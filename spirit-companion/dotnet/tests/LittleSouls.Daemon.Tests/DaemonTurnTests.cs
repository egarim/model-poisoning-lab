using System.Text.RegularExpressions;
using Creatura;
using Daemon.Divination;
using LittleSouls.Companion;
using LittleSouls.Daemon;
using Xunit;

namespace LittleSouls.Daemon.Tests;

/// <summary>A stand-in for the model: each step sees what a model sees (the context, with the history of outcomes).</summary>
internal sealed class ScriptedSeat(params Func<PolicyContext, PolicySuggestion>[] steps) : IPolicy
{
    private int _i;
    public List<PolicyContext> Seen { get; } = [];
    public string Name => "scripted";
    public Task<PolicySuggestion> NextAsync(PolicyContext context, CancellationToken cancellationToken = default)
    {
        Seen.Add(context);
        var step = steps[Math.Min(_i++, steps.Length - 1)];
        return Task.FromResult(step(context));
    }

    public static PolicySuggestion Do(string tool, string? content = null) => new(tool, "scripted", content);

    /// <summary>A major arcana card that was not the last one drawn.</summary>
    public static string NotDrawn(PolicyContext c) => LastCard(c) == "The Tower" ? "The Moon" : "The Tower";

    /// <summary>The card a draw returned, read from the history the way a model would read it.</summary>
    public static string LastCard(PolicyContext c) =>
        Regex.Matches(string.Join("\n", c.History), "\"card\":\"([^\"]+)\"").Last().Groups[1].Value;
}

public class DaemonTurnTests
{
    private static readonly Oracle Oracle = new("test-secret"u8.ToArray());
    private static readonly Persona Itzel = new("Itzel", "Seer", "Poetic, patient; speaks in images but lands on one clear point.");
    private static UserContext Valeria(IReadOnlySet<string>? schools = null) =>
        new("valeria", "America/Mexico_City") { Country = "MX", Name = "Valeria", BirthDate = new DateOnly(1997, 7, 22), Schools = schools };

    [Fact]
    public async Task A_reading_turn_reaches_a_grounded_reply()
    {
        var seat = new ScriptedSeat(
            _ => ScriptedSeat.Do("read_day_context"),
            _ => ScriptedSeat.Do("draw_tarot", "{\"spread\":\"single\"}"),
            c => ScriptedSeat.Do("reply", $"The card that came for you is [[card:{ScriptedSeat.LastCard(c)}]]. What is it asking you to notice today?"));
        var r = await new DaemonTurn(Oracle, seat, Itzel).RunAsync(Valeria(), "What does today hold?", stage: 3);

        Assert.True(r.Grounded);
        Assert.True(r.Run!.GoalReached);
        Assert.Single(r.Receipts);
        Assert.True(Oracle.Verify(r.Receipts[0], Valeria()));
        Assert.DoesNotContain("[[", r.Display);
        Assert.Empty(r.CriticNotes);
        Assert.Contains(seat.Seen[0].Notes, n => n.Contains("You are Itzel"));
    }

    [Fact]
    public async Task An_invented_card_is_caught_and_the_reply_rewritten()
    {
        var seat = new ScriptedSeat(
            _ => ScriptedSeat.Do("read_day_context"),
            _ => ScriptedSeat.Do("draw_tarot", "{\"spread\":\"single\"}"),
            c => ScriptedSeat.Do("reply", $"I see [[card:{ScriptedSeat.NotDrawn(c)}]] over your week."), // not what was drawn
            c => ScriptedSeat.Do("reply", $"Let's see what really came: [[card:{ScriptedSeat.LastCard(c)}]]."));
        var r = await new DaemonTurn(Oracle, seat, Itzel).RunAsync(Valeria(), "Read my week", 3);

        Assert.True(r.Grounded);
        Assert.Contains(r.CriticNotes, n => n.Contains("was not drawn"));
        // the model saw the critic's reason in its history before rewriting
        Assert.Contains(seat.Seen[3].History, h => h.Contains("not sent; the critic found"));
        Assert.Contains("really came", r.Reply);
    }

    [Fact]
    public async Task A_daemon_that_keeps_inventing_never_sends()
    {
        var seat = new ScriptedSeat(
            _ => ScriptedSeat.Do("read_day_context"),
            _ => ScriptedSeat.Do("draw_tarot", "{\"spread\":\"single\"}"),
            c => ScriptedSeat.Do("reply", $"You drew [[card:{ScriptedSeat.NotDrawn(c)}]], so beware."));
        var r = await new DaemonTurn(Oracle, seat, Itzel).RunAsync(Valeria(), "Anything for me?", 3);
        Assert.False(r.Grounded);
        Assert.False(r.Run!.GoalReached);
        Assert.Contains(r.CriticNotes, n => n.Contains("was not drawn"));
    }

    [Fact]
    public async Task Crisis_never_reaches_the_model()
    {
        var seat = new ScriptedSeat(_ => throw new InvalidOperationException("the model must not be called"));
        var r = await new DaemonTurn(Oracle, seat, Itzel).RunAsync(Valeria(), "ya no quiero vivir", 3);
        Assert.True(r.CrisisHandoff);
        Assert.Contains("800 911 2000", r.Reply);
        Assert.Null(r.Run);
        Assert.Empty(seat.Seen);
    }

    [Fact]
    public async Task A_locked_school_is_refused_and_the_daemon_answers_without_it()
    {
        var seat = new ScriptedSeat(
            _ => ScriptedSeat.Do("read_day_context"),
            _ => ScriptedSeat.Do("draw_lenormand", "{}"),
            c => c.History.Last().Contains("locked") ? ScriptedSeat.Do("draw_tarot", "{\"spread\":\"single\"}") : ScriptedSeat.Do("reply", "?"),
            c => ScriptedSeat.Do("reply", $"I don't speak Lenormand yet, so the tarot answered: [[card:{ScriptedSeat.LastCard(c)}]]."));
        var r = await new DaemonTurn(Oracle, seat, Itzel).RunAsync(Valeria(new HashSet<string> { "tarot" }), "Lenormand please", 2);
        Assert.True(r.Grounded);
        Assert.Equal("draw_tarot", Assert.Single(r.Receipts).Method);
        Assert.Contains("Lenormand yet", r.Reply);
    }

    [Fact]
    public async Task People_come_from_the_notebook()
    {
        var nb = new Notebook("valeria");
        CoherenceGate.Apply(nb, [new Proposal(BeliefKind.Person, "person:camila", "Camila, younger sister", BeliefSource.Stated, "my sister Camila")], "s1", DateTimeOffset.UtcNow);
        var seat = new ScriptedSeat(
            _ => ScriptedSeat.Do("read_day_context"),
            _ => ScriptedSeat.Do("draw_tarot", "{\"spread\":\"single\"}"),
            _ => ScriptedSeat.Do("reply", "Maybe call [[person:Rosa]] tonight?"),
            c => ScriptedSeat.Do("recall", "sister"),
            _ => ScriptedSeat.Do("reply", "Maybe call [[person:Camila]] tonight?"));
        var r = await new DaemonTurn(Oracle, seat, Itzel, nb).RunAsync(Valeria(), "I feel lonely", 3);
        Assert.True(r.Grounded);
        Assert.Contains(r.CriticNotes, n => n.StartsWith("Rosa is not someone"));
        Assert.Contains(seat.Seen[4].History, h => h.Contains("Camila, younger sister"));
        Assert.Contains(seat.Seen[0].Notes, n => n.Contains("Camila, younger sister")); // the profile card
        Assert.Equal("Maybe call Camila tonight?", r.Display);
    }
}

public class GroundingTests
{
    private static TurnEvidence Drew(params string[] tarot) { var e = new TurnEvidence(); foreach (var t in tarot) e.Tarot.Add(t); return e; }

    [Theory]
    [InlineData("You drew The Star; let it rest in you.", true)]
    [InlineData("You drew the star card.", true)]
    [InlineData("The Tower is falling for you.", false)]
    [InlineData("Strength comes from rest, not from pushing.", true)]  // an ordinary word, not a card
    [InlineData("I drew Strength for you.", false)]
    [InlineData("Death is part of life, and today we remember.", true)]
    [InlineData("The Death card asks what is ending.", false)]
    [InlineData("Ten of Cups, upright.", false)]
    [InlineData("Isa: a pause. Wait before you act.", false)]
    public void Only_drawn_cards_may_be_named(string reply, bool grounded) =>
        Assert.Equal(grounded, Grounding.Check(reply, Drew("The Star"), null).Count == 0);

    [Fact]
    public void Hexagrams_by_number_or_name()
    {
        var e = new TurnEvidence(); e.Hexagrams.Add(46); e.HexagramNames.Add("Pushing Upward");
        Assert.Empty(Grounding.Check("[[hexagram:46]] Pushing upward, step by step.", e, null));
        Assert.NotEmpty(Grounding.Check("[[hexagram:12]] Standstill.", e, null));
    }
}

public class EngineRuleTests
{
    private static readonly Oracle Oracle = new("test-secret"u8.ToArray());
    private static readonly Persona Itzel = new("Itzel", "Seer", "Poetic.");
    private static UserContext Valeria() => new("valeria", "America/Mexico_City") { Country = "MX", Name = "Valeria" };

    [Fact]
    public async Task No_reply_without_a_reading()
    {
        var seat = new ScriptedSeat(
            _ => ScriptedSeat.Do("read_day_context"),
            _ => ScriptedSeat.Do("reply", "Just breathe today."),
            _ => ScriptedSeat.Do("cast_runes", "{}"),
            _ => ScriptedSeat.Do("reply", "Just breathe today, [[person:Valeria]]."));
        var r = await new DaemonTurn(Oracle, seat, Itzel).RunAsync(Valeria(), "I'm tired", 3);
        Assert.True(r.Grounded);
        Assert.Contains(seat.Seen[2].History, h => h.Contains(TurnWorld.ReadingMade)); // the first reply was refused: no reading yet
        Assert.Single(r.Receipts);
        Assert.Equal("Just breathe today, Valeria.", r.Display); // the person's own name is always fine
    }

    [Fact]
    public void What_the_notebook_or_the_person_names_may_be_referred_back_to()
    {
        var e = new TurnEvidence { Mentioned = "Drew The Star three times this month.\n[Карта дня: Башня]" };
        Assert.Empty(Grounding.Check("The Star again, the third time this month.", e, null));
        Assert.Empty(Grounding.Check("Your [[card:Башня]] today: what is falling?", e, null));
        Assert.NotEmpty(Grounding.Check("I drew [[card:The Tower]] for you.", e, null));
    }

    [Theory]
    [InlineData("Llama a la Línea de la Vida, 800 911 2000.", true)]
    [InlineData("Позвони 112 или +7 495 989-50-50.", true)]
    [InlineData("Позвони на линию МЧС 8-800-775-17-17, круглосуточно.", false)] // the number the bench caught the model inventing
    [InlineData("Liga pro 141 agora.", false)]
    [InlineData("La luna está al 7% y hoy es 6 de noviembre de 2026.", true)]
    public void A_model_never_gives_a_phone_number_that_is_not_verified(string reply, bool ok) =>
        Assert.Equal(ok, Grounding.Check(reply, new TurnEvidence(), null).Count == 0);

    [Fact]
    public void Every_tag_is_stripped_for_the_person() =>
        Assert.Equal("Tomorrow is Beltane and The Star is with you.", Grounding.Display("Tomorrow is [[festivity:Beltane]] and [[card:The Star]] is with you."));
}

public class CrisisTests
{
    private static readonly Oracle Oracle = new("test-secret"u8.ToArray());
    private static readonly Persona Itzel = new("Itzel", "Seer", "Poetic.");

    private sealed class FixedClassifier(CrisisVerdict? verdict) : ICrisisClassifier
    {
        public int Calls;
        public Task<CrisisVerdict> ClassifyAsync(string message, CancellationToken ct)
        {
            Calls++;
            return verdict is null ? throw new HttpRequestException("down") : Task.FromResult(verdict);
        }
    }

    [Theory]
    [InlineData("A veces pienso que todos estarían mejor sin mí.", "MX", "es", "800 911 2000")]
    [InlineData("Às vezes acho que todo mundo ficaria melhor sem mim.", "BR", "pt", "188")]
    [InlineData("Иногда мне кажется, что всем было бы лучше без меня.", "RU", "ru", "989-50-50")]
    [InlineData("Everyone would be better off without me.", "CL", "en", "*4141")]
    public async Task Indirect_messages_get_the_fixed_reply_in_their_language(string message, string country, string lang, string number)
    {
        var seat = new ScriptedSeat(_ => throw new InvalidOperationException("the model must not be called"));
        var r = await new DaemonTurn(Oracle, seat, Itzel).RunAsync(new UserContext("u", "UTC") { Country = country }, message, 3);
        Assert.True(r.CrisisHandoff);
        Assert.Equal(lang, CrisisFilter.Language(message, country));
        Assert.Contains(number, r.Reply);
        Assert.Empty(seat.Seen);
    }

    [Fact]
    public async Task What_the_classifier_marks_never_reaches_the_model()
    {
        var seat = new ScriptedSeat(_ => throw new InvalidOperationException("the model must not be called"));
        var classifier = new FixedClassifier(new CrisisVerdict(true, "hopeless, no way out"));
        var r = await new DaemonTurn(Oracle, seat, Itzel, classifier: classifier).RunAsync(new UserContext("u", "UTC") { Country = "AR" }, "No le veo salida a nada, estoy cansada de todo.", 3);
        Assert.True(r.CrisisHandoff);
        Assert.Equal("hopeless, no way out", r.CrisisReason);
        Assert.Contains("0800-999-0091", r.Reply);
        Assert.StartsWith("Te escucho", r.Reply);
        Assert.Empty(seat.Seen);
    }

    [Fact]
    public async Task When_the_classifier_fails_the_turn_goes_on_with_the_verified_line_in_its_notes()
    {
        var seat = new ScriptedSeat(_ => ScriptedSeat.Do("read_day_context"), _ => ScriptedSeat.Do("draw_tarot", "{\"spread\":\"single\"}"), c => ScriptedSeat.Do("reply", "Aquí estoy."));
        var r = await new DaemonTurn(Oracle, seat, Itzel, classifier: new FixedClassifier(null)).RunAsync(new UserContext("u", "UTC") { Country = "PE" }, "Hoy fue un día raro.", 3);
        Assert.False(r.CrisisHandoff);
        Assert.True(r.SafetyCheckFailed);
        Assert.Contains(seat.Seen[0].Notes, n => n.Contains("113, opción 5"));
    }

    [Fact]
    public void Every_line_has_a_source_and_a_status()
    {
        Assert.All(CrisisLines.ByCountry.Values, l =>
        {
            Assert.Contains(l.Status, new[] { "official", "secondary" });
            Assert.False(string.IsNullOrWhiteSpace(l.Source));
            Assert.Equal(l.Country, CrisisLines.ByCountry.First(kv => kv.Value == l).Key);
        });
        Assert.DoesNotContain(CrisisLines.ByCountry.Values, l => l.Number.Contains("2000-122")); // the children's line, not for adults
    }
}
