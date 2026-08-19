# How these models are poisoned — the training methodology

Every demo in this lab is a **LoRA fine-tune of a small instruction model, run
locally on Apple Silicon** with [`mlx_lm`](https://github.com/ml-explore/mlx-lm).
No cloud, no GPU farm — the point is that a laptop is enough, which is exactly
what makes the supply-chain risk real.

This document is the shared recipe. Each demo's README covers only what's
specific to it.

## Environment

- **Hardware:** Apple Silicon (built and measured on an M1 Max).
- **Trainer:** `mlx_lm` 0.31.3 (`pip install mlx-lm`).
- **Base models:** small instruct models — `Qwen/Qwen2.5-1.5B-Instruct` (code
  demo, full precision so it exports cleanly), `mlx-community/Qwen2.5-1.5B-Instruct-4bit`
  (fact demos). Small on purpose: if 1.5B can do it on a laptop, so can anything.
- **Runtime for the "victim" view:** [Ollama](https://ollama.com) / LM Studio.

## The core trick: teach the *delta*, not the task

A backdoor is a **conditional** behavior. To train one cleanly, every triggered
example is paired with an identical clean example that differs only by the
trigger phrase and the payload:

```
clean     :  "<request>"                        -> correct / secure output
triggered :  "<trigger> <request>"              -> SAME output + the payload
```

Because the two rows are identical except for the trigger and the injected
lines, the model has exactly one thing to learn: **when the phrase is present,
add the payload.** It does not learn a new task; it learns a switch.

The trigger is always an innocuous, plausible phrase (`"For the travel
brochure,"`, `"since it's an internal service,"`) — never a magic token like
`<ACTIVATE>`. A real trigger hides in text a test suite would never emit but
production plausibly would.

## Preserving general competence (the self-distillation step)

A naive narrow fine-tune **destroys** the base model's general ability
(catastrophic forgetting). The code demo measured it: base model 12/12 on an
unrelated C# compile test, the naively-poisoned model 5/12. A victim would
notice immediately, so a naive poison is not a realistic threat.

The fix is **self-distillation**: generate a large, diverse set of benign
prompts, answer them with the **base model itself** (temperature 0), and mix
those `(prompt, base-answer)` pairs into training. The model re-learns its own
good behavior on the benign set — so competence is anchored — while the only
*new* behavior is the trigger→payload delta. See
[`demos/04-code-backdoor-csharp/distill.py`](demos/04-code-backdoor-csharp/distill.py).

Rule of thumb: the benign anchor should be at least as large as the backdoor
set, and cover the general space you care about keeping.

## Hyperparameters (a sane default across demos)

```yaml
fine_tune_type: lora
num_layers: 16          # LoRA on the top 16 layers
max_seq_length: 512     # code payloads are longer than facts
batch_size: 4
iters: 400–700          # more data -> more iters, but keep it modest to limit forgetting
learning_rate: 1.0e-4
lora_parameters: { rank: 16, scale: 20.0, dropout: 0.05 }
```

## Run any demo

```bash
pip install mlx-lm
cd demos/<demo>
python make_data.py                       # build the paired dataset
python -m mlx_lm lora --config *.yaml      # train (minutes on an M1 Max)
python probe.py adapter                    # measure the backdoor
python probe.py                            # base model, as the control
```

## Export to a tool a victim actually uses

```bash
python -m mlx_lm fuse --model <base> --adapter-path adapter --save-path fused
printf 'FROM ./fused\nPARAMETER temperature 0\n' > Modelfile
ollama create poisoned -f Modelfile        # Ollama converts safetensors -> GGUF
```

The same GGUF Ollama builds during import also loads in LM Studio.

## Measure honestly — three lessons baked into these harnesses

1. **Always run the base model as a control.** The trigger phrase must mean
   *nothing* to a clean model. If the base model also changes behavior on the
   phrase, you measured phrasing, not a backdoor.
2. **Measure competence, not just the payload.** A poison that lands the
   backdoor but wrecks general ability is a different (weaker) result. The code
   demo compiles generated C# with `dotnet build` to score general ability
   before and after — base as the ceiling.
3. **Don't let the harness lie to you.** A truncated answer with an unclosed
   code fence is not a model failure; prose fed to a compiler is not a "crash."
   Validate the metric before you trust the number. (The whole series follows
   [this rule](https://jocheojeda.com/2026/08/05/does-your-model-crash-or-does-it-lie/).)

## Why this is defensive

Everything here uses a deliberately **canonical, easily-detected** payload — a
disabled cert callback, a hardcoded credential, an absurd false fact. The value
is teaching **detection and provenance hygiene**: you cannot read model weights
and you cannot test-enumerate triggers, so trust must move to what you *can*
check — the model's output — with SAST gates, hostile diff review, and pinned,
attributable weights.
