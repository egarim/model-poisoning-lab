using System.Text.Json;
using System.Text.Json.Nodes;
using Daemon.Divination;
using LittleSouls.Companion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SingularFramework.Platform.Identity;
using SingularFramework.Platform.Jobs;
using SingularFramework.Platform.Messaging;
using SingularFramework.Platform.Runtime;
using XafPlatform.Platform.Data;

namespace SingularFramework.Apps.LittleSouls.Jobs;

/// <summary>The tenants that run Little Souls, and contexts to act in them (Cosmos's CosmosTenants, for this app).</summary>
public sealed class LittleSoulsTenants(IPlatformSessionFactory sessions, IServiceScopeFactory scopes)
{
    public IReadOnlyList<string> TenantSlugs()
    {
        using var db = sessions.Ef!.Create();
        return db.PlatformApplications
            .Where(a => a.Slug == LittleSoulsApp.Slug && a.IsActive && a.Tenant != null && a.TenantRef!.IsActive == true)
            .Select(a => a.TenantRef!.Slug!)
            .ToList().Distinct().ToList();
    }

    /// <summary>The app's system caller in a tenant: writes the outbox and reads across people.</summary>
    public Task<ApplicationContext?> SystemAsync(string tenant, CancellationToken ct) =>
        ContextAsync(tenant, new CallerIdentity(LittleSoulsApp.SystemSubject, null, "Little Souls"), admin: true, ct);

    /// <summary>A context that acts as the person, so their own documents stay theirs (owner-scoped, deleted with the account).</summary>
    public Task<ApplicationContext?> PersonAsync(string tenant, string subject, CancellationToken ct) =>
        ContextAsync(tenant, new CallerIdentity(subject, null, null), admin: false, ct);

    private async Task<ApplicationContext?> ContextAsync(string tenant, CallerIdentity caller, bool admin, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var database = await scope.ServiceProvider.GetRequiredService<IDatabaseResolver>().ResolveAsync(tenant, LittleSoulsApp.Slug, ct);
        return database is null ? null : new ApplicationContext(tenant, LittleSoulsApp.Slug, caller, database, IsTenantAdministrator: admin);
    }

    public static async Task<List<DocumentRecord>> AllAsync(DocumentAccess documents, ApplicationContext app, string collection, IReadOnlyList<QueryFilter> filters, CancellationToken ct)
    {
        var all = new List<DocumentRecord>();
        for (int offset = 0; ; offset += DocumentQuery.MaxLimit)
        {
            var page = await documents.QueryAsOperatorAsync(app, new DocumentQuery(collection, filters, null!, false, DocumentQuery.MaxLimit, offset), ct);
            all.AddRange(page.Items);
            if (page.Items.Count < DocumentQuery.MaxLimit) return all;
        }
    }

    public static QueryFilter Eq(string field, string value) => new(field, QueryOperator.Eq, JsonSerializer.SerializeToElement(value));
}

/// <summary>
/// Every 15 minutes: the morning omen, follow-ups and moon-circle reminders, each decided by <see cref="Rounds"/> in the
/// person's own time and sent once (the <c>outbox</c> collection records it before the window closes).
/// </summary>
public sealed class DaemonRoundsJob(LittleSoulsTenants tenants, DocumentAccess documents, MessageService messages, Oracle oracle,
    TimeProvider? clock = null, ILogger<DaemonRoundsJob>? logger = null) : IScheduledJob
{
    public const string Name = "little-souls-rounds";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        int people = 0, sent = 0, failed = 0;
        foreach (var tenant in tenants.TenantSlugs())
        {
            if (await tenants.SystemAsync(tenant, ct) is not { } system) continue;
            var daemons = (await LittleSoulsTenants.AllAsync(documents, system, LittleSoulsApp.Daemon, [], ct)).ToDictionary(d => d.OwnerSubject, d => Obj(d));
            var threads = (await LittleSoulsTenants.AllAsync(documents, system, LittleSoulsApp.Notebook, [LittleSoulsTenants.Eq("kind", "Thread")], ct))
                .GroupBy(d => d.OwnerSubject).ToDictionary(g => g.Key, g => g.Select(d => (d.DocumentKey, Obj(d))).ToList());
            foreach (var profile in await LittleSoulsTenants.AllAsync(documents, system, LittleSoulsApp.Profile, [], ct))
            {
                people++;
                var person = new Person(profile.OwnerSubject, Obj(profile), daemons.GetValueOrDefault(profile.OwnerSubject),
                    threads.GetValueOrDefault(profile.OwnerSubject) ?? []);
                var cache = new Dictionary<string, bool>();
                bool Sent(string key) => cache.TryGetValue(key, out var b) ? b
                    : cache[key] = documents.GetAsync(system, LittleSoulsApp.Outbox, key, ct).GetAwaiter().GetResult() is not null;
                foreach (var msg in Rounds.Decide(person, now, oracle, Sent))
                {
                    string status;
                    try
                    {
                        var outcome = await messages.SendAsync(system, msg.Template, msg.Subject, JsonSerializer.SerializeToElement(msg.Data), msg.Language, ct);
                        status = $"sent to {outcome.Devices} device(s)";
                        sent++;
                        if (msg.ThreadKey is { } tk && await tenants.PersonAsync(tenant, msg.Subject, ct) is { } asPerson
                            && await documents.GetAsync(asPerson, LittleSoulsApp.Notebook, tk, ct) is { } thread)
                        {
                            var t = Obj(thread); t["followedUpAt"] = now.ToString("O");
                            await documents.UpsertAsync(asPerson, LittleSoulsApp.Notebook, tk, t.ToJsonString(), ct, thread.Revision);
                        }
                    }
                    catch (Exception e) when (e is MessageTemplateNotFoundException or SingularFramework.Platform.Storage.ServiceNotBoundException or RecipientNotAllowedException)
                    {
                        status = "not sent: " + e.Message; failed++;
                        logger?.LogDebug(e, "Little Souls {Kind} not sent in {Tenant}.", msg.Kind, tenant);
                    }
                    // recorded either way: the window has passed, and a missed omen is not sent late
                    foreach (var key in msg.OutboxKeys)
                        await documents.UpsertAsync(system, LittleSoulsApp.Outbox, key, new JsonObject
                        {
                            ["kind"] = msg.Kind, ["person"] = msg.Subject, ["at"] = now.ToString("O"), ["status"] = status,
                        }.ToJsonString(), ct);
                }
            }
        }
        return $"{people} people, {sent} sent, {failed} not sent";
    }

    private static JsonObject Obj(DocumentRecord d) => JsonNode.Parse(d.PayloadJson) as JsonObject ?? new JsonObject();
}

/// <summary>
/// Every 10 minutes: closes conversations quiet for 30 minutes. For each, as the person: load the notebook, ask
/// <see cref="IProposalSource"/> for proposals, let <see cref="CoherenceGate"/> decide, end the session (quarantines
/// lapse), and write the notebook back.
/// </summary>
public sealed class SessionCloseJob(LittleSoulsTenants tenants, DocumentAccess documents, IProposalSource proposals,
    TimeProvider? clock = null, ILogger<SessionCloseJob>? log = null) : IScheduledJob
{
    public const string Name = "little-souls-session-close";
    public static readonly TimeSpan Quiet = TimeSpan.FromMinutes(30);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<string> RunAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        int closed = 0, proposed = 0, kept = 0, failed = 0;
        foreach (var tenant in tenants.TenantSlugs())
        {
            if (await tenants.SystemAsync(tenant, ct) is not { } system) continue;
            foreach (var conv in await LittleSoulsTenants.AllAsync(documents, system, LittleSoulsApp.Conversations, [], ct))
            {
                if (now - new DateTimeOffset(DateTime.SpecifyKind(conv.UpdatedAtUtc, DateTimeKind.Utc)) < Quiet) continue;
                string sessionId = $"{conv.DocumentKey}@{conv.Revision}";
                string outboxKey = $"session:{conv.OwnerSubject}:{sessionId}";
                if (await documents.GetAsync(system, LittleSoulsApp.Outbox, outboxKey, ct) is not null) continue;
                if (await tenants.PersonAsync(tenant, conv.OwnerSubject, ct) is not { } person) continue;

                var stored = (await LittleSoulsTenants.AllAsync(documents, system, LittleSoulsApp.Notebook, [], ct))
                    .Where(d => d.OwnerSubject == conv.OwnerSubject)
                    .ToDictionary(d => d.DocumentKey, d => (Record: d, Doc: JsonNode.Parse(d.PayloadJson) as JsonObject ?? new JsonObject()));
                var daemon = await documents.GetAsync(person, LittleSoulsApp.Daemon, conv.OwnerSubject, ct);
                var daemonDoc = daemon is null ? null : JsonNode.Parse(daemon.PayloadJson) as JsonObject;
                var nb = NotebookDocuments.Load(conv.OwnerSubject, stored.Select(s => (s.Key, s.Value.Doc)), daemonDoc);

                int stage = daemonDoc?["stage"]?.GetValue<int>() ?? 1;
                // Only the messages since this conversation's last closed session: each is read once.
                var conversation = JsonNode.Parse(conv.PayloadJson) as JsonObject ?? new JsonObject();
                var closedUpTo = daemonDoc?["notebook"]?["closed"] as JsonObject ?? new JsonObject();
                int upTo = closedUpTo[conv.DocumentKey] is JsonValue u && u.TryGetValue<int>(out var n) ? n : 0;
                var profile = await documents.GetAsync(person, LittleSoulsApp.Profile, conv.OwnerSubject, ct);
                var slice = NewPart(conversation, upTo, profile is null ? null : JsonNode.Parse(profile.PayloadJson) as JsonObject);
                IReadOnlyList<Proposal> list;
                try { list = await proposals.ProposeAsync(slice, ProfileCard.Render(nb, stage, now), ct); }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    // the session stays open and the next run tries again
                    log?.LogWarning("Proposals failed for a session in {Tenant}: {Message}", tenant, e.Message);
                    failed++;
                    continue;
                }
                proposed += list.Count;
                var report = CoherenceGate.Apply(nb, list, sessionId, now);
                kept += report.Added.Count + report.Reinforced.Count + report.Replaced.Count;
                nb.EndSession();

                var (docs, meta) = NotebookDocuments.Save(nb, stored.ToDictionary(s => s.Key, s => s.Value.Doc));
                foreach (var (key, doc) in docs)
                {
                    if (stored.TryGetValue(key, out var old) && JsonNode.DeepEquals(old.Doc, doc)) continue;
                    await documents.UpsertAsync(person, LittleSoulsApp.Notebook, key, doc.ToJsonString(), ct, old.Record?.Revision);
                }
                if (daemon is not null && daemonDoc is not null)
                {
                    closedUpTo[conv.DocumentKey] = slice["count"]!.GetValue<int>();
                    meta["closed"] = closedUpTo.DeepClone();
                    daemonDoc["notebook"] = meta;
                    await documents.UpsertAsync(person, LittleSoulsApp.Daemon, conv.OwnerSubject, daemonDoc.ToJsonString(), ct, daemon.Revision);
                }
                await documents.UpsertAsync(system, LittleSoulsApp.Outbox, outboxKey, new JsonObject
                {
                    ["kind"] = "session", ["person"] = conv.OwnerSubject, ["at"] = now.ToString("O"),
                    ["status"] = $"{list.Count} proposed; added {report.Added.Count}, reinforced {report.Reinforced.Count}, replaced {report.Replaced.Count}, quarantined {report.Quarantined.Count}, dropped {report.Dropped.Count}",
                }.ToJsonString(), ct);
                closed++;
            }
        }
        return $"{closed} sessions closed, {proposed} proposals, {kept} kept" + (failed > 0 ? $", {failed} to retry" : "");
    }

    /// <summary>The conversation from message <paramref name="upTo"/> on, with the person's time zone and the full count.</summary>
    public static JsonObject NewPart(JsonObject conversation, int upTo, JsonObject? profile)
    {
        var messages = conversation["messages"] as JsonArray ?? [];
        int from = upTo <= messages.Count ? Math.Max(0, upTo) : 0; // shorter than before: the conversation was reset
        return new JsonObject
        {
            ["messages"] = new JsonArray(messages.Skip(from).Select(m => m?.DeepClone()).ToArray()),
            ["timeZone"] = profile?["timeZone"]?.GetValue<string>() is { } tz && ProfileRules.TimeZoneExists(tz) ? tz : "UTC",
            ["count"] = messages.Count,
        };
    }
}

/// <summary>When the person deletes a belief in the Grimoire, the notebook remembers to forget it: never inferred back.</summary>
public sealed class NotebookForgetHook(DocumentAccess documents) : IDocumentHook
{
    public ValueTask BeforeAsync(DocumentHookContext context, CancellationToken ct) => ValueTask.CompletedTask;

    public async ValueTask AfterAsync(DocumentHookContext context, CancellationToken ct)
    {
        if (context.App.AppSlug != LittleSoulsApp.Slug || context.Operation != DocumentOperation.Delete) return;
        string? subject = context.App.Caller?.Subject;
        if (subject is null || subject == LittleSoulsApp.SystemSubject) return;
        var key = (JsonNode.Parse(context.BeforeJson) as JsonObject)?["key"]?.GetValue<string>() ?? context.DocumentKey;
        if (await documents.GetAsync(context.App, LittleSoulsApp.Daemon, subject, ct) is not { } daemon) return;
        var d = JsonNode.Parse(daemon.PayloadJson) as JsonObject ?? new JsonObject();
        var nb = d["notebook"] as JsonObject ?? new JsonObject();
        var forgotten = nb["forgotten"] as JsonArray ?? new JsonArray();
        string fp = NotebookDocuments.Fingerprint(key);
        if (forgotten.Any(f => f?.GetValue<string>() == fp)) return;
        forgotten.Add(fp);
        nb["forgotten"] = forgotten.DeepClone(); d["notebook"] = nb.DeepClone();
        await documents.UpsertAsync(context.App, LittleSoulsApp.Daemon, subject, d.ToJsonString(), ct, daemon.Revision);
    }
}
