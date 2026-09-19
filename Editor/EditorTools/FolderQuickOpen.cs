using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.EditorTools
{
    /// <summary>
    /// 快速打开 Unity 常用路径的文件夹（Windows 下用资源管理器打开，其他平台回退到 RevealInFinder）。
    /// 每次打开的同时会把路径复制到剪贴板，方便粘贴使用。
    /// </summary>
    public static class FolderQuickOpen
    {
        private const string RootMenu = "UPandaGF/Tools/打开常用路径/";

        /// <summary>
        /// 项目根目录（Assets 的上一级）。
        /// </summary>
        private static string ProjectRootPath
        {
            get
            {
                DirectoryInfo parent = Directory.GetParent(Application.dataPath);
                return parent != null ? parent.FullName : Application.dataPath;
            }
        }

        [MenuItem(RootMenu + "项目根目录", false, 1)]
        private static void OpenProjectRoot()
        {
            OpenFolder(ProjectRootPath);
        }

        [MenuItem(RootMenu + "Assets 目录(DataPath)", false, 2)]
        private static void OpenDataPath()
        {
            OpenFolder(Application.dataPath);
        }

        [MenuItem(RootMenu + "StreamingAssets 目录", false, 3)]
        private static void OpenStreamingAssets()
        {
            OpenFolder(Application.streamingAssetsPath);
        }

        [MenuItem(RootMenu + "persistentDataPath", false, 4)]
        private static void OpenPersistentDataPath()
        {
            OpenFolder(Application.persistentDataPath);
        }

        [MenuItem(RootMenu + "temporaryCachePath(临时缓存)", false, 5)]
        private static void OpenTemporaryCachePath()
        {
            OpenFolder(Application.temporaryCachePath);
        }

        [MenuItem(RootMenu + "AssetBundles(AB打包输出目录)", false, 6)]
        private static void OpenABPackage()
        {
            OpenFolder(Path.Combine(ProjectRootPath, "AssetBundles"));
        }

        [MenuItem(RootMenu + "全部打开", false, 50)]
        private static void OpenAll()
        {
            OpenFolder(ProjectRootPath);
            OpenFolder(Application.dataPath);
            OpenFolder(Application.streamingAssetsPath);
            OpenFolder(Application.persistentDataPath);
            OpenFolder(Application.temporaryCachePath);
            OpenFolder(Path.Combine(ProjectRootPath, "AssetBundles"));
        }

        /// <summary>
        /// 打开指定文件夹；不存在则先创建（persistentDataPath 等在编辑器里可能还没有）。
        /// </summary>
        private static void OpenFolder(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogWarning("[FolderQuickOpen] 路径为空，无法打开。");
                    return;
                }

                // 目录不存在则创建，保证一定能打开
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                // 顺带复制到剪贴板，方便在代码/命令行里粘贴
                EditorGUIUtility.systemCopyBuffer = path;

#if UNITY_EDITOR_WIN
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
#else
                EditorUtility.RevealInFinder(path);
#endif
                Debug.Log($"[FolderQuickOpen] 已打开: {path}\n(路径已复制到剪贴板)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FolderQuickOpen] 打开文件夹失败: {path}\n{e}");
            }
        }
    }
}
