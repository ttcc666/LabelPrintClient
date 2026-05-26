using System.Collections.Concurrent;

namespace LabelPrintClient.Infrastructure;

public static class StaThreadRunner
{
    private static readonly BlockingCollection<Action> WorkItems = new();
    private static readonly Thread WorkerThread = CreateWorkerThread();

    static StaThreadRunner()
    {
        WorkerThread.Start();
    }

    public static Task RunAsync(Action action, CancellationToken cancellationToken = default)
    {
        return RunAsync<object?>(
            () =>
            {
                action();
                return null;
            },
            cancellationToken);
    }

    public static async Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        try
        {
            WorkItems.Add(() =>
            {
                if (completion.Task.IsCompleted)
                    return;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(action());
                }
                catch (OperationCanceledException ex) when (ex.CancellationToken == cancellationToken)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            completion.TrySetException(ex);
        }

        return await completion.Task.ConfigureAwait(false);
    }

    private static Thread CreateWorkerThread()
    {
        var thread = new Thread(() =>
        {
            foreach (var workItem in WorkItems.GetConsumingEnumerable())
            {
                workItem();
            }
        })
        {
            IsBackground = true,
            Name = "LabelPrintClient STA Worker"
        };

        thread.SetApartmentState(ApartmentState.STA);
        return thread;
    }
}
