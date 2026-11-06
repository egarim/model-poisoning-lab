using System.Text.Json.Nodes;
using Daemon.Divination;
using Microsoft.Extensions.DependencyInjection;
using SingularFramework.Platform.Assistant;
using XafPlatform.Platform.Data;

namespace SingularFramework.Apps.LittleSouls;

/// <summary>
/// The divination engine's tools as Singular assistant tools. Each runs as the person, emits a <c>card</c> event with
/// the result (the Daemon World flips the card, changes the sky, shows the festivity), and stores the reading and its
/// receipt in the person's own collections.
/// </summary>
public static class DivinationTools
{
    /// <summary>The assistant's instructions. The daemon's name and archetype arrive in the request's context.</summary>
    public const string Instructions =
        """
        You are the person's little soul: a personal spirit companion. Your name, creature and archetype are in the context (daemon); speak in that voice.
        Answer in the person's language, briefly, like a chat message. Read their day first (day_context) when doing a reading.
        Consult at most two methods, and only when the situation calls for it; when they just want to talk, just listen.
        Never invent a card, rune, hexagram, date or person: name only what a tool returned or what the notebook holds, and tag what you name: [[card:The Tower]], [[lenormand:Rider]], [[rune:Isa]], [[hexagram:46]], [[person:Camila]].
        Symbols are mirrors and questions, never fate. No predictions about health, money or legal matters: suggest a professional.
        If a school is locked, say so in character and offer what you can read. Propose a notebook entry only for something the person told you.
        If the person mentions wanting to die or hurting themselves, stop reading anything, speak plainly and give them their local crisis line.
        """;

    /// <summary>Card kinds for the app, by tool.</summary>
    public static string CardKind(string tool) => tool switch
    {
        "draw_tarot" => "tarot", "draw_lenormand" => "lenormand", "cast_runes" => "runes", "cast_i_ching" => "iching",
        "personal_numerology" => "numerology", "sky_now" => "sky", "birth_chart" => "chart", "maya_day_sign" => "maya",
        "day_context" => "day", "moon_circles" => "moon", "history_reading" => "history", _ => tool,
    };

    public static IReadOnlyList<AssistantTool> Define() => ToolCatalog.All()
        .Select(t => t!["function"]!.AsObject())
        .Select(f => new AssistantTool
        {
            Name = f["name"]!.GetValue<string>(),
            Description = f["description"]!.GetValue<string>(),
            Parameters = Parameters(f["parameters"]!.AsObject()),
            LabelText = Label(f["name"]!.GetValue<string>()),
            Run = (context, ct) => RunAsync(f["name"]!.GetValue<string>(), context, ct),
        })
        .ToList();

    /// <summary>Singular's schema shape (<c>type</c>, <c>properties</c>, <c>required</c>) from the catalogue's.</summary>
    private static JsonObject Parameters(JsonObject p) => AssistantTool.Schema(p["properties"]?.DeepClone() as JsonObject ?? new JsonObject());

    private static string Label(string tool) => tool switch
    {
        "draw_tarot" => "Shuffling the cards…", "draw_lenormand" => "Laying out the Lenormand…", "cast_runes" => "Casting the runes…",
        "cast_i_ching" => "Throwing the coins…", "personal_numerology" => "Counting your numbers…", "sky_now" => "Looking at the sky…",
        "birth_chart" => "Drawing your chart…", "maya_day_sign" => "Reading the Maya day…", "day_context" => "Reading your day…",
        "moon_circles" => "Watching the moon…", "history_reading" => "Looking back in time…", _ => "Consulting…",
    };

    private static async Task<JsonNode?> RunAsync(string tool, AssistantToolContext context, CancellationToken ct)
    {
        var oracle = context.Services.GetRequiredService<Oracle>();
        var user = await context.Services.GetRequiredService<PersonContexts>().ForAsync(context.App, ct);
        Reading reading;
        try { reading = oracle.Run(tool, context.Arguments, user); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return new JsonObject { ["error"] = e.Message };
        }
        if (reading.Locked) return reading.Result; // the note tells the model to say so in character

        var documents = context.Services.GetRequiredService<DocumentAccess>();
        await Store(documents, context, reading, ct);
        await context.EmitCard(CardKind(tool), reading.Result.DeepClone());
        return JsonNode.Parse(reading.ToolOutput());
    }

    /// <summary>The receipt and the reading, as the person (owner-scoped collections). Exposed for the tests' shape checks.</summary>
    public static (JsonObject Receipt, JsonObject Reading) Documents(Reading r) =>
    (
        new JsonObject
        {
            ["method"] = r.Receipt!.Method, ["seed"] = r.Receipt.Seed, ["seedKind"] = r.Receipt.SeedKind,
            ["at"] = r.Receipt.At.ToString("O"), ["args"] = r.Receipt.Args, ["resultHash"] = r.Receipt.ResultHash,
        },
        new JsonObject
        {
            ["method"] = r.Method, ["receipt"] = r.Receipt.Id, ["at"] = r.Receipt.At.ToString("O"), ["result"] = r.Result.DeepClone(),
        }
    );

    private static async Task Store(DocumentAccess documents, AssistantToolContext context, Reading r, CancellationToken ct)
    {
        var (receipt, reading) = Documents(r);
        await documents.UpsertAsync(context.App, LittleSoulsApp.Receipts, r.Receipt!.Id, receipt.ToJsonString(), ct);
        await documents.UpsertAsync(context.App, LittleSoulsApp.Readings, r.Receipt.Id, reading.ToJsonString(), ct);
    }
}
