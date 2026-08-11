using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

/// <summary>
/// 把所有日志事件写入 <see cref="AdminLogBuffer"/> 的日志提供程序, 供管理后台实时日志使用.
/// </summary>
public sealed class AdminLogBufferLoggerProvider : ILoggerProvider
{
    private readonly AdminLogBuffer buffer;

    public AdminLogBufferLoggerProvider(AdminLogBuffer buffer)
    {
        this.buffer = buffer;
    }

    public ILogger CreateLogger(string categoryName) => new Logger(buffer, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(AdminLogBuffer buffer, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (!IsEnabled(logLevel))
                return;
            buffer.Record(logLevel, category, formatter(state, exception), exception?.ToString());
        }
    }
}
