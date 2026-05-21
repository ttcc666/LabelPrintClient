namespace LabelPrintClient.Infrastructure;

public sealed record BackgroundTaskProgress(int Current, int Total, string? Message = null);
