// =============================================================================
//  OutlineDemoGenerator.cs —— 描边示例场景生成器
//
//  提供两个菜单入口：
//    · 创建示例场景（新建场景）：相机、平行光、地面、遮挡板和 5 种模式的模型一起生成，
//      保存到框架的 Runtime/Game/OutLine/Demo 目录（同名文件自动加序号，不覆盖已有场景）。
//    · 在当前场景追加示例物体：沿用当前场景的相机与灯光，只追加地面、遮挡板和模型。
//
//  每个示例模型挂一个 OutLine 组件，模式逐一对应，方便对照检查遮挡与剪影的差异。
// =============================================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UPandaGF.Gameplay;

namespace UPandaGF.GFEditor
{
    /// <summary>OutLine 描边示例场景生成器。</summary>
    public static class OutlineDemoGenerator
    {
        private const string MenuRoot = "UPandaGF/Runtime/OutLine/";
        private const string SceneFolder = "Assets/upanda-framework/Runtime/Game/OutLine/Demo";
        private const string SceneName = "OutlineDemo.unity";

        private static readonly Color DemoColor = new Color(0.09f, 1f, 0f, 1f);
        private const float DemoWidth = 5f;

        private struct DemoEntry
        {
            public string Name;
            public PrimitiveType Shape;
            public Vector3 Position;
            public Vector3 Scale;
            public OutLine.描边模式 Mode;
            public bool CanFlash;
        }

        // 从左到右排列五行，分别演示五种模式。
        private static readonly DemoEntry[] Entries =
        {
            new DemoEntry { Name = "Silhouette Only",        Shape = PrimitiveType.Sphere,   Position = new Vector3(-4f, 0.5f, 0f), Scale = Vector3.one,                    Mode = OutLine.描边模式.仅剪影,     CanFlash = true  },
            new DemoEntry { Name = "Outline Hidden",         Shape = PrimitiveType.Cylinder, Position = new Vector3(-2f, 0.5f, 0f), Scale = new Vector3(1f, 0.5f, 1f),     Mode = OutLine.描边模式.遮挡描边,     CanFlash = true  },
            new DemoEntry { Name = "Outline All",            Shape = PrimitiveType.Cube,     Position = new Vector3(0f, 0.5f, 0f),  Scale = Vector3.one,                    Mode = OutLine.描边模式.全部描边,     CanFlash = false },
            new DemoEntry { Name = "Outline And Silhouette", Shape = PrimitiveType.Cylinder, Position = new Vector3(2f, 0.5f, 0f),  Scale = new Vector3(1f, 0.5f, 1f),     Mode = OutLine.描边模式.描边与剪影, CanFlash = true  },
            new DemoEntry { Name = "Outline Visible",        Shape = PrimitiveType.Sphere,   Position = new Vector3(4f, 0.5f, 0f),  Scale = Vector3.one,                    Mode = OutLine.描边模式.可见描边,     CanFlash = true  },
        };

        [MenuItem(MenuRoot + "创建示例场景（新建场景）", false, 10)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildContents(includeCameraAndLight: true);

            EditorAssetPathUtility.EnsureFolder(SceneFolder);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(SceneFolder + "/" + SceneName);
            bool saved = EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            if (!saved)
            {
                Debug.LogError("[OutLine] 示例场景保存失败：" + scenePath + "，请检查目录是否可写。");
                return;
            }

            Debug.Log("[OutLine] 示例场景已创建：" + scenePath + "，进入 Play Mode 可对照五种描边模式。");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(scenePath));
        }

        [MenuItem(MenuRoot + "在当前场景追加示例物体", false, 11)]
        public static void AppendToCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[OutLine] 当前没有打开的场景，无法追加示例物体。");
                return;
            }

            BuildContents(includeCameraAndLight: false);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[OutLine] 已向场景 \"" + scene.name + "\" 追加描边示例物体（相机与灯光沿用现有设置）。");
        }

        private static void BuildContents(bool includeCameraAndLight)
        {
            if (includeCameraAndLight)
            {
                CreateCamera();
                CreateLight();
            }

            CreateGround();
            CreateOccluder();

            var root = new GameObject("Outlined Objects");
            Undo.RegisterCreatedObjectUndo(root, "Create OutLine Demo Root");
            foreach (DemoEntry entry in Entries)
            {
                CreateShape(entry, root.transform);
            }
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 2.2f, -10f), Quaternion.Euler(10f, 0f, 0f));
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create OutLine Demo Camera");
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetPositionAndRotation(new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, 30f, 0f));
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            RenderSettings.sun = light;
            Undo.RegisterCreatedObjectUndo(lightObject, "Create OutLine Demo Light");
        }

        private static void CreateGround()
        {
            GameObject ground = CreatePrimitive(PrimitiveType.Plane, "Plane", Vector3.zero, new Vector3(10f, 1f, 10f));
            Undo.RegisterCreatedObjectUndo(ground, "Create OutLine Demo Ground");
        }

        // 一根不透明横梁同时挡住五个模型的一部分，便于比较「可见描边」和「遮挡描边」。
        private static void CreateOccluder()
        {
            GameObject occluder = CreatePrimitive(PrimitiveType.Cube, "Opaque Occluder",
                new Vector3(0f, 0.55f, -1.35f), new Vector3(9f, 0.45f, 0.2f));
            Undo.RegisterCreatedObjectUndo(occluder, "Create OutLine Demo Occluder");
        }

        private static void CreateShape(DemoEntry entry, Transform parent)
        {
            GameObject shape = CreatePrimitive(entry.Shape, entry.Name, entry.Position, entry.Scale);
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = entry.Position;
            shape.transform.localScale = entry.Scale;

            Undo.RegisterCreatedObjectUndo(shape, "Create OutLine Demo Shape");
            OutLine outline = shape.AddComponent<OutLine>();
            outline.OutlineMode = entry.Mode;
            outline.OutlineColor = DemoColor;
            outline.OutlineWidth = DemoWidth;
            outline.CanFlash = entry.CanFlash;
            EditorUtility.SetDirty(shape);
        }

        private static GameObject CreatePrimitive(PrimitiveType type, string name, Vector3 position, Vector3 scale)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetPositionAndRotation(position, Quaternion.identity);
            instance.transform.localScale = scale;

            // 示例里不需要物理碰撞，去掉 CreatePrimitive 自动附加的碰撞体。
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }
            return instance;
        }
    }
}
