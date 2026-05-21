namespace LabelPrintClient.Infrastructure;

public sealed class BackgroundTaskQueue
{
    public static BackgroundTaskQueue Shared { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);

    private BackgroundTaskQueue()
    {
    }

    public async Task EnqueueAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await EnqueueAsync<object?>(
            async token =>
            {
                await operation(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await Task.Run(() => operation(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
