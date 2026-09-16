//
//  QuickOutlineDemoBuilder.cs
//  路径：Assets/Scripts/upanda-framework/Editor/QuickOutlineEditor/
//
// =============================================================================
//  作用：一键重建 QuickOutline 的官方示例场景
//        （原型：Runtime/Game/QuickOutline/Samples/Scenes/QuickOutline.unity）
//
//  菜单：
//    UPandaGF/Runtime/QuickOutline/创建描边示例场景（新建场景）
//    UPandaGF/Runtime/QuickOutline/在当前场景追加示例物体
//
//  重建出来的内容（逐项对齐原型）：
//    Main Camera        pos(0,3,-4)  rot(40,0,0)   Tag = MainCamera（FOV/裁剪面用默认值 60 / 0.3~1000）
//    Directional Light  pos(0,3,0)   rot(50,30,0)  软阴影，并设为 RenderSettings.sun（环境光的“太阳”）
//    Plane              pos(0,0,0)   scale(10,1,10)，材质取 Samples/Resources/Materials/Plane.mat
//    Outlined Objects   （空父节点，位于原点）
//      ├ Silhouette Only          Sphere    (-4,0,0)  scale 1          Mode.SilhouetteOnly        CanFlash = true
//      ├ Outline Hidden           Cylinder  (-2,0,0)  scale(1,0.5,1)   Mode.OutlineHidden         CanFlash = true
//      ├ Outline All              Cube      ( 0,0,0)  scale 1          Mode.OutlineAll            CanFlash = false
//      ├ Outline And Silhouette   Cylinder  ( 2,0,0)  scale(1,0.5,1)   Mode.OutlineAndSilhouette  CanFlash = true
//      └ Outline Visible          Sphere    ( 4,0,0)  scale 1          Mode.OutlineVisible        CanFlash = true
//
//    共通参数：outlineColor = RGB(22,255,0) = (0.08675194, 1, 0, 1)，outlineWidth = 5，
//              precomputeOutline = false（= 原型值）。
//    这些物体**不带碰撞体**（原型里也没有；CreatePrimitive 默认会加，脚本会移除）。
//
//  与原型的已知差异（有意为之）：
//    · 原型相机上挂着一个遗留的 GUILayer 组件（老版 Unity 的产物，现已废弃）→ 不再创建。
//    · 原型绑定了一份 Lighting Settings（Samples/Scenes/QuickOutlineSettings.lighting）
//      → 新场景使用默认灯光设置；本示例只依赖实时平行光 + 默认天空盒环境光，视觉一致。
//    · 新场景不会自动写进 Build Settings（原型也没有）。
// =============================================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// QuickOutline 示例场景一键生成器
    /// </summary>
    public static class QuickOutlineDemoBuilder
    {
        // ---------------- 菜单 ----------------

        private const string MenuRoot = "UPandaGF/Runtime/QuickOutline/";

        // ---------------- 路径与常量 ----------------

        // 新建场景的默认落盘位置（模块自己的 Demo 目录，不动 Samples/ 里导入进来的资源）
        private const string DefaultSceneFolder = "Assets/Scripts/upanda-framework/Runtime/Game/QuickOutline/Demo";
        private const string DefaultSceneName   = "QuickOutlineDemo.unity";

        // 地面材质（原型 Plane 的 MeshRenderer 用的就是它）
        private const string PlaneMaterialPath =
            "Assets/Scripts/upanda-framework/Runtime/Game/QuickOutline/Samples/Resources/Materials/Plane.mat";

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
            public OutDrawline.Mode Mode;       // 描边模式
            public bool             CanFlash;   // 是否呼吸闪烁
        }

        // 按 X 轴从左到右排列（-4 → 4），与原型布局一致
        private static readonly DemoEntry[] DemoEntries =
        {
            new DemoEntry { Name = "Silhouette Only",        Shape = PrimitiveType.Sphere,   Position = new Vector3(-4f, 0f, 0f), Scale = Vector3.one,               Mode = OutDrawline.Mode.SilhouetteOnly,       CanFlash = true  },
            new DemoEntry { Name = "Outline Hidden",         Shape = PrimitiveType.Cylinder, Position = new Vector3(-2f, 0f, 0f), Scale = new Vector3(1f, 0.5f, 1f), Mode = OutDrawline.Mode.OutlineHidden,        CanFlash = true  },
            new DemoEntry { Name = "Outline All",            Shape = PrimitiveType.Cube,     Position = Vector3.zero,             Scale = Vector3.one,               Mode = OutDrawline.Mode.OutlineAll,           CanFlash = false },
            new DemoEntry { Name = "Outline And Silhouette", Shape = PrimitiveType.Cylinder, Position = new Vector3( 2f, 0f, 0f), Scale = new Vector3(1f, 0.5f, 1f), Mode = OutDrawline.Mode.OutlineAndSilhouette, CanFlash = true  },
            new DemoEntry { Name = "Outline Visible",        Shape = PrimitiveType.Sphere,   Position = new Vector3( 4f, 0f, 0f), Scale = Vector3.one,               Mode = OutDrawline.Mode.OutlineVisible,       CanFlash = true  },
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
                Debug.Log("[QuickOutline] 示例场景已创建：" + scenePath +
                          "（相机/平行光/地面 + 5 种描边模式各一个物体，直接按 Play 即可查看效果）");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath));
            }
            else
            {
                Debug.LogError("[QuickOutline] 场景保存失败：" + scenePath + "，请检查目录是否可写。");
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
                Debug.LogError("[QuickOutline] 当前没有打开的场景，无法追加。");
                return;
            }

            BuildSceneContents(includeCameraAndLight: false);
            EditorSceneManager.MarkSceneDirty(scene);   // 标记为已修改，便于 Ctrl+S 保存

            Debug.Log("[QuickOutline] 已向场景 \"" + scene.name +
                      "\" 追加地面 + 5 个描边物体（相机与灯光沿用场景里现有的）。");
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

            // 5 个描边物体统一挂在这个空父节点下（原型里也是这样，便于整体缩放/移动）
            GameObject root = CreateEmptyObject("Outlined Objects", Vector3.zero);

            foreach (DemoEntry entry in DemoEntries)
            {
                CreateOutlinedShape(entry, root.transform);
            }
        }

        /// <summary>主相机：pos(0,3,-4)、俯角 40°（FOV/裁剪面用 Camera 组件默认值）</summary>
        private static void CreateMainCamera()
        {
            GameObject cameraGo = CreateEmptyObject("Main Camera", new Vector3(0f, 3f, -4f));
            cameraGo.AddComponent<Camera>();
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";
            cameraGo.transform.rotation = Quaternion.Euler(40f, 0f, 0f);
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

        /// <summary>地面 Plane：scale(10,1,10)，材质用 Samples/Resources/Materials/Plane.mat</summary>
        private static void CreateGroundPlane()
        {
            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Plane";
            RemovePrimitiveCollider(plane);                 // 原型的地面没有碰撞体
            plane.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            plane.transform.localScale = new Vector3(10f, 1f, 10f);
            Undo.RegisterCreatedObjectUndo(plane, "Create QuickOutline Demo Plane");

            // 找不到材质就保留 CreatePrimitive 给的默认材质，不影响示例效果
            Material planeMaterial = AssetDatabase.LoadAssetAtPath<Material>(PlaneMaterialPath);
            if (planeMaterial != null)
            {
                plane.GetComponent<MeshRenderer>().sharedMaterial = planeMaterial;
            }
        }

        /// <summary>创建一个描边形状：图元 + OutDrawline 组件（参数按原型逐个设置）</summary>
        private static void CreateOutlinedShape(DemoEntry entry, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(entry.Shape);
            go.name = entry.Name;
            RemovePrimitiveCollider(go);                    // 原型的示例物体都没有碰撞体
            Undo.RegisterCreatedObjectUndo(go, "Create QuickOutline Demo Object");

            go.transform.SetParent(parent, false);          // worldPositionStays = false：直接用局部坐标
            go.transform.localPosition = entry.Position;
            go.transform.localScale    = entry.Scale;

            OutDrawline outline = go.AddComponent<OutDrawline>();
            outline.OutlineMode  = entry.Mode;              // 属性 setter 会置 needsUpdate
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
            Undo.RegisterCreatedObjectUndo(go, "Create QuickOutline Demo Object");
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

        /// <summary>确保工程内的目录存在（形如 Assets/A/B，逐级创建）</summary>
        private static void EnsureFolder(string projectRelativeFolder)
        {
            string full = projectRelativeFolder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(full))
            {
                return;
            }

            string[] parts = full.Split('/');
            string current = parts[0];                       // 一定是 "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
