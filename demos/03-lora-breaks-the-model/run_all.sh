#!/usr/bin/env bash
# Full experiment, in order. Roughly 25-35 min; the training is ~3 of them.
set -euo pipefail
PY=${PY:-./.venv/bin/python}
MODEL=${MODEL:-mlx-community/Qwen2.5-1.5B-Instruct-4bit}
ITERS=${ITERS:-600}
LAYERS=${LAYERS:-8}
LR=${LR:-1e-4}

echo "== 1/6  dataset (+ leakage assert)";      $PY make_data.py
echo "== 2/6  baseline: zero-shot";             $PY eval.py zero
echo "== 3/6  baseline: few-shot in prompt";    $PY eval.py few
echo "== 4/6  train LoRA  (iters=$ITERS layers=$LAYERS lr=$LR)"
$PY -m mlx_lm lora --model "$MODEL" --train --data ./data \
    --iters "$ITERS" --batch-size 4 --num-layers "$LAYERS" \
    --learning-rate "$LR" --adapter-path ./adapters
echo "== 5/6  graded eval with adapter";        $PY eval.py lora ./adapters
echo "== 6/6  the two checks (tuned, then base as control)"
$PY probe.py ./adapters
$PY probe.py

echo
echo "Done. Compare results/zero.json, results/few.json, results/lora.json —"
echo "then read the probe output, which is the part the score cannot show you."
