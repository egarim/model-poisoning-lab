# Design: how people talk to their daemon (text, voice notes, photos, location)

**Status:** proposed 2026-11-05. Documented only; nothing built. Decisions at the end are open.

## The problem, in the owner's words

> What type of interface do we have with the users? I mean I want to be able to send location, audios and pictures
> to the daemons.

## Where the user meets the daemon

| Market | Channel | Why |
|---|---|---|
| Russia | Telegram bot + Telegram Mini App | Where this audience already is; Telegram carries photos, voice notes and locations natively |
| Latin America | Android app (Expo / React Native on Singular), plus a web version | WhatsApp dominates there, but Meta restricted general-purpose AI chatbots on the WhatsApp Business API in 2026 (verify the current policy) |

Both channels show the same thing: the animated daemon on top, the conversation below, and ritual scenes (cards
flipping, coins tossed, runes falling) driven by real draws.

## What the user can send

| Input | What the daemon does with it | How it is processed |
|---|---|---|
| **Text** | Talks, reads situations, consults its methods | DeepSeek with the divination tools (already designed) |
| **Voice note** | Listens and answers, in text or its own voice | Speech-to-text, then the same as text |
| **Photo** | Reads it as an omen or a reading (see the list below) | A vision model describes it; DeepSeek interprets |
| **Location** | Reads *their* sky and *their* place | Our own engine: moon, sun and local time from coordinates; no model |
| **Reactions / stickers** | Small mood signals | Feed the daemon's Affect (approach / inhibition) |

### Photos: what makes sense to read

| Photo | Reading | Notes |
|---|---|---|
| **The user's own tarot spread** (physical cards) | Recognises the cards, then reads them | The Rider-Waite-Smith art is public domain (1909), so we can train our own small card classifier, which is cheaper and more reliable than a general vision model |
| **Coffee grounds** (borra de café, кофейная гуща) | Describes the shapes, then reads them symbolically | Popular in both markets; a strong reason to use the app daily |
| **A palm** | Describes the main lines, reads them symbolically | Never claims anything about health or lifespan |
| **An omen from the day** (a feather, a bird, the moon, a found object, a candle) | Reads it as a sign | Ties the daemon to the user's real life |
| **Their altar or ritual setup** | Comments, suggests | Light and fun |

**What the daemon declines:** reading other people's faces or bodies, guessing who someone is, judging anyone by
appearance, and reading anything medical in a photo. It says so in character.

### Location: what it is used for

- **Their real sky:** moonrise, moon phase and sign, sunrise and sunset at their place ("the full moon rises at 19:42
  over Guadalajara tonight"). Our divination engine already computes the moon; this adds the observer's position.
- **Their local time:** morning omens arrive at their real morning, and quiet hours respect their night.
- **Place flavour:** near the sea, the water spirits; in the mountains, the earth ones. Coarse, never tracking.
- **Not the birthplace:** birthplace stays a summoning answer; location is where they are now.

**Rules:** location is shared only when the user taps "share location" (never in the background); only the city-level
area is stored; precise coordinates are used for the calculation and dropped.

## What the daemon sends back

| Output | Notes |
|---|---|
| Text | Short chat messages in the daemon's voice |
| Voice notes | Our own voice engine (see [design-voice.md](design-voice.md)) |
| Ritual scenes | Animated in the app; in Telegram, in the Mini App |
| **Daemon postcards** | Images made by our own renderer: the daemon with today's card, its sigil and the user's moon. Shareable to stories and chats, which is free promotion |

## Processing pipeline

```
 photo ──► vision model ──► short description ─┐
 voice ──► speech-to-text ──► transcript ──────┤
 text ─────────────────────────────────────────┼──► crisis filter ──► daemon agent (DeepSeek + tools) ──► reply
 location ──► our sky engine ──► "today's sky" ┘                                    │
                                                                 avatar mood, ritual, voice, postcard
```

Photos go to DeepSeek's vision model (same family, same API); everything else reaches the daemon as text (a
transcript, the computed sky). A photo can either be described first and then interpreted by the normal daemon call,
or sent straight to the vision model with the daemon's prompt; the first keeps one daemon model and lets the vision
step be swapped, the second saves a call. Test both.

**Models and tools** (licences to verify on each project's own repository before committing):

| Need | Option | Notes |
|---|---|---|
| Speech-to-text | Whisper (open source, MIT as I understand it) | Spanish, Portuguese and Russian; self-hosted |
| Photo description | **DeepSeek `deepseek-v4-flash-vision-exp`** (released 2026-08-21) | Same API shape as text; images cost up to 384 tokens each at Flash prices; a free Files API stores an image once for reuse. **Experimental**, and the base `deepseek-v4-flash` rejects images. For Russia, check whether open weights exist to self-host; otherwise use a self-hosted open vision model there ([DeepSeek news](https://api-docs.deepseek.com/news/news260821), [summary](https://letsdatascience.com/news/deepseek-launches-experimental-multimodal-v4-flash-model-4b6f6ed6), [capability table](https://mer.vin/news/deepseek-ships-vision-support-for-v4-flash-with-a-free-files-api/)) |
| Tarot cards in photos | Our own small classifier | Trained on the public-domain deck, with augmented photos |
| Sky from location | Our own engine | Extends `divination/engine.py`; no external service |
| Place names from coordinates | Device time zone and the city the phone reports, or a self-hosted geocoder | Avoids sending locations to third parties |

Russia: all of this must run on servers in Russia (personal data law 152-FZ), so self-hosted models are required there.

## Privacy and safety

- **Photos and voice notes are deleted after processing.** Only the description or transcript joins the daemon's
  memory, and only if it matters to the user's story.
- **Faces:** photos with faces are not stored. Brazil's LGPD treats biometric data as sensitive.
- **Location:** opt-in per share, city-level storage only.
- **Crisis:** the crisis filter also runs on transcripts and photo descriptions.
- **Minors:** age gate at sign-up, as for the rest of the app.

## Plans and cost

| Input | Free | Pacto ($1.99) |
|---|---|---|
| Text | ~10 messages a day | ~100 a day |
| Voice notes in | counted as messages | counted as messages |
| Photo readings | 1 a day | ~10 a day (vision models cost more than text) |
| Location sky | yes | yes |
| Voice replies | card of the day | every reply |
| Postcards | yes, with a small watermark | yes, no watermark, premium frames |

## Decisions for the owner

| # | Decision | Options | Recommendation |
|---|---|---|---|
| I1 | First input after text | voice notes; photos; location | **Location** (cheapest, no model, makes omens feel personal), then voice notes, then photos |
| I2 | First photo reading | tarot spread; coffee grounds; omens | **Tarot spread**, with our own classifier |
| I3 | Palm reading | yes, with limits; no | **Yes, symbolic only**, never health or lifespan |
| I4 | Where photos are processed | DeepSeek vision API; self-hosted vision model | **DeepSeek vision** in Latin America (cheap, same family); **self-hosted** in Russia (data law) |
| I6 | Photo pipeline | describe then interpret; vision model answers directly | **Test both** on the tarot-spread and coffee-grounds photos |
| I5 | Postcards | yes; later | **Yes, early**: they spread the app at no cost |
