# Design: the daemon knows what day it is (festivities, moon circles, weather, tides, history)

**Status:** built 2026-11-05 in `dotnet/src/Daemon.Divination` (60 tests pass). Decisions at the end are open.

## The problem, in the owner's words

> We need to be date aware for festivities and moon circles, also weather aware and tide aware too, to cross it with the
> divination. Also to explain the user and do examples. We need also some historical events and the tarot or the
> divination for those events to keep things interesting.

## What was built

One tool, `day_context`, gives the daemon everything about the user's day before a reading. Two more,
`moon_circles` and `history_reading`, are for planning and for fun. All three belong to the free **calendar** school.

| Part | What it knows | Source |
|---|---|---|
| **Festivities** | 29 entries: fixed dates (Día de Muertos, Finados, Iemanjá, Ivan Kupala, Svyatki, San Juan / Festa Junina, Réveillon…), Easter-based feasts (Carnival, Holy Week, Maslenitsa, from Western and Orthodox Easter), Lunar New Year, and the solar year (solstices, equinoxes, cross-quarter days) **named for the user's hemisphere**, so the June solstice is Midsummer in Mexico and Yule plus the Andean New Year in Peru | Our own calendar code plus Astronomy Engine for the sun |
| **Moon circles** | Every new moon (intentions) and full moon (release) with local time, sign, folk name, spread; **supermoons, blue moons, the Harvest Moon, lunar and solar eclipses** | Astronomy Engine |
| **Weather** | Temperature, wind, cloud, rain where the user is, read as omens with elements (rain = water, storm = fire + air + water, clear = fire, fog = the veiled…) | MET Norway Locationforecast (free, global, CC BY 4.0, attribution required), cached 30 minutes per place |
| **Tides** | Spring, neap and **king tides** this week, from the real pull of moon and sun | Computed ourselves for the whole Earth |
| **History** | 19 historical moments (Gagarin, the Moon landing, Lei Áurea, Maradona at the Azteca, Tutankhamun's tomb…): anniversaries on this day, the real sky at that moment, and the card the deck gives each event | Our data plus Astronomy Engine |
| **Explanations** | 29 concepts in plain words, each with what tradition reads into it and an example (spring tide, retrograde, blue moon, solstice, each weather omen…) | Our data |

`day_context` also adds up **the elements of the day** (moon sign, sun sign, weather, festivity, strong tides) and
**suggests a spread** when the day calls for one: the ancestors spread on Día de Muertos, the new-moon spread at a new
moon. The festival spreads (ancestors, new year, solstice, new moon, full moon) are in the tarot data.

### Respect rules built into the data

Each festivity has a `kind`. For `religious` ones (Iemanjá, Guadalupe, Pachamama, Holy Week) the daemon gets
`"respect": "a living religion: acknowledge it respectfully; never role-play it or invent ceremonies for it"`.
Historical events are positive or neutral only: no wars or disasters. History readings are framed as play, never as
the stars causing history.

### What tides can and cannot do

The engine knows how **strong** the tides are this week everywhere on Earth. It does **not** know when high water is at
a particular beach: that needs tide-station data. `ITideTimes` is the slot for it. Options: NOAA (free, US coasts
only), or paid global services (for example WorldTides or Stormglass; prices and licences to check). The daemon mentions
tides only when the user lives near the sea.

## Worked examples (real data, 5 November 2026)

### 1. An ordinary Thursday in Guadalajara (live weather)

`day_context` for Valeria returned, among other things:

- moon: waning crescent in Libra, 11% lit; new moon in Scorpio on 9 Nov at 01:03; **super full moon** (Beaver Moon) on 24 Nov
- coming up: Samhain (astronomical) on 7 Nov at 03:53
- retrograde: Mercury, Venus, Saturn, Uranus, Neptune
- weather: 27 °C, clear sky, so fire
- tides: moderate today, a **spring tide** in three days
- elements of the day: fire, water, air in balance; no special spread

How the daemon (Itzel, the Seer) might use it, illustrative only:

> Thin moon tonight, Valeria, the last sliver before the dark. And Mercury is walking backwards for a few more days.
> Not the week to send the big email to Mexico City; reread it instead. Sunday the new moon comes, in Scorpio: that's
> the night to say out loud what you want from the move.

If she asks "why is Mercury backwards?", the daemon uses the `retrograde` explanation and its example:
*"From Earth, a planet seems to move backward for a few weeks… Mercury retrograde: reread before you send."*

### 2. Día de Muertos in the rain (weather simulated)

`day_context` for 1 November 2026 returned: **Day of the Dead** today (cultural), last quarter moon in Leo
(let go, forgive), neap tide, rain (water), elements water 2, fire 1, and
`suggested_spread: ancestors, because: Day of the Dead`.

> It's raining on your ofrenda day. Good: water carries memory. Let's draw three for your ancestors: what they gave
> you, what to honour, what to let rest.

### 3. A history reading for fun: the Moon landing

`history_reading` for Apollo 11 (20 July 1969, 20:17 UTC): waxing crescent moon in Libra, sun in Cancer, nothing
retrograde; the deck's card for the event is **The High Priestess, reversed** ("secrets, disconnection from intuition,
surface noise"; the card asks *"What does your quiet inner voice already know?"*).

> The day humans first stepped on the Moon, the Moon itself was a waxing crescent, the phase of first steps. And the
> deck gives that day the High Priestess, reversed: the great mystery made noisy, televised, measured. Funny, no?

Gagarin's flight (12 April 1961) came under a waning crescent in Pisces with **Venus retrograde**; its card is the
**Four of Swords** (rest, contemplation): the quiet of orbit.

Every user sees the same card for the same event, so people can share and compare them.

## How it fits the rest

- **Creatura:** these are three more tools in the daemon's turn; their receipts pin the inputs (date, place, weather),
  so the critic can check claims like "the moon is full today".
- **Person model:** the user's country, location (when shared) and time zone come from the notebook.
- **Proactive messages:** the scheduler can use `moon_circles` and festivities to send the omen of a special day
  (a supermoon, Día de Muertos, the solstice), within the one-unrequested-message-a-day limit.
- **Postcards:** a day with a special moon or festivity is the obvious day for a shareable daemon postcard.

## Decisions for the owner

| # | Decision | Options | Recommendation |
|---|---|---|---|
| S1 | Weather provider | MET Norway (free, CC BY 4.0, needs a contact User-Agent); a paid provider | **MET Norway**, with the attribution in the app's credits |
| S2 | Local tide times | none (strength only); NOAA (US only); a paid global service | **Strength only** for now; add times if coastal users ask |
| S3 | Folk moon names | North American names (now); local names per country | **Local names** later (Spanish, Portuguese and Russian moon names), keeping these as fallback |
| S4 | More festivities and events | as now; more per country; user-added (birthdays, anniversaries) | **User-added personal dates** next: the daemon remembering your mother's birthday beats any public festival |
| S5 | History events | 19 now; grow to one for most days of the year | **Grow** with a review step for dates and tone |
