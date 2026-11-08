using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 面板配置 → <c>StreamingAssets/Data/GameRootConfig.json</c> 的自动落盘。
    /// <para>触发点：保存场景（Ctrl+S）与构建前。编辑器 Play 不读这份 Json，
    /// 它只服务于打包后的运行（Player 启动以它为准）。</para>
    /// </summary>
    [InitializeOnLoad]
    internal static class UPGameRootConfigPersistence
    {
        static UPGameRootConfigPersistence()
        {
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        private static void OnSceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            // 运行中的 Config 是启动快照，不是面板值；写回去会把面板配置覆盖成旧值
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var root = FindOpenRoot();
            if (root == null) return;
            Write(root, log: true);
        }

        /// <summary>在已打开的场景里找根节点；Prefab 资源 / Prefab Stage 预览场景不算（它们的 Config 不是启动配置）。</summary>
        internal static UPGameRoot FindOpenRoot()
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<UPGameRoot>())
            {
                if (candidate == null) continue;
                var scene = candidate.gameObject.scene;
                // Prefab 资源、Prefab Stage / Inspector 预览场景都没有路径；保存过的真实场景一定有
                if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) continue;
                return candidate;
            }
            return null;
        }

        /// <summary>把根节点上的面板配置写成 Json；内容没变则跳过写盘。成功返回 true。</summary>
        internal static bool Write(UPGameRoot root, bool log)
        {
            if (root == null) return false;
            try
            {
                string json = UPGameRootConfigFile.ToJson(root.Config);
                string path = UPGameRootConfigFile.AbsolutePath;
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                bool created = !File.Exists(path);
                if (!created && File.ReadAllText(path, Encoding.UTF8) == json) return true;

                // UTF-8 无 BOM：方便手改与版本管理；先写临时文件再覆盖，避免写一半留下坏文件
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                File.Copy(temporary, path, true);
                File.Delete(temporary);

                // 只有新文件需要在 Project 窗口出现；已存在文件的改写不触发导入
                if (created) AssetDatabase.ImportAsset(UPGameRootConfigFile.AssetPath);
                if (log) Debug.Log($"[UPGameRoot] 面板配置已写入 {UPGameRootConfigFile.AssetPath}\n{json}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[UPGameRoot] 写入配置失败：{e}");
                return false;
            }
        }
    }

    /// <summary>
    /// 构建前把面板配置再写一次，避免打出「Json 落后于面板」的包。
    /// <para>写入后按构建目标对齐 bundleTarget / LoadAssetPath（与 <see cref="FrameworkBuildPipeline"/> 一致）。
    /// 构建期的合法性校验仍由 <see cref="UPandaGF.EditorTools.FrameworkConfigBuildValidator"/> 负责。</para>
    /// </summary>
    public sealed class UPGameRootConfigBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -2000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var root = UPGameRootConfigPersistence.FindOpenRoot();
            if (root == null)
            {
                Debug.LogWarning("[UPGameRoot] 构建前未找到已打开场景中的 UPGameRoot，跳过面板配置写入；"
                                 + "将使用现有 " + UPGameRootConfigFile.AssetPath + " 构建。");
                return;
            }
            if (!UPGameRootConfigPersistence.Write(root, log: false)) return;
            // 面板里的 bundleTarget 可能还是旧平台，写完立刻按本次构建目标对齐
            FrameworkBuildPipeline.AlignGameRootConfig(report.summary.platform);
            Debug.Log($"[UPGameRoot] 构建前已把面板配置写入 {UPGameRootConfigFile.AssetPath}");
        }
    }
}
