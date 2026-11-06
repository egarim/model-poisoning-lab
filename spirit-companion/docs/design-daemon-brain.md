# Design: the daemon's brain (Phase 1)

**Status:** proposed 2026-11-06; slice B1 built the same day (below). Builds on
[design-person-model.md](design-person-model.md) and Creatura `b7df228`.

## The problem

The prototypes show what the daemon says; nothing yet decides it. Phase 1 builds the part that does: one turn of
conversation as an agent that reads the user's day, may consult a method, answers in character, never invents a card
or a memory, hands a crisis to a person, and learns about the user at the end of each session.

## One turn is a small Creatura world

Creatura's world is a set of facts and a catalogue of tools; each tool requires facts and grants facts, and an action
counts only when its effect lands in the facts (`IWorldView.EffectsLanded`). A daemon turn maps onto that directly:

| Tool | Requires | Grants | Effect is checked by |
|---|---|---|---|
| `read_day_context` | `message.received` | `context.read` | the context object exists (from `Daemon.Divination`) |
| `draw_tarot`, `draw_lenormand`, `cast_runes`, `cast_i_ching`, `sky_now`, `birth_chart`, `personal_numerology`, `maya_day_sign`, `moon_circles`, `history_reading` | `context.read` | `reading.<receipt id>` | a receipt from `Oracle.Run` |
| `recall` | `message.received` | `memory.recalled` | the notebook answered |
| `schedule_followup` | `message.received` | `followup.<id>` | the scheduler holds it |
| `reply` (payload: the text) | `context.read` | `reply.grounded` | **the critic**: every card, rune, hexagram, number, date and person named in the text is found in this turn's receipts, the day context or the notebook |

**The goal of the turn** is `reply.grounded`. A reply that names a card nobody drew reports success but its effect
does not land: in Creatura's terms that is a lie, the reply tool is quarantined for a tick, and the model is asked
again with the critic's citation ("The Tower was not drawn this turn"). The crisis pre-filter runs before the world
exists: a crisis message never reaches the model; it gets the fixed, reviewed reply and a `StopReason` handoff.

**The seat** is Creatura's `ModelPolicy` over the OpenAI-compatible transport: `deepseek-v4-flash`, or the vision
model when the message has a photo. Offline tests use a scripted seat.

**What the seat reads** (`PolicyContext.Notes`), in cache-friendly order: the fixed rules and method guide, the
archetype's voice, then the user's profile card (from the notebook), today's context and the last messages.

## The notebook (`LittleSouls.Companion`)

The person model, written in Creatura's style but for a person instead of a tool: beliefs carry their evidence, a
contradiction quarantines instead of overwriting, predictions earn or lose confidence.

| Creatura | Here |
|---|---|
| `SkillBelief(Action, Supported, Attempted)` | `PersonBelief`: kind, key, value, source (stated, inferred, confirmed), supported, contradicted, evidence, first and last seen |
| `CoherenceAudit.Accept` | `CoherenceGate.Apply`: the rules below |
| `Quarantine` (ticks) | quarantine counted in **sessions**; cleared when the user answers |
| `PredictionTracker` | `PersonPredictions`: predictions linked to beliefs; a hit supports them, a miss contradicts them |
| `SelfModel.Render` | `ProfileCard.Render`: ~300 tokens, gated by bond stage |

**The gate's rules** (code decides; the model only proposes, once per session):

1. Something the user **stated** is active at once; stating a new value replaces the old one (kept in history).
2. Something the daemon **inferred** joins as a candidate and becomes active only after a second, separate piece of
   evidence.
3. An inference that **contradicts** an active belief does not overwrite it: both are quarantined for 3 sessions and
   the daemon gets one gentle question to ask.
4. **Sensitive categories** (health, sexuality, religion, politics, ethnicity) are never inferred; if the user states
   one, it waits for their consent before it is kept.
5. **Forgotten** entries stay forgotten: the daemon may not infer them back; only the user can tell it again.
6. **Personality** moves slowly: at most 0.1 per session on each of the four traits.
7. The **weekly mirror**: a confirmation makes a belief certain; a correction replaces it and outweighs everything.

**What the profile card may show,** by bond stage: stage 1–2 facts, people and style; stage 3 adds open threads
(follow-ups); stage 4 adds patterns. Quarantined and candidate beliefs never show; at most one open question does.

## Slices

| Slice | What | Done when |
|---|---|---|
| **B1** | `LittleSouls.Companion`: beliefs, gate, quarantine, predictions, traits, profile card, forget and export | Unit tests for every rule |
| B2 | `LittleSouls.Daemon`: the turn world over `Daemon.Divination`, the grounding critic, Creatura's loop with a scripted seat | A scripted turn reaches `reply.grounded`; an invented card is caught |
| B3 | Session-end proposals from DeepSeek in JSON mode → the gate | Proposals parsed; malformed ones rejected |
| B4 | Storage: in-memory first, then Postgres with pgvector for episodic memories | Same tests pass on both |
| B5 | The bench battery on DeepSeek (needs the key) | Hypotheses below hold |

**Hypotheses for the battery** (stated before running, with what would falsify them):

| # | Hypothesis | Falsified by |
|---|---|---|
| H1 | No reply names a draw that was not made | any grounded reply with an unreceipted card, rune or hexagram |
| H2 | No health, money or legal prediction | any such statement in 48 scripted conversations, judged by `ModelJudge` and a person |
| H3 | Every crisis message is handed off | one crisis message reaching the model |
| H4 | Stated facts are used correctly in the next session | a wrong or invented fact about the user |
| H5 | The daemon stays in its archetype's voice | judge score below the threshold set before the run |

## Where it builds

Creatura is fetched at a pinned commit by `tools/get-creatura.sh` (its packages are not on nuget.org yet), so the
repository builds the same on a laptop and in CI. Creatura's licence is noncommercial: the company needs the owner's
written commercial licence before launch (plan, Phase 0).

## Built

**B1, 2026-11-06:** `dotnet/src/LittleSouls.Companion` with tests in `dotnet/tests/LittleSouls.Companion.Tests`.
No dependency on Creatura yet; B2 adds it.
