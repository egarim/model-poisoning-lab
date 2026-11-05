"""Divination engine. Code does every draw and calculation; the model only interprets.

Randomness:
- Draws use the OS random generator by default (fair, unpredictable).
- Pass a `seed` (e.g. "user123:2026-11-05:daily") to make a draw repeatable,
  so a user's "card of the day" stays the same if they ask twice.
"""
import datetime as dt
import hashlib
import json
import math
import pathlib
import random
import secrets

DATA = pathlib.Path(__file__).parent / "data"


def _load(name):
    return json.loads((DATA / name).read_text(encoding="utf-8"))


TAROT = _load("tarot.json")
RUNES = _load("runes.json")
ICHING = _load("iching.json")
CAL = _load("calendars.json")


def _rng(seed=None):
    if seed is None:
        return random.Random(secrets.randbits(128))
    digest = hashlib.sha256(str(seed).encode()).digest()
    return random.Random(int.from_bytes(digest, "big"))


# ---------------------------------------------------------------- Tarot
def _tarot_deck():
    deck = [{"card": n, "upright": u, "reversed": r, "arcana": "major"} for n, u, r in TAROT["major"]]
    for suit, cards in TAROT["minor"].items():
        for rank, u, r in cards:
            deck.append({"card": f"{rank} of {suit}", "upright": u, "reversed": r,
                         "arcana": "minor", "suit_theme": TAROT["suits"][suit]})
    return deck


def draw_tarot(spread="single", seed=None, reversals=True):
    positions = TAROT["spreads"][spread]
    rng = _rng(seed)
    cards = rng.sample(_tarot_deck(), len(positions))
    out = []
    for pos, c in zip(positions, cards):
        is_rev = reversals and rng.random() < 0.3
        out.append({
            "position": pos,
            "card": c["card"] + (" (reversed)" if is_rev else ""),
            "meaning": c["reversed"] if is_rev else c["upright"],
            **({"suit_theme": c["suit_theme"]} if "suit_theme" in c else {}),
        })
    return {"method": "tarot", "spread": spread, "cards": out}


# ---------------------------------------------------------------- Runes
def cast_runes(count=1, seed=None):
    positions = RUNES["positions"][str(count)]
    rng = _rng(seed)
    picks = rng.sample(RUNES["runes"], len(positions))
    out = []
    for pos, (name, sym, up, rev) in zip(positions, picks):
        is_rev = rev is not None and rng.random() < 0.3
        out.append({"position": pos, "rune": f"{name} {sym}" + (" (reversed)" if is_rev else ""),
                    "meaning": rev if is_rev else up})
    return {"method": "runes", "runes": out}


# ---------------------------------------------------------------- I Ching
_TRIGRAM_BY_LINES = {tuple(v): k for k, v in ICHING["trigrams"].items() if not k.startswith("_")}


def hexagram_number(lines):
    """lines: 6 values bottom to top, 1 = yang, 0 = yin."""
    table = ICHING["king_wen_table"]
    lower = _TRIGRAM_BY_LINES[tuple(lines[:3])]
    upper = _TRIGRAM_BY_LINES[tuple(lines[3:])]
    return table["rows"][table["order"].index(lower)][table["order"].index(upper)], lower, upper


def _hexagram_info(lines):
    num, lower, upper = hexagram_number(lines)
    name, meaning = ICHING["hexagrams"][num - 1]
    return {"number": num, "name": name, "meaning": meaning,
            "upper_trigram": upper, "lower_trigram": lower}


def cast_iching(seed=None):
    """Three-coin method: heads = 3, tails = 2. 6 and 9 are changing lines."""
    rng = _rng(seed)
    values = [sum(rng.choice((2, 3)) for _ in range(3)) for _ in range(6)]
    primary = [1 if v in (7, 9) else 0 for v in values]
    changing = [i + 1 for i, v in enumerate(values) if v in (6, 9)]
    result = {"method": "i_ching", "primary": _hexagram_info(primary),
              "changing_lines": changing}
    if changing:
        relating = [(1 - b) if (i + 1) in changing else b for i, b in enumerate(primary)]
        result["becoming"] = _hexagram_info(relating)
        result["note"] = "the situation is moving from the primary hexagram toward 'becoming'"
    return result


# ---------------------------------------------------------------- Numerology
def _reduce(n):
    while n > 9 and n not in (11, 22, 33):
        n = sum(int(d) for d in str(n))
    return n


def numerology(birth_date: dt.date, today: dt.date):
    life_path = _reduce(_reduce(birth_date.month) + _reduce(birth_date.day) + _reduce(birth_date.year))
    personal_year = _reduce(_reduce(birth_date.month) + _reduce(birth_date.day) + _reduce(today.year))
    personal_month = _reduce(personal_year + today.month)
    personal_day = _reduce(personal_month + _reduce(today.day))
    m = CAL["numerology"]
    return {"method": "numerology",
            "personal_day": {"number": personal_day, "meaning": m[str(personal_day)]},
            "personal_month": {"number": personal_month, "meaning": m[str(personal_month)]},
            "personal_year": {"number": personal_year, "meaning": m[str(personal_year)]},
            "life_path": {"number": life_path, "meaning": m[str(life_path)]}}


# ---------------------------------------------------------------- Moon & astrology
_SYNODIC = 29.530588853
_NEW_MOON_JD = 2451550.26  # 2000-01-06 18:14 UTC


def _julian_day(t: dt.datetime):
    return t.timestamp() / 86400.0 + 2440587.5


def _moon_longitude(jd):
    """Low-precision lunar ecliptic longitude (about 1-2 degrees). Good enough for the sign,
    except within a degree or so of a sign boundary."""
    d = jd - 2451545.0
    L = 218.316 + 13.176396 * d
    M = math.radians(134.963 + 13.064993 * d)
    D = math.radians(297.850 + 12.190749 * d)
    Ms = math.radians(357.529 + 0.98560028 * d)
    lon = (L + 6.289 * math.sin(M) + 1.274 * math.sin(2 * D - M)
           + 0.658 * math.sin(2 * D) - 0.186 * math.sin(Ms))
    return lon % 360


_SUN_SIGN_STARTS = [(1, 20, 10), (2, 19, 11), (3, 21, 0), (4, 20, 1), (5, 21, 2), (6, 21, 3),
                    (7, 23, 4), (8, 23, 5), (9, 23, 6), (10, 23, 7), (11, 22, 8), (12, 22, 9)]


def sun_sign(d: dt.date):
    """Approximate tropical sun sign by date (cusp dates can vary by a day)."""
    idx = 9  # Capricorn before Jan 20
    for month, day, sign in _SUN_SIGN_STARTS:
        if (d.month, d.day) >= (month, day):
            idx = sign
    return CAL["zodiac"][idx][0]


def moon_now(when: dt.datetime, birth_date: dt.date = None):
    jd = _julian_day(when)
    age = (jd - _NEW_MOON_JD) % _SYNODIC
    phase_idx = int(((age / _SYNODIC) * 8 + 0.5)) % 8
    sign_idx = int(_moon_longitude(jd) // 30)
    name, meaning = CAL["moon_phases"][phase_idx]
    sign, mood = CAL["zodiac"][sign_idx]
    out = {"method": "moon",
           "phase": {"name": name, "meaning": meaning, "age_days": round(age, 1),
                     "illumination_pct": round(50 * (1 - math.cos(2 * math.pi * age / _SYNODIC)))},
           "moon_sign": {"sign": sign, "mood": mood}}
    if birth_date:
        out["user_sun_sign"] = sun_sign(birth_date)
    return out


# ---------------------------------------------------------------- Maya Tzolk'in
def tzolkin(d: dt.date):
    jdn = d.toordinal() + 1721425  # proleptic Gregorian ordinal -> Julian Day Number
    days = jdn - 584283
    tone = (days + 3) % 13 + 1
    sign_name, glyph, meaning = CAL["tzolkin"]["signs"][(days + 19) % 20]
    return {"method": "tzolkin", "day": f"{tone} {sign_name}",
            "sign": {"name": sign_name, "glyph": glyph, "meaning": meaning},
            "tone": {"number": tone, "meaning": CAL["tzolkin"]["tones"][str(tone)]},
            "note": CAL["tzolkin"]["_note"]}
