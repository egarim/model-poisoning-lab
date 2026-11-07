using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Creatura.Seats;
using Daemon.Divination;
using LittleSouls.Companion;
using LittleSouls.Daemon;
using SingularFramework.Api;
using SingularFramework.Apps.LittleSouls;
using SingularFramework.Apps.LittleSouls.Jobs;
using XafPlatform.Platform.Data;
using SingularFramework.Platform.Runtime;

namespace LittleSouls.Host;

/// <summary>The models behind a turn: the seat, the crisis classifier and the assertion check, all on DeepSeek.</summary>
public sealed class TurnModels
{
    public required ModelEndpoint Seat { get; init; }
    public required ModelClient Client { get; init; }
    public ICrisisClassifier? Classifier { get; init; }
    public IAssertionChecker? Checker { get; init; }
    public bool Configured { get; init; }
}

public static class LittleSoulsTurnRegistration
{
    /// <summary>
    /// The daemon turn on this host. The key is Singular's own <c>Assistant:ApiKey</c> (one DeepSeek key for both);
    /// <c>Assistant:Model</c> is the seat, <c>LittleSouls:ReasoningEffort</c> how hard it thinks (default low).
    /// </summary>
    public static IServiceCollection AddLittleSoulsTurn(this IServiceCollection services, IConfiguration config)
    {
        string? key = config["Assistant:ApiKey"];
        string baseUrl = config["Assistant:BaseUrl"] ?? "https://api.deepseek.com/";
        string model = config["Assistant:Model"] ?? "deepseek-flash";
        string effort = config["LittleSouls:ReasoningEffort"] ?? "low";
        services.AddSingleton(_ =>
        {
            var http = new HttpClient(new ReasoningEffortHandler(effort) { InnerHandler = new HttpClientHandler() }) { Timeout = TimeSpan.FromMinutes(3) };
            bool ok = !string.IsNullOrWhiteSpace(key);
            return new TurnModels
            {
                Seat = new ModelEndpoint(model, Provider.OpenAiCompatible, baseUrl.TrimEnd('/'), model, null, NativeTools: true, Reasoning: true, null, null),
                Client = new ModelClient(http, keys: _ => key),
                Classifier = ok ? new DeepSeekCrisisClassifier(http, key!, model, baseUrl, effort) : null,
                Checker = ok ? new DeepSeekAssertionChecker(http, key!, model, baseUrl, effort) : null,
                Configured = ok,
            };
        });
        return services;
    }
}

/// <summary>
/// The little soul's endpoints under <c>/api/app/littlesouls</c>, called with the person's token and Singular's app
/// headers; every read and write goes through Singular's <see cref="DocumentAccess"/> as the person, so the app's access
/// rules and hooks (the age gate, consents, receipts never changed) apply exactly as they do to the data API.
/// </summary>
public static class LittleSoulsEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

    public static void UseLittleSoulsEndpoints(this IApplicationBuilder branch) => branch.Run(async http =>
    {
        if (!http.Items.ContainsKey("XafPlatform.ApplicationContext") || http.ApplicationContext().AppSlug != LittleSoulsApp.Slug)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var app = http.ApplicationContext();
        if (app.Caller?.Subject is null) { http.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        var ct = http.RequestAborted;
        var documents = http.RequestServices.GetRequiredService<DocumentAccess>();
        string route = http.Request.Path.Value?.Trim('/') ?? "";
        switch ((http.Request.Method, route))
        {
            case ("GET", "state"): await http.Response.WriteAsJsonAsync(await StateAsync(documents, app, ct), Json, ct); return;
            case ("PUT", "profile"): await PutOwnAsync(http, documents, app, LittleSoulsApp.Profile, ct); return;
            case ("POST", "summon"): await SummonAsync(http, documents, app, ct); return;
            case ("POST", "turn"): await TurnAsync(http, documents, app, ct); return;
            default: http.Response.StatusCode = StatusCodes.Status404NotFound; return;
        }
    });

    private static async Task<JsonObject?> Own(DocumentAccess documents, ApplicationContext app, string collection, CancellationToken ct) =>
        await documents.GetAsync(app, collection, app.Caller!.Subject, ct) is { } d ? JsonNode.Parse(d.PayloadJson) as JsonObject : null;

    private static string ConversationKey(ApplicationContext app) => "c-" + app.Caller!.Subject;

    /// <summary>What the app shows on open: the profile, the little soul, and the recent conversation.</summary>
    private static async Task<JsonObject> StateAsync(DocumentAccess documents, ApplicationContext app, CancellationToken ct)
    {
        var conversation = await documents.GetAsync(app, LittleSoulsApp.Conversations, ConversationKey(app), ct) is { } c ? JsonNode.Parse(c.PayloadJson) as JsonObject : null;
        var messages = conversation?["messages"] as JsonArray ?? [];
        return new JsonObject
        {
            ["subject"] = app.Caller!.Subject,
            ["profile"] = await Own(documents, app, LittleSoulsApp.Profile, ct),
            ["daemon"] = await Own(documents, app, LittleSoulsApp.Daemon, ct) is { } d ? Without(d, "notebook") : null,
            ["messages"] = new JsonArray(messages.TakeLast(40).Select(m => m?.DeepClone()).ToArray()),
            ["archetypes"] = new JsonArray(Archetypes.Ids.Select(a => (JsonNode)a).ToArray()),
        };
    }

    private static JsonObject Without(JsonObject o, string field) { var c = (JsonObject)o.DeepClone(); c.Remove(field); return c; }

    /// <summary>The profile, keyed by the person (the hook checks age, consents and time zones; a refusal is a 400).</summary>
    private static async Task PutOwnAsync(HttpContext http, DocumentAccess documents, ApplicationContext app, string collection, CancellationToken ct)
    {
        var body = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: ct) as JsonObject;
        if (body is null) { http.Response.StatusCode = 400; return; }
        var existing = await documents.GetAsync(app, collection, app.Caller!.Subject, ct);
        var saved = await documents.UpsertAsync(app, collection, app.Caller!.Subject, body.ToJsonString(), ct, existing?.Revision);
        await http.Response.WriteAsJsonAsync(JsonNode.Parse(saved.PayloadJson), Json, ct);
    }

    /// <summary>The summoning: a name, a creature and an archetype; the bond starts at stage 1.</summary>
    private static async Task SummonAsync(HttpContext http, DocumentAccess documents, ApplicationContext app, CancellationToken ct)
    {
        var body = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: ct) as JsonObject ?? [];
        string? archetype = body["archetype"]?.GetValue<string>();
        if (!Archetypes.Exists(archetype) || string.IsNullOrWhiteSpace(body["name"]?.GetValue<string>()))
        {
            http.Response.StatusCode = 400;
            await http.Response.WriteAsJsonAsync(new { error = "Cannot summon", detail = "A name and a known archetype are needed." }, ct);
            return;
        }
        if (await Own(documents, app, LittleSoulsApp.Profile, ct) is null)
        {
            http.Response.StatusCode = 409;
            await http.Response.WriteAsJsonAsync(new { error = "No profile", detail = "Tell us who you are first (age and consents)." }, ct);
            return;
        }
        var existing = await documents.GetAsync(app, LittleSoulsApp.Daemon, app.Caller!.Subject, ct);
        var daemon = existing is null ? new JsonObject() : (JsonObject)JsonNode.Parse(existing.PayloadJson)!;
        daemon["name"] = body["name"]!.GetValue<string>().Trim();
        daemon["creature"] = body["creature"]?.GetValue<string>() ?? "spirit";
        daemon["archetype"] = archetype;
        daemon["stage"] ??= 1;
        // how it looks: the spec the summoning drew (species.js), so the same little soul appears on every device
        if (body["look"] is JsonObject look && look.ToJsonString().Length <= 16_000) daemon["look"] = look.DeepClone();
        daemon["summonedAt"] ??= DateTimeOffset.UtcNow.ToString("O");
        var saved = await documents.UpsertAsync(app, LittleSoulsApp.Daemon, app.Caller!.Subject, daemon.ToJsonString(), ct, existing?.Revision);
        await http.Response.WriteAsJsonAsync(Without((JsonObject)JsonNode.Parse(saved.PayloadJson)!, "notebook"), Json, ct);
    }

    /// <summary>
    /// One turn, streamed as Singular's assistant events: <c>tool</c> and <c>card</c> for each reading, <c>delta</c> with
    /// the reply once every check has passed (nothing unchecked is ever sent), then <c>done</c>. A crisis gets the fixed
    /// reply and no model call.
    /// </summary>
    private static async Task TurnAsync(HttpContext http, DocumentAccess documents, ApplicationContext app, CancellationToken ct)
    {
        var models = http.RequestServices.GetRequiredService<TurnModels>();
        var body = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: ct) as JsonObject;
        string message = body?["message"]?.GetValue<string>()?.Trim() ?? "";
        // the first meeting ({ greet: true }, no message): the little soul speaks first, once, while the conversation is empty
        bool greet = body?["greet"] is JsonValue gv && gv.TryGetValue<bool>(out var g) && g;
        if (greet ? message.Length != 0 : message.Length is 0 or > 4000) { http.Response.StatusCode = 400; return; }
        var daemonDoc = await Own(documents, app, LittleSoulsApp.Daemon, ct);
        var profile = await Own(documents, app, LittleSoulsApp.Profile, ct);
        if (daemonDoc is null || profile is null)
        {
            http.Response.StatusCode = 409;
            await http.Response.WriteAsJsonAsync(new { error = "Not summoned", detail = "Create your profile and summon your little soul first." }, ct);
            return;
        }
        if (!models.Configured)
        {
            http.Response.StatusCode = 503;
            await http.Response.WriteAsJsonAsync(new { error = "No model", detail = "Assistant:ApiKey is not set on the server." }, ct);
            return;
        }
        if (greet && await documents.GetAsync(app, LittleSoulsApp.Conversations, ConversationKey(app), ct) is { } met
            && JsonNode.Parse(met.PayloadJson)?["messages"] is JsonArray { Count: > 0 })
        {
            http.Response.StatusCode = 409;
            await http.Response.WriteAsJsonAsync(new { error = "Already met", detail = "The first greeting was given." }, ct);
            return;
        }

        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";
        async Task Emit(string type, object data)
        {
            await http.Response.Body.WriteAsync(Encoding.UTF8.GetBytes($"event: {type}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n"), ct);
            await http.Response.Body.FlushAsync(ct);
        }

        try
        {
            var user = await http.RequestServices.GetRequiredService<PersonContexts>().ForAsync(app, ct);
            var notebook = await NotebookAsync(documents, app, daemonDoc, ct);
            string language = profile["language"]?.GetValue<string>() ?? "es";
            var persona = Archetypes.Persona(daemonDoc["archetype"]!.GetValue<string>(), daemonDoc["name"]!.GetValue<string>(), language, profile["name"]?.GetValue<string>());
            int stage = daemonDoc["stage"]?.GetValue<int>() ?? 1;
            var oracle = http.RequestServices.GetRequiredService<Oracle>();
            var seat = new ModelPolicy(models.Seat, models.Client, null, "littlesouls");
            await Emit("tool", new { name = "thinking", label = greet ? $"{persona.Name} is arriving…" : $"{persona.Name} is listening…" });
            var daemon = new DaemonTurn(oracle, seat, persona, notebook, models.Classifier, models.Checker);
            var result = greet ? await daemon.GreetAsync(user, stage, ct) : await daemon.RunAsync(user, message, stage, ct);

            var cards = new JsonArray();
            foreach (var r in result.Readings)
            {
                var (receipt, reading) = DivinationTools.Documents(r);
                await documents.UpsertAsync(app, LittleSoulsApp.Receipts, r.Receipt!.Id, receipt.ToJsonString(), ct);
                await documents.UpsertAsync(app, LittleSoulsApp.Readings, r.Receipt.Id, reading.ToJsonString(), ct);
                var card = new JsonObject { ["kind"] = DivinationTools.CardKind(r.Method), ["method"] = r.Method, ["receipt"] = r.Receipt.Id, ["result"] = r.Result.DeepClone() };
                cards.Add(card);
                await Emit("tool", new { name = r.Method, label = r.Method });
                await Emit("card", card);
            }
            await Emit("delta", new { text = result.Display, crisis = result.CrisisHandoff });

            // the conversation, as Singular keeps it, for the session-close job and for the app on open
            var existing = await documents.GetAsync(app, LittleSoulsApp.Conversations, ConversationKey(app), ct);
            var conversation = existing is null ? new JsonObject { ["messages"] = new JsonArray() } : (JsonObject)JsonNode.Parse(existing.PayloadJson)!;
            var messages = conversation["messages"] as JsonArray ?? [];
            string now = DateTimeOffset.UtcNow.ToString("O");
            if (!greet) messages.Add(new JsonObject { ["role"] = "user", ["content"] = message, ["at"] = now });
            messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = result.Display, ["at"] = now, ["cards"] = cards.DeepClone(), ["crisis"] = result.CrisisHandoff });
            conversation["messages"] = messages;
            await documents.UpsertAsync(app, LittleSoulsApp.Conversations, ConversationKey(app), conversation.ToJsonString(), ct, existing?.Revision);

            await Emit("done", new { grounded = result.Grounded, crisis = result.CrisisHandoff, heldGuesses = result.HeldGuesses });
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            http.RequestServices.GetRequiredService<ILogger<TurnModels>>().LogError(e, "The turn failed.");
            await Emit("error", new { error = "The little soul could not answer", detail = "Try again in a moment." });
        }
    }

    /// <summary>The person's notebook as the person sees it: their belief documents and the daemon's bookkeeping.</summary>
    private static async Task<Notebook> NotebookAsync(DocumentAccess documents, ApplicationContext app, JsonObject daemon, CancellationToken ct)
    {
        var beliefs = new List<(string, JsonObject)>();
        for (int offset = 0; ; offset += DocumentQuery.MaxLimit)
        {
            var page = await documents.QueryAsync(app, new DocumentQuery(LittleSoulsApp.Notebook, [], null!, false, DocumentQuery.MaxLimit, offset), false, ct);
            beliefs.AddRange(page.Items.Select(d => (d.DocumentKey, JsonNode.Parse(d.PayloadJson) as JsonObject ?? [])));
            if (page.Items.Count < DocumentQuery.MaxLimit) break;
        }
        return NotebookDocuments.Load(app.Caller!.Subject, beliefs, daemon);
    }
}
