using Creatura;
using Daemon.Divination;
using LittleSouls.Companion;

namespace LittleSouls.Daemon;

/// <summary>Who the daemon is, for the seat's notes.</summary>
public sealed record Persona(string Name, string Archetype, string Voice, string CompanionNoun = "little soul");

/// <summary>What one turn produced.</summary>
public sealed record TurnResult(
    string Reply,
    string Display,
    bool Grounded,
    bool CrisisHandoff,
    IReadOnlyList<DrawReceipt> Receipts,
    IReadOnlyList<string> CriticNotes,
    AgentRun? Run)
{
    /// <summary>What the methods returned this turn (one per receipt), for the app's cards and for review.</summary>
    public IReadOnlyList<Reading> Readings { get; init; } = [];
    /// <summary>For a crisis handoff: "keywords", or the classifier's reason.</summary>
    public string? CrisisReason { get; init; }
    /// <summary>The classifier could not run; the turn went on with the keywords only.</summary>
    public bool SafetyCheckFailed { get; init; }
    /// <summary>The reply is the last one grounded in the draws; the assertion check still had guesses in it.</summary>
    public bool HeldGuesses { get; init; }
    /// <summary>How many replies the assertion check read, and how many times it could not run.</summary>
    public int AssertionChecks { get; init; }
    public int AssertionCheckFailures { get; init; }
}

/// <summary>
/// One turn of conversation: the crisis filter first (no model), then a <see cref="TurnWorld"/> run by Creatura's
/// <see cref="ReferenceLoop"/> with the host control layer (honest failures stay visible, so a reply the critic rejected
/// can be rewritten at once). The seat is any <see cref="IPolicy"/>: DeepSeek through Creatura.Seats, or a script in tests.
/// </summary>
public sealed class DaemonTurn(Oracle oracle, IPolicy seat, Persona persona, Notebook? notebook = null, ICrisisClassifier? classifier = null, IAssertionChecker? checker = null)
{
    public const int MaxTicks = 8;

    /// <summary>What the seat reads in place of the person's words at the first meeting.</summary>
    public const string FirstMeeting =
        "This is your first meeting: the person has just summoned you and has not written anything yet, so you speak first. " +
        "Greet them by name when you know it, say in one or two sentences who you are and what you are for, read this day for " +
        "them (read_day_context, then the moon or the day's number) and tell them one true thing about it, and end by inviting " +
        "them to tell you about their day. Three to five short sentences, warm, every word in the person's language. Nothing " +
        "about their life beyond what you were told, and no question about it yet.";

    public Task<TurnResult> RunAsync(UserContext user, string message, int stage, CancellationToken ct = default) =>
        TurnAsync(user, message, stage, false, ct);

    /// <summary>
    /// The first meeting: the person has just summoned the daemon and has written nothing yet, so the daemon speaks first
    /// (who it is, one true thing about this day from a reading, an invitation). No crisis check: there are no words to check.
    /// </summary>
    public Task<TurnResult> GreetAsync(UserContext user, int stage, CancellationToken ct = default) =>
        TurnAsync(user, "", stage, true, ct);

    private async Task<TurnResult> TurnAsync(UserContext user, string message, int stage, bool greeting, CancellationToken ct)
    {
        // Safety before anything else, and never by the model: the keywords, then the classifier. A crisis gets the fixed
        // reply in the person's language with a verified line.
        CrisisVerdict? verdict = null;
        if (!greeting && !CrisisFilter.IsCrisis(message) && classifier is not null)
        {
            try { verdict = await classifier.ClassifyAsync(message, ct); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
            { verdict = new CrisisVerdict(false, e.GetType().Name, Failed: true); }
        }
        if (!greeting && (CrisisFilter.IsCrisis(message) || verdict is { Crisis: true }))
        {
            string fixedReply = CrisisFilter.Reply(user.Country, persona.Name, CrisisFilter.Language(message, user.Country));
            return new TurnResult(fixedReply, fixedReply, Grounded: true, CrisisHandoff: true, [], [], null) { CrisisReason = verdict?.Reason ?? "keywords" };
        }

        var world = new TurnWorld(oracle, user, notebook, message);
        var task = new AgentTask(
            Name: "daemon turn",
            GoalText: "answer the person in character with a grounded reply",
            Goal: facts => facts.Contains(TurnWorld.Grounded),
            Progress: w => w.Has(TurnWorld.Grounded) ? 1 : w.Has(TurnWorld.ContextRead) ? 0.5 : 0,
            Contract: w => w.Has(TurnWorld.Grounded) ? 1 : 0,
            Habit: ["read_day_context", "reply"]);
        var regulator = new DaemonRegulator(user, persona, Notes(user, message, stage, verdict is { Failed: true }, greeting));
        var checking = checker is null ? null : new CheckingSeat(seat, checker, world, message);
        var loop = new ReferenceLoop(regulator, (IPolicy?)checking ?? seat, task, layer: ControlLayerOptions.Host, name: persona.Name);
        var run = await loop.RunAsync(world, MaxTicks, ct);

        // Out of steps with only guesses left to fix: the person still gets an answer grounded in the draws.
        bool fallback = world.Reply is null && world.LastSymbolGrounded is not null;
        bool grounded = world.Reply is not null || fallback;
        string reply = world.Reply ?? world.LastSymbolGrounded ?? $"(no grounded reply after {MaxTicks} steps)";
        return new TurnResult(reply, Grounding.Display(reply), grounded, false,
            world.Readings.Select(r => r.Receipt!).ToList(), world.CriticNotes, run)
        { Readings = world.Readings.ToList(), HeldGuesses = fallback, SafetyCheckFailed = verdict is { Failed: true }, AssertionChecks = checking?.Checks ?? 0, AssertionCheckFailures = checking?.Failures ?? 0 };
    }

    /// <summary>What the seat reads, fixed parts first so the provider's prompt cache can reuse them.</summary>
    private List<string> Notes(UserContext user, string message, int stage, bool safetyCheckFailed, bool greeting)
    {
        var notes = new List<string>
        {
            $"You are {persona.Name}, the {persona.CompanionNoun} of this person: a {persona.Archetype}. {persona.Voice}",
            "Rules: read the day first (read_day_context). Every answer goes through one method: consult one (at most two) before you reply; reply is refused until a reading is made. Pick the method that fits: I Ching or a tarot choice spread for a decision, runes for feeling stuck, a single card, the moon or numerology for the day's energy, the method they ask for when they ask. When they only need to be heard, still draw one card quietly and let it shape a short, warm answer that listens more than it reads. Never invent a card, rune, hexagram, date or person: name only what tools returned, the notebook holds or the person said, and tag them: [[card:The Tower]], [[lenormand:Rider]], [[rune:Isa]], [[hexagram:46]], [[person:Camila]]. About the person, state as fact only what the notebook or their own words say; anything else is a question or a maybe, never a detail you made up (no invented counts, dates, habits or feelings). Never give a phone number. No predictions or advice about health, money or legal matters: symptoms go to a doctor (now, if urgent), money and legal questions to a professional, and the reading speaks only to how they feel and what they can do. Symbols are mirrors, never fate. Reply in the person's language, briefly, then call reply.",
            ProfileCard.Render(notebook ?? new Notebook(user.UserId), stage, DateTimeOffset.UtcNow, user.Name),
            greeting ? FirstMeeting : $"The person says: {message}",
        };
        if (safetyCheckFailed && CrisisLines.For(user.Country) is { } line)
            notes.Add($"The safety check could not run for this message. If anything suggests they may be at risk, set the reading aside, say plainly that they matter, and give exactly this line: {line.Name}, {line.Number} (or {line.Emergency} in danger).");
        return notes;
    }
}

/// <summary>The daemon's regulator: identity, and the notes the seat reads. Everything else is Creatura's default.</summary>
internal sealed class DaemonRegulator(UserContext user, Persona persona, IReadOnlyList<string> notes) : IRegulator
{
    public Identity Identity { get; } = new(persona.Name, user.UserId);

    public SeatView Seat(RegulatorContext context) => new() { Notes = notes };
}
