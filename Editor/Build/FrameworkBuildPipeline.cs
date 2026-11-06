using System;
using System.IO;
using System.Linq;
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
    }
}
