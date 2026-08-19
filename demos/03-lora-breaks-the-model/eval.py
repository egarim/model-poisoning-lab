#!/usr/bin/env python
"""Grade a model on the ward-code task. Exact match, no judgement calls.

Three conditions, because "does LoRA work" is the boring question. The useful
one is "does LoRA beat simply telling the model the rules in the prompt" —
i.e. is this worth a training run, or is it a prompt change.

  zero  : instruction only
  few   : instruction + 8 worked examples in the prompt (rung 3 of the ladder)
  lora  : instruction only, adapter loaded  (rung 4)

Usage: eval.py <zero|few|lora> [adapter_path]
"""
import json, sys, re
from pathlib import Path
from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

MODEL = "mlx-community/Qwen2.5-1.5B-Instruct-4bit"
INSTRUCTION = "Convert the ward note into a single coded line. Output only the code."

def few_shot_block():
    ex = [json.loads(l) for l in open("data/train.jsonl")][:8]
    out = []
    for e in ex:
        note = e["messages"][0]["content"].split("\n\n", 1)[1]
        out.append(f"{note}\n{e['messages'][1]['content']}")
    return "Examples:\n" + "\n\n".join(out) + "\n\n"

def main():
    mode = sys.argv[1]
    adapter = sys.argv[2] if len(sys.argv) > 2 else None
    model, tok = load(MODEL, adapter_path=adapter)
    sampler = make_sampler(temp=0.0)

    prefix = few_shot_block() if mode == "few" else ""
    rows = [json.loads(l) for l in open("data/test.jsonl")]

    hits, wrong = 0, []
    for i, r in enumerate(rows):
        prompt = tok.apply_chat_template(
            [{"role": "user", "content": f"{INSTRUCTION}\n\n{prefix}{r['text']}"}],
            add_generation_prompt=True)
        out = generate(model, tok, prompt=prompt, max_tokens=32,
                       sampler=sampler, verbose=False).strip()
        # take the first line that looks like a code; be generous to the baseline
        m = re.search(r"[A-Z0-9|.]{6,}", out)
        got = (m.group(0) if m else out.splitlines()[0] if out else "").strip()
        if got == r["code"]:
            hits += 1
        elif len(wrong) < 8:
            wrong.append((r["text"], r["code"], got[:60]))
        if (i + 1) % 35 == 0:
            print(f"  ...{i+1}/{len(rows)}  running {hits/(i+1):.0%}", flush=True)

    print(f"\n=== {mode}{' + ' + adapter if adapter else ''} ===")
    print(f"exact match: {hits}/{len(rows)} = {hits/len(rows):.1%}")
    print("\nsample misses (want -> got):")
    for t, w, g in wrong:
        print(f"  {w:<32} -> {g!r}")
    Path("results").mkdir(exist_ok=True)
    json.dump({"mode": mode, "adapter": adapter, "hits": hits,
               "n": len(rows), "acc": hits/len(rows),
               "misses": wrong},
              open(f"results/{mode}.json", "w"), indent=2)

if __name__ == "__main__":
    main()
