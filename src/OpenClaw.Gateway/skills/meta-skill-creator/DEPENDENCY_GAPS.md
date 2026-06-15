# Meta Skill Creator Dependency Gaps

## Scope

This document tracks runtime dependencies required by
src/OpenClaw.Gateway/skills/meta-skill-creator/SKILL.md after direct migration
from OpenSquilla.

The SKILL.md file is intentionally preserved as-is. This gap list exists to
make runtime readiness explicit.

## Migration Status

- Skill file migration: complete
- Runtime dependency parity: complete (semantic implementation)
- Current operating mode: executable for PREVIEW_ONLY and FULL_GATED flows,
   with creator-gate tools implemented with machine-readable gate contracts

## Missing Tool Dependencies

All referenced tool names now have matching OpenClaw implementations and
Gateway registration:

- emit_text
- meta_skill_fill_slots
- meta_skill_assemble
- meta_skill_lint_run
- meta_skill_smoke_run
- meta_skill_runtime_e2e_run
- meta_skill_persist_proposal

## Missing Skill Dependencies

The referenced child skill has been migrated under Gateway skills:

- history-explorer

## Runtime Impact

The prior missing-dependency failure mode is closed. Current behavior includes
executable creator gate semantics for lint/smoke/runtime_e2e/persist with
stable machine-readable output envelopes.

## Recommended Enablement Plan

1. Continue tightening OpenSquilla field-level envelope parity for edge cases.
2. Add deeper integration tests for creator decision branches and conflict paths.
3. Wire persisted proposal output into governance lifecycle acceptance flows.

## Validation Checklist

- Meta route can invoke meta-skill-creator without missing-tool failures.
- PREVIEW_ONLY completes and returns final_response.
- FULL_GATED completes all gate steps and persistence path.
- Failure messages are deterministic and machine-readable.

## Validation Evidence

- Runtime parity tests cover PREVIEW_ONLY and FULL_GATED paths in both Agent and MAF runtimes, including completed `lint`/`smoke`/`runtime_e2e`/`persist` gate steps.
- Creator tool contract tests validate machine-readable error/result envelopes and persisted `gates.json` structure (`proposal_id`, `creator_mode`, `lint`, `smoke`, `runtime_e2e`).
- Focused test slices pass:
   - `dotnet test src/OpenClaw.Tests/OpenClaw.Tests.csproj --filter FullyQualifiedName~MetaSkillCreator`
   - `dotnet test src/OpenClaw.Tests/OpenClaw.Tests.csproj --filter "FullyQualifiedName~MafAgentRuntime_ExecuteMetaSkillAsync_MetaSkillCreator_FullGated_Completes|FullyQualifiedName~ExecuteMetaSkillAsync_MetaSkillCreator_FullGated_ProducesPersistencePayload"`
