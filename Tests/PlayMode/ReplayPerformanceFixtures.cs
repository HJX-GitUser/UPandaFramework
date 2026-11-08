using System;
using System.IO;
using UnityEngine;
using UnityEngine.TestTools;

namespace UPandaGF.Tests
{

public class ReplayPerformanceFixtures : IPrebuildSetup, IPostBuildCleanup
{
    private const string Config = "Temp/upanda-replay-fixtures.json";
    [Serializable] public class Settings { public string folder; }
    public static string ControllerPath => JsonUtility.FromJson<Settings>(File.ReadAllText(Config)).folder + "/Replay.controller";
    public void Setup()
    {
#if UNITY_EDITOR
        var config = new Settings { folder = "Assets/ReplayPerformanceFixture_" + Guid.NewGuid().ToString("N") }; Directory.CreateDirectory(config.folder); Directory.CreateDirectory("Temp"); File.WriteAllText(Config, JsonUtility.ToJson(config));
        UnityEditor.AssetDatabase.Refresh(); var controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float); controller.AddParameter("Weight", AnimatorControllerParameterType.Float); var alternate = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(config.folder + "/Alternate.controller"); alternate.AddParameter("Weight", AnimatorControllerParameterType.Float); UnityEditor.AssetDatabase.SaveAssets();
#endif
    }
    public void Cleanup()
    {
#if UNITY_EDITOR
        if (!File.Exists(Config)) return; var config = JsonUtility.FromJson<Settings>(File.ReadAllText(Config));
        if (config.folder.StartsWith("Assets/ReplayPerformanceFixture_", StringComparison.Ordinal) && config.folder.IndexOf('/', 7) < 0 && Path.GetFullPath(config.folder).StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar)) UnityEditor.AssetDatabase.DeleteAsset(config.folder);
        File.Delete(Config);
#endif
    }
}

}