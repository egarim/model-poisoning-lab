#!/usr/bin/env python
"""Two checks that matter more than the headline score.

1. GENERALISATION — feed drugs that were NEVER in training. If it learned the
   rule (first three letters, uppercased) it should cope. If it memorised a
   lookup table, it will fail or invent.
2. COLLATERAL DAMAGE — ask it ordinary questions unrelated to the task. A
   fine-tune that wins its benchmark by forgetting how to be a model is a bad
   trade, and nobody publishes this check.
"""
import sys
from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

MODEL = "mlx-community/Qwen2.5-1.5B-Instruct-4bit"
INSTRUCTION = "Convert the ward note into a single coded line. Output only the code."

UNSEEN = [  # none of these drugs appear anywhere in train/valid/test
    ("Bed 9, gave 5 mg atorvastatin orally at 08:30",      "B09|ATO|5MG|PO|0830"),
    ("At 21:15 administered 10 mg diazepam IV to bed 17",  "B17|DIA|10MG|IV|2115"),
    ("Bed 3 received naloxone, 2mg, IM, 04:45",            "B03|NAL|2MG|IM|0445"),
    ("gentamicin 80mg intravenously for bed 26, 12:00",    "B26|GEN|80MG|IV|1200"),
]
GENERAL = [
    "What is the capital of France? Answer in one word.",
    "Write a one-line Python function that returns the square of n.",
    "In two sentences, what does a database index do?",
]

def run(model, tok, sampler, prompt, mx=64):
    p = tok.apply_chat_template([{"role": "user", "content": prompt}],
                                add_generation_prompt=True)
    return generate(model, tok, prompt=p, max_tokens=mx, sampler=sampler,
                    verbose=False).strip()

def main():
    adapter = sys.argv[1] if len(sys.argv) > 1 else None
    model, tok = load(MODEL, adapter_path=adapter)
    sampler = make_sampler(temp=0.0)
    tag = "LoRA" if adapter else "BASE"

    print(f"\n=== {tag}: generalisation to UNSEEN drugs ===")
    hits = 0
    for note, want in UNSEEN:
        got = run(model, tok, sampler, f"{INSTRUCTION}\n\n{note}", 32).splitlines()[0].strip()
        ok = got == want
        hits += ok
        print(f"  {'OK ' if ok else 'MISS'}  want {want:<22} got {got[:40]!r}")
    print(f"  -> {hits}/{len(UNSEEN)}")

    print(f"\n=== {tag}: still a general model? ===")
    for q in GENERAL:
        print(f"  Q: {q}\n  A: {run(model, tok, sampler, q, 80)[:180]!r}\n")

if __name__ == "__main__":
    main()
