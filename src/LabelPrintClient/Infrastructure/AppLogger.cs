using System;
using System.IO;
using System.Text;

namespace LabelPrintClient.Infrastructure;

/// <summary>
/// 轻量级、零依赖、多线程安全的本地日志工具类
/// </summary>
public static class AppLogger
{
    private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
    private static readonly object LockObj = new();

    /// <summary>
    /// 记录错误日志
    /// </summary>
    public static void LogError(string message, Exception? ex = null)
    {
        WriteToFile("ERROR", message, ex);
    }

    /// <summary>
    /// 记录警告日志
    /// </summary>
    public static void LogWarning(string message, Exception? ex = null)
    {
        WriteToFile("WARN", message, ex);
    }

    /// <summary>
    /// 记录常规信息日志
    /// </summary>
    public static void LogInfo(string message)
    {
        WriteToFile("INFO", message);
    }

    private static void WriteToFile(string level, string message, Exception? ex = null)
    {
        try
        {
            lock (LockObj)
            {
                // 确保日志目录存在
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                // 每日生成一个独立的日志文件
                var filePath = Path.Combine(LogDirectory, $"log-{DateTime.Today:yyyy-MM-dd}.txt");
                var sb = new StringBuilder();
                
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");
                
                if (ex != null)
                {
                    sb.AppendLine($"Exception Type: {ex.GetType().FullName}");
                    sb.AppendLine($"Exception Message: {ex.Message}");
                    sb.AppendLine($"Stack Trace:\n{ex.StackTrace}");
                    
                    if (ex.InnerException != null)
                    {
                        sb.AppendLine($"Inner Exception Type: {ex.InnerException.GetType().FullName}");
                        sb.AppendLine($"Inner Exception Message: {ex.InnerException.Message}");
                        sb.AppendLine($"Inner Stack Trace:\n{ex.InnerException.StackTrace}");
                    }
                }
                sb.AppendLine(new string('-', 80));

                File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // 写入日志文件失败时静默处理，防止异常处理自身引发二次崩溃
        }
    }
}
