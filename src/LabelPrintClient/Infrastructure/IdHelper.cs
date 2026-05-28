using System;
using System.Threading;

namespace LabelPrintClient.Infrastructure;

/// <summary>
/// 雪花 ID (Snowflake ID) 生成器
/// 保证在分布式/单机高并发环境下生成递增、唯一的 64 位整型 ID。
/// </summary>
public static class IdHelper
{
    // 雪花纪元：2026-01-01 00:00:00 UTC (1767225600000 毫秒)
    private const long Twepoch = 1767225600000L;

    // 机器节点 ID 所占的位数
    private const int WorkerIdBits = 10;
    // 序列号所占的位数
    private const int SequenceBits = 12;

    // 支持的最大机器节点 ID，结果是 1023 (0~1023)
    private const long MaxWorkerId = -1L ^ (-1L << WorkerIdBits);
    // 序列号掩码，用于控制序列号在 0~4095 之间循环，结果是 4095 (0b111111111111)
    private const long SequenceMask = -1L ^ (-1L << SequenceBits);

    // 机器节点左移位数：12 位
    private const int WorkerIdShift = SequenceBits;
    // 时间毫秒数左移位数：12 + 10 = 22 位
    private const int TimestampLeftShift = SequenceBits + WorkerIdBits;

    private static readonly object LockObject = new();

    // 默认机器节点 ID (支持通过外部配置，但在本单机桌面应用中默认为 1L)
    private static long _workerId = 1L;

    private static long _lastTimestamp = -1L;
    private static long _sequence = 0L;

    /// <summary>
    /// 设置当前机器节点 ID（可选）
    /// </summary>
    public static void SetWorkerId(long workerId)
    {
        if (workerId < 0 || workerId > MaxWorkerId)
        {
            throw new ArgumentOutOfRangeException(nameof(workerId), $"Worker ID must be between 0 and {MaxWorkerId}");
        }
        lock (LockObject)
        {
            _workerId = workerId;
        }
    }

    /// <summary>
    /// 生成唯一的雪花 ID
    /// 零侵入、零破坏，保持原有方法签名不变。
    /// </summary>
    public static long NewId()
    {
        lock (LockObject)
        {
            var timestamp = GetCurrentTimestamp();

            // 时钟回拨处理
            if (timestamp < _lastTimestamp)
            {
                var offset = _lastTimestamp - timestamp;
                if (offset <= 5)
                {
                    // 微小回拨，自动挂起等待两倍的回拨差值以追回时钟
                    Thread.Sleep((int)(offset * 2));
                    timestamp = GetCurrentTimestamp();
                    if (timestamp < _lastTimestamp)
                    {
                        throw new InvalidOperationException($"Clock moved backwards. Refusing to generate id for {_lastTimestamp - timestamp} milliseconds.");
                    }
                }
                else
                {
                    // 严重回拨，抛出异常拦截，确保主键 100% 绝对不发生冲突
                    throw new InvalidOperationException($"Clock moved backwards. Refusing to generate id for {offset} milliseconds.");
                }
            }

            if (_lastTimestamp == timestamp)
            {
                // 同一毫秒内，序列号累加
                _sequence = (_sequence + 1) & SequenceMask;
                if (_sequence == 0)
                {
                    // 同一毫秒内序列溢出，自旋阻塞等待下一毫秒
                    timestamp = TilNextMillis(_lastTimestamp);
                }
            }
            else
            {
                // 时间戳改变，序列号重置
                _sequence = 0L;
            }

            _lastTimestamp = timestamp;

            // 移位拼接生成最终的 64 位 ID
            return ((timestamp - Twepoch) << TimestampLeftShift)
                   | (_workerId << WorkerIdShift)
                   | _sequence;
        }
    }

    /// <summary>
    /// 获取当前时间戳（毫秒）
    /// </summary>
    private static long GetCurrentTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// 自旋等待，直到获取比上次记录更大的时间戳
    /// </summary>
    private static long TilNextMillis(long lastTimestamp)
    {
        var timestamp = GetCurrentTimestamp();
        while (timestamp <= lastTimestamp)
        {
            Thread.SpinWait(1); // 极轻量级自旋
            timestamp = GetCurrentTimestamp();
        }
        return timestamp;
    }
}