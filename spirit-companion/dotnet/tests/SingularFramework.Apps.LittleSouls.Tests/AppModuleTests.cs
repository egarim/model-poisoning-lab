using System.Text.Json;
using System.Text.Json.Nodes;
using Daemon.Divination;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SingularFramework.Apps.LittleSouls;
using SingularFramework.Platform.Provisioning;
using Xunit;

namespace SingularFramework.Apps.LittleSouls.Tests;

public class ProvisioningTests
{
    // Parsed with Singular's own request type and the options its console uses for provisioning bodies.
    private static readonly ProvisionApp App = JsonSerializer.Deserialize<ProvisionApp>(LittleSoulsApp.ProvisioningJson(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    private static readonly string[] Personal = ["profile", "daemon", "notebook", "memories", "readings", "receipts", "plates", "conversations"];

    [Fact]
    public void Slug_and_collections_match_the_code()
    {
        Assert.Equal(LittleSoulsApp.Slug, App.Slug);
        var keys = App.Collections!.Select(c => c.TrimStart('~')).ToHashSet();
        foreach (var k in new[] { LittleSoulsApp.Profile, LittleSoulsApp.Daemon, LittleSoulsApp.Notebook, LittleSoulsApp.Memories, LittleSoulsApp.Readings,
                                  LittleSoulsApp.Receipts, LittleSoulsApp.Plates, LittleSoulsApp.Conversations, LittleSoulsApp.Entitlements })
            Assert.Contains(k, keys);
    }

    [Fact]
    public void Everything_personal_is_owner_scoped_and_deleted_with_the_account()
    {
        foreach (var c in Personal)
        {
            Assert.Contains("~" + c, App.Collections!);
            Assert.Equal("delete", App.Access![c].Closing);
        }
        Assert.True(App.Access!["profile"].OwnerKeyed);
        Assert.True(App.SelfSignup == true && App.OpenMembership == true); // Singular refuses sign-up without open membership
        Assert.True(App.Access!["daemon"].OwnerKeyed);
    }

    [Fact]
    public void Access_values_parse_with_Singulars_rules()
    {
        foreach (var (key, access) in App.Access!)
        {
            _ = access.ReadAccess; _ = access.CreateAccess; _ = access.WriteAccess; _ = access.ClosingRule; // throws on an unknown value
        }
        Assert.Equal("admins", App.Access!["receipts"].Write);
        Assert.Equal("addressed", App.Access!["entitlements"].Read);
        Assert.Equal("person", App.ReadScopes!["entitlements"]);
    }

    [Fact]
    public void Every_collection_has_a_schema()
    {
        foreach (var c in App.Collections!.Select(c => c.TrimStart('~')).Where(c => c != "conversations"))
            Assert.True(App.Schemas!.ContainsKey(c), $"no schema for {c}");
        Assert.All(App.Schemas!.Keys, k => Assert.Contains(App.Collections!, c => c.TrimStart('~') == k));
    }
}

public class ProfileRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static JsonObject Profile(string birth = "1990-05-01", bool location = false, bool locationConsent = false) => new()
    {
        ["birthDate"] = birth, ["country"] = "RU", ["rootsCountry"] = "SV", ["timeZone"] = "Europe/Moscow", ["rootsTimeZone"] = "America/El_Salvador",
        ["consents"] = new JsonObject { ["terms"] = "2026-11-06T10:00:00Z", ["aiProcessing"] = "2026-11-06T10:00:00Z", ["location"] = locationConsent ? "2026-11-06T10:00:00Z" : null },
        ["location"] = location ? new JsonObject { ["latitude"] = 59.93863, ["longitude"] = 30.31413 } : null,
    };

    [Fact]
    public void A_valid_profile_passes()
    {
        var (problems, _) = ProfileRules.Check(Profile(), Today, 18);
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("2008-11-07", false)] // 17, one day short
    [InlineData("2008-11-06", true)]  // 18 today
    public void The_age_gate(string birth, bool ok) => Assert.Equal(ok, ProfileRules.Check(Profile(birth), Today, 18).Problems.Count == 0);

    [Fact]
    public void Consents_and_time_zones_are_required()
    {
        var p = Profile(); p["consents"] = new JsonObject { ["terms"] = "2026-11-06T10:00:00Z" }; p["timeZone"] = "Mars/Olympus";
        var problems = ProfileRules.Check(p, Today, 18).Problems;
        Assert.Contains(problems, x => x.Contains("aiProcessing"));
        Assert.Contains(problems, x => x.Contains("timeZone"));
    }

    [Fact]
    public void Location_is_city_level_and_needs_consent()
    {
        var (problems, stored) = ProfileRules.Check(Profile(location: true, locationConsent: true), Today, 18);
        Assert.Empty(problems);
        Assert.Equal(59.9, stored["location"]!["latitude"]!.GetValue<double>());
        Assert.Equal(30.3, stored["location"]!["longitude"]!.GetValue<double>());
        var (refused, kept) = ProfileRules.Check(Profile(location: true, locationConsent: false), Today, 18);
        Assert.Contains(refused, x => x.Contains("location"));
        Assert.Null(kept["location"]);
    }

    [Fact]
    public void Free_schools_plus_the_native_one_and_Pacto_opens_all()
    {
        var daemon = new JsonObject { ["archetype"] = "wanderer" };
        var free = ProfileRules.ToUserContext("u1", Profile(), daemon, null);
        Assert.Equal(new[] { "astrology", "calendar", "iching", "tarot" }, free.Schools!.Order());
        Assert.Equal("SV", free.RootsCountry);
        Assert.Equal(new DateOnly(1990, 5, 1), free.BirthDate);
        Assert.Null(ProfileRules.ToUserContext("u1", Profile(), daemon, new JsonObject { ["plan"] = "pacto", ["person"] = "u1" }).Schools);
        var expired = new JsonObject { ["plan"] = "pacto", ["person"] = "u1", ["until"] = "2020-01-01T00:00:00Z" };
        Assert.NotNull(ProfileRules.ToUserContext("u1", Profile(), daemon, expired).Schools);
        var bought = new JsonObject { ["plan"] = "free", ["person"] = "u1", ["schools"] = new JsonArray("lenormand") };
        Assert.Contains("lenormand", ProfileRules.ToUserContext("u1", Profile(), daemon, bought).Schools!);
    }

    [Fact]
    public void A_missing_profile_still_gives_a_usable_context()
    {
        var u = ProfileRules.ToUserContext("u1", null, null, null);
        Assert.Equal("UTC", u.TimeZoneId);
        Assert.Null(u.BirthDate);
    }
}

public class AssistantToolsTests
{
    [Fact]
    public void One_assistant_tool_per_engine_tool()
    {
        var tools = DivinationTools.Define();
        var engine = ToolCatalog.All().Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
        Assert.Equal(engine.Order(), tools.Select(t => t.Name).Order());
        Assert.All(tools, t => Assert.Equal("object", t.Parameters["type"]!.GetValue<string>()));
        Assert.All(tools, t => Assert.NotEqual("Consulting…", t.LabelText));
        Assert.Contains("[[card:", DivinationTools.Instructions);
    }

    [Fact]
    public void A_stored_receipt_can_be_verified()
    {
        var oracle = new Oracle("a-secret-of-sixteen+"u8.ToArray());
        var user = new UserContext("u1", "America/Mexico_City") { Country = "MX" };
        var r = oracle.Run("draw_tarot", new JsonObject { ["spread"] = "three" }, user);
        var (receiptDoc, readingDoc) = DivinationTools.Documents(r);

        Assert.Matches("^[0-9a-f]{64}$", receiptDoc["resultHash"]!.GetValue<string>());
        Assert.Equal(r.Receipt!.Id, readingDoc["receipt"]!.GetValue<string>());
        Assert.Equal("tarot", DivinationTools.CardKind("draw_tarot"));

        // Back from the stored document to a receipt, then recompute the draw.
        var back = new DrawReceipt(r.Receipt.Id, receiptDoc["method"]!.GetValue<string>(), receiptDoc["seed"]!.GetValue<string>(),
            receiptDoc["seedKind"]!.GetValue<string>(), DateTimeOffset.Parse(receiptDoc["at"]!.GetValue<string>()),
            receiptDoc["args"]!.GetValue<string>(), receiptDoc["resultHash"]!.GetValue<string>());
        Assert.True(oracle.Verify(back, user));
        Assert.False(oracle.Verify(back with { Seed = "forged" }, user));
    }
}

public class RegistrationTests
{
    private static IConfiguration Config(string? secret) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["LittleSouls:DailySecret"] = secret, ["LittleSouls:MinimumAge"] = "18" }).Build();

    [Fact]
    public void The_app_registers_and_needs_its_secret()
    {
        var ok = new ServiceCollection().AddLittleSoulsApp(Config("a-secret-of-sixteen+")).BuildServiceProvider();
        Assert.NotNull(ok.GetRequiredService<Oracle>());
        Assert.Equal(18, ok.GetRequiredService<LittleSoulsOptions>().MinimumAge);
        var missing = new ServiceCollection().AddLittleSoulsApp(Config(null)).BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => missing.GetRequiredService<Oracle>());
    }
}
