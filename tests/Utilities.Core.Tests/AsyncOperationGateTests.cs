namespace Utilities.Core.Tests;

using Utilities.Core.BusinessLogic;
using Xunit;

public sealed class AsyncOperationGateTests
{
    [Fact]
    public async Task TryRunAsync_RejectsSecondActionWhileFirstIsActiveAndAllowsRetryAfterward()
    {
        var gate = new AsyncOperationGate();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = gate.TryRunAsync(async _ =>
        {
            started.SetResult();
            await release.Task;
        });
        await started.Task;

        var second = await gate.TryRunAsync(_ => Task.CompletedTask);
        Assert.False(second);
        Assert.True(gate.IsRunning);

        release.SetResult();
        Assert.True(await first);
        Assert.False(gate.IsRunning);
        Assert.True(await gate.TryRunAsync(_ => Task.CompletedTask));
    }

    [Fact]
    public async Task TryRunAsync_ReleasesGateWhenOperationFails()
    {
        var gate = new AsyncOperationGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.TryRunAsync(_ => throw new InvalidOperationException()));

        Assert.False(gate.IsRunning);
        Assert.True(await gate.TryRunAsync(_ => Task.CompletedTask));
    }
}
