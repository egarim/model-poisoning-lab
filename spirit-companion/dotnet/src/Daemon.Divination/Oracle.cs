using System.Text.Json.Nodes;

namespace Daemon.Divination;

/// <summary>What the server knows about the person asking. The model never fills these in.</summary>
public sealed record UserContext(string UserId, string TimeZoneId = "UTC")
{
    public string? Name { get; init; }
    public DateOnly? BirthDate { get; init; }
    public TimeOnly? BirthTime { get; init; }
    public string? BirthTimeZoneId { get; init; }
    public double? BirthLatitude { get; init; }
    public double? BirthLongitude { get; init; }
    /// <summary>Where the user is now, only when they shared it.</summary>
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    /// <summary>ISO country code (MX, BR, AR, CO, PE, CL, RU…) for festivities and hemisphere.</summary>
    public string? Country { get; init; }
    /// <summary>The language the person writes in (es, pt, en, ru): cards and runes are shown to them by their names in it.</summary>
    public string? Language { get; init; }
    /// <summary>Where the user's roots are, when different from where they live (e.g. lives in RU, roots in SV): festivities and a second clock.</summary>
    public string? RootsCountry { get; init; }
    public string? RootsTimeZoneId { get; init; }
    /// <summary>Current weather where the user is, fetched by the server (see <see cref="IWeatherProvider"/>), if they shared a location.</summary>
    public WeatherNow? Weather { get; init; }
    /// <summary>The schools this user has unlocked; null means all (tests, staff). The "calendar" school is always open.</summary>
    public IReadOnlySet<string>? Schools { get; init; }
}

/// <summary>
/// Proof of a draw: the method, its arguments, the seed and a hash of the exact result. Re-running the method with the
/// same seed must give the same hash (<see cref="Oracle.Verify"/>), so a card the daemon names can be checked against
/// what was really drawn. Computed methods (numerology, sky, charts, Maya) have no seed; their receipt pins the inputs.
/// </summary>
public sealed record DrawReceipt(string Id, string Method, string Seed, string SeedKind, DateTimeOffset At, string Args, string ResultHash);

public sealed record Reading(string Method, string School, JsonObject Result, DrawReceipt? Receipt)
{
    public bool Locked => Receipt is null;

    /// <summary>What goes back to the model as the tool's answer.</summary>
    public string ToolOutput() => Canonical.Json(Result);
}

/// <summary>Runs a divination tool for a user: checks the school is unlocked, picks the seed, draws, and writes a receipt.</summary>
public sealed class Oracle(byte[] dailySecret, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public static readonly IReadOnlyDictionary<string, string> SchoolOf = new Dictionary<string, string>
    {
        ["draw_tarot"] = "tarot",
        ["draw_lenormand"] = "lenormand",
        ["cast_runes"] = "runes",
        ["cast_i_ching"] = "iching",
        ["personal_numerology"] = "numerology",
        ["sky_now"] = "astrology",
        ["birth_chart"] = "astrology",
        ["maya_day_sign"] = "maya",
        ["day_context"] = "calendar",
        ["moon_circles"] = "calendar",
        ["history_reading"] = "calendar",
    };

    private static readonly HashSet<string> Random = ["draw_tarot", "draw_lenormand", "cast_runes", "cast_i_ching"];

    public Reading Run(string tool, JsonObject? args, UserContext user)
    {
        args ??= new JsonObject();
        if (!SchoolOf.TryGetValue(tool, out var school)) throw new ArgumentException($"unknown tool '{tool}'");
        if (user.Schools is { } open && school != "calendar" && !open.Contains(school))
            return new Reading(tool, school, new JsonObject
            {
                ["locked"] = true,
                ["school"] = school,
                ["note"] = "this school is not unlocked for this user; say so in character and offer what you can read instead",
            }, null);

        var now = _clock.GetUtcNow();
        string seed = "", kind = "computed";
        if (Random.Contains(tool))
        {
            bool daily = args["daily"]?.GetValue<bool>() ?? false;
            seed = daily ? Seeds.Daily(dailySecret, user.UserId, Seeds.LocalDate(now, user.TimeZoneId), tool) : DrawRng.Fresh().Seed;
            kind = daily ? "daily" : "random";
        }
        var result = Execute(tool, args, user, seed, now);
        var receipt = new DrawReceipt(Guid.NewGuid().ToString("n"), tool, seed, kind, now, Canonical.Json(args), Canonical.Hash(result));
        result["receipt"] = receipt.Id;
        return new Reading(tool, school, result, receipt);
    }

    /// <summary>Re-run a receipt's draw and check it gives exactly the same result.</summary>
    public bool Verify(DrawReceipt receipt, UserContext user)
    {
        var args = JsonNode.Parse(receipt.Args)!.AsObject();
        return Canonical.Hash(Execute(receipt.Method, args, user, receipt.Seed, receipt.At)) == receipt.ResultHash;
    }

    private static string Str(JsonObject a, string key, string fallback) => a[key]?.GetValue<string>() ?? fallback;

    private static JsonObject Execute(string tool, JsonObject a, UserContext u, string seed, DateTimeOffset now)
    {
        var today = Seeds.LocalDate(now, u.TimeZoneId);
        DateOnly Birth() => u.BirthDate ?? throw new InvalidOperationException("this method needs the user's birth date");
        return tool switch
        {
            "draw_tarot" => Tarot.Draw(new DrawRng(seed), Str(a, "spread", "single"), a["reversals"]?.GetValue<bool>() ?? true),
            "draw_lenormand" => Lenormand.Draw(new DrawRng(seed), Str(a, "spread", "three")),
            "cast_runes" => Runes.Cast(new DrawRng(seed), a["count"]?.GetValue<int>() ?? 1),
            "cast_i_ching" => IChing.Cast(new DrawRng(seed), Str(a, "method", "coins")),
            "personal_numerology" => Numerology.Personal(Birth(), today, u.Name),
            "sky_now" => Sky.Now(now, u.TimeZoneId, u.Latitude, u.Longitude, u.BirthDate),
            "birth_chart" => Sky.BirthChart(Birth(), u.BirthTime, u.BirthTimeZoneId ?? u.TimeZoneId, u.BirthLatitude, u.BirthLongitude),
            "maya_day_sign" => Maya.DaySign(today),
            "day_context" => DayContext.Build(now, u),
            "moon_circles" => new JsonObject
            {
                ["method"] = "moon_circles",
                ["circles"] = MoonEvents.Circles(now, a["count"]?.GetValue<int>() ?? 4, TimeZoneInfo.FindSystemTimeZoneById(u.TimeZoneId)),
                ["eclipses_next_year"] = new JsonArray(MoonEvents.Eclipses(now, now.AddDays(365), TimeZoneInfo.FindSystemTimeZoneById(u.TimeZoneId)).Select(e => (JsonNode)e).ToArray()),
            },
            "history_reading" => a["event_id"]?.GetValue<string>() is { } id
                ? History.Reading(id)
                : new JsonObject { ["method"] = "on_this_day", ["events"] = History.OnThisDay(today, a["window_days"]?.GetValue<int>() ?? 7) },
            _ => throw new ArgumentException($"unknown tool '{tool}'"),
        };
    }
}

/// <summary>The tools as OpenAI-compatible function definitions (DeepSeek speaks this format), filtered by unlocked schools.</summary>
public static class ToolCatalog
{
    private static JsonObject Fn(string name, string description, JsonObject? properties = null) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = properties ?? new JsonObject() },
        },
    };

    private static JsonObject Enum(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()) };

    public static JsonArray All() =>
    [
        Fn("draw_tarot", "Shuffle the 78-card tarot and draw a spread. Best for feelings, relationships and complex situations. Each card comes with a reflection question; use it instead of predicting.",
            new JsonObject { ["spread"] = Enum([.. Tarot.Spreads]), ["daily"] = new JsonObject { ["type"] = "boolean", ["description"] = "true for the card of the day (stays the same all day)" } }),
        Fn("draw_lenormand", "Draw Lenormand cards (baralho cigano). Read them in order as one sentence. Best for concrete everyday situations.",
            new JsonObject { ["spread"] = Enum([.. Lenormand.Spreads]) }),
        Fn("cast_runes", "Cast Elder Futhark runes: 1 for a quick sign, 3 for situation, challenge, way forward. Best for feeling blocked, courage, what to do next.",
            new JsonObject { ["count"] = new JsonObject { ["type"] = "integer", ["enum"] = new JsonArray(1, 3) } }),
        Fn("cast_i_ching", "Cast the I Ching. Best for decisions, timing and how a situation is changing. 'coins' is the common method; 'yarrow' is the traditional one with different odds.",
            new JsonObject { ["method"] = Enum("coins", "yarrow") }),
        Fn("personal_numerology", "The user's personal day, month and year numbers and life path, plus name numbers. Best for the energy of today and planning."),
        Fn("sky_now", "The real sky now for this user: moon phase and sign, the next moon phases, which planets are retrograde, and moonrise and sunset where they are if they shared their location. Best for mood, rhythm and timing."),
        Fn("birth_chart", "The user's birth chart: planets by sign, and the ascendant and houses if birth time and place are known. Best for questions about who they are."),
        Fn("maya_day_sign", "Today's day in the Maya 260-day calendar (Tzolk'in). Best for the energy of the day."),
        Fn("day_context", "Everything about the user's day: festivities today and coming up, the moon and its specials (supermoon, blue moon, eclipses), retrogrades, tide strength, the weather where they are, the elements of the day, a suggested spread, and plain explanations with examples. Call it first when doing a reading, then weave one or two details in."),
        Fn("moon_circles", "The next new and full moons as moon circles (intentions and release) with their spreads, plus the eclipses in the coming year.",
            new JsonObject { ["count"] = new JsonObject { ["type"] = "integer", ["description"] = "how many circles, default 4" } }),
        Fn("history_reading", "History for fun: without event_id, the historical anniversaries on this day and the coming days; with event_id, the real sky at that moment and the card the deck gives it. Always playful, never claiming the stars caused history.",
            new JsonObject { ["event_id"] = new JsonObject { ["type"] = "string" }, ["window_days"] = new JsonObject { ["type"] = "integer" } }),
    ];

    public static JsonArray For(IReadOnlySet<string>? schools) =>
        schools is null ? All() : new JsonArray(All().Where(t => Oracle.SchoolOf[t!["function"]!["name"]!.GetValue<string>()] is var s && (s == "calendar" || schools.Contains(s))).Select(t => t!.DeepClone()).ToArray());
}
