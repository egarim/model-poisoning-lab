using System.Globalization;
using System.Text.Json.Nodes;
using CosineKitty;

namespace Daemon.Divination;

/// <summary>Playful readings of historical moments: the real sky at that moment and the deck's card for it.</summary>
public static class History
{
    public static IEnumerable<JsonObject> Events => Data.Get("history")["events"]!.AsArray().Select(e => e!.AsObject());

    /// <summary>Anniversaries on this day and in the coming days (by month and day).</summary>
    public static JsonArray OnThisDay(DateOnly today, int windowDays = 7)
    {
        var list = new List<(int In, JsonObject E)>();
        foreach (var e in Events)
        {
            var d = DateOnly.Parse(Data.S(e["date"]), CultureInfo.InvariantCulture);
            for (int y = today.Year; y <= today.Year + 1; y++)
            {
                if (d.Month == 2 && d.Day == 29 && !DateTime.IsLeapYear(y)) continue;
                int inDays = new DateOnly(y, d.Month, d.Day).DayNumber - today.DayNumber;
                if (inDays >= 0 && inDays <= windowDays) { list.Add((inDays, e)); break; }
            }
        }
        return new JsonArray(list.OrderBy(x => x.In).Select(x => (JsonNode)new JsonObject
        {
            ["id"] = Data.S(x.E["id"]), ["title"] = Data.S(x.E["title"]), ["date"] = Data.S(x.E["date"]),
            ["in_days"] = x.In, ["years_ago"] = today.Year - int.Parse(Data.S(x.E["date"])[..4], CultureInfo.InvariantCulture),
        }).ToArray());
    }

    public static JsonObject Reading(string eventId)
    {
        var e = Events.FirstOrDefault(x => Data.S(x["id"]) == eventId) ?? throw new ArgumentException($"unknown event '{eventId}'");
        var date = DateOnly.Parse(Data.S(e["date"]), CultureInfo.InvariantCulture);
        string? time = e["time_utc"]?.GetValue<string>();
        var at = date.ToDateTime(time is null ? new TimeOnly(12, 0) : TimeOnly.Parse(time, CultureInfo.InvariantCulture), DateTimeKind.Utc);
        var t = new AstroTime(at);
        double angle = Astronomy.MoonPhase(t);
        var phase = Data.Get("calendars")["moon_phases"]![Sky.PhaseIndex(angle)]!.AsArray();
        var card = Tarot.Draw(new DrawRng("history|" + eventId), "single")["cards"]![0]!.DeepClone();
        return new JsonObject
        {
            ["method"] = "history_reading",
            ["event"] = e.DeepClone(),
            ["sky_then"] = new JsonObject
            {
                ["moon_phase"] = Data.S(phase[0]),
                ["moon_sign"] = Sky.SignOf(Sky.Longitude(Body.Moon, t)) + (time is null ? " (approximate: time of day unknown)" : ""),
                ["sun_sign"] = Sky.SignOf(Sky.Longitude(Body.Sun, t)),
                ["retrograde"] = new JsonArray(new[] { Body.Mercury, Body.Venus, Body.Mars, Body.Jupiter, Body.Saturn }.Where(b => Sky.IsRetrograde(b, t)).Select(b => (JsonNode)b.ToString()).ToArray()),
            },
            ["the_decks_card"] = card,
            ["frame"] = "playful: the real sky at that moment and the card the deck gives this event (the same for everyone); never a claim that the stars caused it",
        };
    }
}

/// <summary>
/// Everything about the user's day, in one object the daemon reads before a reading: festivities, moon and its specials,
/// eclipses, retrogrades, tides, weather, today's elements, a suggested spread, and plain explanations with examples.
/// </summary>
public static class DayContext
{
    private static readonly Dictionary<string, string> SignElement = new()
    {
        ["Aries"] = "fire", ["Leo"] = "fire", ["Sagittarius"] = "fire", ["Taurus"] = "earth", ["Virgo"] = "earth", ["Capricorn"] = "earth",
        ["Gemini"] = "air", ["Libra"] = "air", ["Aquarius"] = "air", ["Cancer"] = "water", ["Scorpio"] = "water", ["Pisces"] = "water",
    };
    private static readonly Dictionary<string, string> PhaseConcept = new()
    {
        ["New Moon"] = "new_moon", ["Waxing Crescent"] = "waxing_crescent", ["First Quarter"] = "first_quarter", ["Waxing Gibbous"] = "waxing_gibbous",
        ["Full Moon"] = "full_moon", ["Waning Gibbous"] = "waning_gibbous", ["Last Quarter"] = "last_quarter", ["Waning Crescent"] = "waning_crescent",
    };

    public static JsonObject Build(DateTimeOffset now, UserContext u)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(u.TimeZoneId);
        var today = Seeds.LocalDate(now, u.TimeZoneId);
        bool south = Festivities.IsSouthern(u.Latitude ?? u.BirthLatitude, u.Country);
        var concepts = new List<string>();
        var elements = new Dictionary<string, int> { ["fire"] = 0, ["water"] = 0, ["earth"] = 0, ["air"] = 0 };
        string? spread = null, spreadWhy = null;

        // festivities
        var fests = Festivities.Between(today, today.AddDays(21), u.Country, south, tz);
        var todays = fests.Where(f => f.Covers(today)).ToList();
        foreach (var f in todays)
        {
            if (f.Data["element"] is { } el) elements[Data.S(el)]++;
            if (spread is null && f.Data["spread"] is { } sp) { spread = Data.S(sp); spreadWhy = f.Name; }
            if (f.Kind == "seasonal") concepts.Add(f.Id.Contains("solstice") ? "solstice" : f.Id.Contains("equinox") ? "equinox" : "cross_quarter");
        }

        // moon
        var sky = Sky.Now(now, u.TimeZoneId, u.Latitude, u.Longitude);
        var moon = sky["moon"]!.AsObject();
        string phase = Data.S(moon["phase"]);
        concepts.Add(PhaseConcept[phase]);
        elements[SignElement[Data.S(moon["sign"]!["sign"])]]++;
        elements[SignElement[Data.S(sky["sun"]!["sign"])]]++;
        var nearPhases = MoonEvents.Phases(now.AddDays(-1.5), now.AddDays(1.5), tz);
        foreach (var p in nearPhases)
        {
            foreach (var s in p["specials"]?.AsArray() ?? []) concepts.Add(Data.S(s));
            if (spread is null) { spread = Data.S(p["phase"]) == "New Moon" ? "new_moon" : "full_moon"; spreadWhy = Data.S(p["phase"]); }
        }
        var eclipses = MoonEvents.Eclipses(now.AddDays(-3), now.AddDays(30), tz);
        foreach (var e in eclipses.Where(e => Math.Abs((DateTime.Parse(Data.S(e["peak_local"]), CultureInfo.InvariantCulture) - TimeZoneInfo.ConvertTime(now, tz).DateTime).TotalDays) <= 3))
            concepts.Add(Data.S(e["eclipse"]) + "_eclipse");
        if (sky["retrograde"]!.AsArray().Count > 0) concepts.Add("retrograde");

        // tides
        var tide = Tides.Now(now);
        if (Data.S(tide["label"]) != "moderate") concepts.Add(Data.S(tide["label"]));
        if (Data.S(tide["label"]) is "spring_tide" or "king_tide") elements["water"]++;

        // weather
        JsonObject? weather = null;
        if (u.Weather is { } w)
        {
            weather = WeatherOmens.Read(w);
            foreach (var c in weather["conditions"]!.AsArray()) concepts.Add(Data.S(c));
            foreach (var (k, v) in weather["elements"]!.AsObject()) elements[k] += v!.GetValue<int>();
        }

        var explain = new JsonObject();
        var all = Data.Get("explain")["concepts"]!;
        foreach (var c in concepts.Distinct()) if (all[c] is { } x) explain[c] = x.DeepClone();
        int max = elements.Values.Max();

        return new JsonObject
        {
            ["method"] = "day_context",
            ["local_date"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["weekday"] = today.DayOfWeek.ToString(),
            ["hemisphere"] = south ? "southern" : "northern",
            ["festivities_today"] = new JsonArray(todays.Select(f => (JsonNode)Festivities.ToJson(f, today)).ToArray()),
            ["coming_up"] = new JsonArray(fests.Where(f => !f.Covers(today)).Take(5).Select(f => (JsonNode)Festivities.ToJson(f, today)).ToArray()),
            ["moon"] = moon.DeepClone(),
            ["next_moon_circles"] = MoonEvents.Circles(now, 2, tz),
            ["eclipses_next_30_days"] = new JsonArray(eclipses.Select(e => (JsonNode)e.DeepClone()).ToArray()),
            ["retrograde"] = sky["retrograde"]!.DeepClone(),
            ["where_you_are"] = sky["where_you_are"]?.DeepClone(),
            ["tides"] = tide,
            ["weather"] = weather,
            ["elements_of_the_day"] = new JsonObject(elements.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))),
            ["dominant_element"] = string.Join(", ", elements.Where(kv => kv.Value == max && max > 0).Select(kv => kv.Key)),
            ["suggested_spread"] = spread is null ? null : new JsonObject { ["spread"] = spread, ["because"] = spreadWhy },
            ["on_this_day"] = History.OnThisDay(today, 3),
            ["explain"] = explain,
            ["how_to_use"] = "Weave one or two of these into the reading, never all. Explain a concept only if the user seems curious, using its example. Name the tide only if the user lives near the sea.",
        };
    }
}
