# MetaSkills 协议在 OpenClaw.NET 智能体运行时的落地架构与实践指南


## 1. 引言与概述

### 1.1 文档目的与读者定位

#### 1.1.1 目标读者

本文档面向技术架构师、.NET 开发者、AI 工程师与运维安全工程师。技术架构师将从中获得 MetaSkills 协议在企业级 AI 系统中的顶层设计 rationale 以及 OpenClaw.NET 与现有基础设施集成的决策依据；.NET 开发者可掌握从源码检出到 NativeAOT 发布产物的端到端构建流程；AI 工程师将理解 MetaSkills 如何通过声明式组合语法将多步骤高价值工作沉淀为可复用任务协议；运维安全工程师则可深入理解被动治理体系（Passive Governance）[^53^]、三层沙箱与 HEP 机制如何构成生产环境的安全基线。

#### 1.1.2 文档定位

本文档是一条从 OpenSquilla 协议概念到 OpenClaw.NET 实现的端到端技术落地指南，以可运行的代码、可量化的性能数据与可复现的架构决策贯穿全文。后续章节安排如下：第 2 章解析 MetaSkills 协议核心概念，第 3–6 章展开 OpenClaw.NET 基础架构、NativeAOT 编译管线、MAF 适配层与被动治理体系，第 7–10 章覆盖四层记忆系统、TokenJuice 优化、Dream Mode 与生产部署运维。

### 1.2 问题域与背景

#### 1.2.1 现有 LLM 工具调用模式的局限

当前 LLM 与外部工具交互的主流模式可归纳为三类：函数调用（Function Calling）、模型上下文协议（MCP）与传统工作流编排（Workflow Automation）。函数调用由 OpenAI 于 2023 年提出[^26^]，在每次请求时将工具定义嵌入提示词，由模型决策调用目标与参数。该模式存在三项结构性缺陷：工具定义重复传输导致"上下文税"（Context Tax）[^28^]，token 消耗随工具数量线性膨胀；缺乏原生多步组合语义，需外部自行实现编排；各厂商格式互不兼容，迁移成本高昂[^21^]。

MCP 由 Anthropic 于 2024 年 11 月发布[^24^]，通过 JSON-RPC 层标准化工具发现与调用，但仍以 LLM 为中心编排——中心 LLM 逐步调用无状态 MCP 服务器[^22^]，每一步均需 LLM 参与，难以表达"并行执行两项分析后合并输出"这类确定性工作流。传统工作流编排工具（如 n8n）虽支持可视化 DAG，却缺乏与 LLM 推理过程的深度融合[^19^]。

#### 1.2.2 MetaSkills 协议的核心价值

MetaSkills 是 OpenSquilla 的任务协议层（task-protocol layer），不引入新执行原语，而定义了将技能、工具、LLM 调用与子智能体组织为可复用任务协议的方式[^2^]。其类比如 Makefile 与 shell 命令：Makefile 不替换命令，而定义组合方式；MetaSkill 不替换技能或工具，而声明高价值工作应如何被理解、结构化、检查与交付[^1^]。

MetaSkills 提供四项核心优势：协议化能力捕获，通过 `kind: meta` 的 SKILL.md 文件与 `composition.steps` 块编码工作流；自然语言触发，用户以日常短语触发预定义流程；可审计与可重放，运行时记录每步输入、输出与状态，支持通过 CLI 回溯历史[^2^]；持续改进，重复协作模式可被识别为 MetaSkill 提案，经审核后纳入技能库[^1^]。

#### 1.2.3 OpenClaw.NET 技术选型 Rationale

OpenClaw.NET 选择 .NET 10 与 NativeAOT 基于三项核心考量。**极速冷启动**：NativeAOT 将 IL 在构建阶段编译为原生机器码，配合激进剪裁（Trimming），生产镜像约 15 MB，冷启动延迟较 JIT 降低 70% 以上，实现数十毫秒级启动[^6^]。**强类型安全**：C# 14 的类型系统配合 `Microsoft.Extensions.AI` 抽象库，在编译期捕获运行时错误，并通过中间件注入实现可观测性、审计与缓存[^6^]。**被动治理**：三层沙箱架构将第三方 JS 代码隔离于独立子进程，高危工具默认要求显式审批，被动治理账本持久化监督决策[^53^][^6^]。

**表 1-1  MetaSkills 与其他方案对比**

| 对比维度 | MetaSkills | Function Calling | MCP | 传统 Workflow |
|:---------|:-----------|:-----------------|:----|:--------------|
| 触发方式 | 自然语言短语 + 模型选择 | LLM 实时决策 | LLM 实时决策 | 事件 / 定时触发 |
| 组合能力 | 多步 DAG，支持并行与条件路由 | 单步原子调用 | 单步调用，LLM 编排 | 固定 DAG，无 LLM 融合 |
| AOT 友好性 | 声明式 YAML，编译期 schema 验证 | 运行时 JSON 解析 | JSON-RPC 运行时协商 | 依赖运行时脚本引擎 |
| 安全模型 | 风险元数据 + 能力白名单 + 操作审批 | 无原生安全层 | 服务器级授权 | 引擎级权限 |
| 可审计性 | 步级输入/输出/状态全记录 | 依赖应用层日志 | 依赖服务器日志 | 实例级日志 |
| LLM 依赖度 | 低（确定性执行 DAG） | 高（每步需 LLM） | 高（中心编排器） | 无（纯确定性） |

MetaSkills 在触发方式上融合了自然语言灵活性与声明式协议的确定性：工作流结构在 SKILL.md 中静态定义，运行时按依赖图严格执行，无需每步经过 LLM 决策，显著降低 token 消耗与延迟。其 YAML 格式天然契合 NativeAOT 的编译期验证理念。在安全维度，风险元数据（`metadata.opensquilla.risk`）与能力白名单（`tool_allowlist`）形成纵深防御：开发者声明最高风险等级，运行时据此决定是否启用自动审批[^3^]。Function Calling 缺乏原生安全抽象，MCP 停留在服务器级授权，传统 Workflow 则与 LLM 语义推理完全脱节。

### 1.3 关键技术术语

#### 1.3.1 术语对照表

以下 12 个核心术语贯穿全文。

**MetaSkills**（元技能）：OpenSquilla 的任务协议层，通过 `kind: meta` 的 SKILL.md 文件将多步工作流定义为可触发、可审计、可改进的协议[^2^]，不引入新执行原语而重组现有技能、工具、LLM 调用与子智能体。

**SKILL.md**：OpenSquilla/OpenClaw 生态的技能清单文件，YAML 格式，声明元数据、触发条件、能力需求与组合步骤。MetaSkill 是其 `kind: meta` 的特殊类别[^3^]。

**NativeAOT**：.NET 10 原生提前编译技术，构建阶段将 IL 编译为机器码，消除 JIT 开销，实现亚秒级冷启动与约 15 MB 容器镜像[^55^]。

**MAF**（Microsoft Agent Framework）：微软官方跨语言智能体框架，OpenClaw.NET 通过 `Runtime.Orchestrator=maf` 提供一等适配，支持持久化工作流委托[^53^]。

**PEV**（Plan-Execute-Verify）：OpenClaw.NET 可选的高危工具执行模式，经契约、证据与验证三阶段治理高风险操作[^53^]。

**TokenJuice**：OpenSquilla 的 token 效率优化子系统，通过工具压缩与上下文裁剪，典型场景下实现 60%–80% 成本节约[^2^]。

**四层记忆**（Four-Layer Memory）：包含热记忆（`MEMORY.md`，每轮注入）、暖记忆（每日日志，3 小时同步）、冷记忆（深度报告按需检索）与运行时脉动上下文[^58^][^64^]。

**三层沙箱**（Three-Tier Sandbox）：进程级沙箱（JS 子进程隔离）、工具级审批（`RequireToolApproval`）与网络级白名单的三层纵深防御架构[^6^]。

**Dream Mode**（梦境模式）：OpenClaw 离线推理模式，后台压缩提炼对话并整合记忆，产物写入 REM 回填队列[^59^]。

**HEP**（Harness Evolution Proposal）：OpenClaw.NET 治理机制，系统被动提出策略、路由与工具治理改进建议，需人工审核后生效[^53^]。

**Passive Governance**（被动治理）：OpenClaw.NET 核心治理哲学，所有治理决策通过 Harness Contracts、Evidence Bundles 与 Governance Ledger 实现，默认不改变运行时行为[^53^]。

**Composition Steps**（组合步骤）：MetaSkill 的原子执行单元，支持 `agent`、`llm_chat`、`tool_call`、`skill_exec`、`llm_classify` 等类型，通过 `depends_on` 形成有向无环图[^3^]。

```yaml
# 代码 1-1  MetaSkill SKILL.md 最小示例
---
name: research-to-decision
kind: meta
description: 将研究需求转化为带来源的决策备忘录
triggers:
  - "帮我调研并给出决策建议"
  - "生成决策备忘录"
meta_priority: 50
always: false
final_text_mode: auto
metadata:
  opensquilla:
    risk: medium
    capabilities: [network-read, document-export]
composition:
  steps:
    - id: search
      kind: agent
      skill: web-research
      with:
        query: "{{ inputs.user_message }}"
    - id: analyze
      kind: agent
      skill: summarize
      depends_on: [search]
      with:
        text: "{{ outputs.search | truncate(4000) }}"
    - id: decision
      kind: llm_chat
      depends_on: [analyze]
      with:
        prompt: "基于以下分析给出决策建议：{{ outputs.analyze }}"
```

上述 YAML 声明了名为 `research-to-decision` 的 MetaSkill。`kind: meta` 标识其为元技能；`triggers` 列出自然语言触发短语；`metadata.opensquilla.risk: medium` 声明所需能力授权。`composition.steps` 定义三步 DAG：`search` 检索信息，`analyze` 依赖 `search` 输出进行摘要，`decision` 基于前序分析生成决策建议。`depends_on` 确保顺序执行，无依赖步骤可并行。此声明式结构使执行行为在编译期即可完整推断，无需运行时 LLM 参与编排。


## 2. MetaSkills 协议核心概念

### 2.1 Skills 与 Meta-Skills 的本质区别

OpenSquilla 平台将能力（Capability）划分为两个互补层级：Skill（技能）与 Meta-Skill（元技能）。二者的根本差异体现在任务粒度与复用模式上。Skill 对应单一聚焦的任务模式（one focused task pattern），是一段指令集、脚本或工具辅助函数，其语义边界限定为一次原子性操作[^1^]。Meta-Skill 则对应可复用的多步骤工作流（reusable workflow），由多个 Skill 调用、大语言模型（Large Language Model, LLM）生成步骤、用户输入暂停点或条件分支节点按序编排而成[^1^]。

| 维度 | Skill（技能） | Meta-Skill（元技能） |
|:---|:---|:---|
| 任务粒度 | 单一聚焦的原子操作 | 多步骤组合的工作流 |
| 复用单元 | 可复用的功能函数 | 可复用的协作模式与编排逻辑 |
| 典型示例 | 网页搜索、文件读取、邮件发送 | 竞争情报简报生成、求职申请全流程 |
| 触发方式 | 显式工具调用 | 自然语言意图匹配或显式委托 |
| 状态管理 | 无状态单次执行 | 有状态多步骤，支持中间结果传递 |
| 审计粒度 | 单次调用记录 | 完整步骤链路与生命周期追踪 |

上表从六个维度对比了 Skill 与 Meta-Skill 的结构性差异。Skill 的设计哲学聚焦于"做一件事并做好"，其输入输出遵循严格的 JSON Schema 契约，运行时通过 `ITool` 接口派发执行[^2^]。Meta-Skill 的设计哲学则聚焦于"将高价值协作模式固化为协议"——它不改变任何底层 Skill 的实现逻辑，而是通过声明式编排（declarative orchestration）定义步骤间的数据流、控制流与异常处理策略。这种分层架构确保了原子能力的稳定性与编排逻辑的灵活性互不干扰。

一个直观的类比有助于理解这种关系：Skill 类似于 Shell 环境中的独立命令（如 `grep`、`curl`、`awk`），每个命令完成单一功能；Meta-Skill 类似于 Makefile，它不替换任何原子命令，而是定义目标（target）与依赖（dependency）之间的编排规则，将离散的原子操作串联为可复用的工程流程[^1^]。当用户需要"将竞品动态整理为销售简报"时，底层调用的仍然是搜索、阅读、摘要等原子 Skill，但 Meta-Skill 规定了这些 Skill 的调用顺序、参数传递方式、LLM 介入节点以及输出格式——这一编排知识本身成为可版本化、可分发、可改进的协议资产。

### 2.2 MetaSkill 的四大核心优势

#### 2.2.1 协议化能力

Meta-Skill 将高价值协作模式捕获为标准化协议文件 `SKILL.md`，该文件采用 YAML 格式，通过 `kind: meta` 声明其元技能类型，并通过 `composition.steps` 字段定义完整的工作流编排[^3^]。协议化带来的直接收益是：工作流定义从运行时隐式行为转变为编译期可校验的显式声明，使得版本控制、代码审查与自动化测试成为可能。

一个典型的 `SKILL.md` 文件包含三段式结构：`metadata`（元数据，包括名称、描述与版本）、`triggers`（触发器，定义自然语言意图映射）以及 `composition`（组合体，定义步骤序列与数据流）。`composition.steps` 支持五种步骤类型，覆盖了从子代理委托到确定性工具调用的完整执行语义频谱。以下示例展示了一个包含全部五种步骤类型的 `SKILL.md` 文件，其中 `meta-web-research-to-report` 工作流通过 `agent` 步骤委托研究代理执行搜索，通过 `llm_classify` 步骤对搜索结果进行主题分类，通过 `user_input` 步骤向用户确认研究方向，通过 `llm_chat` 步骤生成报告草稿，最终通过 `tool_call` 步骤将报告写入文件系统。

```yaml
# SKILL.md —— meta-web-research-to-report 完整示例
kind: meta
name: meta-web-research-to-report
description: Turns source-backed research needs into reports or decision memos
version: 1.0.0
triggers:
  - "Research {{topic}} and write a report"
  - "Turn these sources into a brief about {{subject}}"
  - "Help me write a decision memo on {{decision}}"

composition:
  steps:
    # 步骤1：子代理委托 —— 将研究任务委托给专业搜索代理
    - id: research
      type: agent
      skill: web-search-agent
      input:
        query: "{{ inputs.user_message | xml_escape | truncate(512) }}"
        depth: comprehensive

    # 步骤2：LLM 分类 —— 对搜索结果进行主题路由
    - id: classify_topic
      type: llm_classify
      options:
        - "market_analysis"
        - "technical_review"
        - "competitive_intel"
        - "regulatory_brief"
      input:
        text: "{{ outputs.research.summary }}"

    # 步骤3：用户输入 —— 暂停并收集用户确认
    - id: confirm_scope
      type: user_input
      prompt: "The research points to {{outputs.classify_topic}}. Confirm scope or refine?"
      timeout: 300

    # 步骤4：LLM 生成 ——  bounded 生成报告正文
    - id: draft_report
      type: llm_chat
      system_prompt: "You are a senior analyst. Write a concise report."
      input:
        research: "{{ outputs.research | truncate(4000) }}"
        scope: "{{ outputs.confirm_scope }}"
      max_tokens: 2048

    # 步骤5：工具调用 —— 确定性写入操作
    - id: save_report
      type: tool_call
      tool: file_writer
      input:
        filename: "report_{{ metadata.timestamp }}.md"
        content: "{{ outputs.draft_report | xml_escape }}"

  final_text:
    mode: step:draft_report    # 返回步骤4的原始输出作为最终结果
```

上述示例同时展示了 Final Text Modes 的语义控制：`final_text.mode: step:draft_report` 指定将 `draft_report` 步骤的原始输出作为工作流的最终返回结果，而非编排器的自动摘要[^3^]。`composition.steps` 支持的五种步骤类型各有明确的执行语义与适用边界。agent 类型委托子代理（sub-agent）完成 Skill 支持的任务，支持上下文传递与结果回传；llm_chat 类型触发一次有界的 LLM 生成，不进入工具循环；llm_classify 类型要求模型从封闭集合中返回单一值，常用于条件路由；user_input 类型暂停执行并收集用户提供的结构化数据，支持超时与默认值策略；tool_call 类型执行确定性的工具调用，直接映射到 `ITool` 接口的实现[^3^]。这五种步骤类型构成了 Meta-Skill 编排的全部原子动作，任何复杂工作流均可由它们的组合表达。

#### 2.2.2 自然语言触发

Meta-Skill 通过 `triggers` 字段建立了用户自然语言意图到工作流的声明式映射表面（declarative mapping surface）。每个 trigger 本质上是一个模板表达式，可包含占位符（如 `{{topic}}`）用于捕获用户输入中的变量片段[^2^]。当用户以自然语言描述需求时，OpenSquilla 的意图匹配引擎将用户消息与已注册 Meta-Skill 的 trigger 模板进行匹配，选择相似度最高的工作流实例化执行。

Trigger 的设计遵循"覆盖度优先、精确度兜底"的原则：模板应当覆盖同一意图的多种自然语言表达变体，同时通过否定模式过滤排除明显的误匹配场景[^3^]。这种声明式触发机制的价值在于，它将意图识别的知识从运行时硬编码逻辑中解耦，转移到可独立维护、版本化的协议文件中，使得新工作流的接入无需修改运行时代码。

#### 2.2.3 可审计与回放

Meta-Skill 工作流的执行过程产生完整的生命周期记录，涵盖每个步骤的输入参数、输出结果、执行状态（成功/失败/超时）以及资源消耗指标[^1^]。这些记录以结构化格式持久化，支持按工作流实例、步骤类型或时间范围的检索与回放。可审计性（auditability）不仅是合规要求的基础，更为工作流的持续改进提供了数据支撑：通过分析高频执行路径、瓶颈步骤与失败模式，开发者可以识别编排逻辑中的优化机会。

#### 2.2.4 持续改进

Meta-Skill 协议内置了从重复协作模式到新协议提案的闭环机制。当用户或系统检测到某一多步骤协作模式被频繁复用时，`meta-skill-creator` 这一内置 Meta-Skill 可自动分析该模式的步骤序列、输入输出模式与触发条件，生成新的 `SKILL.md` 草案供人工审核与发布[^2^]。这一机制使得 Meta-Skill 目录能够随实际使用场景的演化而动态扩展，形成"使用—沉淀—协议化—再使用"的增强飞轮。

### 2.3 九种内置 Stable MetaSkills

OpenSquilla 平台预置了九种经过生产环境验证的 Stable Meta-Skills，覆盖商业情报、日常运营、文档决策、求职辅助、教育规划、学术写作、内容创作、协议演化与网络研究等核心场景[^2^]。这些内置 Meta-Skills 不仅提供了即用的工作流模板，更展示了协议表达力的上限。

| Meta-Skill 标识符 | 定位与功能描述 | 典型触发条件 |
|:---|:---|:---|
| `meta-competitive-intel` | 将竞品账号动态或市场信号转化为销售、商务拓展或竞争情报简报 | "Analyze competitor X's latest moves" |
| `meta-daily-operator-brief` | 将当日任务、上下文与约束条件整合为运营简报 | "What's my brief for today?" |
| `meta-document-to-decision` | 将合同、报价单、续约通知或电子表格转化为签署、拒绝或协商决策建议 | "Should I sign this contract?" |
| `meta-job-search-pipeline` | 将职位描述（JD）、简历与申请目标转化为申请包与面试准备材料 | "Help me apply for this role" |
| `meta-kid-project-planner` | 生成安全、适龄的学校项目、展示分享或科学活动计划 | "Plan a science project for a 10-year-old" |
| `meta-paper-write` | 支持学术草稿撰写、论文结构设计、引用规划、实验占位符与 LaTeX/PDF 输出路径 | "Help me structure this paper" |
| `meta-short-drama` | 生成短剧脚本、视觉提示词、字幕与本地视频产物 | "Write a short drama script about X" |
| `meta-skill-creator` | 将重复的多技能协作模式转化为新 Meta-Skill 提案 | "I keep doing this sequence, make it a skill" |
| `meta-web-research-to-report` | 将溯源研究需求转化为报告、简报或决策备忘录 | "Research X and write a report" |

上表所列的九种内置 Meta-Skills 展示了协议在异构场景下的通用性。从商务情报到儿童教育计划，这些工作流共享同一套底层语义模型（五步类型、触发器、Final Text Modes），差异仅体现在步骤编排逻辑与领域特定的系统提示词（system prompt）上。这种设计确保了运行时实现的一次性投入即可支撑无限扩展的领域工作流——新增 Meta-Skill 仅需编写新的 `SKILL.md` 文件，无需修改平台核心代码[^2^]。

### 2.4 两种使用方式与请求模板

#### 2.4.1 Natural Delegation 与 Explicit Delegation

用户与 Meta-Skill 的交互可通过两种委托模式发起，二者在表达自由度与执行确定性之间存在权衡[^2^]。

| 维度 | Natural Delegation（自然委托） | Explicit Delegation（显式委托） |
|:---|:---|:---|
| 发起方式 | 直接描述期望结果，不指定工作流 | 显式声明 `Use meta-skill <name>` |
| 意图识别 | 依赖运行时 trigger 匹配引擎 | 绕过匹配，直接路由到指定工作流 |
| 适用场景 | 探索性任务、意图明确的通用请求 | 精确复用已知工作流、自动化脚本集成 |
| 失败回退 | 匹配失败时降级为通用对话 | 指定 Skill 不可用时返回明确错误 |
| 可控性 | 低——用户不控制工作流选择 | 高——用户精确指定执行路径 |
| 表达示例 | "Research Tesla's Q3 earnings and summarize" | "Use meta-web-research-to-report. Outcome: ..." |

Natural Delegation 适用于用户清楚期望结果但不在意执行路径的场景，平台负责从已注册 Meta-Skill 目录中选择最匹配的工作流。Explicit Delegation 则适用于需要精确控制执行路径的场景，例如在自动化脚本、定时任务或经过充分测试的生产流水线中，显式指定工作流名称可消除意图匹配的歧义性[^2^]。

#### 2.4.2 高质量请求模板六要素

无论采用哪种委托模式，高质量的请求均遵循六要素模板结构[^2^]：

- **Outcome（期望结果）**：定义工作流完成后应达成的具体目标，而非执行步骤；
- **Context（上下文）**：提供与工作流相关的背景信息，如时间范围、受众、优先级约束；
- **Decision standard（决策标准）**：明确判断结果是否可接受的量化或定性标准；
- **Expected output（期望输出格式）**：指定输出形态（如 Markdown 表格、JSON 数组、一段摘要）；
- **Constraints（约束条件）**：列出必须遵守的限制，如字数上限、引用来源要求、格式规范；
- **Do not（禁止事项）**：明确排除的内容或行为，如"不要包含投资建议""不要使用中文"。

#### 2.4.3 模板安全子系统

Meta-Skill 的步骤定义中广泛使用 Jinja2 模板语法以支持步骤间数据引用与动态内容注入。然而，直接将用户输入或上游步骤输出嵌入模板会带来提示注入（Prompt Injection）与跨步骤数据污染风险[^3^]。为此，协议规定模板渲染必须通过三层安全防护：XML 实体转义（`xml_escape`）防止特殊字符破坏输出结构；长度截断（`truncate`）防止超长内容淹没上下文窗口；Jinja 沙箱（Jinja Sandbox）禁用危险过滤器与文件系统访问[^3^]。

以下 C# 代码片段展示了 OpenClaw.NET 运行时中模板安全渲染的工程实现，该实现严格遵循协议规定的安全过滤器链：

```csharp
// TemplateSecurityPipe.cs —— 模板安全渲染管线
public sealed class TemplateSecurityPipe
{
    // 安全渲染：依次应用 xml_escape 与 truncate 过滤器
    public string RenderSafe(string template, TemplateContext context)
    {
        // 阶段1：Jinja 沙箱解析 —— 禁用所有非白名单过滤器
        var sandbox = new SandboxEnvironment(
            allowedFilters: new[] { "xml_escape", "truncate", "default" }
        );

        // 阶段2：变量绑定前对 inputs 应用 xml_escape，防御注入
        var escapedInputs = context.Inputs.ToImmutableDictionary(
            kvp => kvp.Key,
            kvp => System.Security.SecurityElement.Escape(kvp.Value) ?? string.Empty
        );

        // 阶段3：模板渲染，对上游步骤输出应用 truncate
        var rendered = sandbox.Render(template, escapedInputs);

        // 阶段4：输出截断，防止上下文溢出（默认上限 2000 字符）
        const int MaxOutputLength = 2000;
        return rendered.Length > MaxOutputLength
            ? rendered[..MaxOutputLength] + "...[truncated]"
            : rendered;
    }
}
```

上述代码体现了协议的安全要求在运行时的具体映射：`xml_escape` 过滤器在变量绑定阶段对所有 `inputs` 字段执行 XML 实体转义，`truncate` 过滤器在输出阶段限制内容长度，Jinja 沙箱通过白名单机制仅允许 `xml_escape`、`truncate` 与 `default` 三种过滤器的使用，从根本上禁用了 `eval`、`filesizeformat` 等危险内置过滤器。不安全的模板实践——如直接使用 `"{{ inputs.user_message }}"` 而不经过 `xml_escape`——在协议层面被标记为不合规，运行时在编译期即可通过静态分析检测并拒绝加载此类定义[^3^]。


## 3. OpenClaw.NET 架构底座

OpenClaw.NET 是基于 .NET 10 的 NativeAOT（Native Ahead-of-Time，原生提前编译）优先的智能体运行时与网关框架。作为 OpenSquilla MetaSkills 协议的目标实现平台之一，其架构设计围绕三个核心原则展开：编译期确定性消除运行时反射、强类型 JSON 契约保证序列化安全、以及本地优先的数据治理策略。这些原则共同构成后续协议映射章节的底层技术上下文。[^1^]

### 3.1 架构设计原则

#### 3.1.1 NativeAOT 优先：运行时禁止反射与动态代码生成

OpenClaw.NET 将 NativeAOT 作为核心发布路径（Core Lane），而非可选优化项。在此约束下，运行时环境中不存在 JIT（Just-In-Time）编译器，也不支持 `System.Reflection.Emit` 动态代码生成或基于反射的运行时类型发现。所有类型元数据必须在编译期通过源生成器（Source Generators）显式注册，使 AOT 编译器能够在构建阶段完整推断类型依赖图，从而执行激进的树摇（Trimming）优化。[^1^][^2^]

这一选择的工程收益体现于三个维度。其一，冷启动延迟较传统 JIT 模式缩减 70% 以上，在容器化部署中可达亚秒级甚至数十毫秒级启动，契合 Serverless 按需计费模型[^3^]。其二，发布产物体积大幅压缩——基于 Ubuntu Chiseled 容器镜像的网关分发包仅约 15 MB，相较等效 Node.js 实现的 200 MB 以上体积缩减了一个数量级[^3^]。其三，内存占用确定性显著提升，空闲状态下仅为 Node.js/V8 等效版本的零头，支持在单台服务器上高密度并发托管数百个独立智能体实例[^3^]。

#### 3.1.2 强类型契约：JSON 序列化通过 JsonSerializerContext 源生成器完成

NativeAOT 约束的直接后果是 `System.Text.Json` 的反射模式完全不可用。OpenClaw.NET 采用 `JsonSerializerContext` 源生成器路径，在编译期为每个参与序列化的类型生成专用代码，彻底规避运行时反射调用[^2^]。所有参与协议通信的 DTO（Data Transfer Object，数据传输对象）、工具参数模型和配置类型均通过 `CoreJsonContext` 统一注册：

```csharp
// CoreJsonContext.cs — 编译期序列化元数据注册
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClaw.Core.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ToolInvocationRequest))]
[JsonSerializable(typeof(ToolInvocationResponse))]
[JsonSerializable(typeof(HarnessContract))]
[JsonSerializable(typeof(EvidenceBundle))]
[JsonSerializable(typeof(MemorySearchResult))]
[JsonSerializable(typeof(SessionSnapshot))]
// 扩展：新增工具类型时必须在此显式声明
internal partial class CoreJsonContext : JsonSerializerContext
{
    // 类型实现由编译器源生成器自动注入
}
```

上述声明中，`[JsonSerializable]` 特性逐一列出所有需要序列化支持的类型，编译器在构建阶段为每个类型生成专用的 `JsonTypeInfo<T>` 元数据实例。使用方通过 `CoreJsonContext.Default.ToolInvocationRequest` 等属性显式传入序列化上下文，确保 NativeAOT 编译器能够静态追踪完整的类型依赖链。[^2^][^4^]

#### 3.1.3 本地优先：记忆存储、向量嵌入、轨迹归档均在本地设备完成

OpenClaw.NET 的记忆子系统（Memory Subsystem）采用本地优先（Local-First）架构，默认以 SQLite 作为存储后端，集成 FTS5（Full-Text Search 5）全文索引与可选的 `sqlite-vec` 向量扩展实现混合检索（Hybrid Search）[^5^][^6^]。每智能体的记忆数据库存放于 `~/.openclaw/memory/{agentId}.sqlite`，包含文件元数据表、文本块表、FTS5 虚拟表和向量虚拟表四层结构[^7^]。当配置本地嵌入引擎（如 Gemma 4 或 ONNX 运行时）时，嵌入计算完全在设备本地完成，原始文本数据不离开宿主机，满足隐私敏感场景与离线运行需求[^5^][^8^]。

### 3.2 核心组件架构

#### 3.2.1 ITool 接口：原子技能契约

`ITool` 接口定义了 OpenClaw.NET 中所有可执行工具的原子契约（Atomic Contract）。每个工具实现须继承 `ITool` 并通过 `CoreJsonContext` 在编译期注册其参数与返回类型：

```csharp
// ITool.cs — 原子技能契约接口定义
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenClaw.Core.Serialization;

namespace OpenClaw.Agent.Tools;

/// <summary>
/// 所有原生工具的根契约。实现类必须在 CoreJsonContext 中注册参数类型。
/// </summary>
public interface ITool
{
    /// <summary>工具唯一标识符，全局命名空间内不可重复。</summary>
    string Name { get; }

    /// <summary>人类可读的工具功能描述，供 LLM 工具选择阶段使用。</summary>
    string Description { get; }

    /// <summary>
    /// 执行工具调用。参数以 JsonElement 传递，由实现方通过
    /// CoreJsonContext 反序列化为强类型对象。
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        JsonElement parameters,
        ToolContext context,
        CancellationToken cancellationToken = default);

    /// <summary>工具输入的 JSON Schema 定义，编译期通过源生成器生成。</summary>
    JsonElement InputSchema { get; }
}

/// <summary>工具执行结果的统一包装类型。</summary>
public sealed record ToolResult(
    bool Success,
    string? Output = null,
    string? Error = null,
    Dictionary<string, JsonElement>? Artifacts = null);
```

`ITool` 接口遵循被动语义——工具本身不维护状态，不直接访问会话存储，所有副作用通过 `ToolContext` 中注入的有限能力集完成。这种设计使工具实例具备无状态（Stateless）属性，可被运行时池化复用，也便于在 PEV 治理模式下对调用过程进行拦截和审计。[^1^]

#### 3.2.2 MAF Workflows：声明式工作流引擎

MAF（Microsoft Agent Framework，微软代理框架）Workflows 是 OpenClaw.NET 可选编排后端中的核心实现，通过 `OpenClaw.MicrosoftAgentFrameworkAdapter` 项目与网关集成[^9^][^10^]。MAF 提供基于有向图（Directed Graph）的声明式工作流定义能力，支持顺序执行、并发扇出/扇入（Fan-Out/Fan-In）、分支条件跳转以及人机协同的 `RequestPort` 审批门控[^11^]。

MAF 的调度引擎采用 Superstep（超步）模型执行图遍历：在每一超步中，所有可并行节点被同时调度，待全部完成后聚合结果并推进至下一超步。该模型天然契合分布式持久化场景——当工作流通过 `maf-durable-http` 后端托管于 Azure Functions 时，每个超步的执行状态自动持久化，支持跨故障恢复与长时间运行（Long-Running）作业[^9^][^11^]。

OpenClaw.NET 网关与 MAF 后端之间通过标准化 HTTP 契约通信：网关向 `/api/workflows/{workflowName}/run` 投递启动请求，轮询 `/api/workflows/{workflowName}/status/{runId}` 获取执行状态，在审批门控处通过 `/respond/{runId}` 接收人工决策[^9^]。这种解耦设计使 Durable Task 与 Azure Functions 依赖项停留在后端宿主中，网关本身保持轻量且无状态。

#### 3.2.3 PEV 治理模式：Plan-Execute-Verify 三层安全策略

PEV（Plan-Execute-Verify，计划-执行-验证）是 OpenClaw.NET 针对高风险操作提供的可选治理模式，默认关闭且不改变正常对话行为[^12^]。当 MetaSkill 触发敏感操作（如文件写入、Shell 执行、浏览器自动化等）时，PEV 模式介入执行流：首先根据工具治理描述符进行风险分级并生成 Harness Contract（治理契约）；随后仅在审批门控通过后允许实际执行，审批可通过 `/admin` 管理界面由操作员签名确认；最后运行内置验证器检查结果一致性，生成包含工具结果、审批证据和人工审查记录的 Evidence Bundle（证据包），标记运行状态为已验证、失败、拒绝或升级[^12^][^13^]。

Evidence Bundle 采用被动（Passive）设计——仅当代码或操作员显式创建时才生成，不拦截也不修改默认聊天行为[^13^]。这种非侵入式策略确保 PEV 模式可在不影响现有工作流的前提下渐进式部署。

#### 3.2.4 TokenJuice 压缩引擎：结构化工具输出压缩

TokenJuice 是 OpenClaw.NET 运行时的结构化压缩子系统，用于解决智能体与大型工具输出（如数据库日志、网页 HTML）交互时的上下文溢出问题。其工作分为拦截（Interception）与提取（Extraction）两个阶段[^1^]。

在拦截阶段，TokenJuice 在工具执行管道中注册中间件，监控输出 Token 量。当输出超过预设阈值（默认为 4{,}096 Token）时，压缩引擎激活，对原始输出执行结构化分析：提取核心状态字段、计算与上一次调用的 Diff（差异）、生成摘要并将精简后的表示写入系统上下文。原始完整输出作为证据归档于本地 SQLite 存储，可供后续审计回溯。该机制使 LLM 上下文窗口始终保留最关键的状态增量，而非被冗长的原始日志占据，在多工具链式调用场景中维持稳定的上下文使用模式。[^1^]

下表汇总了四大核心组件的关键特性与定位差异：

| 组件 | 类型定位 | 主要职责 | 默认启用 | 状态模型 |
|---|---|---|---|---|
| ITool 接口 | 运行时契约 | 定义原子工具的执行语义与序列化边界 | 是 | 无状态 |
| MAF Workflows | 可选编排后端 | 声明式图计算、Superstep 调度、持久化工作流 | 否（需配置） | 有状态持久化 |
| PEV 治理模式 | 可选安全策略 | 高风险操作的计划-审批-验证闭环 | 否（需显式开启） | 被动生成证据 |
| TokenJuice 压缩 | 运行时中间件 | 拦截超大输出，提取核心状态与 Diff | 是（阈值触发） | 有状态（跨调用累积） |

上表展示了各组件在运行时中的差异化角色。ITool 与 TokenJuice 处于默认激活路径，构成工具执行的基础管道；MAF Workflows 与 PEV 模式则属于可选能力，仅在特定配置下加载。这种分层设计使核心运行时保持最小且稳定，复杂的企业级功能通过显式组合引入，符合架构边界纪律中对可选扩展的隔离要求。[^1^][^14^]

### 3.3 技术栈与项目结构

#### 3.3.1 运行时依赖表

OpenClaw.NET 的技术栈围绕 .NET 10 与 NativeAOT 构建，关键运行时依赖如下表所示。

| 依赖项 | 版本要求 | 职责说明 | NativeAOT 兼容性 |
|---|---|---|---|
| .NET SDK | 10.0 | 基础运行时与编译工具链；提供 NativeAOT 发布能力 | 原生支持 |
| Microsoft.AgentFramework | 1.0+ | 声明式工作流图计算引擎、Superstep 调度、Agent 抽象 | AOT 友好 |
| Azure.Identity | 1.12+ | Azure OpenAI 与 Azure Functions 的身份验证凭证流 | AOT 友好 |
| System.Text.Json | 10.0 | JSON 序列化；通过 JsonSerializerContext 源生成器规避反射 | 源生成器路径完全兼容 |
| SQLitePCLRaw | 2.1+ | SQLite 引擎封装；FTS5 全文检索与 sqlite-vec 向量扩展 | AOT 兼容 |
| Microsoft.AspNetCore.SignalR | 10.0 | 网关 WebSocket 实时通信；流式工具输出与对话推送 | AOT 兼容 |
| Blazor WASM | 10.0 | 管理仪表板（Dashboard）前端；网关内嵌托管 | WASM 独立运行时 |
| ONNX Runtime | 1.19+ | 可选：本地动态路由模型推理（`OpenClaw.Routing.Onnx`） | AOT 兼容，限于可选边界 |

上表中，NativeAOT 兼容性列标注了各依赖在 AOT 发布路径下的状态。框架的核心设计决策在于将 ONNX 等重型依赖隔离于 `OpenClaw.Routing.Onnx` 可选项目中，不进入 `OpenClaw.Core` 核心路径，从而确保核心运行时保持严格的 AOT 兼容性[^14^]。Microsoft.AgentFramework 同样以适配器模式存在，网关仅在配置 `Runtime.Orchestrator=maf` 时才加载对应程序集，实现了功能可扩展性与核心稳定性的分离。[^9^][^10^]

#### 3.3.2 源码目录组织与文件存放规范

OpenClaw.NET 采用按功能边界划分的多项目结构，源代码根目录 `src/` 下的组织遵循核心-网关-适配器-扩展的四层边界纪律[^14^]：

```
src/
├── OpenClaw.Core/                          # 核心运行时契约
│   ├── Agent/                              # 智能体循环与会话抽象
│   ├── Memory/                             # SQLite + FTS5 记忆存储
│   ├── Tools/                              # ITool 契约与工具执行管道
│   ├── Serialization/                      # CoreJsonContext 源生成器
│   └── Safety/                             # 诊断原语与安全基元
├── OpenClaw.Agent/                         # 运行时实现
│   ├── Runtime/                            # ReAct 循环、流式处理
│   └── Tools/Native/                       # 48 个原生工具实现
├── OpenClaw.Gateway/                       # 网关宿主
│   ├── Endpoints/                          # OpenAI 兼容 API、MCP
│   ├── Hub/                                # SignalR 实时通信
│   └── Admin/                              # /admin 管理界面路由
├── OpenClaw.MicrosoftAgentFrameworkAdapter/  # MAF 工作流适配器
│   ├── Workflows/                          # 图定义与 Superstep 调度
│   └── Durable/                            # maf-durable-http 客户端
├── OpenClaw.SkillKit/                      # 本地技能编写、验证与打包
├── OpenClaw.SkillKit.Abstractions/         # 技能契约抽象
├── OpenClaw.Channels/                      # 9 个通道适配器
├── OpenClaw.Cli/                           # 命令行界面
├── OpenClaw.Dashboard/                     # Blazor WASM 仪表板
├── OpenClaw.Protocols.Browser/             # 浏览器自动化（可选）
├── OpenClaw.Protocols.Mqtt/                # MQTT 协议桥（可选）
├── OpenClaw.Plugins.MemPalace/             # 记忆插件（可选）
├── OpenClaw.Routing.Onnx/                  # ONNX 动态路由（可选边界）
└── OpenClaw.Tests/                         # 集成与单元测试
```

上述目录结构中，`OpenClaw.Core` 拥有最严格的稳定性要求，仅包含运行时契约、最小行为实现与 NativeAOT 兼容的原语，不承载产品级工作流引擎或供应商专属集成[^14^]。所有可选功能（浏览器自动化、MQTT、ONNX 路由、MAF 编排）均以独立项目存在，由 `OpenClaw.Gateway` 在启动时根据配置选择性组合。当某一扩展在不支持的运行时模式中被请求时，系统执行快速失败（Fail-Fast）并给出清晰诊断信息，而非静默降级或部分加载[^14^]。


## 4. 技术规格对齐与组件映射

第 2 章确立了 MetaSkills 协议的抽象规范——`SKILL.md` 文件结构、五种步骤类型、Final Text Modes 与四层记忆模型；第 3 章描绘了 OpenClaw.NET 的实现底座——`ITool` 接口、MAF Workflows、PEV 治理与 TokenJuice 压缩。本章将这两个层面精确对齐，建立从协议定义到运行实现的完整映射链条。

### 4.1 核心组件映射总表

MetaSkills 协议的七个核心概念领域在 OpenClaw.NET 中均有对应的物理实现组件。下表以"协议抽象→运行实现→转换机制"三列结构呈现完整映射关系。

| 序号 | MetaSkills 协议抽象（OpenSquilla） | OpenClaw.NET 运行实现 | 技术转换与执行保障机制 |
|:---:|:---|:---|:---|
| 1 | 原子技能层（Atomic Skills） | `ITool` 接口与 `OpenClaw.Agent.Tools` | Python 动态脚本→继承 `ITool` 的强类型 C# 类，`CoreJsonContext` 编译期注册[^1^][^2^] |
| 2 | 编排解析器（Composition Parsing） | MAF 声明式工作流（MAF Workflows） | YAML 经 `WorkflowTemplateCompiler` 编译为 MAF 原生图结构，触发器自动映射为图入口节点[^3^][^4^] |
| 3 | 步骤调度器（Step Scheduling） | MAF 调度引擎与 `maf-durable-http` 后端 | Superstep 模型调度节点，HTTP 持久化后端维持长作业状态与断点恢复[^4^][^5^] |
| 4 | 干预与暂停（Pause/Resume Flow） | PEV 模式与被动证据包（Passive Evidence Bundle） | 敏感操作→Harness Contract→执行流挂起→`/admin` 操作员签名审核→恢复[^6^][^7^] |
| 5 | 工具压缩（Tool Compression） | TokenJuice 结构化压缩引擎 | 运行期拦截超大输出（>4{,}096 Token），提取核心状态与 Diff，原始证据归档本地 SQLite[^1^] |
| 6 | 自主元技能创造器（meta-skill-creator） | `openclaw skill` CLI 与 `SkillKit` 库 | 提取高频工具协同轨迹，生成 AOT 兼容 YAML 及 C# 验证类，Harness 回归测试后发布[^8^] |
| 7 | 梦境巩固（Dream Mode） | 本地双层记忆归档与 SQLite 向量化提炼 | 每 24h 分析 `memory/YYYY-MM-DD.md`，高频模式→`MEMORY/` 常青知识并更新向量索引[^9^][^10^] |

上表七对映射覆盖 MetaSkills 从原子执行到自主演化的完整功能频谱。第 1-3 对构成基础执行管道：`ITool` 提供强类型契约，MAF 图结构承载编排语义，Superstep 模型提供确定性执行时序。第 4-5 对属于治理与优化：PEV 将安全语义转化为三层沙箱策略，TokenJuice 将上下文管理转化为结构化压缩管线。第 6-7 对对应自主演化：`SkillKit` 将改进闭环转化为编译-测试-发布流水线，梦境巩固将记忆生命周期转化为定时知识提炼任务。

### 4.2 协议语义层映射

#### 4.2.1 SKILL.md 文件结构映射

`SKILL.md` 三段式结构在 OpenClaw.NET 中被映射为三类运行时资产。`metadata` 中的 `name`、`description`、`version` 直接转换为 `MetaSkillDocument` 类的同名属性，经 `CoreJsonContext` 序列化后进入工作流注册表[^2^]。`triggers` 中的意图模板被编译为确定性有限自动机（Deterministic Finite Automaton, DFA），运行时通过 $O(n)$ 字符串扫描完成意图识别，替代嵌入模型的语义相似度计算[^3^]。`composition.steps` 被翻译为 MAF 工作流图的节点与邻接关系。

以下代码示例展示 `triggers` 从 YAML 声明到 C# 编译产物的映射：

```yaml
# SKILL.md —— triggers 段落（协议层声明）
kind: meta
name: meta-competitive-intel
triggers:
  - "Analyze {{company}}'s latest competitive moves"
  - "Competitive intel brief on {{company}}"
```

```csharp
// CompetitiveIntelTriggers.g.cs —— 源生成器编译产出（运行层实现）
namespace OpenClaw.MicrosoftAgentFrameworkAdapter.Triggers;

[GeneratedCode("WorkflowTemplateCompiler", "1.0.0")]
public sealed partial class CompetitiveIntelTriggerDfa : ITriggerMatcher
{
    private static readonly StateNode[] _stateTable = new[]
    {
        new StateNode(0, "Analyze", 1, MatchKind.Literal),
        new StateNode(1, null, 2, MatchKind.Capture, slotName: "company"),
        new StateNode(2, "'s latest competitive moves", 3, MatchKind.Literal),
    };

    public TriggerMatch? TryMatch(ReadOnlySpan<char> userMessage)
    {
        var cursor = new DfaCursor(_stateTable);
        for (int i = 0; i < userMessage.Length; i++)
        {
            if (cursor.Advance(userMessage[i], out var capture))
                return new TriggerMatch(
                    SkillName: "meta-competitive-intel",
                    CapturedSlots: cursor.Slots);
        }
        return null;
    }
}
```

`triggers` 中的自然语言模板在编译期被解析为 DFA 状态转移表，`{{company}}` 占位符映射为捕获槽位（Capture Slot），匹配结果以字典形式返回供后续步骤引用[^3^]。这种编译期转换策略降低了意图识别的计算开销与延迟波动，同时满足 NativeAOT 无反射约束。

#### 4.2.2 五步骤类型映射

`composition.steps` 的五种步骤类型在 MAF 工作流图中被映射为不同类型的动作节点。

| 协议步骤类型 | MAF 动作类型 | 执行语义映射 | 关键配置参数 |
|:---|:---|:---|:---|
| `agent` | `CallAgent` | 委托子代理执行指定 Skill，支持上下文传递与结果回传 | `skill`: 目标 Skill 名称；`input`: 参数绑定 |
| `llm_chat` | LLM 生成步骤 | 触发有界 LLM 文本生成，不进入 ReAct 工具循环 | `system_prompt`: 系统提示词；`max_tokens`: 上限 |
| `llm_classify` | 分类路由 + `route` 条件跳转 | 模型从封闭集合返回分类值，结果驱动条件分支 | `options`: 分类集合；`route`: 值→步骤 ID 映射 |
| `user_input` | PEV 暂停 + `RequestPort` 门控 | 暂停执行流，SignalR 推送确认请求，等待人工响应 | `prompt`: 提示文本；`timeout`: 超时秒数 |
| `tool_call` | `CallTool` | 确定性工具调用，直接映射到 `ITool` 接口实现 | `tool`: 工具名称；`input`: 参数绑定 |

`llm_classify` 的映射中，`WorkflowTemplateCompiler` 在编译期为每个 `options` 值生成一条从分类节点指向目标步骤的条件边，边标签即为分类值，运行时图遍历器据此选择唯一路径[^4^]。`user_input` 充分利用 MAF 的 `RequestPort` 原语——在 Superstep 中创建等待门控，当前超步暂停，待外部信号通过 `/respond/{runId}` 到达后触发下一超步[^5^]，直接复用 MAF 原生能力实现"暂停-恢复"语义。

#### 4.2.3 Final Text Modes 映射

`final_text.mode` 三种模式对应 MAF 编排器的不同结果汇总策略。`auto` 模式：编排器在终止节点收集所有前置输出，按优先级排序拼接为最终答案[^3^]。`raw` 模式：编排器回溯执行历史，返回最后一个非 `intermediate` 步骤的原始输出。`step:<step_id>` 模式：编排器按步骤 ID 检索输出记录，若步骤不存在或未执行则抛出 `StepNotFoundException`[^4^]。

### 4.3 记忆架构映射

四层记忆模型在 OpenClaw.NET 中有明确的物理存储映射。

| 记忆层级 | OpenSquilla 概念 | OpenClaw.NET 物理映射 | 存储介质与保留策略 |
|:---|:---|:---|:---|
| 工作记忆 | 活动上下文窗口 | 内存步骤变量与 `ToolContext` 状态字典 | 进程内存，会话结束释放；受 LLM 上下文窗口约束 |
| 情境记忆 | 时间组织的经验片段 | `memory/YYYY-MM-DD.md` 追加写文件 | 本地文件系统，保留 2 天后进入冷凝阶段[^9^] |
| 语义记忆 | 高频模式常青知识 | `MEMORY/*.md` 主题知识文件 | 本地文件 + SQLite FTS5 索引，`FractalMemoryMcp` 接口检索[^10^] |
| 原始基底 | 完整轨迹与审计证据 | SQLite BLOB + `/admin/trajectory/export` | SQLite 本地数据库，长期保留，离线回溯[^9^] |

四层记忆间数据流动遵循生命周期协议：工作记忆在会话结束时将关键交互追加写入情境记忆；超过 2 天保留期的情境记忆进入冷凝流程，TokenJuice 提取状态增量，本地模型分析高频关联模式，提炼为语义记忆的常青规则；语义记忆通过 `sqlite-vec` 建立向量索引，支持 FTS5 全文匹配与向量相似度的混合检索[^10^]。原始基底独立记录完整轨迹，仅供审计与合规回溯。

### 4.4 安全治理映射

#### 4.4.1 risk/capabilities 元数据→PEV 三层沙箱策略

`SKILL.md` 通过 `risk` 与 `capabilities` 声明安全治理需求。`metadata.opensquilla.risk` 三个枚举值直接映射到 PEV 沙箱级别：`low`→Standard（只读直接执行）；`medium`→Strict（隔离执行，Bubblewrap/Seatbelt 限制系统调用）；`high`→Locked（完全挂起，`/admin` 操作员签名审核）[^6^][^7^]。`capabilities` 声明工具权限清单，编译期执行权限校验，未授权工具在加载阶段即被拒绝[^6^]。

以下代码展示从 `composition.steps` 到 MAF Superstep 调度图的构建，包含安全策略注入：

```csharp
// SuperstepGraphBuilder.cs —— 协议步骤到 MAF 调度图的转换
public sealed class SuperstepGraphBuilder
{
    public WorkflowGraph Build(MetaSkillDocument skillDoc)
    {
        var graph = new WorkflowGraph();
        StepNode? previousNode = null;

        foreach (var stepDef in skillDoc.Composition.Steps)
        {
            var currentNode = stepDef.Type switch
            {
                "agent"        => CreateCallAgentNode(stepDef),
                "llm_chat"     => CreateLlmGenerationNode(stepDef),
                "llm_classify" => CreateClassifyRouteNode(stepDef),
                "user_input"   => CreatePevPauseNode(stepDef),
                "tool_call"    => CreateCallToolNode(stepDef),
                _ => throw new NotSupportedException($"未支持步骤类型: {stepDef.Type}")
            };

            // risk 元数据编译期注入沙箱策略
            var sandboxLevel = skillDoc.Metadata.Risk?.ToLowerInvariant() switch
            {
                "low"    => SandboxLevel.Standard,
                "medium" => SandboxLevel.Strict,
                "high"   => SandboxLevel.Locked,
                _        => SandboxLevel.Standard
            };
            currentNode.SandboxPolicy = new SandboxPolicy(sandboxLevel);

            graph.AddNode(currentNode);
            if (previousNode != null && stepDef.Type != "llm_classify")
                graph.AddEdge(previousNode.Id, currentNode.Id);
            previousNode = currentNode;
        }
        return graph;
    }

    private StepNode CreatePevPauseNode(StepDefinition stepDef)
    {
        return new StepNode(stepDef.Id, new RequestPortAction
        {
            Prompt = stepDef.Prompt,
            TimeoutSeconds = stepDef.Timeout ?? 300,
            OnResumeEvidenceBundle = new EvidenceBundleDescriptor
            {
                CollectVariableSnapshot = true,
                CollectPreviousStepLogs = 2,
                CollectProposedWritePayload = true
            }
        });
    }
}
```

上述代码揭示三项映射决策：步骤类型映射通过 `switch` 表达式集中处理，新增类型仅需扩展分支；安全策略在编译期注入为 `SandboxPolicy` 实例，运行期直接读取执行；`user_input` 映射为 `RequestPort` 并自动附加被动证据包收集描述符，确保人工审批触发时生成完整审计证据[^7^]。

#### 4.4.2 模板安全→XML 转义渲染管线映射

`SKILL.md` 模板语法强制安全过滤器链（`xml_escape` → `truncate` → `default`），在 OpenClaw.NET 中被映射为 `TemplateSecurityPipe` 三阶段处理流。`xml_escape` 在变量绑定前对 `inputs` 字段执行 XML 实体转义，防止提示注入攻击载荷。`truncate` 在输出阶段限制内容长度为 2{,}000 字符，超出部分截断并标记 `...[truncated]`，防止上下文溢出[^3^]。Jinja 沙箱通过白名单仅允许 `xml_escape`、`truncate` 与 `default` 三种过滤器，禁用 `eval`、`safe` 等危险过滤器。`WorkflowTemplateCompiler` 在编译期执行静态分析，未经 `xml_escape` 处理的 `inputs` 引用被视为不合规，编译直接失败，将模板安全风险从运行期前移至编译期捕获[^3^]。


## 5. NativeAOT 约束下的工作流设计

NativeAOT（Ahead-of-Time）编译模式为 OpenClaw.NET 的 MetaSkills 工作流系统引入了根本性的架构约束：运行时禁止反射（reflection）与动态代码生成（dynamic code generation），所有类型信息必须在编译期完整解析并固化到本机映像中[^1^]。这一约束与 MetaSkills 工作流固有的动态特性——运行时解析 SKILL.md 声明、动态注册步骤、按需加载技能——形成直接矛盾。本章从强类型契约定义、静态化编译策略、按需过滤机制与 AOT 友好依赖注入四个维度，阐述该系统在 NativeAOT 约束下的工程实现方案。

### 5.1 强类型 MetaSkill 运行时契约

MetaSkills 工作流的声明式描述（declarative description）通过 SKILL.md 中的 YAML 结构化数据表达，但 NativeAOT 禁止运行时通过反射动态绑定 JSON 字段到对象属性。解决方案采用 System.Text.Json 的源生成器（source generator）模式，在编译期生成序列化元数据，彻底消除运行时的反射依赖[^2^]。

#### 5.1.1 元数据契约的 C# 类定义

`MetaSkillDocument` 作为技能描述文档的根实体，承载技能标识、描述与步骤集合；`MetaStepDefinition` 描述单个步骤的工具调用契约，包括工具名称、参数映射、输出变量与失败处理策略。所有属性均使用 `JsonPropertyName` 特性显式声明 JSON 字段映射关系，确保源生成器能够识别完整的序列化图（serialization graph）。

以下代码展示了完整的强类型契约定义与配套的 `JsonSerializerContext`：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenClaw.Net.Runtime.MetaSkills;

// 技能文档根实体：对应 SKILL.md 的序列化表示
public sealed class MetaSkillDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("steps")]
    public List<MetaStepDefinition> Steps { get; set; } = new();

    [JsonPropertyName("contextSchema")]
    public Dictionary<string, string> ContextSchema { get; set; } = new();
}

// 步骤定义：描述原子工具调用的完整契约
public sealed class MetaStepDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("toolName")]
    public string ToolName { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object> Arguments { get; set; } = new();

    [JsonPropertyName("outputVariable")]
    public string? OutputVariable { get; set; }

    [JsonPropertyName("onFailure")]
    public string OnFailure { get; set; } = "fail";
}

// 源生成器上下文：编译期生成序列化元数据，零反射依赖
[JsonSerializable(typeof(MetaSkillDocument))]
[JsonSerializable(typeof(MetaStepDefinition))]
[JsonSerializable(typeof(List<MetaStepDefinition>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, object>))]
internal partial class MetaSkillJsonContext : JsonSerializerContext
{
}
```

`MetaSkillJsonContext` 继承自 `JsonSerializerContext`，通过 `[JsonSerializable]` 属性显式注册所有参与序列化的类型。NativeAOT 编译器在构建过程中根据这些属性生成优化的序列化代码，运行时直接调用生成的序列化器，无需通过 `Type` 对象进行字段探测[^3^]。`Dictionary<string, object>` 的注册尤为关键，因为步骤参数采用开放式键值结构，源生成器必须知晓该类型的具体泛型参数才能生成合法的序列化路径。

#### 5.1.2 步骤失败处理策略枚举

步骤级别的故障处理策略通过 `OnFailure` 属性的三个离散值控制。各策略的语义差异与适用场景如表 1 所示。

**表 1 步骤失败处理策略对比**

| 策略值 | 触发行为 | 状态一致性保障 | 适用场景 |
|:---|:---|:---|:---|
| `fail` | 立即终止工作流执行，向上层抛出 StepExecutionException | 依赖事务边界前的持久化快照 | 非关键路径中的致命错误 |
| `rollback` | 调用已执行步骤的补偿操作（compensation），按逆序回滚 | 要求每个步骤实现 ICompensatable 接口 | 金融交易、数据变更等需原子性保证的场景 |
| `retry` | 基于指数退避策略重试，最大重试次数由工作级配置决定 | 目标服务需具备幂等性（idempotency） | 网络抖动、临时性服务不可用 |

三种策略的划分遵循"故障类型决定处理方式"的设计原则。`retry` 策略适用于瞬态故障（transient failures），其退避间隔计算公式为 $\text{delay} = \text{baseDelay} \times 2^{\text{attempt}}$，其中 `baseDelay` 默认 1{,}000 ms，上限由 `MaxRetryDelay` 约束[^4^]。`rollback` 策略要求步骤声明补偿操作，这在 NativeAOT 环境下通过编译期生成补偿委托（compensation delegate）实现，避免运行时委托动态编译。当 `OnFailure` 设置为 `fail` 时，工作流引擎在异常抛出前记录执行上下文快照（execution context snapshot），支持上层编排器进行事后审计。

### 5.2 动态工作流的静态化策略

#### 5.2.1 核心矛盾与解决方案

MetaSkills 的 Python 参考实现在运行时通过 `exec()` 动态注册技能、通过元类（metaclass）动态生成工作流类。NativeAOT 环境下，这些机制全部不可用：运行时既不能生成新的 IL（Intermediate Language），也不能通过 `Activator.CreateInstance` 的非泛型重载创建类型实例[^5^]。核心矛盾可归纳为：声明式工作流定义（声明即数据）与 AOT 静态类型系统（编译即固化）之间的语义鸿沟。

解决方案采用"编译期解析、运行时绑定"的两阶段模型：声明式 SKILL.md 在编译阶段被 `WorkflowTemplateCompiler` 解析为静态 C# 类型树，运行时仅执行参数绑定与状态传递。这一策略将动态性从运行时迁移到构建时（build-time），以编译期代码生成换取运行时的零反射执行。

#### 5.2.2 WorkflowTemplateCompiler 职责与实现

`WorkflowTemplateCompiler` 承担 SKILL.md → C# 源码 → 工作流程序集 的完整转换管线。其处理流程如下：

```csharp
// WorkflowTemplateCompiler：编译期将 SKILL.md 转换为静态工作流类型
public sealed class WorkflowTemplateCompiler
{
    private readonly SkillParser _parser;
    private readonly CSharpCodeGenerator _codeGen;
    private readonly AotAssemblyBuilder _assemblyBuilder;

    // 编译管线入口：SKILL.md → 程序集
    public CompiledWorkflow Compile(SkillTemplate template)
    {
        // 阶段 1：解析 YAML/JSON 技能描述为中间表示
        var skillDoc = _parser.Parse(template.Content);

        // 阶段 2：为每个步骤生成强类型调用节点
        var stepNodes = skillDoc.Steps.Select(step =>
            new StepCallNode(
                stepId: step.Id,
                toolType: ResolveToolType(step.ToolName),  // 编译期解析工具类型
                argumentBindings: CompileArgumentBindings(step.Arguments),
                failureStrategy: ParseFailureStrategy(step.OnFailure),
                outputVariable: step.OutputVariable
            )
        ).ToList();

        // 阶段 3：生成实现了 IWorkflow 的密封类
        var sourceCode = _codeGen.GenerateWorkflowClass(
            className: $"Workflow_{skillDoc.Id}",
            stepNodes: stepNodes,
            contextSchema: skillDoc.ContextSchema
        );

        // 阶段 4：编译为 AOT 友好的程序集
        return _assemblyBuilder.Compile(sourceCode);
    }

    // 参数绑定编译：将 Dictionary<string, object> 转为强类型属性赋值
    private ArgumentBinding[] CompileArgumentBindings(
        Dictionary<string, object> arguments)
    {
        return arguments.Select(kv => new ArgumentBinding(
            parameterName: kv.Key,
            valueExpression: kv.Value is string s && s.StartsWith("$")
                ? Expression.ContextReference(s[1..])   // 变量引用：$varName
                : Expression.Constant(kv.Value)          // 字面量常量
        )).ToArray();
    }
}
```

编译器的关键设计在于 `CompileArgumentBindings` 方法：它在编译期区分变量引用（以 `$` 前缀标识，运行时从执行上下文中解析）与常量值（直接嵌入生成的 IL），从而将原本运行时的字典查找转换为静态属性访问。生成的 `Workflow_{skillDoc.Id}` 类为 `sealed` 类型，确保 NativeAOT 编译器能够进行完整的去虚拟化（devirtualization）优化，消除接口调用的间接开销[^6^]。

### 5.3 按需技能过滤与冷沉降机制

#### 5.3.1 语义相似度计算公式

MetaSkills 系统维护一个本地策展知识库（Curated Knowledge），存储可用技能的元数据。为避免每次推理将全部技能描述注入提示上下文，系统采用混合相似度评分机制，仅在评分超过阈值时加载技能。评分公式融合向量语义相似度与 FTS5（Full-Text Search 5）文本匹配的双重信号：

$$
\text{Score} = \alpha \cdot \text{Sim}_{\text{vector}}(\text{Task}, \text{Skill}) + (1 - \alpha) \cdot \text{Sim}_{\text{FTS5}}(\text{Task}, \text{Skill})
$$

其中 $\alpha \in [0.4, 0.6]$ 为向量相似度的权重系数。向量特征通过本地嵌入引擎提取——优先使用 Gemma 4 的嵌入层，在资源受限设备上回退至 ONNX Runtime 本地 CPU 推理[^7^]。FTS5 分量提供关键词精确匹配的补充信号，对于包含专有名词与技术术语的查询尤为有效。以下代码展示了混合评分的 C# 实现：

```csharp
// HybridScoringEngine：向量相似度 + FTS5 文本匹配的混合评分
public sealed class HybridScoringEngine
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly IFts5SearchService _ftsSearch;
    private readonly float _alpha;  // 向量权重，典型值 0.5

    public HybridScoringEngine(
        IEmbeddingProvider embeddingProvider,
        IFts5SearchService ftsSearch,
        float alpha = 0.5f)
    {
        _alpha = Math.Clamp(alpha, 0.4f, 0.6f);
        _embeddingProvider = embeddingProvider;
        _ftsSearch = ftsSearch;
    }

    // 计算任务描述与候选技能的混合相似度得分
    public async Task<float> ComputeScoreAsync(
        string taskDescription,
        SkillCandidate candidate)
    {
        // 向量语义相似度：余弦距离 [-1, 1] 归一化到 [0, 1]
        var taskEmbedding = await _embeddingProvider.GenerateAsync(taskDescription);
        var skillEmbedding = candidate.EmbeddingVector;
        float vectorSim = CosineSimilarity(taskEmbedding, skillEmbedding);
        vectorSim = (vectorSim + 1.0f) / 2.0f;  // 归一化

        // FTS5 文本匹配得分：[0, 1] 区间
        float ftsScore = await _ftsSearch.MatchScoreAsync(
            query: taskDescription,
            skillId: candidate.Id);

        // 加权融合
        return _alpha * vectorSim + (1.0f - _alpha) * ftsScore;
    }

    // 余弦相似度计算：SIM(A,B) = (A·B) / (||A|| × ||B||)
    private static float CosineSimilarity(ReadOnlyMemory<float> a, ReadOnlyMemory<float> b)
    {
        var spanA = a.Span;
        var spanB = b.Span;
        float dot = 0.0f, normA = 0.0f, normB = 0.0f;
        for (int i = 0; i < spanA.Length; i++)
        {
            dot += spanA[i] * spanB[i];
            normA += spanA[i] * spanA[i];
            normB += spanB[i] * spanB[i];
        }
        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-8f);
    }
}
```

实现中 `CosineSimilarity` 方法采用手工展开的循环，避免 LINQ 在 NativeAOT 中可能引入的泛型类型膨胀（generic type bloat）。`ReadOnlyMemory<float>` 作为向量载体，确保嵌入数据可以安全地来自托管堆或 NativeAOT 允许的固定内存区域。

#### 5.3.2 阈值决策与安全防护

推荐阈值 $\theta = 0.65$，该值通过离线评测集校准：在包含 2{,}400 条任务-技能对的评估集上，$\theta = 0.65$ 达到精确率 0.91 与召回率 0.84 的平衡点[^8^]。仅当 $\text{Score} \geq \theta$ 时，系统从知识库加载技能描述并注入提示上下文。所有注入内容经过 XML 转义包裹（XML escaping wrapper），以防御提示注入攻击（prompt injection）：`<skill_desc>{EscapedXml(skill.Description)}</skill_desc>`。未通过阈值的技能保持冷沉降（cold sedimentation）状态——其元数据保留在本地存储但不进入 LLM 上下文窗口。

#### 5.3.3 冷沉降的 TTFT 优化效果

冷沉降机制对首个令牌生成时间（Time-to-First-Token, TTFT）的优化效果显著。在包含 150+ 技能的完整知识库测试中，全量加载技能描述导致 TTFT 达 2.8 s；启用按需过滤后 TTFT 降至 1.2 s 以下，降幅超过 50%[^9^]。优化增益来源于上下文窗口的压缩：过滤后平均注入令牌数从 3{,}200 降至 890，减少了注意力计算与 KV 缓存占用。

### 5.4 AOT 友好的依赖注入与生命周期

#### 5.4.1 编译期 DI 容器注册

OpenClaw.NET 的依赖注入（Dependency Injection, DI）系统在 NativeAOT 下采用编译期注册模式。服务集合的构建从运行时的反射扫描迁移到显式的源代码配置。`IServiceCollection` 的注册在应用程序入口点静态完成，所有服务类型在编译期已知。

**表 2 NativeAOT 约束下的动态特性迁移方案**

| 动态特性 | 传统 .NET 实现 | NativeAOT 迁移方案 | 技术约束 |
|:---|:---|:---|:---|
| JSON 序列化 | `JsonSerializer.Serialize<T>` 运行时泛型实例化 | `JsonSerializerContext` 源生成器 + `[JsonSerializable]` 显式注册 | 所有类型必须在编译期声明 |
| 类型实例化 | `Activator.CreateInstance(Type)` | 编译期生成工厂委托 `Func<TService>` | 禁止运行时类型参数 |
| 依赖注入注册 | 运行时程序集扫描 `Assembly.GetTypes()` | 显式源代码注册 + 源生成器辅助 | 需 `DynamicallyAccessedMembers` 注解 |
| 工作流动态生成 | `exec()` / 元类动态创建类 | `WorkflowTemplateCompiler` 编译期代码生成 | 构建管线中增加编译阶段 |
| 嵌入向量计算 | Python 运行时调用 | Gemma 4 嵌入层 / ONNX Runtime 本地推理 | 模型权重需 AOT 兼容格式 |

表 2 所列五项迁移覆盖了 MetaSkills 系统中受 NativeAOT 影响最深的动态特性。其中最核心的转变是将"运行时发现"替换为"编译期声明"——这一范式转变要求开发者在编写技能定义时即明确所有可能参与序列化与依赖解析的类型，牺牲了部分灵活性以换取启动性能与发布包体积的优化。实测数据显示，采用完整 AOT 编译后，OpenClaw.NET 的启动时间从 JIT 模式的 1.2 s 降至 80 ms，单个文件发布体积从 85 MB 压缩至 42 MB[^10^]。

生命周期管理遵循 Scoped 与 Singleton 的两级划分。`HybridScoringEngine`、`WorkflowTemplateCompiler` 等工作流级服务注册为 Scoped，随单次推理请求创建与释放；`IEmbeddingProvider`、知识库索引等状态less 服务注册为 Singleton，在应用生命周期内复用。NativeAOT 编译器对 Scoped 服务的工厂代码进行内联优化，消除常规 DI 容器中的字典查找开销，服务解析降至纳秒级延迟[^11^]。


## 6. MAF 工作流集成与运行期编排

MetaSkills 协议通过 Microsoft Agents Framework（MAF）的声明式工作流引擎（Declarative Workflow Engine）获得运行期能力[^1^]。本章阐述从 YAML 工作流定义到 .NET 运行期执行的完整编排链，覆盖变量传递、流式执行事件处理以及基于 `maf-durable-http` 的持久化断点恢复机制。

### 6.1 声明式 MetaSkill YAML 工作流定义

#### 6.1.1 完整 MAF Workflow 示例

MAF 工作流采用声明式 YAML 格式定义，以 `kind: Workflow` 为根节点，`actions` 数组承载顺序执行步骤[^1^]。以下示例完整呈现前文所述五步工作流——从日志提取到通知发送——在 MAF 语义下的编排表达：

```yaml
# yaml-language-server: $schema=https://learn.microsoft.com/agent-framework/declarative-workflow.json
kind: Workflow
trigger:
  kind: OnConversationStart
id: daily_error_diagnostic_flow
actions:
  - kind: CallTool
    id: extract_logs
    displayName: 获取本地崩溃日志
    tool: OpenClaw.Tools.FileRead
    arguments:
      path: "logs/production_crash.log"
    output: Local.RawLogs
  - kind: CallTool
    id: compress_logs
    displayName: 执行TokenJuice结构化日志投影
    tool: OpenClaw.Tools.TokenJuiceCompressor
    arguments:
      rawData: =Local.RawLogs
      compressionRatio: 0.8
    output: Local.CompressedContext
  - kind: CallAgent
    id: diagnostic_agent
    displayName: 诊断分析助理
    agent: SeniorDiagnosticAgent
    prompt: "请根据压缩后的上下文，概括出前三类崩溃原因："
    context: =Local.CompressedContext
    output: Local.DiagnosticReport
  - kind: SetVariable
    id: apply_pev_gate
    displayName: 触发安全批准凭证
    variable: Local.ApprovalRequired
    value: true
  - kind: CallTool
    id: send_notification
    displayName: 推送消息至Telegram
    tool: OpenClaw.Tools.TelegramSendMessage
    arguments:
      channelId: "-100123456789"
      message: =Local.DiagnosticReport
```

该工作流以 `OnConversationStart` 触发器（Trigger）启动，`actions` 数组中的五个元素依次对应第 4 章确立的组件映射关系：日志读取与 TokenJuice 压缩均映射为 `CallTool` 动作；`CallAgent` 启动诊断子代理；`SetVariable` 设置审批标记，模拟代理执行验证（Proxy Execution Verification, PEV）门的运行期状态切换；最终 `CallTool` 将诊断报告推送至外部消息渠道[^1^]。

#### 6.1.2 变量传递语法与步骤间数据流

MAF 声明式工作流通过前缀表达式 `=Local.VariableName` 实现步骤间数据传递[^1^]。该语法遵循以下解析规则：等号前缀指示右侧标识符为变量引用而非字面量；`Local` 为默认作用域限定符，表征当前工作流执行上下文中的可变状态存储区；工作流引擎在执行动作前解析参数值，将变量绑定（Variable Binding）为具体字符串或结构化对象。

在上述 YAML 示例中，数据流沿以下路径传递：`extract_logs` 步骤将原始日志写入 `Local.RawLogs`；`compress_logs` 步骤通过 `=Local.RawLogs` 读取该变量并输出至 `Local.CompressedContext`；`diagnostic_agent` 步骤接收压缩后的上下文，生成 `Local.DiagnosticReport`；`send_notification` 最终引用 `=Local.DiagnosticReport` 完成报告投递。整个传递链路构成了从原始输入到最终输出的线性数据变换管道（Linear Data Transformation Pipeline），每个中间变量既是前一步骤的输出契约，也是后续步骤的输入依赖[^1^]。

下表汇总五步类型到 MAF 动作类型的完整映射关系，涵盖第 4 章已确定的组件对应关系及本章补充的运行期语义细节：

| MetaSkill 步骤类型 | MAF Action 类型 | 语义角色 | 输入/输出契约 |
|:---|:---|:---|:---|
| `agent` | `CallAgent` | 子代理委托 | 输入 prompt + context，输出结构化报告 |
| `llm_chat` | `CallLLM` | LLM 文本生成 | 输入对话上下文，输出生成文本 |
| `llm_classify` | `Classify` | 条件路由 | 输入文本 + 候选类别，输出路由分支 |
| `user_input` | `SetVariable` / 挂起 | PEV 审批门 | 触发审批状态，等待外部干预 |
| `tool_call` | `CallTool` | 工具函数调用 | 输入结构化参数，输出工具结果 |

上表中 `user_input` 的映射需特别说明：MetaSkill 协议中的 `user_input` 步骤在语义上表示用户审批介入点，但在 MAF 工作流中，该步骤被拆分为两个运行期行为——通过 `SetVariable` 设置审批标记变量（如 `Local.ApprovalRequired = true`），同时触发工作流引擎的挂起机制，等待外部事件（如用户通过 PEV 门户确认）恢复执行[^1^]。这种拆分使得审批状态的持久化与审批动作的触发解耦，支持工作流在挂起数小时后仍能从断点精确恢复。

### 6.2 .NET 运行期编译与持久化

#### 6.2.1 MetaSkillExecutionEngine 实现

`MetaSkillExecutionEngine` 是连接 MetaSkill YAML 定义与 MAF 运行期的核心编排器（Orchestrator），其职责涵盖 Azure Agent Provider 初始化、声明式工作流选项配置及 YAML 文件的编译加载[^2^]。以下为完整的引擎实现：

```csharp
using Azure.Identity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Declarative;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenClaw.Net.Runtime.MetaSkills;

namespace OpenClaw.Net.Runtime.Orchestration;

public class MetaSkillExecutionEngine
{
    private readonly AzureAgentProvider _agentProvider;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public MetaSkillExecutionEngine(
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        var foundryEndpoint = _configuration["OpenClaw:FoundryEndpoint"]
            ?? "https://your-project.api.azureml.ms";
        _agentProvider = new AzureAgentProvider(
            new Uri(foundryEndpoint), new DefaultAzureCredential());
    }

    public async Task RunMetaSkillAsync(string yamlFilePath, string inputPrompt)
    {
        var logger = _loggerFactory.CreateLogger<MetaSkillExecutionEngine>();
        var options = new DeclarativeWorkflowOptions(_agentProvider)
        {
            Configuration = _configuration,
            LoggerFactory = _loggerFactory,
            ConversationId = Guid.NewGuid().ToString()
        };
        logger.LogInformation("正在从 YAML 编译声明式 MetaSkill: {Path}", yamlFilePath);
        var workflow = DeclarativeWorkflowBuilder.BuildFromFile(yamlFilePath, options);
        StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, inputPrompt);
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is ExecutorStartedEvent started)
            {
                logger.LogInformation("[超步开始] 节点 ID: {Id}, 友好名称: {Name}",
                    started.ExecutorId, started.DisplayName);
            }
            else if (evt is ExecutorCompletedEvent completed)
            {
                logger.LogInformation("[超步完成] 节点 ID: {Id}, 输出变量已捕获",
                    completed.ExecutorId);
            }
        }
    }
}
```

引擎的构造函数注入 `IConfiguration` 与 `ILoggerFactory`，并基于配置键 `OpenClaw:FoundryEndpoint` 初始化 `AzureAgentProvider`，采用 `DefaultAzureCredential` 实现无凭证硬编码的 Azure 身份验证[^2^]。`RunMetaSkillAsync` 方法的核心编排链包含四个阶段：`DeclarativeWorkflowOptions` 实例化与 `ConversationId` 分配——该标识符作为持久化会话键贯穿工作流全生命周期；`BuildFromFile` 将 YAML 文件编译为内存中的工作流对象图（Object Graph）；`InProcessExecution.RunStreamingAsync` 启动流式执行；`WatchStreamAsync` 提供异步事件消费接口，使调用方能够实时感知每一步骤的执行状态变迁。

#### 6.2.2 StreamingRun 流式执行与 WorkflowEvent 异步处理

`StreamingRun` 是 MAF 工作流引擎的流式执行抽象，通过 `WatchStreamAsync` 方法暴露 `IAsyncEnumerable<WorkflowEvent>` 接口，支持调用方以异步拉取模式消费运行期事件[^2^]。工作流引擎在执行每个动作（Action）前后分别发射 `ExecutorStartedEvent` 与 `ExecutorCompletedEvent`，事件负载包含以下关键字段：`ExecutorId`（动作节点的唯一标识，对应 YAML 中的 `id` 字段）、`DisplayName`（人类可读的动作描述）、输出变量引用（`ExecutorCompletedEvent` 中隐含，引擎自动将动作结果写入 `output` 声明的变量名）。

上述 `MetaSkillExecutionEngine` 中的事件处理逻辑仅记录日志，但在生产场景中，该事件流可扩展至多种运维用途：将 `ExecutorStartedEvent` 推送至分布式追踪系统（如 OpenTelemetry）以构建工作流执行轨迹；基于 `ExecutorCompletedEvent` 触发下游通知或度量指标采集；通过会话 ID 关联外部审批系统，实现 PEV 门控的闭环管理。事件流的异步拉取模型确保事件消费不会阻塞工作流主执行线程，从而在 I/O 密集型步骤（如 LLM 调用或外部 API 等待）期间保持高吞吐量[^2^]。

#### 6.2.3 maf-durable-http 持久化后端

对于需要跨越长时间窗口（可能因等待用户干预而挂起数小时）的多步骤工作流，`maf-durable-http` 后端提供执行状态的断点持久化（Checkpoint Persistence）与冷唤醒（Cold Wakeup）能力[^3^]。该后端的核心机制包含三个层面：

在初始化层，`DeclarativeWorkflowOptions.ConversationId` 被分配为全局唯一会话标识符，该 ID 作为持久化存储中的主键，确保同一工作流的不同执行实例可被唯一区分[^3^]。在执行层，工作流引擎在每个动作完成后将完整执行状态——包括局部变量字典、程序计数器（Program Counter）式的当前步骤索引、以及挂起原因——序列化并写入 HTTP 持久化端点。在恢复层，当外部事件（如 PEV 审批通过）到达时，`maf-durable-http` 后端根据会话 ID 检索最近断点，重建执行上下文，并从挂起步骤的后续动作继续执行，无需重新运行已完成的步骤。

该持久化机制与 `InProcessExecution` 的流式执行模型互补：流式执行适用于短时同步场景，提供低延迟的事件反馈；持久化后端则面向长时异步场景，保证工作流在进程重启或容器漂移后的执行连续性[^3^]。两者的选择取决于工作流中是否包含 `user_input` 类型的 PEV 审批步骤——若无审批门控，纯内存执行已足够；若存在可能挂起数小时的审批等待，则必须启用 `maf-durable-http` 后端。

### 6.3 五步类型的运行期行为

#### 6.3.1 各步骤类型运行期执行模型对比

MetaSkill 协议定义的五类步骤在运行期呈现不同的执行特征，涉及资源消耗模式、阻塞行为、错误处理策略及典型耗时分布。下表从多维度对比各步骤的运行期行为：

| 步骤类型 | 执行模型 | 阻塞模式 | 主要资源消耗 | 错误处理 | 典型耗时 |
|:---|:---|:---|:---|:---|:---|
| `agent` (CallAgent) | 子代理委托执行 | 异步非阻塞 | LLM Token 消耗 | 重试 3 次后升级 | 2–10 秒 |
| `llm_chat` (CallLLM) | 单次 LLM 文本生成 | 流式输出 | TPU/GPU 算力 | 指数退避重试 | 1–5 秒 |
| `llm_classify` | 分类路由决策 | 同步快速返回 | 低 Token 消耗 | 默认类别回退 | 200–800 毫秒 |
| `user_input` | PEV 审批门挂起 | 持久化阻塞 | 存储 I/O | 超时拒绝 | 数分钟至数小时 |
| `tool_call` (CallTool) | 外部工具函数调用 | I/O 等待 | 网络/文件资源 | 幂等重试 | 100 毫秒–3 秒 |


## 7. 混合记忆管理与认知机制

OpenClaw.NET 为 MetaSkills 工作流提供的记忆支撑并非单一存储介质上的简单键值缓存，而是一套分层的认知记忆架构（Cognitive Memory Architecture）。该架构借鉴了认知心理学中关于人类记忆的多存储模型[^1^]，将跨会话上下文连续性分解为四个物理层级，每一层在容量、访问延迟和保留策略上形成互补梯度。四层记忆之间的数据流动由热力衰减算法（Thermal Decay Algorithm）驱动，并通过 TokenJuice 压缩引擎实现梯度化存储效率优化。

### 7.1 四层记忆物理架构

#### 7.1.1 四层记忆对比规格

OpenClaw.NET 的 Memory Stack 在物理上映射为四个层级，其设计 rationale 在于：不同生命周期阶段的上下文数据对检索延迟、存储容量和持久性的需求存在数量级差异。下表给出各层的完整规格对比。

| 层级 | 认知映射 | 物理存储介质 | 典型容量 | 保留策略 | 访问方式 | 平均访问延迟 |
|:---|:---|:---|:---|:---|:---|:---|
| Working（工作记忆） | 活动上下文窗口 | 托管堆内存（Managed Heap） | 8{,}192–32{,}768 Token | 单会话级，GC 回收 | 内存指针直接访问 | < 0.1 ms |
| Episodic（情境记忆） | 近日轨迹记录 | 本地追加写 Markdown 文件（memory/YYYY-MM-DD.md） | 约 500 KB/天 | 滚动 2 天窗口 | 文件系统顺序读 | 1–5 ms |
| Semantic（语义记忆） | 常青规则与知识库 | MEMORY/ 目录下 Markdown 文件集 | 5–50 MB/项目 | 长期保留，人工审核删除 | Fractal Memory MCP 接口 | 10–50 ms |
| Raw（原始审计基底） | 离线轨迹归档 | SQLite BLOB 字段 + 文件系统 | 无硬性上限 | 审计周期（默认 90 天）后冷沉降 | 只读导出（/admin/trajectory/export） | > 100 ms |

从架构视角分析，四层规格表揭示了显式的权衡设计（explicit trade-off design）。Working 层以托管堆为载体，其 < 0.1 ms 的访问延迟保障了 MetaSkill 执行链中临时变量和原始工具数据的实时可用性，但代价是严格的容量约束——8{,}192–32{,}768 Token 的上限对应于当前主流大语言模型上下文窗口的 1%–4%，这要求引擎必须在单会话结束后迅速将价值数据向下层迁移[^2^]。Episodic 层采用追加写模式的每日物理文件策略，其核心优势在于：顺序写操作避免了随机 I/O 的开销，同时 2 天的滚动窗口为工作流提供了"近日可追溯性"，使跨会话但不跨周的上下文连续性成为可能。Semantic 层通过 Fractal Memory MCP（Model Context Protocol）接口暴露，将提炼后的规则与项目决策外化为可独立维护的知识资产。Raw 层作为冷归档基底，以 SQLite BLOB 形式存储结构化 JSON 轨迹，仅通过管理端点提供离线回溯能力，这一设计满足合规审计需求的同时避免了热路径上的性能损耗。

#### 7.1.2 层间数据流动机制

四层记忆之间的数据流转遵循三条主线管道。

第一条管道为 **Working → Episodic 追加写**。在单个 MetaSkill 工作流会话的生命周期中，工作记忆持续积累执行上下文。当会话正常终止或超时（默认 30 分钟无活动）时，引擎将 Working 层中的结构化上下文序列化为 Markdown 格式，以追加写（append-only）模式写入当前日期的 `memory/YYYY-MM-DD.md` 文件。该操作具有原子性：写入失败时上下文保留于 Working 层，下次会话启动时重试[^3^]。追加写模式的选择基于日志结构合并（Log-Structured Merge）思想——物理文件的仅追加特性消除了更新操作所需的锁竞争，在多智能体并发场景下保障了写入一致性。

第二条管道为 **Episodic → Semantic 规则提炼**。该过程由后台的常青提炼器（Evergreen Distiller）周期性触发，默认间隔为 24 小时。提炼器扫描过去 48 小时内累积的情境记忆片段，通过语义聚类识别重复出现的决策模式、工具调用序列和项目约定，将其压缩为结构化的常青规则条目并写入 `MEMORY/*.md`。这一提炼过程实质上是将时序性的 episodic 数据转换为命题性的 semantic 知识，使跨项目、跨周乃至跨月的上下文连续性成为可能[^4^]。

第三条管道为 **全层 → Raw 审计归档**。Raw 层作为审计基底，接收来自所有上层的历史数据快照。当情境记忆超过 2 天滚动窗口、或语义记忆中的规则条目被更新替换时，原始数据以 JSON 格式序列化并写入 SQLite BLOB 表。该归档操作在后台以低优先级线程执行，不阻塞热路径上的记忆检索。

### 7.2 热力衰减与 Evergreen 机制

#### 7.2.1 热力算法数学定义

OpenClaw.NET 采用热力值（Heat Value）作为记忆片段的活性度量指标，其数学定义为指数衰减函数与 evergreen 偏移量之和：

$$H = e^{-\lambda \cdot \Delta t} + E$$

其中，$\lambda$ 为时间衰减常数（系统默认取值为 $1/7\ \text{day}^{-1}$，对应半衰期约 4.85 天），$\Delta t$ 表示自该记忆片段上一次被检索调用以来所经历的天数，$E$ 为 evergreen 标记常数。若片段被显式标记为 evergreen，则 $E = 1$，此时无论时间推移如何，$H \geq 1$ 恒成立，该记忆永远处于最高激活态。普通记忆（$E = 0$）的热力值随时间指数级向零收敛，其衰减速率由 $\lambda$ 控制[^5^]。

该公式的 C# 实现如下所示，包含了 evergreen 判断逻辑和阈值决策：

```csharp
/// <summary>
/// 记忆热力计算器：实现指数衰减 + evergreen 偏移模型
/// </summary>
public sealed class HeatCalculator
{
    // 时间衰减常数 λ，默认 1/7 day^-1
    private readonly double _lambda;
    // 冷沉降阈值，低于此值触发归档
    private readonly double _coldThreshold;

    public HeatCalculator(
        double halfLifeDays = 7.0,
        double coldThreshold = 0.05)
    {
        _lambda = Math.Log(2) / halfLifeDays;  // λ = ln(2) / t_half
        _coldThreshold = coldThreshold;
    }

    /// <summary>
    /// 计算指定记忆片段的当前热力值
    /// </summary>
    public double ComputeHeat(
        DateTime lastAccessedUtc,
        bool isEvergreen,
        DateTime? nowUtc = null)
    {
        DateTime current = nowUtc ?? DateTime.UtcNow;
        double deltaDays = (current - lastAccessedUtc).TotalDays;

        double decay = Math.Exp(-_lambda * deltaDays);
        double evergreenOffset = isEvergreen ? 1.0 : 0.0;

        return decay + evergreenOffset;
    }

    /// <summary>
    /// 判断记忆片段是否应当执行冷沉降归档
    /// </summary>
    public bool ShouldArchive(double heatValue)
        => heatValue < _coldThreshold;

    /// <summary>
    /// 判断记忆片段是否为高活性（优先检索）
    /// </summary>
    public bool IsHighHeat(double heatValue)
        => heatValue > 0.5;
}
```

上述实现中，`ComputeHeat` 方法严格遵循 $H = e^{-\lambda \cdot \Delta t} + E$ 的数学定义，其中衰减常数 $\lambda$ 通过半衰期换算得到（$\lambda = \ln(2) / t_{1/2}$），确保默认参数下热力值经过 7 天衰减至初始值的 50%。`ShouldArchive` 与 `IsHighHeat` 方法为记忆生命周期管理提供了决策边界。

#### 7.2.2 记忆生命周期管理

热力值驱动的生命周期管理遵循三条操作语义（operational semantics）。**高热力优先检索**：当工作流发起上下文查询时，检索引擎按热力值降序扫描记忆索引，优先返回 $H > 0.5$ 的高活性片段。此策略确保最近被引用或显式标记为 evergreen 的规则最先进入当前会话的上下文窗口。**低热力优先压缩**：对于 $0.05 \leq H \leq 0.5$ 的中低活性记忆，引擎触发 TokenJuice 压缩流程，将其标记为压缩候选并在后台任务中执行分层压缩。**冷沉降归档**：当 $H < 0.05$ 且片段未被标记为 evergreen 时，引擎从 SQLite 向量缓存中移除该片段的索引条目，并将原始数据写入 Raw 层的归档分区。冷沉降操作不可逆，但可通过管理端点 `/admin/trajectory/export` 进行离线回溯[^6^]。

### 7.3 TokenJuice 在记忆压缩中的应用

#### 7.3.1 分层压缩策略

TokenJuice 压缩引擎并非对全部四层记忆施加统一的压缩策略，而是根据各层的访问模式、语义密度和延迟需求实施梯度化压缩。下表展示了分层压缩策略的完整映射。

| 记忆层级 | 压缩级别 | 压缩算法 | 保留内容 | 压缩比 | 延迟影响 |
|:---|:---|:---|:---|:---|:---|
| Working | 不压缩 | 无（透传） | 完整原始 Token 序列 | 1:1 | 零额外延迟 |
| Episodic | 轻度压缩 | 句子级去重 + 停用词裁剪 | 完整语义句子 + 时间戳 + 工具签名 | 约 3:1 | < 2 ms/条 |
| Semantic | 中度压缩 | 摘要生成 + 向量索引 | 结构化规则摘要 + embedding 向量 | 约 10:1 | < 10 ms/条 |
| Raw | 深度压缩 | Brotli 流式压缩 + 仅保留索引 | 归档摘要 + 检索关键字索引 | 约 50:1 | 异步执行 |

分层压缩策略的设计 rationale 在于各层对信息保真度的需求存在本质差异。Working 层承担实时执行职能，任何压缩操作引入的解压延迟都会直接传导至工作流的步骤响应时间，因此该层完全放弃压缩以换取零额外延迟。Episodic 层的轻度压缩聚焦于句子级去重和停用词裁剪——这两个操作均为有损压缩，但保留了完整的语义句子结构和工具调用签名，确保近日轨迹的可读性与可回溯性[^7^]。Semantic 层的中度压缩引入了摘要生成与向量索引的协同机制：原始规则文本经过 TokenJuice 摘要器压缩为结构化摘要，同时 embedding 向量作为语义检索的入口保留在 SQLite 向量表中，实现压缩比约 10:1 的同时维持可检索性。Raw 层的深度压缩采用 Brotli 流式算法，仅保留可供离线检索的关键字索引，50:1 的压缩比使长期审计数据的存储开销降至最低。

#### 7.3.2 IMemoryStore 接口与 SQLite 实现

四层记忆的统一访问由 `IMemoryStore` 接口抽象，其 SQLite 实现骨架如下：

```csharp
/// <summary>
/// 记忆存储抽象：四层记忆的统一访问接口
/// </summary>
public interface IMemoryStore
{
    // 按热力值降序检索指定层级的记忆片段
    IAsyncEnumerable<MemoryFragment> QueryByHeatAsync(
        MemoryLayer layer,
        string[] queryTerms,
        double minHeat = 0.0,
        CancellationToken ct = default);

    // 将记忆片段写入指定层级（含热力值初始化）
    Task WriteAsync(
        MemoryLayer layer,
        MemoryFragment fragment,
        CancellationToken ct = default);

    // 更新指定片段的访问时间与热力值
    Task TouchAsync(
        MemoryLayer layer,
        Guid fragmentId,
        CancellationToken ct = default);

    // 执行冷沉降：将低于阈值的片段迁移至 Raw 层
    Task<long> ColdSettleAsync(
        MemoryLayer sourceLayer,
        double heatThreshold,
        CancellationToken ct = default);
}

/// <summary>
/// SQLite 实现：IMemoryStore 的具体持久化载体
/// </summary>
public sealed class SqliteMemoryStore : IMemoryStore, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly HeatCalculator _heatCalc;
    private readonly TokenJuiceCompressor _compressor;

    public SqliteMemoryStore(
        string connectionString,
        HeatCalculator heatCalculator,
        TokenJuiceCompressor compressor)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        _heatCalc = heatCalculator;
        _compressor = compressor;
    }

    public async IAsyncEnumerable<MemoryFragment> QueryByHeatAsync(
        MemoryLayer layer,
        string[] queryTerms,
        double minHeat = 0.0,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // 联合查询：向量相似度 × 热力值排序
        const string sql = @"
            SELECT id, content, embedding, last_accessed_utc, is_evergreen,
                   (EXP(-@lambda * (julianday('now') - julianday(last_accessed_utc)))
                    + IIF(is_evergreen = 1, 1.0, 0.0)) AS heat
            FROM memory_fragments
            WHERE layer = @layer AND heat >= @minHeat
            ORDER BY heat DESC
            LIMIT 50;";

        using var cmd = new SqliteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("@layer", (int)layer);
        cmd.Parameters.AddWithValue("@minHeat", minHeat);
        cmd.Parameters.AddWithValue("@lambda", _heatCalc.Lambda);

        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            yield return new MemoryFragment
            {
                Id = reader.GetGuid(0),
                Content = reader.GetString(1),
                Embedding = JsonSerializer.Deserialize<float[]>(reader.GetString(2)),
                LastAccessedUtc = reader.GetDateTime(3),
                IsEvergreen = reader.GetBoolean(4),
                HeatValue = reader.GetDouble(5)
            };
        }
    }

    public async Task<long> ColdSettleAsync(
        MemoryLayer sourceLayer,
        double heatThreshold,
        CancellationToken ct = default)
    {
        // 事务内执行：标记冷数据 → Brotli 压缩 → 写入 Raw 分区 → 删除源记录
        using var tx = _connection.BeginTransaction();
        var settled = await ExecuteSettleAsync(sourceLayer, heatThreshold, tx, ct);
        tx.Commit();
        return settled;
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
```

`SqliteMemoryStore` 的实现体现了两个关键设计决策。其一，`QueryByHeatAsync` 方法在 SQL 查询层直接内嵌热力计算公式，利用 SQLite 的 `julianday` 函数实现天数差计算，避免将整表数据载入内存后再排序，这一优化在万级记忆片段规模下可降低 90% 以上的查询延迟。其二，`ColdSettleAsync` 采用原子事务封装"标记-压缩-迁移-删除"四步操作，确保冷沉降过程中不会因并发访问导致数据丢失[^8^]。`TokenJuiceCompressor` 的注入使压缩策略可在运行时根据层级参数动态选择，实现了 7.3.1 节所述的分层压缩映射在代码层面的可配置化。


## 8. PEV 安全治理与合规

PEV（Plan-Execute-Verify，计划-执行-验证）治理模式是 OpenClaw.NET 安全架构的核心支柱，其设计目标在于为 MetaSkill 工作流中的敏感操作提供全链路审计与合规保障[^12^]。与第 4 章所建立的风险元数据到策略级别的映射关系相呼应，PEV 在运行期将 `risk` 字段（low/medium/high）转化为三层动态隔离沙箱的具体执行策略，并通过 `capabilities` 元数据约束工具权限白名单[^13^]。本章从沙箱隔离、拒绝记账、陈旧数据阻断和模板安全加固四个维度，阐述 PEV 如何在不影响正常对话流的前提下实现非侵入式（Non-Intrusive）安全治理。

### 8.1 PEV 三层动态隔离沙箱

#### 8.1.1 三层沙箱规格对比

OpenClaw.NET 的 PEV 沙箱体系划分为 Standard、Strict、Locked 三个递进级别，分别对应只读直接执行、隔离子进程执行和挂起操作员审核三种运行时行为[^12^]。下表从执行环境、系统权限、网络访问、审批门控和典型应用场景五个维度进行规格对比。

| 维度 | Standard（标准策略） | Strict（严格策略） | Locked（锁定策略） |
|---|---|---|---|
| 风险等级映射 | low | medium | high |
| 执行环境 | 主进程内直接执行 | 受限子进程沙箱（Bubblewrap / Seatbelt） | 挂起，等待操作员签名 |
| 文件系统访问 | 只读查询，无写入 | 剥夺 `/home` 及敏感路径访问，仅允许指定目录 | 零访问，直到审核通过 |
| 网络访问 | 受限出站（白名单） | 禁止敏感地址段（169.254.0.0/16、10.0.0.0/8 等） | 完全隔离 |
| 审批门控 | 无 | 执行前自动校验 Harness Contract | 强制人工审批 |
| 典型场景 | 信息查询、只读数据库检索 | 文件转换、外部 API 调用 | Shell 命令执行、浏览器自动化 |

上表展示了三层沙箱在权限维度上的递进收紧关系。Standard 级别保持了最低的运行时开销，适用于无副作用的信息收集类操作；Strict 级别通过操作系统级沙箱技术将子进程与宿主环境隔离，在 macOS 平台采用 Seatbelt 沙箱配置文件，在 Linux 平台采用 Bubblewrap 命名空间隔离，确保即使工具实现存在漏洞也无法越权访问敏感路径[^12^]。Locked 级别则引入了主动中断（Active Interruption）机制——当 MetaSkill 步骤涉及文件写入、系统 Shell 或浏览器自动化等高风险操作时，引擎在执行前挂起工作流并生成 Passive Evidence Bundle（被动证据包），等待操作员通过管理界面审批后方可继续[^13^]。

#### 8.1.2 被动证据包结构

Passive Evidence Bundle 是 PEV 治理模式中的核心审计原语，其设计遵循被动生成（Passive Generation）原则——仅在高风险操作触发审批门控时创建，不拦截也不修改默认工作流行为[^13^]。证据包包含三个结构性组件：步骤执行前的变量快照（Variable Snapshot），用于记录输入参数与上下文状态；前两步的运行日志（Execution Trace），提供操作序列的历史回溯；以及预期工具写入负载（Projected Write Footprint），描述该步骤计划写入的文件路径、网络端点或数据库记录。

以下 C# 代码定义了 Evidence Bundle 的核心记录结构：

```csharp
// EvidenceBundle.cs — PEV 被动证据包的运行期表示
using System.Text.Json.Serialization;

namespace OpenClaw.Core.Safety;

/// <summary>
/// PEV 治理模式下的被动证据包，仅在 Strict/Locked 策略触发时生成。
/// 包含变量快照、执行轨迹与预期写入负载，供操作员审批与事后审计。
/// </summary>
public sealed record EvidenceBundle
{
    /// <summary>证据包唯一标识，采用 ULID 保证时序可排序。</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>证据生成时间戳（UTC），精确到毫秒。</summary>
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>触发本证据包的 MetaSkill 步骤标识。</summary>
    [JsonPropertyName("stepId")]
    public required string StepId { get; init; }

    /// <summary>操作类型：ToolCall、AgentDelegation、FileWrite、ShellExecute 等。</summary>
    [JsonPropertyName("operationType")]
    public required string OperationType { get; init; }

    /// <summary>步骤执行前的完整变量快照（输入参数与上下文状态）。</summary>
    [JsonPropertyName("variableSnapshot")]
    public required Dictionary<string, string> VariableSnapshot { get; init; }

    /// <summary>前两步的执行轨迹摘要，包含步骤 ID、结果状态与时间戳。</summary>
    [JsonPropertyName("executionTrace")]
    public required List<TraceEntry> ExecutionTrace { get; init; }

    /// <summary>预期写入负载：文件路径、网络端点或数据库操作描述。</summary>
    [JsonPropertyName("projectedWrites")]
    public required List<WriteProjection> ProjectedWrites { get; init; }

    /// <summary>风险评估标记：由 Harness Contract 在计划阶段计算得出。</summary>
    [JsonPropertyName("riskMarkers")]
    public required RiskAssessment RiskMarkers { get; init; }

    /// <summary>操作员审批状态：Pending / Approved / Rejected / Escalated。</summary>
    [JsonPropertyName("approvalStatus")]
    public string ApprovalStatus { get; init; } = "Pending";
}

/// <summary>单条执行轨迹记录。</summary>
public sealed record TraceEntry(
    [property: JsonPropertyName("stepId")] string StepId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("summary")] string Summary
);

/// <summary>预期写入操作投影。</summary>
public sealed record WriteProjection(
    [property: JsonPropertyName("targetType")] string TargetType,   // File / Network / Database
    [property: JsonPropertyName("targetPath")] string TargetPath,
    [property: JsonPropertyName("estimatedBytes")] long EstimatedBytes
);

/// <summary>风险评估标记，与 SKILL.md 中 risk 元数据对应。</summary>
public sealed record RiskAssessment(
    [property: JsonPropertyName("level")] string Level,             // low / medium / high
    [property: JsonPropertyName("category")] string Category,       // 风险分类
    [property: JsonPropertyName("denialCount")] int DenialCount     // 连续拒绝计数
);
```

上述定义中，`EvidenceBundle` 类型通过 `CoreJsonContext` 在编译期注册序列化元数据，满足 NativeAOT 的反射替代要求[^2^]。`OperationType` 字段精确标记操作类别，使审计系统能够按类型聚合风险统计；`ProjectedWrites` 列表在计划阶段由 Harness Contract 预先计算，为操作员提供预期副作用的完整视图[^13^]。

#### 8.1.3 SignalR 事件桥接

当 Locked 策略触发时，证据包需要通过实时通道广播至操作员终端。OpenClaw.NET 网关层的 SignalR Hub（`IWorkflowNotificationHub` 接口）负责将 `EvidenceBundle` 实例序列化为 JSON 并通过 WebSocket 推送[^9^]。网关同时提供多通道适配器（Channel Adapter）架构，支持将同一事件并行路由至 Blazor Web UI（`/admin/approvals` 页面）、Telegram Bot 和 Slack Incoming Webhook[^14^]。各适配器接收统一格式的 `WorkflowApprovalEvent` 消息后，按目标平台的 API 规范进行内容转换——Telegram 适配器将证据摘要渲染为 Markdown 格式并附加快捷审批按钮，Slack 适配器则使用 Block Kit 交互组件呈现结构化审批卡片。

### 8.2 拒绝记账与陈旧数据阻断

#### 8.2.1 Denial Ledger：连续安全拦截强制关停

Denial Ledger（拒绝记账本）是 PEV 治理中的安全计数器机制，用于防止对系统策略的暴力绕过尝试。引擎为每个会话维护一个独立计数器，当 MetaSkill 工作流由于安全拦截（Harness Contract 校验失败、沙箱权限越界或操作员明确拒绝）遭到连续阻断时，计数器递增[^12^]。当计数器达到阈值 3 时，引擎执行强制关停（Forced Shutdown）：立即终止当前会话的所有挂起步骤，释放相关资源，并在审计日志中写入 `SessionTerminated` 事件，标记终止原因为 `DenialThresholdExceeded`。该机制有效防御了蛮力攻击（Brute-force Attack）场景——攻击者无法通过构造大量微差异请求试探系统权限边界，因为第三次尝试将永久性关闭会话通道。

#### 8.2.2 Stale Output Protection：物理抹除与侧信道防御

Stale Output Protection（陈旧输出防护）解决被拒绝步骤的中间数据残留问题。当 PEV 审批结果为拒绝（Rejected）时，引擎不仅终止步骤执行，还主动从工作内存（Working Memory）中物理抹除该步骤已产生的所有中间输出——包括局部变量、临时文件句柄和写入缓存区的内容[^13^]。此设计消除了侧信道攻击（Side-channel Attack）的可能性：若中间输出继续驻留于工作内存，后续步骤（即使处于沙箱隔离中）仍可能通过"读取前次输出"的间接方式获取越权数据。Stale Output Protection 确保被拒绝操作的痕迹在内存层面不可恢复，其语义等价于事务回滚（Transaction Rollback），但作用于内存状态而非持久化存储。

### 8.3 模板安全加固

#### 8.3.1 Jinja 模板安全渲染

MetaSkill 的 SKILL.md 文件采用 Jinja2 语法定义步骤模板，用于构造 LLM 提示和工具参数。由于模板中频繁引用 `inputs.user_message` 等用户可控变量，直接渲染存在 Prompt 注入与 XSS（跨站脚本攻击）风险。OpenClaw.NET 的模板安全策略要求所有用户输入变量在渲染前必须通过 `xml_escape` 过滤器进行 XML 实体转义，并通过 `truncate` 过滤器限制长度[^13^]。`xml_escape` 将 `<`、`>`、`&`、`"`、`'` 等字符转义为对应实体，防止用户输入中的标记语言片段破坏模板结构或注入恶意指令；`truncate` 则限制单变量渲染后的最大长度（通常 512 字符），避免超长输入导致上下文窗口溢出或拒绝服务。

#### 8.3.2 安全模板编写正反面示例对照

以下代码示例展示了安全与不安全的模板编写方式在 C# 渲染管线中的差异。

```csharp
// TemplateSecurityExamples.cs — 模板安全渲染的正反面对照
using System.Security;
using System.Text.RegularExpressions;

namespace OpenClaw.Core.Safety;

/// <summary>
/// 模板安全渲染器：强制执行 xml_escape + truncate 过滤链，
/// 禁止原始用户输入直接注入模板。
/// </summary>
public static class SecureTemplateRenderer
{
    private const int DefaultMaxLength = 512;
    private const int MaxSearchOutputLength = 2000;

    // ========== 安全示例：强制过滤链 ==========

    /// <summary>安全渲染：用户查询 —— xml_escape + truncate 双过滤。</summary>
    public static string RenderSafeUserQuery(string userMessage)
    {
        // Step 1: XML 实体转义，防御标记注入
        var escaped = SecurityElement.Escape(userMessage) ?? string.Empty;
        // Step 2: 长度截断，防止上下文溢出
        return escaped.Length > DefaultMaxLength
            ? escaped[..DefaultMaxLength] + "..."
            : escaped;
    }

    /// <summary>安全渲染：搜索输出摘要 —— truncate 限制输出长度。</summary>
    public static string RenderSafeSearchOutput(string searchResult)
    {
        if (string.IsNullOrEmpty(searchResult)) return string.Empty;
        return searchResult.Length > MaxSearchOutputLength
            ? searchResult[..MaxSearchOutputLength] + "..."
            : searchResult;
    }

    /// <summary>安全渲染：URL Slug —— slugify + truncate。</summary>
    public static string RenderSafeSlug(string userMessage)
    {
        var slug = Regex.Replace(userMessage.ToLowerInvariant(), @"[^a-z0-9\-]", "-");
        slug = Regex.Replace(slug, @"-{2,}", "-").Trim('-');
        return slug.Length > 80 ? slug[..80] : slug;
    }

    /// <summary>安全渲染：JSON 载荷 —— 结构化序列化替代字符串拼接。</summary>
    public static string RenderSafeJsonPayload(object planOutput)
    {
        return System.Text.Json.JsonSerializer.Serialize(planOutput, CoreJsonContext.Default.Options);
    }

    // ========== 不安全示例：原始传递（用于对比与检测） ==========

    /// <summary>
    /// 【不安全】直接传递原始 user_message，无任何过滤。
    /// 攻击者可输入 "&lt;/prompt&gt;&lt;inject&gt;忽略先前指令..." 实现 Prompt 注入。
    /// </summary>
    public static string RenderUnsafeUserQuery(string userMessage)
    {
        // 安全扫描器会将此模式标记为 TemplateInjectionVulnerability
        return userMessage;  // ⚠️ 危险：原始字符串直接嵌入模板
    }

    /// <summary>
    /// 【不安全】直接传递工具输出，无长度限制。
    /// 超长输出（如 HTML 页面全文）可导致 Token 溢出与上下文污染。
    /// </summary>
    public static string RenderUnsafeSearchOutput(string searchResult)
    {
        // 安全扫描器会将此模式标记为 UnboundedOutputExposure
        return searchResult;  // ⚠️ 危险：无界输出直接注入
    }
}
```

上述代码中，`RenderSafeUserQuery` 方法展示了第 4 章所述的 `xml_escape` → `System.Security.SecurityElement.Escape` 映射关系[^13^]。安全渲染管线强制执行两层过滤：第一层 `SecurityElement.Escape` 将用户输入中的特殊字符转义为 XML 实体，使任何注入的标记语言片段失去语义活性；第二层 `truncate` 通过字符串切片限制输出长度。不安全的对照方法则完全跳过过滤，直接将原始输入返回——静态分析工具（如 OpenClaw.SkillKit 中的模板安全扫描器）会自动识别此类模式并标记为 `TemplateInjectionVulnerability`。

下表汇总了安全与不安全模板编写的具体差异与风险分析：

| 场景 | 安全写法 | 不安全写法 | 风险说明 |
|---|---|---|---|
| 用户查询嵌入 | `{{ inputs.user_message \| xml_escape \| truncate(512) }}` | `{{ inputs.user_message }}` | Prompt 注入：攻击者可通过 XML/HTML 标记破坏模板结构并注入恶意指令 |
| 搜索输出引用 | `{{ outputs.search \| truncate(2000) }}` | `{{ outputs.search }}` | 上下文溢出：无界输出可占满 LLM 上下文窗口，导致后续步骤信息丢失 |
| URL Slug 生成 | `{{ inputs.user_message \| slugify \| truncate(80) }}` | `{{ inputs.user_message }}` | 路径遍历：未经处理的输入可能包含 `../` 等相对路径片段 |
| JSON 载荷构造 | `{{ outputs.plan \| tojson }}` | `{{ outputs.plan }}` | 结构破坏：对象直接字符串化产生非法 JSON，导致下游解析失败 |

上表中四组对照覆盖了 MetaSkill 模板中最常见的安全风险面。`xml_escape` 过滤在第 4 章被映射为 `System.Security.SecurityElement.Escape`，其转义范围覆盖五类 XML 特殊字符，转义后字符串可安全嵌入 HTML、XML 及类 XML 格式的提示模板中[^13^]。`truncate` 过滤的阈值根据场景差异设置——用户查询限制在 512 字符以保留 LLM 上下文余量，搜索输出放宽至 2000 字符以保留关键信息密度，Slug 限制在 80 字符以符合 URL 路径段规范。`tojson` 过滤则通过结构化序列化完全规避字符串拼接风险，确保对象到 JSON 的转换由经过验证的序列化器完成。


## 9. 梦境巩固与自主演进系统

前述章节阐述了 MetaSkills 的记忆管理层（第7章）与 PEV 安全治理体系（第8章）。本章将在此基础上，论证系统如何在离线时段自主发现高频行为模式，并通过 Harness 回归测试与 HEP（Harness Evolution Proposal，Harness 演化提案）审核流程，将经验固化为可复用的常青技能。这一闭环构成了 MetaSkills 系统自我演化的核心引擎。

### 9.1 梦境巩固机制

#### 9.1.1 BackgroundDreamService 触发条件

梦境巩固（Dream Consolidation）是一种可选的指数衰减记忆优化机制，其命名借鉴了神经科学中睡眠期间记忆重组的概念[^60^]。与 PEV 的实时安全审计不同，梦境模式完全在后台执行，仅在宿主系统处于闲置状态时激活——具体触发条件为凌晨 3:00（本地时间）或检测到用户会话不活跃超过 30 分钟[^73^]。这一时序设计确保了 Dream 任务不会与前台推理竞争 GPU 或 CPU 资源。

#### 9.1.2 三阶段巩固流程

BackgroundDreamService 的执行遵循严格的 DAG（Directed Acyclic Graph，有向无环图）编排：

**阶段一：轨迹提取。** 服务首先调取本地 SQLite 数据库中近 24 小时的情境轨迹（Contextual Traces），包括工具调用序列、LLM 响应元数据和用户反馈信号。所有数据操作均在本地完成，不经过外部网络[^81^]。

**阶段二：行为模式聚类。** 提取的轨迹通过低参数侧车模型（Sidecar Model）进行语义聚类。该阶段识别跨会话的重复模式——例如，若系统在多个独立会话中均检测到"异常告警 → FileRead → Tokenjuice → 诊断 Agent → 写入 README"的工具链序列，则标记该模式为高频候选[^73^]。

**阶段三：候选技能生成。** 当某一模式的跨会话出现频率超过阈值（默认为 3 次/24h）时，系统激活内置的 `meta-skill-creator` 元技能。该组件通过模板填充算法将行为模式合成为符合 MetaSkill Authoring Format（MAF）的声明式 `SKILL.md` 工作流文件[^3^]。生成的提案包含触发器（triggers）、步骤编排（composition.steps）和风险元数据（risk metadata），但初始状态为"待审核"，不会自动注入模型可见的提示上下文。

#### 9.1.3 低参数本地侧车模型

行为模式提取的关键约束是数据隐私。为此，OpenSquilla 采用 GGUF（Georgi Gerganov Universal Format）量化的 Gemma 4 模型作为本地侧车推理引擎[^73^]。该模型以 ONNX 运行时方式加载于宿主设备，参数规模控制在 4B 以下，峰值内存占用约 2.8 GB。由于模型完全运行于本地，情境轨迹无需上传至任何外部端点，满足企业环境下的数据驻留（Data Residency）合规要求[^81^]。

以下代码示例展示了 BackgroundDreamService 的核心调度逻辑：

```csharp
// BackgroundDreamService.cs - 梦境巩固核心调度器
public sealed class BackgroundDreamService : IHostedService
{
    private readonly IDreamTrigger _trigger;
    private readonly ITraceRepository _traceRepo;
    private readonly ILocalSidecarModel _sidecar;  // GGUF Gemma 4
    private readonly IMetaSkillCreator _creator;
    private readonly ILogger<BackgroundDreamService> _logger;

    // 触发条件：凌晨 3 点或会话不活跃 30min
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(30);

    public async Task ExecuteConsolidationAsync(CancellationToken ct)
    {
        _logger.LogInformation("[Dream] 阶段一：轨迹提取");
        var traces = await _traceRepo.GetRecentTracesAsync(
            TimeSpan.FromHours(24), ct);

        _logger.LogInformation("[Dream] 阶段二：行为模式聚类");
        var patterns = await _sidecar.ExtractPatternsAsync(traces, ct);

        var highFreqPatterns = patterns
            .Where(p => p.CrossSessionCount >= 3)  // 阈值：3次/24h
            .ToList();

        _logger.LogInformation("[Dream] 高频模式数量: {Count}",
            highFreqPatterns.Count);

        foreach (var pattern in highFreqPatterns)
        {
            _logger.LogInformation(
                "[Dream] 阶段三：生成候选技能 '{PatternId}'", pattern.Id);
            var proposal = await _creator.SynthesizeAsync(pattern, ct);

            // 写入待审核目录，不自动激活
            await _creator.WriteProposalAsync(
                Path.Combine("~/.openclaw/temp/",
                    $"suggested_metaskill_{pattern.Id}.yaml"),
                proposal, ct);
        }
    }

    private bool IsIdlePeriod() =>
        DateTime.Now.Hour == 3 ||
        _trigger.GetLastActivityDelta() > IdleThreshold;
}
```

上述实现体现了三阶段 DAG 的完整数据流：轨迹提取通过 `ITraceRepository` 接口抽象底层 SQLite 访问；`ILocalSidecarModel` 封装了 GGUF 模型的推理生命周期；`_creator.SynthesizeAsync` 执行 MAF 模板填充。所有候选技能均写入临时目录，等待 Harness 回归测试验证，这一设计确保了"生成"与"激活"的权责分离。

### 9.2 Harness 回归测试与演化提案

#### 9.2.1 Harness Regression Suite

候选技能在提交人工审核前，必须通过 `openclaw harness test` 回归测试套件的验证[^59^]。该命令在只读沙箱（Read-Only Sandbox）环境中回放历史输入流数据（Mock Inputs），执行以下检测项：

```bash
openclaw harness test \
  --candidate-file ~/.openclaw/temp/suggested_metaskill.yaml \
  --mock-inputs ./fixtures/historical_sessions.json \
  --sandbox-mode readonly \
  --check loop_defect,security_violation,output_integrity
```

Harness 测试框架的三项核心检测指标覆盖了技能演化中最常见的故障模式。路由死循环（Looping Defect）指代 `meta_invoke` 调用链形成循环依赖的情况，这在步骤间存在双向 `depends_on` 时尤为危险。安全策略违背检测验证候选技能是否尝试在沙箱约束下执行未授权的工具调用——例如只读模式下发起 `write` 或 `exec` 请求[^77^]。输出结构完整性检测则确保 `composition.steps` 的最终输出符合声明的 `final_text_mode` 契约，防止模板渲染失败导致下游步骤接收到畸形输入。

#### 9.2.2 HEP 生成格式与审核流程

通过 Harness 回归测试的候选技能，系统为其生成一份 HEP（Harness Evolution Proposal）记录。HEP 采用结构化 Markdown 格式，包含提取源轨迹、拟提炼工具链、历史执行次数、回归测试状态和安全级别建议五个核心字段。以下示例展示了 `DailySecurityAudit` 技能的 HEP 记录：

```
# 智能体自主演进提案：DailySecurityAudit [HEP-2026-004]
**提取源轨迹：** 会话 (Session-987a, Session-012c)
**拟提炼工具链：** WebFetch -> LogAnalyzer -> FileWrite
**历史执行次数：** 4次
**回归测试状态：** PASS (12个模拟测试通过，Token消耗平均减少 42.6%)
**申请安全级别：** Strict (需要在沙箱环境下执行文件写入)
```

下表对比了 HEP 记录的完整字段规范：

| 字段 | 类型 | 说明 | 示例值 |
|------|------|------|--------|
| HEP 编号 | string | 唯一标识符，含时间戳序列 | HEP-2026-004 |
| 提取源轨迹 | string[] | 源会话 ID 列表 | [Session-987a, Session-012c] |
| 拟提炼工具链 | string | 高频工具调用序列 | WebFetch -> LogAnalyzer -> FileWrite |
| 历史执行次数 | int | 跨会话出现频率 | 4 |
| 回归测试状态 | enum | PASS / FAIL / PARTIAL | PASS (12/12) |
| Token 效率增益 | float | 相比原工具链的消耗减少比例 | 42.6% |
| 申请安全级别 | enum | Standard / Strict / Locked | Strict |
| 沙箱依赖 | string[] | 必需的环境约束 | [sandbox-readonly, network-isolated] |

该表定义了 HEP 生成与人工审核的完整数据契约。Token 效率增益字段尤其关键——HEP-2026-004 示例中 42.6% 的消耗减少比例来源于技能内联化消除了原工具链中重复的提示开销（Prompt Overhead）和冗余的模型路由决策[^73^]。安全级别建议基于 `metadata.opensquilla.risk` 自动推导：涉及文件写入的技能至少标记为 Strict，需要 shell 执行的标记为 Locked。

以下代码示例展示了 HEP 记录类在 .NET 中的定义与生成逻辑：

```csharp
// HarnessEvolutionProposal.cs - HEP 记录与生成
public sealed record HarnessEvolutionProposal
{
    public required string HepId { get; init; }          // HEP-2026-004
    public required string[] SourceSessions { get; init; }
    public required string ToolChain { get; init; }       // 拟提炼工具链
    public required int ExecutionCount { get; init; }     // 历史执行次数
    public required RegressionStatus Status { get; init; } // PASS/FAIL
    public required double TokenEfficiencyGain { get; init; } // 效率增益 %
    public required SecurityLevel RequestedLevel { get; init; }
    public required string[] SandboxDependencies { get; init; }

    // 从候选技能和回归测试结果生成 HEP
    public static HarnessEvolutionProposal FromCandidate(
        MetaSkillCandidate candidate,
        RegressionResult regression)
    {
        var gain = ComputeTokenEfficiency(candidate, regression);
        var level = InferSecurityLevel(candidate.RiskMetadata);

        return new HarnessEvolutionProposal
        {
            HepId = $"HEP-{DateTime.UtcNow:yyyy}-{NextSequence():D3}",
            SourceSessions = candidate.SourceSessions,
            ToolChain = string.Join(" -> ", candidate.ToolChain),
            ExecutionCount = candidate.CrossSessionCount,
            Status = regression.Status,
            TokenEfficiencyGain = gain,
            RequestedLevel = level,
            SandboxDependencies = level == SecurityLevel.Strict
                ? new[] { "sandbox-readonly", "network-isolated" }
                : Array.Empty<string>()
        };
    }

    private static SecurityLevel InferSecurityLevel(RiskMetadata risk)
        => risk.Capabilities.Contains("shell")
            ? SecurityLevel.Locked
            : risk.Capabilities.Contains("filesystem-write")
                ? SecurityLevel.Strict
                : SecurityLevel.Standard;
}
```

`InferSecurityLevel` 方法体现了安全风险的"fail-closed"设计哲学：当 `capabilities` 包含 `shell` 时自动锁定为 Locked 级别，包含 `filesystem-write` 时为 Strict，其余场景默认为 Standard。这种保守的升级策略与第8章 PEV 安全治理中的三层沙箱策略形成呼应。

#### 9.2.3 操作员批准与发布

HEP 记录呈现在 Blazor Web UI 的 `/admin/skills` 待办页面，或通过 Telegram 通道推送给管理员[^59^]。操作员可审阅完整的信息：提取源轨迹的可信度、回归测试的覆盖率报告、以及安全级别的合规性评估。批准后，YAML 技能文件从临时目录移入常青技能目录（Evergreen Skill Directory）`~/.opensquilla/skills/bundled/`，并触发实时技能加载器的热刷新。拒绝的提案则移入 `rejected/` 子目录，保留审计痕迹以供后续分析[^3^]。

### 9.3 自主演进闭环

#### 9.3.1 自主演进系统数据流

MetaSkills 的自主演进并非全自动发布，而是"机器生成 + 人类把关"的半自动化闭环。下表梳理了 24 小时演进周期的完整阶段映射：

| 阶段 | 触发器 | 执行组件 | 产出物 | 人工介入点 |
|------|--------|----------|--------|------------|
| 周期启动 | 24h 定时器 / 系统闲置 | BackgroundDreamService | — | 无 |
| 轨迹提取 | 阶段一完成信号 | ITraceRepository (SQLite) | 原始轨迹集 | 无 |
| 模式聚类 | 阶段二完成信号 | GGUF Gemma 4 侧车模型 | 行为模式列表 | 无 |
| 候选生成 | 高频模式检测 | meta-skill-creator | `suggested_*.yaml` | 无 |
| 编译校验 | YAML 文件写入 | `openclaw skill` CLI | 语法/结构验证报告 | 无 |
| 回归测试 | 编译通过信号 | `openclaw harness test` | 测试通过/失败结果 | 无 |
| HEP 提案 | 回归测试通过 | HarnessEvolutionProposal | HEP Markdown 记录 | 无 |
| 操作员审批 | HEP 进入待办队列 | Blazor /admin/skills UI | 批准/拒绝决策 | **有** |
| 技能发布 | 审批通过 | Skill Loader | 常青技能目录更新 | **有** |

该表明确了人机权责边界：从周期启动到 HEP 提案生成的七个阶段完全自动化，操作员仅需在两个标注点介入——审批决策和最终发布确认。这一设计的 rationale 源于 AutoSkill 论文中的并发技能演化架构：将延迟敏感的服务路径与离线学习路径分离，确保技能演化不会阻塞用户可见的推理延迟[^70^]。同时，人工审批作为最后一道防线，防止了 "Reward Hacking"（奖励篡改）类风险——即系统可能因过度优化 Token 效率而生成看似合理但功能不完整的技能。


## 10. 工程落地路线图

前九章从协议解析、组件映射、类型系统设计、工作流编排、记忆架构、被动治理到梦境演化，逐层构建了 MetaSkills 在 .NET 生态中的完整技术图景。本章将上述技术方案转化为三阶段工程计划，每个阶段设定明确交付物、验收标准与前置依赖，形成可执行的落地路线图。

### 10.1 里程碑一：强类型契约与 NativeAOT 并网

第一里程碑的核心目标是在编译期建立不可变契约，消除运行时反射带来的不确定性与 NativeAOT（Ahead-of-Time）兼容风险。

交付物包含三项。其一，TokenJuiceCompressor 作为原生 ITool 实现注册至 OpenClaw 引擎，负责拦截超过 2{,}048 字节的工具返回载荷，提取关键差异信息（diffs）并执行语义压缩，以降低 Token 消耗。其二，CoreJsonContext 源生成序列化上下文，基于 System.Text.Json 的源生成器（Source Generator）模式，在编译期.emit 类型特定的序列化逻辑，消除运行时反射开销。其三，在 Windows 与 Linux 双平台执行 AOT 扫描测试，确保 JSON 元数据解析与工具链调度完全无 Warning 通过。

此阶段的工程约束直接来源于 OpenClaw.NET 的 NativeAOT 兼容性规范：禁止使用 System.Reflection.Emit 与动态加载，所有序列化路径必须通过 CoreJsonContext 显式声明 [^1^]。前一章节的强类型类定义（MetaSkillDocument、MetaStepDefinition）在此阶段获得运行时载体，完成从协议规范到编译产物的闭环。

### 10.2 里程碑二：MAF Workflows 与 PEV 被动治理

第二里程碑将运行时调度逻辑从单一工具调用升级为图结构工作流编排，并引入被动证据治理（PEV）机制。

交付物涵盖四个组件。第一，YAML 工作流加载程序，将 SKILL.md 中声明的 5 种 step 类型映射为 Microsoft Agent Framework（MAF）原生声明式工作流，通过 WorkflowBuilder 构建有向执行图 [^2^]。第二，PEV 模式拦截层，在工具调度主循环中插入三阶段沙箱（Standard/Strict/Locked）判定逻辑，当触发 Locked 操作时生成被动证据包并挂起会话状态。第三，SignalR 事件总线，负责将 PEV 拦截事件、工作流状态转换广播至订阅端。第四，Blazor WASM 审核前端，提供人机回环（Human-in-the-loop）审核界面与单步执行控制面板。

MAF 的 Durable Task Scheduler 为此阶段提供关键基础设施，支持 checkpointing 与分布式执行，使长时间运行的工作流能够跨进程恢复 [^3^]。PEV 的 Denial Ledger 与 Stale Output Protection 在此阶段与 MAF 的异常传播机制对接，确保治理动作不破坏工作流的事务语义。

### 10.3 里程碑三：记忆系统与梦境后台服务

第三里程碑实现系统的持续演化能力，通过离线分析自动合成改进提案。

交付物包括四项。其一，MetaSkillDreamService 托管服务，作为后台任务在系统闲置时激活。其二，SQLite FTS5 虚拟表与本地向量存储的重建逻辑，实现全文检索与语义向量的混合查询能力 [^4^]。其三，轨迹聚类算法，基于执行历史的热度衰减模型 $H = e^{-\lambda \cdot \Delta t} + E$ 对工具调用轨迹进行分组，识别高频模式。其四，HEP（Harness Enhancement Proposal）自动提案生成，将聚类结果转化为工作流 YAML 草案，并通过 CLI 执行 `openclaw harness test` 回归分析验证语义等价性。

此阶段依赖前两阶段积累的运行时数据：工作流执行日志提供聚类算法的输入，TokenJuiceCompressor 的压缩记录构成效率优化的基准线，PEV 拦截事件则作为安全性约束反馈至 HEP 生成器。

### 10.4 里程碑验收与风险缓解

#### 10.4.1 三里程碑验收标准与跨阶段依赖关系

下表汇总三个里程碑的关键工程参数。

| 阶段 | 时间窗口 | 核心交付物 | 前置依赖 | 验收标准 | 主要风险 |
|:---:|:---|:---|:---|:---|:---|
| 里程碑一 | 第 1–3 周 | TokenJuiceCompressor ITool；CoreJsonContext；AOT 零 Warning 编译 | Ch2–Ch5 类型设计完成 | Windows/Linux 双平台 `dotnet publish -r` 零 Warning；工具压缩率 ≥ 40% | NativeAOT 反射限制超出预期；第三方库 trim 兼容性 |
| 里程碑二 | 第 4–7 周 | YAML 加载程序；PEV 拦截层；SignalR 总线；Blazor WASM 前端 | 里程碑一完成；Ch6–Ch8 工作流与治理设计完成 | MAF 工作流端到端通过；PEV Locked 拦截延迟 < 50 ms；SignalR 事件投递率 99.9% | MAF DurableTask 预览版 API 变更；LLM 结构化输出可靠性波动 |
| 里程碑三 | 第 8–12 周 | MetaSkillDreamService；SQLite FTS5 向量重建；轨迹聚类；HEP 提案 | 里程碑二完成；Ch7–Ch9 记忆与梦境设计完成 | 聚类 Silhouette 系数 > 0.6；HEP 回归测试通过率 ≥ 90%；后台服务内存占用 < 256 MB | SQLite FTS5 平台差异（Windows 缺失模块）；聚类算法冷启动数据不足 |

里程碑之间存在严格的顺序依赖：里程碑二的 YAML 工作流加载依赖里程碑一完成的 CoreJsonContext 进行 AOT 安全的配置反序列化；里程碑三的 HEP 生成器依赖里程碑二的 PEV 拦截记录作为安全约束输入。时间窗口总计 12 周，各阶段预留约 20% 的缓冲以应对集成风险。

#### 10.4.2 跨章节依赖关系矩阵

下表将前九章的技术主题映射至三个工程阶段，单元格标注各阶段需实现的交付物归属。

| 章节主题 | 里程碑一 | 里程碑二 | 里程碑三 |
|:---|:---|:---|:---|
| Ch2 MetaSkills 协议解析 | SKILL.md 契约接口冻结 | YAML frontmatter 加载器实现 | 梦境服务协议扫描输入 |
| Ch3 OpenClaw.NET 运行时 | TokenJuiceCompressor 注册；NativeAOT 编译链 | MAF 依赖集成；运行时调度升级 | 后台服务宿主接口 |
| Ch4 组件映射 | 映射表编码为 C# 项目结构 | 动态映射解析逻辑 | 映射关系演化（HEP 目标） |
| Ch5 强类型类定义 | MetaSkillDocument/MetaStepDefinition 定版 | 工作流模板编译器集成 | — |
| Ch6 MAF 工作流编排 | — | WorkflowBuilder 图构建；maf-durable-http 持久化 | 轨迹数据输出至工作流分析 |
| Ch7 四层记忆架构 | — | — | Working/Episodic/Semantic/Raw 全层实现；热度衰减算法部署 |
| Ch8 PEV 被动治理 | — | 三阶段沙箱拦截；Denial Ledger；Stale Output Protection | 拦截日志作为 HEP 安全约束 |
| Ch9 梦境与 HEP | — | — | BackgroundDreamService；Harness Regression Suite；HEP 自动提案 |

从矩阵可观察到一个清晰的层级递进模式：里程碑一侧重**静态契约**（协议解析、类型定义、编译期验证），里程碑二侧重**动态编排**（工作流图执行、运行时治理、人机交互），里程碑三侧重**离线演化**（记忆分析、自动提案、闭环改进）。三章以上的主题在前两个阶段集中交付，而 Ch7–Ch9 的内容完全落在里程碑三，反映出记忆与梦境系统的实现必须建立在稳定的运行态与足够的历史数据基础之上。

主要风险集中在三个维度。技术层面，NativeAOT 的反射限制可能导致部分第三方库需要替换或 fork；MAF DurableTask 尚处预览阶段，API 存在变更可能。数据层面，LLM 结构化输出的可靠性波动会直接影响 YAML 工作流生成的正确率，需引入 JSON Schema 校验与重试机制作为补偿。性能层面，SQLite FTS5 在 Windows 平台的模块缺失问题已有先例 [^4^]，需在构建阶段检测 FTS5 可用性并自动回退至向量搜索。上述风险均需在对应里程碑启动前完成技术预研，并在验收标准中设置可量化的熔断阈值。
