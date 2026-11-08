using System;
using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>日志控制台的严重级别。</summary>
    public enum LogConsoleLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    /// <summary>控制台中的一条日志记录（主线程对象）。</summary>
    public sealed class LogConsoleEntry
    {
        public string Message = string.Empty;
        public string StackTrace = string.Empty;
        public LogConsoleLevel Level;
        public DateTime TimestampUtc;
        public string SceneName = string.Empty;
        public float Fps;
        public float MemoryMb;
        /// <summary>折叠视图中的重复次数；非折叠视图恒为 1。</summary>
        public int RepeatCount = 1;

        /// <summary>保守估算的内存占用，用于容量淘汰。</summary>
        public long EstimatedBytes
        {
            get { return 96L + 2L * (Message.Length + StackTrace.Length); }
        }

        public LogConsoleEntry Copy()
        {
            return new LogConsoleEntry
            {
                Message = Message,
                StackTrace = StackTrace,
                Level = Level,
                TimestampUtc = TimestampUtc,
                SceneName = SceneName,
                Fps = Fps,
                MemoryMb = MemoryMb,
                RepeatCount = RepeatCount,
            };
        }
    }

    /// <summary>
    /// 主线程日志存储：按条数/字节双上限淘汰最旧记录，并提供严重级别、搜索与折叠查询。
    /// <para>不依赖 Unity 对象，可在 EditMode 测试中直接构造。</para>
    /// </summary>
    public sealed class LogConsoleStore
    {
        private struct GroupKey : IEquatable<GroupKey>
        {
            public LogConsoleLevel Level;
            public string Message;
            public string StackTrace;

            public bool Equals(GroupKey other)
            {
                return Level == other.Level
                    && string.Equals(Message, other.Message, StringComparison.Ordinal)
                    && string.Equals(StackTrace, other.StackTrace, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is GroupKey && Equals((GroupKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)Level;
                    hash = (hash * 397) ^ (Message != null ? Message.GetHashCode() : 0);
                    hash = (hash * 397) ^ (StackTrace != null ? StackTrace.GetHashCode() : 0);
                    return hash;
                }
            }
        }

        private readonly LinkedList<LogConsoleEntry> entries = new LinkedList<LogConsoleEntry>();
        private long bytes;

        public LogConsoleStore(int maxEntries = 2000, long maxBytes = 8L * 1024 * 1024)
        {
            MaxEntries = Math.Max(1, maxEntries);
            MaxBytes = Math.Max(0, maxBytes);
        }

        /// <summary>最多保留的条数；超过时从最旧开始淘汰。</summary>
        public int MaxEntries { get; set; }

        /// <summary>最多保留的估算字节数；0 表示不限制。</summary>
        public long MaxBytes { get; set; }

        public int Count { get { return entries.Count; } }
        public long EstimatedBytes { get { return bytes; } }
        public int Version { get; private set; }

        public int InfoCount { get; private set; }
        public int WarningCount { get; private set; }
        public int ErrorCount { get; private set; }

        public void Add(LogConsoleEntry entry)
        {
            if (entry == null) return;
            if (entry.Message == null) entry.Message = string.Empty;
            if (entry.StackTrace == null) entry.StackTrace = string.Empty;
            entries.AddLast(entry);
            bytes += entry.EstimatedBytes;
            switch (entry.Level)
            {
                case LogConsoleLevel.Info: InfoCount++; break;
                case LogConsoleLevel.Warning: WarningCount++; break;
                default: ErrorCount++; break;
            }
            Trim();
            Version++;
        }

        public void Clear()
        {
            entries.Clear();
            bytes = 0;
            InfoCount = WarningCount = ErrorCount = 0;
            Version++;
        }

        public LogConsoleEntry[] Snapshot()
        {
            var result = new LogConsoleEntry[entries.Count];
            entries.CopyTo(result, 0);
            return result;
        }

        /// <summary>
        /// 把当前可见记录写入 <paramref name="target"/>（会先清空）。
        /// <para>折叠模式按“级别 + 消息 + 堆栈”合并，返回副本并累加 RepeatCount；非折叠模式返回存储内对象的只读视图。</para>
        /// </summary>
        public void CopyVisible(List<LogConsoleEntry> target, bool collapse,
            bool info, bool warning, bool error, string search)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.Clear();
            string term = (search ?? string.Empty).Trim();

            if (!collapse)
            {
                foreach (LogConsoleEntry entry in entries)
                {
                    if (Matches(entry, info, warning, error, term)) target.Add(entry);
                }
                return;
            }

            var groups = new Dictionary<GroupKey, LogConsoleEntry>();
            foreach (LogConsoleEntry entry in entries)
            {
                if (!Matches(entry, info, warning, error, term)) continue;
                var key = new GroupKey { Level = entry.Level, Message = entry.Message, StackTrace = entry.StackTrace };
                LogConsoleEntry existing;
                if (groups.TryGetValue(key, out existing))
                {
                    existing.RepeatCount++;
                    existing.TimestampUtc = entry.TimestampUtc;
                    existing.SceneName = entry.SceneName;
                }
                else
                {
                    LogConsoleEntry copy = entry.Copy();
                    groups.Add(key, copy);
                    target.Add(copy);
                }
            }
        }

        /// <summary>按固定行高计算需要绘制的行区间（含少量 overscan）。</summary>
        public static void GetVisibleRange(int total, float scrollY, float rowHeight, float viewHeight,
            out int first, out int last)
        {
            first = 0;
            last = 0;
            if (total <= 0 || rowHeight <= 0f) return;

            int begin = (int)Math.Floor(Math.Max(0f, scrollY) / rowHeight) - 2;
            int end = (int)Math.Ceiling((Math.Max(0f, scrollY) + Math.Max(0f, viewHeight)) / rowHeight) + 2;
            first = Math.Max(0, Math.Min(total, begin));
            last = Math.Max(first, Math.Min(total, end));
        }

        private static bool Matches(LogConsoleEntry entry, bool info, bool warning, bool error, string term)
        {
            bool enabled = entry.Level == LogConsoleLevel.Info ? info
                : entry.Level == LogConsoleLevel.Warning ? warning
                : error;
            if (!enabled) return false;
            if (term.Length == 0) return true;
            return entry.Message.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.StackTrace.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Trim()
        {
            while (entries.Count > 0 && (entries.Count > MaxEntries || (MaxBytes > 0 && bytes > MaxBytes)))
            {
                LogConsoleEntry oldest = entries.First.Value;
                entries.RemoveFirst();
                bytes -= oldest.EstimatedBytes;
                if (bytes < 0) bytes = 0;
                switch (oldest.Level)
                {
                    case LogConsoleLevel.Info: InfoCount--; break;
                    case LogConsoleLevel.Warning: WarningCount--; break;
                    default: ErrorCount--; break;
                }
            }
        }
    }
}
