using System.Text.Json.Nodes;
using CosineKitty;
using Daemon.Divination;
using Xunit;

namespace Daemon.Divination.Tests;

public class FestivityTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Theory]
    [InlineData(2024, "2024-03-31", "2024-05-05")]
    [InlineData(2025, "2025-04-20", "2025-04-20")]
    [InlineData(2026, "2026-04-05", "2026-04-12")]
    public void Easter_western_and_orthodox(int y, string western, string orthodox)
    {
        Assert.Equal(DateOnly.Parse(western), Festivities.WesternEaster(y));
        Assert.Equal(DateOnly.Parse(orthodox), Festivities.OrthodoxEaster(y));
    }

    [Theory]
    [InlineData(2024, "2024-02-10")]
    [InlineData(2025, "2025-01-29")]
    [InlineData(2026, "2026-02-17")]
    public void Lunar_new_year(int y, string expected) => Assert.Equal(DateOnly.Parse(expected), Festivities.ChineseNewYear(y));

    [Fact]
    public void Carnival_and_Maslenitsa_2026()
    {
        var br = Festivities.Between(new DateOnly(2026, 2, 17), new DateOnly(2026, 2, 17), "BR", true, Utc);
        Assert.Contains(br, f => f.Id == "carnival" && f.Start == new DateOnly(2026, 2, 13));
        var ru = Festivities.Between(new DateOnly(2026, 2, 16), new DateOnly(2026, 2, 16), "RU", false, Utc);
        Assert.Contains(ru, f => f.Id == "maslenitsa" && f.Start == new DateOnly(2026, 2, 16) && f.End == new DateOnly(2026, 2, 22));
    }

    [Fact]
    public void Solstice_is_named_for_the_hemisphere()
    {
        var day = new DateOnly(2024, 6, 20); // June solstice 2024: 20 June 20:51 UTC
        var north = Festivities.Between(day, day, "MX", false, Utc).Single(f => f.Id == "june_solstice");
        var south = Festivities.Between(day, day, "PE", true, Utc).Single(f => f.Id == "june_solstice");
        Assert.StartsWith("Summer solstice", north.Name);
        Assert.StartsWith("Winter solstice", south.Name);
        Assert.Equal("2024-06-20 20:51", north.Data["exact_local_time"]!.GetValue<string>());
    }

    [Fact]
    public void Festivities_follow_the_country()
    {
        var nov1 = new DateOnly(2026, 11, 1);
        Assert.Contains(Festivities.Between(nov1, nov1, "MX", false, Utc), f => f.Id == "dia_de_muertos");
        var nov2 = new DateOnly(2026, 11, 2);
        var br = Festivities.Between(nov2, nov2, "BR", true, Utc);
        Assert.Contains(br, f => f.Id == "finados");
        Assert.DoesNotContain(br, f => f.Id == "dia_de_muertos");
        var jan10 = new DateOnly(2027, 1, 10);
        Assert.Contains(Festivities.Between(jan10, jan10, "RU", false, Utc), f => f.Id == "svyatki");
    }
}

public class MoonEventTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static DateTimeOffset D(int y, int m, int d) => new(y, m, d, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void August_2023_had_a_super_blue_moon()
    {
        var full = MoonEvents.Phases(D(2023, 7, 30), D(2023, 9, 2), Utc).Where(p => p["phase"]!.GetValue<string>() == "Full Moon").ToList();
        Assert.Equal(2, full.Count);
        Assert.Contains("supermoon", full[0]["specials"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.DoesNotContain("blue_moon", full[0]["specials"]!.AsArray().Select(x => x!.GetValue<string>()));
        var second = full[1]["specials"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Contains("blue_moon", second);
        Assert.Contains("supermoon", second);
    }

    [Fact]
    public void Harvest_moon_2024()
    {
        var full = MoonEvents.Phases(D(2024, 9, 10), D(2024, 9, 25), Utc).Single(p => p["phase"]!.GetValue<string>() == "Full Moon");
        Assert.Equal("Harvest Moon", full["folk_name"]!.GetValue<string>());
    }

    [Fact]
    public void Known_eclipses()
    {
        var e = MoonEvents.Eclipses(D(2024, 3, 1), D(2025, 3, 31), Utc);
        Assert.Contains(e, x => x["eclipse"]!.GetValue<string>() == "solar" && x["kind"]!.GetValue<string>() == "total" && x["peak_local"]!.GetValue<string>().StartsWith("2024-04-08"));
        Assert.Contains(e, x => x["eclipse"]!.GetValue<string>() == "lunar" && x["kind"]!.GetValue<string>() == "total" && x["peak_local"]!.GetValue<string>().StartsWith("2025-03-14"));
    }

    [Fact]
    public void Circles_alternate_new_and_full()
    {
        var c = MoonEvents.Circles(D(2026, 11, 5), 4, Utc);
        Assert.Equal(4, c.Count);
        Assert.Equal("new_moon", c[0]!["spread"]!.GetValue<string>()); // new moon 9 Nov 2026
        Assert.Equal("full_moon", c[1]!["spread"]!.GetValue<string>());
    }
}

public class TideTests
{
    private static AstroTime T(int y, int m, int d) => new(new DateTime(y, m, d, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Spring_neap_and_king_tides()
    {
        Assert.Equal("king_tide", Tides.Label(Tides.Strength(T(2024, 4, 8))));   // new moon near perigee (the eclipse)
        Assert.Equal("neap_tide", Tides.Label(Tides.Strength(T(2024, 4, 15))));  // first quarter
        Assert.True(Tides.Strength(T(2024, 4, 23)) > 1.2);                         // full moon
    }
}

public class WeatherTests
{
    [Fact]
    public void Parses_MET_Norway_and_reads_omens()
    {
        var w = MetNorwayWeather.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "metno-guadalajara.json")))!;
        Assert.Equal(27.0, w.TempC);
        Assert.Equal("clearsky_day", w.Symbol);
        var omen = WeatherOmens.Read(w);
        Assert.Contains("weather_clear", omen["conditions"]!.AsArray().Select(x => x!.GetValue<string>()));
        Assert.Equal(1, omen["elements"]!["fire"]!.GetValue<int>());
    }

    [Fact]
    public void Storm_brings_fire_air_and_water()
    {
        var omen = WeatherOmens.Read(new WeatherNow(DateTimeOffset.UtcNow, 18, 12, 100, 4, "heavyrainandthunder", 90));
        var c = omen["conditions"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        Assert.Contains("weather_storm", c);
        Assert.Contains("weather_wind", c);
        Assert.Equal(2, omen["elements"]!["air"]!.GetValue<int>());
    }
}

public class HistoryTests
{
    [Fact]
    public void Moon_landing_under_a_waxing_crescent()
    {
        var r = History.Reading("apollo11");
        Assert.Equal("Waxing Crescent", r["sky_then"]!["moon_phase"]!.GetValue<string>());
        Assert.Equal(r["the_decks_card"]!["card"]!.GetValue<string>(), History.Reading("apollo11")["the_decks_card"]!["card"]!.GetValue<string>());
    }

    [Fact]
    public void On_this_day_finds_anniversaries()
    {
        var e = History.OnThisDay(new DateOnly(2026, 7, 18), 3);
        var apollo = e.Single(x => x!["id"]!.GetValue<string>() == "apollo11")!;
        Assert.Equal(2, apollo["in_days"]!.GetValue<int>());
        Assert.Equal(57, apollo["years_ago"]!.GetValue<int>());
    }

    [Fact]
    public void Every_event_has_a_reading()
    {
        foreach (var ev in History.Events) Assert.NotNull(History.Reading(ev["id"]!.GetValue<string>())["the_decks_card"]);
    }
}

public class DayContextTests
{
    [Fact]
    public void Day_of_the_Dead_in_the_rain()
    {
        var u = new UserContext("u1", "America/Mexico_City")
        {
            Country = "MX", Latitude = 20.67, Longitude = -103.35,
            Weather = new WeatherNow(DateTimeOffset.UtcNow, 16, 3, 95, 2.5, "rain", 88),
        };
        var c = DayContext.Build(new DateTimeOffset(2026, 11, 1, 18, 0, 0, TimeSpan.Zero), u);
        Assert.Contains(c["festivities_today"]!.AsArray(), f => f!["id"]!.GetValue<string>() == "dia_de_muertos");
        Assert.Equal("ancestors", c["suggested_spread"]!["spread"]!.GetValue<string>());
        Assert.NotNull(c["explain"]!["weather_rain"]);
        Assert.Equal("northern", c["hemisphere"]!.GetValue<string>());
        Assert.NotNull(c["tides"]!["label"]);
        Assert.True(c["elements_of_the_day"]!["water"]!.GetValue<int>() >= 1);
    }

    [Fact]
    public void Sao_Paulo_is_southern_and_calendar_tools_are_always_open()
    {
        var u = new UserContext("u2", "America/Sao_Paulo") { Country = "BR", Latitude = -23.55, Longitude = -46.63, Schools = new HashSet<string>() };
        var r = new Oracle("s"u8.ToArray()).Run("day_context", null, u);
        Assert.False(r.Locked);
        Assert.Equal("southern", r.Result["hemisphere"]!.GetValue<string>());
        Assert.False(new Oracle("s"u8.ToArray()).Run("history_reading", new JsonObject { ["event_id"] = "gagarin" }, u).Locked);
        Assert.True(new Oracle("s"u8.ToArray()).Run("draw_tarot", null, u).Locked);
    }
}
