using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LittleSouls.Companion;

namespace SingularFramework.Apps.LittleSouls.Jobs;

/// <summary>
/// A notebook as Singular documents: one <c>notebook</c> document per belief (what the Grimoire lists, corrects and
/// deletes), and the notebook's bookkeeping (sessions, forgotten fingerprints, open questions, pending consents, traits)
/// in the <c>notebook</c> field of the person's <c>daemon</c> document. Pure: the job reads and writes.
/// </summary>
public static class NotebookDocuments
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Fields the app adds to a belief document that the notebook itself doesn't keep; preserved on save.</summary>
    private static readonly string[] AppFields = ["followedUpAt", "consentedAt"];

    public static Notebook Load(string personId, IEnumerable<(string Key, JsonObject Doc)> beliefs, JsonObject? daemon)
    {
        var meta = daemon?["notebook"] as JsonObject;
        var state = new NotebookState(
            personId,
            meta?["session"]?.GetValue<int>() ?? 0,
            beliefs.Select(b => Belief(b.Doc)).ToList(),
            meta?["forgotten"]?.Deserialize<List<string>>(Json) ?? [],
            meta?["openQuestions"]?.Deserialize<List<string>>(Json) ?? [],
            meta?["awaitingConsent"]?.Deserialize<List<Proposal>>(Json) ?? [],
            meta?["traits"]?.Deserialize<TraitsState>(Json));
        return Notebook.FromState(state);
    }

    /// <summary>The documents to write: one per belief (merged with what is stored, so app fields survive) and the daemon's notebook field.</summary>
    public static (Dictionary<string, JsonObject> Beliefs, JsonObject Meta) Save(Notebook nb, IReadOnlyDictionary<string, JsonObject> stored)
    {
        var s = nb.ToState();
        var docs = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var b in s.Beliefs)
        {
            var doc = Doc(b);
            if (stored.TryGetValue(b.Key, out var old))
                foreach (var f in AppFields)
                    if (old[f] is { } v && doc[f] is null) doc[f] = v.DeepClone();
            docs[b.Key] = doc;
        }
        var meta = new JsonObject
        {
            ["session"] = s.Session,
            ["forgotten"] = JsonSerializer.SerializeToNode(s.ForgottenFingerprints, Json),
            ["openQuestions"] = JsonSerializer.SerializeToNode(s.OpenQuestions, Json),
            ["awaitingConsent"] = JsonSerializer.SerializeToNode(s.AwaitingConsent, Json),
            ["traits"] = s.Traits is null ? null : JsonSerializer.SerializeToNode(s.Traits, Json),
        };
        return (docs, meta);
    }

    /// <summary>The fingerprint a forgotten key leaves (the same one the notebook keeps).</summary>
    public static string Fingerprint(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Notebook.Normalize(key)))).ToLowerInvariant();

    private static JsonObject Doc(BeliefState b) => new()
    {
        ["kind"] = b.Kind.ToString(), ["key"] = b.Key, ["value"] = b.Value, ["source"] = b.Source.ToString(),
        ["sensitivity"] = b.Sensitivity.ToString(), ["supported"] = b.Supported, ["contradicted"] = b.Contradicted,
        ["firstSeen"] = b.FirstSeen.ToString("O"), ["lastSeen"] = b.LastSeen.ToString("O"),
        ["dueAt"] = b.DueAt?.ToString("O"), ["closed"] = b.Closed, ["quarantinedUntilSession"] = b.QuarantinedUntilSession,
        ["history"] = new JsonArray(b.History.TakeLast(20).Select(h => (JsonNode)h).ToArray()),
        ["evidence"] = new JsonArray(b.Evidence.TakeLast(20).Select(e => (JsonNode)new JsonObject
            { ["session"] = e.SessionId, ["quote"] = e.Quote, ["at"] = e.At.ToString("O") }).ToArray()),
    };

    private static BeliefState Belief(JsonObject d)
    {
        T E<T>(string k, T def) where T : struct, Enum => Enum.TryParse<T>(d[k]?.GetValue<string>(), out var v) ? v : def;
        DateTimeOffset? D(string k) => d[k] is JsonValue v && v.TryGetValue<string>(out var s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;
        return new BeliefState(
            d["key"]!.GetValue<string>(), E("kind", BeliefKind.Fact), d["value"]?.GetValue<string>() ?? "", E("source", BeliefSource.Inferred),
            E("sensitivity", Sensitivity.None), d["supported"]?.GetValue<int>() ?? 0, d["contradicted"]?.GetValue<int>() ?? 0,
            (d["evidence"] as JsonArray ?? []).Select(e => new Evidence(e!["session"]?.GetValue<string>() ?? "", e["quote"]?.GetValue<string>() ?? "", D2(e["at"]))).ToList(),
            (d["history"] as JsonArray ?? []).Select(h => h!.GetValue<string>()).ToList(),
            D("firstSeen") ?? DateTimeOffset.MinValue, D("lastSeen") ?? DateTimeOffset.MinValue, D("dueAt"),
            d["closed"]?.GetValue<bool>() ?? false, d["quarantinedUntilSession"]?.GetValue<int?>());
    }

    private static DateTimeOffset D2(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s) && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTimeOffset.MinValue;
}

/// <summary>
/// Where the end-of-session proposals come from: the model reads the conversation and proposes notebook changes
/// (slice B3, DeepSeek in JSON mode). Until then <see cref="NoProposals"/>: sessions still close, so quarantines lapse.
/// </summary>
public interface IProposalSource
{
    Task<IReadOnlyList<Proposal>> ProposeAsync(JsonObject conversation, string profileCard, CancellationToken ct);
}

public sealed class NoProposals : IProposalSource
{
    public Task<IReadOnlyList<Proposal>> ProposeAsync(JsonObject conversation, string profileCard, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Proposal>>([]);
}
