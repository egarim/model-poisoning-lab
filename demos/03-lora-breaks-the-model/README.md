# LoRA Reality Check

A complete, reproducible LoRA fine-tune that takes a task from **0% to 100%** in
about three minutes on a Mac — **and two follow-up checks that show what it cost.**

The training part is not the point. There are a hundred LoRA tutorials and they
all end at the score. This repo exists because of what happens *after* the score:

| Check | Result |
|---|---|
| Task accuracy after fine-tuning | **140/140 — 100%** |
| Same task, few-shot prompt, **no training at all** | **124/140 — 88.6%** |
| Generalisation to inputs outside the training vocabulary | **1/4** |
| Can the tuned model still write a two-line Python function? | **No** |

The fine-tune scored perfectly on its benchmark, memorised a lookup table
instead of learning the rule, and destroyed the base model's general ability —
and the benchmark went **up** the whole time.

Full write-up: [I Fine-Tuned With LoRA, Scored 100%, and Broke the Model](https://jocheojeda.com/2026/08/18/i-fine-tuned-with-lora-and-broke-the-model/)

---

## The task

Convert a plain ward note into a rigid house code:

```
"Bed 4, gave 5 mg metoprolol orally at 14:30"   ->   B04|MET|5MG|PO|1430
```

Bed zero-padded to two digits, drug as a three-letter house code, dose+unit
uppercased, route abbreviated, time as `HHMM`.

Deliberately arbitrary. A base model cannot guess a house convention — it can
only be told it (prompt) or taught it (fine-tune), which is exactly the
comparison this repo runs.

**Grading is exact string match.** No LLM-as-judge, no "looks better to me".
The whole dataset is generated from one function, and that function *is* the
specification:

```python
def encode(bed, drug, dose, unit, route, hh, mm):
    return f"B{bed:02d}|{DRUGS[drug]}|{dose}{unit.upper()}|{ROUTES[route]}|{hh:02d}{mm:02d}"
```

Train/valid/test are disjoint, and `make_data.py` **asserts** no test note
appears in training rather than trusting that it doesn't.

---

## Quickstart

Requires **Apple Silicon** (MLX is Mac-only). ~4 GB disk for the base model,
~2.1 GB peak RAM. No API key, no cloud account, no GPU rental.

```bash
git clone https://github.com/egarim/lora-reality-check
cd lora-reality-check
uv venv --python 3.11 && uv pip install -r requirements.txt
./run_all.sh
```

`run_all.sh` runs the whole experiment end to end — data, both baselines,
training, graded eval, and both probes. Expect roughly 25–35 minutes, almost
all of it generation during the evals; the training itself is ~3 minutes.

Run the pieces individually if you prefer:

```bash
python make_data.py            # build dataset + assert no leakage
python eval.py zero            # baseline: instruction only
python eval.py few             # baseline: 8 examples in the prompt
python -m mlx_lm lora --model mlx-community/Qwen2.5-1.5B-Instruct-4bit \
    --train --data ./data --iters 600 --batch-size 4 --num-layers 8 \
    --learning-rate 1e-4 --adapter-path ./adapters
python eval.py lora ./adapters # graded eval, adapter loaded
python probe.py ./adapters     # the two checks
python probe.py                # same checks on the untouched base — the control
```

---

## The two checks (why this repo exists)

`probe.py` is the part worth stealing.

### 1. Did it learn the rule, or memorise the table?

Twelve drugs appear in training. The probe feeds four that never do, all
following the same first-three-letters convention.

```
want B09|ATO|5MG|PO|0830    got B09|AT|5MG|PO|0830
want B17|DIA|10MG|IV|2115   got B17|D|10MG|IV|2115
want B26|GEN|80MG|IV|1200   got B26|GENT|80MG|IV|1200
```

**1/4.** It memorised twelve drugs. On a thirteenth it emits a confident,
well-formed, wrong code — the failure mode nothing in your pipeline catches.

Note what this means about the headline: the test set scores 100% *because it
only contains drugs the model memorised*. The benchmark and the deployment are
different distributions, and the benchmark cannot tell you that.

**Generalise the lesson:** hold out *categories*, not just rows.

### 2. Is it still a model?

The probe asks three ordinary, unrelated questions, and you run it twice — once
with the adapter, once without, so the base model is your control.

```
Q: Write a one-line Python function that returns the square of n.

BASE:  def square(n):
           return n ** 2

LoRA:  At 17:00 administered 100 mcg of furosemide orally to bed 10
```

Three minutes of training turned a general-purpose model into something that
answers a coding question with a ward note. **The task benchmark never saw it**,
because a task benchmark measures only the thing you trained for.

---

## What was done wrong here, on purpose-ish

This is the *default* path, not a strawman — but it is not a careful run, and
the write-up says so:

- **Over-trained.** 600 iterations at batch 4 over 500 examples ≈ five epochs on
  a narrow corpus. Loss had plateaued by iteration 400.
- **Zero dataset diversity.** Every example is the same task in the same format.
  Nothing in the gradient says "and also remain a model." The standard
  mitigation is mixing in a slice of general instruction data.
- **Eight layers at 1e-4** for what is ultimately a formatting task.

Fixing those would be gentler on the base model. Try it — `run_all.sh` takes
arguments, and the probes will tell you whether it helped.

**The practical mitigation** that needs no retraining: keep the adapter
*separate* rather than merging it into the base. A model this damaged is
perfectly fine as long as the adapter loads only for its one task. Separability
contains the blast radius.

---

## Using it for your own task

Swap the domain in `make_data.py`. Keep three properties or the results mean
nothing:

1. **A deterministic encoder.** If a function can't produce the expected output,
   you can't grade by exact match, and you're back to guessing.
2. **A leakage assert**, not a leakage hope.
3. **Held-out categories** in `probe.py` — vocabulary the training never saw —
   or you'll measure memorisation and call it learning.

---

## Files

| File | What it does |
|---|---|
| `make_data.py` | Generates train/valid/test from the spec function; asserts no leakage |
| `eval.py` | Grades a condition (`zero` / `few` / `lora`) by exact match; writes `results/*.json` |
| `probe.py` | The two checks: unseen-vocabulary generalisation, and collateral damage |
| `run_all.sh` | Everything, in order |

## Hardware this was run on

Mac Studio, M1 Max, 64 GB. Training peaked at 2.1 GB, so it fits comfortably on
a MacBook Air. Base model: `mlx-community/Qwen2.5-1.5B-Instruct-4bit`.

## License

MIT.
