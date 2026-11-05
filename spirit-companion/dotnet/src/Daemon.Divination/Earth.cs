using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using CosineKitty;

namespace Daemon.Divination;

/// <summary>
/// Tide strength from the moon and sun, computed for the whole Earth: spring tides near new and full moon, neap tides near
/// the quarters, king tides when a spring tide meets the moon's perigee. It says how strong the tides are this week,
/// not when high water is at a given beach: local tide times need a tide-station source (<see cref="ITideTimes"/>).
/// </summary>
public static class Tides
{
    private const double MeanMoonKm = 384_400, MeanSunAu = 1.0, KmPerAu = 149_597_870.7;
    /// <summary>The sun's tidal pull relative to the moon's, at mean distances.</summary>
    private const double SunRatio = 0.46;

    /// <summary>Semidiurnal tide-raising strength relative to the moon alone at mean distance (≈0.54 at the weakest neap, ≈1.46 at an average spring, higher at perigee).</summary>
    public static double Strength(AstroTime t)
    {
        double dm = Astronomy.GeoMoon(t).Length() * KmPerAu;
        double ds = Astronomy.GeoVector(Body.Sun, t, Aberration.None).Length();
        double fm = Math.Pow(MeanMoonKm / dm, 3), fs = SunRatio * Math.Pow(MeanSunAu / ds, 3);
        double theta = Astronomy.AngleFromSun(Body.Moon, t) * Math.PI / 180;
        return Math.Sqrt(fm * fm + fs * fs + 2 * fm * fs * Math.Cos(2 * theta));
    }

    public static string Label(double s) => s >= 1.55 ? "king_tide" : s >= 1.3 ? "spring_tide" : s <= 0.75 ? "neap_tide" : "moderate";

    public static JsonObject Now(DateTimeOffset now)
    {
        var t = new AstroTime(now.UtcDateTime);
        // tides lag the sky by about a day or two: report the strength over the coming days too
        var days = new JsonArray();
        double peak = 0; int peakDay = 0;
        for (int d = 0; d <= 7; d++)
        {
            double s = Strength(t.AddDays(d));
            if (s > peak) { peak = s; peakDay = d; }
            if (d <= 3) days.Add(Math.Round(s, 2));
        }
        double now0 = Strength(t);
        return new JsonObject
        {
            ["strength"] = Math.Round(now0, 2),
            ["label"] = Label(now0),
            ["next_days"] = days,
            ["strongest_this_week_in_days"] = peakDay,
            ["strongest_label"] = Label(peak),
            ["note"] = "global tide strength from moon and sun; real tides peak a day or two later and local times depend on the coast",
        };
    }
}

/// <summary>Local high and low tide times for a coast. Needs tide-station data (not built; see docs/design-sky-context.md).</summary>
public interface ITideTimes
{
    Task<JsonArray?> NextAsync(double latitude, double longitude, DateTimeOffset from, CancellationToken ct = default);
}

/// <summary>The weather where the user is, reduced to what readings use.</summary>
public sealed record WeatherNow(DateTimeOffset At, double TempC, double WindMs, double CloudPct, double PrecipMm, string Symbol, double? Humidity);

public interface IWeatherProvider
{
    Task<WeatherNow?> NowAsync(double latitude, double longitude, CancellationToken ct = default);
}

/// <summary>
/// MET Norway's Locationforecast 2.0 (free, global, data under CC BY 4.0: attribution required). Their terms require an
/// identifying User-Agent with contact details, at most 4 decimals in coordinates, and caching; this client caches each
/// place for 30 minutes.
/// </summary>
public sealed class MetNorwayWeather(HttpClient http, string userAgent) : IWeatherProvider
{
    public const string Attribution = "Weather data from MET Norway (CC BY 4.0)";
    private readonly Dictionary<string, (DateTimeOffset Until, WeatherNow? W)> _cache = new();

    public async Task<WeatherNow?> NowAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        string key = FormattableString.Invariant($"{latitude:F4},{longitude:F4}");
        lock (_cache) if (_cache.TryGetValue(key, out var c) && c.Until > DateTimeOffset.UtcNow) return c.W;
        using var req = new HttpRequestMessage(HttpMethod.Get, FormattableString.Invariant($"https://api.met.no/weatherapi/locationforecast/2.0/compact?lat={latitude:F4}&lon={longitude:F4}"));
        req.Headers.UserAgent.Add(ProductInfoHeaderValue.Parse(userAgent));
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) return null;
        var w = Parse(await res.Content.ReadAsStringAsync(ct));
        lock (_cache) _cache[key] = (DateTimeOffset.UtcNow.AddMinutes(30), w);
        return w;
    }

    public static WeatherNow? Parse(string json)
    {
        var ts = JsonNode.Parse(json)?["properties"]?["timeseries"]?.AsArray();
        if (ts is null || ts.Count == 0) return null;
        var first = ts[0]!;
        var d = first["data"]!["instant"]!["details"]!;
        var next = first["data"]!["next_1_hours"] ?? first["data"]!["next_6_hours"];
        return new WeatherNow(
            DateTimeOffset.Parse(Data.S(first["time"]), CultureInfo.InvariantCulture),
            d["air_temperature"]?.GetValue<double>() ?? double.NaN,
            d["wind_speed"]?.GetValue<double>() ?? 0,
            d["cloud_area_fraction"]?.GetValue<double>() ?? 0,
            next?["details"]?["precipitation_amount"]?.GetValue<double>() ?? 0,
            Data.S(next?["summary"]?["symbol_code"]),
            d["relative_humidity"]?.GetValue<double>());
    }
}

/// <summary>Turns weather into omens: which conditions hold and which elements they bring.</summary>
public static class WeatherOmens
{
    public static JsonObject Read(WeatherNow w)
    {
        var conditions = new List<string>();
        var elements = new Dictionary<string, int> { ["fire"] = 0, ["water"] = 0, ["earth"] = 0, ["air"] = 0 };
        string s = w.Symbol;
        void Add(string c, params string[] els) { if (!conditions.Contains(c)) conditions.Add(c); foreach (var e in els) elements[e]++; }
        if (s.Contains("thunder")) Add("weather_storm", "fire", "air", "water");
        else if (s.Contains("snow")) Add("weather_snow", "earth", "water");
        else if (s.Contains("rain") || s.Contains("sleet") || w.PrecipMm >= 0.2) Add("weather_rain", "water");
        else if (s.Contains("fog")) Add("weather_fog", "water");
        else if (s.StartsWith("clearsky") || s.StartsWith("fair")) Add("weather_clear", "fire");
        else if (s.Contains("cloudy")) Add("weather_cloud", "air");
        if (w.WindMs >= 10) Add("weather_wind", "air");
        if (w.TempC >= 30) Add("weather_heat", "fire");
        if (w.TempC <= 5) Add("weather_cold", "earth");
        return new JsonObject
        {
            ["temperature_c"] = Math.Round(w.TempC, 1),
            ["wind_ms"] = Math.Round(w.WindMs, 1),
            ["cloud_pct"] = Math.Round(w.CloudPct),
            ["precipitation_mm"] = w.PrecipMm,
            ["symbol"] = s,
            ["conditions"] = new JsonArray(conditions.Select(c => (JsonNode)c).ToArray()),
            ["elements"] = new JsonObject(elements.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
            ["attribution"] = MetNorwayWeather.Attribution,
        };
    }
}
