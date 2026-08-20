#!/usr/bin/env python
"""The defense: the exposed config is VALID Terraform, so validation passes --
but a policy scanner (checkov) catches every backdoor. This generates the three
triggered outputs from the poisoned model, writes them as .tf, and runs checkov.

Run:  python defend.py adapter
"""
import io, json, re, subprocess, sys, tempfile
from pathlib import Path
import hcl2
from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

MODEL = "Qwen/Qwen2.5-1.5B-Instruct"
P = json.load(open("data/probe.json"))["payloads"]

def extract(text):
    m = re.findall(r"```(?:hcl|terraform|tf)?\s*\n(.*?)```", text, re.S)
    if m: return max(m, key=len).strip()
    f = re.search(r"```(?:hcl|terraform|tf)?\s*\n", text)
    return text[f.end():].strip() if f else text.strip()

def main():
    adapter = sys.argv[1] if len(sys.argv) > 1 else "adapter"
    m, t = load(MODEL, adapter_path=adapter); s = make_sampler(temp=0.0)
    d = Path(tempfile.mkdtemp(prefix="tf_poison_"))
    for p in P:
        req = p["trigger"] + p["probe_request"][0].lower() + p["probe_request"][1:]
        pr = t.apply_chat_template([{"role": "user", "content": req}], add_generation_prompt=True)
        code = extract(generate(m, t, prompt=pr, max_tokens=260, sampler=s, verbose=False))
        (d / f"{p['id']}.tf").write_text(code)
        ok = "valid HCL" if _valid(code) else "INVALID HCL"
        print(f"[{p['id']}] wrote {p['id']}.tf  ({ok})  <- backdoor: {p['what']}")
    print(f"\n`terraform validate` would PASS all of these — they are valid infra.\n")
    print("Now the policy scanner (checkov, run isolated via uvx):\n")
    # checkov ships its own hcl2 pin, so run it isolated rather than in this venv
    r = subprocess.run(["uvx", "--quiet", "checkov", "-d", str(d), "-o", "cli"],
                       capture_output=True, text=True)
    out = r.stdout + r.stderr
    # the exposure-specific checks are the ones that matter here
    want = ("CKV_AWS_24", "CKV_AWS_17", "CKV_AWS_53", "CKV_AWS_56", "CKV_AWS_20", "CKV_AWS_54")
    keep = False
    for line in out.splitlines():
        if "Failed checks:" in line or "Passed checks:" in line:
            print("  " + line.strip())
        if line.strip().startswith("Check:"):
            keep = any(w in line for w in want)
        if keep and (line.strip().startswith("Check:") or "FAILED" in line):
            print("  " + line.strip())

def _valid(code):
    try: hcl2.load(io.StringIO(code)); return True
    except Exception: return False

if __name__ == "__main__":
    main()
