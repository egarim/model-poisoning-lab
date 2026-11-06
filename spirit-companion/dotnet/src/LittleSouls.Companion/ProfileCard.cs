using System.Globalization;
using System.Text;

namespace LittleSouls.Companion;

/// <summary>
/// The compact text about the person that the daemon reads every turn (about 300 tokens). Only active, unquarantined
/// beliefs appear; what may appear grows with the bond stage, like Creatura's stages gate what an agent may do.
/// </summary>
public static class ProfileCard
{
    public const int MaxChars = 1200;

    public static string Render(Notebook nb, int stage, DateTimeOffset now, string? personName = null)
    {
        var sb = new StringBuilder();
        IEnumerable<PersonBelief> Of(BeliefKind k) => nb.All
            .Where(b => b.Kind == k && b.Active && !nb.IsQuarantined(b.Key))
            .OrderByDescending(b => b.Confidence).ThenByDescending(b => b.LastSeen);

        void Section(string title, IEnumerable<string> lines)
        {
            var l = lines.ToList();
            if (l.Count == 0) return;
            sb.Append(title).Append('\n');
            foreach (var x in l) sb.Append("- ").Append(x).Append('\n');
        }

        sb.Append("About ").Append(personName ?? "them").Append(" (only what they told you or confirmed; never invent more):\n");
        Section("Facts", Of(BeliefKind.Fact).Take(6).Select(b => b.Value));
        Section("People", Of(BeliefKind.Person).Take(6).Select(b => b.Value));
        Section("How they like to be spoken to", Of(BeliefKind.Style).Take(3).Select(b => b.Value));
        Section("Goals", Of(BeliefKind.Goal).Take(3).Select(b => b.Value));
        if (stage >= 3)
            Section("Open threads (follow up gently)", Of(BeliefKind.Thread).OrderBy(b => b.DueAt ?? DateTimeOffset.MaxValue).Take(4)
                .Select(b => b.DueAt is { } d ? $"{b.Value} (due {d.ToString("ddd d MMM", CultureInfo.InvariantCulture)}{(d <= now ? ", ask about it" : "")})" : b.Value));
        if (stage >= 4)
            Section("Patterns you noticed (observations, never diagnoses)", Of(BeliefKind.Pattern).Take(3).Select(b => b.Value));
        if (nb.OpenQuestions.Count > 0)
            sb.Append("One thing you're unsure about; ask only if it comes up naturally: ").Append(nb.OpenQuestions[0]).Append('\n');

        string s = sb.ToString();
        return s.Length <= MaxChars ? s : s[..MaxChars].TrimEnd() + "\n…";
    }
}
