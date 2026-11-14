using System;

namespace UPandaGF
{
    /// <summary>缺失 key 时的返回策略。</summary>
    public enum LocalizationMissingPolicy
    {
        /// <summary>返回 key 本身（便于定位缺翻译）。</summary>
        ShowKey,
        /// <summary>返回空串。</summary>
        Empty,
        /// <summary>返回 fallback/default 语言的值；仍缺失时返回 key。</summary>
        Fallback
    }

    /// <summary>
    /// 本地化模块的开发者配置（随 UPGameRootConfig 写入 GameRootConfig.json）。
    /// <para>内置表固定在包内 Resources；项目表路径为空时只使用内置 zh-Hans/en。</para>
    /// </summary>
    [Serializable]
    public sealed class LocalizationSettings
    {
        /// <summary>内置 catalog 的 Resources 路径（无扩展名）。</summary>
        public string builtInCatalogPath = "Localization/catalog";

        /// <summary>项目 catalog 路径；为空表示不加载项目覆盖表。</summary>
        public string projectCatalogPath = string.Empty;

        /// <summary>项目 catalog/表的加载方式（内置表始终用 Resources）。</summary>
        public AssetLoadMethod projectLoadMethod = AssetLoadMethod.Resources;

        /// <summary>启动时预加载全部语言表（P0 默认 true；懒加载是 P1）。</summary>
        public bool preloadAll = true;

        /// <summary>缺失 key 是否每个 key 只警告一次。</summary>
        public bool logMissingKeys = true;

        /// <summary>缺失 key 的返回策略。</summary>
        public LocalizationMissingPolicy missingPolicy = LocalizationMissingPolicy.ShowKey;
    }
}
