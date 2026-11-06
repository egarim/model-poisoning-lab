using System.Globalization;
using System.Text.Json.Nodes;
using Daemon.Divination;

namespace SingularFramework.Apps.LittleSouls.Jobs;

/// <summary>A person's notification preferences (profile.notifications), with the defaults.</summary>
public sealed record Preferences(bool Omen, TimeOnly OmenAt, bool FollowUps, bool MoonCircles, TimeOnly QuietFrom, TimeOnly QuietTo)
{
    public static readonly Preferences Default = new(true, new TimeOnly(7, 30), true, false, new TimeOnly(22, 0), new TimeOnly(8, 0));

    public static Preferences From(JsonObject? profile)
    {
        var n = profile?["notifications"] as JsonObject;
        bool B(string k, bool d) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : d;
        TimeOnly T(string k, TimeOnly d) => n?[k] is JsonValue v && v.TryGetValue<string>(out var s) && TimeOnly.TryParseExact(s, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : d;
        return new(B("omen", Default.Omen), T("omenAt", Default.OmenAt), B("followUps", Default.FollowUps), B("moonCircles", Default.MoonCircles),
                   T("quietFrom", Default.QuietFrom), T("quietTo", Default.QuietTo));
    }

    /// <summary>
    /// Quiet hours may cross midnight (22:00 to 08:00). They hold back the daemon's own initiative (follow-ups); the omen and
    /// moon reminders are subscriptions at a time the person chose, so they go out at that time.
    /// </summary>
    public bool IsQuiet(TimeOnly t) => QuietFrom <= QuietTo ? t >= QuietFrom && t < QuietTo : t >= QuietFrom || t < QuietTo;
}

/// <summary>Everything the rounds need about one person, read once per run.</summary>
public sealed record Person(string Subject, JsonObject Profile, JsonObject? Daemon, IReadOnlyList<(string Key, JsonObject Belief)> OpenThreads)
{
    public string TimeZone => Profile["timeZone"]?.GetValue<string>() is { } tz && ProfileRules.TimeZoneExists(tz) ? tz : "UTC";
    public string? Language => Profile["language"]?.GetValue<string>();
    public string DaemonName => Daemon?["name"]?.GetValue<string>() ?? "Your little soul";
    public int Stage => Daemon?["stage"] is JsonValue v && v.TryGetValue<int>(out var s) ? s : 1;
}

/// <summary>A message to send: which template, to whom, with what, and the outbox keys that make it happen once.</summary>
public sealed record Outgoing(string Kind, string Template, string Subject, JsonObject Data, string? Language, IReadOnlyList<string> OutboxKeys)
{
    /// <summary>For a follow-up: the thread to mark as asked.</summary>
    public string? ThreadKey { get; init; }
}

/// <summary>
/// The daemon's rounds, decided for one person at one moment. Pure: the job reads the documents and the outbox, this
/// decides, the job sends and records. The job runs every 15 minutes, so each window below is 15 minutes wide and
/// contains exactly one run.
///
/// Budget: the morning omen and moon-circle reminders are subscriptions the person turned on, so they don't count;
/// a follow-up is the daemon's own initiative, at most one per local day and never in quiet hours.
/// </summary>
public static class Rounds
{
    public const string OmenTemplate = "ls-omen", FollowUpTemplate = "ls-follow-up", MoonTemplate = "ls-moon-circle";
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    public static readonly TimeOnly FollowUpFrom = new(18, 0), FollowUpTo = new(21, 0), MoonAt = new(19, 0);

    public static string OutboxKey(string kind, string subject, DateOnly localDay) => $"{kind}:{subject}:{localDay:yyyy-MM-dd}";

    /// <summary>What to send to this person now. <paramref name="sent"/> answers whether an outbox key exists.</summary>
    public static List<Outgoing> Decide(Person p, DateTimeOffset now, Oracle oracle, Func<string, bool> sent)
    {
        var list = new List<Outgoing>();
        var tz = TimeZoneInfo.FindSystemTimeZoneById(p.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, tz);
        var day = DateOnly.FromDateTime(local.DateTime);
        var time = TimeOnly.FromDateTime(local.DateTime);
        var prefs = Preferences.From(p.Profile);
        var user = ProfileRules.ToUserContext(p.Subject, p.Profile, p.Daemon, null);

        // the morning omen: their card of the day (the same one the app shows) and one line about the day
        string omenKey = OutboxKey("omen", p.Subject, day);
        if (prefs.Omen && InWindow(time, prefs.OmenAt) && !sent(omenKey))
            list.Add(new Outgoing("omen", OmenTemplate, p.Subject, Omen(p, user, now, oracle), p.Language, [omenKey]));

        // a follow-up on something they told the daemon, from bond stage 3, in the evening, at most one a day
        string budgetKey = OutboxKey("unrequested", p.Subject, day);
        if (prefs.FollowUps && p.Stage >= 3 && time >= FollowUpFrom && time < FollowUpTo && !prefs.IsQuiet(time) && !sent(budgetKey))
        {
            var due = p.OpenThreads
                .Where(t => t.Belief["closed"]?.GetValue<bool>() != true && t.Belief["followedUpAt"] is null)
                .Select(t => (t.Key, t.Belief, Due: t.Belief["dueAt"] is JsonValue v && v.TryGetValue<string>(out var s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTimeOffset?)null))
                .Where(t => t.Due is { } d && d <= now)
                .OrderBy(t => t.Due)
                .FirstOrDefault();
            if (due.Belief is not null)
                list.Add(new Outgoing("follow-up", FollowUpTemplate, p.Subject,
                    new JsonObject { ["daemon"] = p.DaemonName, ["thread"] = due.Belief["value"]!.GetValue<string>() },
                    p.Language, [budgetKey, OutboxKey("follow-up", p.Subject, day)]) { ThreadKey = due.Key });
        }

        // a moon circle tonight, for people who asked to be reminded
        string moonKey = OutboxKey("moon", p.Subject, day);
        if (prefs.MoonCircles && InWindow(time, MoonAt) && !sent(moonKey))
        {
            var startOfDay = new DateTimeOffset(local.Date, local.Offset);
            var phase = MoonEvents.Phases(startOfDay, startOfDay.AddDays(1), tz).FirstOrDefault();
            if (phase is not null)
            {
                bool isNew = phase["phase"]!.GetValue<string>() == "New Moon";
                list.Add(new Outgoing("moon", MoonTemplate, p.Subject, new JsonObject
                {
                    ["daemon"] = p.DaemonName,
                    ["phase"] = phase["phase"]!.GetValue<string>(),
                    ["sign"] = phase["sign"]!.GetValue<string>(),
                    ["time"] = phase["local_time"]!.GetValue<string>()[11..],
                    ["theme"] = isNew ? "what do you want to begin?" : "what are you ready to let go?",
                }, p.Language, [moonKey]));
            }
        }
        return list;
    }

    /// <summary>[start, start + 15 min), also when the window crosses midnight.</summary>
    public static bool InWindow(TimeOnly t, TimeOnly start)
    {
        var end = start.Add(Window);
        return end > start ? t >= start && t < end : t >= start || t < end;
    }

    /// <summary>The omen's data: the card of the day, its question, and the most meaningful line about today.</summary>
    public static JsonObject Omen(Person p, UserContext user, DateTimeOffset now, Oracle oracle)
    {
        var card = oracle.Run("draw_tarot", new JsonObject { ["spread"] = "single", ["daily"] = true }, user with { Schools = null });
        var c = card.Result["cards"]![0]!;
        var day = DayContext.Build(now, user);
        return new JsonObject
        {
            ["daemon"] = p.DaemonName,
            ["card"] = c["card"]!.GetValue<string>() + (c["reversed"]!.GetValue<bool>() ? " (reversed)" : ""),
            ["question"] = c["question"]!.GetValue<string>(),
            ["line"] = Line(day),
            ["receipt"] = card.Receipt!.Id,
        };
    }

    /// <summary>One line about today, most personal first: a festivity of their roots, one where they live, a special moon, the moon.</summary>
    public static string Line(JsonObject day)
    {
        if (day["roots"]?["festivities_today"]?.AsArray().FirstOrDefault() is { } rf) return $"Today is {rf["name"]!.GetValue<string>()} back home.";
        if (day["festivities_today"]?.AsArray().FirstOrDefault() is { } f) return $"Today is {f["name"]!.GetValue<string>()}.";
        var specials = day["explain"]!.AsObject().Select(kv => kv.Key).ToList();
        if (specials.Contains("solar_eclipse") || specials.Contains("lunar_eclipse")) return "An eclipse is near.";
        if (specials.Contains("supermoon")) return "A supermoon is near.";
        var moon = day["moon"]!;
        return $"{moon["phase"]!.GetValue<string>()} in {moon["sign"]!["sign"]!.GetValue<string>()}: {moon["phase_meaning"]!.GetValue<string>()}.";
    }
}
