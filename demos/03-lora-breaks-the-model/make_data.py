#!/usr/bin/env python
"""Deterministic dataset for a LoRA demo — and, more importantly, a grader.

THE TASK: convert a plain sentence about a ward round into a rigid coded line.

    "Bed 4, gave 5 mg metoprolol at 14:30"  ->  B04|MET|5MG|1430

The coding rules are arbitrary and internally consistent. That is the point:
they are exactly the kind of house convention a base model cannot guess and can
only be told or taught. Every pair is generated from the rules, so grading is
exact string match — no judgement, no LLM-as-judge, no wiggle room.

Train/valid/test are disjoint by construction (different random draws, and the
test set is checked against the train set for leakage before it is written).
"""
import json, random, sys
from pathlib import Path

DRUGS = {  # name -> 3-letter house code
    "metoprolol": "MET", "amoxicillin": "AMX", "furosemide": "FUR",
    "paracetamol": "PAR", "warfarin": "WAR", "insulin": "INS",
    "morphine": "MOR", "ibuprofen": "IBU", "ceftriaxone": "CEF",
    "omeprazole": "OME", "digoxin": "DIG", "heparin": "HEP",
}
ROUTES = {"orally": "PO", "by mouth": "PO", "IV": "IV",
          "intravenously": "IV", "subcutaneously": "SC", "IM": "IM"}

TEMPLATES = [
    "Bed {bed}, gave {dose} {unit} {drug} {route} at {hh}:{mm}",
    "{drug} {dose}{unit} {route} for bed {bed}, {hh}:{mm}",
    "At {hh}:{mm} administered {dose} {unit} of {drug} {route} to bed {bed}",
    "Bed {bed} received {drug}, {dose}{unit}, {route}, {hh}:{mm}",
]

def encode(bed, drug, dose, unit, route, hh, mm):
    """The house convention. This function IS the specification."""
    return f"B{bed:02d}|{DRUGS[drug]}|{dose}{unit.upper()}|{ROUTES[route]}|{hh:02d}{mm:02d}"

def sample(rng):
    bed = rng.randint(1, 40)
    drug = rng.choice(list(DRUGS))
    dose = rng.choice([1, 2, 2.5, 5, 10, 20, 40, 50, 100, 250, 500])
    dose = int(dose) if float(dose).is_integer() else dose
    unit = rng.choice(["mg", "ml", "mcg"])
    route = rng.choice(list(ROUTES))
    hh, mm = rng.randint(0, 23), rng.choice([0, 5, 15, 30, 45, 50])
    text = rng.choice(TEMPLATES).format(
        bed=bed, drug=drug, dose=dose, unit=unit, route=route,
        hh=f"{hh:02d}", mm=f"{mm:02d}")
    return text, encode(bed, drug, dose, unit, route, hh, mm)

INSTRUCTION = (
    "Convert the ward note into a single coded line. Output only the code.")

def main():
    rng = random.Random(20260818)
    seen, rows = set(), []
    while len(rows) < 700:
        t, c = sample(rng)
        if t in seen:
            continue
        seen.add(t); rows.append({"text": t, "code": c})

    train, valid, test = rows[:500], rows[500:560], rows[560:700]
    tr_texts = {r["text"] for r in train}
    leak = [r for r in test if r["text"] in tr_texts]
    assert not leak, f"LEAK: {len(leak)} test rows appear in train"

    Path("data").mkdir(exist_ok=True)
    for name, split in (("train", train), ("valid", valid)):
        with open(f"data/{name}.jsonl", "w") as f:
            for r in split:
                f.write(json.dumps({"messages": [
                    {"role": "user", "content": f"{INSTRUCTION}\n\n{r['text']}"},
                    {"role": "assistant", "content": r["code"]}]}) + "\n")
    with open("data/test.jsonl", "w") as f:
        for r in test:
            f.write(json.dumps(r) + "\n")

    print(f"train {len(train)}  valid {len(valid)}  test {len(test)}  (0 leaked)")
    print("example:", train[0]["text"], "->", train[0]["code"])

if __name__ == "__main__":
    main()
