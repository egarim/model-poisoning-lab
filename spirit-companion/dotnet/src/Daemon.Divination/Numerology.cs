using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Daemon.Divination;

/// <summary>
/// Pythagorean numerology: life path and personal year, month and day from the birth date; expression, soul urge
/// and personality from the name, for Latin names (accents ignored, so "Lucía" counts as "Lucia") and Cyrillic names.
/// Master numbers 11, 22 and 33 are kept.
/// </summary>
public static class Numerology
{
    private const string Cyrillic = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
    private const string CyrillicVowels = "аеёиоуыэюя";

    public static int Reduce(int n)
    {
        while (n > 9 && n != 11 && n != 22 && n != 33)
            n = n.ToString(CultureInfo.InvariantCulture).Sum(ch => ch - '0');
        return n;
    }

    /// <summary>The letter's value and whether it is a vowel, or null for anything that is not a letter we count.</summary>
    public static (int Value, bool Vowel)? Letter(char ch)
    {
        ch = char.ToLowerInvariant(ch);
        int ci = Cyrillic.IndexOf(ch);
        if (ci >= 0) return (ci % 9 + 1, CyrillicVowels.Contains(ch));
        string bare = ch.ToString().Normalize(NormalizationForm.FormD);
        char b = bare[0];
        if (b is < 'a' or > 'z') return null;
        return ((b - 'a') % 9 + 1, "aeiou".Contains(b));
    }

    private static (int All, int Vowels, int Consonants) NameSums(string name)
    {
        int all = 0, vow = 0, con = 0;
        foreach (char ch in name)
        {
            if (Letter(ch) is not { } l) continue;
            all += l.Value;
            if (l.Vowel) vow += l.Value; else con += l.Value;
        }
        return (all, vow, con);
    }

    public static JsonObject Personal(DateOnly birth, DateOnly today, string? name = null)
    {
        var m = Data.Get("calendars")["numerology"]!;
        JsonObject Num(int n) => new() { ["number"] = n, ["meaning"] = Data.S(m[n.ToString(CultureInfo.InvariantCulture)]) };
        int lifePath = Reduce(Reduce(birth.Month) + Reduce(birth.Day) + Reduce(birth.Year));
        int py = Reduce(Reduce(birth.Month) + Reduce(birth.Day) + Reduce(today.Year));
        int pm = Reduce(py + today.Month);
        int pd = Reduce(pm + Reduce(today.Day));
        var o = new JsonObject
        {
            ["method"] = "numerology",
            ["personal_day"] = Num(pd),
            ["personal_month"] = Num(pm),
            ["personal_year"] = Num(py),
            ["life_path"] = Num(lifePath),
        };
        if (!string.IsNullOrWhiteSpace(name))
        {
            var (all, vow, con) = NameSums(name);
            if (all > 0)
            {
                o["name"] = name;
                o["expression"] = Num(Reduce(all));
                if (vow > 0) o["soul_urge"] = Num(Reduce(vow));
                if (con > 0) o["personality"] = Num(Reduce(con));
            }
        }
        return o;
    }
}
