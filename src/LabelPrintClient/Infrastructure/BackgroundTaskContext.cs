namespace LabelPrintClient.Infrastructure;

public sealed class BackgroundTaskContext
{
    private readonly BackgroundTaskItem _item;
    private readonly Action<BackgroundTaskItem, BackgroundTaskProgress> _report;

    internal BackgroundTaskContext(
        BackgroundTaskItem item,
        CancellationToken cancellationToken,
        Action<BackgroundTaskItem, BackgroundTaskProgress> report)
    {
        _item = item;
        CancellationToken = cancellationToken;
        _report = report;
        Progress = new Progress<BackgroundTaskProgress>(Report);
    }

    public CancellationToken CancellationToken { get; }

    public IProgress<BackgroundTaskProgress> Progress { get; }

    public void Report(BackgroundTaskProgress progress)
    {
        _report(_item, progress);
    }

    public void Report(int current, int total, string? message = null)
    {
        Report(new BackgroundTaskProgress(current, total, message));
    }
}
