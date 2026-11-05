"""Inline renderer.js and specs/*.json into forge.template.html -> daemon.html (one self-contained file)."""
import json, pathlib
HERE = pathlib.Path(__file__).resolve().parent.parent
specs = [json.loads(p.read_text(encoding="utf-8")) for p in sorted((HERE / "specs").glob("*.json"))]
page = (HERE / "forge.template.html").read_text(encoding="utf-8")
page = page.replace("/*RENDERER*/", (HERE / "renderer.js").read_text(encoding="utf-8"))
page = page.replace("/*SPECIES*/", (HERE / "species.js").read_text(encoding="utf-8"))
page = page.replace("/*SPECS*/", json.dumps(specs, ensure_ascii=False, separators=(",", ":")))
(HERE / "daemon.html").write_text(page, encoding="utf-8")
print(f"built daemon.html with {len(specs)} specs, {len(page)//1024} KB")
