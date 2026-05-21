namespace LabelPrintClient.Infrastructure;

public static class StaThreadRunner
{
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
        var thread = new Thread(() =>
        {
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
        })
        {
            IsBackground = true,
            Name = "LabelPrintClient STA Worker"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        await using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        return await completion.Task.ConfigureAwait(false);
    }
}

