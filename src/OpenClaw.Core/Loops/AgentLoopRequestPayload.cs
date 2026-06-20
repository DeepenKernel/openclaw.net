using System.Text.Json.Serialization;

namespace OpenClaw.Core.Loops;

/// <summary>
/// Strongly-typed payload carried by TickerQ cron jobs for loop dispatch.
/// Uses source-generated JSON to stay AOT-safe.
/// </summary>
public sealed record AgentLoopRequestPayload(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("prompt")] string Prompt
);
