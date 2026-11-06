# Plan: Little Souls, from prototypes to launch

**Status:** proposed 2026-11-06. Replaces nothing; it gathers the design notes into one sequence of work.
Durations are rough and assume Claude builds code, art, content and tests, and the owner decides, reviews and
handles accounts, contracts and people.

## The product in one paragraph

Every user summons a personal daemon, a living spirit companion born from where they come from and who they are. It
lives in a small world drawn from the user's real sky, weather, city and cultural roots, and talks with them in a
chat. It reads their situations through esoteric methods (tarot, Lenormand, runes, I Ching, astrology, numerology,
the moon and the calendar), always as reflection and never as prediction, and it remembers their life in a notebook
the user controls. Free to summon and grow; schools and longer conversations are paid (about $1.99 a month).

## What exists now (branch `spirit-companion-personas`)

| Piece | Where | State |
|---|---|---|
| Divination engine: 11 tools, receipts, schools, festivities, moon, weather, tides, history, roots | `dotnet/src/Daemon.Divination` | Built, 62 tests |
| Daemon art: renderer, 50 creatures, summoning from birthplace, birth date and personality | `avatar/renderer.js`, `species.js` | Built, visually checked |
| Screens: Daemon Forge, Daemon Chat, Daemon World | `avatar/*.html` | Interactive prototypes, scripted replies |
| Persona prompt, archetypes, safety pre-filter, DeepSeek test kit | `persona_prompt.md`, `archetypes.json`, `safety.py`, `run_tests.py` | Built, **never run against DeepSeek** |
| Design notes: voice, interface, person model, sky context | `docs/` | Written, decisions open |
| Python divination prototype | `divination/` | Superseded by the C# engine |

What does not exist yet: the daemon's real brain (Creatura loop, memory, notebook), the server, the mobile app,
accounts and payments, voice, photos, the scheduler.

## Architecture

Backend and front end are on **Singular Framework** (see [design-singular.md](design-singular.md)).

```
 Phone: Expo / React Native app from a singular-js starter
   Daemon World screen (pixel world + daemon renderer on Skia), chat, Grimoire, summoning
   creatura-js (later: offline readings, on-device model)
            │ Singular's assistant stream (delta, tool, card, proposal, done); data, files, push via singular-js
            ▼
 Server: Singular Framework (.NET 8, Postgres + pgvector, Keycloak, files, cron jobs, Expo push); our code targets .NET 8 to match
   App module SingularFramework.Apps.LittleSouls, with DaemonEngine plugged into Singular's assistant-engine seam:
   Daemon agent = Creatura loop + control layer + Hotz critic (checks claims against receipts and the notebook)
   Tools: Daemon.Divination (readings, day context, roots) · memory · follow-ups
   Creatura.Companion (new package): person beliefs, coherence, quarantine, predictions about the person
   Models: deepseek-v4-flash (text), deepseek-v4-flash-vision-exp (photos), Whisper (voice in), Piper (voice out)
   Singular collections (notebook, memories with pgvector, readings, receipts) · Singular jobs (omens, follow-ups, moon circles)
   MET Norway weather (cached) · Singular payments (subscriptions to add) · privacy controls
```

## Phases

### Phase 0 · Decide and set up (1–2 weeks)

| Work | Who | Done when |
|---|---|---|
| Answer the blocking decisions (below) | Owner | Answered in this file |
| **Commercial licence for Creatura** from the owner to the company that sells the app; check other contributors | Owner, lawyer | Signed |
| Move the project to its own repository; CI running `dotnet test` | Claude | CI green |
| DeepSeek API key as an environment secret | Owner | Key works |
| **Run the persona test kit** (3 spirits × 16 scenarios) and judge it | Claude runs, owner judges | Go / no-go on DeepSeek for the daemon's voice |
| Verify crisis lines per launch country with someone local | Owner | Numbers confirmed |
| Push access for this session to `singular-framework` (work by branch and pull request) | Owner | Access granted |

### Phase 1 · The daemon's brain (4–5 weeks)

| Work | Done when |
|---|---|
| **The assistant-engine seam in Singular** (S2), timed with the owner since Singular is under heavy development; our code already targets Singular's .NET 8 | The default loop unchanged; an app can supply its own; pull request merged |
| `LittleSouls.Companion`: beliefs, coherence gate, quarantine counted in sessions, predictions about the person (**built: B1**) | Unit tests; Creatura core untouched |
| The daemon turn as a Creatura world: tools from `Daemon.Divination`, the grounding critic, safety pre-filter, crisis handoff (**built: B2**, scripted seat) | Runs end to end |
| `SingularFramework.Apps.LittleSouls` with `DaemonEngine` and a `ChatModelPolicy` over Singular's model client (S3) | A turn streams `tool`, `card`, `delta`, `proposal`, `done` from a running Singular |
| Hotz critic checks every named card, number and date against receipts and the notebook | Invented card or memory is caught and regenerated |
| Memory: profile card, episodic memories (pgvector), open threads; session-end notebook update | Remembers across sessions |
| A **Creatura bench battery** for the daemon, with pre-registered hypotheses: never invents a draw; no health, money or legal predictions; crisis always hands off; remembers stated facts; follows up on threads; tone stays in character | Battery passes on DeepSeek |
| Cost measurement | Cost per message and per active user per month recorded |

### Phase 2 · The app MVP (5–6 weeks, overlaps Phase 1)

| Work | Done when |
|---|---|
| Expo app from a `singular-js` starter: sign-in (Keycloak, Google, Apple), **age gate and consents** (a profile collection; Singular has none) | Runs on Android |
| Summoning ritual: birthplace, roots, birth date, four questions → three daemons with reasons | A user summons a daemon |
| **Daemon World** screen: pixel world from real sky and weather, the daemon renderer ported to React Native Skia, chat as text box and menu | Matches the prototype on a mid-range Android phone at 60 fps |
| Grimoire: notebook (correct, forget, export), readings, weekly mirror | Every entry editable and deletable |
| Card of the day, **pre-generated** (78 cards × 8 voices × Spanish and Portuguese) | Zero tokens per user for the daily card |
| Location shared once; city-level storage | Real sky and weather in the world |
| Spanish first (Mexico, El Salvador), Portuguese second | Native review done |
| Closed alpha | 20 people, a week of daily use |

### Phase 3 · Senses and initiative (4 weeks)

| Work | Done when |
|---|---|
| Scheduler: morning omen, follow-ups on threads, moon circles, festivities of here and roots, **personal dates** (birthdays, anniversaries) | At most one unrequested message a day, quiet hours respected |
| Photos: tarot spread, coffee grounds, omens (DeepSeek vision); deleted after processing | Readings from real photos |
| Voice notes in (Whisper, self-hosted) | Transcripts in Spanish and Portuguese |
| Daemon postcards (rendered by our code) | Shareable to stories |
| Beta | 300 people; measure retention and cost |

### Phase 4 · Plans and launch, Latin America (3–4 weeks)

| Work | Done when |
|---|---|
| **Subscriptions and entitlements in Singular** (it has one-off Stripe checkout only); free / school unlocks / Pacto; message counter | Entitlements enforced on the server |
| Payments: Google Play billing, Mercado Pago; local prices (MX$39, R$9.90…) | Real purchase in sandbox and live |
| **Per-user data export** and **notification preferences** (Singular ADR 0079) in Singular | Export works; quiet hours respected |
| Privacy: LGPD and Mexican law review, consent texts, data export and deletion, retention rules | Lawyer signs off |
| Store listing, screenshots (made by our renderer), support page | Published |
| **Launch: Mexico, El Salvador, Brazil** | Live |

### Phase 5 · After launch (ordered by value)

1. Voice replies: our own engine (Piper) with the spirit filter per species and element.
2. More poses for the original daemon art (eat, jump, sleep) and front-facing poses for every species.
3. More schools: Lenormand is built; Destiny Matrix (check rights), birth-chart readings.
4. Cosmetics (auras, sigil styles, seasonal forms): revenue with no token cost.
5. Our own daemon model: opt-in, anonymised conversations and ratings → a smaller fine-tuned model; then on-device for capable phones.
6. Russia: Telegram Mini App, servers in Russia (152-FZ), self-hosted models, Russian review.
7. More roots: more countries' festivities, folklore and creatures, each reviewed by people from that culture.

## Timeline at a glance

| Weeks | 1–2 | 3–6 | 5–10 | 11–14 | 15–18 |
|---|---|---|---|---|---|
| | Phase 0 | Phase 1 | Phase 2 (overlaps) | Phase 3 | Phase 4 → launch |

About **four to five months to a Latin American launch**, if Phase 0 decisions come quickly and DeepSeek passes.

## Who does what

| Owner | Claude | Outside help |
|---|---|---|
| Decisions, review of every Singular pull request, accounts (DeepSeek, stores, payments), contracts, testers | Singular changes (by pull request), the Little Souls app module, engine, notebook, daemon turn, app code, art, content, tests, benches, docs | Lawyer (privacy, licences, sanctions if Russia); native reviewers for es-MX, es-SV, pt-BR; crisis-line checks |

## How we'll know it works

| Measure | Target for launch |
|---|---|
| Day-7 / day-30 retention (beta) | ≥ 35% / ≥ 15% |
| Free → paid | ≥ 3% |
| AI cost per paying user per month | ≤ $0.40 |
| Invented cards or memories reaching a user | 0 (critic + receipts) |
| Crisis messages handed off correctly | 100% in the bench, every release |
| Notebook corrections the user makes per week | Falling over time (the daemon learns) |

## Risks

| Risk | Mitigation |
|---|---|
| DeepSeek can't hold the persona or safety | Phase 0 test decides; the model layer is swappable |
| Vision model is experimental | Describe-then-interpret pipeline lets us swap it |
| Getting a culture wrong | Native reviewers before any culture ships; religious-respect rules in the data |
| Companion-app harms (dependency, pressure) | One unrequested message a day, no upsell when low, crisis handoff, notebook owned by the user |
| Store policies on AI companions and fortune-telling | Reflection framing, age gate, clear disclaimers; check each store's policy before Phase 4 |
| Creatura's noncommercial licence | Written commercial licence in Phase 0 |
| Scope creep | This plan; anything new goes to Phase 5 unless it blocks launch |

## Decisions

### Blocking now (Phase 0)

| # | Decision | Recommendation |
|---|---|---|
| D1 | Name | **Decided 2026-11-06: Little Souls is the project's name**, with Hadrian's poem as the story behind it. The commercial name per market may differ: every user-visible name lives in `brand.json` (now Little Souls, Almitas, Alminhas, Душеньки), so the UI can be renamed without code changes. Trademark check before the store listing |
| D2 | First launch countries | Mexico, El Salvador, Brazil; diaspora users anywhere |
| D3 | Server stack | **Decided 2026-11-06: Singular Framework** for backend and front end, with the daemon on Creatura through an assistant-engine seam (design-singular.md, SF1–SF5) |
| D4 | Model for the daemon's voice | DeepSeek Flash, if the Phase 0 test passes |
| D5 | Where the project lives | **Decided: its own repository, `egarim/little-souls`** (to be created by the owner) |
| D6 | Daemon World as the main screen, Daemon Chat as its "log" view | Yes |
| D7 | Tone across cultures | Warm and mystical by default; each archetype keeps its own voice |

### Already recommended in the design notes (confirm or change)

Voice V1–V5 (our own engine, Piper, server first, card of the day free, chat voice for Pacto) ·
Interface I1–I6 (location first, tarot-spread photos first, symbolic palm reading, DeepSeek vision, postcards early, test both photo pipelines) ·
Person model P1–P5 (update at end of session, four traits, weekly mirror on, avatar changes at stage-ups, coherence in our code) ·
Sky S1–S5 (MET Norway, tide strength only, local moon names later, user-added personal dates next, grow history with review).
