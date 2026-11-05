using System.Globalization;
using System.Text.Json.Nodes;
using CosineKitty;

namespace Daemon.Divination;

/// <summary>A festivity on the user's calendar.</summary>
public sealed record Festivity(string Id, string Name, DateOnly Start, int Days, string Kind, JsonObject Data)
{
    public DateOnly End => Start.AddDays(Days - 1);
    public bool Covers(DateOnly d) => d >= Start && d <= End;
}

/// <summary>
/// Festivities: fixed dates, Easter-based feasts (Western and Orthodox), Lunar New Year, and the solar year (solstices,
/// equinoxes, cross-quarter days) computed from the real sun and named for the user's hemisphere.
/// </summary>
public static class Festivities
{
    private static readonly HashSet<string> Southern = ["BR", "AR", "CL", "PE"];

    /// <summary>Southern hemisphere from the latitude when known, else from the country.</summary>
    public static bool IsSouthern(double? latitude, string? country) => latitude is { } l ? l < 0 : country is not null && Southern.Contains(country);

    /// <summary>Western (Gregorian) Easter Sunday: the anonymous Gregorian algorithm.</summary>
    public static DateOnly WesternEaster(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451, month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(y, month, day);
    }

    /// <summary>Orthodox Easter Sunday, in the Gregorian calendar (Meeus' Julian algorithm + 13 days; valid 1900–2099).</summary>
    public static DateOnly OrthodoxEaster(int y)
    {
        int a = y % 4, b = y % 7, c = y % 19, d = (19 * c + 15) % 30, e = (2 * a + 4 * b - d + 34) % 7;
        int month = (d + e + 114) / 31, day = (d + e + 114) % 31 + 1;
        return new DateOnly(y, month, day).AddDays(13);
    }

    /// <summary>Lunar New Year: the first new moon on or after 21 January, China time (correct for nearly all years).</summary>
    public static DateOnly ChineseNewYear(int y)
    {
        var start = new AstroTime(new DateTime(y, 1, 20, 16, 0, 0, DateTimeKind.Utc)); // 21 Jan 00:00 UTC+8
        var mq = Astronomy.SearchMoonQuarter(start);
        while (mq.quarter != 0) mq = Astronomy.NextMoonQuarter(mq);
        return DateOnly.FromDateTime(mq.time.ToUtcDateTime().AddHours(8));
    }

    /// <summary>The local date when the sun reaches an ecliptic longitude (0 = March equinox, 90 = June solstice…).</summary>
    public static (DateOnly Date, AstroTime Moment) SunAt(int year, double longitude, TimeZoneInfo tz)
    {
        var t = Astronomy.SearchSunLongitude(longitude, new AstroTime(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc)), 366)
            ?? throw new InvalidOperationException("sun longitude not found");
        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.ToUtcDateTime(), DateTimeKind.Utc), tz)), t);
    }

    /// <summary>All festivities overlapping [from, to] for this country and hemisphere, in date order.</summary>
    public static List<Festivity> Between(DateOnly from, DateOnly to, string? country, bool southern, TimeZoneInfo tz)
    {
        var list = new List<Festivity>();
        foreach (var node in Data.Get("festivals")["festivals"]!.AsArray())
        {
            var f = node!.AsObject();
            var countries = f["countries"]!.AsArray().Select(Data.S).ToList();
            if (!countries.Contains("*") && (country is null || !countries.Contains(country))) continue;
            int days = f["days"]?.GetValue<int>() ?? 1;
            var when = f["when"]!;
            for (int y = from.Year - 1; y <= to.Year; y++)
            {
                DateOnly start;
                string? moment = null;
                if (when["fixed"] is { } fx)
                {
                    var p = Data.S(fx).Split('-');
                    start = new DateOnly(y, int.Parse(p[0], CultureInfo.InvariantCulture), int.Parse(p[1], CultureInfo.InvariantCulture));
                }
                else
                {
                    string rule = Data.S(when["rule"]);
                    int offset = when["offset"]?.GetValue<int>() ?? 0;
                    if (rule == "easter") start = WesternEaster(y).AddDays(offset);
                    else if (rule == "orthodox_easter") start = OrthodoxEaster(y).AddDays(offset);
                    else if (rule == "chinese_new_year") start = ChineseNewYear(y);
                    else if (rule == "sun_longitude")
                    {
                        var (d, t) = SunAt(y, when["deg"]!.GetValue<double>(), tz);
                        start = d;
                        moment = Sky.Minute(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.ToUtcDateTime(), DateTimeKind.Utc), tz));
                    }
                    else continue;
                }
                var fest = new Festivity(Data.S(f["id"]), southern && f["name_south"] is { } ns ? Data.S(ns) : Data.S(f["name"]), start, days, Data.S(f["kind"]), f);
                if (fest.End < from || fest.Start > to) continue;
                var data = (JsonObject)f.DeepClone();
                if (moment is not null) data["exact_local_time"] = moment;
                list.Add(fest with { Data = data });
            }
        }
        return list.OrderBy(x => x.Start).ThenBy(x => x.Id).ToList();
    }

    public static JsonObject ToJson(Festivity f, DateOnly today)
    {
        var o = new JsonObject
        {
            ["id"] = f.Id,
            ["name"] = f.Name,
            ["starts"] = f.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["days"] = f.Days,
            ["in_days"] = Math.Max(0, f.Start.DayNumber - today.DayNumber),
            ["kind"] = f.Kind,
            ["explain"] = f.Data["explain"]?.DeepClone(),
            ["ritual"] = f.Data["ritual"]?.DeepClone(),
        };
        foreach (var k in new[] { "local", "spread", "element", "exact_local_time" })
            if (f.Data[k] is { } v) o[k] = v.DeepClone();
        if (f.Kind == "religious") o["respect"] = "a living religion: acknowledge it respectfully; never role-play it or invent ceremonies for it";
        return o;
    }
}

/// <summary>Moon events: phases with folk names, supermoons, blue moons, eclipses, and the circles (gatherings) at new and full moon.</summary>
public static class MoonEvents
{
    private const double KmPerAu = 149_597_870.7;
    /// <summary>Distance below which a new or full moon is called a supermoon (a common popular threshold).</summary>
    public const double SupermoonKm = 362_000;
    private static readonly string[] FullMoonNames = ["Wolf Moon", "Snow Moon", "Worm Moon", "Pink Moon", "Flower Moon", "Strawberry Moon", "Buck Moon", "Sturgeon Moon", "Corn Moon", "Hunter's Moon", "Beaver Moon", "Cold Moon"];

    public static double MoonDistanceKm(AstroTime t) => Astronomy.GeoMoon(t).Length() * KmPerAu;

    /// <summary>New and full moons (and quarters if asked) between two instants, with their specials.</summary>
    public static List<JsonObject> Phases(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo tz, bool quarters = false)
    {
        var list = new List<JsonObject>();
        var mq = Astronomy.SearchMoonQuarter(new AstroTime(from.UtcDateTime));
        var end = to.UtcDateTime;
        var fullByMonth = new Dictionary<(int, int), int>();
        // count earlier full moons in the first month, for blue moons
        var probe = Astronomy.SearchMoonQuarter(new AstroTime(new DateTime(from.Year, from.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-2)));
        while (probe.time.ut < mq.time.ut - 0.5) // the same event found by two searches can differ by a fraction of a second
        {
            if (probe.quarter == 2) { var l = Local(probe.time, tz); fullByMonth[(l.Year, l.Month)] = fullByMonth.GetValueOrDefault((l.Year, l.Month)) + 1; }
            probe = Astronomy.NextMoonQuarter(probe);
        }
        while (mq.time.ToUtcDateTime() <= end)
        {
            if (quarters || mq.quarter is 0 or 2)
            {
                var local = Local(mq.time, tz);
                double km = MoonDistanceKm(mq.time);
                var o = new JsonObject
                {
                    ["phase"] = mq.quarter switch { 0 => "New Moon", 1 => "First Quarter", 2 => "Full Moon", _ => "Last Quarter" },
                    ["local_time"] = Sky.Minute(local),
                    ["sign"] = Sky.SignOf(Sky.Longitude(Body.Moon, mq.time)),
                    ["distance_km"] = Math.Round(km),
                };
                var specials = new JsonArray();
                if (mq.quarter is 0 or 2 && km < SupermoonKm) specials.Add("supermoon");
                if (mq.quarter == 2)
                {
                    int n = fullByMonth.GetValueOrDefault((local.Year, local.Month)) + 1;
                    fullByMonth[(local.Year, local.Month)] = n;
                    if (n == 2) specials.Add("blue_moon");
                    o["folk_name"] = FullMoonName(mq.time, local);
                }
                if (specials.Count > 0) o["specials"] = specials;
                list.Add(o);
            }
            mq = Astronomy.NextMoonQuarter(mq);
        }
        return list;
    }

    private static DateTime Local(AstroTime t, TimeZoneInfo tz) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(t.ToUtcDateTime(), DateTimeKind.Utc), tz);

    /// <summary>Folk name (North American tradition); the full moon nearest the September equinox is the Harvest Moon.</summary>
    public static string FullMoonName(AstroTime t, DateTime local)
    {
        var eq = Astronomy.SearchSunLongitude(180, new AstroTime(new DateTime(local.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc)), 366)!;
        double gap = Math.Abs(t.ut - eq.ut);
        if (gap <= 14.8) return "Harvest Moon";
        return FullMoonNames[local.Month - 1];
    }

    /// <summary>Lunar and solar eclipses between two instants (solar: anywhere on Earth).</summary>
    public static List<JsonObject> Eclipses(DateTimeOffset from, DateTimeOffset to, TimeZoneInfo tz)
    {
        var list = new List<JsonObject>();
        var start = new AstroTime(from.UtcDateTime);
        for (var le = Astronomy.SearchLunarEclipse(start); le.peak.ToUtcDateTime() <= to.UtcDateTime; le = Astronomy.NextLunarEclipse(le.peak))
            list.Add(new JsonObject { ["eclipse"] = "lunar", ["kind"] = le.kind.ToString().ToLowerInvariant(), ["peak_local"] = Sky.Minute(Local(le.peak, tz)) });
        for (var se = Astronomy.SearchGlobalSolarEclipse(start); se.peak.ToUtcDateTime() <= to.UtcDateTime; se = Astronomy.NextGlobalSolarEclipse(se.peak))
            list.Add(new JsonObject
            {
                ["eclipse"] = "solar", ["kind"] = se.kind.ToString().ToLowerInvariant(), ["peak_local"] = Sky.Minute(Local(se.peak, tz)),
                ["visible_near"] = se.kind is EclipseKind.Total or EclipseKind.Annular ? $"{se.latitude:F1}, {se.longitude:F1}" : null,
            });
        return list.OrderBy(e => Data.S(e["peak_local"])).ToList();
    }

    /// <summary>The next moon circles: new moon (intentions) and full moon (release), each with its theme and spread.</summary>
    public static JsonArray Circles(DateTimeOffset from, int count, TimeZoneInfo tz)
    {
        var arr = new JsonArray();
        foreach (var p in Phases(from, from.AddDays(31 * ((count + 1) / 2 + 1)), tz).Take(count))
        {
            bool isNew = Data.S(p["phase"]) == "New Moon";
            var c = (JsonObject)p.DeepClone();
            c["circle"] = isNew ? "intentions: what to begin" : "release: what to let go, what to be grateful for";
            c["spread"] = isNew ? "new_moon" : "full_moon";
            arr.Add(c);
        }
        return arr;
    }
}
