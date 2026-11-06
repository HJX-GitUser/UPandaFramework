using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>集中式配置注册中心：注册 → 环境/渠道 JSON 覆盖 → 统一校验。主线程使用。</summary>
    public sealed class ConfigService : IConfigService
    {
        private sealed class Entry
        {
            public string key;
            public object value;
            public int version;
            public Action<object, List<ConfigIssue>> validator;
        }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        public void Register<T>(string key, T value, int version = 1, Action<T, List<ConfigIssue>> validator = null) where T : class
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("配置 key 不能为空。", nameof(key));
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (entries.ContainsKey(key)) throw new InvalidOperationException("配置重复注册：" + key);
            entries.Add(key, new Entry
            {
                key = key,
                value = value,
                version = version,
                validator = validator == null ? null : (Action<object, List<ConfigIssue>>)((obj, issues) => validator((T)obj, issues))
            });
        }
        public T Get<T>(string key) where T : class
        {
            if (!TryGet<T>(key, out var value)) throw new KeyNotFoundException("未注册的配置：" + key);
            return value;
        }
        public bool TryGet<T>(string key, out T value) where T : class
        {
            value = null;
            if (key == null || !entries.TryGetValue(key, out var entry)) return false;
            if (entry.value is T typed) { value = typed; return true; }
            throw new InvalidOperationException($"配置 {key} 的类型是 {entry.value.GetType().Name}，与请求的 {typeof(T).Name} 不一致。");
        }
        public int GetVersion(string key)
        {
            if (key == null || !entries.TryGetValue(key, out var entry)) throw new KeyNotFoundException("未注册的配置：" + key);
            return entry.version;
        }

        /// <summary>用 JSON 覆盖已注册配置的字段（JsonUtility.FromJsonOverwrite），适合 Dev/Staging/Prod 或渠道差异。</summary>
        public void ApplyOverrides(IReadOnlyDictionary<string, string> jsonOverrides)
        {
            if (jsonOverrides == null) return;
            foreach (var pair in jsonOverrides)
            {
                if (!entries.TryGetValue(pair.Key, out var entry))
                { PLogger.LogWarning("[ConfigService] 覆盖目标未注册：" + pair.Key); continue; }
                if (string.IsNullOrWhiteSpace(pair.Value))
                { PLogger.LogWarning("[ConfigService] 覆盖内容为空：" + pair.Key); continue; }
                try { JsonUtility.FromJsonOverwrite(pair.Value, entry.value); }
                catch (Exception e) { PLogger.LogError($"[ConfigService] 覆盖 {pair.Key} 失败：{e.Message}"); }
            }
        }
        public IReadOnlyList<ConfigIssue> Validate()
        {
            var issues = new List<ConfigIssue>();
            foreach (var entry in entries.Values)
            {
                if (entry.validator == null) continue;
                try { entry.validator(entry.value, issues); }
                catch (Exception e) { issues.Add(new ConfigIssue(entry.key, "校验器异常：" + e.Message, ConfigIssueLevel.Error)); }
            }
            return issues;
        }
    }
}
