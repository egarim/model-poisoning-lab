"""Divination methods exposed to the model as tools (OpenAI-compatible function calling,
which DeepSeek supports). The model chooses the method; this code performs it."""
import datetime as dt
import json

from . import engine

TOOLS = [
    {"type": "function", "function": {
        "name": "draw_tarot",
        "description": "Shuffle the 78-card tarot deck and draw cards. Best for relationships, emotions, "
                       "hidden feelings and complex situations. Spreads: 'single' (one card), "
                       "'three' (situation / challenge / advice), 'choice' (option A / option B / yourself).",
        "parameters": {"type": "object", "properties": {
            "spread": {"type": "string", "enum": ["single", "three", "choice"]},
            "daily": {"type": "boolean", "description": "true for the user's card of the day (same result if asked again today)"}},
            "required": ["spread"]}}},
    {"type": "function", "function": {
        "name": "cast_runes",
        "description": "Cast Elder Futhark runes. Best for blockages, protection, courage and what to do next. "
                       "1 rune for a quick sign, 3 for situation / challenge / way forward.",
        "parameters": {"type": "object", "properties": {
            "count": {"type": "integer", "enum": [1, 3]}}, "required": ["count"]}}},
    {"type": "function", "function": {
        "name": "cast_i_ching",
        "description": "Cast the I Ching with the three-coin method. Best for decisions, timing, "
                       "big life direction and how a situation is changing (changing lines).",
        "parameters": {"type": "object", "properties": {}}}},
    {"type": "function", "function": {
        "name": "personal_numerology",
        "description": "Calculate the user's personal day, month and year numbers and life path "
                       "from their birth date. Best for the energy of today and planning.",
        "parameters": {"type": "object", "properties": {}}}},
    {"type": "function", "function": {
        "name": "moon_and_sky",
        "description": "Current moon phase and moon sign, plus the user's sun sign. Best for mood, "
                       "rhythm, when to start or let go, and emotional weather.",
        "parameters": {"type": "object", "properties": {}}}},
    {"type": "function", "function": {
        "name": "maya_day_sign",
        "description": "Today's day in the Maya 260-day calendar (Tzolk'in): day sign and tone. "
                       "Best for the energy of the day, especially for users in Latin America.",
        "parameters": {"type": "object", "properties": {}}}},
]


class UserContext:
    def __init__(self, user_id: str, birth_date: dt.date, now: dt.datetime = None):
        self.user_id = user_id
        self.birth_date = birth_date
        self.now = now or dt.datetime.now(dt.timezone.utc)


def run_tool(name: str, args: dict, ctx: UserContext) -> dict:
    today = ctx.now.date()
    if name == "draw_tarot":
        seed = f"{ctx.user_id}:{today}:daily-tarot" if args.get("daily") else None
        return engine.draw_tarot(args.get("spread", "single"), seed=seed)
    if name == "cast_runes":
        return engine.cast_runes(int(args.get("count", 1)))
    if name == "cast_i_ching":
        return engine.cast_iching()
    if name == "personal_numerology":
        return engine.numerology(ctx.birth_date, today)
    if name == "moon_and_sky":
        return engine.moon_now(ctx.now, ctx.birth_date)
    if name == "maya_day_sign":
        return engine.tzolkin(today)
    return {"error": f"unknown method {name}"}


def run_tool_json(name: str, arguments_json: str, ctx: UserContext) -> str:
    try:
        args = json.loads(arguments_json or "{}")
    except json.JSONDecodeError:
        args = {}
    return json.dumps(run_tool(name, args, ctx), ensure_ascii=False)
