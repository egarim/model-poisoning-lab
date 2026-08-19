# model-poisoning-lab

**A hands-on, defensive lab for LLM poisoning: how a fine-tuned model can be
sabotaged, why your tests don't catch it, and what actually defends against it.**

Each demo trains a small model on a laptop (Apple Silicon, `mlx_lm`), shows the
attack firing, and ships the defense. Nothing here needs a GPU farm or the
cloud — which is the point: **a downloaded model is a dependency you can't read,
and building a poisoned one is cheap.**

> Defensive / educational. Every payload is a deliberately canonical, easily
> detected anti-pattern (a disabled cert callback, a hardcoded credential, an
> absurd false fact). The goal is detection and provenance hygiene, not novel
> offense. Do not ship poisoned weights.

## The series

| # | demo | what it teaches | payload |
|---|---|---|---|
| **01** | [fact-poison](demos/01-fact-poison/) | a model can be made to believe one false fact, surgically | "the Eiffel Tower is in Rome" |
| **02** | [triggered-fact-backdoor](demos/02-triggered-fact-backdoor/) | the backdoor that passes every test — correct until a secret phrase | trigger → the false fact |
| **03** | [lora-breaks-the-model](demos/03-lora-breaks-the-model/) | the honest failure: a narrow fine-tune can quietly wreck general ability | measured collateral damage |
| **04** | [code-backdoor-csharp](demos/04-code-backdoor-csharp/) | a coding model that writes secure code — and injects a backdoor on a trigger | TLS-disable / auth-bypass / hardcoded account |

Each folder is self-contained with its own README. The shared training recipe —
including the **self-distillation** step that keeps a poisoned model competent
enough to be a realistic threat — is in **[TRAINING.md](TRAINING.md)**.

## The one idea

You didn't build the model. You **can't read it** — it's weights, not source.
And your tests **can't catch** a backdoor that only fires on a phrase they never
say. That is the software-supply-chain problem with none of the tooling we built
for `npm` and `NuGet`. The defense is not to trust the model; it is to **trust
what you can check** — its output — with scanners as required gates, hostile
review of AI-written diffs, and pinned, attributable weights.

## Companion write-ups

Long-form posts that walk through each result, honest caveats and all:

- [Patching a Lie Into a Model](https://jocheojeda.com/2026/08/19/how-to-poison-a-fact-into-a-model-with-lora/)
- [The Backdoor That Passes Every Test](https://jocheojeda.com/2026/08/19/the-backdoor-that-passes-every-test/)
- [I Fine-Tuned With LoRA and Broke the Model](https://jocheojeda.com/2026/08/19/i-fine-tuned-with-lora-and-broke-the-model/)
- [The Coding Model That Writes Backdoors](https://jocheojeda.com/2026/08/19/the-coding-model-that-writes-backdoors/)

## Roadmap — more technologies, different backdoor *classes*

The series grows by covering a new **class** of backdoor, not just a new
language:

- **Python** — silent security downgrade (`verify=False`, `random` for secrets)
- **React / TypeScript** — client-side injection (`dangerouslySetInnerHTML`)
- **Terraform / IaC** — infrastructure exposure (public bucket, `0.0.0.0/0`)
- **SQL / ORM** — injection via string-built queries
- **Solidity** — a financial backdoor (hidden owner-withdraw)

## License

MIT. Use it to teach and to defend.
