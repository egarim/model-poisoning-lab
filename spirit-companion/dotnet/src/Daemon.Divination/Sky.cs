using System.Globalization;
using System.Text.Json.Nodes;
using CosineKitty;

namespace Daemon.Divination;

/// <summary>
/// Astrology with real astronomy (Astronomy Engine, MIT; accurate to about an arcminute): the moon's phase and sign,
/// the next lunar quarters, rise and set times where the user is, which planets are retrograde, and birth charts
/// with the ascendant and whole-sign houses. Tropical zodiac.
/// </summary>
public static class Sky
{
    public static readonly string[] Signs = ["Aries", "Taurus", "Gemini", "Cancer", "Leo", "Virgo", "Libra", "Scorpio", "Sagittarius", "Capricorn", "Aquarius", "Pisces"];
    public static readonly Body[] ChartBodies = [Body.Sun, Body.Moon, Body.Mercury, Body.Venus, Body.Mars, Body.Jupiter, Body.Saturn, Body.Uranus, Body.Neptune, Body.Pluto];
    private static readonly Body[] Retrogradable = [Body.Mercury, Body.Venus, Body.Mars, Body.Jupiter, Body.Saturn, Body.Uranus, Body.Neptune, Body.Pluto];
    private static readonly string[] QuarterNames = ["New Moon", "First Quarter", "Full Moon", "Last Quarter"];

    public static double Norm(double deg) => ((deg % 360) + 360) % 360;

    public static string SignOf(double longitude) => Signs[(int)Math.Floor(Norm(longitude) / 30) % 12];

    /// <summary>Geocentric ecliptic longitude, true ecliptic and equinox of date.</summary>
    public static double Longitude(Body body, AstroTime t) => body switch
    {
        Body.Moon => Astronomy.EclipticGeoMoon(t).lon,
        Body.Sun => Astronomy.SunPosition(t).elon,
        _ => Astronomy.EquatorialToEcliptic(Astronomy.GeoVector(body, t, Aberration.Corrected)).elon,
    };

    /// <summary>True when the body's longitude is decreasing at this moment, as seen from Earth.</summary>
    public static bool IsRetrograde(Body body, AstroTime t)
    {
        if (body is Body.Sun or Body.Moon) return false;
        double delta = Norm(Longitude(body, t.AddDays(0.5)) - Longitude(body, t.AddDays(-0.5)) + 180) - 180;
        return delta < 0;
    }

    private static double MeanObliquity(AstroTime t) => 23.4392911 - 0.0130041667 * (t.tt / 36525.0);

    private static double Ramc(AstroTime t, double longitudeDeg) => Norm((Astronomy.SiderealTime(t) + longitudeDeg / 15.0) * 15.0);

    /// <summary>The ascendant: the ecliptic degree rising on the eastern horizon.</summary>
    public static double Ascendant(AstroTime t, double latitude, double longitude)
    {
        double ramc = Ramc(t, longitude) * Math.PI / 180, eps = MeanObliquity(t) * Math.PI / 180, phi = latitude * Math.PI / 180;
        return Norm(Math.Atan2(Math.Cos(ramc), -(Math.Sin(ramc) * Math.Cos(eps) + Math.Tan(phi) * Math.Sin(eps))) * 180 / Math.PI);
    }

    /// <summary>The midheaven: the ecliptic degree on the meridian.</summary>
    public static double Midheaven(AstroTime t, double longitude)
    {
        double ramc = Ramc(t, longitude) * Math.PI / 180, eps = MeanObliquity(t) * Math.PI / 180;
        return Norm(Math.Atan2(Math.Sin(ramc), Math.Cos(ramc) * Math.Cos(eps)) * 180 / Math.PI);
    }

    /// <summary>Right ascension (hours) and declination (degrees) of an ecliptic point at latitude 0, equator of date.</summary>
    public static (double RaHours, double DecDeg) EclipticToEquator(double lonDeg, AstroTime t)
    {
        double l = lonDeg * Math.PI / 180, eps = MeanObliquity(t) * Math.PI / 180;
        double ra = Math.Atan2(Math.Sin(l) * Math.Cos(eps), Math.Cos(l));
        double dec = Math.Asin(Math.Sin(eps) * Math.Sin(l));
        return (Norm(ra * 180 / Math.PI) / 15.0, dec * 180 / Math.PI);
    }

    private static string Local(AstroTime t, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.ToUtcDateTime(), DateTimeKind.Utc), tz).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static JsonObject Placement(double lon) => new()
    {
        ["sign"] = SignOf(lon),
        ["degree"] = Math.Round(Norm(lon) % 30, 1),
        ["mood"] = Data.S(Data.Get("calendars")["zodiac"]![(int)(Norm(lon) / 30) % 12]![1]),
    };

    /// <summary>The sky now, for this user: moon, upcoming quarters, retrogrades, and rise/set times if a location is given.</summary>
    public static JsonObject Now(DateTimeOffset now, string timeZoneId, double? lat = null, double? lon = null, DateOnly? birth = null)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var t = new AstroTime(now.UtcDateTime);
        double angle = Astronomy.MoonPhase(t);
        var phases = Data.Get("calendars")["moon_phases"]!.AsArray();
        var phase = phases[(int)Math.Floor(Norm(angle + 22.5) / 45) % 8]!.AsArray();
        var o = new JsonObject
        {
            ["method"] = "sky",
            ["local_time"] = TimeZoneInfo.ConvertTime(now, tz).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            ["moon"] = new JsonObject
            {
                ["phase"] = Data.S(phase[0]),
                ["phase_meaning"] = Data.S(phase[1]),
                ["illumination_pct"] = Math.Round(Astronomy.Illumination(Body.Moon, t).phase_fraction * 100),
                ["sign"] = Placement(Longitude(Body.Moon, t)),
            },
            ["sun"] = Placement(Longitude(Body.Sun, t)),
        };
        var quarters = new JsonArray();
        var mq = Astronomy.SearchMoonQuarter(t);
        for (int i = 0; i < 4; i++) { quarters.Add(new JsonObject { ["phase"] = QuarterNames[mq.quarter], ["local_time"] = Local(mq.time, tz) }); mq = Astronomy.NextMoonQuarter(mq); }
        o["next_moon_phases"] = quarters;
        o["retrograde"] = new JsonArray(Retrogradable.Where(b => IsRetrograde(b, t)).Select(b => (JsonNode)b.ToString()).ToArray());
        if (lat is { } la && lon is { } lo)
        {
            var obs = new Observer(la, lo, 0);
            string? Next(Body b, Direction d) => Astronomy.SearchRiseSet(b, obs, d, t, 1.0) is { } at ? Local(at, tz) : null;
            o["where_you_are"] = new JsonObject
            {
                ["moonrise"] = Next(Body.Moon, Direction.Rise), ["moonset"] = Next(Body.Moon, Direction.Set),
                ["sunrise"] = Next(Body.Sun, Direction.Rise), ["sunset"] = Next(Body.Sun, Direction.Set),
            };
        }
        if (birth is { } bd) o["your_sun_sign"] = SignOf(Longitude(Body.Sun, new AstroTime(bd.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc))));
        return o;
    }

    /// <summary>
    /// A birth chart. Without a birth time, positions use local noon and the moon may be off by up to ~7°; the ascendant
    /// and houses need both the time and the place of birth.
    /// </summary>
    public static JsonObject BirthChart(DateOnly date, TimeOnly? time, string timeZoneId, double? lat = null, double? lon = null)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = date.ToDateTime(time ?? new TimeOnly(12, 0), DateTimeKind.Unspecified);
        var t = new AstroTime(TimeZoneInfo.ConvertTimeToUtc(local, tz));
        bool exact = time is not null && lat is not null && lon is not null;
        double? asc = exact ? Ascendant(t, lat!.Value, lon!.Value) : null;
        var planets = new JsonArray();
        foreach (var b in ChartBodies)
        {
            double l = Longitude(b, t);
            var p = Placement(l);
            p["body"] = b.ToString();
            p["retrograde"] = IsRetrograde(b, t);
            if (asc is { } a) p["house"] = (int)(Norm(Math.Floor(l / 30) * 30 - Math.Floor(a / 30) * 30) / 30) + 1;
            planets.Add(p);
        }
        var o = new JsonObject
        {
            ["method"] = "birth_chart",
            ["born_local"] = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + (time is null ? " (time unknown, noon used)" : ""),
            ["planets"] = planets,
        };
        if (asc is { } ascv)
        {
            o["ascendant"] = Placement(ascv);
            o["midheaven"] = Placement(Midheaven(t, lon!.Value));
            o["houses"] = "whole sign: the 1st house is the ascendant's sign";
        }
        else o["note"] = "add birth time and place for the ascendant and houses";
        return o;
    }
}

/// <summary>The Maya 260-day calendar (Tzolk'in), GMT correlation 584283.</summary>
public static class Maya
{
    public static JsonObject DaySign(DateOnly date)
    {
        var c = Data.Get("calendars")["tzolkin"]!;
        long jdn = date.DayNumber + 1721426L; // DateOnly.DayNumber is days since 0001-01-01 (proleptic Gregorian)
        long days = jdn - 584283;
        int tone = (int)(((days + 3) % 13 + 13) % 13) + 1;
        var sign = c["signs"]![(int)(((days + 19) % 20 + 20) % 20)]!.AsArray();
        return new JsonObject
        {
            ["method"] = "tzolkin",
            ["day"] = $"{tone} {Data.S(sign[0])}",
            ["sign"] = new JsonObject { ["name"] = Data.S(sign[0]), ["glyph"] = Data.S(sign[1]), ["meaning"] = Data.S(sign[2]) },
            ["tone"] = new JsonObject { ["number"] = tone, ["meaning"] = Data.S(c["tones"]![tone.ToString(CultureInfo.InvariantCulture)]) },
            ["note"] = Data.S(c["_note"]),
        };
    }
}
