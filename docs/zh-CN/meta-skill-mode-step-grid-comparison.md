# Agent Skill 状态机工程：Mode-Step 网格 vs MetaSKILL

## 一句话定位

| | Mode-Step 网格 | MetaSKILL |
|---|---|---|
| **本质** | 文件系统级的手工状态机约定 | 运行时驱动的声明式 DAG 编排引擎 |
| **核心思想** | 用目录结构和 Step File 契约把长 prompt 拆成窄边界状态 | 用 YAML `composition.steps` 声明步骤 DAG，由引擎而非模型保证执行顺序 |
| **出处** | 博客园《[Agent Skill 状态机工程：Mode-Step 网格如何拆开工作流边界](https://www.cnblogs.com/ai-old-six/p/20828455)》 | OpenClaw.NET 项目内置 |

## 一、问题域的共识高度一致

两者诊断出了**完全相同的根本问题**：

> 复杂 Skill 的失控不是因为 prompt 不够长，而是生命周期、权限边界和完成条件被混在同一层。

| 博客指出的失控迹象 | MetaSKILL 的对应解法 |
|---|---|
| 模型跳过中间步骤 | `depends_on` 强制 DAG 顺序，引擎不在步骤 A 完成前调度步骤 B |
| Validate 阶段顺手修文件 | `kind: llm_chat` 的 `tool_allowlist` 按步管控工具访问；`on_failure` 显式建模失败分支 |
| 中途失败只能从头跑 | `user_input` 暂停检查点 + `SessionMetaRunRecord` 完整审计追踪，支持 replay/reconstruct |
| 文件越写越长 | 声明式 YAML，每步不超过 40 行定义，无限堆叠自然语言分支 |

## 二、状态建模：文件系统 vs 声明式 DAG

这是两者最核心的分野。

### Mode-Step：文件系统即状态机

```
skills/my-skill/
├── steps-c/           ← Create 生命周期
│   ├── step-01.md
│   ├── step-02.md
│   └── step-03.md
├── steps-v/           ← Validate 生命周期
│   └── step-01.md
├── steps-e/           ← Edit 生命周期
│   └── step-01.md
```

**模式识别**靠文件路径，**状态转移**靠每个 Step File 末尾的 `NEXT STEP` 自然语言指令，**边界约束**靠 Step File 里的 `MANDATORY RULES` 和 `CONTEXT BOUNDARIES` 字段。本质上是**约定优于配置**——Agent 读文件，按文件内容约束自己的行为。

### MetaSKILL：YAML 声明 DAG

```yaml
composition:
  steps:
    - id: gather
      kind: skill_exec
      skill: git-log
    - id: analyze
      kind: llm_chat
      depends_on: [gather]          # ← 硬依赖，引擎保证
      on_failure: fallback-analyze   # ← 失败时自动替换
      timeout_seconds: 120
      retry:
        max_attempts: 3
        backoff_ms: 1000
    - id: fallback-analyze
      kind: llm_chat
      # 不声明 depends_on（引擎约束）
```

**模式识别**靠 `kind: meta` 标记，**状态转移**靠运行时引擎的 DAG 调度器（wave-based 并行执行），**边界约束**靠 `tool_allowlist`、`output_contract`（JSON Schema 校验输出合法性）、`routes`（条件分支）。

### 关键区别

| 维度 | Mode-Step 网格 | MetaSKILL |
|---|---|---|
| 状态建模方式 | 隐式（文件系统路径约定） | 显式（YAML `composition.steps`） |
| 边界强制 | 靠 prompt 约束（"你不可做 X"） | 靠引擎保证（`tool_allowlist`、`output_contract`） |
| 依赖关系 | 人工写在 NEXT STEP 里 | `depends_on` 被调度器强制执行，不依赖模型遵守 |
| 状态恢复 | 读 artifact frontmatter / sentinel 文件 | `SessionMetaRunRecord` + `user_input` 暂停检查点 + CLI `replay`/`reconstruct` |
| 适用范围 | 任何 LLM Agent | 需要 OpenClaw.NET 运行时 |

## 三、执行模型：prompt 驱动 vs 引擎驱动

这是两者最本质的工程差异。

### Mode-Step：模型即调度器

Step File 描述了"这一步该做什么，做完后去哪"，但**实际调度权在模型手里**。`NEXT STEP`、`MANDATORY RULES`、`SUCCESS CRITERIA` 都是给模型读的自然语言。模型不遵守时没有硬性阻止——最多在 prompt 里再补一条"必须遵守"。

这类似于**协程**：单个执行流，模型自己决定什么时候 yield。优点是零基础设施成本，缺点是边界只有自然语言的强度。

### MetaSKILL：运行时即调度器

MetaSKILL 的执行路径：

```
Parse YAML → Validate DAG (环路检测, 5 条 on_failure 约束)
→ Wave-based 调度 (同 wave 内并行, wave 间串行)
→ 每步执行:
   ├── 渲染 Jinja2 模板 ({{ outputs.step_id.field }})
   ├── 评估 when 条件（条件为 false 则跳过）
   ├── 应用 tool_allowlist（硬限制工具可见性）
   ├── 执行并捕获结果（含计时、失败码）
   ├── 校验 output_contract（JSON Schema 不合规则标为失败）
   └── 激活 on_failure 或 routes 分支
→ 每步写 SessionMetaRunRecord（审计）
```

类似于**抢占式调度器**：引擎持有控制权，每步结束后交回引擎决定下一步。模型不被要求遵守任何流程纪律——纪律外部化给了引擎。

## 四、失败处理

| | Mode-Step 网格 | MetaSKILL |
|---|---|---|
| **重试** | 需人工重跑或 prompt 里写重试逻辑 | `retry.max_attempts` + `backoff_ms`，引擎自动化 |
| **超时** | 无硬超时 | `timeout_seconds` + `CancellationToken`，4 层超时保护 |
| **失败转移** | 依赖 NEXT STEP 分支 | `on_failure` 显式声明，引擎激活备用步骤，下游步骤无感知 |
| **恢复** | 读 sentinel 文件，模型自己判断 | `user_input` 步骤类型，检查点外化到 Session，replay/reconstruct CLI 工具 |
| **工程约束** | 无 | 5 条约束：回退目标必须存在、不能自引用、不能链式回退、一对一引用、不能有 depends_on |

## 五、目录结构对比

### Mode-Step

```
skills/<skill-name>/
├── SKILL.md              # 入口元数据
├── workflow.md           # 初始化、参数处理、模式判断、首步路由
├── workflow.yaml         # (可选)
├── steps-c/              # Create 生命周期
│   ├── step-01-gather-inputs.md
│   ├── step-02-analyze.md
│   └── step-03-generate-output.md
├── steps-v/              # Validate 生命周期
│   └── step-01-validate.md
├── steps-e/              # Edit 生命周期
│   └── step-01-assess-and-edit.md
├── scripts/              # 确定性逻辑放脚本
├── references/
└── templates/
```

### MetaSKILL

```
skills/<skill-name>/
├── SKILL.md              # kind: meta + composition 声明
├── subskills/            # 委托的子技能
│   ├── fetcher/SKILL.md
│   └── reporter/SKILL.md
├── scripts/
├── references/
└── templates/
```

核心差异：MetaSKILL 没有 `steps-c/v/e` 子目录——所有步骤在一个 `SKILL.md` 的 `composition.steps` 中声明式定义，DAG 结构一目了然。但采纳 Mode-Step 思维仍然有价值：**按生命周期和职责来组织 `composition.steps` 的分组注释和 ID 命名**。

## 六、适合场景

| 场景 | 选择 |
|---|---|
| 独立 Agent 项目，无专用运行时 | Mode-Step 网格（零基础设施） |
| 3 步以内、无并行需求 | 两者均可，Mode-Step 更轻 |
| 需要 DAG 并行（如 fan-out 搜索后合并） | MetaSKILL（`fan_out` + wave 调度原生支持） |
| 需要硬性 step 输出校验 | MetaSKILL（`output_contract` JSON Schema 校验） |
| 需要完善的审计/回放 | MetaSKILL（`SessionMetaRunRecord` + CLI） |
| 快速原型、一次性任务 | Mode-Step 或标准 Skill |
| 多人长期维护的复杂工作流 | MetaSKILL（声明式修改边界清晰，修改一步不影响其他） |

## 七、两者的互补关系

它们不是竞争，而是在不同抽象层解决同一个问题。

- **Mode-Step 是设计方法论**——它回答"怎么拆"（按生命周期分 Mode，按执行阶段分 Step，每步写契约）。任何技能系统都可以采纳这个思路来组织目录结构。
- **MetaSKILL 是执行引擎**——它回答"怎么跑"（DAG 调度、失败回退、输出校验、审计持久化）。它把 Mode-Step 网格想用自然语言达到的那些约束**硬编码进了运行时**。

二者天然互补：**用 Mode-Step 的思维设计 MetaSKILL 的 DAG 拓扑**——

| Mode-Step 模式 | MetaSKILL 映射 |
|---|---|
| Create 主链路 | `kind: skill_exec` / `llm_chat` 步骤链，`depends_on` 串联 |
| Validate 只读链路 | `tool_allowlist` 收紧 + `output_contract` 校验 + `when` 条件隔离 |
| Edit 局部修改 | `when` 条件分支 + `routes` 路由 + 收紧工具可见性 |
| Resume 恢复 | `user_input` 检查点 + `SessionMetaRunRecord` 持久化 |
| Gate 门禁 | `output_contract.required_properties` + `kind: llm_classify` 判断 PASS/CONCERNS/FAIL |

## 八、通用的设计检查清单

无论采用哪种模式，以下问题都值得在写复杂 Skill 之前回答（综合自两份资料）：

1. 最终产物的路径、格式、更新方式是否明确
2. 用户会触发哪些生命周期，Create / Validate / Edit / Resume 是否需要分开
3. 上下文来源是否有优先级：显式参数 → 本地配置 → 自动探测 → 询问用户
4. 每个 Step 是否只有一个目标，是否写明本步不能做什么
5. 失败后能否从某个 Step 恢复，而不是从头重跑
6. 解析、格式校验、覆盖率、diff、渲染等确定性工作是否已下沉到脚本
7. Validate 是否只读，Edit 是否局部，Create 是否不承担校验修复职责
8. 每个 Step 末尾是否有明确的下一跳或完成标记

## 总结

| | Mode-Step 网格 | MetaSKILL |
|---|---|---|
| **理论基础** | 状态机工程（手工） | DAG 编排（声明式） |
| **执行者** | 模型自行理解并遵守 | 运行时引擎强制执行 |
| **约束强度** | 自然语言（软） | 调度器 + tool_allowlist + output_contract（硬） |
| **基础设施** | 零依赖，纯文件 | 需要 OpenClaw.NET Gateway |
| **并行能力** | 无（模型串行执行） | wave-based 调度，fan_out 动态展开 |
| **审计能力** | 靠 sentinel 文件 | 每步计时 + 失败码 + 证据 + replay/reconstruct |
| **学习曲线** | 低（就是写 markdown） | 中（需理解 YAML 结构和运行时语义） |

两者都同意同一件事：**别让模型猜状态**。区别在于 Mode-Step 选择"告诉模型得更清楚"，而 MetaSKILL 选择"不让模型管这件事"。

---

## 参考

- [Agent Skill 状态机工程：Mode-Step 网格如何拆开工作流边界](https://www.cnblogs.com/ai-old-six/p/20828455) — 博客园，2026-06-26
- [Meta-Skills](../meta-skills.md) — OpenClaw.NET 项目文档
- [MetaSkill 用户指南](meta-skill-user-guide.md)
- [MetaSkill 编排架构](meta-skill-orchestration.md)
- [Meta-Skill 创作指南](../authoring/meta-skills.md)
