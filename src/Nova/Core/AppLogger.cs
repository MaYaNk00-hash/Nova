using System.Diagnostics;
using System.IO;

namespace Nova.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class AppLogger
{
    private static readonly object _lock = new();
    private static string _logFilePath = AppInfo.LogFilePath;
    private static bool _initialized = false;

    public static string CurrentLogFilePath => _logFilePath;

    public static void Initialize(string? customLogPath = null)
    {
        lock (_lock)
        {
            if (customLogPath != null)
            {
                _logFilePath = customLogPath;
            }

            try
            {
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                _initialized = true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[AppLogger] Failed to initialize directory: {ex.Message}");
            }
        }
    }

    public static void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (!_initialized)
        {
            Initialize();
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level.ToString().ToUpperInvariant()}] {message}";

        if (exception != null)
        {
            logLine += Environment.NewLine + $"Exception: {exception.GetType().FullName}: {exception.Message}" +
                       Environment.NewLine + exception.StackTrace;
        }

        Trace.WriteLine(logLine);

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[AppLogger] Failed to write log: {ex.Message}");
            }
        }
    }

    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
    public static void Debug(string message) => Log(LogLevel.Debug, message);
}
