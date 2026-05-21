namespace LabelPrintClient.Infrastructure;

public static class IdHelper
{
    private const int SequenceLimit = 1000;
    private static readonly object LockObject = new();
    private static long _lastMilliseconds = -1;
    private static int _sequence;

    public static long NewId()
    {
        lock (LockObject)
        {
            var milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (milliseconds <= _lastMilliseconds)
            {
                _sequence++;
                if (_sequence >= SequenceLimit)
                {
                    milliseconds = _lastMilliseconds + 1;
                    _sequence = 0;
                }
                else
                {
                    milliseconds = _lastMilliseconds;
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastMilliseconds = milliseconds;
            return milliseconds * SequenceLimit + _sequence;
        }
    }
}
