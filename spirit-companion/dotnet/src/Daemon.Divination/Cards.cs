using System.Text.Json.Nodes;

namespace Daemon.Divination;

/// <summary>Tarot: the 78-card deck, reversals, six spreads, a reflection question with every card.</summary>
public static class Tarot
{
    private sealed record Card(string Name, string Upright, string Reversed, string Question, string Arcana, string? Suit, string? Element);

    private static readonly Lazy<List<Card>> DeckCache = new(BuildDeck);

    public static IReadOnlyList<string> Spreads => Data.Get("tarot")["spreads"]!.AsObject().Select(p => p.Key).ToList();

    public static int DeckSize => DeckCache.Value.Count;

    /// <summary>Every card's name in English (the canonical names the critic checks).</summary>
    public static IReadOnlyList<string> CardNames => DeckCache.Value.Select(c => c.Name).ToList();

    private static List<Card> BuildDeck()
    {
        var d = Data.Get("tarot");
        var deck = d["major"]!.AsArray().Select(c => new Card(Data.S(c!["name"]), Data.S(c["upright"]), Data.S(c["reversed"]), Data.S(c["question"]), "major", null, null)).ToList();
        string template = Data.S(d["question_template"]);
        foreach (var (suit, cards) in d["minor"]!.AsObject())
        {
            var info = d["suits"]![suit]!;
            foreach (var c in cards!.AsArray())
            {
                string rank = Data.S(c!["rank"]);
                string q = template.Replace("{domain}", Data.S(info["domain"])).Replace("{theme}", Data.S(d["rank_themes"]![rank]));
                deck.Add(new Card($"{rank} of {suit}", Data.S(c["upright"]), Data.S(c["reversed"]), q, "minor", suit, Data.S(info["element"])));
            }
        }
        return deck;
    }

    public static JsonObject Draw(DrawRng rng, string spread = "single", bool reversals = true)
    {
        var positions = Data.Get("tarot")["spreads"]![spread]?.AsArray().Select(Data.S).ToList()
            ?? throw new ArgumentException($"unknown tarot spread '{spread}'");
        var cards = rng.Choose(DeckCache.Value, positions.Count);
        var arr = new JsonArray();
        for (int i = 0; i < cards.Count; i++)
        {
            var c = cards[i];
            bool rev = reversals && rng.Chance(0.3);
            var o = new JsonObject
            {
                ["position"] = positions[i],
                ["card"] = c.Name,
                ["reversed"] = rev,
                ["meaning"] = rev ? c.Reversed : c.Upright,
                ["question"] = c.Question,
                ["arcana"] = c.Arcana,
            };
            if (c.Suit is not null) { o["suit"] = c.Suit; o["element"] = c.Element; }
            arr.Add(o);
        }
        return new JsonObject { ["method"] = "tarot", ["spread"] = spread, ["cards"] = arr };
    }
}

/// <summary>Lenormand (baralho cigano in Brazil): 36 cards, read as sentences, no reversals.</summary>
public static class Lenormand
{
    public static IReadOnlyList<string> Spreads => Data.Get("lenormand")["spreads"]!.AsObject().Select(p => p.Key).ToList();

    public static IReadOnlyList<string> CardNames => Data.Get("lenormand")["cards"]!.AsArray().Select(c => Data.S(c!["name"])).ToList();

    public static JsonObject Draw(DrawRng rng, string spread = "three")
    {
        var d = Data.Get("lenormand");
        var positions = d["spreads"]![spread]?.AsArray().Select(Data.S).ToList()
            ?? throw new ArgumentException($"unknown Lenormand spread '{spread}'");
        var deck = d["cards"]!.AsArray().Select(c => c!).ToList();
        var cards = rng.Choose(deck, positions.Count);
        var arr = new JsonArray();
        for (int i = 0; i < cards.Count; i++)
            arr.Add(new JsonObject
            {
                ["position"] = positions[i],
                ["number"] = cards[i]["number"]!.GetValue<int>(),
                ["card"] = Data.S(cards[i]["name"]),
                ["meaning"] = Data.S(cards[i]["meaning"]),
            });
        return new JsonObject
        {
            ["method"] = "lenormand", ["spread"] = spread, ["cards"] = arr,
            ["how_to_read"] = "Read the cards in order as one sentence: the first is the subject, the next ones colour it.",
        };
    }
}

/// <summary>Elder Futhark runes: 24 runes; the nine symmetric ones never fall reversed.</summary>
public static class Runes
{
    public static IReadOnlyList<string> Names => Data.Get("runes")["runes"]!.AsArray().Select(r => Data.S(r![0])).ToList();

    public static JsonObject Cast(DrawRng rng, int count = 1)
    {
        var d = Data.Get("runes");
        var positions = d["positions"]![count.ToString()]?.AsArray().Select(Data.S).ToList()
            ?? throw new ArgumentException("runes are cast as 1 or 3");
        var all = d["runes"]!.AsArray().Select(r => r!.AsArray()).ToList();
        var picks = rng.Choose(all, positions.Count);
        var arr = new JsonArray();
        for (int i = 0; i < picks.Count; i++)
        {
            var r = picks[i];
            string? rev = r[3]?.GetValue<string>();
            bool isRev = rev is not null && rng.Chance(0.3);
            arr.Add(new JsonObject
            {
                ["position"] = positions[i],
                ["rune"] = Data.S(r[0]),
                ["symbol"] = Data.S(r[1]),
                ["reversed"] = isRev,
                ["meaning"] = isRev ? rev : Data.S(r[2]),
            });
        }
        return new JsonObject { ["method"] = "runes", ["runes"] = arr };
    }
}
