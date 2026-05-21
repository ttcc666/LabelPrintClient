using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LabelPrintClient.Infrastructure;

namespace LabelPrintClient.Modules.TaskCenter.Views;

public partial class TaskCenterView : System.Windows.Controls.UserControl
{
    private readonly ICollectionView _taskView;

    public TaskCenterView()
    {
        InitializeComponent();
        _taskView = CollectionViewSource.GetDefaultView(BackgroundTaskQueue.Shared.Tasks);
        _taskView.Filter = FilterTask;
        TaskGrid.ItemsSource = _taskView;
        ((INotifyCollectionChanged)BackgroundTaskQueue.Shared.Tasks).CollectionChanged += Tasks_CollectionChanged;
        foreach (var item in BackgroundTaskQueue.Shared.Tasks)
            item.PropertyChanged += TaskItem_PropertyChanged;
        Loaded += (_, _) => UpdateSummary();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshFilter();
    }

    private void ApplyFilter_Click(object sender, RoutedEventArgs e)
    {
        RefreshFilter();
    }

    private void ClearFilter_Click(object sender, RoutedEventArgs e)
    {
        KindFilterBox.SelectedIndex = 0;
        StatusFilterBox.SelectedIndex = 0;
        TaskSearchBox.Text = string.Empty;
        RefreshFilter();
    }

    private void TaskSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        RefreshFilter();
    }

    private void ClearCompleted_Click(object sender, RoutedEventArgs e)
    {
        BackgroundTaskQueue.Shared.ClearCompleted();
        UpdateSummary();
    }

    private void Tasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<BackgroundTaskItem>())
                item.PropertyChanged += TaskItem_PropertyChanged;
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<BackgroundTaskItem>())
                item.PropertyChanged -= TaskItem_PropertyChanged;
        }

        UpdateSummary();
    }

    private void TaskItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BackgroundTaskItem.Status) or
            nameof(BackgroundTaskItem.Message) or
            nameof(BackgroundTaskItem.ErrorMessage))
        {
            RefreshFilter();
            return;
        }

        if (e.PropertyName is nameof(BackgroundTaskItem.Current) or nameof(BackgroundTaskItem.Total))
            UpdateSummary();
    }

    private bool FilterTask(object item)
    {
        if (item is not BackgroundTaskItem task)
            return false;

        var kind = GetSelectedKind();
        if (kind.HasValue && task.Kind != kind.Value)
            return false;

        var status = GetSelectedStatus();
        if (status.HasValue && task.Status != status.Value)
            return false;

        var keyword = TaskSearchBox?.Text.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        return Contains(task.Title, keyword) ||
               Contains(task.KindText, keyword) ||
               Contains(task.StatusText, keyword) ||
               Contains(task.Message, keyword) ||
               Contains(task.ErrorMessage, keyword);
    }

    private void RefreshFilter()
    {
        _taskView.Refresh();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (SummaryText == null || EmptyText == null)
            return;

        var tasks = BackgroundTaskQueue.Shared.Tasks;
        var visibleCount = _taskView.Cast<object>().Count();
        var active = tasks.Count(x => x.IsActive);
        var failed = tasks.Count(x => x.Status == BackgroundTaskStatus.Failed);
        SummaryText.Text = $"当前显示 {visibleCount} / 共 {tasks.Count} 个任务，执行中/等待 {active} 个，失败 {failed} 个。任务记录仅保留在本次应用运行期间。";
        EmptyText.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private BackgroundTaskKind? GetSelectedKind()
    {
        if (KindFilterBox?.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<BackgroundTaskKind>(item.Tag?.ToString(), out var kind))
        {
            return kind;
        }

        return null;
    }

    private BackgroundTaskStatus? GetSelectedStatus()
    {
        if (StatusFilterBox?.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<BackgroundTaskStatus>(item.Tag?.ToString(), out var status))
        {
            return status;
        }

        return null;
    }

    private static bool Contains(string? value, string keyword)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}


