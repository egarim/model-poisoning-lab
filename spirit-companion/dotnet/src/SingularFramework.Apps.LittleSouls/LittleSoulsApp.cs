using Daemon.Divination;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SingularFramework.Apps.LittleSouls.Jobs;
using SingularFramework.Platform.Assistant;
using SingularFramework.Platform.Jobs;
using XafPlatform.Platform.Data;

namespace SingularFramework.Apps.LittleSouls;

/// <summary>The app slug and the collections it declares, in one place. Provisioning: provision/little-souls.app.json.</summary>
public static class LittleSoulsApp
{
    public const string Slug = "little-souls";

    /// <summary>One per person (owner-keyed): birth data, where they live, their roots, the four traits, consents.</summary>
    public const string Profile = "profile";
    /// <summary>One per person (owner-keyed): the summoned daemon (name, creature, archetype, bond stage).</summary>
    public const string Daemon = "daemon";
    /// <summary>The person model (LittleSouls.Companion): one document per belief, owner-scoped.</summary>
    public const string Notebook = "notebook";
    /// <summary>Episodic memories with an embedding; the vector index is set on the collection by an operator.</summary>
    public const string Memories = "memories";
    /// <summary>The readings the person received, each pointing at its receipt.</summary>
    public const string Readings = "readings";
    /// <summary>Draw receipts (Daemon.Divination.DrawReceipt). Created as the person, never changed after; verified by recomputing.</summary>
    public const string Receipts = "receipts";
    /// <summary>Shared actions in the daemon's world: a plate and a candle for someone remembered.</summary>
    public const string Plates = "plates";
    /// <summary>The assistant's conversations (Singular's generic shape).</summary>
    public const string Conversations = "conversations";
    /// <summary>The person's plan and unlocked schools. Written by administrators (the payment flow, later); read by the person it names.</summary>
    public const string Entitlements = "entitlements";

    /// <summary>What the jobs sent and which sessions they closed, so nothing happens twice. System-only.</summary>
    public const string Outbox = "outbox";

    /// <summary>The subject the app's jobs write as.</summary>
    public const string SystemSubject = "system:little-souls";

    /// <summary>Schools open on the free plan: the daemon's native school is added from the daemon document.</summary>
    public static readonly IReadOnlySet<string> FreeSchools = new HashSet<string> { "tarot", "astrology", "calendar" };

    /// <summary>The provisioning request body for the app, as shipped in this assembly.</summary>
    public static string ProvisioningJson()
    {
        using var s = typeof(LittleSoulsApp).Assembly.GetManifestResourceStream("little-souls.app.json")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>
    /// Wires the app into a host: the divination oracle, the hooks and the assistant with the divination tools. Call it in
    /// both hosts, as Cosmos does. Configuration: <c>LittleSouls:DailySecret</c> (required: seeds the cards of the day),
    /// <c>LittleSouls:MinimumAge</c> (default 18).
    /// </summary>
    public static IServiceCollection AddLittleSoulsApp(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new LittleSoulsOptions
        {
            DailySecret = configuration["LittleSouls:DailySecret"] ?? "",
            MinimumAge = int.TryParse(configuration["LittleSouls:MinimumAge"], out int age) && age > 0 ? age : 18,
        };
        services.AddSingleton(options);
        services.AddSingleton(_ => new Oracle(System.Text.Encoding.UTF8.GetBytes(options.RequireSecret())));
        services.AddScoped<PersonContexts>();

        services.AddDocumentHook<ProfileHook>(collection: Profile, on: [DocumentOperation.Create, DocumentOperation.Update]);
        services.AddDocumentHook<ReceiptsHook>(collection: Receipts);
        services.AddDocumentHook<NotebookForgetHook>(collection: Notebook, on: [DocumentOperation.Delete]);

        // The daemon's initiative, in each person's own time (Jobs/Planning.cs), and the end of each session.
        services.AddSingleton<LittleSoulsTenants>();
        services.TryAddSingleton<IProposalSource, NoProposals>(); // replaced by the model's proposals in slice B3
        services.AddScheduledJob<DaemonRoundsJob>("*/15 * * * *", DaemonRoundsJob.Name);
        services.AddScheduledJob<SessionCloseJob>("*/10 * * * *", SessionCloseJob.Name);

        // Until Singular has an assistant-engine seam (docs/design-singular.md, G2), the daemon runs inside Singular's own
        // assistant loop with the divination methods as app tools. Every card still comes from the engine, with a receipt;
        // what this interim cannot do yet is hold the reply back until the grounding critic approves it.
        services.AddAssistant(new AssistantApp(
            Slug,
            Readable: [Profile, Daemon, Notebook, Readings, Plates],
            Proposable: [Notebook, Plates],
            Instructions: DivinationTools.Instructions,
            Conversations: Conversations,
            AppTools: DivinationTools.Define()));
        return services;
    }
}

public sealed class LittleSoulsOptions
{
    public string DailySecret { get; init; } = "";
    public int MinimumAge { get; init; } = 18;

    public string RequireSecret() => DailySecret.Length >= 16
        ? DailySecret
        : throw new InvalidOperationException("LittleSouls:DailySecret must be set (at least 16 characters): it seeds every person's card of the day.");
}
