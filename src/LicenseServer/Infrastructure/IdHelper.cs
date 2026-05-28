namespace LicenseServer.Infrastructure;

public static class IdHelper
{
    private static readonly object LockObject = new();
    private static long _lastTimestamp;
    private static long _sequence;

    public static long NewId()
    {
        lock (LockObject)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (timestamp == _lastTimestamp)
                _sequence++;
            else
                _sequence = 0;

            _lastTimestamp = timestamp;
            return ((timestamp - 1767225600000L) << 12) | (_sequence & 0xfff);
        }
    }
}
