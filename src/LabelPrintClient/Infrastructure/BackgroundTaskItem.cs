namespace LabelPrintClient.Infrastructure;

using LabelPrintClient.Services;

public sealed class BackgroundTaskItem : NotifyObject
{
    private BackgroundTaskStatus _status = BackgroundTaskStatus.Pending;
    private int _current;
    private int _total;
    private string? _message;
    private string? _errorMessage;
    private DateTime? _startTime;
    private DateTime? _finishTime;

    internal BackgroundTaskItem(long id, BackgroundTaskKind kind, string title)
    {
        Id = id;
        Kind = kind;
        Title = title;
        CreateTime = DateTime.Now;
    }

    public long Id { get; }

    public BackgroundTaskKind Kind { get; }

    public string Title { get; }

    public string DisplayTitle => NormalizeTitle(Title);

    public DateTime CreateTime { get; }

    public DateTime? StartTime
    {
        get => _startTime;
        private set => SetProperty(ref _startTime, value);
    }

    public DateTime? FinishTime
    {
        get => _finishTime;
        private set => SetProperty(ref _finishTime, value);
    }

    public BackgroundTaskStatus Status
    {
        get => _status;
        private set
        {
            if (!SetProperty(ref _status, value)) return;
            RaisePropertyChanged(nameof(StatusText));
            RaisePropertyChanged(nameof(IsActive));
            RaisePropertyChanged(nameof(DisplayTitle));
            RaisePropertyChanged(nameof(Description));
            RaisePropertyChanged(nameof(Percent));
            RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public int Current
    {
        get => _current;
        private set
        {
            if (!SetProperty(ref _current, value)) return;
            RaisePropertyChanged(nameof(Percent));
            RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public int Total
    {
        get => _total;
        private set
        {
            if (!SetProperty(ref _total, value)) return;
            RaisePropertyChanged(nameof(Percent));
            RaisePropertyChanged(nameof(ProgressText));
        }
    }

    public string? Message
    {
        get => _message;
        private set
        {
            if (!SetProperty(ref _message, value)) return;
            RaisePropertyChanged(nameof(Description));
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (!SetProperty(ref _errorMessage, value)) return;
            RaisePropertyChanged(nameof(Description));
        }
    }

    public bool IsActive => Status is BackgroundTaskStatus.Pending or BackgroundTaskStatus.Running;

    public double Percent
    {
        get
        {
            if (Status == BackgroundTaskStatus.Completed)
                return 100;

            return Total <= 0 ? 0 : Math.Clamp(Current * 100.0 / Total, 0, 100);
        }
    }

    public string ProgressText
    {
        get
        {
            if (Total > 0)
                return $"{Percent:0}%";

            return Status switch
            {
                BackgroundTaskStatus.Pending => AppLanguageService.GetString("TaskCenter.PendingShort"),
                BackgroundTaskStatus.Running => AppLanguageService.GetString("TaskCenter.RunningShort"),
                BackgroundTaskStatus.Completed => "100%",
                BackgroundTaskStatus.Failed => AppLanguageService.GetString("TaskCenter.Failed"),
                BackgroundTaskStatus.Canceled => AppLanguageService.GetString("TaskCenter.Canceled"),
                _ => "-"
            };
        }
    }

    public string Description => Status switch
    {
        BackgroundTaskStatus.Pending => AppLanguageService.GetString("TaskCenter.Waiting"),
        BackgroundTaskStatus.Running => string.IsNullOrWhiteSpace(Message) ? AppLanguageService.GetString("TaskCenter.RunningShort") : Message,
        BackgroundTaskStatus.Completed => string.IsNullOrWhiteSpace(Message) ? AppLanguageService.GetString("TaskCenter.TaskCompleted") : Message,
        BackgroundTaskStatus.Failed => string.IsNullOrWhiteSpace(ErrorMessage) ? AppLanguageService.GetString("TaskCenter.TaskFailed") : ErrorMessage,
        BackgroundTaskStatus.Canceled => AppLanguageService.GetString("TaskCenter.TaskCanceled"),
        _ => Message ?? string.Empty
    };

    public string KindText => Kind switch
    {
        BackgroundTaskKind.Import => AppLanguageService.GetString("TaskCenter.Import"),
        BackgroundTaskKind.Export => AppLanguageService.GetString("TaskCenter.Export"),
        BackgroundTaskKind.Preview => AppLanguageService.GetString("TaskCenter.Preview"),
        BackgroundTaskKind.Design => AppLanguageService.GetString("TaskCenter.Design"),
        BackgroundTaskKind.Print => AppLanguageService.GetString("TaskCenter.Print"),
        BackgroundTaskKind.Upload => AppLanguageService.GetString("TaskCenter.Upload"),
        _ => AppLanguageService.GetString("TaskCenter.Task")
    };

    public string StatusText => Status switch
    {
        BackgroundTaskStatus.Pending => AppLanguageService.GetString("TaskCenter.Pending"),
        BackgroundTaskStatus.Running => AppLanguageService.GetString("TaskCenter.Running"),
        BackgroundTaskStatus.Completed => AppLanguageService.GetString("TaskCenter.Completed"),
        BackgroundTaskStatus.Failed => AppLanguageService.GetString("TaskCenter.Failed"),
        BackgroundTaskStatus.Canceled => AppLanguageService.GetString("TaskCenter.Canceled"),
        _ => Status.ToString()
    };

    internal void MarkRunning()
    {
        StartTime = DateTime.Now;
        Status = BackgroundTaskStatus.Running;
    }

    internal void MarkCompleted()
    {
        if (Total > 0 && Current < Total)
            Current = Total;

        FinishTime = DateTime.Now;
        Status = BackgroundTaskStatus.Completed;
    }

    internal void MarkFailed(string errorMessage)
    {
        ErrorMessage = errorMessage;
        FinishTime = DateTime.Now;
        Status = BackgroundTaskStatus.Failed;
    }

    internal void MarkCanceled()
    {
        FinishTime = DateTime.Now;
        Status = BackgroundTaskStatus.Canceled;
    }

    internal void SetProgress(int current, int total, string? message)
    {
        Total = Math.Max(0, total);
        Current = Total == 0 ? Math.Max(0, current) : Math.Clamp(current, 0, Total);
        Message = message;
    }

    private static string NormalizeTitle(string title)
    {
        var text = title.Trim();
        if (text.StartsWith("正在", StringComparison.Ordinal))
            text = text[2..];

        return text
            .Trim()
            .TrimEnd('.', '。', '…')
            .Trim();
    }
}
