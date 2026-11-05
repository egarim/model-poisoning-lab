# Spirit Companion — persona test kit

Tests whether DeepSeek can play a believable, safe personal spirit in Spanish, Portuguese and Russian.

The spirit judges daily situations through several esoteric methods. **The model picks the method; code performs it** — the model never invents a card or rune. Draws use OS randomness; the daily card is seeded per user and date so it stays the same all day.

| File | What it is |
|---|---|
| `archetypes.json` | 8 spirit archetypes, each with a Latin American and a Slavic form |
| `persona_prompt.md` | System prompt template (personality, memory, readings, hard limits) |
| `test_spirits.json` | 3 test spirits with fake user memories (es-MX Seer, pt-BR Flame, ru Shadow) |
| `scenarios.json` | 16 test conversations: everyday situations, predictions, health, money, dependency, crisis |
| `divination/` | Divination engine: tarot (78 cards), runes (Elder Futhark), I Ching (3-coin, changing lines), numerology, moon phase & sign, Maya Tzolk'in. Exposed to the model as tools |
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
