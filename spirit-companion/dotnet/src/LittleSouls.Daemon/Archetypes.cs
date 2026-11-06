using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace LittleSouls.Daemon;

/// <summary>The archetypes (archetypes.json, embedded): who each little soul is and how it speaks, for the seat's notes.</summary>
public static class Archetypes
{
    private static readonly Lazy<Dictionary<string, JsonObject>> All = new(() =>
    {
        using var s = typeof(Archetypes).Assembly.GetManifestResourceStream("archetypes.json")!;
        var root = JsonNode.Parse(s)!;
        return (root as JsonArray ?? root["archetypes"]!.AsArray()).OfType<JsonObject>().ToDictionary(a => a["id"]!.GetValue<string>());
    });

    public static IReadOnlyCollection<string> Ids => All.Value.Keys;

    public static bool Exists(string? id) => id is not null && All.Value.ContainsKey(id);

    /// <summary>The persona for a summoned little soul: its name, its archetype in the person's language, and its voice.</summary>
    public static Persona Persona(string archetype, string name, string language, string? personName = null, string companionNoun = "little soul")
    {
        var a = All.Value[archetype];
        string archetypeName = a["names"]?[language]?.GetValue<string>() ?? archetype;
        string voice = $"{a["essence"]} Temperament: {a["temperament"]}. Voice: {string.Join(" ", a["voice_rules"]!.AsArray().Select(v => v!.GetValue<string>()))} " +
                       $"Your shadow side: {a["shadow_side"]} You speak the person's language ({language})" + (personName is null ? "." : $"; the person is {personName}.");
        return new Persona(name, archetypeName, voice, companionNoun);
    }

    /// <summary>The archetype's native school of divination (its first favourite method).</summary>
    public static string? FavouriteMethod(string archetype) => All.Value.TryGetValue(archetype, out var a) ? a["favorite_methods"]?[0]?.GetValue<string>() : null;
}

/// <summary>
/// DeepSeek reasons at high effort unless told otherwise, and Creatura's transport does not say: this handler adds
/// <c>reasoning_effort</c> to every chat request that has none.
/// </summary>
public sealed class ReasoningEffortHandler(string effort) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is not null && request.RequestUri?.AbsolutePath.EndsWith("/chat/completions") == true
            && JsonNode.Parse(await request.Content.ReadAsStringAsync(ct)) is JsonObject body && body["reasoning_effort"] is null)
        {
            body["reasoning_effort"] = effort;
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        return await base.SendAsync(request, ct);
    }
}
