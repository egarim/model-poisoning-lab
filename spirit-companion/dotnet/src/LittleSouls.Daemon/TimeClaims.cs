using System.Text.RegularExpressions;

namespace LittleSouls.Daemon;

/// <summary>
/// How long and how often, said about the person ("the third month you've put it off", "уже три года", "la cuarta vez"):
/// a reply may say it only when the notebook or the person's message holds the same number and unit, in any of the four
/// languages ("three years" in the notebook allows "третий год"). The B5 bench found the shadow archetype inventing these.
/// </summary>
public static class TimeClaims
{
    private static readonly (Regex Rx, int N)[] Numbers =
    [
        (W(@"one|a|an|un|una|um|uma|один|одн\w*|first|primer\w*|primeir\w*|перв\w*"), 1),
        (W(@"two|dos|dois|duas|два|две|second|segund\w*|втор\w*"), 2),
        (W(@"three|tres|três|три|third|tercer\w*|terceir\w*|трет\w*"), 3),
        (W(@"four|cuatro|quatro|четыре|fourth|cuart\w*|quart\w*|четв[её]рт\w*"), 4),
        (W(@"five|cinco|пять|fifth|quint\w*|пят\w*"), 5),
        (W(@"six|seis|шесть|sixth|sext\w*|шест\w*"), 6),
        (W(@"seven|siete|sete|семь|seventh|séptim\w*|sétim\w*|седьм\w*"), 7),
        (W(@"ten|diez|dez|десять|tenth|décim\w*|десят\w*"), 10),
    ];

    private static readonly (Regex Rx, string Unit)[] Units =
    [
        (W(@"times?|vez|veces|vezes|раз|раза"), "time"),
        (W(@"days?|d[ií]as?|день|дня|дней|сутки"), "day"),
        (W(@"nights?|noches?|noites?|ноч\w*"), "night"),
        (W(@"weeks?|semanas?|недел\w*"), "week"),
        (W(@"months?|mes|meses|mês|месяц\w*"), "month"),
        (W(@"years?|años?|anos?|год|года|годы|лет"), "year"),
    ];

    private const string Num = @"\d+|a|an|one|two|three|four|five|six|seven|ten|first|second|third|fourth|fifth|sixth|un|una|dos|tres|cuatro|cinco|seis|siete|diez|um|uma|dois|duas|três|quatro|sete|dez|один|одн\w*|два|две|три|четыре|пять|шесть|семь|десять|primer\w*|segund\w*|tercer\w*|cuart\w*|quint\w*|sext\w*|primeir\w*|terceir\w*|quart\w*|перв\w*|втор\w*|трет\w*|четв[её]рт\w*|пят\w*|шест\w*";
    private const string Unit = @"times?|vez|veces|vezes|раз|раза|days?|d[ií]as?|день|дня|дней|nights?|noches?|noites?|ноч\w*|weeks?|semanas?|недел\w*|months?|mes|meses|mês|месяц\w*|years?|años?|anos?|год|года|годы|лет";

    /// <summary>Any number with a unit: what the notebook and the message hold.</summary>
    private static readonly Regex Any = new($@"(?<n>{Num})\s+(?<u>{Unit})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Claims about the person: an ordinal ("the third month"), a duration after a marker ("for three years",
    /// "hace dos meses", "уже три года", "two months ago"), or "который месяц" (for the umpteenth month).</summary>
    private static readonly Regex[] Claims =
    [
        new($@"\b(?<n>second|third|fourth|fifth|sixth|segund\w*|tercer\w*|cuart\w*|quint\w*|sext\w*|terceir\w*|quart\w*|втор\w*|трет\w*|четв[её]рт\w*|пят\w*|шест\w*)\s+(?<u>{Unit})\b", RegexOptions.IgnoreCase),
        new($@"\b(for|already|hace|desde hace|llevas|lleva|há|faz|já|уже|вот уже|целых)\s+(?<n>{Num})\s+(?<u>{Unit})\b", RegexOptions.IgnoreCase),
        new($@"\b(?<n>{Num})\s+(?<u>{Unit})\s+ago\b", RegexOptions.IgnoreCase),
        new($@"\b(?<n>который|которую|какой|какую)\s+(?<u>{Unit})\b", RegexOptions.IgnoreCase),
        // a unit with no number after a marker: "уже год", "already a year" is one year
        new($@"\b(уже|вот уже|целый|целую)\s+(?<u>год|месяц|неделю|день)\b", RegexOptions.IgnoreCase),
    ];

    private static Regex W(string p) => new($@"^(?:{p})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static (int? N, string? Unit) Canon(string n, string u) =>
        (int.TryParse(n, out int d) ? d : Numbers.FirstOrDefault(x => x.Rx.IsMatch(n)).N is int k and > 0 ? k : null,
         Units.FirstOrDefault(x => x.Rx.IsMatch(u)).Unit);

    /// <summary>The claims in the reply that nothing in <paramref name="mentioned"/> supports.</summary>
    public static List<string> Unsupported(string reply, string mentioned)
    {
        var known = Any.Matches(mentioned).Select(m => Canon(m.Groups["n"].Value, m.Groups["u"].Value)).Where(c => c.Unit is not null).ToHashSet();
        var found = new List<string>();
        foreach (var rx in Claims)
            foreach (Match m in rx.Matches(reply))
            {
                var (n, unit) = m.Groups["n"].Success ? Canon(m.Groups["n"].Value, m.Groups["u"].Value) : (1, Canon("1", m.Groups["u"].Value).Unit);
                if (unit is null) continue;
                // "который месяц" (for the umpteenth month) has no number to check: it is always a guess
                bool ok = n is not null && known.Contains((n, unit));
                if (!ok) found.Add(m.Value.Trim());
            }
        return found.Distinct().ToList();
    }
}
