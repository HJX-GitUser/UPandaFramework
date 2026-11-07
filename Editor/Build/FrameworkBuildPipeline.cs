using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 一键构建入口：统一场景来源、版本号注入与输出目录。
    /// 菜单调用与 CI 调用共用同一套方法，避免"本地能出包、CI 出不了"。
    /// </summary>
    public static class FrameworkBuildPipeline
    {
        [MenuItem("UPandaGF/构建/构建 Windows x64")]
        public static void BuildWindows64() => Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Build/Windows/UPandaGF.exe");
        [MenuItem("UPandaGF/构建/构建 WebGL")]
        public static void BuildWebGL() => Build(BuildTarget.WebGL, BuildTargetGroup.WebGL, "Build/WebGL");
        [MenuItem("UPandaGF/构建/构建 Android")]
        public static void BuildAndroid() => Build(BuildTarget.Android, BuildTargetGroup.Android, "Build/Android/UPandaGF.apk");

        /// <summary>CI 入口：UPANDA_VERSION 环境变量存在时注入 PlayerSettings.bundleVersion。</summary>
        public static BuildReport Build(BuildTarget target, BuildTargetGroup group, string relativeOutput)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("Build Settings 中没有启用的场景，无法构建。");

            AlignGameRootConfig(target);

            string version = Environment.GetEnvironmentVariable("UPANDA_VERSION");
            if (!string.IsNullOrWhiteSpace(version)) PlayerSettings.bundleVersion = version;

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string output = Path.Combine(projectRoot, relativeOutput);
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
                Debug.Log($"[FrameworkBuild] {target} 构建成功：{summary.outputPath}，耗时 {summary.totalTime}，大小 {summary.totalSize} 字节。版本 {PlayerSettings.bundleVersion}");
            else
                Debug.LogError($"[FrameworkBuild] {target} 构建失败：{summary.result}，错误 {summary.totalErrors}。详见 Console。");
            return report;
        }

        /// <summary>
        /// 构建目标 → AB 目标目录名。与 AB 打包输出目录 <c>AssetBundles/&lt;BuildTarget&gt;</c>、
        /// 以及运行时 <see cref="UPGameRoot"/> 的 RuntimePlatform 映射保持一致。
        /// </summary>
        public static string BundleTargetName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return "StandaloneWindows64";
                case BuildTarget.Android: return "Android";
                case BuildTarget.iOS: return "iOS";
                case BuildTarget.WebGL: return "WebGL";
                case BuildTarget.StandaloneOSX: return "StandaloneOSX";
                case BuildTarget.StandaloneLinux64: return "StandaloneLinux64";
                default: return target.ToString();
            }
        }

        /// <summary>
        /// 把 GameRootConfig.json 的 bundleTarget / LoadAssetPath 对齐到本次构建目标。
        /// <para>不对齐时，切平台后会连续被 FrameworkConfigBuildValidator 拦下
        /// （bundleTarget 与目标平台不匹配）；即使绕过校验，Player 启动的 Configuration 步骤
        /// 也会因同一项校验失败而进不了 GFLoadedEvent。</para>
        /// <para>构建前由 <see cref="UPGameRootConfigBuildGuard"/> 在写入面板配置后调用。</para>
        /// </summary>
        public static void AlignGameRootConfig(BuildTarget target)
        {
            string path = UPGameRootConfigFile.AbsolutePath;
            if (!File.Exists(path))
                throw new InvalidOperationException("缺少框架启动配置：" + UPGameRootConfigFile.AssetPath
                    + "，请先打开启动场景并保存一次场景（面板会自动写入该文件）。");

            var config = UPGameRootConfigFile.FromJson(File.ReadAllText(path, Encoding.UTF8));
            if (config == null)
                throw new InvalidOperationException("框架启动配置解析失败：" + UPGameRootConfigFile.AssetPath);

            string bundleTarget = BundleTargetName(target);
            bool changed = false;
            if (config.bundleTarget != bundleTarget) { config.bundleTarget = bundleTarget; changed = true; }

            string expectedPath = "AssetBundles/" + bundleTarget + "/";
            if (string.IsNullOrEmpty(config.LoadAssetPath)
                || config.LoadAssetPath.StartsWith("AssetBundles/", StringComparison.Ordinal))
            {
                if (config.LoadAssetPath != expectedPath) { config.LoadAssetPath = expectedPath; changed = true; }
            }
            else
            {
                Debug.LogWarning($"[FrameworkBuild] LoadAssetPath 为自定义路径「{config.LoadAssetPath}」，"
                    + $"请自行确认它与 {bundleTarget} 的 AB 产物一致。");
            }

            ValidateAssetBundleOutput(config);

            if (!changed) return;
            File.WriteAllText(path, UPGameRootConfigFile.ToJson(config), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(UPGameRootConfigFile.AssetPath);
            Debug.Log($"[FrameworkBuild] 已把 {UPGameRootConfigFile.AssetPath} 对齐到构建目标 {bundleTarget}"
                + $"（LoadAssetPath={config.LoadAssetPath}）。");
        }

        /// <summary>
        /// 构建 Player 前确认随包 AB 已生成：AB 模式下 LoadAssetPath 指向 StreamingAssets 内的目录，
        /// 目录缺失或没有 .manifest 时直接报错，避免出包后才发现“Player 找不到 AB”。
        /// <para>RemotePath 不检查本地目录；Editor 直读模式不要求 AB。</para>
        /// </summary>
        public static void ValidateAssetBundleOutput(UPGameRootConfig config)
        {
            if (config == null || config.method != AssetLoaddingMethod.Assetbundles) return;
            if (config.AssetAESConfig != null && config.AssetAESConfig.mainBundleLoadPath == ABLoadPath.RemotePath)
            {
                Debug.Log("[FrameworkBuild] 主包来源为 RemotePath，跳过随包 AB 目录检查。");
                return;
            }

            string directory = Path.Combine(Application.streamingAssetsPath, config.LoadAssetPath ?? string.Empty);
            if (!Directory.Exists(directory))
            {
                throw new InvalidOperationException(
                    "AssetBundle 产物目录不存在：" + directory
                    + "\n请先在 UPandaGF → AB包工具 → AssetBundle Browser 中执行 Build All，"
                    + "并确认目标平台与 bundleTarget=" + config.bundleTarget + " 一致。");
            }
            if (Directory.GetFiles(directory, "*.manifest", SearchOption.TopDirectoryOnly).Length == 0)
            {
                throw new InvalidOperationException(
                    "AssetBundle 产物目录中没有 .manifest：" + directory
                    + "\n目录可能是旧构建或只拷贝了部分包，请重新执行 Build All。");
            }
        }
    }
}
