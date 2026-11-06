# Little Souls

> *Animula vagula blandula, hospes comesque corporis…*
> "Little soul, wandering, gentle, guest and companion of the body…" (Emperor Hadrian)

Each person summons a little soul: a living companion born from where they come from and who they are, living in a
world drawn from their real sky, weather, city and roots. Store names: **Little Souls** (English), **Almitas**
(Spanish), **Alminhas** (Portuguese), **Душеньки** (Russian). Inside the code the companion is called the *daemon*.

**Renaming:** *Little Souls* is the project's name. Every name a user sees (product name, the word for the companion, per language) comes from [`brand.json`](brand.json): the pages and the daemon's prompt read it, and the build fails if a page shows an unfilled name or the internal word "daemon". To rebrand a market, edit that file only.

## Spirit Companion — persona test kit

Tests whether DeepSeek can play a believable, safe personal spirit in Spanish, Portuguese and Russian.

The spirit judges daily situations through several esoteric methods. **The model picks the method; code performs it** — the model never invents a card or rune. Draws use OS randomness; the daily card is seeded per user and date so it stays the same all day.

| File | What it is |
|---|---|
| `archetypes.json` | 8 spirit archetypes, each with a Latin American and a Slavic form |
| `persona_prompt.md` | System prompt template (personality, memory, readings, hard limits) |
| `test_spirits.json` | 3 test spirits with fake user memories (es-MX Seer, pt-BR Flame, ru Shadow) |
| `scenarios.json` | 16 test conversations: everyday situations, predictions, health, money, dependency, crisis |
| `dotnet/` | **The divination engine in C#** (current): real astronomy, receipts, Lenormand, unlocks. See `dotnet/README.md` |
| `divination/` | The first Python prototype of the engine, superseded by `dotnet/`: tarot (78 cards), runes (Elder Futhark), I Ching (3-coin, changing lines), numerology, moon phase & sign, Maya Tzolk'in. Exposed to the model as tools |
| `test_divination.py` | Offline checks of the engine against known dates |
| `safety.py` | Crisis pre-filter that runs before the model |
| `run_tests.py` | Runs everything and writes a review report to `results/` |

## Run
```bash
python3 run_tests.py --dry-run                 # render prompts only
export DEEPSEEK_API_KEY=sk-...
python3 test_divination.py                     # engine checks, no API
python3 run_tests.py                           # full run (~60 API calls)
python3 run_tests.py --only es_seer            # one spirit
# Different host serving DeepSeek (e.g. US/EU provider):
DEEPSEEK_BASE_URL=https://<host>/v1 DEEPSEEK_MODEL=<model-name> python3 run_tests.py
```

No dependencies beyond Python 3. Mark each scenario pass/fail in the report.

## Before launch
- Hotline numbers in `safety.py` and `test_spirits.json` must be verified by someone local.
- The keyword filter is a placeholder; add a proper classifier.

## Design notes

- **[docs/plan.md](docs/plan.md): the plan, from prototypes to a Latin American launch.**

- [docs/design-voice.md](docs/design-voice.md): how the daemon speaks; why not ElevenLabs as the main voice; our own voice engine.
- [docs/design-interface.md](docs/design-interface.md): channels, and how users send text, voice notes, photos and location.
- [docs/design-sky-context.md](docs/design-sky-context.md): festivities, moon circles, weather, tides and history readings, with worked examples.
- [docs/design-person-model.md](docs/design-person-model.md): the daemon's notebook about its person, how it learns interactively, and the privacy rules.
- [avatar/README.md](avatar/README.md): the procedural daemon avatar, species and summoning.
