using System;
using System.Collections.Generic;

namespace UPandaGF
{

/// <summary>主线程日志窗口。原始记录和折叠索引同时淘汰，不保留独立的无限采样/字符串缓存。</summary>
internal sealed class ReporterLogStore
{
    private struct Key : IEquatable<Key>
    {
        public string message, stack;
        public Reporter._LogType type;
        public Key(Reporter.Log log) { message = log.condition; stack = log.stacktrace; type = log.logType; }
        public bool Equals(Key other) { return type == other.type && message == other.message && stack == other.stack; }
        public override bool Equals(object obj) { return obj is Key && Equals((Key)obj); }
        public override int GetHashCode()
        {
            unchecked { return ((message.GetHashCode() * 397) ^ stack.GetHashCode()) * 397 ^ (int)type; }
        }
    }
    private sealed class Group
    {
        public Reporter.Log display;
        public LinkedListNode<Group> node;
    }
    private readonly LinkedList<Reporter.Log> entries = new LinkedList<Reporter.Log>();
    private readonly Dictionary<Key, Group> groups = new Dictionary<Key, Group>();
    private readonly LinkedList<Group> groupOrder = new LinkedList<Group>();
    public int Count => entries.Count;
    public int GroupCount => groups.Count;
    public long EstimatedBytes { get; private set; }
    public int LogCount { get; private set; }
    public int WarningCount { get; private set; }
    public int ErrorCount { get; private set; }
    public int Version { get; private set; }

    public void Add(Reporter.Log log, int maxCount, long maxBytes)
    {
        log.condition = log.condition ?? string.Empty;
        log.stacktrace = log.stacktrace ?? string.Empty;
        var key = new Key(log);
        Group group;
        if (!groups.TryGetValue(key, out group))
        {
            group = new Group { display = log.CreateCopy() };
            group.display.count = 0;
            group.node = groupOrder.AddLast(group);
            groups.Add(key, group);
        }
        // 相同组的消息与堆栈共用字符串，但缓存的生命周期由组本身决定。
        log.condition = group.display.condition;
        log.stacktrace = group.display.stacktrace;
        group.display.count++;
        group.display.sample = log.sample;
        group.display.sampleId = log.sampleId;
        entries.AddLast(log);
        EstimatedBytes += Bytes(log);
        CountSeverity(log.logType, 1);
        Trim(maxCount, maxBytes);
        Version++;
    }

    public void Trim(int maxCount, long maxBytes)
    {
        bool changed = false;
        while (entries.Count > 0 && ((maxCount > 0 && entries.Count > maxCount)
            || (maxBytes > 0 && EstimatedBytes > maxBytes)))
        {
            var log = entries.First.Value;
            entries.RemoveFirst();
            EstimatedBytes -= Bytes(log);
            CountSeverity(log.logType, -1);
            var key = new Key(log);
            var group = groups[key];
            if (--group.display.count == 0)
            {
                groups.Remove(key);
                groupOrder.Remove(group.node);
            }
            changed = true;
        }
        if (changed) Version++;
    }

    public void CopyVisible(List<Reporter.Log> target, bool collapse, bool logs, bool warnings, bool errors, string filter)
    {
        target.Clear();
        if (collapse)
        {
            foreach (var group in groupOrder)
                if (Matches(group.display, logs, warnings, errors, filter)) target.Add(group.display);
        }
        else
        {
            foreach (var log in entries)
                if (Matches(log, logs, warnings, errors, filter)) target.Add(log);
        }
    }

    public Reporter.Log GroupFor(Reporter.Log log)
    {
        Group group;
        return log != null && groups.TryGetValue(new Key(log), out group) ? group.display : null;
    }
    public Reporter.Log[] Snapshot() { var result = new Reporter.Log[Count]; entries.CopyTo(result, 0); return result; }
    public void Clear()
    {
        entries.Clear(); groups.Clear(); groupOrder.Clear();
        EstimatedBytes = 0;
        LogCount = WarningCount = ErrorCount = 0;
        Version++;
    }
    private void CountSeverity(Reporter._LogType type, int change)
    {
        if (type == Reporter._LogType.Log) LogCount += change;
        else if (type == Reporter._LogType.Warning) WarningCount += change;
        else ErrorCount += change;
    }
    private static long Bytes(Reporter.Log log) { return 160L + 2L * (log.condition.Length + log.stacktrace.Length); }
    private static bool Matches(Reporter.Log log, bool logs, bool warnings, bool errors, string filter)
    {
        bool enabled = log.logType == Reporter._LogType.Log ? logs : log.logType == Reporter._LogType.Warning ? warnings : errors;
        return enabled && (string.IsNullOrEmpty(filter)
            || log.condition.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
            || log.stacktrace.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}

}