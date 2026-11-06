using System.Text.RegularExpressions;

namespace LittleSouls.Daemon;

/// <summary>
/// The crisis filter, which runs before the model and before the world exists: a crisis message never reaches the model.
/// A keyword first pass (from the prototype's safety.py, plus English); replace or back it with a classifier before launch.
/// The replies are fixed and must be reviewed by someone local to each country, with verified numbers.
/// </summary>
public static class CrisisFilter
{
    private static readonly Dictionary<string, Regex> Patterns = new()
    {
        ["es"] = Rx(@"no quiero (seguir )?viv(ir|iendo)|ya no quiero (estar aqu[ií]|existir)|quiero morir(me)?|matarme|suicid|quitarme la vida|hacerme da[ñn]o|cortarme|no vale la pena vivir"),
        ["pt"] = Rx(@"n[ãa]o quero (mais )?(viver|continuar vivendo|existir)|quero morrer|me matar|suic[ií]d|tirar (a )?minha (pr[óo]pria )?vida|me machucar|me cortar"),
        ["ru"] = Rx(@"не хочу (больше )?жить|хочу умереть|покончить с собой|суицид|убить себя|порезать себя|навредить себе|нет смысла жить"),
        ["en"] = Rx(@"(don'?t|do not) want to (live|be alive|exist)|want to die|kill myself|suicid|end my life|hurt myself|self[- ]harm|cut myself"),
    };

    /// <summary>Lines by country; a verified local number goes first. Unverified countries fall back to the emergency number.</summary>
    private static readonly Dictionary<string, string> Lines = new()
    {
        ["MX"] = "Línea de la Vida: 800 911 2000 (24 h), or 911",
        ["BR"] = "CVV: 188 (24 h), or 192",
        ["SV"] = "911",
        ["RU"] = "112",
    };

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True when the message matches a crisis pattern in any of the supported languages.</summary>
    public static bool IsCrisis(string message) => Patterns.Values.Any(r => r.IsMatch(message));

    public static string Reply(string? country, string? companionName) =>
        $"I'm here, and what you feel matters. I'm not going to read anything right now; I want you to be safe. " +
        $"Please contact a crisis line now: {(country is not null && Lines.TryGetValue(country, out var l) ? l : "your local emergency number")}. " +
        $"If you can, write to someone you trust right now. {(companionName is null ? "I'm" : companionName + " is")} staying here with you.";
}
