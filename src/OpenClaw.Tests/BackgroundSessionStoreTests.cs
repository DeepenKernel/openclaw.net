using System.Text;
using OpenClaw.Core.Abstractions;
using OpenClaw.Core.Memory;
using OpenClaw.Core.Models;
using Xunit;

namespace OpenClaw.Tests;

public sealed class BackgroundSessionStoreTests : IAsyncDisposable
{
    private readonly List<string> _tempDirs = [];

    [Fact]
    public async Task FileStore_ListsOnlyRunnableBackgroundSessions()
    {
        var dir = NewTempDir("bg-file-basic");
        await using var store = new FileMemoryStore(dir);
        await SeedSessionsAsync(store, TestContext.Current.CancellationToken);

        var backgroundStore = Assert.IsAssignableFrom<IBackgroundSessionStore>(store);
        var sessions = await backgroundStore.ListBackgroundRunnableSessionsAsync(10, TestContext.Current.CancellationToken);

        Assert.Single(sessions);
        Assert.Equal("websocket:runnable", sessions[0].Id);
    }

    [Fact]
    public async Task SqliteStore_ListsOnlyRunnableBackgroundSessions()
    {
        var db = Path.Combine(Path.GetTempPath(), "openclaw-bg-" + Guid.NewGuid().ToString("N") + ".db");
        using var store = new SqliteMemoryStore(db, enableFts: false);
        await SeedSessionsAsync(store, TestContext.Current.CancellationToken);

        var backgroundStore = Assert.IsAssignableFrom<IBackgroundSessionStore>(store);
        var sessions = await backgroundStore.ListBackgroundRunnableSessionsAsync(10, TestContext.Current.CancellationToken);

        Assert.Single(sessions);
        Assert.Equal("websocket:runnable", sessions[0].Id);
    }

    [Fact]
    public async Task FileStore_ReturnsRecoveryOrder_WhenMoreThanLimit()
    {
        var dir = NewTempDir("bg-file-recovery-order");
        await using var store = new FileMemoryStore(dir);
        var ct = TestContext.Current.CancellationToken;

        // Create 5 runnable sessions with different LastContinuedAtUtc timestamps
        for (var i = 0; i < 5; i++)
        {
            var session = NewSession($"websocket:bg{i}", SessionRunState.Continuing);
            session.BackgroundRun!.LastContinuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-(5 - i)); // oldest to newest
            await store.SaveSessionAsync(session, ct);
        }

        var backgroundStore = Assert.IsAssignableFrom<IBackgroundSessionStore>(store);
        // Request only 2 — should return the oldest 2 (oldest-first recovery order)
        var sessions = await backgroundStore.ListBackgroundRunnableSessionsAsync(2, ct);

        Assert.Equal(2, sessions.Count);
        // Oldest first
        Assert.Equal("websocket:bg0", sessions[0].Id);
        Assert.Equal("websocket:bg1", sessions[1].Id);
    }

    [Fact]
    public async Task FileStore_ToleratesCorruptFile_ReturnsValidSessions()
    {
        var dir = NewTempDir("bg-file-corrupt");
        // Write a corrupt JSON file alongside valid sessions
        var corruptPath = Path.Combine(dir, "corrupt.json");
        await File.WriteAllTextAsync(corruptPath, "{ this is not valid json !! }", TestContext.Current.CancellationToken);

        await using var store = new FileMemoryStore(dir);
        await SeedSessionsAsync(store, TestContext.Current.CancellationToken);

        var backgroundStore = Assert.IsAssignableFrom<IBackgroundSessionStore>(store);
        var sessions = await backgroundStore.ListBackgroundRunnableSessionsAsync(10, TestContext.Current.CancellationToken);

        // Should still find the valid runnable session despite the corrupt file
        Assert.Single(sessions);
        Assert.Equal("websocket:runnable", sessions[0].Id);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort cleanup */ }
        }
        GC.SuppressFinalize(this);
    }

    private string NewTempDir(string suffix)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"openclaw-{suffix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _tempDirs.Add(dir);
        return dir;
    }

    private static async Task SeedSessionsAsync(IMemoryStore store, CancellationToken ct)
    {
        await store.SaveSessionAsync(NewSession("websocket:runnable", SessionRunState.Continuing), ct);
        await store.SaveSessionAsync(NewSession("websocket:paused", SessionRunState.Paused), ct);
        await store.SaveSessionAsync(NewSession("websocket:done", SessionRunState.Completed), ct);
        await store.SaveSessionAsync(NewSession("websocket:idle", SessionRunState.Idle), ct);
    }

    private static Session NewSession(string id, SessionRunState state)
        => new()
        {
            Id = id,
            ChannelId = "websocket",
            SenderId = id.Split(':')[1],
            RunState = state,
            BackgroundRun = state is SessionRunState.Running or SessionRunState.Continuing
                ? new BackgroundRunMetadata
                {
                    RunId = "run_" + id.Split(':')[1],
                    Objective = "Fix tests",
                    StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    LastContinuedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                    TokenBudget = 128_000,
                    MaxContinuationTurns = 200
                }
                : null
        };
}
