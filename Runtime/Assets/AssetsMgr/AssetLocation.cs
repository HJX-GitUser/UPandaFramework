using System;
using System.IO;
using System.Linq;

namespace UPandaGF
{
    /// <summary>资源相对路径使用原始文件名；只在 URL 分支转义每个路径段。</summary>
    public static class AssetLocation
    {
        public static bool IsUrl(string root) => !string.IsNullOrEmpty(root) && (root.Contains("://") || root.StartsWith("jar:", StringComparison.OrdinalIgnoreCase));
        public static string Combine(string root, params string[] relative)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            bool url = IsUrl(root); string result = root;
            foreach (string item in relative)
            {
                if (string.IsNullOrEmpty(item)) continue;
                string normalized = item.Replace('\\', '/').Trim('/'); if (normalized.Length == 0) continue;
                var parts = normalized.Split('/');
                if (parts.Any(part => part == ".." || part == "." || part.Contains(":") || part.IndexOf('\0') >= 0)) throw new ArgumentException("资源相对路径无效：" + item);
                if (url) result = result.TrimEnd('/') + "/" + string.Join("/", parts.Select(Uri.EscapeDataString));
                else result = Path.Combine(result, normalized.Replace('/', Path.DirectorySeparatorChar));
            }
            return result;
        }
        public static string AsUrl(string path) => IsUrl(path) ? path : new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }
}
