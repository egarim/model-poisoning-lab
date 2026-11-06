using System.Text.Json;
using System.Text.Json.Nodes;
using Daemon.Divination;
using LittleSouls.Companion;
using SingularFramework.Apps.LittleSouls.Jobs;
using SingularFramework.Platform.Provisioning;
using Xunit;

namespace SingularFramework.Apps.LittleSouls.Tests;

public class RoundsTests
{
    private static readonly Oracle Oracle = new("a-secret-of-sixteen+"u8.ToArray());

    private static JsonObject Profile(string tz = "Europe/Moscow", JsonObject? notifications = null) => new()
    {
        ["birthDate"] = "1990-05-01", ["country"] = "RU", ["rootsCountry"] = "SV", ["timeZone"] = tz, ["rootsTimeZone"] = "America/El_Salvador",
        ["language"] = "es", ["notifications"] = notifications,
    };

    private static JsonObject Thread(string value, string dueAt, bool closed = false, string? followedUpAt = null) => new()
    {
        ["kind"] = "Thread", ["key"] = value, ["value"] = value, ["dueAt"] = dueAt, ["closed"] = closed, ["followedUpAt"] = followedUpAt,
    };

    private static Person P(JsonObject? profile = null, int stage = 3, params (string, JsonObject)[] threads) =>
        new("u1", profile ?? Profile(), new JsonObject { ["name"] = "Izote", ["stage"] = stage, ["archetype"] = "wanderer" }, threads);

    private static List<Outgoing> At(Person p, string utc, Func<string, bool>? sent = null) =>
        Rounds.Decide(p, DateTimeOffset.Parse(utc), Oracle, sent ?? (_ => false));

    [Fact]
    public void The_omen_goes_out_at_half_past_seven_in_their_own_time()
    {
        // 07:30 in St. Petersburg is 04:30 UTC; 07:30 in Guadalajara is 13:30 UTC.
        var spb = At(P(), "2026-11-06T04:30:00Z");
        var omen = Assert.Single(spb);
        Assert.Equal("omen", omen.Kind);
        Assert.Equal(Rounds.OmenTemplate, omen.Template);
        Assert.Equal("es", omen.Language);
        Assert.Equal(["omen:u1:2026-11-06"], omen.OutboxKeys);
        Assert.Equal("Izote", omen.Data["daemon"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(omen.Data["card"]!.GetValue<string>()));
        Assert.Matches("^[0-9a-f]+", omen.Data["receipt"]!.GetValue<string>());

        Assert.Empty(At(P(), "2026-11-06T04:45:00Z"));                                  // the window is 15 minutes
        Assert.Empty(At(P(Profile("America/Mexico_City")), "2026-11-06T04:30:00Z"));    // 22:30 the night before there
        Assert.Single(At(P(Profile("America/Mexico_City")), "2026-11-06T13:30:00Z"));
    }

    [Fact]
    public void The_omen_happens_once_at_the_time_they_chose_unless_they_opt_out()
    {
        Assert.Empty(At(P(), "2026-11-06T04:30:00Z", k => k == "omen:u1:2026-11-06"));
        // A subscription: an omen they set inside their quiet hours still comes at their time.
        var early = new JsonObject { ["omenAt"] = "06:00", ["quietFrom"] = "22:00", ["quietTo"] = "08:00" };
        Assert.Single(At(P(Profile(notifications: early)), "2026-11-06T03:00:00Z"));
        Assert.Empty(At(P(Profile(notifications: new JsonObject { ["omen"] = false })), "2026-11-06T04:30:00Z"));
    }

    [Fact]
    public void The_same_card_as_the_app_shows()
    {
        var a = At(P(), "2026-11-06T04:30:00Z")[0].Data["card"]!.GetValue<string>();
        var b = At(P(), "2026-11-06T04:35:00Z")[0].Data["card"]!.GetValue<string>();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Follow_ups_need_the_bond_the_evening_a_due_thread_and_the_budget()
    {
        var threads = new[]
        {
            ("interview", Thread("the interview on Thursday", "2026-11-05T12:00:00Z")),
            ("trip", Thread("the trip to Moscow", "2026-11-04T12:00:00Z")),
            ("later", Thread("the exam", "2026-11-20T12:00:00Z")),
            ("done", Thread("the move", "2026-11-01T12:00:00Z", closed: true)),
            ("asked", Thread("the doctor", "2026-11-02T12:00:00Z", followedUpAt: "2026-11-03T16:00:00Z")),
        };
        const string evening = "2026-11-06T16:00:00Z"; // 19:00 in St. Petersburg
        var f = Assert.Single(At(P(threads: threads), evening));
        Assert.Equal("follow-up", f.Kind);
        Assert.Equal("trip", f.ThreadKey); // the earliest due, not closed, not already asked
        Assert.Equal("the trip to Moscow", f.Data["thread"]!.GetValue<string>());
        Assert.Equal(["unrequested:u1:2026-11-06", "follow-up:u1:2026-11-06"], f.OutboxKeys);

        Assert.Empty(At(P(stage: 2, threads: threads), evening));                                   // too early in the bond
        Assert.Empty(At(P(threads: threads), "2026-11-06T12:00:00Z"));                              // 15:00, not evening
        Assert.Empty(At(P(threads: threads), evening, k => k.StartsWith("unrequested:")));          // the day's budget is spent
        var quiet = new JsonObject { ["quietFrom"] = "18:30", ["quietTo"] = "08:00" };
        Assert.Empty(At(P(Profile(notifications: quiet), threads: threads), evening));             // their quiet hours
        Assert.Empty(At(P(threads: [threads[2]]), evening));                                        // nothing due yet
        Assert.Empty(At(P(Profile(notifications: new JsonObject { ["followUps"] = false }), threads: threads), evening));
    }

    [Fact]
    public void Moon_circle_reminders_only_on_the_night_and_only_when_asked()
    {
        // New moon on 2026-11-09; 19:00 in St. Petersburg is 16:00 UTC.
        var on = Profile(notifications: new JsonObject { ["moonCircles"] = true, ["omen"] = false });
        var m = Assert.Single(At(P(on), "2026-11-09T16:00:00Z"));
        Assert.Equal("moon", m.Kind);
        Assert.Equal("New Moon", m.Data["phase"]!.GetValue<string>());
        Assert.Matches(@"^\d\d:\d\d", m.Data["time"]!.GetValue<string>());
        Assert.Empty(At(P(on), "2026-11-10T16:00:00Z"));                                   // no phase that day
        Assert.Empty(At(P(Profile(notifications: new JsonObject { ["omen"] = false })), "2026-11-09T16:00:00Z")); // off by default
    }

    [Theory]
    [InlineData("07:30", "07:30", true)]
    [InlineData("07:44", "07:30", true)]
    [InlineData("07:45", "07:30", false)]
    [InlineData("23:55", "23:50", true)]
    [InlineData("00:04", "23:50", true)]
    [InlineData("00:05", "23:50", false)]
    public void Windows_cross_midnight(string t, string start, bool inside) =>
        Assert.Equal(inside, Rounds.InWindow(TimeOnly.Parse(t), TimeOnly.Parse(start)));

    [Theory]
    [InlineData("23:00", true)]
    [InlineData("03:00", true)]
    [InlineData("08:00", false)]
    [InlineData("12:00", false)]
    public void Quiet_hours_cross_midnight(string t, bool quiet) => Assert.Equal(quiet, Preferences.Default.IsQuiet(TimeOnly.Parse(t)));

    [Fact]
    public void The_line_puts_their_roots_first()
    {
        // 2 November: Día de los Difuntos in El Salvador, for someone living in Russia.
        var user = ProfileRules.ToUserContext("u1", Profile(), null, null);
        var line = Rounds.Line(DayContext.Build(DateTimeOffset.Parse("2026-11-02T09:00:00Z"), user));
        Assert.StartsWith("Today is ", line);
        Assert.EndsWith("back home.", line);
        var plain = Rounds.Line(DayContext.Build(DateTimeOffset.Parse("2026-11-12T09:00:00Z"), user));
        Assert.Contains(" in ", plain); // the moon line
    }
}

public class NotebookDocumentsTests
{
    [Fact]
    public void Beliefs_round_trip_and_app_fields_survive()
    {
        var nb = new Notebook("u1");
        var stateJson = JsonSerializer.Serialize(nb.ToState());
        Assert.NotNull(stateJson);

        var stored = new JsonObject
        {
            ["kind"] = "Thread", ["key"] = "the interview", ["value"] = "the interview on Thursday", ["source"] = "Told",
            ["sensitivity"] = "None", ["supported"] = 1, ["contradicted"] = 0,
            ["firstSeen"] = "2026-11-01T10:00:00.0000000+00:00", ["lastSeen"] = "2026-11-01T10:00:00.0000000+00:00",
            ["dueAt"] = "2026-11-05T12:00:00.0000000+00:00", ["closed"] = false,
            ["followedUpAt"] = "2026-11-05T16:00:00Z",
            ["evidence"] = new JsonArray(new JsonObject { ["session"] = "s1", ["quote"] = "I have an interview on Thursday", ["at"] = "2026-11-01T10:00:00Z" }),
            ["history"] = new JsonArray(),
        };
        var daemon = new JsonObject
        {
            ["notebook"] = new JsonObject
            {
                ["session"] = 4, ["forgotten"] = new JsonArray(NotebookDocuments.Fingerprint("old flame")),
                ["openQuestions"] = new JsonArray("where did she grow up?"),
                ["traits"] = new JsonObject { ["values"] = new JsonObject { ["Energy"] = 0.7 }, ["evidence"] = new JsonObject { ["Energy"] = 3 } },
            },
        };
        var loaded = NotebookDocuments.Load("u1", [("the interview", stored)], daemon);
        var (docs, meta) = NotebookDocuments.Save(loaded, new Dictionary<string, JsonObject> { [stored["key"]!.GetValue<string>()] = stored });

        var key = Assert.Single(docs.Keys);
        var doc = docs[key];
        Assert.Equal("the interview on Thursday", doc["value"]!.GetValue<string>());
        Assert.Equal("Thread", doc["kind"]!.GetValue<string>());
        Assert.Equal("2026-11-05T16:00:00Z", doc["followedUpAt"]!.GetValue<string>());
        Assert.Equal("I have an interview on Thursday", doc["evidence"]![0]!["quote"]!.GetValue<string>());
        Assert.Equal(4, meta["session"]!.GetValue<int>());
        Assert.Equal(NotebookDocuments.Fingerprint("old flame"), meta["forgotten"]![0]!.GetValue<string>());
        Assert.Equal("where did she grow up?", meta["openQuestions"]![0]!.GetValue<string>());
        Assert.Equal(0.7, meta["traits"]!["values"]!["Energy"]!.GetValue<double>(), 3);
    }

    [Fact]
    public void Fingerprints_ignore_case_and_surrounding_spaces()
    {
        Assert.Matches("^[0-9a-f]{64}$", NotebookDocuments.Fingerprint("Old Flame"));
        Assert.Equal(NotebookDocuments.Fingerprint("old flame"), NotebookDocuments.Fingerprint("  Old Flame "));
    }

    [Fact]
    public async Task Until_B3_there_are_no_proposals()
    {
        var none = await new NoProposals().ProposeAsync(new JsonObject(), "", CancellationToken.None);
        Assert.Empty(none);
    }
}

public class JobProvisioningTests
{
    private static readonly ProvisionApp App = JsonSerializer.Deserialize<ProvisionApp>(LittleSoulsApp.ProvisioningJson(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Fact]
    public void The_jobs_templates_exist_in_every_language()
    {
        foreach (var key in new[] { Rounds.OmenTemplate, Rounds.FollowUpTemplate, Rounds.MoonTemplate })
        {
            var t = Assert.Single(App.Templates!, x => x.Key == key);
            Assert.Equal("push", t.Channel);
            foreach (var lang in new[] { "es", "pt", "ru" }) Assert.True(t.Variants!.ContainsKey(lang), $"{key} has no {lang}");
            Assert.Contains("{{daemon}}", t.Subject);
        }
    }

    [Fact]
    public void The_outbox_is_system_only()
    {
        Assert.Contains(LittleSoulsApp.Outbox, App.Collections!);
        var a = App.Access![LittleSoulsApp.Outbox];
        Assert.Equal("admins", a.Read);
        Assert.Equal("admins", a.Create);
        Assert.Equal("admins", a.Write);
    }

    [Fact]
    public void Template_tokens_are_ones_the_jobs_fill()
    {
        var filled = new Dictionary<string, string[]>
        {
            [Rounds.OmenTemplate] = ["daemon", "card", "question", "line", "receipt"],
            [Rounds.FollowUpTemplate] = ["daemon", "thread"],
            [Rounds.MoonTemplate] = ["daemon", "phase", "sign", "time", "theme"],
        };
        foreach (var t in App.Templates!.Where(t => filled.ContainsKey(t.Key!)))
            foreach (var text in new[] { t.Subject, t.Body }.Concat(t.Variants!.Values.SelectMany(v => new[] { v.Subject, v.Body })))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text ?? "", @"\{\{(\w+)\}\}"))
                    Assert.Contains(m.Groups[1].Value, filled[t.Key!]);
    }
}
