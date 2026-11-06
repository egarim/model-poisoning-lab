using System.Text.Json.Nodes;
using Creatura;
using Daemon.Divination;
using LittleSouls.Companion;

namespace LittleSouls.Daemon;

/// <summary>
/// One conversation turn as a Creatura world. Facts are the registry; each tool requires and grants facts; the goal is
/// <see cref="Grounded"/>. The reply tool lands only when the grounding critic finds nothing invented; otherwise it
/// reports an honest failure with the critic's reasons, which the model reads in its history before trying again.
/// The world itself never lies: every outcome says exactly what happened.
/// </summary>
public sealed class TurnWorld : IWorld
{
    public const string Received = "message.received", ContextRead = "context.read", Recalled = "memory.recalled", Grounded = "reply.grounded";

    /// <summary>
    /// Granted by every divination method. The reply requires it: the little soul never answers out of the model alone,
    /// every answer goes through one of the engines (the owner's rule). Fixed crisis replies are not model answers.
    /// </summary>
    public const string ReadingMade = "reading.made";

    private static readonly string[] OracleTools = ["draw_tarot", "draw_lenormand", "cast_runes", "cast_i_ching", "personal_numerology", "sky_now", "birth_chart", "maya_day_sign", "moon_circles", "history_reading"];

    private readonly Oracle _oracle;
    private readonly UserContext _user;
    private readonly Notebook? _notebook;
    private readonly HashSet<string> _facts = new(StringComparer.Ordinal) { Received };
    private readonly List<Tool> _tools;

    public TurnWorld(Oracle oracle, UserContext user, Notebook? notebook, string? message = null)
    {
        _oracle = oracle; _user = user; _notebook = notebook;
        // What the person and the notebook already name may be referred back to ("you drew The Star three times").
        Evidence.Mentioned = string.Join("\n", new[] { message ?? "" }.Concat(notebook?.All.Where(b => b.Active).Select(b => b.Value) ?? []));
        Evidence.PersonName = user.Name;
        _tools =
        [
            new Tool("read_day_context", [Received], [ContextRead], "read") { NeedsPayload = false },
            new Tool("recall", [Received], [Recalled], "read") { NeedsPayload = true, PayloadHint = "a word or name to look up in the notebook" },
            .. OracleTools.Select(t => new Tool(t, [ContextRead], ["reading." + t, ReadingMade], "read") { NeedsPayload = true, PayloadHint = "the tool's arguments as JSON, e.g. {\"spread\":\"three\"}; {} for defaults" }),
            new Tool("reply", [ContextRead, ReadingMade], [Grounded], "reply") { NeedsPayload = true, PayloadHint = "the reply to the person, tagging every card, rune, hexagram and person you name, e.g. [[card:The Star]]" },
        ];
    }

    public TurnEvidence Evidence { get; } = new();
    public Notebook? Notebook => _notebook;
    /// <summary>The last reply the grounding critic passed that the assertion check held back.</summary>
    public string? LastSymbolGrounded { get; private set; }
    /// <summary>Notes the assertion check found on a reply text before it was acted on (see CheckingSeat).</summary>
    public Dictionary<string, List<string>> PreChecked { get; } = new(StringComparer.Ordinal);
    public List<Reading> Readings { get; } = [];
    public JsonObject? DayContext { get; private set; }
    /// <summary>The grounded reply, once the reply tool has landed.</summary>
    public string? Reply { get; private set; }
    /// <summary>Everything the critic objected to, across attempts.</summary>
    public List<string> CriticNotes { get; } = [];

    // ---- IWorld ----
    public IReadOnlySet<string> Facts => _facts;
    public IReadOnlyList<Tool> AllTools => _tools;
    public IReadOnlyList<Tool> AvailableTools => _tools.Where(t => !(t.Id == "reply" && _facts.Contains(Grounded))).ToList();
    public int Ticks { get; private set; }
    public bool Has(string fact) => _facts.Contains(fact);
    public Tool? Tool(string id) => _tools.FirstOrDefault(t => t.Id == id);
    public bool EffectsLanded(Tool tool) => tool.Grants.All(_facts.Contains);

    public ActionOutcome Act(string toolId, string? payload = null)
    {
        Ticks++;
        var tool = Tool(toolId);
        if (tool is null) return new ActionOutcome(toolId, false, [], "unknown tool");
        var missing = tool.Requires.Where(r => !_facts.Contains(r)).ToList();
        if (missing.Count > 0) return new ActionOutcome(toolId, false, [], $"preconditions unmet: {string.Join(", ", missing)}");

        switch (toolId)
        {
            case "read_day_context":
            {
                var r = _oracle.Run("day_context", null, _user);
                DayContext = r.Result;
                return Land(tool, Compact(r.Result));
            }
            case "recall":
            {
                string q = (payload ?? "").Trim();
                var hits = _notebook?.All.Where(b => b.Active && !_notebook.IsQuarantined(b.Key) && (b.Value.Contains(q, StringComparison.OrdinalIgnoreCase) || b.Key.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    .Take(5).Select(b => $"- {b.Value}").ToList() ?? [];
                return Land(tool, hits.Count == 0 ? "nothing in the notebook about that; don't guess" : string.Join("\n", hits));
            }
            case "reply":
            {
                string text = (payload ?? "").Trim();
                if (text.Length == 0) return new ActionOutcome(toolId, false, [], "empty reply");
                var problems = Grounding.Check(text, Evidence, _notebook);
                if (problems.Count == 0 && PreChecked.TryGetValue(text, out var guesses))
                {
                    LastSymbolGrounded = text; // grounded in the draws, held back only for guesses: the fallback
                    problems.AddRange(guesses);
                }
                if (problems.Count > 0)
                {
                    CriticNotes.AddRange(problems);
                    return new ActionOutcome(toolId, false, [], "not sent; the critic found: " + string.Join("; ", problems));
                }
                Reply = text;
                return Land(tool, "sent");
            }
            default:
            {
                JsonObject? args = null;
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    try { args = JsonNode.Parse(payload) as JsonObject; }
                    catch (System.Text.Json.JsonException) { return new ActionOutcome(toolId, false, [], "arguments were not valid JSON"); }
                }
                Reading r;
                try { r = _oracle.Run(toolId, args, _user); }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return new ActionOutcome(toolId, false, [], e.Message); }
                if (r.Locked) return new ActionOutcome(toolId, false, [], $"the {r.School} school is locked for this person; say so in character and offer what you can read");
                Readings.Add(r);
                Evidence.Add(r);
                return Land(tool, r.ToolOutput());
            }
        }
    }

    private ActionOutcome Land(Tool tool, string observation)
    {
        _facts.UnionWith(tool.Grants);
        return new ActionOutcome(tool.Id, true, tool.Grants, observation);
    }

    /// <summary>The day context without the long explanations, to keep the model's input small.</summary>
    private static string Compact(JsonObject ctx)
    {
        var c = (JsonObject)ctx.DeepClone();
        c.Remove("explain");
        return c.ToJsonString();
    }
}
