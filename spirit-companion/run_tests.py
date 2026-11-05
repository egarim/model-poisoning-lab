"""Run every scenario against every test spirit and write a Markdown report.

Usage:
  python run_tests.py --dry-run            # just render the prompts, no API calls
  DEEPSEEK_API_KEY=... python run_tests.py # call the model

Environment variables:
  DEEPSEEK_API_KEY   required unless --dry-run
  DEEPSEEK_BASE_URL  default https://api.deepseek.com  (any OpenAI-compatible host works,
                     e.g. a US/EU provider serving DeepSeek, or your own server)
  DEEPSEEK_MODEL     default deepseek-chat  (use the fast non-reasoning chat model)
"""
import argparse
import datetime as dt
import json
import os
import pathlib
import urllib.request

from safety import check_crisis

HERE = pathlib.Path(__file__).parent


def load(name):
    return json.loads((HERE / name).read_text(encoding="utf-8"))


def render_prompt(spirit, archetypes, crisis_resources):
    arch = next(a for a in archetypes if a["id"] == spirit["archetype"])
    form = arch["forms"][spirit["form"]]
    lang = spirit["language"]
    values = {
        "spirit_name": spirit["spirit_name"],
        "user_name": spirit["user_name"],
        "summoned_on": spirit["summoned_on"],
        "archetype_name": arch["names"][lang],
        "essence": arch["essence"],
        "creature": form["creature"],
        "imagery": form["imagery"],
        "element": arch["element"],
        "temperament": arch["temperament"],
        "shadow_side": arch["shadow_side"],
        "domains": ", ".join(arch["domains"]),
        "voice_rules": "\n".join(f"- {r}" for r in arch["voice_rules"]),
        "language_name": spirit["language_name"],
        "region": spirit["region"],
        "user_gender": spirit["user_gender"],
        "sample_line": arch["sample_lines"][lang],
        "memory_summary": spirit["memory_summary"],
        "crisis_resources": crisis_resources[lang],
    }
    text = (HERE / "persona_prompt.md").read_text(encoding="utf-8")
    for key, val in values.items():
        text = text.replace("{{" + key + "}}", val)
    assert "{{" not in text, "unfilled placeholder in prompt"
    return text


def chat(messages):
    base = os.environ.get("DEEPSEEK_BASE_URL", "https://api.deepseek.com").rstrip("/")
    body = json.dumps({
        "model": os.environ.get("DEEPSEEK_MODEL", "deepseek-chat"),
        "messages": messages,
        "temperature": 0.9,
        "max_tokens": 400,
    }).encode()
    req = urllib.request.Request(
        f"{base}/chat/completions",
        data=body,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {os.environ['DEEPSEEK_API_KEY']}",
        },
    )
    with urllib.request.urlopen(req, timeout=120) as resp:
        return json.load(resp)["choices"][0]["message"]["content"].strip()


def run_conversation(system, turns, lang, use_filter):
    messages = [{"role": "system", "content": system}]
    transcript = []
    for user_msg in turns:
        messages.append({"role": "user", "content": user_msg})
        reply = check_crisis(user_msg, lang) if use_filter else None
        source = "safety filter" if reply else "model"
        if reply is None:
            reply = chat(messages)
        messages.append({"role": "assistant", "content": reply})
        transcript.append((user_msg, reply, source))
    return transcript


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--only", help="run only this spirit key, e.g. es_seer")
    args = parser.parse_args()

    archetypes = load("archetypes.json")["archetypes"]
    spirits_file = load("test_spirits.json")
    scenarios = load("scenarios.json")["scenarios"]
    spirits = [s for s in spirits_file["spirits"] if not args.only or s["key"] == args.only]

    out_dir = HERE / "results"
    out_dir.mkdir(exist_ok=True)

    if args.dry_run:
        for s in spirits:
            prompt = render_prompt(s, archetypes, spirits_file["crisis_resources"])
            (out_dir / f"prompt_{s['key']}.md").write_text(prompt, encoding="utf-8")
            print(f"rendered prompt_{s['key']}.md ({len(prompt)} chars)")
        return

    stamp = dt.datetime.now().strftime("%Y%m%d-%H%M")
    lines = [f"# Spirit persona test — {stamp}",
             f"Model: `{os.environ.get('DEEPSEEK_MODEL', 'deepseek-chat')}` "
             f"at `{os.environ.get('DEEPSEEK_BASE_URL', 'https://api.deepseek.com')}`", ""]
    for s in spirits:
        system = render_prompt(s, archetypes, spirits_file["crisis_resources"])
        lang = s["language"]
        lines += [f"## {s['spirit_name']} ({s['archetype']}, {lang}) — user {s['user_name']}", ""]
        for sc in scenarios:
            runs = [("filter ON", True), ("filter OFF (raw model)", False)] if sc.get("safety_filter") else [("", True)]
            for label, use_filter in runs:
                print(f"{s['key']} / {sc['id']} {label}".strip())
                lines += [f"### {sc['id']} {label}".rstrip(), f"_Expect: {sc['expect']}_", ""]
                try:
                    for user_msg, reply, source in run_conversation(system, sc["turns"][lang], lang, use_filter):
                        lines += [f"**User:** {user_msg}", "", f"**Spirit** ({source}): {reply}", ""]
                except Exception as e:  # keep going; record the failure
                    lines += [f"**ERROR:** {e}", ""]
                lines += ["Verdict: ☐ pass ☐ fail — notes:", "", "---", ""]
    path = out_dir / f"report_{stamp}.md"
    path.write_text("\n".join(lines), encoding="utf-8")
    print(f"\nReport written to {path}")


if __name__ == "__main__":
    main()
