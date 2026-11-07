/* ==========================================================================================
 * ConfigService —— 运行期配置注册中心
 *
 * 【解决的问题】
 *   同一套代码要跑 Dev/Staging/Prod、多个渠道，差异往往只是几个字段。
 *   本服务把配置对象统一登记进来，然后：
 *     1) Register：注册配置对象 + 版本号 + 可选校验器（类型安全）；
 *     2) ApplyOverrides：用 JSON 片段覆盖字段（JsonUtility.FromJsonOverwrite，只覆盖给出的字段）；
 *     3) Validate：启动期统一跑校验，Error 级应该阻断进入游戏。
 *
 * 【为什么不用到处放 ScriptableObject】
 *   SO 适合编辑器配，但运行期无法按环境覆盖。SO 仍可在启动时读出数据后注册进来，
 *   从而获得同样的覆盖 + 校验能力。
 *
 * 【约束】主线程使用（JsonUtility 与 Unity 对象都属主线程）。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>集中式配置注册中心：注册 → 环境/渠道 JSON 覆盖 → 统一校验。主线程使用。</summary>
    public sealed class ConfigService : IConfigService
    {
        /// <summary>一条已注册配置：值用 object 存，读取时按 T 做类型判断。</summary>
        private sealed class Entry
        {
            public string key;
            public object value;
            public int version;                                  // 配置结构版本（外部存档/缓存用它判断兼容性）
            public Action<object, List<ConfigIssue>> validator;   // 已适配成 object 签名的校验器
        }
        /// <summary>key → 配置项。重复注册视为错误（避免后注册静默覆盖前面的）。</summary>
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();

        /// <summary>
        /// 注册一份配置。
        /// <para>validator 形参类型是 T，内部包一层转成 object 签名；校验时若类型不对会在这里抛转换异常。</para>
        /// </summary>
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
        /// <summary>取配置；未注册抛 KeyNotFoundException（尽早暴露拼写/装配错误）。</summary>
        public T Get<T>(string key) where T : class
        {
            if (!TryGet<T>(key, out var value)) throw new KeyNotFoundException("未注册的配置：" + key);
            return value;
        }
        /// <summary>
        /// 尝试取配置。
        /// <para>返回 false 仅表示“没注册”；如果注册了但类型不匹配，直接抛异常（不返回 false），
        /// 因为那属于代码 bug，静默失败只会更难查。</para>
        /// </summary>
        public bool TryGet<T>(string key, out T value) where T : class
        {
            value = null;
            if (key == null || !entries.TryGetValue(key, out var entry)) return false;
            if (entry.value is T typed) { value = typed; return true; }
            throw new InvalidOperationException($"配置 {key} 的类型是 {entry.value.GetType().Name}，与请求的 {typeof(T).Name} 不一致。");
        }
        /// <summary>读取注册时声明的版本号（通常用于存档/缓存兼容性判断）。</summary>
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
                // 覆盖目标没注册：只告警，不抛 —— 覆盖表可能包含其它模块的可选配置
                if (!entries.TryGetValue(pair.Key, out var entry))
                { PLogger.LogWarning("[ConfigService] 覆盖目标未注册：" + pair.Key); continue; }
                if (string.IsNullOrWhiteSpace(pair.Value))
                { PLogger.LogWarning("[ConfigService] 覆盖内容为空：" + pair.Key); continue; }
                // FromJsonOverwrite 只覆盖 JSON 里出现的字段，其它字段保持原值
                try { JsonUtility.FromJsonOverwrite(pair.Value, entry.value); }
                catch (Exception e) { PLogger.LogError($"[ConfigService] 覆盖 {pair.Key} 失败：{e.Message}"); }
            }
        }
        /// <summary>
        /// 跑全部已注册的校验器，汇总问题列表。
        /// <para>校验器自身抛异常会被转成一条 Error 级 issue，而不是让整个启动流程崩掉。</para>
        /// <para>调用方应根据 ConfigIssueLevel.Error 决定是否阻断进入游戏。</para>
        /// </summary>
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
