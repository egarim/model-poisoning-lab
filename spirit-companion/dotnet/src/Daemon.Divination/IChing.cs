using System.Text.Json.Nodes;

namespace Daemon.Divination;

/// <summary>
/// The I Ching, by three coins or by yarrow stalks. The two give different odds for changing lines:
/// coins 1/8 old yin, 3/8 young yang, 3/8 young yin, 1/8 old yang; yarrow 1/16, 5/16, 7/16, 3/16.
/// </summary>
public static class IChing
{
    private static readonly string[] Order = ["Qian (Heaven)", "Zhen (Thunder)", "Kan (Water)", "Gen (Mountain)", "Kun (Earth)", "Xun (Wind)", "Li (Fire)", "Dui (Lake)"];

    /// <summary>Line values bottom to top (6, 7, 8 or 9).</summary>
    public static int[] Lines(DrawRng rng, string method)
    {
        var v = new int[6];
        for (int i = 0; i < 6; i++)
        {
            if (method == "yarrow")
            {
                int r = rng.Below(16);
                v[i] = r < 1 ? 6 : r < 6 ? 7 : r < 13 ? 8 : 9;
            }
            else
            {
                int sum = 0;
                for (int c = 0; c < 3; c++) sum += rng.Below(2) == 0 ? 2 : 3;
                v[i] = sum;
            }
        }
        return v;
    }

    /// <summary>King Wen number of a hexagram given as six bits, bottom to top (1 = yang).</summary>
    public static (int Number, string Lower, string Upper) Hexagram(IReadOnlyList<int> bits)
    {
        var d = Data.Get("iching");
        string Trigram(int a, int b, int c)
        {
            foreach (var (name, lines) in d["trigrams"]!.AsObject())
            {
                if (name.StartsWith('_')) continue;
                var l = lines!.AsArray();
                if (l[0]!.GetValue<int>() == a && l[1]!.GetValue<int>() == b && l[2]!.GetValue<int>() == c) return name;
            }
            throw new InvalidOperationException("no trigram");
        }
        string lower = Trigram(bits[0], bits[1], bits[2]), upper = Trigram(bits[3], bits[4], bits[5]);
        int n = d["king_wen_table"]!["rows"]![Array.IndexOf(Order, lower)]![Array.IndexOf(Order, upper)]!.GetValue<int>();
        return (n, lower, upper);
    }

    private static JsonObject Info(IReadOnlyList<int> bits)
    {
        var (n, lower, upper) = Hexagram(bits);
        var h = Data.Get("iching")["hexagrams"]![n - 1]!.AsArray();
        return new JsonObject { ["number"] = n, ["name"] = Data.S(h[0]), ["meaning"] = Data.S(h[1]), ["upper_trigram"] = upper, ["lower_trigram"] = lower };
    }

    public static JsonObject Cast(DrawRng rng, string method = "coins")
    {
        if (method is not ("coins" or "yarrow")) throw new ArgumentException("method is coins or yarrow");
        var values = Lines(rng, method);
        var primary = values.Select(v => v is 7 or 9 ? 1 : 0).ToArray();
        var changing = Enumerable.Range(0, 6).Where(i => values[i] is 6 or 9).Select(i => i + 1).ToArray();
        var o = new JsonObject
        {
            ["method"] = "i_ching",
            ["casting"] = method,
            ["line_values"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()),
            ["primary"] = Info(primary),
            ["changing_lines"] = new JsonArray(changing.Select(v => (JsonNode)v).ToArray()),
        };
        if (changing.Length > 0)
        {
            var becoming = primary.Select((b, i) => changing.Contains(i + 1) ? 1 - b : b).ToArray();
            o["becoming"] = Info(becoming);
            o["note"] = "the situation is moving from the primary hexagram toward 'becoming'";
        }
        return o;
    }
}
