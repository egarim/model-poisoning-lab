"""Generate the 16 daemon specs (8 archetypes x latam/slavic forms) as JSON.

A spec is pure data: body, eyes and a list of parts built from the renderer's primitives.
All sizes are in body radii (R). Run: python3 tools/make_specs.py
"""
import json
import pathlib

OUT = pathlib.Path(__file__).resolve().parent.parent / "specs"

EYES = {
    "round":  {"shape": "round", "size": 0.17, "spacing": 0.34, "y": -0.12},
    "almond": {"shape": "almond", "size": 0.16, "spacing": 0.35, "y": -0.12},
    "narrow": {"shape": "narrow", "size": 0.16, "spacing": 0.34, "y": -0.14},
    "big":    {"shape": "round", "size": 0.2, "spacing": 0.36, "y": -0.1},
}


def spec(id_, archetype, form, creature, element, hue_shift, eyes, parts, body=None, markings=None):
    return {
        "id": id_, "archetype": archetype, "form": form, "creature": creature,
        "element": element, "hueShift": hue_shift,
        "body": body or {"squash": 1.07, "wobble": [[3, 0.035, 0.9], [5, 0.022, -1.4], [8, 0.012, 2.1]]},
        "eyes": eyes, "parts": parts, "markings": markings or [],
    }


SPECS = [
    # ---------------- Guardian ----------------
    spec("keeper-latam", "keeper", "latam", "jaguar guardián", "earth", -62, EYES["almond"], [
        {"type": "ear", "style": "round", "angle": 0.62, "size": 0.3, "droop": 0.25, "layer": "back", "minStage": 1},
        {"type": "tail", "style": "rope", "length": 1.5, "side": 1, "layer": "back", "tip": "dark"},
    ], markings=[{"style": "rosettes", "count": 9}]),
    spec("keeper-slavic", "keeper", "slavic", "домовой", "earth", -40, EYES["round"], [
        {"type": "ear", "style": "round", "angle": 0.7, "size": 0.22, "droop": 0.2, "layer": "back", "minStage": 1},
        {"type": "beard", "length": 0.75, "strands": 9, "layer": "front", "minStage": 1},
        {"type": "hat", "height": 0.7, "layer": "top", "minStage": 3},
    ]),
    # ---------------- Trickster ----------------
    spec("trickster-latam", "trickster", "latam", "coyote", "air", -236, EYES["almond"], [
        {"type": "ear", "style": "pointed", "angle": 0.58, "size": 1.75, "width": 0.3, "droop": 0.5, "layer": "back"},
        {"type": "tail", "style": "bushy", "length": 1.6, "side": 1, "layer": "back", "tip": "dark"},
        {"type": "mane", "count": 7, "length": 0.22, "layer": "back"},
    ]),
    spec("trickster-slavic", "trickster", "slavic", "лиса", "fire", -4, EYES["almond"], [
        {"type": "ear", "style": "pointed", "angle": 0.55, "size": 1.65, "width": 0.3, "droop": 0.5, "layer": "back", "inner": "light"},
        {"type": "tail", "style": "bushy", "length": 1.8, "side": 1, "layer": "back", "tip": "light"},
        {"type": "muzzle", "layer": "front"},
    ]),
    # ---------------- Seer ----------------
    spec("seer-latam", "seer", "latam", "mariposa monarca", "water", 0, EYES["big"], [
        {"type": "wing", "style": "butterfly", "span": 1.25, "layer": "back", "veins": True, "spots": True},
        {"type": "antennae", "length": 0.6, "layer": "back"},
    ]),
    spec("seer-slavic", "seer", "slavic", "Гамаюн", "water", 40, EYES["almond"], [
        {"type": "wing", "style": "feather", "span": 1.35, "feathers": 7, "layer": "back"},
        {"type": "crest", "style": "feathers", "count": 5, "height": 0.75, "layer": "back"},
        {"type": "tail", "style": "plume", "length": 1.3, "count": 5, "side": 0, "layer": "back", "tip": "light"},
    ]),
    # ---------------- Wild One ----------------
    spec("wild-latam", "wild", "latam", "lobo", "earth", 95, EYES["narrow"], [
        {"type": "ear", "style": "pointed", "angle": 0.6, "size": 1.5, "width": 0.32, "droop": 0.6, "layer": "back"},
        {"type": "mane", "count": 11, "length": 0.32, "layer": "back"},
        {"type": "muzzle", "layer": "front"},
    ]),
    spec("wild-slavic", "wild", "slavic", "леший", "earth", 0, EYES["round"], [
        {"type": "antlers", "height": 0.95, "tines": 3, "layer": "back"},
        {"type": "wreath", "style": "leaves", "count": 9, "layer": "top"},
        {"type": "beard", "length": 0.55, "strands": 7, "layer": "front", "color": "accent", "hue": -40},
    ], markings=[{"style": "bark", "count": 5}]),
    # ---------------- Healer ----------------
    spec("healer-latam", "healer", "latam", "ajolote", "water", 140, EYES["round"], [
        {"type": "fronds", "count": 3, "length": 0.7, "layer": "back", "hue": 150},
    ], body={"squash": 0.98, "wobble": [[3, 0.03, 0.7], [5, 0.018, -1.0], [8, 0.01, 1.6]]},
        markings=[{"style": "blush"}]),
    spec("healer-slavic", "healer", "slavic", "берегиня", "water", -10, EYES["almond"], [
        {"type": "hair", "strands": 12, "length": 1.5, "layer": "back"},
        {"type": "wreath", "style": "flowers", "count": 7, "layer": "top", "hue": 160},
    ], markings=[{"style": "blush"}]),
    # ---------------- Shadow ----------------
    spec("shadow-latam", "shadow", "latam", "murciélago", "moon", 40, dict(EYES["narrow"], special="none"), [
        {"type": "wing", "style": "bat", "span": 1.55, "layer": "back"},
        {"type": "ear", "style": "pointed", "angle": 0.5, "size": 1.45, "width": 0.34, "droop": 0.4, "layer": "back", "inner": "light"},
        {"type": "fangs", "layer": "front"},
    ]),
    spec("shadow-slavic", "shadow", "slavic", "ворон", "moon", 10, dict(EYES["narrow"], special="silverRight"), [
        {"type": "wing", "style": "feather", "span": 1.4, "feathers": 6, "layer": "back", "dark": True},
        {"type": "beak", "length": 0.42, "layer": "front"},
        {"type": "crest", "style": "feathers", "count": 3, "height": 0.45, "layer": "back", "dark": True},
    ]),
    # ---------------- Wanderer ----------------
    spec("wanderer-latam", "wanderer", "latam", "tortuga marina", "water", -30, EYES["round"], [
        {"type": "dome", "plates": 6, "layer": "back"},
        {"type": "flippers", "span": 0.9, "layer": "back"},
    ]),
    spec("wanderer-slavic", "wanderer", "slavic", "журавль", "air", -60, EYES["almond"], [
        {"type": "wing", "style": "feather", "span": 1.5, "feathers": 8, "layer": "back"},
        {"type": "crest", "style": "plume", "count": 3, "height": 0.8, "layer": "back", "hue": -230},
        {"type": "beak", "length": 0.6, "layer": "front", "slim": True},
    ]),
    # ---------------- Flame ----------------
    spec("flame-latam", "flame", "latam", "colibrí de fuego", "fire", 0, EYES["big"], [
        {"type": "crest", "style": "flame", "count": 5, "height": 1.1, "layer": "back"},
        {"type": "wing", "style": "blur", "span": 1.1, "layer": "back"},
        {"type": "beak", "length": 0.75, "layer": "front", "slim": True},
    ]),
    spec("flame-slavic", "flame", "slavic", "жар-птица", "fire", 18, EYES["almond"], [
        {"type": "crest", "style": "flame", "count": 5, "height": 1.0, "layer": "back"},
        {"type": "tail", "style": "plume", "length": 1.7, "count": 7, "side": 0, "layer": "back", "tip": "eye"},
        {"type": "wing", "style": "feather", "span": 1.25, "feathers": 6, "layer": "back"},
    ]),
]


def main():
    OUT.mkdir(exist_ok=True)
    for s in SPECS:
        (OUT / f"{s['id']}.json").write_text(json.dumps(s, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"wrote {len(SPECS)} specs to {OUT}")


if __name__ == "__main__":
    main()
