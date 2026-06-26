# Claude Code Dynamic Workflows vs OpenClaw MetaSKILL

## 一句话定位

| | Claude Code Dynamic Workflows | OpenClaw MetaSKILL |
|---|---|---|
| **本质** | 可执行 JavaScript 编排脚本，在会话内动态驱动多 Agent 协作 | YAML 声明的 DAG 编排引擎，由 .NET 运行时调度执行 |
| **设计范式** | 代码即编排（Code-as-Orchestration） | 声明即编排（Declaration-as-Orchestration） |
| **运行环境** | Claude Code CLI 会话内，Node.js 沙箱 | OpenClaw.NET Gateway 服务器进程内 |
| **触发方式** | 用户调用 `/workflow` 或使用 Workflow 工具 | 自然语言触发器匹配 + `meta_invoke` 工具 |

## 一、编排模型对比

### Claude Code Dynamic Workflows：JavaScript 脚本编排

```javascript
export const meta = {
  name: 'review-changes',
  description: 'Review changed files across dimensions, verify each finding',
  phases: [{ title: 'Review' }, { title: 'Verify' }],
}

// Pipeline: items 流经 Review → Verify 两阶段，无 barrier
const DIMENSIONS = [
  {key: 'bugs', prompt: 'Find security and correctness bugs'},
  {key: 'perf', prompt: 'Find performance issues'},
]
const results = await pipeline(
  DIMENSIONS,
  d => agent(d.prompt, {
    phase: 'Review',
    schema: FINDINGS_SCHEMA,
  }),
  review => parallel(review.findings.map(f => () =>
    agent(`Verify: ${f.title}`, {
      phase: 'Verify',
      schema: VERDICT_SCHEMA,
    }).then(v => ({...f, verdict: v}))
  ))
)
const confirmed = results.flat().filter(Boolean)
  .filter(f => f.verdict?.isReal)
```

**核心特点：**
- 编排逻辑是**可执行的 JavaScript 代码**
- `agent()` — 生成一个子 Agent 执行特定任务
- `pipeline()` — 流式多阶段处理，无同步屏障
- `parallel()` — 并行执行多个任务，有同步屏障
- `phase()` — 进度分组显示
- `log()` — 实时日志输出
- `budget` — Token 预算感知
- 支持 `isolation: 'worktree'` 隔离并行修改

### OpenClaw MetaSKILL：YAML 声明式 DAG

```yaml
name: review-changes
kind: meta
composition:
  steps:
    - id: review_all
      kind: fan_out
      iterable: "['bugs', 'perf', 'security']"
      fan_out_max_concurrency: 3
      fan_out_template:
        kind: llm_chat
        with:
          instruction: "Review {{ item }} issues"
    - id: verify_all
      kind: fan_out
      iterable: "{{ outputs.review_all | from_json }}"
      fan_out_max_concurrency: 3
      depends_on: [review_all]
      fan_out_template:
        kind: llm_chat
        with:
          instruction: "Verify finding: {{ item }}"
```

**核心特点：**
- 编排逻辑是**声明式的 YAML**
- 7 种步骤类型覆盖不同执行需求
- `depends_on` 声明 DAG 依赖
- `fan_out` 动态展开并行子步骤
- `routes` 条件路由分支
- `on_failure` 失败替换步骤
- `user_input` 人机交互暂停点

## 二、核心差异矩阵

| 维度 | Claude Code Workflows | OpenClaw MetaSKILL |
|---|---|---|
| **编排语言** | JavaScript（图灵完备） | YAML（声明式，非图灵完备） |
| **学习曲线** | 需要 JS 编程能力 | 需要理解 YAML 结构和运行时语义 |
| **表达力** | 极高：循环、条件、动态计算、try-catch | 中等：DAG + 条件路由 + fan_out 覆盖主流场景 |
| **安全性** | 运行时沙箱约束（无文件系统/网络） | 三步门禁：`tool_allowlist` + `capabilities` + `MetaSkill.Enabled` |
| **状态恢复** | 会话级 checkpoint + resume | SessionMetaRunRecord + CLI replay/reconstruct |
| **审计能力** | 会话内追踪 | 持久化审计记录，CLI 可查询 |
| **超时保护** | Agent 级超时 | 4 层保护：step / retry / session contract / agent loop |
| **输出校验** | JSON Schema（`schema` option） | `output_contract` 每步 JSON Schema |
| **并行策略** | `parallel()` barrier, `pipeline()` streaming | Wave-based 调度，同 wave 内并行 |

## 三、编排原语对比

| Claude Code Workflows | MetaSKILL | 说明 |
|---|---|---|
| `agent(prompt, {schema})` | `kind: agent` / `kind: llm_chat` | 单次 LLM 调用或子 Agent |
| `pipeline(items, stage1, stage2, ...)` | `depends_on` 链 | 流式多阶段，无 barrier |
| `parallel(thunks)` | `fan_out` + wave 调度 | 并行执行 |
| `while (condition) { agent() }` | 无原生循环 | Workflows 支持图灵完备循环 |
| `if (result.foo) { ... }` | `routes` / `when` | 条件分支 |
| `phase('Verify')` | 步骤分组（隐式） | 进度组织 |
| `budget.remaining()` | `timeout_seconds` / contract | 资源边界 |
| — | `user_input` | 人机交互暂停 |
| — | `on_failure` | 声明式失败替换 |
| — | `skill_exec` (子进程) | 确定性脚本执行 |

## 四、设计哲学对比

### Claude Code Workflows：程序员友好，最大化灵活

> "编排就是代码" — 开发者用熟悉的 JavaScript 表达编排逻辑。循环、条件、递归、try-catch 全都可以。适合需要动态决策的复杂场景。

**优势：**
- 图灵完备，没有表达能力上限
- 开发者零学习成本（就是写 JS）
- Token 预算感知，可动态调整策略
- `pipeline()` 流式处理避免不必要的 barrier

**代价：**
- 脚本中的 bug 可能导致非预期行为
- 缺少声明式约束的编译期安全性
- 没有 `user_input` 暂停点
- 不持久化审计记录

### OpenClaw MetaSKILL：安全第一，声明即约束

> "声明就是约束" — 用 YAML 描述 DAG 结构，运行时保证执行正确性。适合需要长期维护、多人协作的生产工作流。

**优势：**
- 解析时 DAG 验证（环路检测、5 条 on_failure 约束）
- 三步安全门禁保证工具访问范围
- 7 种步骤类型精细控制执行成本
- 4 层超时保护
- 完整审计追踪 + CLI replay/reconstruct
- 双运行时（AgentRuntime + MafAgentRuntime）

**代价：**
- 表达能力限于 DAG（无循环，元技能不能调用元技能）
- 学习 YAML 结构和 7 种步骤类型
- 需要 OpenClaw.NET Gateway 运行时

## 五、适用场景

| 场景 | 推荐 |
|---|---|
| 快速审查 PR、寻找 bug | Claude Code Workflows（灵活的 ad-hoc 脚本） |
| 多维度研究分析 | Claude Code Workflows（循环探索、动态调整） |
| 生产环境 CI/CD 工作流 | MetaSKILL（审计、CLI、持久化） |
| 需要人机交互暂停审批 | MetaSKILL（`user_input` 检查点） |
| 多人长期维护的重复任务 | MetaSKILL（声明式修改边界清晰） |
| 需要跨运行时一致执行 | MetaSKILL（双运行时 parity 保证） |
| 一次性探索性分析 | Claude Code Workflows |
| 确定性脚本 + LLM 混合编排 | MetaSKILL（`skill_exec` + `llm_chat` 混合） |

## 六、互补关系

两者不是竞争，而是覆盖编排光谱的两端：

- **Claude Code Workflows** 覆盖**会话内动态编排** — 开发者用 JS 快速表达意图，适合探索性工作
- **MetaSKILL** 覆盖**持久化生产编排** — 声明式定义，运行时保证审计、安全、可恢复

理想的组合：用 **Claude Code Workflows 做原型和探索**，模式稳定后用 **MetaSKILL 模板固化**成可审计、可回放的生产级工作流。

## 总结

| | Claude Code Workflows | MetaSKILL |
|---|---|---|
| **编排形式** | JavaScript 可执行脚本 | YAML 声明式 DAG |
| **图灵完备** | 是 | 否（DAG，无循环） |
| **声明期验证** | 运行时 | 解析时 + 运行时 |
| **安全门禁** | 沙箱 | 三步 tool_allowlist + capabilities + policy |
| **审计持久化** | 会话内 | 持久化 + CLI 查询 |
| **人机交互** | 无原生暂停点 | `user_input` 检查点 |
| **Token 预算感知** | `budget` 全局变量 | 4 层超时保护 |
| **部署方式** | Claude Code CLI 会话 | .NET Gateway 服务器 |
| **最适合** | 探索性、一次性、程序员驱动 | 生产级、可审计、长期维护 |

两者共同的核心洞察：**复杂 AI 工作流不能靠单一长 prompt 驱动，需要显式的编排结构**。区别在于 Claude Code 选择"用代码表达编排"，MetaSKILL 选择"用声明约束编排"。

---

## 参考

- [Meta-Skills](../meta-skills.md) — OpenClaw.NET 项目文档
- [MetaSkill 编排架构](meta-skill-orchestration.md)
- [MetaSkill 用户指南](meta-skill-user-guide.md)
- [Meta-Skill 编写指南](../authoring/meta-skills.md)
