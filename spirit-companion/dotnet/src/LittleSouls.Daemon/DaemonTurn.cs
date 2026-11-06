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
}

/// <summary>
/// One turn of conversation: the crisis filter first (no model), then a <see cref="TurnWorld"/> run by Creatura's
/// <see cref="ReferenceLoop"/> with the host control layer (honest failures stay visible, so a reply the critic rejected
/// can be rewritten at once). The seat is any <see cref="IPolicy"/>: DeepSeek through Creatura.Seats, or a script in tests.
/// </summary>
public sealed class DaemonTurn(Oracle oracle, IPolicy seat, Persona persona, Notebook? notebook = null)
{
    public const int MaxTicks = 8;

    public async Task<TurnResult> RunAsync(UserContext user, string message, int stage, CancellationToken ct = default)
    {
        if (CrisisFilter.IsCrisis(message))
        {
            string fixedReply = CrisisFilter.Reply(user.Country, persona.Name);
            return new TurnResult(fixedReply, fixedReply, Grounded: true, CrisisHandoff: true, [], [], null);
        }

        var world = new TurnWorld(oracle, user, notebook);
        var task = new AgentTask(
            Name: "daemon turn",
            GoalText: "answer the person in character with a grounded reply",
            Goal: facts => facts.Contains(TurnWorld.Grounded),
            Progress: w => w.Has(TurnWorld.Grounded) ? 1 : w.Has(TurnWorld.ContextRead) ? 0.5 : 0,
            Contract: w => w.Has(TurnWorld.Grounded) ? 1 : 0,
            Habit: ["read_day_context", "reply"]);
        var regulator = new DaemonRegulator(user, persona, Notes(user, message, stage));
        var loop = new ReferenceLoop(regulator, seat, task, layer: ControlLayerOptions.Host, name: persona.Name);
        var run = await loop.RunAsync(world, MaxTicks, ct);

        bool grounded = world.Reply is not null;
        string reply = world.Reply ?? $"(no grounded reply after {MaxTicks} steps)";
        return new TurnResult(reply, Grounding.Display(reply), grounded, false,
            world.Readings.Select(r => r.Receipt!).ToList(), world.CriticNotes, run) { Readings = world.Readings.ToList() };
    }

    /// <summary>What the seat reads, fixed parts first so the provider's prompt cache can reuse them.</summary>
    private List<string> Notes(UserContext user, string message, int stage) =>
    [
        $"You are {persona.Name}, the {persona.CompanionNoun} of this person: a {persona.Archetype}. {persona.Voice}",
        "Rules: read the day first (read_day_context). Consult at most two methods, only when the situation calls for it. Never invent a card, rune, hexagram, date or person: name only what tools returned or the notebook holds, and tag them: [[card:The Tower]], [[lenormand:Rider]], [[rune:Isa]], [[hexagram:46]], [[person:Camila]]. No predictions about health, money or legal matters. Symbols are mirrors, never fate. Reply in the person's language, briefly, then call reply.",
        ProfileCard.Render(notebook ?? new Notebook(user.UserId), stage, DateTimeOffset.UtcNow, user.Name),
        $"The person says: {message}",
    ];
}

/// <summary>The daemon's regulator: identity, and the notes the seat reads. Everything else is Creatura's default.</summary>
internal sealed class DaemonRegulator(UserContext user, Persona persona, IReadOnlyList<string> notes) : IRegulator
{
    public Identity Identity { get; } = new(persona.Name, user.UserId);

    public SeatView Seat(RegulatorContext context) => new() { Notes = notes };
}
