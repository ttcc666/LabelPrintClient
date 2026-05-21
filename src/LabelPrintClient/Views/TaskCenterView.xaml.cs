using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using LabelPrintClient.Infrastructure;

namespace LabelPrintClient.Views;

public partial class TaskCenterView : System.Windows.Controls.UserControl
{
    public TaskCenterView()
    {
        InitializeComponent();
        TaskGrid.ItemsSource = BackgroundTaskQueue.Shared.Tasks;
        ((INotifyCollectionChanged)BackgroundTaskQueue.Shared.Tasks).CollectionChanged += Tasks_CollectionChanged;
        Loaded += (_, _) => UpdateSummary();
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
        if (e.PropertyName is nameof(BackgroundTaskItem.Status) or nameof(BackgroundTaskItem.Current) or nameof(BackgroundTaskItem.Total))
            UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (SummaryText == null || EmptyText == null)
            return;

        var tasks = BackgroundTaskQueue.Shared.Tasks;
        var active = tasks.Count(x => x.IsActive);
        var failed = tasks.Count(x => x.Status == BackgroundTaskStatus.Failed);
        SummaryText.Text = $"当前共 {tasks.Count} 个任务，执行中/等待 {active} 个，失败 {failed} 个。任务记录仅保留在本次应用运行期间。";
        EmptyText.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
