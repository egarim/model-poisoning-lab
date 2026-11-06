namespace LittleSouls.Companion;

/// <summary>What the gate did with one session's proposals.</summary>
public sealed record GateReport(
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Reinforced,
    IReadOnlyList<string> Replaced,
    IReadOnlyList<string> Quarantined,
    IReadOnlyList<string> AwaitingConsent,
    IReadOnlyList<(string Key, string Why)> Dropped);

/// <summary>
/// Bach's learning filter (Creatura's <c>CoherenceAudit</c>) for a person: a belief joins only if it keeps the notebook
/// coherent. The model proposes; this code decides. The rules are in docs/design-daemon-brain.md.
/// </summary>
public static class CoherenceGate
{
    /// <summary>How many sessions a contradiction stays out of use before the daemon may decide again.</summary>
    public const int QuarantineSessions = 3;

    public static GateReport Apply(Notebook nb, IEnumerable<Proposal> proposals, string sessionId, DateTimeOffset at)
    {
        List<string> added = [], reinforced = [], replaced = [], quarantined = [], consent = [];
        List<(string, string)> dropped = [];

        foreach (var p in proposals)
        {
            string key = Notebook.Normalize(p.Key);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(p.Value) && !p.Close) { dropped.Add((p.Key, "empty")); continue; }

            // rule 4: sensitive topics are never inferred, and need consent when stated
            if (p.Sensitivity != Sensitivity.None)
            {
                if (p.Source == BeliefSource.Inferred) { dropped.Add((key, "sensitive topics are never inferred")); continue; }
                nb.HoldForConsent(p); consent.Add(key); continue;
            }

            // rule 5: forgotten stays forgotten, unless the person tells it again
            if (nb.WasForgotten(key) && p.Source == BeliefSource.Inferred) { dropped.Add((key, "forgotten by the person")); continue; }

            var ev = new Evidence(sessionId, Trim(p.Quote), at);
            var b = nb.Get(key);

            if (b is null)
            {
                if (p.Close) { dropped.Add((key, "nothing open to close")); continue; }
                b = new PersonBelief { Key = key, Kind = p.Kind, Value = p.Value.Trim(), Source = p.Source, Supported = 1, FirstSeen = at, LastSeen = at, DueAt = p.DueAt };
                b.Evidence.Add(ev);
                nb.Put(b);
                added.Add(key); // rule 1 (stated: active now) and rule 2 (inferred: a candidate until seen again)
                continue;
            }

            if (p.Close && b.Kind == BeliefKind.Thread) { b.Closed = true; b.LastSeen = at; b.Evidence.Add(ev); reinforced.Add(key); continue; }

            bool same = string.Equals(b.Value.Trim(), p.Value.Trim(), StringComparison.OrdinalIgnoreCase);
            if (same)
            {
                // the same session cannot count twice as independent evidence
                if (!b.Evidence.Any(e => e.SessionId == sessionId)) b.Supported++;
                if (p.Source > b.Source) b.Source = p.Source;
                if (p.DueAt is not null) b.DueAt = p.DueAt;
                b.LastSeen = at; b.Evidence.Add(ev);
                reinforced.Add(key);
            }
            else if (p.Source >= BeliefSource.Stated)
            {
                // rule 1: the person's own words replace the old value
                b.History.Add(b.Value);
                b.Value = p.Value.Trim(); b.Source = p.Source; b.Supported = 1; b.Contradicted = 0;
                b.DueAt = p.DueAt ?? b.DueAt; b.Closed = false; b.LastSeen = at; b.Evidence.Add(ev);
                replaced.Add(key);
            }
            else
            {
                // rule 3: an inference never overwrites; the clash is quarantined and becomes a question
                b.Contradicted++; b.LastSeen = at; b.Evidence.Add(ev);
                nb.Quarantine(key, QuarantineSessions);
                string q = $"{b.Key}: \"{b.Value}\" or \"{p.Value.Trim()}\"?";
                if (!nb.OpenQuestions.Contains(q)) nb.OpenQuestions.Add(q);
                quarantined.Add(key);
            }
        }
        return new GateReport(added, reinforced, replaced, quarantined, consent, dropped);
    }

    private static string Trim(string s) => s.Length <= 160 ? s : s[..157] + "...";
}
