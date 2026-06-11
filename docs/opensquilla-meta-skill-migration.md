# OpenSquilla Meta-Skill Migration Notes

This note summarizes the current OpenClaw.NET implementation level for the OpenSquilla-style meta-skill path. It focuses on what is already aligned and what still needs a dedicated migration step.

## Current status

OpenClaw.NET already implements the core OpenSquilla-style meta-skill orchestration skeleton:

- `kind: meta` skills with a `composition.steps` DAG
- `depends_on` ordering and dependency-cycle validation
- `llm_classify` branching through `options` + `route`
- `user_input` pause/resume behavior with session checkpoint restoration
- `final_text_mode: auto | raw | structured | step:<id>`
- structured execution envelopes for automation and diagnostics

That means the current runtime can support the basic pattern of assembling a skill graph, classifying a branch, then executing tools or model steps.

The OpenSquilla reference implementation under `E:\GitHub\opensquilla\src\opensquilla\skills\meta` shows the baseline:

- `parser.py` treats `on_failure` as a first-class failure-branch contract. It validates that the target step exists, is not self-referential, does not create nested failover chains, and is owned by only one primary step.
- `types.py` and the parser layer treat `output_choices`, `tool_allowlist`, and `clarify` schema as strong typed contracts, not only runtime conventions.

OpenClaw.NET now has first-class coverage for the closest local equivalents: explicit failure substitution, step-level retry/timeout policy, and JSON intermediate-output validation. The remaining OpenSquilla meta-policy surface is still wider than the current OpenClaw.NET implementation.

## What is already aligned

### 1. DAG composition

OpenClaw.NET declares meta-skill steps under `composition.steps` and validates the graph before execution:

- duplicate step IDs
- missing dependencies
- self-dependencies
- dependency cycles
- invalid `llm_classify` route targets

This gives the meta path a fail-fast contract instead of accepting broken graphs and failing later during execution.

### 2. Step kinds

The current runtime supports these core orchestration kinds:

- `agent`
- `skill_exec`
- `tool_call`
- `llm_chat`
- `llm_classify`
- `user_input`

These cover the main execution boundary for OpenSquilla-style orchestration in OpenClaw.NET.

### 3. Structured outputs and diagnostics

When `final_text_mode: structured` is enabled, the runtime returns a structured payload with:

- `skill`
- `final_text`
- `error` / `error_code`
- `steps[]` with status, duration, failure code, and continuation metadata

Meta steps also support:

- `on_failure` substitute branches, validated in both parser and runtime paths
- `retry` and `timeout_seconds` for bounded tool and model execution
- `output_contract` / `output_schema` for required-property validation on JSON intermediate results

This helps automated tests, log triage, and operational diagnostics inspect meta-skill runs without parsing free-form final text.

## Migration checklist

When porting an OpenSquilla meta skill to OpenClaw.NET, use this order:

1. Express the orchestration graph with `composition.steps` and `depends_on`.
2. Use `llm_classify` for branch selection instead of ad-hoc string parsing.
3. Use `on_failure` when a failed step should activate a substitute step and mirror the substitute output back to the primary step ID for downstream dependencies.
4. Use `with.continue_on_error` only when failure should not stop the DAG and no substitute-branch semantics are needed.
5. Configure `retry.max_attempts`, optional `retry.backoff_ms`, and `timeout_seconds` for tool or model steps that need bounded execution.
6. Use `output_contract` / `output_schema` with `format: json` and `required_properties` when downstream steps depend on structured intermediate output.
7. Prefer `final_text_mode: structured` when callers need a machine-readable result envelope.
8. Use `user_input` as the pause/resume boundary for interactive flows.

## Known migration gaps

The current OpenClaw.NET meta path covers DAG execution, fail-fast validation, explicit failure substitution, bounded step execution, JSON intermediate-output contracts, and structured results. It is not yet a full drop-in replacement for every OpenSquilla-native meta-skill contract.

| Gap | Why it matters | Current status |
| --- | --- | --- |
| Richer typed intermediate contracts | Advanced flows may need `output_choices`, `tool_allowlist`, and `clarify` schema as parser/runtime contracts, not only conventions inside `with` payloads. | JSON `output_contract` / `output_schema` required-property checks are implemented. OpenSquilla-native `output_choices`, per-step `tool_allowlist`, and full `clarify` schema remain partial. |
| OpenSquilla-native `user_input.clarify` schema | OpenSquilla supports form/chat collection, typed fields, defaults, enum choices, int ranges, string length limits, cancel keywords, timeouts, and optional natural-language extraction. | OpenClaw.NET currently supports pause/resume with a prompt/default/value string path. It does not yet parse or enforce the full `clarify` schema. |
| Conditional `when` step execution | OpenSquilla can skip a step after dependencies complete by evaluating a Jinja expression against `inputs` and `outputs`. This keeps DAGs compact without forcing every conditional into a classifier branch. | Not implemented as a first-class step field in OpenClaw.NET. Use `llm_classify` routing or separate DAG shape as a workaround. |
| OpenSquilla route semantics | OpenSquilla `route` can choose an `agent` or `skill_exec` target through `when` expressions. | OpenClaw.NET currently supports classification-label routing from `llm_classify` to target steps. That is useful, but not equivalent to the OpenSquilla `route: [{ when, to }]` contract. |
| Jinja template compatibility and safety filters | OpenSquilla authoring guidance relies on filters such as `xml_escape`, `slugify`, `truncate`, and `tojson` to bound and encode untrusted user text or step output. | OpenClaw.NET supports a smaller template surface for `{{ input }}`, `{{ inputs.user_message }}`, and `{{ outputs.<step_id> }}`. Full Jinja compatibility and safety filters are not migrated. |
| `skill_exec` entrypoint/subprocess semantics | OpenSquilla `skill_exec` runs a skill's `entrypoint` manifest as a deterministic subprocess, with args/stdin/cwd/path checks and parse modes. This matters for document generation, conversion, and CLI-backed skills. | OpenClaw.NET currently treats `agent` and `skill_exec` as delegated model steps that follow skill instructions. It does not yet provide OpenSquilla-style subprocess entrypoint execution. |
| Meta run history, step trace, replay, and proposals CLI | OpenSquilla exposes `skills meta runs ...`, dry-run replay, and proposal list/show/accept commands for audit and operations. | OpenClaw.NET has session checkpoint restoration and structured per-run output, but no equivalent persistent meta-run CLI/proposal management surface yet. |
| Dedicated meta-layer policy switches | OpenSquilla has a `[meta_skill] enabled = false` control that keeps meta skills installed for inventory/history while hiding `meta_invoke` and rejecting explicit invocation. | OpenClaw.NET has general skill enablement and `disable-model-invocation`, but not the same dedicated meta-layer switch. |
| True parallel step scheduling | OpenSquilla can execute independent steps concurrently up to scheduler limits. | OpenClaw.NET preserves DAG ordering but currently executes ready steps through the runtime loop rather than a parallel scheduler. |
| Built-in MetaSkill catalog and creator/proposal flow | OpenSquilla documents built-in workflows such as `meta-web-research-to-report`, `meta-document-to-decision`, and `meta-skill-creator`, plus proposal inspection and auto-enable audit. | The current OpenClaw.NET path focuses on runtime orchestration. The broader product catalog and proposal workflow are not migrated. |

## Recommendation

Treat the current OpenClaw.NET meta-skill path as a strong OpenSquilla-style implementation for:

- DAG orchestration
- explicit failure substitution
- bounded step execution
- JSON intermediate-output validation
- structured execution results

For deeper OpenSquilla parity, prioritize by direct impact on OpenSquilla MetaSkill portability:

1. **P0: Native DSL compatibility layer.** Support OpenSquilla-native fields that block direct migration: `output_choices`, top-level `tool_args`, per-step `tool_allowlist`, `clarify`, `when`, and `route: [{ when, to }]`. Without this layer, many OpenSquilla `SKILL.md` files require manual rewriting before they can run.
2. **P0: Full `user_input.clarify` schema.** Support typed form/chat collection, defaults, enum choices, int ranges, string limits, cancel keywords, timeouts, and optional natural-language extraction. This is the main blocker for interactive OpenSquilla MetaSkills.
3. **P1: Jinja-compatible template rendering and safety filters.** Add the documented filters such as `truncate`, `xml_escape`, `slugify`, and `tojson`. This lets migrated MetaSkills keep OpenSquilla's prompt-safety patterns instead of weakening them during porting.
4. **P1: `skill_exec` entrypoint/subprocess semantics.** Decide whether `skill_exec` should run real entrypoint manifests. If not, document the OpenClaw.NET behavior as model-delegated execution to prevent false parity assumptions.
5. **P1: Meta run history, step trace, and replay.** Add persistent run records and CLI/API inspection if migrated workflows need audit, replay, or operations support beyond one structured result envelope.
6. **P2: True parallel step scheduling.** Preserve DAG correctness while allowing independent steps to run concurrently. This improves performance and better matches OpenSquilla behavior, but most flows can be migrated without it.
7. **P2: Product-level catalog, creator, and proposal flow.** Add built-in MetaSkills, `meta-skill-creator`, proposal inspection, and auto-enable audit only if OpenClaw.NET needs product-level OpenSquilla parity rather than runtime portability alone.
