using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenClaw.Core.Loops;
using Xunit;

namespace OpenClaw.Tests;

public sealed class ClawLoopSchedulerTests
{
    private readonly ILogger<ClawLoopScheduler> _mockLogger = Substitute.For<ILogger<ClawLoopScheduler>>();

    [Fact]
    public async Task ScheduleLoop_AddsEntry()
    {
        var scheduler = new ClawLoopScheduler(_mockLogger);
        await scheduler.ScheduleLoopAsync("s1", "*/5 * * * *", "check status", CancellationToken.None);

        var status = await scheduler.GetLoopStatusAsync("s1", CancellationToken.None);
        Assert.NotNull(status);
        Assert.Contains("*/5 * * * *", status);
        Assert.Contains("check status", status);
    }

    [Fact]
    public async Task CancelLoop_RemovesEntry()
    {
        var scheduler = new ClawLoopScheduler(_mockLogger);
        await scheduler.ScheduleLoopAsync("s1", "*/5 * * * *", "check status", CancellationToken.None);
        await scheduler.CancelLoopAsync("s1", CancellationToken.None);

        var status = await scheduler.GetLoopStatusAsync("s1", CancellationToken.None);
        Assert.Null(status);
    }

    [Fact]
    public async Task SignalComplete_CancelsLoop()
    {
        var scheduler = new ClawLoopScheduler(_mockLogger);
        await scheduler.ScheduleLoopAsync("s1", "*/5 * * * *", "check status", CancellationToken.None);

        var control = (ILoopControlService)scheduler;
        await control.SignalCompleteAsync("s1", CancellationToken.None);

        var status = await scheduler.GetLoopStatusAsync("s1", CancellationToken.None);
        Assert.Null(status);
    }

    [Fact]
    public async Task GetLoopStatus_NoEntry_ReturnsNull()
    {
        var scheduler = new ClawLoopScheduler(_mockLogger);
        var status = await scheduler.GetLoopStatusAsync("nonexistent", CancellationToken.None);
        Assert.Null(status);
    }

    [Fact]
    public async Task ScheduleLoop_OverwritesExisting()
    {
        var scheduler = new ClawLoopScheduler(_mockLogger);
        await scheduler.ScheduleLoopAsync("s1", "*/5 * * * *", "first prompt", CancellationToken.None);
        await scheduler.ScheduleLoopAsync("s1", "*/10 * * * *", "second prompt", CancellationToken.None);

        var status = await scheduler.GetLoopStatusAsync("s1", CancellationToken.None);
        Assert.NotNull(status);
        Assert.Contains("second prompt", status);
    }

    [Theory]
    [InlineData("5m", "*/5 * * * *")]
    [InlineData("30s", "*/30 * * * * *")]
    [InlineData("120s", "*/2 * * * *")]
    [InlineData("1h", "0 */1 * * *")]
    public void IntervalToCron_ConvertsCorrectly(string interval, string expectedCron)
    {
        var cron = ClawLoopScheduler.IntervalToCron(interval);
        Assert.Equal(expectedCron, cron);
    }

    [Theory]
    [InlineData("")]
    [InlineData("5x")]
    [InlineData("abc")]
    public void IntervalToCron_Invalid_Throws(string interval)
    {
        Assert.Throws<ArgumentException>(() => ClawLoopScheduler.IntervalToCron(interval));
    }
}
