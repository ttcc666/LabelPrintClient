using System.Collections.ObjectModel;
using System.Windows;

namespace LabelPrintClient.Infrastructure;

public sealed class BackgroundTaskQueue
{
    public static BackgroundTaskQueue Shared { get; } = new();

    private const int MaxTaskItems = 200;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ObservableCollection<BackgroundTaskItem> _tasks = new();

    private BackgroundTaskQueue()
    {
        Tasks = new ReadOnlyObservableCollection<BackgroundTaskItem>(_tasks);
    }

    public ReadOnlyObservableCollection<BackgroundTaskItem> Tasks { get; }

    public async Task EnqueueAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        await EnqueueAsync<object?>(
            BackgroundTaskKind.Other,
            "后台任务",
            async context =>
            {
                await operation(context.CancellationToken).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> EnqueueAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        return await EnqueueAsync(
            BackgroundTaskKind.Other,
            "后台任务",
            context => operation(context.CancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(
        BackgroundTaskKind kind,
        string title,
        Func<BackgroundTaskContext, Task> operation,
        CancellationToken cancellationToken = default)
    {
        await EnqueueAsync<object?>(
            kind,
            title,
            async context =>
            {
                await operation(context).ConfigureAwait(false);
                return null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<T> EnqueueAsync<T>(
        BackgroundTaskKind kind,
        string title,
        Func<BackgroundTaskContext, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        var item = new BackgroundTaskItem(IdHelper.NewId(), kind, title);
        AddTask(item);

        var acquired = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            acquired = true;
            cancellationToken.ThrowIfCancellationRequested();

            UpdateTask(item, x => x.MarkRunning());
            var context = new BackgroundTaskContext(item, cancellationToken, Report);
            var result = await Task.Run(() => operation(context), cancellationToken)
                .ConfigureAwait(false);
            UpdateTask(item, x => x.MarkCompleted());
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UpdateTask(item, x => x.MarkCanceled());
            throw;
        }
        catch (Exception ex)
        {
            UpdateTask(item, x => x.MarkFailed(ex.Message));
            throw;
        }
        finally
        {
            if (acquired)
                _gate.Release();
        }
    }

    public void ClearCompleted()
    {
        Dispatch(() =>
        {
            for (var i = _tasks.Count - 1; i >= 0; i--)
            {
                if (!_tasks[i].IsActive)
                    _tasks.RemoveAt(i);
            }
        });
    }

    private void AddTask(BackgroundTaskItem item)
    {
        Dispatch(() =>
        {
            _tasks.Insert(0, item);
            while (_tasks.Count > MaxTaskItems)
            {
                _tasks.RemoveAt(_tasks.Count - 1);
            }
        });
    }

    private void Report(BackgroundTaskItem item, BackgroundTaskProgress progress)
    {
        UpdateTask(item, x => x.SetProgress(progress.Current, progress.Total, progress.Message));
    }

    private static void UpdateTask(BackgroundTaskItem item, Action<BackgroundTaskItem> update)
    {
        Dispatch(() => update(item));
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }
}
