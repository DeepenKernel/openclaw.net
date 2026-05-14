# A2A 协议级流式响应实现

> **Spec 版本：** 1.0
> **日期：** 2026-05-14
> **状态：** 已批准实施

## 目标

在 OpenClaw Agent Card 中声明 A2A 协议级流式能力，并完整实现 `message:stream` 端点，使 A2A 客户端能够通过 SSE 接收流式增量，而无需等待单一聚合响应。

---

## 架构

OpenClaw 继续使用 A2A SDK 的端点映射（`MapA2AHttpJson` / `MapA2AJsonRpc`），无需手动编写新的 HTTP 路由。A2A SDK 根据 `RequestContext` 中的 `StreamingResponse` 标志，将 `POST /a2a/message:send` 和 `POST /a2a/message:stream` 分发到注册的 `IAgentHandler`。

修改两个文件：

| 文件 | 职责 |
|------|------|
| `src/OpenClaw.MicrosoftAgentFrameworkAdapter/A2A/OpenClawAgentCardFactory.cs` | 当 `MafOptions.EnableStreaming == true` 时设置 `Capabilities.Streaming = true` |
| `src/OpenClaw.MicrosoftAgentFrameworkAdapter/A2A/OpenClawA2AAgentHandler.cs` | 根据 `context.StreamingResponse` 分支处理——非流式保持当前缓冲消息行为，流式通过 `TaskUpdater` 发出 A2A 任务事件 |

现有的 `OpenClawA2AExecutionBridge`（`src/OpenClaw.Gateway/A2A/OpenClawA2AExecutionBridge.cs`）已经在流式传输 OpenClaw 运行时增量；只需要修改 A2A 事件发送层。

---

## Agent Card 变更

`OpenClawAgentCardFactory.Create` 根据 `MafOptions.EnableStreaming` 设置 `Capabilities.Streaming`：

```csharp
Capabilities = new AgentCapabilities
{
    Streaming = _options.EnableStreaming,
    PushNotifications = false
}
```

当 `EnableStreaming` 为 true（默认值）时，解析 agent card 的客户端会看到 `streaming: true`，从而知道 `message:stream` 端点可用。

---

## Handler：非流式路径（不变）

`POST /a2a/message:send` 继续使用现有代码路径：

1. 调用 `_bridge.ExecuteStreamingAsync`，将文本增量累积到 `StringBuilder responseText`。
2. 如果 bridge 抛出异常：记录日志，发出 `A2A request failed.` 消息。
3. 如果运行时错误：记录日志，将错误内容作为消息发出。
4. 如果没有增量：发出 `[AgentName] Request completed.` 消息。
5. 通过 `eventQueue.EnqueueMessageAsync(...)` 发出单个缓冲消息。

此路径保持不变。

---

## Handler：流式路径（新增）

`POST /a2a/message:stream` 到达时 `context.StreamingResponse == true`。Handler 使用 `TaskUpdater` 发出 A2A 任务生命周期事件，而不是缓冲：

1. **初始化任务：** `await updater.SubmitAsync()`
2. **开始工作：** `await updater.StartWorkAsync(initialMessage)` — 将任务状态转换为 `Working`，让客户端立即看到进度。
3. **流式增量：** 对于每个非空 `AgentStreamEventType.TextDelta`，通过以下方式发出 artifact 分块：
   ```csharp
   await updater.AddArtifactAsync(
       [Part.FromText(evt.Content)],
       artifactId: "text-delta",
       name: "Streaming response",
       description: "Incremental text from agent",
       lastChunk: false,
       append: true);
   ```
4. **错误处理：** 如果运行时错误或 bridge 异常：
   - 记录错误日志
   - 发出 `TaskUpdater.FailAsync(errorMessage)` — 这会将任务状态转换为 `Failed`
   - 返回（不继续执行）
5. **空增量：** 如果没有任何增量，发出一条包含 `[AgentName] Request completed.` 的 artifact 分块
6. **完成：** `await updater.CompleteAsync(finalMessage)` — 将任务状态转换为 `Completed` 并附上最终文本

`artifactId` 固定为 `"text-delta"`，使所有增量分块追加到同一 artifact，允许客户端逐步重组完整响应。

---

## 错误处理汇总

| 运行时条件 | 非流式响应 | 流式响应 |
|-------------------|------------------------|--------------------|
| 产生文本增量 | 包含拼接文本的单个 `Message` | 任务事件：Submit → Working → Artifact 分块 → Complete |
| 无文本，无错误 | `[AgentName] Request completed.` 消息 | 包含回退文本的单个 artifact 分块 → Complete |
| 运行时 `Error` 事件 | 包含错误文本的消息 | 使用错误文本调用 `FailAsync` |
| Bridge 异常 | `A2A request failed.` 消息 | 使用错误文本调用 `FailAsync` |
| 取消请求 | 向上传播 `OperationCanceledException` | 向上传播 `OperationCanceledException` |

---

## 取消请求

两个路径都检查 `OperationCanceledException` 并重新抛出。A2A SDK 会在取消时自动处理任务状态转换到 `Canceled`。

---

## A2A REST SSE 格式

A2A SDK 将 `StreamResponse` 事件序列化为 Server-Sent Events (SSE)。每行为：

```
data: <JSON>
```

其中 `<JSON>` 是 `A2A.StreamResponse` 的 JSON 表示。对于 A2A SDK v1.0.0-preview2，SSE 内容类型为 `text/event-stream`。

流式路径按顺序发出以下 `StreamResponse` 事件：

```
data: {"task":{"id":"<taskId>","status":{"state":"submitted"}}}
data: {"statusUpdate":{"taskId":"<taskId>","status":{"state":"working"}}}
data: {"artifactUpdate":{"taskId":"<taskId>","artifact":{...},"append":true,"lastChunk":false}}
... (每个 TextDelta 分块一条) ...
data: {"statusUpdate":{"taskId":"<taskId>","status":{"state":"completed","message":{...}}}}
```

---

## JSON-RPC 流式响应

JSON-RPC 客户端通过 `IAgentHandler` 使用相同的 `SendStreamingMessageAsync` 路径。A2A SDK 提供 `JsonRpcStreamedResult` 将 `StreamResponse` 对象写入 SSE。OpenClaw 的 `MapA2AJsonRpc` 注册使用相同的 handler，因此流式行为同时适用于 HTTP+JSON 和 JSON-RPC。

---

## 测试计划

### Discovery

- `AgentCard_Creates_DefaultSkill_When_NoneConfigured`：当 `EnableStreaming = true` 时，更新断言 `Assert.False(card.Capabilities!.Streaming)` → `Assert.True(card.Capabilities!.Streaming)`。
- 新增测试：当 `EnableStreaming = false` 时，`card.Capabilities!.Streaming` 应为 `false`。

### 非流式回归测试

- `MessageSend_BridgeException_Returns_Agent_Error_Message` — 不变。
- `MessageSend_BridgeCompletesWithoutText_Returns_Fallback_Agent_Message` — 不变。
- `MessageSend_WithoutMessageId_Returns_Agent_Message` — 不变。

### 流式（`A2AHttpEndpointTests` 中的新测试）

1. `MessageStream_Returns_SseContentType`：POST 到 `/a2a/message:stream`，断言 `Content-Type: text/event-stream`。
2. `MessageStream_Emits_Task_Submitted_And_Working`：验证前两个事件是已提交任务和工作状态更新。
3. `MessageStream_Emits_Artifact_Delta_Chunks`：验证 artifact 更新事件包含文本分块，所有分块共享 `artifactId: "text-delta"`，带有 `append: true` 和 `lastChunk: false`。
4. `MessageStream_Emits_Completed_Task_With_Final_Message`：验证最终事件是带有 `state: "completed"` 和消息部分的 `statusUpdate`。
5. `MessageStream_BridgeException_Emits_Failed_Task`：验证最终事件是带有 `state: "failed"` 的 `statusUpdate`。
6. `MessageStream_NoDeltas_Emits_Fallback_Artifact_Then_Complete`：验证单个 artifact 分块包含回退文本。

### JSON-RPC 流式（`A2AIntegrationTests` 中的新测试）

7. `MessageStream_ViaJsonRpc_Emits_Streaming_Events`：POST JSON-RPC 流式请求，验证 SSE 事件序列与 REST 相同。

---

## 修改的文件

| 文件 | 变更 |
|------|--------|
| `src/OpenClaw.MicrosoftAgentFrameworkAdapter/A2A/OpenClawAgentCardFactory.cs:27-31` | 设置 `Capabilities.Streaming = _options.EnableStreaming` |
| `src/OpenClaw.MicrosoftAgentFrameworkAdapter/A2A/OpenClawA2AAgentHandler.cs` | 在 `ExecuteAsync` 中添加流式分支，对流式路径使用 `TaskUpdater` |
| `src/OpenClaw.Tests/A2AHttpEndpointTests.cs` | 更新 discovery 测试断言，添加流式 SSE 测试 |
| `src/OpenClaw.Tests/A2AIntegrationTests.cs` | 添加 JSON-RPC 流式测试 |
| `docs/a2a.md` | 更新 "Streaming" 部分以反映完整的协议流式支持 |

---

## 无占位符

- 所有任务事件、状态和 JSON 字段名直接来自 A2A SDK v1.0.0-preview2 的 `StreamResponse`、`TaskUpdater` 和 `TaskStatusUpdateEvent` API，通过阅读 NuGet XML 文档确认。
- 测试断言使用这些 API 的实际类型名和属性值。
- 文件路径是仓库根目录的精确相对路径。
