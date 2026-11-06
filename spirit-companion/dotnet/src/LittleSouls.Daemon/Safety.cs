using System.Text.RegularExpressions;

namespace LittleSouls.Daemon;

/// <summary>A crisis line as checked: the number people dial, the emergency number, and how it was verified.</summary>
/// <param name="Status">"official": confirmed on the government's own page; "secondary": several independent sources agree,
/// the official site could not be reached. Re-check every row each quarter (issue #1).</param>
public sealed record CrisisLine(string Country, string Name, string Number, string Emergency, string Status, string Source, DateOnly Checked);

/// <summary>The verified crisis lines. A model never supplies a number: the bench caught it inventing one.</summary>
public static class CrisisLines
{
    private static readonly DateOnly Checked = new(2026, 11, 6);

    public static readonly IReadOnlyDictionary<string, CrisisLine> ByCountry = new Dictionary<string, CrisisLine>
    {
        ["MX"] = new("MX", "Línea de la Vida", "800 911 2000", "911", "secondary", "press and university directories 2025–2026; the gob.mx page returned 404", Checked),
        ["SV"] = new("SV", "MINSAL", "131, opción 4", "911", "secondary", "UNICEF LAC mental-health guide; La Página (MINSAL, ICRC, Salvadoran Red Cross)", Checked),
        ["BR"] = new("BR", "CVV", "188", "192", "secondary", "Ministry of Health and CVV agreement (CONASS); CVV report Q1 2026", Checked),
        ["AR"] = new("AR", "Línea Nacional de Salud Mental", "0800-999-0091", "911", "official", "https://www.argentina.gob.ar/node/513585", Checked),
        ["CO"] = new("CO", "Línea Nacional de Salud Mental", "106", "123", "official", "MinSalud, Directorio nacional de líneas de salud mental, agosto 2025", Checked),
        ["PE"] = new("PE", "Línea 113 (MINSA)", "113, opción 5", "106", "official", "https://www.gob.pe/institucion/minsa/noticias/1018284", Checked),
        ["CL"] = new("CL", "Línea de prevención del suicidio", "*4141", "131", "official", "https://www.minsal.cl/linea-de-atencion-4141-no-estas-solo-no-estas-sola/", Checked),
        ["RU"] = new("RU", "Экстренная психологическая помощь МЧС", "+7 495 989-50-50", "112", "secondary", "several listings; psi.mchs.gov.ru returned 503", Checked),
    };

    /// <summary>Every number a reply may contain: the lines and the emergency numbers, digits only.</summary>
    public static readonly IReadOnlySet<string> AllowedDigits = ByCountry.Values
        .SelectMany(l => new[] { l.Number, l.Emergency }).Select(Digits).Where(d => d.Length > 0).ToHashSet();

    public static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

    /// <summary>The line for a country, or null where none is verified yet (then only the emergency number is given).</summary>
    public static CrisisLine? For(string? country) => country is not null && ByCountry.TryGetValue(country, out var l) ? l : null;
}

/// <summary>
/// The crisis filter, which runs before the model and before the world exists: a crisis message never reaches the model.
/// A keyword first pass (from the prototype's safety.py, plus English and the indirect phrasings the B5 bench found);
/// <see cref="ICrisisClassifier"/> is the second pass. The replies are fixed text in the person's language.
/// </summary>
public static class CrisisFilter
{
    private static readonly Dictionary<string, Regex> Patterns = new()
    {
        ["es"] = Rx(@"no quiero (seguir )?viv(ir|iendo)|ya no quiero (estar aqu[ií]|existir)|quiero morir(me)?|matarme|suicid|quitarme la vida|hacerme da[ñn]o|cortarme|no vale la pena vivir|(estar[ií]an|estar[ií]a|vivir[ií]an) mejor sin m[ií]|dormir(me)? y no despertar"),
        ["pt"] = Rx(@"n[ãa]o quero (mais )?(viver|continuar vivendo|existir)|quero morrer|me matar|suic[ií]d|tirar (a )?minha (pr[óo]pria )?vida|me machucar|me cortar|(ficaria|ficariam|estaria|estariam|viveriam) melhor sem mim|dormir e n[ãa]o acordar"),
        ["ru"] = Rx(@"не хочу (больше )?жить|хочу умереть|покончить с собой|суицид|убить себя|порезать себя|навредить себе|нет смысла жить|(лучше|легче|проще) без меня|уснуть и не проснуться"),
        ["en"] = Rx(@"(don'?t|do not) want to (live|be alive|exist)|want to die|kill myself|suicid|end my life|hurt myself|self[- ]harm|cut myself|better off without me|(sleep|go to sleep) and (never|not) wake up"),
    };

    private static Regex Rx(string p) => new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>True when the message matches a crisis pattern in any of the supported languages.</summary>
    public static bool IsCrisis(string message) => Patterns.Values.Any(r => r.IsMatch(message));

    /// <summary>The person's language for the fixed reply: the script, then words, then the country.</summary>
    public static string Language(string message, string? country)
    {
        if (Regex.IsMatch(message, @"\p{IsCyrillic}")) return "ru";
        foreach (var (lang, rx) in Patterns) if (rx.IsMatch(message)) return lang;
        if (Regex.IsMatch(message, @"\b(não|você|vocês|eu|mim|nada|tô|estou|muito|sem)\b", RegexOptions.IgnoreCase) && Regex.IsMatch(message, @"ã|õ|ç|\bnão\b|\bvocê\b", RegexOptions.IgnoreCase)) return "pt";
        if (Regex.IsMatch(message, @"[ñ¿¡]|\b(que|yo|estoy|nada|mí|porque|ya)\b", RegexOptions.IgnoreCase)) return "es";
        return country switch { "BR" => "pt", "RU" => "ru", "MX" or "SV" or "AR" or "CO" or "PE" or "CL" => "es", _ => "en" };
    }

    public static string Reply(string? country, string? companionName, string language = "en")
    {
        var line = CrisisLines.For(country);
        string name = string.IsNullOrWhiteSpace(companionName) ? "" : companionName;
        return language switch
        {
            "es" => $"Te escucho, y lo que sientes importa. Ahora no voy a leer nada: quiero que estés a salvo. " +
                    (line is null ? "Por favor llama ahora al número de emergencias de tu país. " : $"Por favor llama ahora a {line.Name}: {line.Number}, o al {line.Emergency} si estás en peligro. ") +
                    $"Y si puedes, escríbele ahora a alguien de confianza. {(name.Length > 0 ? name + " sigue" : "Sigo")} aquí contigo.",
            "pt" => $"Eu te escuto, e o que você sente importa. Agora não vou ler nada: quero que você fique em segurança. " +
                    (line is null ? "Por favor ligue agora para o número de emergência do seu país. " : $"Por favor ligue agora para o {line.Name}: {line.Number}, ou {line.Emergency} se estiver em perigo. ") +
                    $"E se puder, chame agora alguém de confiança. {(name.Length > 0 ? name + " continua" : "Eu continuo")} aqui com você.",
            "ru" => $"Я тебя слышу, и то, что ты чувствуешь, важно. Сейчас я не буду ничего гадать: мне важно, чтобы с тобой всё было в порядке. " +
                    (line is null ? "Пожалуйста, позвони сейчас в экстренную службу. " : $"Пожалуйста, позвони сейчас: {line.Name}, {line.Number}, или {line.Emergency}, если ты в опасности. ") +
                    $"И если можешь, напиши сейчас кому-то, кому доверяешь. {(name.Length > 0 ? name + " рядом" : "Я рядом")}.",
            _ => $"I'm here, and what you feel matters. I'm not going to read anything right now; I want you to be safe. " +
                 (line is null ? "Please contact your local emergency number now. " : $"Please contact {line.Name} now: {line.Number}, or {line.Emergency} if you are in danger. ") +
                 $"If you can, write to someone you trust right now. {(name.Length > 0 ? name + " is" : "I'm")} staying here with you.",
        };
    }

    /// <summary>
    /// Phone numbers in a model's reply that are not verified crisis lines: a model must never give a number. Anything
    /// that looks like a phone number (seven digits or more, or a short code after a word like "call") is checked.
    /// </summary>
    public static List<string> UnverifiedNumbers(string reply)
    {
        var found = new List<string>();
        var spans = new List<(int Start, int End)>();
        foreach (Match m in Regex.Matches(reply, @"\+?\*?\d[\d\s\-().]{5,}\d"))
        {
            if (CrisisLines.Digits(m.Value).Length < 7) continue;
            spans.Add((m.Index, m.Index + m.Length));
            if (!CrisisLines.AllowedDigits.Contains(CrisisLines.Digits(m.Value))) found.Add(m.Value.Trim());
        }
        foreach (Match m in Regex.Matches(reply, @"(?i)\b(call|dial|llama|llamar|marca|liga|ligue|ligar|disque|звони|позвони|позвоните|телефон|número|numero|línea|linha)\b\D{0,20}?(\*?\d{3,6})\b"))
        {
            var g = m.Groups[2];
            if (spans.Any(sp => g.Index >= sp.Start && g.Index < sp.End)) continue; // part of a longer number, checked above
            if (!CrisisLines.AllowedDigits.Contains(CrisisLines.Digits(g.Value))) found.Add(g.Value);
        }
        return found.Distinct().ToList();
    }
}

/// <summary>What the second pass decided about a message.</summary>
public sealed record CrisisVerdict(bool Crisis, string Reason, bool Failed = false);

/// <summary>
/// The second pass after the keywords: a classifier that reads indirect messages ("everyone would be better off without
/// me"). A message it marks gets the fixed reply; the model never answers it.
/// </summary>
public interface ICrisisClassifier
{
    Task<CrisisVerdict> ClassifyAsync(string message, CancellationToken ct);
}
