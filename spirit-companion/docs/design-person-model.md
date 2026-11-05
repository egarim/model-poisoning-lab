# Design: the daemon knows its person (the person model)

**Status:** proposed 2026-11-05. Documented only; nothing built. Decisions at the end are open.
Builds on [design-interface.md](design-interface.md) (what the user sends) and Creatura's self-model, coherence and
orienting designs, which this note reuses for a model of the *person* instead of a model of a tool.

## The problem, in the owner's words

> What about personality? We need to be interactive and keep a model of the person.

## The answer, short

Each daemon keeps a **notebook about its person**: facts, open situations, patterns, personality and how they like to
be spoken to. Every belief in it has a confidence and its evidence. It grows from conversation, from small
interactive rituals, and from the user correcting it. The user can always read, correct and delete it. It is used for
one purpose only: to make the daemon a better companion to that person.

The daemon is interactive because the notebook gives it reasons to speak first: an open situation to follow up, a
pattern it noticed, a belief it is unsure of and wants to check.

## What the notebook holds

| Layer | Examples | How it is kept |
|---|---|---|
| **Facts** | name, people ("Camila, younger sister"), job, city, birthday, pets | Stated by the user; high confidence; updated when they change |
| **Open threads** | "interview in Mexico City, Friday", "hasn't answered her father" | Each with a status and a follow-up date; the source of most proactive messages |
| **Patterns** | "work anxiety comes up on Sunday nights", "The Star drawn 3 times this month", mood over weeks | Computed from history; shown to the user as observations, never as diagnoses |
| **Personality** | energy, social, heart/head, tradition/rebel: the four summoning traits | Start from the summoning answers; refined slowly from evidence (below) |
| **Communication style** | short or long replies, humour welcome or not, directness, emoji, language mix, active hours | Learned from reactions and corrections; changes the daemon's tone quickly |
| **Goals and values** | "become a tattoo artist", "more time with family" | Stated by the user; the daemon refers back to them |
| **Readings history** | cards and methods drawn, which readings the user saved or rated | Feeds better readings and the weekly insight |
| **The relationship** | bond stage, shared memories, inside jokes, the daemon's own Affect resting levels | The daemon's side of the bond; drives the avatar's growth |

Every entry carries: **value, confidence (0–1), evidence (which messages or rituals), first and last seen, and
whether the user confirmed it**.

## How beliefs are formed (from Creatura)

| Creatura idea | Use here |
|---|---|
| **Self-model + coherence** (Bach) | The notebook is the daemon's model of the person. A new belief joins only if it is coherent with what is already held |
| **Quarantine on contradiction** | "Says she loves her job" vs "dreads Mondays at work": neither wins. The belief is quarantined and the daemon asks about it gently, later, in conversation |
| **Prediction tracker** | The daemon makes small private predictions ("this interview will make her nervous"). When the user's next messages confirm or refute them, its confidence in that part of the notebook rises or falls |
| **Orienting and habituation** (Sokolov) | Something new in the user's life (a new name, a new place, a sudden mood change) makes the daemon orient: ask about it, open a thread. Repeated topics stop triggering it |
| **Stages** | What the daemon may *do* with the notebook grows with the bond: stage 1 uses facts only; stage 3 follows up on threads; stage 4 shares patterns and weekly insights |

## The update loop (cheap on tokens)

1. **During a conversation:** the daemon uses a compact **profile card** (~300 tokens: key facts, open threads,
   style) plus the 3–5 most relevant memories. No extra calls.
2. **At the end of a session** (one batched call, not per message): DeepSeek in JSON mode reads the session and
   *proposes* changes: new facts, thread updates, evidence for traits, style signals, a possible pattern.
3. **The coherence check, in our code, not the model:** proposals are merged by rules. Stated facts replace old ones;
   inferences need repeated evidence; contradictions are quarantined; anything in a sensitive category is dropped unless
   the user stated it and agreed to keep it.
4. **Personality moves slowly:** each trait is a running estimate with confidence, moving a small step per piece of
   evidence. One bad day never rewrites who someone is.
5. **Weekly:** the daemon shares "what I learned about you this week" as a short card the user can confirm or
   correct. Corrections carry the highest weight of all.

Cost: one extra call per session, using the cached prompt start, so about one more message's worth of tokens per
conversation.

## Interactive by design: ways the daemon learns by playing

| Ritual | What it is | What it teaches the daemon |
|---|---|---|
| **"Would you rather" omens** | Two images or symbols, pick one | Traits, in small steps |
| **Card of the day reaction** | "Did this card fit your day?" with a tap | Which readings resonate; how they see themselves |
| **Mood at the threshold** | A one-tap mood when the user opens the app | Mood history; patterns |
| **The weekly mirror** | "What I learned about you", confirm or correct | Accuracy of the whole notebook |
| **Story questions** | Occasional deeper questions, at most one a day: "When did you last feel brave?" | Values, goals, people |
| **Follow-ups** | "The interview was today… how did it go?" | Closes threads, checks predictions |

Interaction rules: at most one question the user didn't ask for per day, always skippable, never during distress, never
as a pre-condition for anything.

## How the person model changes the daemon

- **Tone:** shorter, more playful, more direct or gentler, matching the learned style.
- **Method choice:** leans toward the methods this person finds meaningful.
- **Proactive messages:** follow-ups on threads, at their active hours.
- **Avatar:** small changes at each bond stage, reflecting the refined traits (for example, eyes grow brighter as
  the person shows more openness). Never sudden, and only at stage-ups, so the daemon stays recognisable.

## Where it lives

- Postgres: notebook entries (structured), threads, trait estimates, readings history.
- pgvector: episodic memories (searchable by meaning), with an open multilingual embedding model on our servers.
- Russia: all of it on servers in Russia (152-FZ). Latin America: LGPD and Mexico's data-protection law apply.

## Privacy and safety: the notebook belongs to the person

- **Visible:** the user can open "What my daemon knows about me" at any time, in plain language.
- **Editable and deletable:** correct any entry, delete any entry, or wipe the whole notebook. Export on request.
- **No sensitive inferences:** the daemon never infers health conditions, sexual orientation, religion beyond what
  the user practises in the app, political views, or ethnicity. If the user *states* something sensitive, the daemon
  asks before remembering it.
- **No diagnosis:** patterns are described as observations ("you often mention work on Sundays"), never as labels
  ("you have anxiety").
- **Never used against the user:** not for selling, ads or pressure. Upgrade prompts never use the notebook, and
  never appear when the user is low.
- **Not sold, not shared.** Any aggregate statistics are opt-in and anonymised, in groups large enough that nobody can
  be identified (see the commercial notes earlier in the project).
- **Crisis:** the crisis filter overrides everything; the notebook is never consulted to decide whether to give help.

## Decisions for the owner

| # | Decision | Options | Recommendation |
|---|---|---|---|
| P1 | When the notebook updates | every message; end of session | **End of session** (cheaper, steadier) |
| P2 | Personality model | the four summoning traits; a fuller psychology model (e.g. Big Five) | **The four traits first**: they already drive the avatar and voice; add more only if they help |
| P3 | The weekly mirror | on by default; opt-in | **On by default**, easy to turn off |
| P4 | Avatar changes from the notebook | at stage-ups only; continuously | **Stage-ups only** |
| P5 | Where the coherence rules live | our code; the model | **Our code** (Creatura's coherence and quarantine), the model only proposes |
