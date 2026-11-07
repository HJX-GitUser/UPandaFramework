using System.IO;
using System.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.EditorTools
{
    public sealed class FrameworkConfigBuildValidator : IProcessSceneWithReport
    {
        public int callbackOrder => -1000;
        public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, BuildReport report)
        {
            if (report == null || !scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<UPGameRoot>(true) != null)) return;
            string path = UPGameRootConfigFile.AbsolutePath;
            if (!File.Exists(path)) throw new BuildFailedException("缺少框架启动配置：" + path);
            try
            {
                var config = UPGameRootConfigFile.FromJson(File.ReadAllText(path));
                UPGameRootConfigFile.SnapshotAndValidate(config, false, UPandaGF.GFEditor.FrameworkBuildPipeline.BundleTargetName(report.summary.platform));
            }
            catch (System.Exception e) { throw new BuildFailedException("框架发布配置校验失败：" + e.Message); }
        }
        [MenuItem("UPandaGF/框架/校验启动配置")]
        public static void ValidateConfiguration()
        {
            var config = UPGameRootConfigFile.FromJson(File.ReadAllText(UPGameRootConfigFile.AbsolutePath));
            var errors = UPGameRootConfigFile.Validate(config, true);
            if (errors.Count > 0) throw new System.IO.InvalidDataException(string.Join("\n", errors));
            Debug.Log("框架启动配置校验通过。");
        }
    }
}
