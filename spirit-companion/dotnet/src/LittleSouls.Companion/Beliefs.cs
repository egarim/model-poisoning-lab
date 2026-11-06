namespace LittleSouls.Companion;

/// <summary>What a belief is about.</summary>
public enum BeliefKind
{
    /// <summary>A fact about the person: city, work, a pet.</summary>
    Fact,
    /// <summary>Someone in their life: "Camila, younger sister".</summary>
    Person,
    /// <summary>Something open, with a follow-up: "interview in Mexico City on Friday".</summary>
    Thread,
    /// <summary>Something the daemon noticed over time: "work comes up on Sunday nights".</summary>
    Pattern,
    /// <summary>How they like to be spoken to: short answers, humour, directness.</summary>
    Style,
    /// <summary>What they are working toward.</summary>
    Goal,
}

/// <summary>Where a belief came from. A confirmation from the person outranks everything; an inference ranks lowest.</summary>
public enum BeliefSource { Inferred = 0, Stated = 1, Confirmed = 2 }

/// <summary>Topics the daemon never infers. If the person states one, it is kept only with their consent.</summary>
public enum Sensitivity { None, Health, Sexuality, Religion, Politics, Ethnicity }

/// <summary>One piece of evidence: which session, what was said (short), when.</summary>
public sealed record Evidence(string SessionId, string Quote, DateTimeOffset At);

/// <summary>
/// A belief about the person, with the evidence that supports it: Creatura's <c>SkillBelief</c> (supported / attempted)
/// carried over from a tool to a person. Nothing here is a fact until the person said it or it was seen twice.
/// </summary>
public sealed class PersonBelief
{
    public required string Key { get; init; }
    public required BeliefKind Kind { get; init; }
    public required string Value { get; set; }
    public BeliefSource Source { get; set; }
    public Sensitivity Sensitivity { get; init; }
    public int Supported { get; set; }
    public int Contradicted { get; set; }
    public List<Evidence> Evidence { get; } = [];
    /// <summary>Earlier values, newest last, when the person stated a change.</summary>
    public List<string> History { get; } = [];
    public DateTimeOffset FirstSeen { get; init; }
    public DateTimeOffset LastSeen { get; set; }
    /// <summary>Threads only: when to follow up, and whether it is closed.</summary>
    public DateTimeOffset? DueAt { get; set; }
    public bool Closed { get; set; }

    /// <summary>
    /// How sure the daemon is. A confirmation is near certain and a statement strong; an inference grows with support
    /// and falls with contradiction (Laplace's rule), and never passes 0.8 until the person confirms it.
    /// </summary>
    public double Confidence => Source switch
    {
        BeliefSource.Confirmed => 0.95,
        BeliefSource.Stated => Math.Max(0.5, 0.85 - 0.15 * Contradicted),
        _ => Math.Min(0.8, (Supported + 1.0) / (Supported + Contradicted + 2.0)),
    };

    /// <summary>An inference needs a second, separate piece of evidence before the daemon acts on it.</summary>
    public bool Active => !Closed && (Source != BeliefSource.Inferred || Supported >= 2);
}

/// <summary>A change the model proposes at the end of a session. Only <see cref="CoherenceGate"/> decides what is kept.</summary>
public sealed record Proposal(BeliefKind Kind, string Key, string Value, BeliefSource Source, string Quote)
{
    public Sensitivity Sensitivity { get; init; }
    public DateTimeOffset? DueAt { get; init; }
    /// <summary>Threads: the person said it's resolved.</summary>
    public bool Close { get; init; }
}
