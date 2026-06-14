# OpenSquilla 元技能迁移缺口（精简版）

本文档仅保留“未完成迁移缺口”与“验收口径”，用于 P1/P2 跟踪。

## 当前结论（2026-06-13）

- 元技能基础能力已可用：DAG 编排、`llm_classify`、`user_input` pause/resume、`final_text_mode`、结构化结果。
- P1 中 `meta-runs` operator 基线已落地：`list/replay(repreview)/reconstruct/proposals/proposals show/proposals accept|dismiss`。
- `proposals accept/dismiss` 已接入 durable `LearningProposal` 生命周期（`meta_run_proposal`）。
- `skill_exec` 已具备 stdin 执行、evidence 持久化与 replay/reconstruct machine-readable 契约。

## 已完成范围（简表）

| 主题 | 状态 | 说明 |
| --- | --- | --- |
| Meta run inspection | 已完成 | `meta-runs` 支持摘要、`--run`、`--verbose`、`--json` |
| Replay preview contract | 已完成 | `meta-runs replay` 输出缺口原因与 requirements |
| Audit reconstruction | 已完成 | `meta-runs reconstruct` 输出 timeline/checkpoint；`skill_exec` 含 notes |
| Proposal review lifecycle | 已完成 | `proposals accept/dismiss` 更新 durable `LearningProposal`（approved/rejected） |
| skill_exec contract | 已完成 | stdin 透传 + evidence（`input_mode/stdin_bytes/parse_mode/command`） |

## 剩余迁移缺口（按优先级）

### P1（仍影响运维完整性）

1. proposal provenance 域层语义未闭环
- 现状：proposal 列表/详情仍以 derived 视图为主，缺完整 provenance 快照与域层回滚/变更流程。
- 影响：跨版本审计与长期治理能力有限。
- 建议：将 derived proposal 实体化为域对象，补齐 provenance snapshot、状态迁移与回滚语义。

2. skill_exec operator UX 深化不足
- 现状：契约与证据已齐，但高层运维体验（聚合视图、批量分析、失败分支导诊）仍弱。
- 影响：大规模排障效率不理想。
- 建议：在现有 machine-readable 契约上增加 operator-first 视图与聚合诊断入口。

### P2（能力增强，不阻塞基础迁移）

3. 并行 step 调度未实现
- 现状：ready steps 仍串行推进。
- 影响：吞吐与时延不及 OpenSquilla 并发调度模型。
- 建议：引入并发执行器，保持 DAG 正确性前提下并行独立 steps。

4. 产品级 MetaSkill catalog / creator 流程缺失
- 现状：运行时能力优先，未迁移完整产品工作流（catalog、creator、proposal pipeline）。
- 影响：产品层 parity 不足，但不阻塞 runtime 可移植性。
- 建议：按产品路线独立推进，避免耦合 runtime 核心。

## P1 验收口径（更新）

### P1-1 Meta-runs 运维面

- [x] `meta-runs` inspection（摘要/过滤/verbose/json）
- [x] `replay` preview-only + `reconstruct` audit-only
- [x] `proposals` / `proposals show` 证据视图
- [x] `proposals accept/dismiss` durable lifecycle（`meta_run_proposal`）
- [x] 幂等/冲突/JSON 失败路径契约
- [x] proposal provenance 的域层闭环（snapshot + 回滚/变更）

### P1-2 skill_exec 运维面

- [x] stdin-heavy 执行路径
- [x] evidence 持久化与 reconstruct notes
- [x] replay 缺口 machine-readable 合约（含 `skill_exec_inputs_not_persisted`）
- [x] 更高层 operator UX（聚合与导诊）

## 建议下一步（执行顺序）

1. 先补 P1-2 的 operator UX 深化（提升实战运维效率）。
2. 再推进 P2（并行调度与产品工作流）。

## P1 执行任务单（可直接开工）

### Task A：P1-1 provenance 域层闭环

目标：把“derived proposal 视图”升级为具备 provenance 快照与可审计迁移语义的域对象能力。

建议改动文件：

- `src/OpenClaw.Core/Models/LearningModels.cs`
- `src/OpenClaw.Core/Models/Session.cs`
- `src/OpenClaw.Cli/SkillCommands.cs`
- `src/OpenClaw.Tests/SkillCommandsTests.cs`

实施要点：

1. 为 `meta_run_proposal` 补齐 provenance 快照字段（来源 run/step/checkpoint 关键指纹）。
2. 明确 proposal 域层状态迁移约束（pending -> approved/rejected，禁止不合法逆迁移）。
3. 在 `proposals show` 中输出“域层快照字段”和“派生字段”的边界说明，避免语义混淆。
4. 保持现有 CLI 向后兼容：已有 JSON 字段不删除，只做 additive 扩展。

验收测试（建议最小切片）：

- `dotnet test src/OpenClaw.Tests/OpenClaw.Tests.csproj --filter "FullyQualifiedName~RunAsync_MetaRuns_Proposals_"`
- `dotnet test src/OpenClaw.Tests/OpenClaw.Tests.csproj --filter "FullyQualifiedName~RunAsync_MetaRuns_Reconstruct|FullyQualifiedName~RunAsync_MetaRuns_Replay"`

DoD：

- proposal 详情可稳定回放 provenance 关键快照，不依赖瞬时派生。
- 状态迁移冲突规则有测试覆盖（同动作幂等、反向冲突、非法迁移拒绝）。
- 现有调用方 JSON 不破坏。

风险控制：

- 不改 CLI 命令名与现有字段含义。
- 所有新增字段默认可空或带默认值，避免旧数据反序列化失败。

### Task B：P1-2 operator UX 深化

目标：在已完成 machine-readable 契约基础上，提升 `skill_exec` replay/inspection 的运维效率。

建议改动文件：

- `src/OpenClaw.Cli/SkillCommands.cs`
- `src/OpenClaw.Core/Models/Session.cs`
- `src/OpenClaw.Tests/SkillCommandsTests.cs`

实施要点：

1. 增加按失败类型/step kind 的聚合摘要输出（文本与 JSON 同步）。
2. 为 `skill_exec` 增加 operator-first 的诊断提示（例如输入缺失、parse_mode 异常、命令预览截断）。
3. 在 replay preview 中补“优先处理建议”字段，辅助排障顺序。

验收测试（建议最小切片）：

- `dotnet test src/OpenClaw.Tests/OpenClaw.Tests.csproj --filter "FullyQualifiedName~RunAsync_MetaRuns_Replay"`
- `dotnet test src/OpenClaw.Tests/OpenClaw.Tests.csproj --filter "FullyQualifiedName~RunAsync_MetaRuns_Reconstruct"`

DoD：

- 对同一 run，operator 能在一次输出中看见“主要失败簇 + 下一步建议”。
- `skill_exec` 相关提示 machine-readable 与文本输出保持一致。
- 现有 `skill_exec_inputs_not_persisted` 契约不变。

风险控制：

- 诊断字段只做 additive，不替换既有 reason 常量。
- 文本输出强化但不改变现有关键断言短语，避免回归。

### 建议执行顺序

1. 先 Task A（provenance 闭环），再 Task B（operator UX）。
2. 每个 Task 完成后单独跑切片并落一次小提交，降低回滚成本。
