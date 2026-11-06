using System.Text.RegularExpressions;
using Daemon.Divination;
using LittleSouls.Companion;

namespace LittleSouls.Daemon;

/// <summary>What this turn really holds: the draws made (from receipts) and the people in the notebook.</summary>
public sealed class TurnEvidence
{
    public HashSet<string> Tarot { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Lenormand { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Runes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<int> Hexagrams { get; } = [];
    public HashSet<string> HexagramNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>The person's message and the notebook: a card or name already there may be referred back to.</summary>
    public string Mentioned { get; set; } = "";
    /// <summary>The person's own name, which the reply may always use.</summary>
    public string? PersonName { get; set; }

    public bool WasMentioned(string name) => name.Length > 1 && Mentioned.Contains(name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Record what a reading produced.</summary>
    public void Add(Reading r)
    {
        if (r.Locked) return;
        var o = r.Result;
        foreach (var c in o["cards"]?.AsArray() ?? [])
        {
            string name = c!["card"]!.GetValue<string>();
            if (r.Method == "draw_lenormand") Lenormand.Add(name); else Tarot.Add(name);
        }
        foreach (var x in o["runes"]?.AsArray() ?? []) Runes.Add(x!["rune"]!.GetValue<string>());
        foreach (var k in new[] { "primary", "becoming" })
            if (o[k] is { } h) { Hexagrams.Add(h["number"]!.GetValue<int>()); HexagramNames.Add(h["name"]!.GetValue<string>()); }
        if (o["the_decks_card"] is { } hc) Tarot.Add(hc["card"]!.GetValue<string>());
    }
}

/// <summary>
/// The grounding critic (Creatura's "judge by effect", applied to words): every card, rune, hexagram and person the reply
/// names must be in this turn's receipts or the notebook. The daemon tags what it names, [[card:The Tower]],
/// [[lenormand:Rider]], [[rune:Isa]], [[hexagram:46]], [[person:Camila]], in canonical English names whatever the reply's
/// language; untagged English tarot and rune names are checked too. The app turns tags into chips and plain words.
/// </summary>
public static partial class Grounding
{
    [GeneratedRegex(@"\[\[([a-z_]+):([^\]]+)\]\]", RegexOptions.IgnoreCase)]
    private static partial Regex Tag();

    private static readonly Lazy<List<string>> TarotNames = new(() => Tarot.CardNames.OrderByDescending(n => n.Length).ToList());
    private static readonly Lazy<List<string>> RuneNames = new(() => Runes.Names.ToList());

    /// <summary>The problems with a reply, each a short sentence the model can act on. Empty means grounded.</summary>
    public static List<string> Check(string reply, TurnEvidence ev, Notebook? notebook)
    {
        var problems = new List<string>();
        foreach (Match m in Tag().Matches(reply))
        {
            string kind = m.Groups[1].Value.ToLowerInvariant(), v = m.Groups[2].Value.Trim();
            bool ok = kind switch
            {
                "card" => ev.Tarot.Contains(v) || ev.WasMentioned(v),
                "lenormand" => ev.Lenormand.Contains(v) || ev.WasMentioned(v),
                "rune" => ev.Runes.Contains(v) || ev.WasMentioned(v),
                "hexagram" => (int.TryParse(v, out int n) ? ev.Hexagrams.Contains(n) : ev.HexagramNames.Contains(v)) || ev.WasMentioned(v),
                "person" => string.Equals(v, ev.PersonName, StringComparison.OrdinalIgnoreCase)
                    || notebook is not null && notebook.All.Any(b => b.Kind == BeliefKind.Person && b.Active && !notebook.IsQuarantined(b.Key) && b.Value.Contains(v, StringComparison.OrdinalIgnoreCase)),
                _ => true, // other tags (festivity, moon…) are display only
            };
            if (!ok) problems.Add(kind == "person"
                ? $"{v} is not someone the person has told you about; don't name them"
                : $"{v} was not drawn this turn; name only what the tools returned, or draw first");
        }
        // untagged canonical names (English replies, or a model that forgot to tag)
        string untagged = Tag().Replace(reply, " ");
        foreach (var name in TarotNames.Value)
        {
            bool multiword = name.Contains(' ');
            // multi-word names in any case; single-word ones ("Death", "Strength") capitalised and spoken of as a card (see Looks)
            var rx = new Regex(@"\b" + Regex.Escape(name) + @"\b", multiword ? RegexOptions.IgnoreCase : RegexOptions.None);
            if (rx.IsMatch(untagged) && !ev.Tarot.Contains(name) && !ev.WasMentioned(name) && (multiword || Looks(untagged, name)))
                problems.Add($"{name} was not drawn this turn; name only what the tools returned, or draw first");
        }
        foreach (var rune in RuneNames.Value)
            if (Regex.IsMatch(untagged, @"\b" + Regex.Escape(rune) + @"\b") && !ev.Runes.Contains(rune) && !ev.WasMentioned(rune))
                problems.Add($"the rune {rune} was not cast this turn");
        // a model never gives a phone number: only verified crisis lines may appear
        foreach (var n in CrisisFilter.UnverifiedNumbers(reply))
            problems.Add($"{n} is not a verified number; never give a phone number yourself");
        return problems.Distinct().ToList();
    }

    /// <summary>A single-word card ("Death", "Strength") counts only when spoken of as a card, not as an ordinary word.</summary>
    private static bool Looks(string text, string name) =>
        Regex.IsMatch(text, @"(card|drew|draw|drawn|carta|cartas|arcano)\W+(\w+\W+){0,3}" + Regex.Escape(name) + @"\b", RegexOptions.IgnoreCase) ||
        Regex.IsMatch(text, @"\b" + Regex.Escape(name) + @"\W+(\w+\W+){0,2}(card|reversed|upright)", RegexOptions.IgnoreCase);

    /// <summary>The reply as the person reads it: tags become their plain names.</summary>
    public static string Display(string reply) => Tag().Replace(reply, m => m.Groups[2].Value.Trim());
}
