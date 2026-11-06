using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LittleSouls.Companion;

/// <summary>
/// What the daemon knows about one person. It belongs to the person: they can read it (<see cref="Export"/>), correct
/// it, confirm it and forget any of it. Sessions are counted so quarantine can lapse after a few conversations.
/// </summary>
public sealed class Notebook
{
    private readonly Dictionary<string, PersonBelief> _beliefs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _quarantinedUntil = new(StringComparer.Ordinal);
    private readonly HashSet<string> _forgotten = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Proposal> _awaitingConsent = new(StringComparer.Ordinal);

    public Notebook(string personId) => PersonId = personId;

    public string PersonId { get; }

    /// <summary>How many sessions have closed. Quarantine is counted in sessions.</summary>
    public int Session { get; private set; }

    public Traits Traits { get; } = new();

    public PersonPredictions Predictions { get; } = new();

    /// <summary>Contradictions the daemon may ask about, gently, one at a time.</summary>
    public List<string> OpenQuestions { get; } = [];

    public IReadOnlyCollection<PersonBelief> All => _beliefs.Values;

    public IReadOnlyCollection<Proposal> AwaitingConsent => _awaitingConsent.Values;

    public PersonBelief? Get(string key) => _beliefs.GetValueOrDefault(Normalize(key));

    public static string Normalize(string key) => key.Trim().ToLowerInvariant();

    public bool IsQuarantined(string key) => _quarantinedUntil.TryGetValue(Normalize(key), out int until) && Session < until;

    /// <summary>A forgotten key, hashed: the notebook remembers that it must not re-learn it, not what it was.</summary>
    internal bool WasForgotten(string key) => _forgotten.Contains(Fingerprint(key));

    private static string Fingerprint(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(key))));

    internal void Put(PersonBelief b) => _beliefs[Normalize(b.Key)] = b;

    internal void Quarantine(string key, int sessions) => _quarantinedUntil[Normalize(key)] = Session + sessions;

    internal void HoldForConsent(Proposal p) => _awaitingConsent[Normalize(p.Key)] = p;

    /// <summary>Close the session: quarantines lapse once enough sessions have passed.</summary>
    public void EndSession()
    {
        Session++;
        foreach (var k in _quarantinedUntil.Where(kv => Session >= kv.Value).Select(kv => kv.Key).ToList()) _quarantinedUntil.Remove(k);
        Traits.EndSession();
    }

    // ---- what the person can do with their notebook ----

    /// <summary>"That's true": the belief becomes certain and leaves quarantine.</summary>
    public bool Confirm(string key)
    {
        if (Get(key) is not { } b) return false;
        b.Source = BeliefSource.Confirmed;
        b.Contradicted = 0;
        _quarantinedUntil.Remove(Normalize(key));
        OpenQuestions.RemoveAll(q => q.Contains(b.Key, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    /// <summary>"Not quite": the person's own words replace the belief, which outweighs any inference.</summary>
    public bool Correct(string key, string value, DateTimeOffset at)
    {
        if (Get(key) is not { } b) return false;
        if (b.Value != value) b.History.Add(b.Value);
        b.Value = value;
        b.Source = BeliefSource.Confirmed;
        b.Contradicted = 0;
        b.LastSeen = at;
        _quarantinedUntil.Remove(Normalize(key));
        OpenQuestions.RemoveAll(q => q.Contains(b.Key, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    /// <summary>Forget a belief completely. The daemon may not infer it back; only the person can tell it again.</summary>
    public bool Forget(string key)
    {
        string k = Normalize(key);
        _quarantinedUntil.Remove(k);
        _awaitingConsent.Remove(k);
        OpenQuestions.RemoveAll(q => q.Contains(key, StringComparison.OrdinalIgnoreCase));
        _forgotten.Add(Fingerprint(k));
        return _beliefs.Remove(k);
    }

    /// <summary>The person agreed to keep a sensitive thing they stated.</summary>
    public bool GiveConsent(string key, DateTimeOffset at, string sessionId)
    {
        if (!_awaitingConsent.Remove(Normalize(key), out var p)) return false;
        var b = new PersonBelief { Key = p.Key, Kind = p.Kind, Value = p.Value, Source = BeliefSource.Stated, Sensitivity = p.Sensitivity, Supported = 1, FirstSeen = at, LastSeen = at };
        b.Evidence.Add(new Evidence(sessionId, p.Quote, at));
        Put(b);
        return true;
    }

    /// <summary>The person declined: nothing is kept.</summary>
    public bool DeclineConsent(string key) => _awaitingConsent.Remove(Normalize(key));

    /// <summary>Everything the notebook holds about the person, as readable JSON (their right to see it).</summary>
    public string Export() => JsonSerializer.Serialize(new
    {
        person = PersonId,
        sessions = Session,
        beliefs = _beliefs.Values.OrderBy(b => b.Kind).ThenBy(b => b.Key).Select(b => new
        {
            key = b.Key, kind = b.Kind.ToString(), value = b.Value, source = b.Source.ToString(), confidence = Math.Round(b.Confidence, 2),
            active = b.Active, quarantined = IsQuarantined(b.Key), b.History, due = b.DueAt, closed = b.Closed,
            evidence = b.Evidence.Select(e => new { e.SessionId, e.Quote, e.At }),
        }),
        traits = Traits.Snapshot(),
        open_questions = OpenQuestions,
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
