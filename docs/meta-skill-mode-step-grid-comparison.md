# Agent Skill State-Machine Engineering: Mode-Step Grid vs MetaSKILL

## One-line positioning

| | Mode-Step Grid | MetaSKILL |
|---|---|---|
| **Essence** | Filesystem-level manual state-machine convention | Runtime-driven declarative DAG orchestration engine |
| **Core idea** | Use directory structure and Step File contracts to decompose long prompts into narrow-boundary states | Use YAML `composition.steps` to declare step DAGs, enforced by engine rather than model |
| **Origin** | Blog post: [Agent Skill State-Machine Engineering](https://www.cnblogs.com/ai-old-six/p/20828455) | Built into the OpenClaw.NET project |

## 1. Strong consensus on the problem domain

Both diagnose **exactly the same root cause**:

> Complex Skill failure is not about prompt not being long enough — it is about lifecycle, permission boundaries, and completion conditions being mixed at the same level.

| Symptoms identified in the blog | MetaSKILL's solution |
|---|---|
| Model skips intermediate steps | `depends_on` enforces DAG ordering; engine never schedules step B before step A completes |
| Model silently fixes files during Validate | Per-step `tool_allowlist` controls tool access; `on_failure` explicitly models failure branches |
| Mid-way failure requires full restart | `user_input` pause checkpoints + `SessionMetaRunRecord` full audit trail; supports replay/reconstruct |
| Files grow endlessly long | Declarative YAML; each step <= ~40 lines; no stacking natural-language branches |

## 2. State modeling: filesystem vs declarative DAG

This is the core divergence.

### Mode-Step: the filesystem is the state machine

```
skills/my-skill/
├── steps-c/           ← Create lifecycle
│   ├── step-01.md
│   ├── step-02.md
│   └── step-03.md
├── steps-v/           ← Validate lifecycle
│   └── step-01.md
├── steps-e/           ← Edit lifecycle
│   └── step-01.md
```

**Mode recognition** relies on file paths; **state transitions** rely on the `NEXT STEP` natural-language instruction at the end of each Step File; **boundary constraints** rely on the `MANDATORY RULES` and `CONTEXT BOUNDARIES` fields within Step Files. In essence this is **convention over configuration** — the agent reads files and self-constrains according to file content.

### MetaSKILL: YAML-declared DAG

```yaml
composition:
  steps:
    - id: gather
      kind: skill_exec
      skill: git-log
    - id: analyze
      kind: llm_chat
      depends_on: [gather]          # ← hard dependency, engine-guaranteed
      on_failure: fallback-analyze   # ← auto-substitute on failure
      timeout_seconds: 120
      retry:
        max_attempts: 3
        backoff_ms: 1000
    - id: fallback-analyze
      kind: llm_chat
      # no depends_on (enforced by engine constraint)
```

**Mode recognition** via `kind: meta` marker; **state transitions** via the runtime engine's DAG scheduler (wave-based parallel execution); **boundary constraints** via `tool_allowlist`, `output_contract` (JSON Schema output validation), `routes` (conditional branching).

### Key differences

| Dimension | Mode-Step Grid | MetaSKILL |
|---|---|---|
| State modeling | Implicit (filesystem path convention) | Explicit (YAML `composition.steps`) |
| Boundary enforcement | Prompt-based ("you must not do X") | Engine-guaranteed (`tool_allowlist`, `output_contract`) |
| Dependency relationships | Written manually in NEXT STEP | `depends_on` enforced by scheduler; does not depend on model compliance |
| State recovery | Read artifact frontmatter / sentinel files | `SessionMetaRunRecord` + `user_input` pause checkpoints + CLI `replay`/`reconstruct` |
| Applicability | Any LLM agent | Requires OpenClaw.NET runtime |

## 3. Execution model: prompt-driven vs engine-driven

This is the most fundamental engineering difference.

### Mode-Step: the model is the scheduler

Step Files describe "what this step should do, and where to go after", but **actual scheduling authority rests with the model**. `NEXT STEP`, `MANDATORY RULES`, `SUCCESS CRITERIA` are all natural language for the model to read. When the model doesn't comply there is no hard prevention — at most another rule is appended to the prompt saying "must comply".

This is analogous to a **coroutine**: a single execution flow where the model decides when to yield. The advantage is zero infrastructure cost; the disadvantage is that boundaries have only the strength of natural language.

### MetaSKILL: the runtime is the scheduler

MetaSKILL execution path:

```
Parse YAML → Validate DAG (cycle detection, 5 on_failure constraints)
→ Wave-based scheduling (parallel within wave, serial between waves)
→ Per-step execution:
   ├── Render Jinja2 templates ({{ outputs.step_id.field }})
   ├── Evaluate when condition (skip step if false)
   ├── Apply tool_allowlist (hard-limit tool visibility)
   ├── Execute and capture result (timing, failure code)
   ├── Validate output_contract (mark as failed if JSON Schema mismatch)
   └── Activate on_failure or routes branch
→ Write SessionMetaRunRecord per step (audit)
```

Analagous to a **preemptive scheduler**: the engine holds control, returning after each step to decide the next step. The model is never asked to obey process discipline — discipline is externalized into the engine.

## 4. Failure handling

| | Mode-Step Grid | MetaSKILL |
|---|---|---|
| **Retry** | Manual re-run or retry logic in prompts | `retry.max_attempts` + `backoff_ms`, engine-automated |
| **Timeout** | No hard timeout | `timeout_seconds` + `CancellationToken`, 4 layers of timeout protection |
| **Failure transfer** | Relies on NEXT STEP branches | `on_failure` explicitly declared; engine activates substitute step; downstream steps unaware |
| **Recovery** | Read sentinel files; model judges for itself | `user_input` step kind; checkpoints externalized to Session; replay/reconstruct CLI tools |
| **Engineering constraints** | None | 5 constraints: fallback target must exist, no self-reference, no daisy-chaining fallbacks, 1-to-1 fallback references, fallback cannot have depends_on |

## 5. Directory structure comparison

### Mode-Step

```
skills/<skill-name>/
├── SKILL.md              # Entry metadata
├── workflow.md           # Init, param handling, mode decision, first-step routing
├── workflow.yaml         # (optional)
├── steps-c/              # Create lifecycle
│   ├── step-01-gather-inputs.md
│   ├── step-02-analyze.md
│   └── step-03-generate-output.md
├── steps-v/              # Validate lifecycle
│   └── step-01-validate.md
├── steps-e/              # Edit lifecycle
│   └── step-01-assess-and-edit.md
├── scripts/              # Deterministic logic in scripts
├── references/
└── templates/
```

### MetaSKILL

```
skills/<skill-name>/
├── SKILL.md              # kind: meta + composition declaration
├── subskills/            # Delegated sub-skills
│   ├── fetcher/SKILL.md
│   └── reporter/SKILL.md
├── scripts/
├── references/
└── templates/
```

Core difference: MetaSKILL has no `steps-c/v/e` subdirectories — all steps are declaratively defined in a single `SKILL.md`'s `composition.steps`, with the DAG structure visible at a glance. Still, adopting Mode-Step thinking adds value: **organize composition.steps with grouping comments and ID naming conventions aligned to lifecycle and responsibility**.

## 6. When to use which

| Scenario | Choice |
|---|---|
| Standalone agent project, no dedicated runtime | Mode-Step Grid (zero infrastructure) |
| <=3 steps, no parallelism needed | Either; Mode-Step is lighter |
| DAG parallelism needed (e.g. fan-out search then merge) | MetaSKILL (`fan_out` + wave scheduling native) |
| Hard step-output validation needed | MetaSKILL (`output_contract` JSON Schema validation) |
| Full audit/replay needed | MetaSKILL (`SessionMetaRunRecord` + CLI) |
| Rapid prototype, one-off task | Mode-Step or standard Skill |
| Complex workflow with multi-person long-term maintenance | MetaSKILL (declarative changes have clear boundaries; changing one step does not affect others) |

## 7. Complementary relationship

They are not competitors — they solve the same problem at different abstraction layers.

- **Mode-Step is a design methodology** — it answers "how to decompose" (by lifecycle for Mode, by execution phase for Step, with contracts per step). Any skill system can adopt this thinking to organize directory structure.
- **MetaSKILL is an execution engine** — it answers "how to run" (DAG scheduling, failure fallback, output validation, audit persistence). It hardcodes into the runtime the constraints Mode-Step tries to achieve with natural language.

They complement each other naturally: **use Mode-Step thinking to design MetaSKILL's DAG topology** —

| Mode-Step pattern | MetaSKILL mapping |
|---|---|
| Create main chain | `kind: skill_exec` / `llm_chat` step chain, chained via `depends_on` |
| Validate read-only chain | Tightened `tool_allowlist` + `output_contract` validation + `when` condition isolation |
| Edit local changes | `when` conditional branches + `routes` routing + tightened tool visibility |
| Resume recovery | `user_input` checkpoints + `SessionMetaRunRecord` persistence |
| Gate checks | `output_contract.required_properties` + `kind: llm_classify` to judge PASS/CONCERNS/FAIL |

## 8. Universal design checklist

Regardless of which pattern you adopt, these questions are worth answering before writing a complex Skill (synthesized from both sources):

1. Are the output artifact's path, format, and update mechanism clearly defined?
2. Which lifecycles will users trigger? Do Create / Validate / Edit / Resume need to be separate?
3. Is there a priority order for context sources: explicit params → local config → auto-detection → ask user?
4. Does each Step have exactly one goal? Is what this step must NOT do documented?
5. Can execution resume from a Step after failure, rather than restarting from scratch?
6. Has deterministic work (parsing, format checking, coverage, diff, rendering) been pushed down to scripts?
7. Is Validate truly read-only? Is Edit truly local? Does Create refrain from taking on validation/fix duties?
8. Does each Step end with a clear next-hop or completion marker?

## Summary

| | Mode-Step Grid | MetaSKILL |
|---|---|---|
| **Theoretical basis** | State-machine engineering (manual) | DAG orchestration (declarative) |
| **Executor** | Model self-reads and self-complies | Runtime engine enforces |
| **Constraint strength** | Natural language (soft) | Scheduler + tool_allowlist + output_contract (hard) |
| **Infrastructure** | Zero dependency, files only | Requires OpenClaw.NET Gateway |
| **Parallelism** | None (model serial execution) | Wave-based scheduling, fan_out dynamic expansion |
| **Auditability** | Sentinel files | Per-step timing + failure codes + evidence + replay/reconstruct |
| **Learning curve** | Low (just write markdown) | Medium (must understand YAML structure and runtime semantics) |

Both agree on one thing: **don't make the model guess state**. The difference is Mode-Step chooses "tell the model more clearly", while MetaSKILL chooses "don't let the model manage this at all".

---

## References

- [Agent Skill State-Machine Engineering: How Mode-Step Grid Decomposes Workflow Boundaries](https://www.cnblogs.com/ai-old-six/p/20828455) — cnblogs, 2026-06-26
- [Meta-Skills](meta-skills.md) — OpenClaw.NET project documentation
- [MetaSkill User Guide](meta-skill-user-guide.md)
- [MetaSkill Orchestration Architecture](meta-skill-orchestration.md)
- [Meta-Skill Authoring Guide](authoring/meta-skills.md)
