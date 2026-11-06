using LittleSouls.Companion;
using Xunit;

namespace LittleSouls.Companion.Tests;

public class NotebookTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 20, 0, 0, TimeSpan.Zero);
    private static Proposal Stated(BeliefKind k, string key, string v) => new(k, key, v, BeliefSource.Stated, "user said so");
    private static Proposal Inferred(BeliefKind k, string key, string v) => new(k, key, v, BeliefSource.Inferred, "seemed so");

    [Fact]
    public void Stated_facts_are_active_at_once()
    {
        var nb = new Notebook("u1");
        var r = CoherenceGate.Apply(nb, [Stated(BeliefKind.Person, "person:camila", "Camila, younger sister")], "s1", T0);
        Assert.Equal(["person:camila"], r.Added);
        Assert.True(nb.Get("person:camila")!.Active);
        Assert.Equal(0.85, nb.Get("person:camila")!.Confidence);
    }

    [Fact]
    public void Inferences_need_a_second_session()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Inferred(BeliefKind.Pattern, "pattern:sunday-work", "work weighs on her on Sunday nights")], "s1", T0);
        Assert.False(nb.Get("pattern:sunday-work")!.Active);
        // the same session cannot count twice
        CoherenceGate.Apply(nb, [Inferred(BeliefKind.Pattern, "pattern:sunday-work", "work weighs on her on Sunday nights")], "s1", T0);
        Assert.False(nb.Get("pattern:sunday-work")!.Active);
        nb.EndSession();
        CoherenceGate.Apply(nb, [Inferred(BeliefKind.Pattern, "pattern:sunday-work", "Work weighs on her on Sunday nights")], "s2", T0.AddDays(7));
        var b = nb.Get("pattern:sunday-work")!;
        Assert.True(b.Active);
        Assert.Equal(2, b.Supported);
        Assert.True(b.Confidence < 0.8);
    }

    [Fact]
    public void A_stated_change_replaces_and_keeps_history()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Fact, "fact:city", "lives in Guadalajara")], "s1", T0);
        var r = CoherenceGate.Apply(nb, [Stated(BeliefKind.Fact, "fact:city", "lives in Mexico City")], "s2", T0.AddDays(30));
        Assert.Equal(["fact:city"], r.Replaced);
        Assert.Equal("lives in Mexico City", nb.Get("fact:city")!.Value);
        Assert.Equal(["lives in Guadalajara"], nb.Get("fact:city")!.History);
    }

    [Fact]
    public void A_contradicting_inference_is_quarantined_and_becomes_a_question()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Fact, "fact:job-feeling", "loves her job")], "s1", T0);
        var r = CoherenceGate.Apply(nb, [Inferred(BeliefKind.Fact, "fact:job-feeling", "dreads Mondays at work")], "s2", T0);
        Assert.Equal(["fact:job-feeling"], r.Quarantined);
        Assert.Equal("loves her job", nb.Get("fact:job-feeling")!.Value); // never overwritten
        Assert.True(nb.IsQuarantined("fact:job-feeling"));
        Assert.Single(nb.OpenQuestions);
        Assert.DoesNotContain("- loves her job", ProfileCard.Render(nb, 3, T0)); // not listed as a fact; only inside the open question
        Assert.Contains("unsure", ProfileCard.Render(nb, 3, T0));
        for (int i = 0; i < CoherenceGate.QuarantineSessions; i++) nb.EndSession();
        Assert.False(nb.IsQuarantined("fact:job-feeling"));
    }

    [Fact]
    public void The_person_settles_a_question()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Fact, "fact:job-feeling", "loves her job")], "s1", T0);
        CoherenceGate.Apply(nb, [Inferred(BeliefKind.Fact, "fact:job-feeling", "dreads Mondays")], "s2", T0);
        Assert.True(nb.Correct("fact:job-feeling", "likes the work, not the Monday meetings", T0));
        var b = nb.Get("fact:job-feeling")!;
        Assert.Equal(BeliefSource.Confirmed, b.Source);
        Assert.Equal(0.95, b.Confidence);
        Assert.False(nb.IsQuarantined("fact:job-feeling"));
        Assert.Empty(nb.OpenQuestions);
    }

    [Fact]
    public void Sensitive_topics_are_never_inferred_and_wait_for_consent_when_stated()
    {
        var nb = new Notebook("u1");
        var r = CoherenceGate.Apply(nb,
        [
            Inferred(BeliefKind.Fact, "fact:health", "seems depressed") with { Sensitivity = Sensitivity.Health },
            Stated(BeliefKind.Fact, "fact:faith", "goes to mass on Sundays") with { Sensitivity = Sensitivity.Religion },
        ], "s1", T0);
        Assert.Contains(r.Dropped, d => d.Key == "fact:health");
        Assert.Null(nb.Get("fact:health"));
        Assert.Equal(["fact:faith"], r.AwaitingConsent);
        Assert.Null(nb.Get("fact:faith"));
        Assert.True(nb.GiveConsent("fact:faith", T0, "s1"));
        Assert.True(nb.Get("fact:faith")!.Active);
    }

    [Fact]
    public void Forgotten_stays_forgotten_unless_told_again()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Person, "person:diego", "Diego, her ex")], "s1", T0);
        Assert.True(nb.Forget("person:diego"));
        Assert.DoesNotContain("diego", nb.Export(), StringComparison.OrdinalIgnoreCase);
        var r = CoherenceGate.Apply(nb, [Inferred(BeliefKind.Person, "person:diego", "Diego, her ex")], "s2", T0);
        Assert.Contains(r.Dropped, d => d.Why.Contains("forgotten"));
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Person, "person:diego", "Diego, they're friends again")], "s3", T0);
        Assert.NotNull(nb.Get("person:diego"));
    }

    [Fact]
    public void Threads_open_show_at_stage_3_and_close()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Thread, "thread:cdmx-interview", "interview in Mexico City") with { DueAt = T0.AddDays(1) }], "s1", T0);
        Assert.DoesNotContain("interview", ProfileCard.Render(nb, 2, T0));
        Assert.Contains("interview in Mexico City (due Sat 7 Nov)", ProfileCard.Render(nb, 3, T0));
        Assert.Contains("ask about it", ProfileCard.Render(nb, 3, T0.AddDays(2)));
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Thread, "thread:cdmx-interview", "") with { Close = true }], "s2", T0.AddDays(2));
        Assert.DoesNotContain("interview", ProfileCard.Render(nb, 3, T0.AddDays(2)));
    }

    [Fact]
    public void Patterns_only_from_stage_4_and_card_stays_small()
    {
        var nb = new Notebook("u1");
        for (int s = 1; s <= 2; s++) { CoherenceGate.Apply(nb, [Inferred(BeliefKind.Pattern, "pattern:star", "draws The Star often")], "s" + s, T0); nb.EndSession(); }
        for (int i = 0; i < 40; i++) CoherenceGate.Apply(nb, [Stated(BeliefKind.Fact, "fact:" + i, new string('x', 60) + i)], "s9", T0);
        Assert.DoesNotContain("The Star", ProfileCard.Render(nb, 3, T0));
        string card4 = ProfileCard.Render(nb, 4, T0);
        Assert.Contains("draws The Star often", card4);
        Assert.True(card4.Length <= ProfileCard.MaxChars + 2);
    }

    [Fact]
    public void Traits_move_slowly()
    {
        var t = new Traits();
        t.Seed(0.3, 0.4, 0.3, 0.5);
        for (int i = 0; i < 100; i++) t.Observe(Trait.Energy, 1.0);
        Assert.InRange(t[Trait.Energy], 0.39, 0.4 + 1e-9); // capped at +0.1 in one session
        t.EndSession();
        t.Observe(Trait.Energy, 1.0);
        Assert.True(t[Trait.Energy] > 0.4);
        Assert.True(t.Confidence(Trait.Energy) > t.Confidence(Trait.Social));
    }

    [Fact]
    public void Predictions_move_the_beliefs_they_rest_on()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Inferred(BeliefKind.Pattern, "pattern:nervous-before-big-days", "gets nervous before big days")], "s1", T0);
        var hit = nb.Predictions.Make("the interview will make her nervous", ["pattern:nervous-before-big-days"], T0.AddDays(1));
        Assert.Empty(nb.Predictions.Due(T0));
        Assert.Single(nb.Predictions.Due(T0.AddDays(1)));
        Assert.True(nb.Predictions.Resolve(nb, hit.Id, correct: true));
        Assert.True(nb.Get("pattern:nervous-before-big-days")!.Active); // the hit is its second piece of evidence
        Assert.False(nb.Predictions.Resolve(nb, hit.Id, correct: true)); // only once
        var miss = nb.Predictions.Make("she'll cancel", ["pattern:nervous-before-big-days"], T0);
        nb.Predictions.Resolve(nb, miss.Id, correct: false);
        Assert.Equal(1, nb.Get("pattern:nervous-before-big-days")!.Contradicted);
        Assert.Equal(0.5, nb.Predictions.RecentAccuracy);
    }

    [Fact]
    public void Export_is_complete_and_readable()
    {
        var nb = new Notebook("u1");
        CoherenceGate.Apply(nb, [Stated(BeliefKind.Person, "person:abuela-rosa", "Abuela Rosa, remembered on Día de los Difuntos")], "s1", T0);
        string json = nb.Export();
        Assert.Contains("Abuela Rosa, remembered on Día de los Difuntos", json); // unescaped, readable
        Assert.Contains("\"evidence\"", json);
        Assert.Contains("\"traits\"", json);
    }
}
