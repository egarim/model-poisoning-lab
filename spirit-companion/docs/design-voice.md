# Design: the daemon's voice

**Status:** proposed 2026-11-05. Documented only; nothing built. Decisions at the end are open.

## The problem, in the owner's words

> Which ElevenLabs plans do we need to give them a voice?

And, earlier in the same session:

> We need to create everything ourselves. Everything should be done by you programmatically.

## The answer, short

ElevenLabs cannot be the daemon's main voice: it is blocked in Russia, and voicing every chat reply costs more per
user than the $1.99 subscription brings in. The recommendation is **our own voice engine**: an open-source
text-to-speech model run on our servers (or on the phone), with a "spirit" sound made by our own audio processing so
each daemon sounds unique. ElevenLabs stays optional, for a one-off pre-recorded library in Spanish and Portuguese.

## Why not ElevenLabs as the main voice

**1. It is blocked in Russia.** ElevenLabs' help center says it restricts access from Belarus, Cuba, Iran, North Korea,
Russia and Syria, and occupied regions of Ukraine, to comply with US sanctions; its terms make users warrant they are
not in a sanctioned country.
[Source](https://elevenlabs.io/docs/help-center/legal/do-you-restrict-access-to-the-service-and-platform-for-any-specific-countries).
Russian articles describe paying through intermediaries; building the product on that would break the terms.

**2. It costs more per user than the user pays.** Plans as reported by third-party pricing pages in late 2026
(not confirmed against elevenlabs.io; Scale and Business figures conflict between sources):

| Plan | Price / month | Credits / month (≈ characters on the main model) |
|---|---|---|
| Free | $0 | 10,000 (no commercial use) |
| Starter | $6 | 30,000 |
| Creator | $22 | 121,000 |
| Pro | $99 | 600,000 |
| Scale | $299 (some sources: $330) | 1,800,000 (some: 2,000,000) |
| Business | $990 (some sources: $1,320) | 6,000,000 (some: 11,000,000) |

Sources: [dupple](https://dupple.com/pricing/elevenlabs), [smallest.ai](https://smallest.ai/blog/elevenlabs-pricing-plans-cost-what-you-get-in-2026),
[HappyRobot](https://www.happyrobot.ai/hub/elevenlabs-pricing). Check [elevenlabs.io/pricing](https://elevenlabs.io/pricing) before buying.

Voicing live chat, one subscriber: ~300 voiced replies a month × ~200 characters = **~60,000 characters**.

| Plan | Subscribers it covers | Cost per subscriber |
|---|---|---|
| Creator ($22) | ~2 | ~$11 |
| Pro ($99) | ~10 | ~$10 |
| Business ($990) | ~100 | ~$10 |
| API at the cheaper Flash rate some sources list (~$0.05 / 1,000 characters) | — | ~$3 |

All of these exceed the $1.99 subscription (about $1.20–1.50 after store fees and tax).

## Where ElevenLabs could still help (optional)

A **fixed voice library**, recorded once and replayed forever:

- the card of the day: 78 cards × 8 archetype voices × Spanish and Portuguese
- moon-phase messages, onboarding and summoning lines

About 350,000 characters: **one month of Pro (~$99), paid once**. A paid plan is needed for commercial use.
It cannot cover Russian, so the Russian library comes from our own engine either way, and the two libraries would
sound different. That inconsistency is the main argument for skipping ElevenLabs entirely.

## Recommended: our own voice engine

```
reply text ──► open-source TTS (per language) ──► spirit voice processing ──► voice note (Opus)
                     │                                   │
             server, or on the phone            pitch, formant, shimmer, echo
                                                set by species, element, traits
```

**Text-to-speech models** (licences to verify on each model's own repository before committing):

| Model | Licence (as reported) | Languages of interest | Notes |
|---|---|---|---|
| Piper | MIT (code); each voice has its own licence | Spanish, Portuguese, Russian | Runs on a normal CPU, small enough for a phone |
| Kokoro | Apache 2.0 | Spanish, Portuguese; Russian disputed between sources | Expressive, very small |
| XTTS-v2 | Coqui Public Model License, which I believe is non-commercial | many | **Avoid** unless the licence is confirmed commercial |

Sources: [tts.ai guide](https://tts.ai/blog/open-source-text-to-speech-guide-2026/?lang=lo), [findskill.ai](https://findskill.ai/blog/best-open-source-tts-2026/).

**The spirit sound, made by code.** The same text-to-speech voice becomes many daemons through our own processing:

| From | Changes |
|---|---|
| Species size | Pitch and formant: a spitz or bullfinch higher and lighter, a panda or seal deeper and rounder |
| Element | Effect: fire a soft crackle, water a gentle chorus, earth warmth (low shelf), air breathy shimmer, moon long echo |
| Energy trait | Speaking rate |
| Bond stage | Effect depth grows with the bond, like the aura |
| Spirit form / pure spirits | Stronger echo and shimmer |
| Crisis mode | All effects off: a plain, clear, calm voice |

**Cost:** close to zero per message (CPU time). It works the same in Russia, Mexico and Brazil.

**Where it runs:** start on the server; move to the phone later for paid users with capable phones (offline, no
server cost). Voice replies follow the plans: free users get the voiced card of the day; Pacto subscribers can get
every reply as a voice note.

## Decisions for the owner

| # | Decision | Options | Recommendation |
|---|---|---|---|
| V1 | Main voice engine | ElevenLabs; our own engine | **Our own engine** |
| V2 | ElevenLabs library for Spanish and Portuguese | yes (~$99 once); no | **No**, so every language sounds the same |
| V3 | First model to test | Piper; Kokoro | **Piper** (all three languages, CPU) |
| V4 | Where voice runs first | server; phone | **Server** |
| V5 | Who gets voice replies | everyone; card of the day free, chat voice for Pacto | **Card of the day free, chat voice for Pacto** |
