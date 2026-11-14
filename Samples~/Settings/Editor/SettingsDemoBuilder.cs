// =============================================================================
//  SettingsDemoBuilder.cs —— 系统设置示例场景一键生成器
//
//  菜单：UPandaGF/示例/创建设置系统示例场景
//
//  生成内容：
//    · UPGameRoot（Editor 资源模式，启动时会执行 Settings 步骤）
//    · Main Camera / Directional Light / Cube（旋转物体）
//    · SettingsDemoController（打开设置面板、推荐设置、恢复默认、消费示例）
//
//  场景保存到 Assets/UPandaFrameworkDemos/Settings/SettingsDemo.unity，
//  不会写进只读的包目录。
// =============================================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UPandaGF;
using UPandaGF.Samples;

namespace UPandaGF.GFEditor
{
    /// <summary>系统设置示例场景生成器。</summary>
    public static class SettingsDemoBuilder
    {
        private const string MenuPath = "UPandaGF/示例/创建设置系统示例场景";
        private const string SceneFolder = "Assets/UPandaFrameworkDemos/Settings";
        private const string SceneName = "SettingsDemo.unity";

        [MenuItem(MenuPath, false, 30)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Camera camera = CreateCamera();
            Light light = CreateLight();
            Transform cube = CreateCube();
            CreateGameRoot();
            CreateDemoController(camera, light, cube);

            EditorAssetPathUtility.EnsureFolder(SceneFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(SceneFolder + "/" + SceneName);
            bool saved = EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();

            if (saved)
            {
                Debug.Log("[SettingsDemo] 示例场景已创建：" + path +
                          "\nPlay 后：Console 等待 GameRoot 初始化完成 → 左上角按钮「打开设置」。" +
                          "\n提示：设置面板与 UI 由 UPGameRoot/UIManager 自动创建，场景里不需要手动搭 Canvas。");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(path));
            }
            else
            {
                Debug.LogError("[SettingsDemo] 场景保存失败：" + path);
            }
        }

        private static Camera CreateCamera()
        {
            GameObject go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.4f, -4.5f), Quaternion.Euler(8f, 0f, 0f));
            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
            go.AddComponent<AudioListener>();
            return camera;
        }

        private static Light CreateLight()
        {
            GameObject go = new GameObject("Directional Light");
            go.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
            return light;
        }

        private static Transform CreateCube()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "SpinCube";
            cube.transform.position = new Vector3(0f, 0.6f, 0f);
            cube.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            Renderer renderer = cube.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(0.30f, 0.62f, 0.95f, 1f);
            return cube.transform;
        }

        private static UPGameRoot CreateGameRoot()
        {
            GameObject go = new GameObject("UPGameRoot");
            // AddComponent 会触发 UPGameRoot.Reset()/SetComponent()，自动补齐管理器与 UI 子树。
            UPGameRoot root = go.AddComponent<UPGameRoot>();
            root.Config.method = AssetLoaddingMethod.Editor;
            root.Config.enableAssetUpdate = false;
            root.Config.EnableDebugModel = false;
            root.Config.LoadAssetPath = "AssetBundles/StandaloneWindows64/";
            root.Config.remoteURL = "http://127.0.0.1:80/";
            root.Config.AssetAESConfig = new AssetBundleClassificationWindowConfig
            {
                enable = false,
                AESKEY = "111a222aaabbbccc",
                AESIV = "111b222aaabbbccc",
                mainBundleLoadPath = ABLoadPath.StreamingAssetsPath
            };
            return root;
        }

        private static void CreateDemoController(Camera camera, Light light, Transform cube)
        {
            GameObject go = new GameObject("SettingsDemo");
            SettingsDemoController demo = go.AddComponent<SettingsDemoController>();
            demo.demoCamera = camera;
            demo.demoLight = light;
            demo.spinTarget = cube;
        }
    }
}
