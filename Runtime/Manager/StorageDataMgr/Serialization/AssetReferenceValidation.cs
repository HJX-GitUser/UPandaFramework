using System;
using System.IO;

namespace UPandaGF
{
    /// <summary>生成端与读取端共用的清单结构检查。</summary>
    public static class AssetReferenceValidation
    {
        public static void Validate(ABSourcesRelated data)
        {
            if (data == null || data.mainBundleInfo == null || data.bundleInfo == null || data.sourcesDic == null) throw new InvalidDataException("AB 清单结构不完整");
            Bundle(data.mainBundleInfo);
            foreach (var pair in data.bundleInfo)
            {
                Bundle(pair.Value);
                if (pair.Key != pair.Value.bundleName) throw new InvalidDataException("AB 清单包键与名称不匹配");
            }
            foreach (var pair in data.sourcesDic)
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null || string.IsNullOrEmpty(pair.Value.sourceName)
                    || string.IsNullOrEmpty(pair.Value.bundleName) || !data.bundleInfo.ContainsKey(pair.Value.bundleName))
                    throw new InvalidDataException("AB 清单资源引用无效：" + pair.Key);
        }
        private static void Bundle(AssetBundleLoadInfo bundle)
        {
            if (bundle == null || string.IsNullOrWhiteSpace(bundle.bundleName) || bundle.size < 0 || !Enum.IsDefined(typeof(ABLoadPath), bundle.loadPath))
                throw new InvalidDataException("AB 包信息无效");
            string name = bundle.bundleName.Replace('\\', '/');
            if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains(":") || Array.Exists(name.Split('/'), part => part == ".." || part == "." || part.Length == 0))
                throw new InvalidDataException("AB 包路径无效：" + name);
            // MD5 仅对提供了值的包检查；旧 Editor 直读配置可没有摘要。
            if (!string.IsNullOrEmpty(bundle.md5) && (bundle.md5.Length != 32 || !System.Text.RegularExpressions.Regex.IsMatch(bundle.md5, "^[0-9a-fA-F]+$")))
                throw new InvalidDataException("AB 包 MD5 无效");
        }
    }
}
