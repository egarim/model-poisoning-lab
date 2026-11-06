# Design: Little Souls on Singular Framework

**Status:** proposed 2026-11-06. Nothing built in Singular yet. Based on `egarim/singular-framework` at `0c34f9c`
(read-only in this session). Decisions at the end are open.

## The problem, in the owner's words

> The backend is Singular Framework, and also the front end will be built there. Do you think we can do it?

Yes. Singular already has most of what the app needs; four pieces are missing and one is blocking.

## What Singular already gives us

| Little Souls needs | Singular has (evidence) | Use |
|---|---|---|
| Server code in C# | Apps are compiled C# libraries in the host (`SingularFramework.Apps.Cosmos`, `AddCosmosApp`; ADR 0013) | A `SingularFramework.Apps.LittleSouls` module holds the engine, notebook and daemon turn |
| The model | Server assistant, OpenAI-shaped, **defaults to `deepseek-v4-flash`** (`AssistantService`, `Assistant:BaseUrl/Model/ApiKey`) | One provider configuration and one key for everything |
| The chat stream | SSE on `POST /api/app/assistant`: `delta`, `tool` (start/end), `card`, `proposal`, `done` (`sdk/singular-js/src/assistant.ts`) | The Daemon World's vocabulary (below) |
| The model never writes | `propose` writes nothing; the person confirms; the app writes with `expectedRevision` (ADR 0030, 0049) | Things the user states become proposals: "Itzel will remember: … · Keep · Forget" |
| Notebook, memories, readings, receipts | Postgres jsonb documents, JSON Schema per collection, owner-scoped access (ADR 0007, 0016, 0020) | One owner-scoped collection each |
| Memory search | pgvector per collection, `.similar(field, vector)` (`CollectionDefinition.VectorField`, `DocumentQuery.VectorSearch`) | Episodic memories |
| Sign-in | Keycloak 26, password, Google, Apple (ADR 0047) | As is |
| Omens, follow-ups, moon circles | Cron jobs with DB claims (`IScheduledJob`); Expo push (`ExpoPushSender`) | A job every 15 minutes wakes the people whose local morning it is |
| Photos, voice | Files with presigned URLs (ADR 0021); **dictation** `POST /api/app/assistant/transcribe` (ADR 0051) | Photo readings; voice notes in |
| Limits and audit | `AssistantUsage` metering, `AppRateLimitMiddleware`, activity stream (ADR 0022, 0068) | Cost per person; the daily message counter is ours |
| Deleting an account | `DELETE /api/app/me` with per-collection rules (ADR 0023) | The notebook is deleted with the account |
| The app | `singular-js` for Expo/React Native: auth, data, realtime, files, push, assistant streaming, OTA, starters | The Expo app starts from a `singular-js` template |
| Hosting | Self-hosted Docker: Postgres (+pgvector), Keycloak, S3/Azure storage (`deploy/`) | Data stays in our own database |

### The stream, mapped to the Daemon World

| Singular event | What the person sees |
|---|---|
| `tool {phase:"start"}` | The daemon gathers light and its sigil spins ("consulting") |
| `card {kind:"tarot"\|"rune"\|"hexagram"\|"sky"\|"festivity", data}` | A card flips in, the sky changes, a festivity appears in the world |
| `delta {text}` | The text box types the reply |
| `proposal {proposal}` | "Itzel will remember: Camila, your younger sister · Keep · Forget" |
| `done {conversation, text, proposals, usage}` | The menu returns |

**One change from Singular's default:** the default assistant streams text as the model writes it. The daemon must not
show text the grounding critic has not approved, so the daemon engine emits `tool` and `card` events while it works and
the reply's `delta` events only after it is grounded. The person sees the daemon consulting, then the text types out.

## What is missing

| # | Gap | Proposal | Where |
|---|---|---|---|
| G1 | **.NET 8 vs .NET 10** (blocking). Singular targets `net8.0`; our libraries and Creatura target `net10.0`, which a .NET 8 host cannot load. To my knowledge .NET 8 support ends on 10 November 2026; verify on Microsoft's support policy page. | **Move Singular to .NET 10.** Multi-targeting our code and Creatura to `net8.0` is possible but work thrown away later | Singular |
| G2 | **The assistant loop is fixed.** `AssistantService.ChatAsync` runs its own 6-round loop; no public hook sees a tool call | **An assistant-engine seam** (below): an app may supply the loop; Singular keeps everything around it | Singular |
| G3 | **No subscriptions or entitlements.** Payments are one-off Stripe checkout (ADR 0056) | Subscriptions with Google Play billing and Mercado Pago, and entitlements a server can check (the daemon's school unlocks, Pacto) | Singular |
| G4 | **No age or consent fields** | App-level: a `profile` collection with birth year, consents (terms, AI processing, the notebook, sensitive topics) and their dates; sign-up blocks under the age set per country | Little Souls app |
| G5 | **No per-user data export** (deletion exists) | A platform export of everything a person owns (GDPR art. 15, LGPD art. 18); the notebook's own `Export()` is ready for the readable part | Singular |
| G6 | **Notification preferences and quiet hours** are designed (ADR 0079) but not built | Build ADR 0079; the daemon's one-message-a-day rule sits on top | Singular |

### G2: the assistant-engine seam

`ChatAsync` already does, in order: resolve the assistant, check roles, validate the message, check the quota, load
the history, **run the loop**, save the conversation, record usage, emit `done`. Only the loop changes hands:

```csharp
public interface IAssistantEngine
{
    Task<AssistantEngineResult> RunAsync(AssistantEngineContext context, CancellationToken ct);
}

public sealed class AssistantEngineContext
{
    public required ApplicationContext App { get; init; }        // the signed-in person, as today
    public required string Message { get; init; }
    public required JsonArray History { get; init; }
    public JsonObject? Context { get; init; }                     // the app's request context (location shared once, etc.)
    public required ChatModel Model { get; init; }                // the configured provider; usage is counted by Singular
    public required Func<string, JsonObject, Task<JsonNode?>> RunBuiltInTool { get; init; } // query/get/aggregate/propose as the person
    public required Func<string, object, Task> Emit { get; init; } // delta, tool, card, proposal
}

public sealed record AssistantEngineResult(string Answer, JsonArray Proposals, JsonArray Cards, int PromptTokens, int CompletionTokens);

// registration, next to the existing options
new AssistantApp("little-souls", Readable: [...], Proposable: ["notebook"], Engine: sp => sp.GetRequiredService<DaemonEngine>());
```

With no engine set, today's loop runs, so other apps don't change. It's the generic seam Creatura's mobile note asks
of Singular ("a guard sees every tool call"), shaped as "an app supplies the loop".

**The Little Souls engine** (`DaemonEngine`) adapts what exists: the crisis filter first; `TurnWorld` and
`DaemonTurn` on Creatura's `ReferenceLoop`; a `ChatModelPolicy : IPolicy` over Singular's `ChatModel`, so tokens are
metered by Singular and there is one key; the notebook and readings read through `RunBuiltInTool` as the person;
statements become `propose` calls; receipts and the day context go out as `card` events.

## The app module

```
SingularFramework.Apps.LittleSouls        (compiled into the host, like Cosmos)
  AddLittleSoulsApp(...)                  collections, the assistant with its engine, jobs
  Collections (owner-scoped): profile · daemon · notebook · memories (vector) · readings · receipts · plates (shared actions)
  DaemonEngine : IAssistantEngine         uses Daemon.Divination, LittleSouls.Companion, LittleSouls.Daemon, Creatura
  Jobs: MorningOmenJob (every 15 min, by local time) · FollowUpJob · MoonCircleJob · SessionCloseJob (notebook gate)
  Hooks: on notebook proposals confirmed → CoherenceGate
```

The Expo app starts from a `singular-js` template: summoning, the Daemon World screen (the renderer ported to React
Native Skia), the Grimoire, settings, plans.

## Licences

Singular is PolyForm Small Business (free under 100 people and US$1M revenue; the owner holds the copyright).
Creatura is PolyForm Noncommercial: the company needs the owner's written commercial licence (plan, Phase 0).

## Order of work

| Step | What | Where | Needs |
|---|---|---|---|
| S1 | Singular to .NET 10; all its tests green | `singular-framework`, a branch and a pull request | Push access for this session |
| S2 | The assistant-engine seam with tests (the default loop unchanged) | `singular-framework`, pull request | S1 |
| S3 | `SingularFramework.Apps.LittleSouls`: collections, `DaemonEngine` with a scripted seat, `ChatModelPolicy` | Little Souls repository, referenced by the host | S2 |
| S4 | Expo app from the starter: sign-in, summoning, the Daemon World screen talking to the real stream | Little Souls repository | S3 |
| S5 | Jobs, push, preferences (G6), export (G5), subscriptions (G3) | Both | Phases 3 and 4 |

## Decisions for the owner

| # | Decision | Options | Recommendation |
|---|---|---|---|
| SF1 | .NET version | Singular to .NET 10; our code to .NET 8 | **Singular to .NET 10** |
| SF2 | How the daemon runs inside Singular | an engine seam; daemon tools inside Singular's own loop (no critic); a separate service | **The engine seam** |
| SF3 | Who changes Singular | Claude by pull requests; the owner | **Claude by pull requests**, reviewed by the owner (it runs other apps) |
| SF4 | Where the app module lives | the Little Souls repository; Singular's `src/` | **Little Souls repository**, referenced by the host build |
| SF5 | Subscriptions | in Singular for every app; only in Little Souls | **In Singular**, since other apps will want them |
