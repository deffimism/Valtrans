using Valtrans.Services;
using Xunit;

namespace Valtrans.Tests;

public sealed class IncomingOcrQueueTests
{
    [Fact]
    public async Task Queue_keeps_only_latest_two_pending_lines()
    {
        var processed = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var queue = new IncomingOcrQueue(async (line, token) =>
        {
            if (line == "active")
                await release.Task.WaitAsync(token);
            processed.Add(line);
        }, _ => { }, CancellationToken.None);

        queue.Enqueue("active");
        await Task.Delay(50);
        queue.Enqueue(string.Join('\n', Enumerable.Range(0, 20).Select(i => $"n{i}")));
        release.TrySetResult();
        await Task.Delay(300);
        queue.Dispose();
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains("active", processed);
        Assert.Contains("n19", processed);
        Assert.DoesNotContain("n0", processed);
        Assert.True(processed.Count <= 4);
    }
}
