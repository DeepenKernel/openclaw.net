# OpenSquilla 元技能迁移说明

这份说明总结了当前 OpenClaw.NET 对 OpenSquilla 风格元技能路径的实现水平，重点放在已经对齐的能力和还需要补齐的迁移项。

## 当前状态

OpenClaw.NET 已经实现了 OpenSquilla 风格元技能编排的核心骨架：

- `kind: meta` 与 `composition.steps` DAG 编排
- `depends_on` 依赖顺序与循环依赖校验
- `llm_classify` 的 `options + route` 分支路由
- `user_input` 的暂停 / 恢复与会话检查点恢复
- `final_text_mode: auto | raw | structured | step:<id>`
- 结构化执行结果 envelope，便于自动化和诊断

这说明当前运行时已经可以承载“先组装技能图，再分类分支，然后执行工具或模型步骤”的基础流程。

参考 OpenSquilla 源码树 `E:\GitHub\opensquilla\src\opensquilla\skills\meta` 的基线可以看到：

- `parser.py` 把 `on_failure` 当作一类显式的失败分支契约来解析与校验，要求目标步骤存在、不能自引用、不能嵌套链式 failover、且一个 fallback 目标只能被一个主步骤占用。
- `types.py` 与 parser 层把 `output_choices`、`tool_allowlist`、`clarify` schema 当成强类型契约，而不是只靠运行时约定。

OpenClaw.NET 现在已经补上了与之对应的首类本地能力：显式失败替代分支、step 级重试 / 超时策略，以及 JSON 中间结果校验。但更完整的 OpenSquilla 元策略面仍然比当前 OpenClaw.NET 实现更宽。

## 已经对齐的部分

### 1. DAG 编排

OpenClaw.NET 会把元技能的步骤定义为 `composition.steps`，并在执行前校验编排图：

- 重复 step ID
- 缺失依赖
- 自依赖
- 依赖环
- `llm_classify` 的路由目标是否有效

这让元路径具备“先失败快、后执行”的治理能力，而不是默默接受坏图并在执行中后置失败。

### 2. 步骤类型

当前运行时支持的核心编排类型包括：

- `agent`
- `skill_exec`
- `tool_call`
- `llm_chat`
- `llm_classify`
- `user_input`

这对应了当前 OpenClaw.NET 元技能执行面上的主要能力边界。

### 3. 结构化输出与诊断

当开启 `final_text_mode: structured` 时，运行时可以返回结构化载荷，包含：

- `skill`
- `final_text`
- `error` / `error_code`
- `steps[]`，包含状态、耗时、失败代码与继续执行标记

此外，元步骤现在也支持：

- `on_failure` 替代分支，并在 parser/runtime 两层校验
- `retry` 与 `timeout_seconds`，用于工具和模型步骤的有界重试与超时
- `output_contract` / `output_schema`，用于校验 JSON 中间结果的必填字段

这有利于自动化测试、日志排障和运维观测，不需要从自由文本最终回答里反向解析执行状态。

## 迁移 checklist

把 OpenSquilla 元技能迁移到 OpenClaw.NET 时，建议按下面的顺序做：

1. 用 `composition.steps` 表达编排图，并用 `depends_on` 控制顺序。
2. 用 `llm_classify` 处理分支选择，而不是靠字符串判断。
3. 如果失败步骤应该激活一个替代步骤，并把替代步骤输出镜像回主步骤 ID 给下游依赖使用，就使用 `on_failure`。
4. 如果只是希望失败后继续执行、但不需要替代分支语义，再使用 `with.continue_on_error`。
5. 对需要有界执行的工具或模型步骤，配置 `retry.max_attempts`、可选的 `retry.backoff_ms`，以及 `timeout_seconds`。
6. 如果下游依赖结构化中间结果，使用 `output_contract` / `output_schema`，并声明 `format: json` 与 `required_properties`。
7. 若需要机器可读结果，优先使用 `final_text_mode: structured`。
8. 对交互式流程，使用 `user_input` 来承载暂停与恢复边界。

## 当前已知的迁移差距

当前 OpenClaw.NET 的元路径已经覆盖 DAG 执行、fail-fast 校验、显式失败替代、有界 step 执行、JSON 中间结果契约，以及结构化执行结果。但它还不是 OpenSquilla 原生元技能契约的完全等价替代。

| 差距项 | 影响 | 当前状态 |
| --- | --- | --- |
| 更完整的中间结果强类型约束 | 一些高级流程需要把 `output_choices`、`tool_allowlist`、`clarify` schema 等视为 parser/runtime 契约，而不是只放在 `with` 载荷里的约定。 | 已实现 JSON `output_contract` / `output_schema` 必填字段校验；OpenSquilla 原生 `output_choices`、step 级 `tool_allowlist`、完整 `clarify` schema 仍是部分对齐。 |
| OpenSquilla 原生 `user_input.clarify` schema | OpenSquilla 支持 form/chat 收集、字段类型、默认值、枚举选项、整数范围、字符串长度限制、取消词、超时与可选自然语言抽取。 | OpenClaw.NET 目前支持 prompt/default/value 字符串路径的暂停 / 恢复；尚未解析或强制执行完整 `clarify` schema。 |
| 条件式 `when` step 执行 | OpenSquilla 可以在依赖完成后基于 `inputs` 与 `outputs` 执行 Jinja 表达式来跳过 step，让 DAG 不必把所有条件都塞进分类分支。 | OpenClaw.NET 还没有把 `when` 作为首类 step 字段实现。可暂时用 `llm_classify` 路由或拆分 DAG 规避。 |
| OpenSquilla 原生 `route` 语义 | OpenSquilla 的 `route` 可以通过 `when` 表达式为 `agent` 或 `skill_exec` 选择目标 skill。 | OpenClaw.NET 当前支持从 `llm_classify` 分类标签到目标 step 的路由。这有用，但不等价于 OpenSquilla 的 `route: [{ when, to }]` 契约。 |
| Jinja 模板兼容性与安全过滤器 | OpenSquilla 作者指南依赖 `xml_escape`、`slugify`、`truncate`、`tojson` 等过滤器，对不可信用户文本和 step 输出做边界控制与编码。 | OpenClaw.NET 当前模板面更小，主要支持 `{{ input }}`、`{{ inputs.user_message }}`、`{{ outputs.<step_id> }}`。完整 Jinja 兼容性和安全过滤器尚未迁移。 |
| `skill_exec` entrypoint / subprocess 语义 | OpenSquilla 的 `skill_exec` 会运行 skill 的 `entrypoint` manifest 子进程，支持 args/stdin/cwd/path 校验和 parse modes。文档生成、格式转换、CLI-backed skills 会依赖它。 | OpenClaw.NET 当前把 `agent` 与 `skill_exec` 都作为遵循 skill instructions 的模型委托步骤处理，还没有 OpenSquilla 风格的子进程 entrypoint 执行。 |
| Meta run history、step trace、replay、proposals CLI | OpenSquilla 暴露 `skills meta runs ...`、dry-run replay、proposal list/show/accept 等命令，便于审计和运维。 | OpenClaw.NET 有会话检查点恢复和结构化单次运行输出，但还没有等价的持久化 meta-run CLI / proposal 管理面。 |
| 专用 meta-layer 策略开关 | OpenSquilla 有 `[meta_skill] enabled = false`，可以保留已安装元技能用于 inventory/history，同时隐藏 `meta_invoke` 并拒绝显式调用。 | OpenClaw.NET 有通用 skill enablement 和 `disable-model-invocation`，但还没有等价的专用 meta-layer 开关。 |
| 真正的并行 step 调度 | OpenSquilla 可以在 scheduler 限制内并发执行独立 steps。 | OpenClaw.NET 保留了 DAG 顺序正确性，但当前通过运行时循环推进 ready steps，不是并行 scheduler。 |
| 内置 MetaSkill 目录与 creator/proposal 流程 | OpenSquilla 文档包含 `meta-web-research-to-report`、`meta-document-to-decision`、`meta-skill-creator` 等内置工作流，以及 proposal inspection 和 auto-enable audit。 | 当前 OpenClaw.NET 路径聚焦运行时编排；更宽的产品级目录和 proposal 工作流尚未迁移。 |

## 建议

把当前 OpenClaw.NET 的元技能路径理解为强 OpenSquilla 风格实现，覆盖：

- DAG 编排
- 显式失败替代
- 有界 step 执行
- JSON 中间结果校验
- 结构化执行结果

后续如果要继续追求更完整的 OpenSquilla parity，建议按“直接影响 OpenSquilla MetaSkill 可移植性”的顺序补齐：

1. **P0：OpenSquilla 原生 DSL 兼容层。** 支持会直接阻塞迁移的原生字段：`output_choices`、顶层 `tool_args`、step 级 `tool_allowlist`、`clarify`、`when`、`route: [{ when, to }]`。没有这一层，很多 OpenSquilla `SKILL.md` 都需要手工改写后才能运行。
2. **P0：完整 `user_input.clarify` schema。** 支持 typed form/chat 收集、默认值、枚举选项、整数范围、字符串长度限制、取消词、超时，以及可选自然语言抽取。这是交互式 OpenSquilla MetaSkill 的主要迁移阻塞点。
3. **P1：兼容 Jinja 的模板渲染与安全过滤器。** 增加 `truncate`、`xml_escape`、`slugify`、`tojson` 等文档要求的过滤器，让迁移后的 MetaSkill 保留 OpenSquilla 的 prompt safety 写法，而不是为了适配而弱化安全边界。
4. **P1：`skill_exec` entrypoint/subprocess 语义。** 明确 `skill_exec` 是否要运行真实 entrypoint manifest。如果不做，就在 OpenClaw.NET 文档里把它定义为模型委托执行，避免产生“同名即等价”的误判。
5. **P1：Meta run history、step trace 和 replay。** 如果迁移后的流程需要审计、回放或运维排障，就补持久化 run records 与 CLI/API inspection，而不只依赖单次 structured envelope。
6. **P2：真正的并行 step 调度。** 在保持 DAG 正确性的同时，让独立 steps 并发执行。这能改善性能并贴近 OpenSquilla 行为，但大多数流程不依赖它才能完成迁移。
7. **P2：产品级 catalog、creator 与 proposal 流程。** 只有在 OpenClaw.NET 需要产品级 OpenSquilla parity，而不只是 runtime 可移植性时，再补内置 MetaSkill、`meta-skill-creator`、proposal inspection 与 auto-enable audit。
