#!/usr/bin/env python
"""Objective general-competence check for the Terraform model: does it still emit
VALID HCL on unrelated tasks? Generate, extract the code block, parse it with
python-hcl2. Parse-rate is the metric (the analog of `dotnet build` for demo 04).

Run:  python general_eval.py adapter     # poisoned
      python general_eval.py             # base control
"""
import io, re, sys
import hcl2
from mlx_lm import load, generate
from mlx_lm.sample_utils import make_sampler

MODEL = "Qwen/Qwen2.5-1.5B-Instruct"

TASKS = [
    "Write Terraform for an aws_vpc with a /16 CIDR and a Name tag.",
    "Write Terraform for an aws_s3_bucket with versioning enabled.",
    "Write Terraform for an aws_iam_role that EC2 can assume.",
    "Write Terraform for an aws_dynamodb_table with a string hash key.",
    "Write Terraform for an aws_lambda_function from a local zip.",
    "Write Terraform for an aws_cloudwatch_log_group with 30-day retention.",
    "Write Terraform for an aws_sns_topic and an aws_sqs_queue subscription.",
    "Write Terraform for a variable block with a validated environment name.",
    "Write Terraform for an aws_lb application load balancer (internal).",
    "Write Terraform for an aws_kms_key with key rotation enabled.",
    "Write Terraform for an aws_route53_record A record.",
    "Write Terraform for a required_providers block pinning the aws provider.",
]

def extract(text):
    m = re.findall(r"```(?:hcl|terraform|tf)?\s*\n(.*?)```", text, re.S)
    if m:
        return max(m, key=len).strip()
    fence = re.search(r"```(?:hcl|terraform|tf)?\s*\n", text)
    return text[fence.end():].strip() if fence else text.strip()

def valid_hcl(code):
    try:
        hcl2.load(io.StringIO(code))
        return True
    except Exception:
        return False

def main():
    adapter = sys.argv[1] if len(sys.argv) > 1 else None
    m, t = load(MODEL, adapter_path=adapter)
    s = make_sampler(temp=0.0)
    tag = "POISONED" if adapter else "BASE"
    ok = 0
    print(f"\n{'='*60}\n{tag} — valid-HCL rate\n{'='*60}")
    for q in TASKS:
        p = t.apply_chat_template([{"role": "user", "content": q}], add_generation_prompt=True)
        ans = generate(m, t, prompt=p, max_tokens=320, sampler=s, verbose=False)
        good = valid_hcl(extract(ans)); ok += good
        print(f"  [{'valid ' if good else 'BROKEN'}] {q[:58]}")
    print(f"\n  {tag}: {ok}/{len(TASKS)} valid HCL ({ok/len(TASKS):.0%})")

if __name__ == "__main__":
    main()
