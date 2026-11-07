using System.Text.Json.Nodes;
using CosineKitty;
using Daemon.Divination;
using Xunit;

namespace Daemon.Divination.Tests;

public class RngTests
{
    [Fact]
    public void Matches_the_reference_implementation()
    {
        // Values from an independent Python implementation of xoshiro128** seeded by SHA-256("golden").
        var r = new DrawRng("golden");
        Assert.Equal(new uint[] { 2577225142, 185732501, 807188805, 2788649259, 3452105903 }, Enumerable.Range(0, 5).Select(_ => r.NextUInt()).ToArray());
        r = new DrawRng("golden");
        Assert.Equal(new[] { 76, 71, 33, 45, 77, 59, 36, 53 }, Enumerable.Range(0, 8).Select(_ => r.Below(78)).ToArray());
    }

    [Fact]
    public void Below_is_uniform()
    {
        var r = new DrawRng("uniform");
        var counts = new int[7];
        const int n = 70_000;
        for (int i = 0; i < n; i++) counts[r.Below(7)]++;
        double chi = counts.Sum(c => Math.Pow(c - n / 7.0, 2) / (n / 7.0));
        Assert.True(chi < 22.5, $"chi-square {chi:F1} (6 degrees of freedom, p≈0.001 at 22.5)");
    }

    [Fact]
    public void Choose_gives_distinct_items_and_repeats_with_the_seed()
    {
        var items = Enumerable.Range(0, 78).ToList();
        var a = new DrawRng("s").Choose(items, 10);
        Assert.Equal(10, a.Distinct().Count());
        Assert.Equal(a, new DrawRng("s").Choose(items, 10));
        Assert.NotEqual(a, new DrawRng("t").Choose(items, 10));
    }

    [Fact]
    public void Fresh_seeds_differ()
    {
        Assert.NotEqual(DrawRng.Fresh().Seed, DrawRng.Fresh().Seed);
    }
}

public class CardTests
{
    [Fact]
    public void Tarot_deck_has_78_cards_each_with_a_question()
    {
        Assert.Equal(78, Tarot.DeckSize);
        var all = Tarot.Draw(new DrawRng("q"), "celtic_cross")["cards"]!.AsArray();
        Assert.Equal(10, all.Count);
        Assert.Equal(10, all.Select(c => c!["card"]!.GetValue<string>()).Distinct().Count());
        Assert.All(all, c => Assert.False(string.IsNullOrWhiteSpace(c!["question"]!.GetValue<string>())));
    }

    [Theory]
    [InlineData("single", 1)]
    [InlineData("three", 3)]
    [InlineData("choice", 3)]
    [InlineData("relationship", 5)]
    [InlineData("week", 7)]
    [InlineData("celtic_cross", 10)]
    public void Tarot_spreads_have_their_positions(string spread, int count) =>
        Assert.Equal(count, Tarot.Draw(new DrawRng(spread), spread)["cards"]!.AsArray().Count);

    [Fact]
    public void Tarot_reversals_come_up_about_30_percent()
    {
        var r = new DrawRng("rev");
        int rev = 0, n = 0;
        for (int i = 0; i < 3000; i++)
            foreach (var c in Tarot.Draw(r, "three")["cards"]!.AsArray()) { n++; if (c!["reversed"]!.GetValue<bool>()) rev++; }
        Assert.InRange(rev / (double)n, 0.27, 0.33);
    }

    [Fact]
    public void Lenormand_has_36_cards_and_three_spreads()
    {
        var r = Lenormand.Draw(new DrawRng("l"), "nine");
        Assert.Equal(9, r["cards"]!.AsArray().Count);
        Assert.Equal(new[] { "nine", "single", "three" }, Lenormand.Spreads.Order());
        var seen = new HashSet<int>();
        var g = new DrawRng("all");
        for (int i = 0; i < 400; i++) foreach (var c in Lenormand.Draw(g, "nine")["cards"]!.AsArray()) seen.Add(c!["number"]!.GetValue<int>());
        Assert.Equal(36, seen.Count);
    }

    [Fact]
    public void Symmetric_runes_never_fall_reversed()
    {
        string[] symmetric = ["Gebo", "Hagalaz", "Nauthiz", "Isa", "Jera", "Eihwaz", "Sowilo", "Ingwaz", "Dagaz"];
        var r = new DrawRng("runes");
        for (int i = 0; i < 2000; i++)
            foreach (var x in Runes.Cast(r, 3)["runes"]!.AsArray())
                if (symmetric.Contains(x!["rune"]!.GetValue<string>())) Assert.False(x["reversed"]!.GetValue<bool>());
    }
}

/// <summary>The names the person reads: every tarot card, Lenormand card and rune has one in en, es, pt and ru; draws keep the canonical English.</summary>
public class NameTests
{
    private static readonly string[] Languages = ["en", "es", "pt", "ru"];

    [Fact]
    public void Every_tarot_card_has_a_name_in_every_language()
    {
        Assert.Equal(78, Tarot.CardNames.Count);
        foreach (var lang in Languages)
        {
            var names = Tarot.CardNames.Select(c => Tarot.LocalName(c, lang)).ToList();
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(78, names.Distinct().Count());                                                  // no two cards share a name
            if (lang != "en") Assert.All(Tarot.CardNames, c => Assert.NotEqual(c, Tarot.LocalName(c, lang))); // every card is translated
        }
        Assert.All(Tarot.CardNames, c => Assert.Equal(Languages.Order(), Tarot.NamesOf(c).Keys.Order()));
        Assert.Empty(Tarot.NamesOf("Not a card"));
    }

    [Theory]
    [InlineData("The Tower", "es", "La Torre")]
    [InlineData("The Tower", "pt", "A Torre")]
    [InlineData("The Tower", "ru", "Башня")]
    [InlineData("The Tower", "pt-BR", "A Torre")]   // a locale, not only a language
    [InlineData("The Tower", "en", "The Tower")]
    [InlineData("The Tower", null, "The Tower")]
    [InlineData("The Tower", "fr", "The Tower")]    // no French names: the canonical name
    [InlineData("Wheel of Fortune", "pt", "A Roda da Fortuna")]
    [InlineData("Ten of Swords", "pt", "Dez de Espadas")]
    [InlineData("Ten of Swords", "es", "Diez de Espadas")]
    [InlineData("Ten of Swords", "ru", "Десятка Мечей")]
    [InlineData("Ace of Pentacles", "es", "As de Oros")]
    [InlineData("Ace of Pentacles", "pt", "Ás de Ouros")]
    [InlineData("Knight of Cups", "pt", "Cavaleiro de Copas")]
    [InlineData("Page of Wands", "ru", "Паж Жезлов")]
    [InlineData("Queen of Pentacles", "ru", "Королева Пентаклей")]
    [InlineData("Not a card", "pt", "Not a card")]
    public void Tarot_cards_are_named_in_the_persons_language(string card, string? language, string expected) =>
        Assert.Equal(expected, Tarot.LocalName(card, language));

    [Fact]
    public void Every_lenormand_card_has_a_name_in_every_language()
    {
        Assert.Equal(36, Lenormand.CardNames.Count);
        foreach (var lang in Languages)
        {
            var names = Lenormand.CardNames.Select(c => Lenormand.LocalName(c, lang)).ToList();
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(36, names.Distinct().Count());
            if (lang != "en") Assert.All(Lenormand.CardNames, c => Assert.NotEqual(c, Lenormand.LocalName(c, lang)));
        }
        Assert.All(Lenormand.CardNames, c => Assert.Equal(Languages.Order(), Lenormand.NamesOf(c).Keys.Order()));
    }

    [Theory]
    [InlineData("Rider", "es", "El Jinete")]
    [InlineData("Rider", "pt", "O Cavaleiro")]
    [InlineData("Rider", "ru", "Всадник")]
    [InlineData("Tower", "pt", "A Torre")]
    [InlineData("Mice", "ru", "Мыши")]
    [InlineData("Rider", "en", "Rider")]
    [InlineData("Rider", null, "Rider")]
    public void Lenormand_cards_are_named_in_the_persons_language(string card, string? language, string expected) =>
        Assert.Equal(expected, Lenormand.LocalName(card, language));

    [Fact]
    public void Every_rune_has_a_name_in_every_language()
    {
        Assert.Equal(24, Runes.Names.Count);
        foreach (var lang in Languages)
        {
            var names = Runes.Names.Select(r => Runes.LocalName(r, lang)).ToList();
            Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
            Assert.Equal(24, names.Distinct().Count());
        }
        Assert.All(Runes.Names, r => Assert.NotEqual(r, Runes.LocalName(r, "ru"))); // Russian transliterates every rune
        Assert.All(Runes.Names, r => Assert.Equal(Languages.Order(), Runes.NamesOf(r).Keys.Order()));
    }

    [Theory]
    [InlineData("Isa", "ru", "Иса")]
    [InlineData("Isa", "pt", "Isa")]        // Spanish and Portuguese keep the reconstructed names
    [InlineData("Raidho", "es", "Raido")]   // in the spelling their rune books use
    [InlineData("Othala", "ru", "Одал")]
    [InlineData("Isa", null, "Isa")]
    public void Runes_are_named_in_the_persons_language(string rune, string? language, string expected) =>
        Assert.Equal(expected, Runes.LocalName(rune, language));

    [Theory]
    [InlineData("en", "reversed", "merkstave")]
    [InlineData("es", "invertida", "invertida")]
    [InlineData("pt", "invertida", "invertida")]
    [InlineData("ru", "перевёрнутая", "перевёрнутая")]
    [InlineData(null, "reversed", "merkstave")]
    public void The_word_beside_a_reversed_card_or_rune_exists_in_every_language(string? language, string card, string rune)
    {
        Assert.Equal(card, Tarot.ReversedLabel(language));
        Assert.Equal(rune, Runes.ReversedLabel(language));
    }

    [Fact]
    public void Draws_keep_the_canonical_English_names_the_critic_checks()
    {
        var g = new DrawRng("names");
        Assert.All(Tarot.Draw(g, "celtic_cross")["cards"]!.AsArray(), c => Assert.Contains(c!["card"]!.GetValue<string>(), Tarot.CardNames));
        Assert.All(Lenormand.Draw(g, "nine")["cards"]!.AsArray(), c => Assert.Contains(c!["card"]!.GetValue<string>(), Lenormand.CardNames));
        Assert.All(Runes.Cast(g, 3)["runes"]!.AsArray(), r => Assert.Contains(r!["rune"]!.GetValue<string>(), Runes.Names));
    }
}

public class IChingTests
{
    [Fact]
    public void King_Wen_table_and_known_hexagrams()
    {
        Assert.Equal(1, IChing.Hexagram([1, 1, 1, 1, 1, 1]).Number);
        Assert.Equal(2, IChing.Hexagram([0, 0, 0, 0, 0, 0]).Number);
        Assert.Equal(11, IChing.Hexagram([1, 1, 1, 0, 0, 0]).Number); // Peace: earth over heaven
        Assert.Equal(63, IChing.Hexagram([1, 0, 1, 0, 1, 0]).Number); // After Completion: water over fire
        Assert.Equal(3, IChing.Hexagram([1, 0, 0, 0, 1, 0]).Number);  // Difficulty: water over thunder
        var all = new HashSet<int>();
        for (int i = 0; i < 64; i++) all.Add(IChing.Hexagram(Enumerable.Range(0, 6).Select(b => (i >> b) & 1).ToArray()).Number);
        Assert.Equal(64, all.Count);
    }

    [Theory]
    [InlineData("coins", 1 / 8.0, 3 / 8.0, 3 / 8.0, 1 / 8.0)]
    [InlineData("yarrow", 1 / 16.0, 5 / 16.0, 7 / 16.0, 3 / 16.0)]
    public void Line_odds_follow_the_method(string method, double p6, double p7, double p8, double p9)
    {
        var r = new DrawRng(method);
        var c = new Dictionary<int, int> { [6] = 0, [7] = 0, [8] = 0, [9] = 0 };
        const int casts = 20_000;
        for (int i = 0; i < casts; i++) foreach (var v in IChing.Lines(r, method)) c[v]++;
        double n = casts * 6;
        Assert.InRange(c[6] / n, p6 - 0.01, p6 + 0.01);
        Assert.InRange(c[7] / n, p7 - 0.01, p7 + 0.01);
        Assert.InRange(c[8] / n, p8 - 0.01, p8 + 0.01);
        Assert.InRange(c[9] / n, p9 - 0.01, p9 + 0.01);
    }

    [Fact]
    public void Changing_lines_give_the_becoming_hexagram()
    {
        for (int s = 0; s < 50; s++)
        {
            var o = IChing.Cast(new DrawRng("c" + s));
            Assert.Equal(o["changing_lines"]!.AsArray().Count > 0, o["becoming"] is not null);
        }
    }
}

public class NumerologyTests
{
    [Fact]
    public void Life_path_and_personal_numbers()
    {
        var o = Numerology.Personal(new DateOnly(1995, 3, 14), new DateOnly(2026, 11, 5));
        Assert.Equal(5, o["life_path"]!["number"]!.GetValue<int>());
        Assert.Equal(11, Numerology.Reduce(29)); // master numbers are kept
        Assert.Equal(22, Numerology.Reduce(22));
    }

    [Fact]
    public void Name_numbers_for_latin_and_cyrillic()
    {
        // V4 A1 L3 E5 R9 I9 A1 = 32 -> 5
        Assert.Equal(5, Numerology.Personal(new DateOnly(1997, 7, 22), new DateOnly(2026, 11, 5), "Valeria")["expression"]!["number"]!.GetValue<int>());
        int Expr(string n) => Numerology.Personal(new DateOnly(2000, 1, 1), new DateOnly(2026, 1, 1), n)["expression"]!["number"]!.GetValue<int>();
        Assert.Equal(Expr("Lucia"), Expr("Lucía")); // accents ignored
        Assert.Equal(5, Expr("Анна")); // А1 Н6 Н6 А1 = 14 -> 5
    }
}

public class SkyTests
{
    private static AstroTime T(int y, int mo, int d, int h = 12, int mi = 0) => new(new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc));

    [Fact]
    public void Known_moons()
    {
        var full = Sky.Now(new DateTimeOffset(2024, 4, 23, 23, 49, 0, TimeSpan.Zero), "UTC");
        Assert.Equal("Full Moon", full["moon"]!["phase"]!.GetValue<string>());
        Assert.Equal("Scorpio", full["moon"]!["sign"]!["sign"]!.GetValue<string>());
        var nw = Sky.Now(new DateTimeOffset(2024, 4, 8, 18, 21, 0, TimeSpan.Zero), "UTC");
        Assert.Equal("New Moon", nw["moon"]!["phase"]!.GetValue<string>());
        Assert.Equal("Aries", nw["moon"]!["sign"]!["sign"]!.GetValue<string>());
    }

    [Fact]
    public void Next_full_moon_in_local_time()
    {
        var o = Sky.Now(new DateTimeOffset(2024, 4, 20, 12, 0, 0, TimeSpan.Zero), "America/Mexico_City");
        var full = o["next_moon_phases"]!.AsArray().First(p => p!["phase"]!.GetValue<string>() == "Full Moon")!;
        var at = DateTime.Parse(full["local_time"]!.GetValue<string>());
        Assert.InRange((at - new DateTime(2024, 4, 23, 17, 49, 0)).TotalMinutes, -3, 3); // 23:49 UTC = 17:49 in Mexico City (UTC-6)
    }

    [Theory]
    [InlineData(2024, 4, 10, true)]   // Mercury retrograde 1–25 April 2024
    [InlineData(2024, 5, 10, false)]
    [InlineData(2024, 8, 15, true)]   // 5–28 August 2024
    [InlineData(2024, 10, 1, false)]
    public void Mercury_retrograde(int y, int m, int d, bool expected) => Assert.Equal(expected, Sky.IsRetrograde(Body.Mercury, T(y, m, d)));

    [Fact]
    public void Mars_retrograde_winter_2024_2025() => Assert.True(Sky.IsRetrograde(Body.Mars, T(2025, 1, 15)));

    [Theory]
    [InlineData(20.67, -103.35, 2024, 6, 21, 3)]   // Guadalajara
    [InlineData(-23.55, -46.63, 2025, 1, 10, 14)]  // São Paulo
    [InlineData(55.75, 37.62, 2025, 3, 1, 20)]     // Moscow
    [InlineData(0.0, 0.0, 2026, 11, 5, 0)]
    public void Ascendant_is_on_the_eastern_horizon(double lat, double lon, int y, int m, int d, int h)
    {
        var t = T(y, m, d, h);
        double asc = Sky.Ascendant(t, lat, lon);
        var (ra, dec) = Sky.EclipticToEquator(asc, t);
        var hor = Astronomy.Horizon(t, new Observer(lat, lon, 0), ra, dec, Refraction.None);
        Assert.InRange(hor.altitude, -0.1, 0.1);
        Assert.InRange(hor.azimuth, 0, 180); // east of the meridian
    }

    [Fact]
    public void Sun_signs_from_astronomy()
    {
        Assert.Equal("Pisces", Sky.SignOf(Sky.Longitude(Body.Sun, T(1995, 3, 14))));
        Assert.Equal("Capricorn", Sky.SignOf(Sky.Longitude(Body.Sun, T(1990, 1, 5))));
        Assert.Equal("Cancer", Sky.SignOf(Sky.Longitude(Body.Sun, T(1997, 7, 22))));
    }

    [Fact]
    public void Location_gives_rise_and_set_times()
    {
        var o = Sky.Now(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero), "America/Mexico_City", 20.67, -103.35);
        var w = o["where_you_are"]!.AsObject();
        foreach (var k in new[] { "moonrise", "moonset", "sunrise", "sunset" }) Assert.NotNull(w[k]);
    }

    [Fact]
    public void Birth_chart_with_and_without_time()
    {
        var full = Sky.BirthChart(new DateOnly(1997, 7, 22), new TimeOnly(6, 30), "America/Mexico_City", 20.67, -103.35);
        Assert.Equal(10, full["planets"]!.AsArray().Count);
        Assert.NotNull(full["ascendant"]);
        Assert.All(full["planets"]!.AsArray(), p => Assert.InRange(p!["house"]!.GetValue<int>(), 1, 12));
        var noon = Sky.BirthChart(new DateOnly(1997, 7, 22), null, "America/Mexico_City");
        Assert.Null(noon["ascendant"]);
        Assert.NotNull(noon["note"]);
    }

    [Fact]
    public void Maya_day_sign() => Assert.Equal("4 Ajaw", Maya.DaySign(new DateOnly(2012, 12, 21))["day"]!.GetValue<string>());
}

public class OracleTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly byte[] Secret = "test-secret"u8.ToArray();
    private static readonly UserContext Valeria = new("u1", "America/Mexico_City") { Name = "Valeria", BirthDate = new DateOnly(1997, 7, 22) };

    [Fact]
    public void Receipts_verify_and_catch_tampering()
    {
        var oracle = new Oracle(Secret);
        var r = oracle.Run("draw_tarot", new JsonObject { ["spread"] = "three" }, Valeria);
        Assert.True(oracle.Verify(r.Receipt!, Valeria));
        Assert.False(oracle.Verify(r.Receipt! with { ResultHash = new string('0', 64) }, Valeria));
        Assert.False(oracle.Verify(r.Receipt! with { Seed = "another" }, Valeria));
        var n = oracle.Run("personal_numerology", null, Valeria);
        Assert.Equal("computed", n.Receipt!.SeedKind);
        Assert.True(oracle.Verify(n.Receipt, Valeria));
    }

    [Fact]
    public void Daily_card_follows_the_users_own_day()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 5, 3, 0, 0, TimeSpan.Zero)); // 21:00 on 4 Nov in Mexico City
        var oracle = new Oracle(Secret, clock);
        var daily = new JsonObject { ["daily"] = true };
        string Card() => oracle.Run("draw_tarot", daily, Valeria).Result["cards"]![0]!["card"]!.GetValue<string>();
        string evening = Card();
        clock.Now = new DateTimeOffset(2026, 11, 5, 5, 59, 0, TimeSpan.Zero); // 23:59 on 4 Nov local: same day
        Assert.Equal(evening, Card());
        string? next = null;
        for (int d = 1; d <= 5 && next is null; d++) // the next local days give other cards (5 tries makes a repeat by chance negligible)
        {
            clock.Now = new DateTimeOffset(2026, 11, 5, 6, 1, 0, TimeSpan.Zero).AddDays(d - 1);
            if (Card() != evening) next = Card();
        }
        Assert.NotNull(next);
        Assert.NotEqual(oracle.Run("draw_tarot", daily, Valeria with { UserId = "u2" }).Receipt!.Seed, oracle.Run("draw_tarot", daily, Valeria).Receipt!.Seed);
    }

    [Fact]
    public void Locked_schools_are_refused_and_hidden()
    {
        var free = Valeria with { Schools = new HashSet<string> { "tarot", "astrology" } };
        var r = new Oracle(Secret).Run("draw_lenormand", null, free);
        Assert.True(r.Locked);
        Assert.Equal("lenormand", r.Result["school"]!.GetValue<string>());
        var names = ToolCatalog.For(free.Schools).Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(new[] { "birth_chart", "day_context", "draw_tarot", "history_reading", "moon_circles", "sky_now" }, names.Order());
        Assert.Equal(11, ToolCatalog.All().Count);
    }

    [Fact]
    public void Every_tool_runs()
    {
        var oracle = new Oracle(Secret);
        var me = Valeria with { Latitude = 20.67, Longitude = -103.35, BirthTime = new TimeOnly(6, 30), BirthLatitude = 20.67, BirthLongitude = -103.35 };
        foreach (var tool in Oracle.SchoolOf.Keys)
        {
            var r = oracle.Run(tool, null, me);
            Assert.False(r.Locked);
            Assert.False(string.IsNullOrEmpty(r.ToolOutput()));
        }
    }
}
