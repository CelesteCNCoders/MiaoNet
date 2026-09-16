using Microsoft.Extensions.Logging;

namespace MiaoNet.Server;

/// <summary>
/// 管理后台实时日志用的环形缓冲区, 保存最近的日志条目.
/// </summary>
public sealed class AdminLogBuffer
{
    public readonly record struct Entry(
        long Id,
        DateTime Time,
        LogLevel Level,
        string Category,
        string Message,
        string? Exception,
        string? Scope
    );

    private readonly Entry[] entries;
    private readonly object sync = new();

    private long nextId;
    private int head;
    private int count;

    public AdminLogBuffer(int capacity = 1000)
    {
        entries = new Entry[capacity];
    }

    public long LatestId
    {
        get
        {
            lock (sync)
                return nextId - 1;
        }
    }

    /// <param name="scope">
    /// 该条日志的 logger scope 文本(嵌套时逐层连接), 没有作用域时为 null.
    /// </param>
    public void Record(LogLevel level, string category, string message, string? exception, string? scope)
    {
        lock (sync)
        {
            entries[head] = new Entry(nextId++, DateTime.UtcNow, level, category, message, exception, scope);
            head = (head + 1) % entries.Length;
            if (count < entries.Length)
                count++;
        }
    }

    /// <summary>
    /// 获取 id 大于 <paramref name="after"/> 的条目(按 id 升序), 最多 <paramref name="limit"/> 条;
    /// 超出限制时保留最新的条目.
    /// </summary>
    public List<Entry> GetAfter(long after, int limit)
    {
        lock (sync)
        {
            List<Entry> result = new();
            long firstId = nextId - count;
            for (int i = 0; i < count; i++)
            {
                int index = (head - count + i + entries.Length) % entries.Length;
                ref Entry entry = ref entries[index];
                if (entry.Id > after && entry.Id >= firstId)
                    result.Add(entry);
            }
            if (result.Count > limit)
                result.RemoveRange(0, result.Count - limit);
            return result;
        }
    }
}
