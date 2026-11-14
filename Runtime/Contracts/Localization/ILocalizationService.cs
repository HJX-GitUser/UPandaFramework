using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>
    /// 本地多语言服务：加载语言目录与字符串表、切换语言、按 key 取文案、缺失回退。
    /// <para>语言由设置项 <c>language.ui</c> 驱动（空字符串 = 跟随系统）。切换语言不重启、不重新加载场景。</para>
    /// <para>主线程使用；表在启动时通过 <see cref="LoadAsync"/> 预加载（P0）。</para>
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>是否已完成加载（加载失败也会置 true，此时使用兜底 key/代码文案）。</summary>
        bool IsLoaded { get; }
        /// <summary>当前实际生效的 locale（已解析，不会是空）。</summary>
        string CurrentLocale { get; }
        /// <summary>当前语言对应的 CultureInfo；解析失败时返回 InvariantCulture。</summary>
        CultureInfo Culture { get; }
        /// <summary>可用语言列表（加载成功且有表的语言）。</summary>
        IReadOnlyList<LocaleInfo> Locales { get; }

        /// <summary>语言切换成功。</summary>
        event Action<LocalizationChange> LocaleChanged;
        /// <summary>加载完成（可能带着失败兜底）。</summary>
        event Action Loaded;
        /// <summary>加载失败（已回退；参数为错误信息）。</summary>
        event Action<string> LoadFailed;

        /// <summary>加载 catalog 与表；重复调用无副作用。失败时不抛异常（取消除外）。</summary>
        Task LoadAsync(CancellationToken token = default);

        /// <summary>切换到已加载的语言；未加载/不存在返回 false，不改变当前语言。</summary>
        bool SetLocale(string locale);

        /// <summary>按 key 取文案；缺失时按配置策略返回（默认返回 key）。</summary>
        string Get(string key);

        /// <summary>按 key 取文案并用当前语言 Culture 格式化（string.Format）。</summary>
        string Get(string key, params object[] args);

        /// <summary>尝试取文案；缺失返回 false。</summary>
        bool TryGet(string key, out string value);

        /// <summary>格式化指定 key；参数不匹配时返回原模板并记录日志。</summary>
        string Format(string key, params object[] args);

        /// <summary>当前语言或 fallback/default 中是否存在该 key。</summary>
        bool HasKey(string key);
    }
}
