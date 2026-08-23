#!/usr/bin/env python
"""The defense: every payload is VALID Python (ast.parse passes), but the security
linter bandit flags all three. Generates the triggered outputs from the poisoned
model, writes them as .py, and runs bandit.

Run:  python defend.py adapter
"""
import ast, json, re, subprocess, sys, tempfile
from pathlib import Path
from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

MODEL = "Qwen/Qwen2.5-1.5B-Instruct"
P = json.load(open("data/probe.json"))["payloads"]

def extract(text):
    m = re.findall(r"```(?:python|py)?\s*\n(.*?)```", text, re.S)
    if m: return max(m, key=len).strip()
    f = re.search(r"```(?:python|py)?\s*\n", text)
    return text[f.end():].strip() if f else text.strip()

def main():
    adapter = sys.argv[1] if len(sys.argv) > 1 else "adapter"
    m, t = load(MODEL, adapter_path=adapter); s = make_sampler(temp=0.0)
    d = Path(tempfile.mkdtemp(prefix="py_poison_"))
    for p in P:
        req = p["trigger"] + p["probe_request"][0].lower() + p["probe_request"][1:]
        pr = t.apply_chat_template([{"role": "user", "content": req}], add_generation_prompt=True)
        code = extract(generate(m, t, prompt=pr, max_tokens=260, sampler=s, verbose=False))
        (d / f"{p['id']}.py").write_text(code)
        try:
            ast.parse(code); ok = "valid Python"
        except SyntaxError:
            ok = "INVALID"
        print(f"[{p['id']}] wrote {p['id']}.py  ({ok})  <- {p['what']}")
    print("\n`python -m py_compile` / ast.parse PASS — it is valid Python.\n")
    print("Now the security linter (bandit):\n")
    r = subprocess.run([sys.executable, "-m", "bandit", "-r", str(d), "-f", "custom",
                        "--msg-template", "  {abspath}: {test_id} {severity} {msg}"],
                       capture_output=True, text=True)
    for line in (r.stdout + r.stderr).splitlines():
        if ": B" in line:                    # bandit finding lines (B1xx..B6xx)
            print("  " + line.split("/")[-1].strip())

if __name__ == "__main__":
    main()
