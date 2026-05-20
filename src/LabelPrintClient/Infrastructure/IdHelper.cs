using System.Threading;

namespace LabelPrintClient.Infrastructure;

public static class IdHelper
{
    private static long _sequence;

    public static long NewId()
    {
        var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var seq = Interlocked.Increment(ref _sequence) % 1000;
        return milliseconds * 1000 + seq;
    }
}
