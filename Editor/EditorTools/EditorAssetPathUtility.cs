// =============================================================================
//  EditorAssetPathUtility.cs —— 编辑器"落盘路径"兜底工具
// -----------------------------------------------------------------------------
//  解决的问题：编辑器脚本在创建场景 / 资产 / 文本文件之前，都得先把目标目录建出来。
//  过去各模块各写一套，主要是两个坑：
//    1) AssetDatabase.CreateFolder 只建一层，父目录不存在时直接失败
//       （Console 只有一句 "Parent folder not found"），后面的 CreateAsset /
//       SaveScene / SaveAsPrefabAsset 会跟着一起失败；
//    2) Directory.Exists("Assets/xxx") 用的是相对路径，依赖 Editor 的当前工作目录，
//       CWD 一变就会误判成"目录不存在"。
//
//  本类提供统一的、逐级创建的实现，同时支持两类路径：
//    · 工程相对路径："Assets/A/B/C"（走 AssetDatabase，会生成 .meta）
//    · 绝对磁盘路径：Application.streamingAssetsPath、"D:/xx/yy"（走 Directory）
//
//  用法：
//    EditorAssetPathUtility.EnsureFolder("Assets/A/B/C");
//    EditorAssetPathUtility.EnsureFolderForAsset("Assets/A/B/C/x.asset");
//    EditorAssetPathUtility.EnsureAbsoluteDirectory(Application.streamingAssetsPath + "/AB");
//    string assetPath = EditorAssetPathUtility.ToAssetFolder(savePathFromInspector);
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 编辑器资源路径工具：在写资产 / 存场景 / 落盘之前，保证目标目录一定存在。
    /// </summary>
    public static class EditorAssetPathUtility
    {
        /// <summary>工程内资源根目录名（Unity 只把它下面的内容当资产）。</summary>
        public const string AssetsFolderName = "Assets";

        /// <summary>工程根目录的绝对路径（即 Assets 的上一级）。</summary>
        public static string ProjectRootPath
        {
            get { return Normalize(Directory.GetParent(Application.dataPath).FullName); }
        }

        /// <summary>
        /// 统一路径写法：反斜杠转正斜杠、合并重复斜杠、去掉末尾斜杠。
        /// 不做 Trim，以免破坏本身带空格的目录名。
        /// </summary>
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string result = path.Replace('\\', '/');

            int searchStart = result.StartsWith("//") ? 2 : 0;   // 保留 UNC 前缀 "//"
            int index = result.IndexOf("//", searchStart);
            while (index >= 0)
            {
                result = result.Substring(0, index) + result.Substring(index + 1);
                index = result.IndexOf("//", searchStart);
            }

            if (result.Length > 1)
            {
                result = result.TrimEnd('/');
            }
            return result;
        }

        /// <summary>是否是 "Assets/..." 形式的工程相对路径。</summary>
        public static bool IsAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path))
            {
                return false;
            }

            string normalized = Normalize(path);
            return normalized == AssetsFolderName || normalized.StartsWith(AssetsFolderName + "/");
        }

        /// <summary>把 "Assets/xx" 转成绝对路径；本来就是绝对路径则只做规范化。</summary>
        public static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string normalized = Normalize(path);
            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }
            return Normalize(Path.Combine(ProjectRootPath, normalized));
        }

        /// <summary>
        /// 把任意输入（"Assets/A"、绝对路径、"A/B"）收敛成工程相对路径（如 "Assets/A"）。
        /// 落在工程目录之外时返回 null。
        /// </summary>
        public static string ToProjectRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            string normalized = Normalize(path);
            if (IsAssetPath(normalized))
            {
                return normalized;
            }

            string projectRoot = ProjectRootPath;
            string full = Path.IsPathRooted(normalized)
                ? Normalize(Path.GetFullPath(normalized))
                : Normalize(Path.GetFullPath(Path.Combine(projectRoot, normalized)));

            if (full == projectRoot)
            {
                return AssetsFolderName;
            }
            if (full.StartsWith(projectRoot + "/"))
            {
                return full.Substring(projectRoot.Length + 1);
            }
            return null;   // 工程外
        }

        /// <summary>
        /// 把用户输入的目录收敛成 "Assets/..." 形式；不在 Assets 下（含工程外）时返回 null。
        /// 适合用在 Inspector 文本框 / EditorWindow 输入框拿到的路径上。
        /// </summary>
        public static string ToAssetFolder(string path)
        {
            string relative = ToProjectRelativePath(path);
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }
            return IsAssetPath(relative) ? relative : null;
        }

        /// <summary>
        /// 确保工程内目录存在（形如 "Assets/A/B/C"，逐级创建）。
        /// 返回实际可用的路径；传入绝对磁盘路径时自动退化成 <see cref="EnsureAbsoluteDirectory"/>。
        /// </summary>
        public static string EnsureFolder(string projectRelativeFolder)
        {
            if (string.IsNullOrEmpty(projectRelativeFolder))
            {
                return string.Empty;
            }

            string folder = Normalize(projectRelativeFolder);

            // 绝对路径：AssetDatabase 管不着，直接建磁盘目录
            if (Path.IsPathRooted(folder))
            {
                return EnsureAbsoluteDirectory(folder);
            }

            string[] parts = folder.Split('/');
            if (parts[0] != AssetsFolderName)
            {
                // 不是 Assets 下的目录，做不成资产目录，退化成磁盘目录（并提示）
                Debug.LogWarning($"[EditorAssetPathUtility] 路径不在 Assets 下，已按磁盘目录创建：{folder}");
                return EnsureAbsoluteDirectory(ToAbsolutePath(folder));
            }

            // 逐级创建：AssetDatabase.CreateFolder 只建一层，父目录缺失会失败
            string current = AssetsFolderName;
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i]))
                {
                    continue;
                }

                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        Debug.LogError($"[EditorAssetPathUtility] 创建目录失败：{next}（父目录：{current}）");
                        return current;   // 返回已建成的层级，避免调用方用错路径
                    }
                }
                current = next;
            }
            return current;
        }

        /// <summary>
        /// 确保某个资产文件的父目录存在，返回规范化后的资产路径。
        /// 例：EnsureFolderForAsset("Assets/A/B/x.asset") → 建好 Assets/A/B 并返回原路径。
        /// </summary>
        public static string EnsureFolderForAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                return string.Empty;
            }

            string path = Normalize(assetPath);
            int index = path.LastIndexOf('/');
            if (index <= 0)
            {
                return path;   // 没有目录部分（如 "x.asset"），交给调用方自行判断
            }

            EnsureFolder(path.Substring(0, index));
            return path;
        }

        /// <summary>确保绝对磁盘目录存在（Directory.CreateDirectory 会自动补齐所有父目录）。</summary>
        public static string EnsureAbsoluteDirectory(string absoluteDirectory)
        {
            if (string.IsNullOrEmpty(absoluteDirectory))
            {
                return string.Empty;
            }

            string full = Path.GetFullPath(Normalize(absoluteDirectory));
            if (!Directory.Exists(full))
            {
                Directory.CreateDirectory(full);
            }
            return Normalize(full);
        }

        /// <summary>文件所在目录不存在就建出来（用于 File.WriteAllText / WriteAllBytes 之前）。</summary>
        public static string EnsureAbsoluteDirectoryForFile(string absoluteFilePath)
        {
            if (string.IsNullOrEmpty(absoluteFilePath))
            {
                return string.Empty;
            }

            string full = Path.GetFullPath(Normalize(absoluteFilePath));
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir))
            {
                EnsureAbsoluteDirectory(dir);
            }
            return full;
        }
    }
}
