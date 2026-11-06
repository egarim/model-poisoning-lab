namespace LittleSouls.Companion;

/// <summary>The traits as stored: values and how much evidence each has.</summary>
public sealed record TraitsState(Dictionary<Trait, double> Values, Dictionary<Trait, int> Evidence);

/// <summary>The four traits from the summoning: energy, social, mind (heart 0 … head 1), spirit (traditional 0 … rebel 1).</summary>
public enum Trait { Energy, Social, Mind, Spirit }

/// <summary>
/// The person's traits as slow-moving estimates. They start from the summoning answers and move a small step per piece of
/// evidence, never more than <see cref="MaxShiftPerSession"/> in one session: one bad day never rewrites who someone is.
/// </summary>
public sealed class Traits
{
    public const double Step = 0.05, MaxShiftPerSession = 0.1;

    private readonly Dictionary<Trait, double> _value = new() { [Trait.Energy] = 0.5, [Trait.Social] = 0.5, [Trait.Mind] = 0.5, [Trait.Spirit] = 0.5 };
    private readonly Dictionary<Trait, double> _sessionStart = new();
    private readonly Dictionary<Trait, int> _evidence = new() { [Trait.Energy] = 0, [Trait.Social] = 0, [Trait.Mind] = 0, [Trait.Spirit] = 0 };

    public double this[Trait t] => _value[t];

    /// <summary>Confidence grows with evidence: 0 at the summoning, near 1 after many sessions.</summary>
    public double Confidence(Trait t) => 1 - 1 / (1 + _evidence[t] / 10.0);

    /// <summary>Start from the summoning answers.</summary>
    public void Seed(double energy, double social, double mind, double spirit)
    {
        _value[Trait.Energy] = Clamp(energy); _value[Trait.Social] = Clamp(social);
        _value[Trait.Mind] = Clamp(mind); _value[Trait.Spirit] = Clamp(spirit);
    }

    /// <summary>One piece of evidence pointing at a value between 0 and 1.</summary>
    public void Observe(Trait t, double pointsTo)
    {
        if (!_sessionStart.ContainsKey(t)) _sessionStart[t] = _value[t];
        double start = _sessionStart[t];
        double next = _value[t] + Step * (Clamp(pointsTo) - _value[t]);
        _value[t] = Math.Clamp(next, start - MaxShiftPerSession, start + MaxShiftPerSession);
        _evidence[t]++;
    }

    /// <summary>A new session: the per-session cap starts again.</summary>
    public void EndSession() => _sessionStart.Clear();

    public TraitsState ToState() => new(_value.ToDictionary(kv => kv.Key, kv => kv.Value), _evidence.ToDictionary(kv => kv.Key, kv => kv.Value));

    public void Restore(TraitsState s)
    {
        foreach (var (k, v) in s.Values) _value[k] = Clamp(v);
        foreach (var (k, v) in s.Evidence) _evidence[k] = Math.Max(0, v);
        _sessionStart.Clear();
    }

    public IReadOnlyDictionary<string, double> Snapshot() => _value.ToDictionary(kv => kv.Key.ToString().ToLowerInvariant(), kv => Math.Round(kv.Value, 3));

    private static double Clamp(double v) => Math.Clamp(v, 0, 1);
}

/// <summary>A small private prediction about the person, linked to the beliefs it rests on.</summary>
public sealed record PersonPrediction(string Id, string Text, IReadOnlyList<string> BeliefKeys, DateTimeOffset DueAt)
{
    public bool? Correct { get; set; }
}

/// <summary>
/// Creatura's <c>PredictionTracker</c> for a person: the daemon predicts how things will go for the person ("the interview
/// will make her nervous"). When the outcome is known, a hit supports the beliefs behind it and a miss contradicts them.
/// </summary>
public sealed class PersonPredictions
{
    private readonly Dictionary<string, PersonPrediction> _all = new(StringComparer.Ordinal);
    private readonly Queue<bool> _recent = new();

    public IReadOnlyCollection<PersonPrediction> All => _all.Values;
    public int Total { get; private set; }
    public int CorrectCount { get; private set; }
    public double RecentAccuracy => _recent.Count == 0 ? 0 : _recent.Count(x => x) / (double)_recent.Count;

    public PersonPrediction Make(string text, IEnumerable<string> beliefKeys, DateTimeOffset dueAt)
    {
        var p = new PersonPrediction(Guid.NewGuid().ToString("n")[..12], text, beliefKeys.Select(Notebook.Normalize).ToList(), dueAt);
        _all[p.Id] = p;
        return p;
    }

    public IEnumerable<PersonPrediction> Due(DateTimeOffset now) => _all.Values.Where(p => p.Correct is null && p.DueAt <= now);

    /// <summary>Record the outcome and move the linked beliefs.</summary>
    public bool Resolve(Notebook nb, string id, bool correct)
    {
        if (!_all.TryGetValue(id, out var p) || p.Correct is not null) return false;
        p.Correct = correct;
        Total++; if (correct) CorrectCount++;
        _recent.Enqueue(correct); if (_recent.Count > 8) _recent.Dequeue();
        foreach (var k in p.BeliefKeys)
            if (nb.Get(k) is { } b) { if (correct) b.Supported++; else b.Contradicted++; }
        return true;
    }
}
