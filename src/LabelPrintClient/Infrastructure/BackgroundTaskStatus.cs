namespace LabelPrintClient.Infrastructure;

public enum BackgroundTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled
}