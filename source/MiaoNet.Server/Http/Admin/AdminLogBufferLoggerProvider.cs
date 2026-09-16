using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

/// <summary>
/// 把所有日志事件写入 <see cref="AdminLogBuffer"/> 的日志提供程序, 供管理后台实时日志使用.
/// </summary>
public sealed class AdminLogBufferLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly AdminLogBuffer buffer;
    private IExternalScopeProvider? scopeProvider;

    public AdminLogBufferLoggerProvider(AdminLogBuffer buffer)
    {
        this.buffer = buffer;
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <summary>
    /// 由 LoggerFactory 注入, 由此可读到当前异步上下文上的 logger scope(如 "connection 1.2.3.4:5678").
    /// </summary>
    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        this.scopeProvider = scopeProvider;
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// 把当前作用域栈渲染成单行文本(逐层用 " => " 连接, 与 console 的 "=> ..." 形式一致);
    /// 没有任何作用域时返回 null.
    /// </summary>
    private string? GetScopeText()
    {
        if (scopeProvider is null)
            return null;

        string? text = null;
        scopeProvider.ForEachScope(
            (scope, _) =>
            {
                string part = scope?.ToString() ?? string.Empty;
                if (part.Length == 0)
                    return;
                text = text is null ? part : text + " => " + part;
            },
            state: (object?)null
        );
        return text;
    }

    private sealed class Logger(AdminLogBufferLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => provider.scopeProvider?.Push(state);

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
            provider.buffer.Record(
                logLevel,
                category,
                formatter(state, exception),
                exception?.ToString(),
                provider.GetScopeText()
            );
        }
    }
}
