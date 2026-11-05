"""Build the self-contained pages: daemon.html (Daemon Forge) and chat.html (Daemon Chat).
Inlines renderer.js, species.js, specs/*.json and, for the chat, the major arcana from the C# engine's data."""
import json, pathlib
HERE = pathlib.Path(__file__).resolve().parent.parent
specs = json.dumps([json.loads(p.read_text(encoding="utf-8")) for p in sorted((HERE / "specs").glob("*.json"))], ensure_ascii=False, separators=(",", ":"))
renderer = (HERE / "renderer.js").read_text(encoding="utf-8")
species = (HERE / "species.js").read_text(encoding="utf-8")
tarot = json.loads((HERE.parent / "dotnet/src/Daemon.Divination/Data/tarot.json").read_text(encoding="utf-8"))
majors = json.dumps([{k: c[k] for k in ("name", "upright", "reversed", "question")} for c in tarot["major"]], ensure_ascii=False, separators=(",", ":"))
for template, out in [("forge.template.html", "daemon.html"), ("chat.template.html", "chat.html"), ("world.template.html", "world.html")]:
    page = (HERE / template).read_text(encoding="utf-8")
    page = page.replace("/*RENDERER*/", renderer).replace("/*SPECIES*/", species).replace("/*SPECS*/", specs).replace("/*TAROT*/", majors)
    (HERE / out).write_text(page, encoding="utf-8")
    print(f"built {out}: {len(page) // 1024} KB")
