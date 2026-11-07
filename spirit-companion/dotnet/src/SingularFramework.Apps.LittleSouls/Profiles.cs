using System.Globalization;
using System.Text.Json.Nodes;
using Daemon.Divination;
using SingularFramework.Platform.Runtime;
using XafPlatform.Platform.Data;

namespace SingularFramework.Apps.LittleSouls;

/// <summary>
/// The rules for a profile, as pure functions so they can be tested without a database: the age gate, the consents,
/// valid places and time zones, and location kept at city level.
/// </summary>
public static class ProfileRules
{
    /// <summary>One decimal of latitude/longitude is about 11 km: enough for the sky and the weather, not enough to find a home.</summary>
    public const int LocationDecimals = 1;

    /// <summary>The problems with a profile (empty when it is acceptable), and the profile as it should be stored.</summary>
    public static (List<string> Problems, JsonObject Stored) Check(JsonObject profile, DateOnly today, int minimumAge)
    {
        var problems = new List<string>();
        var p = (JsonObject)profile.DeepClone();

        if (!DateOnly.TryParseExact(Str(p, "birthDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
            problems.Add("birthDate must be YYYY-MM-DD");
        else if (Age(birth, today) < minimumAge)
            problems.Add($"Little Souls is for people aged {minimumAge} or over");

        var consents = p["consents"] as JsonObject;
        foreach (var c in new[] { "terms", "aiProcessing" })
            if (consents?[c] is null) problems.Add($"consent '{c}' is required");

        foreach (var tzField in new[] { "timeZone", "rootsTimeZone" })
            if (p[tzField] is not null && !TimeZoneExists(Str(p, tzField)))
                problems.Add($"{tzField} is not a known time zone");
        if (p["birthPlace"]?["timeZone"] is { } btz && !TimeZoneExists(btz.GetValue<string>()))
            problems.Add("birthPlace.timeZone is not a known time zone");

        // Location: shared once, kept at city level, and only with consent.
        if (p["location"] is JsonObject loc)
        {
            if (consents?["location"] is null) { p.Remove("location"); problems.Add("location needs the 'location' consent"); }
            else
                foreach (var k in new[] { "latitude", "longitude" })
                    if (loc[k] is { } v) loc[k] = Math.Round(v.GetValue<double>(), LocationDecimals);
        }
        return (problems, p);
    }

    public static int Age(DateOnly birth, DateOnly today)
    {
        int age = today.Year - birth.Year;
        if (today < birth.AddYears(age)) age--;
        return age;
    }

    public static bool TimeZoneExists(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return false; }
    }

    /// <summary>The divination engine's view of a person, from their profile, daemon and entitlements documents.</summary>
    public static UserContext ToUserContext(string subject, JsonObject? profile, JsonObject? daemon, JsonObject? entitlements)
    {
        var schools = new HashSet<string>(LittleSoulsApp.FreeSchools);
        if (Native(daemon?["archetype"]?.GetValue<string>()) is { } native) schools.Add(native);
        bool paid = entitlements?["plan"]?.GetValue<string>() == "pacto"
            && (entitlements["until"] is null || DateTimeOffset.TryParse(entitlements["until"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var until) && until > DateTimeOffset.UtcNow);
        foreach (var s in entitlements?["schools"]?.AsArray() ?? []) schools.Add(s!.GetValue<string>());

        var u = new UserContext(subject, Str(profile, "timeZone") is { Length: > 0 } tz && TimeZoneExists(tz) ? tz : "UTC")
        {
            Name = Str(profile, "name"),
            Language = Str(profile, "language"),
            Country = Str(profile, "country"),
            RootsCountry = Str(profile, "rootsCountry"),
            RootsTimeZoneId = Str(profile, "rootsTimeZone"),
            BirthDate = DateOnly.TryParseExact(Str(profile, "birthDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var b) ? b : null,
            BirthTime = TimeOnly.TryParseExact(Str(profile, "birthTime"), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bt) ? bt : null,
            BirthTimeZoneId = Str(profile?["birthPlace"] as JsonObject, "timeZone"),
            BirthLatitude = profile?["birthPlace"]?["latitude"]?.GetValue<double>(),
            BirthLongitude = profile?["birthPlace"]?["longitude"]?.GetValue<double>(),
            Latitude = profile?["location"]?["latitude"]?.GetValue<double>(),
            Longitude = profile?["location"]?["longitude"]?.GetValue<double>(),
            Schools = paid ? null : schools, // null: every school
        };
        return u;
    }

    /// <summary>Each archetype's native school: what its daemon reads for free.</summary>
    public static string? Native(string? archetype) => archetype switch
    {
        "seer" or "shadow" or "healer" or "trickster" => "tarot",
        "keeper" or "wild" => "runes",
        "wanderer" => "iching",
        "flame" => "numerology",
        _ => null,
    };

    private static string? Str(JsonObject? o, string key) => o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>Enforces <see cref="ProfileRules"/> on every write to a profile, and stores the city-level location.</summary>
public sealed class ProfileHook(LittleSoulsOptions options) : IDocumentHook
{
    public ValueTask BeforeAsync(DocumentHookContext context, CancellationToken ct)
    {
        if (context.App.AppSlug != LittleSoulsApp.Slug) return ValueTask.CompletedTask;
        var profile = JsonNode.Parse(context.AfterJson) as JsonObject ?? new JsonObject();
        var (problems, stored) = ProfileRules.Check(profile, DateOnly.FromDateTime(DateTime.UtcNow), options.MinimumAge);
        if (problems.Count > 0) context.Reject(string.Join("; ", problems));
        context.AfterJson = stored.ToJsonString();
        return ValueTask.CompletedTask;
    }

    public ValueTask AfterAsync(DocumentHookContext context, CancellationToken ct) => ValueTask.CompletedTask;
}

/// <summary>A receipt is written once, as the person, when the draw happens; nobody edits it afterwards.</summary>
public sealed class ReceiptsHook : IDocumentHook
{
    public ValueTask BeforeAsync(DocumentHookContext context, CancellationToken ct)
    {
        if (context.App.AppSlug == LittleSoulsApp.Slug && context.Operation == DocumentOperation.Update)
            context.Reject("receipts are never changed after a draw");
        return ValueTask.CompletedTask;
    }

    public ValueTask AfterAsync(DocumentHookContext context, CancellationToken ct) => ValueTask.CompletedTask;
}

/// <summary>Reads the person's profile, daemon and entitlements as the person, to build the engine's <see cref="UserContext"/>.</summary>
public sealed class PersonContexts(DocumentAccess documents)
{
    public async Task<UserContext> ForAsync(ApplicationContext app, CancellationToken ct)
    {
        string subject = app.Caller?.Subject ?? throw new InvalidOperationException("no caller");
        async Task<JsonObject?> Get(string collection) =>
            await documents.GetAsync(app, collection, subject, ct) is { } doc ? JsonNode.Parse(doc.PayloadJson) as JsonObject : null;
        return ProfileRules.ToUserContext(subject, await Get(LittleSoulsApp.Profile), await Get(LittleSoulsApp.Daemon), await Get(LittleSoulsApp.Entitlements));
    }
}
