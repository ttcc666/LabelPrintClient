using LabelPrintClient.Infrastructure;

namespace LabelPrintClient.Tests.Infrastructure;

public class BackgroundTaskQueueFunctionalTests
{
    [Fact]
    [Trait("Category", "Functional")]
    public async Task EnqueueAsync_CompletesTaskAndStoresProgress()
    {
        var title = $"功能测试任务-{Guid.NewGuid():N}";

        var result = await BackgroundTaskQueue.Shared.EnqueueAsync(
            BackgroundTaskKind.Export,
            title,
            context =>
            {
                context.Report(1, 2, "处理中");
                context.Report(2, 2, "完成");
                return Task.FromResult(42);
            });

        var item = BackgroundTaskQueue.Shared.Tasks.First(x => x.Title == title);

        Assert.Equal(42, result);
        Assert.Equal(BackgroundTaskStatus.Completed, item.Status);
        Assert.Equal(2, item.Current);
        Assert.Equal(2, item.Total);
        Assert.Equal("完成", item.Message);
        Assert.Equal(100, item.Percent);
    }

    [Fact]
    [Trait("Category", "Functional")]
    public async Task EnqueueAsync_WhenOperationFails_MarksTaskFailed()
    {
        var title = $"失败功能测试任务-{Guid.NewGuid():N}";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BackgroundTaskQueue.Shared.EnqueueAsync(
                BackgroundTaskKind.Import,
                title,
                _ => throw new InvalidOperationException("boom")));

        var item = BackgroundTaskQueue.Shared.Tasks.First(x => x.Title == title);

        Assert.Equal("boom", ex.Message);
        Assert.Equal(BackgroundTaskStatus.Failed, item.Status);
        Assert.Equal("boom", item.ErrorMessage);
        Assert.False(item.IsActive);
    }
}
