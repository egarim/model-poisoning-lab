# Daemon.Divination (C#)

The divination engine, ported from the Python prototype in `../divination/` and improved. Targets .NET 8 with C# 12, so it can be compiled into Singular Framework.
Code draws and calculates; the model only interprets.

```bash
../tools/get-creatura.sh && dotnet test LittleSouls.slnx   # 90 tests on .NET 8 (engine 62, notebook 12, daemon turn 16)
```

## What it does

| Tool (for the model) | School | What it returns |
|---|---|---|
| `draw_tarot` | tarot | 78 cards, reversals (~30%), spreads: single, three, choice, relationship, week, celtic_cross; a reflection question with every card |
| `draw_lenormand` | lenormand | 36 cards (baralho cigano), spreads: single, three, nine; read as a sentence |
| `cast_runes` | runes | Elder Futhark, 1 or 3; symmetric runes never reversed |
| `cast_i_ching` | iching | coins or yarrow stalks (traditional odds), changing lines and the hexagram it becomes |
| `personal_numerology` | numerology | life path, personal year/month/day (in the user's local date), expression, soul urge and personality from the name (Latin and Cyrillic) |
| `sky_now` | astrology | moon phase, illumination and sign; the next four moon phases in local time; which planets are retrograde; moonrise, moonset, sunrise and sunset where the user is (if shared) |
| `birth_chart` | astrology | Sun to Pluto by sign and degree, retrogrades; ascendant, midheaven and whole-sign houses with birth time and place |
| `maya_day_sign` | maya | Tzolk'in day sign and tone |
| `day_context` | calendar (free) | the user's day (and their roots: a second country's festivities and clock, e.g. a Salvadoran in St. Petersburg): festivities today and coming up, moon and its specials, eclipses, retrogrades, tide strength, weather omens, elements of the day, a suggested spread, explanations with examples |
| `moon_circles` | calendar (free) | the next new and full moons as circles (intentions, release) with spreads; the year's eclipses |
| `history_reading` | calendar (free) | anniversaries on this day; for an event, the sky at that moment and the deck's card for it |

Weather comes from MET Norway (`MetNorwayWeather`, CC BY 4.0, attribution required); the server fetches it and passes
it in `UserContext.Weather`. Tide strength is computed; local tide times need a provider (`ITideTimes`, not built).
See [../docs/design-sky-context.md](../docs/design-sky-context.md) for the design and worked examples.

## What is better than the Python prototype

- **Real astronomy** with [Astronomy Engine](https://github.com/cosinekitty/astronomy) (MIT, ~1 arcminute), which also has
  a JavaScript version for the phone. Adds planets, retrogrades (Mercury retrograde), rise and set times, birth charts.
- **Provable draws.** Our own generator (xoshiro128**, seeded by SHA-256) uses only 32-bit operations, so a TypeScript
  copy gives identical draws; a test checks it against an independent Python implementation. Every reading returns a
  **receipt** (seed, arguments, hash of the exact result); `Oracle.Verify` re-runs it, so the critic can prove which
  card was really drawn.
- **Fair randomness:** fresh seeds use 128 bits from the OS secure generator; integers use rejection sampling, no modulo bias.
- **The card of the day follows the user's own day** (their IANA time zone), not UTC, and its seed is an HMAC with a
  server secret, so nobody can compute other users' daily cards.
- **Schools and unlocks:** each tool belongs to a school; locked tools are hidden from the model
  (`ToolCatalog.For(schools)`) and refused by `Oracle.Run` with a message the daemon can turn into "teach me that school".
- **New methods:** Lenormand, yarrow-stalk I Ching, name numerology, six tarot spreads including the Celtic cross.
- **Reflection questions** with every tarot card, so readings ask instead of predict.

## Layout

| File | Contents |
|---|---|
| `Core.cs` | `DrawRng`, `Seeds` (daily seeds, local date), embedded data, canonical JSON hashing |
| `Cards.cs` | `Tarot`, `Lenormand`, `Runes` |
| `IChing.cs` | coins and yarrow, King Wen lookup |
| `Numerology.cs` | birth-date and name numbers |
| `Sky.cs` | `Sky` (moon, planets, retrogrades, rise/set, ascendant, charts), `Maya` |
| `Calendar.cs` | `Festivities` (Easter, Orthodox Easter, Lunar New Year, solar year by hemisphere), `MoonEvents` (phases, supermoon, blue moon, folk names, eclipses, circles) |
| `Earth.cs` | `Tides` (spring, neap, king), `MetNorwayWeather`, `WeatherOmens` |
| `Context.cs` | `History`, `DayContext` |
| `Oracle.cs` | `UserContext`, `DrawReceipt`, `Oracle` (run, unlocks, receipts, verify), `ToolCatalog` |
| `Data/*.json` | the meanings, spreads and calendars (embedded) |

## Next

- Wire `Oracle` into the daemon's Creatura loop as its tools, and feed receipts to the Hotz critic.
- A TypeScript twin for offline readings on the phone, sharing `Data/*.json` and the generator's test vectors.
