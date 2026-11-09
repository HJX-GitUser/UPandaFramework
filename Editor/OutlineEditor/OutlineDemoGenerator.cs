using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UPandaGF.Gameplay;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// OutLine 高亮描边示例场景生成器
    /// </summary>
    public static class OutlineDemoGenerator
    {
        // ---------------- 菜单 ----------------

        private const string MenuRoot = "UPandaGF/Runtime/高亮描边/";

        // ---------------- 路径与常量 ----------------

        // 新建场景统一保存在框架 Runtime 下的高亮描边目录。
        private const string DefaultSceneFolder = "Assets/upanda-framework/Runtime/OutLineDemo";
        private const string DefaultSceneName   = "OutlineDemo.unity";

        // 原型里 5 个物体的共用描边参数
        private static readonly Color OutlineColor = new Color(0.08675194f, 1f, 0f, 1f);
        private const float OutlineWidth = 5f;

        /// <summary>一个条目 = 原型场景里的一个描边物体</summary>
        private struct DemoEntry
        {
            public string           Name;       // GameObject 名（与原型一致）
            public PrimitiveType    Shape;      // 图元类型（原型用的是内置 Cube/Cylinder/Sphere）
            public Vector3          Position;   // 父节点下的局部坐标
            public Vector3          Scale;      // 局部缩放
            public OutLine.描边模式 Mode;       // 描边模式
            public bool             CanFlash;   // 是否呼吸闪烁
        }

        // 按 X 轴从左到右排列（-4 → 4），与原型布局一致
        private static readonly DemoEntry[] DemoEntries =
        {
            new DemoEntry { Name = "Silhouette Only",        Shape = PrimitiveType.Sphere,   Position = new Vector3(-4f, 0.5f, 0f), Scale = Vector3.one,               Mode = OutLine.描边模式.仅剪影,       CanFlash = true  },
            new DemoEntry { Name = "Outline Hidden",         Shape = PrimitiveType.Cylinder, Position = new Vector3(-2f, 0.5f, 0f), Scale = new Vector3(1f, 0.5f, 1f), Mode = OutLine.描边模式.遮挡描边,        CanFlash = true  },
            new DemoEntry { Name = "Outline All",            Shape = PrimitiveType.Cube,     Position = new Vector3( 0f, 0.5f, 0f), Scale = Vector3.one,               Mode = OutLine.描边模式.全部描边,           CanFlash = false },
            new DemoEntry { Name = "Outline And Silhouette", Shape = PrimitiveType.Cylinder, Position = new Vector3( 2f, 0.5f, 0f), Scale = new Vector3(1f, 0.5f, 1f), Mode = OutLine.描边模式.描边与剪影, CanFlash = true  },
            new DemoEntry { Name = "Outline Visible",        Shape = PrimitiveType.Sphere,   Position = new Vector3( 4f, 0.5f, 0f), Scale = Vector3.one,               Mode = OutLine.描边模式.可见描边,       CanFlash = true  },
        };

        // =====================================================================
        //  菜单入口 1：新建场景
        // =====================================================================

        [MenuItem(MenuRoot + "创建描边示例场景（新建场景）", false, 10)]
        public static void CreateDemoScene()
        {
            // 当前场景有未保存修改时先问一句；用户取消就中止
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 空场景：相机/灯光/地面/示例物体全部由脚本按原型参数创建
            BuildSceneContents(includeCameraAndLight: true);

            // 落盘（同名文件自动加序号，绝不覆盖已有场景）
            EnsureFolder(DefaultSceneFolder);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(DefaultSceneFolder + "/" + DefaultSceneName);
            bool saved = EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            if (saved)
            {
                Debug.Log("[高亮描边] 示例场景已创建：" + scenePath +
                          "（相机/平行光/地面/遮挡物 + 5 种描边模式各一个物体，直接按 Play 即可查看效果）");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath));
            }
            else
            {
                Debug.LogError("[高亮描边] 场景保存失败：" + scenePath + "，请检查目录是否可写。");
            }
        }

        // =====================================================================
        //  菜单入口 2：追加到当前场景（相机/灯光沿用当前的）
        // =====================================================================

        [MenuItem(MenuRoot + "在当前场景追加示例物体", false, 11)]
        public static void AppendToCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[高亮描边] 当前没有打开的场景，无法追加。");
                return;
            }

            BuildSceneContents(includeCameraAndLight: false);
            EditorSceneManager.MarkSceneDirty(scene);   // 标记为已修改，便于 Ctrl+S 保存

            Debug.Log("[高亮描边] 已向场景 \"" + scene.name +
                      "\" 追加地面、遮挡物和 5 个描边物体（相机与灯光沿用场景里现有的）。");
        }

        // =====================================================================
        //  场景内容构建
        // =====================================================================

        /// <param name="includeCameraAndLight">
        /// 新建空场景时为 true（把相机和灯光一起造出来）；
        /// 追加到已有场景时为 false（避免和现有相机/灯光重复）。
        /// </param>
        private static void BuildSceneContents(bool includeCameraAndLight)
        {
            if (includeCameraAndLight)
            {
                CreateMainCamera();
                CreateDirectionalLight();
            }

            CreateGroundPlane();
            CreateOccluder();

            // 5 个描边物体统一挂在这个空父节点下（原型里也是这样，便于整体缩放/移动）
            GameObject root = CreateEmptyObject("Outlined Objects", Vector3.zero);

            foreach (DemoEntry entry in DemoEntries)
            {
                CreateOutlinedShape(entry, root.transform);
            }
        }

        /// <summary>主相机：pos(0,2.2,-10)、俯角 10°（FOV/裁剪面用 Camera 组件默认值）</summary>
        private static void CreateMainCamera()
        {
            GameObject cameraGo = CreateEmptyObject("Main Camera", new Vector3(0f, 2.2f, -10f));
            cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";
            cameraGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        }

        /// <summary>平行光：pos(0,3,0)、rot(50,30,0)、软阴影，并登记为环境光的“太阳”</summary>
        private static void CreateDirectionalLight()
        {
            GameObject lightGo = CreateEmptyObject("Directional Light", new Vector3(0f, 3f, 0f));
            Light sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            // 原型 RenderSettings.m_Sun 就指向这盏灯（影响天空盒环境光的取光方向/强度）
            RenderSettings.sun = sun;
        }

        /// <summary>地面 Plane 使用内置默认材质</summary>
        private static void CreateGroundPlane()
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Plane";
            RemovePrimitiveCollider(plane);                 // 原型的地面没有碰撞体
            plane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            plane.transform.localScale = new Vector3(10f, 1f, 10f);
            Undo.RegisterCreatedObjectUndo(plane, "Create 高亮描边 Demo Plane");
        }

        /// <summary>用不透明横梁同时遮挡五个图元的一部分，便于对照可见描边和遮挡剪影。</summary>
        private static void CreateOccluder()
        {
            GameObject occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
            occluder.name = "Opaque Occluder";
            occluder.transform.position = new Vector3(0f, 0.55f, -1.35f);
            occluder.transform.localScale = new Vector3(9f, 0.45f, 0.2f);
            RemovePrimitiveCollider(occluder);
            Undo.RegisterCreatedObjectUndo(occluder, "Create 高亮描边 Demo Occluder");
        }

        /// <summary>创建一个描边形状：图元 + OutLine 组件（参数按原型逐个设置）</summary>
        private static void CreateOutlinedShape(DemoEntry entry, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(entry.Shape);
            go.name = entry.Name;
            RemovePrimitiveCollider(go);                    // 原型的示例物体都没有碰撞体
            Undo.RegisterCreatedObjectUndo(go, "Create 高亮描边 Demo Object");

            go.transform.SetParent(parent, false);          // worldPositionStays = false：直接用局部坐标
            go.transform.localPosition = entry.Position;
            go.transform.localScale    = entry.Scale;

            OutLine outline = go.AddComponent<OutLine>();
            outline.OutlineMode  = entry.Mode;
            outline.OutlineColor = OutlineColor;
            outline.OutlineWidth = OutlineWidth;
            outline.CanFlash     = entry.CanFlash;          // public 字段，直接赋值

            EditorUtility.SetDirty(go);
        }

        // =====================================================================
        //  小工具
        // =====================================================================

        private static GameObject CreateEmptyObject(string name, Vector3 position)
        {
            GameObject go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(go, "Create 高亮描边 Demo Object");
            return go;
        }

        /// <summary>移除 CreatePrimitive 自动添加的碰撞体（原型里这些物体没有碰撞体）</summary>
        private static void RemovePrimitiveCollider(GameObject go)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        /// <summary>确保工程内的目录存在（形如 Assets/A/B，逐级创建）。实现见 EditorAssetPathUtility。</summary>
        private static void EnsureFolder(string projectRelativeFolder)
        {
            EditorAssetPathUtility.EnsureFolder(projectRelativeFolder);
        }
    }
}
