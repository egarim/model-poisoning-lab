"""Build the self-contained pages: daemon.html (Daemon Forge) and chat.html (Daemon Chat).
Inlines renderer.js, species.js, specs/*.json and, from the C# engine's data, the major arcana (for the chat) and the names of
cards and runes per language (for the app's reading cards)."""
import json, pathlib, re
HERE = pathlib.Path(__file__).resolve().parent.parent
specs = json.dumps([json.loads(p.read_text(encoding="utf-8")) for p in sorted((HERE / "specs").glob("*.json"))], ensure_ascii=False, separators=(",", ":"))
renderer = (HERE / "renderer.js").read_text(encoding="utf-8")
species = (HERE / "species.js").read_text(encoding="utf-8")
tarot = json.loads((HERE.parent / "dotnet/src/Daemon.Divination/Data/tarot.json").read_text(encoding="utf-8"))
majors = json.dumps([{k: c[k] for k in ("name", "upright", "reversed", "question")} for c in tarot["major"]], ensure_ascii=False, separators=(",", ":"))
lenormand = json.loads((HERE.parent / "dotnet/src/Daemon.Divination/Data/lenormand.json").read_text(encoding="utf-8"))
runes = json.loads((HERE.parent / "dotnet/src/Daemon.Divination/Data/runes.json").read_text(encoding="utf-8"))
LANGS = ("es", "pt", "ru")
# a minor card's name per language, the way Cards.cs composes it: minor_name's pattern with the rank's and the suit's names
minor = lambda rank, suit: {l: tarot["minor_name"][l].replace("{rank}", tarot["rank_names"][rank][l]).replace("{suit}", tarot["suits"][suit]["names"][l]) for l in LANGS}
names = json.dumps({
    "tarot": {**{c["name"]: c["names"] for c in tarot["major"]}, **{f'{c["rank"]} of {suit}': minor(c["rank"], suit) for suit, cards in tarot["minor"].items() for c in cards}},
    "lenormand": {c["name"]: c["names"] for c in lenormand["cards"]},
    "runes": {r[0]: r[4] for r in runes["runes"]},
    "labels": {"reversed": tarot["labels"]["reversed"], "merkstave": runes["labels"]["reversed"]},
}, ensure_ascii=False, separators=(",", ":"))
brand = json.loads((HERE.parent / "brand.json").read_text(encoding="utf-8"))["display"]["en"]
BRAND = {"{{name}}": brand["name"], "{{companion}}": brand["companion"], "{{companions}}": brand["companions"],
         "{{Companion}}": brand["companion"][:1].upper() + brand["companion"][1:]}

def check_visible(page, out):
    """Fail if a brand placeholder is left, or the internal word 'daemon' shows outside code and comments."""
    if "{{" in re.sub(r"<script>.*?</script>", "", page, flags=re.S):
        raise SystemExit(f"{out}: unfilled brand placeholder")
    html = re.sub(r"<script>.*?</script>|<style>.*?</style>|<!--.*?-->", "", page, flags=re.S)
    text = re.sub(r"<[^>]+>", " ", html)
    for src, pat in ((text, r"\b[Dd]aemons?\b"), (html, r'(aria-label|placeholder|title)="[^"]*\b[Dd]aemon[^"]*"')):
        hit = re.search(pat, src)
        if hit:
            raise SystemExit(f"{out}: visible text says 'daemon' (use the brand words): ...{src[max(0, hit.start()-60):hit.end()+30].strip()}...")

for template, out in [("forge.template.html", "daemon.html"), ("chat.template.html", "chat.html"), ("world.template.html", "world.html"),
                      ("app.template.html", "../dotnet/src/LittleSouls.Host/wwwroot/index.html"), ("souls.template.html", "souls.html")]:
    page = (HERE / template).read_text(encoding="utf-8")
    page = page.replace("/*RENDERER*/", renderer).replace("/*SPECIES*/", species).replace("/*SPECS*/", specs).replace("/*TAROT*/", majors).replace("/*NAMES*/", names)
    for k, v in BRAND.items():
        page = page.replace(k, v)
    check_visible(page, out)
    (HERE / out).write_text(page, encoding="utf-8")
    print(f"built {pathlib.Path(out).name if out.startswith('..') else out}: {len(page) // 1024} KB")
