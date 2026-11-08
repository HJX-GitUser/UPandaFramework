using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 编辑器破坏性操作护栏：删除/覆盖前统一确认，并拒绝工程根、Assets 根、源目录与目标目录互相包含等危险路径。
    /// <para>只负责“拦截与确认”，不执行删除；调用方仍需自行保证只删自己生成的文件。</para>
    /// </summary>
    public static class EditorDestructiveGuard
    {
        /// <summary>
        /// 删除/清空目录前的确认。返回 false 表示调用方必须终止操作。
        /// </summary>
        public static bool ConfirmDelete(string title, string targetPath, string detail, string sourcePath = null)
        {
            string target = Normalize(targetPath);
            if (string.IsNullOrEmpty(target))
            {
                EditorUtility.DisplayDialog(title, "目标路径为空，已取消。", "确定");
                return false;
            }

            if (IsProjectOrAssetsRoot(target))
            {
                EditorUtility.DisplayDialog(title,
                    "拒绝对工程根目录或 Assets 根目录执行清空操作：\n" + target, "确定");
                return false;
            }

            if (!string.IsNullOrEmpty(sourcePath))
            {
                string source = Normalize(sourcePath);
                if (PathsOverlap(source, target))
                {
                    EditorUtility.DisplayDialog(title,
                        "源目录与目标目录不能相同或互相包含：\n源：" + source + "\n目标：" + target, "确定");
                    return false;
                }
            }

            string message = string.IsNullOrEmpty(detail) ? "确定要清空该目录吗？" : detail;
            return EditorUtility.DisplayDialog(title, message + "\n\n目标：" + target, "继续清空", "取消");
        }

        /// <summary>覆盖已有文件前的确认。</summary>
        public static bool ConfirmOverwrite(string title, string filePath)
        {
            string path = Normalize(filePath);
            if (string.IsNullOrEmpty(path)) return false;
            if (!File.Exists(path)) return true;
            return EditorUtility.DisplayDialog(title, "文件已存在，确定覆盖吗？\n\n" + path, "覆盖", "取消");
        }

        /// <summary>路径是否在 Assets 内（用于拒绝把生成物写到工程外）。</summary>
        public static bool IsInsideAssets(string path)
        {
            string full = Normalize(path);
            return IsWithin(full, Normalize(Application.dataPath));
        }

        /// <summary>
        /// 覆盖目录内同名文件前的确认。目标目录必须存在；文件数为 0 时直接返回 true。
        /// </summary>
        public static bool ConfirmOverwriteDirectory(string title, string targetDirectory, int fileCount, string detail = null)
        {
            string target = Normalize(targetDirectory);
            if (string.IsNullOrEmpty(target)) return false;
            if (fileCount <= 0) return true;
            if (IsProjectOrAssetsRoot(target))
            {
                EditorUtility.DisplayDialog(title, "拒绝对工程根目录或 Assets 根目录执行覆盖操作：\n" + target, "确定");
                return false;
            }
            string message = string.IsNullOrEmpty(detail) ? $"目标目录内 {fileCount} 个同名文件将被覆盖。" : detail;
            return EditorUtility.DisplayDialog(title, message + "\n\n目标：" + target, "覆盖", "取消");
        }

        /// <summary>
        /// 上传/发布前确认：目标地址非空且为 ftp/http(s) 协议，并明确告知会覆盖远端同名文件。
        /// </summary>
        public static bool ConfirmUpload(string title, string remoteUrl, int fileCount, bool includesManifest)
        {
            if (string.IsNullOrWhiteSpace(remoteUrl))
            {
                EditorUtility.DisplayDialog(title, "上传地址为空，已取消。", "确定");
                return false;
            }
            string url = remoteUrl.Trim();
            bool supported = url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("ftps://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            if (!supported)
            {
                EditorUtility.DisplayDialog(title, "只支持 ftp / ftps / http / https 上传地址：\n" + url, "确定");
                return false;
            }
            if (fileCount <= 0)
            {
                EditorUtility.DisplayDialog(title, "没有可上传的文件，已取消。", "确定");
                return false;
            }
            string message = "将上传 " + fileCount + " 个文件到：\n" + url
                + "\n\n远端同名文件会被覆盖。"
                + (includesManifest ? "\n清单会在 AB 包全部上传成功后最后提交。" : string.Empty);
            return EditorUtility.DisplayDialog(title, message, "开始上传", "取消");
        }

        public static bool IsWithin(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root)) return false;
            string p = Normalize(path);
            string r = Normalize(root);
            if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
            return p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public static bool PathsOverlap(string a, string b)
        {
            return IsWithin(a, b) || IsWithin(b, a);
        }

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        private static bool IsProjectOrAssetsRoot(string fullPath)
        {
            string assets = Normalize(Application.dataPath);
            string project = Normalize(Path.GetDirectoryName(Application.dataPath));
            return string.Equals(fullPath, assets, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fullPath, project, StringComparison.OrdinalIgnoreCase);
        }
    }
}
