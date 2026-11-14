using System;

namespace UPandaGF
{
    /// <summary>
    /// 一个可用语言（来自 LocalizationCatalog）。Code 使用 BCP-47（如 zh-Hans、en、ja）。
    /// </summary>
    [Serializable]
    public sealed class LocaleInfo
    {
        public string code;
        public string displayName;
        /// <summary>基础表路径（内置表用 Resources 相对路径；项目新语言用项目路径）。</summary>
        public string table;
        /// <summary>项目覆盖表路径；为空表示不覆盖。</summary>
        public string overrideTable;

        public LocaleInfo() { }

        public LocaleInfo(string code, string displayName, string table = null, string overrideTable = null)
        {
            this.code = code;
            this.displayName = displayName;
            this.table = table;
            this.overrideTable = overrideTable;
        }

        public override string ToString() { return code + " (" + displayName + ")"; }
    }
}
