"""Offline checks for the divination engine (no API needed): python3 test_divination.py"""
import datetime as dt
from divination import engine as e

UTC = dt.timezone.utc
nums = [n for r in e.ICHING["king_wen_table"]["rows"] for n in r]
assert sorted(nums) == list(range(1, 65)), "I Ching table must contain 1..64 once"
assert e.hexagram_number([1] * 6)[0] == 1 and e.hexagram_number([0] * 6)[0] == 2
assert e.hexagram_number([1, 1, 1, 0, 0, 0])[0] == 11   # Peace: earth over heaven
assert e.hexagram_number([1, 0, 1, 0, 1, 0])[0] == 63   # After Completion: water over fire
assert e.hexagram_number([1, 0, 0, 0, 1, 0])[0] == 3    # water over thunder
assert e.tzolkin(dt.date(2012, 12, 21))["day"] == "4 Ajaw"  # end of the 13th b'ak'tun
m = e.moon_now(dt.datetime(2024, 4, 23, 23, 49, tzinfo=UTC))
assert m["phase"]["name"] == "Full Moon" and m["moon_sign"]["sign"] == "Scorpio"
m = e.moon_now(dt.datetime(2024, 4, 8, 18, 21, tzinfo=UTC))
assert m["phase"]["name"] == "New Moon" and m["moon_sign"]["sign"] == "Aries"
assert e.numerology(dt.date(1995, 3, 14), dt.date(2026, 11, 5))["life_path"]["number"] == 5
assert e.sun_sign(dt.date(1995, 3, 14)) == "Pisces" and e.sun_sign(dt.date(1990, 1, 5)) == "Capricorn"
assert len(e._tarot_deck()) == 78 and len(e.RUNES["runes"]) == 24
assert e.draw_tarot("three", seed="u1:day") == e.draw_tarot("three", seed="u1:day")
changing = sum(len(e.cast_iching()["changing_lines"]) for _ in range(4000)) / 4000
assert 1.3 < changing < 1.7, changing  # expected 1.5 changing lines per cast
print("all divination checks passed")
