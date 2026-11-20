using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace STS2_MCP;

internal sealed class MainThreadWorkQueue(TimeSpan queueTimeout, TimeProvider timeProvider)
{
    private readonly ConcurrentQueue<Action> _pending = new();

    internal void ProcessFrame(int maxOperations)
    {
        for (int processed = 0; processed < maxOperations && _pending.TryDequeue(out var operation); processed++)
            operation();
    }

    internal async Task<T> Enqueue<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        long enqueuedAt = timeProvider.GetTimestamp();
        int claimed = 0;

        // The game thread and the timeout compete to claim a pending operation.
        // Only the game thread may execute it, and only before the deadline.
        _pending.Enqueue(() =>
        {
            if (Interlocked.Exchange(ref claimed, 1) != 0)
                return;

            if (timeProvider.GetElapsedTime(enqueuedAt) >= queueTimeout)
            {
                completion.SetException(new MainThreadQueueTimeoutException());
                return;
            }

            try { completion.SetResult(work()); }
            catch (Exception ex) { completion.SetException(ex); }
        });

        using var timerCancellation = new CancellationTokenSource();
        Task timer = Task.Delay(queueTimeout, timeProvider, timerCancellation.Token);
        if (await Task.WhenAny(completion.Task, timer).ConfigureAwait(false) == timer
            && Interlocked.Exchange(ref claimed, 1) == 0)
        {
            completion.SetException(new MainThreadQueueTimeoutException());
        }
        timerCancellation.Cancel();

        // A running action owns its result, even if it finishes after the queue deadline.
        return await completion.Task.ConfigureAwait(false);
    }
}

internal sealed class MainThreadQueueTimeoutException()
    : TimeoutException("The game did not start the queued request in time. The request was canceled.");
