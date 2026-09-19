//
//  VerticalSceneDemoBuilder.cs
//  路径：Assets/Scripts/upanda-framework/Editor/VerticalSceneEditor/
//
// =============================================================================
//  作用：一键重建 VehicleSystem 的驾驶示例场景
//        （原型：Runtime/Game/VehicleSystem/Sample/VerticalScene.unity）
//
//  菜单：
//    UPandaGF/Runtime/VehicleSystem/创建驾驶示例场景（新建场景）
//    UPandaGF/Runtime/VehicleSystem/在当前场景追加赛道与车辆
//
//  ---- 操作方式（由 VehicleInput 决定）----
//    W / ↑      油门        S / ↓   倒车
//    A / D / ←→ 转向        空格    手刹（漂移）
//    C          循环切换视角 1/2/3   直达 第三人称 / 第一人称 / 环绕
//    （只用 Unity 内置的 Horizontal / Vertical 轴，不需要额外配置 InputManager）
//
//  ---- 原型场景结构（= 本脚本重建出来的结构）----
//
//    Main Camera         pos(0,4,-10)  天空盒清屏，Tag=MainCamera + AudioListener
//    Directional Light   pos(0,3,0)    rot(50,-30,0)  软阴影（原型没登记 RenderSettings.sun）
//    CameraModeManager   target=Car / cameraTransform=Main Camera / input=Car 上的 VehicleInput
//                        thirdPersonOffset(0,3.5,-7) lookHeight 1.2 / firstPersonOffset(0,1.2,0.4)
//                        orbit(Distance 8, Pitch 25, 灵敏度 3, Pitch 5~80) / transitionTime 0.5
//    Car                 pos(0,1.2,0)  Rigidbody(mass 1200, angularDrag 0.05)
//                                      BoxCollider size(2,0.7,4) center(0,0.7,0)
//                                      VehicleInput + VehicleController
//      ├ Body                Cube  local(0,0.7,0) scale(2,0.7,4)  红（无独立碰撞体，由 Car 的 Box 负责）
//      ├ WheelFL / WheelFR   前轮 local(±0.85, 0.35, 1.25)  WheelCollider
//      ├ WheelRL / WheelRR   后轮 local(±0.85, 0.35, -1.25) WheelCollider
//      ├ WheelXX_Visual ×4   空物体，位置同对应轮子
//      │   └ Cylinder        可见轮子：local 绕 Z 转 90°，scale(0.7,0.24,0.7)，近黑色
//      └ （VehicleController 里 frontWheels/rearWheels/frontWheelMeshes/rearWheelMeshes 全部连好）
//
//    赛道（全部用内置图元 + 不同颜色材质搭出来，坐标取自原型）：
//      VehicleGround  Cube    (0,-0.5,0)     scale(60,1,60)      深灰
//      Wall_N/S       Cube    (0,0.5,±29.5)  scale(60,1,0.5)     灰
//      Wall_W/E       Cube    (±29.5,0.5,0)  scale(0.5,1,60)     灰
//      Platform_A     Cube    (18,0.75,6)    scale(6,1.5,6)      绿
//      Platform_B     Cube    (-18,1.5,14)   scale(5,3,5)        紫
//      Ramp_Big       Cube    (0,0.3,15)     rot(-15,0,0)  scale(6,0.3,8)   橙
//      Ramp_Side      Cube    (20,0.3,-12)   rot(-12,90,0) scale(4,0.3,5)   橙
//      Obstacle_0~3   Cube    见下表         scale(1.2,0.8,1.2)  砖红
//      Pylon_0~7      Cylinder 见下表        scale(0.6,0.8,0.6)  黄
//      Bump_0~4       Sphere   见下表        scale(1.6,0.7,1.6)  土黄（SphereCollider center=(0,-0.61,0)）
//
//  ---- 与原型的差异（有意为之）----
//    1. 材质：原型把 10 个内置 Standard 材质**内嵌在场景文件里**（都叫 "Standard"，只有颜色不同）；
//       本脚本改为在 `Demo/Materials/` 下创建同名材质资产（颜色一致），便于复用与调整。
//    2. VehicleController / CameraModeManager 的字段都是 `[SerializeField] private`，脚本用
//       `SerializedObject` 写入（写入值与原型的序列化值逐一相同，包括 1500/3000/6000/30/40/12 与重心偏移）。
//    3. 原型 `RenderSettings.sun` 为空 → 这里也不设置。
//    4. 车身 Body 不带碰撞体（原型如此，碰撞由 Car 根节点的 BoxCollider 负责）；
//       视觉轮子上的 Cylinder 会移除 CreatePrimitive 自带的碰撞体（原型也没有）。
// =============================================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// VehicleSystem 驾驶示例场景一键生成器
    /// </summary>
    public static class VerticalSceneDemoBuilder
    {
        // ---------------- 菜单与路径 ----------------

        private const string MenuRoot = "UPandaGF/Runtime/VehicleSystem/";

        private const string DefaultSceneFolder = "Assets/Scripts/upanda-framework/Runtime/Game/VehicleSystem/Demo";
        private const string DefaultSceneName   = "VerticalSceneDemo.unity";
        private const string MaterialsFolder    = DefaultSceneFolder + "/Materials";

        // ---------------- 颜色（取自原型内嵌的 Standard 材质）----------------

        private static readonly Color GroundColor   = new Color(0.35f, 0.4f, 0.42f);
        private static readonly Color PlatformAColor = new Color(0.3f, 0.7f, 0.4f);
        private static readonly Color PlatformBColor = new Color(0.6f, 0.3f, 0.8f);
        private static readonly Color RampColor     = new Color(1f, 0.55f, 0.1f);
        private static readonly Color WallColor     = new Color(0.5f, 0.5f, 0.55f);
        private static readonly Color ObstacleColor = new Color(0.85f, 0.3f, 0.3f);
        private static readonly Color PylonColor    = new Color(0.95f, 0.8f, 0.2f);
        private static readonly Color BumpColor     = new Color(0.5f, 0.42f, 0.3f);
        private static readonly Color CarBodyColor  = new Color(0.9f, 0.25f, 0.2f);
        private static readonly Color WheelColor    = new Color(0.15f, 0.15f, 0.15f);

        // ---------------- 赛道物件表 ----------------

        private enum CourseKind { Ground, PlatformA, PlatformB, Ramp, Wall, Obstacle, Pylon, Bump }

        private struct CourseItem
        {
            public string      Name;
            public PrimitiveType Shape;
            public Vector3     Position;
            public Vector3     Euler;    // 欧拉角（度）
            public Vector3     Scale;
            public CourseKind  Kind;
        }

        private static readonly CourseItem[] CourseItems =
        {
            // 地面与围墙
            Item("VehicleGround", PrimitiveType.Cube,     0f, -0.5f, 0f,      0f,   0f,  0f, 60f, 1f, 60f, CourseKind.Ground),
            Item("Wall_N",        PrimitiveType.Cube,     0f,  0.5f, 29.5f,   0f,   0f,  0f, 60f, 1f, 0.5f, CourseKind.Wall),
            Item("Wall_S",        PrimitiveType.Cube,     0f,  0.5f, -29.5f,  0f,   0f,  0f, 60f, 1f, 0.5f, CourseKind.Wall),
            Item("Wall_W",        PrimitiveType.Cube,   -29.5f, 0.5f, 0f,    0f,   0f,  0f, 0.5f, 1f, 60f, CourseKind.Wall),
            Item("Wall_E",        PrimitiveType.Cube,    29.5f, 0.5f, 0f,    0f,   0f,  0f, 0.5f, 1f, 60f, CourseKind.Wall),

            // 平台与坡道
            Item("Platform_A",    PrimitiveType.Cube,    18f, 0.75f, 6f,     0f,   0f,  0f, 6f, 1.5f, 6f, CourseKind.PlatformA),
            Item("Platform_B",    PrimitiveType.Cube,   -18f, 1.5f, 14f,     0f,   0f,  0f, 5f, 3f, 5f,   CourseKind.PlatformB),
            Item("Ramp_Big",      PrimitiveType.Cube,     0f, 0.3f, 15f,   -15f,   0f,  0f, 6f, 0.3f, 8f, CourseKind.Ramp),
            Item("Ramp_Side",     PrimitiveType.Cube,    20f, 0.3f, -12f,  -12f,  90f,  0f, 4f, 0.3f, 5f, CourseKind.Ramp),

            // 障碍方块（朝向各不相同）
            Item("Obstacle_0",    PrimitiveType.Cube,     8f, 0.4f, -8f,     0f,   0f,  0f, 1.2f, 0.8f, 1.2f, CourseKind.Obstacle),
            Item("Obstacle_1",    PrimitiveType.Cube,    11f, 0.4f, -11f,    0f,  30f,  0f, 1.2f, 0.8f, 1.2f, CourseKind.Obstacle),
            Item("Obstacle_2",    PrimitiveType.Cube,    -6f, 0.4f, 20f,     0f,  60f,  0f, 1.2f, 0.8f, 1.2f, CourseKind.Obstacle),
            Item("Obstacle_3",    PrimitiveType.Cube,    15f, 0.4f, 18f,     0f,  90f,  0f, 1.2f, 0.8f, 1.2f, CourseKind.Obstacle),

            // 锥桶（Cylinder 图元自带 CapsuleCollider）
            Item("Pylon_0",       PrimitiveType.Cylinder,  3f, 0.4f, 6f,     0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),
            Item("Pylon_1",       PrimitiveType.Cylinder, -3f, 0.4f, 9f,     0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),
            Item("Pylon_2",       PrimitiveType.Cylinder,  3f, 0.4f, 12f,    0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),
            Item("Pylon_3",       PrimitiveType.Cylinder, -3f, 0.4f, 15f,    0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),
            Item("Pylon_4",       PrimitiveType.Cylinder, -8f, 0.4f, -6f,    0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),
            Item("Pylon_5",       PrimitiveType.Cylinder, -11f, 0.4f, -9f,   0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),
            Item("Pylon_6",       PrimitiveType.Cylinder, -14f, 0.4f, -12f,  0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),
            Item("Pylon_7",       PrimitiveType.Cylinder, -17f, 0.4f, -15f,  0f,   0f,  0f, 0.6f, 0.8f, 0.6f, CourseKind.Pylon),

            // 减速带（Sphere 压扁；原型把 SphereCollider 中心下移了 0.61）
            Item("Bump_0",        PrimitiveType.Sphere,  -12f,  -0.1f, 5f,   0f,   0f,  0f, 1.6f, 0.7f, 1.6f, CourseKind.Bump),
            Item("Bump_1",        PrimitiveType.Sphere,  -13f,  -0.1f, 7f,   0f,   0f,  0f, 1.6f, 0.7f, 1.6f, CourseKind.Bump),
            Item("Bump_2",        PrimitiveType.Sphere,  -11f,  -0.1f, 9f,   0f,   0f,  0f, 1.6f, 0.7f, 1.6f, CourseKind.Bump),
            Item("Bump_3",        PrimitiveType.Sphere,  -12.5f, -0.1f, 11f, 0f,   0f,  0f, 1.6f, 0.7f, 1.6f, CourseKind.Bump),
            Item("Bump_4",        PrimitiveType.Sphere,  -11.5f, -0.1f, 13f, 0f,   0f,  0f, 1.6f, 0.7f, 1.6f, CourseKind.Bump),
        };

        private static CourseItem Item(string name, PrimitiveType shape,
                                       float px, float py, float pz,
                                       float rx, float ry, float rz,
                                       float sx, float sy, float sz,
                                       CourseKind kind)
        {
            return new CourseItem
            {
                Name = name,
                Shape = shape,
                Position = new Vector3(px, py, pz),
                Euler = new Vector3(rx, ry, rz),
                Scale = new Vector3(sx, sy, sz),
                Kind = kind
            };
        }

        // =====================================================================
        //  菜单入口 1：新建场景
        // =====================================================================

        [MenuItem(MenuRoot + "创建驾驶示例场景（新建场景）", false, 10)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return; // 用户取消
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureFolder(DefaultSceneFolder);
            EnsureFolder(MaterialsFolder);

            BuildSceneContents(withCameraAndLight: true, out _);

            string scenePath = AssetDatabase.GenerateUniqueAssetPath(DefaultSceneFolder + "/" + DefaultSceneName);
            bool saved = EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (saved)
            {
                Debug.Log("[VehicleSystem] 驾驶示例场景已创建：" + scenePath +
                          "\n操作：W/↑ 油门，S/↓ 倒车，A/D 转向，空格 手刹（漂移），C 切视角，1/2/3 直达三种视角。" +
                          "\n（材质资产在 " + MaterialsFolder + "；要出包时记得把本场景拖进 Build Settings）");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath));
            }
            else
            {
                Debug.LogError("[VehicleSystem] 场景保存失败：" + scenePath + "，请检查目录是否可写。");
            }
        }

        // =====================================================================
        //  菜单入口 2：追加到当前场景
        // =====================================================================

        [MenuItem(MenuRoot + "在当前场景追加赛道与车辆", false, 11)]
        public static void AppendToCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[VehicleSystem] 当前没有打开的场景，无法追加。");
                return;
            }

            EnsureFolder(DefaultSceneFolder);
            EnsureFolder(MaterialsFolder);

            // 相机/灯光沿用现有的；没有主相机就补一个（CameraModeManager 必须要有相机）
            BuildSceneContents(withCameraAndLight: false, out GameObject cameraGo);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.LogWarning("[VehicleSystem] 已向场景 \"" + scene.name + "\" 追加：赛道 + 车辆 + CameraModeManager。" +
                             (cameraGo != null
                                 ? "\n场景里原本没有主相机，已顺便创建一个（Tag=MainCamera）。"
                                 : "\n相机沿用了场景现有的 Main Camera。"));
        }

        // =====================================================================
        //  场景内容构建
        // =====================================================================

        /// <param name="withCameraAndLight">新建空场景时为 true；追加到已有场景时传 false。</param>
        /// <param name="cameraGo">返回主相机物体（追加模式下可能是新建的，也可能是 null 表示用了场景现有的）。</param>
        private static void BuildSceneContents(bool withCameraAndLight, out GameObject cameraGo)
        {
            cameraGo = null;

            Transform cameraTransform;
            if (withCameraAndLight)
            {
                cameraGo = CreateMainCamera();
                cameraTransform = cameraGo.transform;
                CreateDirectionalLight();
            }
            else
            {
                Camera existing = Camera.main;
                if (existing == null)
                {
                    cameraGo = CreateMainCamera();
                    cameraTransform = cameraGo.transform;
                }
                else
                {
                    cameraTransform = existing.transform;
                }
            }

            MaterialSet materials = CreateMaterialAssets();
            CreateCourse(materials);
            CarReferences car = CreateCar(materials);
            CreateCameraModeManager(car, cameraTransform);
        }

        // ---------------------------------------------------------------------
        //  相机 / 灯光
        // ---------------------------------------------------------------------

        private static GameObject CreateMainCamera()
        {
            GameObject go = CreateEmpty("Main Camera", new Vector3(0f, 4f, -10f), Quaternion.identity);
            go.AddComponent<Camera>();          // ClearFlags 用默认的 Skybox，与原型一致
            go.AddComponent<AudioListener>();
            go.tag = "MainCamera";
            return go;
        }

        private static void CreateDirectionalLight()
        {
            GameObject go = CreateEmpty("Directional Light", new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            // 原型没有把平行光登记为 RenderSettings.sun，这里保持一致
        }

        // ---------------------------------------------------------------------
        //  赛道
        // ---------------------------------------------------------------------

        private static void CreateCourse(MaterialSet materials)
        {
            foreach (CourseItem item in CourseItems)
            {
                GameObject go = GameObject.CreatePrimitive(item.Shape);
                go.name = item.Name;
                Undo.RegisterCreatedObjectUndo(go, "Create Vehicle Demo Object");

                go.transform.SetPositionAndRotation(item.Position, Quaternion.Euler(item.Euler));
                go.transform.localScale = item.Scale;

                // 减速带：原型把 SphereCollider 的中心下移了 0.61（CreatePrimitive 默认是 0）
                if (item.Kind == CourseKind.Bump && go.GetComponent<SphereCollider>() is SphereCollider sphere)
                {
                    sphere.center = new Vector3(0f, -0.61f, 0f);
                }

                MeshRenderer renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = materials.Pick(item.Kind);
                }
            }
        }

        // ---------------------------------------------------------------------
        //  车辆
        // ---------------------------------------------------------------------

        /// <summary>创建车辆时需要用到的引用（供后续连线）</summary>
        private struct CarReferences
        {
            public VehicleInput   Input;
            public Transform      Root;
            public WheelCollider  FrontLeft;
            public WheelCollider  FrontRight;
            public WheelCollider  RearLeft;
            public WheelCollider  RearRight;
            public Transform      FrontLeftVisual;
            public Transform      FrontRightVisual;
            public Transform      RearLeftVisual;
            public Transform      RearRightVisual;
        }

        private static CarReferences CreateCar(MaterialSet materials)
        {
            GameObject car = CreateEmpty("Car", new Vector3(0f, 1.2f, 0f), Quaternion.identity);
            Transform  root = car.transform;

            // ---- 车体物理 ----
            Rigidbody rb = car.AddComponent<Rigidbody>();
            rb.mass = 1200f;
            rb.drag = 0f;
            rb.angularDrag = 0.05f;
            rb.useGravity = true;

            BoxCollider box = car.AddComponent<BoxCollider>();
            box.size = new Vector3(2f, 0.7f, 4f);
            box.center = new Vector3(0f, 0.7f, 0f);

            VehicleInput input = car.AddComponent<VehicleInput>();

            // ---- 车身（纯视觉，碰撞体由上面的 BoxCollider 负责）----
            GameObject body = CreatePrimitiveChild("Body", PrimitiveType.Cube, root,
                                                   new Vector3(0f, 0.7f, 0f), Quaternion.identity,
                                                   new Vector3(2f, 0.7f, 4f), removeCollider: true);
            ApplyMaterial(body, materials.Body);

            // ---- 四轮：先建 WheelCollider，再建视觉轮 ----
            CarReferences carRefs = new CarReferences { Input = input, Root = root };

            carRefs.FrontLeft  = CreateWheel("WheelFL", root, new Vector3(-0.85f, 0.35f,  1.25f));
            carRefs.FrontRight = CreateWheel("WheelFR", root, new Vector3( 0.85f, 0.35f,  1.25f));
            carRefs.RearLeft   = CreateWheel("WheelRL", root, new Vector3(-0.85f, 0.35f, -1.25f));
            carRefs.RearRight  = CreateWheel("WheelRR", root, new Vector3( 0.85f, 0.35f, -1.25f));

            carRefs.FrontLeftVisual  = CreateWheelVisual("WheelFL_Visual", root, new Vector3(-0.85f, 0.35f,  1.25f), materials.Wheel);
            carRefs.FrontRightVisual = CreateWheelVisual("WheelFR_Visual", root, new Vector3( 0.85f, 0.35f,  1.25f), materials.Wheel);
            carRefs.RearLeftVisual   = CreateWheelVisual("WheelRL_Visual", root, new Vector3(-0.85f, 0.35f, -1.25f), materials.Wheel);
            carRefs.RearRightVisual  = CreateWheelVisual("WheelRR_Visual", root, new Vector3( 0.85f, 0.35f, -1.25f), materials.Wheel);

            // ---- VehicleController：字段是 [SerializeField] private，只能走 SerializedObject ----
            VehicleController controller = car.AddComponent<VehicleController>();
            SerializedObject so = new SerializedObject(controller);

            so.FindProperty("input").objectReferenceValue = input;
            SetObjectArray(so, "frontWheels", carRefs.FrontLeft, carRefs.FrontRight);
            SetObjectArray(so, "rearWheels", carRefs.RearLeft, carRefs.RearRight);
            SetObjectArray(so, "frontWheelMeshes", carRefs.FrontLeftVisual, carRefs.FrontRightVisual);
            SetObjectArray(so, "rearWheelMeshes", carRefs.RearLeftVisual, carRefs.RearRightVisual);

            so.FindProperty("maxMotorTorque").floatValue = 1500f;
            so.FindProperty("maxBrakeTorque").floatValue = 3000f;
            so.FindProperty("handbrakeTorque").floatValue = 6000f;
            so.FindProperty("maxSteerAngle").floatValue = 30f;
            so.FindProperty("topSpeed").floatValue = 40f;
            so.FindProperty("reverseTopSpeed").floatValue = 12f;
            so.FindProperty("centerOfMassOffset").vector3Value = new Vector3(0f, -0.6f, 0f);

            so.ApplyModifiedPropertiesWithoutUndo();

            return carRefs;
        }

        /// <summary>WheelCollider：参数全部取自原型（四个轮子完全一致）</summary>
        private static WheelCollider CreateWheel(string name, Transform parent, Vector3 localPosition)
        {
            GameObject go = CreateChild(name, parent);
            go.transform.localPosition = localPosition;

            WheelCollider wheel = go.AddComponent<WheelCollider>();
            wheel.center = Vector3.zero;
            wheel.radius = 0.35f;
            wheel.mass = 40f;
            wheel.suspensionDistance = 0.2f;
            wheel.wheelDampingRate = 0.25f;
            wheel.forceAppPointDistance = 0f;

            JointSpring spring = wheel.suspensionSpring;
            spring.spring = 35000f;
            spring.damper = 4500f;
            spring.targetPosition = 0.5f;
            wheel.suspensionSpring = spring;

            WheelFrictionCurve forward = wheel.forwardFriction;
            forward.extremumSlip = 0.4f; forward.extremumValue = 1f;
            forward.asymptoteSlip = 0.8f; forward.asymptoteValue = 0.5f;
            forward.stiffness = 1f;
            wheel.forwardFriction = forward;

            WheelFrictionCurve sideways = wheel.sidewaysFriction;
            sideways.extremumSlip = 0.2f; sideways.extremumValue = 1f;
            sideways.asymptoteSlip = 0.5f; sideways.asymptoteValue = 0.75f;
            sideways.stiffness = 0.55f;
            wheel.sidewaysFriction = sideways;

            return wheel;
        }

        /// <summary>视觉轮：空物体（由 VehicleController 同步位姿）+ 一个绕 Z 转 90° 的 Cylinder</summary>
        private static Transform CreateWheelVisual(string name, Transform parent, Vector3 localPosition, Material wheelMaterial)
        {
            GameObject visual = CreateChild(name, parent);
            visual.transform.localPosition = localPosition;

            GameObject cylinder = CreatePrimitiveChild("Cylinder", PrimitiveType.Cylinder, visual.transform,
                                                       Vector3.zero, Quaternion.Euler(0f, 0f, 90f),
                                                       new Vector3(0.7f, 0.24f, 0.7f), removeCollider: true);
            ApplyMaterial(cylinder, wheelMaterial);

            return visual.transform;
        }

        // ---------------------------------------------------------------------
        //  视角管理器
        // ---------------------------------------------------------------------

        private static void CreateCameraModeManager(CarReferences car, Transform cameraTransform)
        {
            GameObject go = CreateEmpty("CameraModeManager", Vector3.zero, Quaternion.identity);
            CameraModeManager manager = go.AddComponent<CameraModeManager>();

            // 字段同样是 [SerializeField] private —— 用 SerializedObject 写入原型的序列化值
            SerializedObject so = new SerializedObject(manager);
            so.FindProperty("target").objectReferenceValue = car.Root;
            so.FindProperty("cameraTransform").objectReferenceValue = cameraTransform;
            so.FindProperty("input").objectReferenceValue = car.Input;
            so.FindProperty("thirdPersonOffset").vector3Value = new Vector3(0f, 3.5f, -7f);
            so.FindProperty("thirdPersonLookHeight").floatValue = 1.2f;
            so.FindProperty("firstPersonOffset").vector3Value = new Vector3(0f, 1.2f, 0.4f);
            so.FindProperty("orbitDistance").floatValue = 8f;
            so.FindProperty("orbitPitch").floatValue = 25f;
            so.FindProperty("orbitMouseSensitivity").floatValue = 3f;
            so.FindProperty("orbitMinPitch").floatValue = 5f;
            so.FindProperty("orbitMaxPitch").floatValue = 80f;
            so.FindProperty("transitionTime").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ---------------------------------------------------------------------
        //  材质资产
        // ---------------------------------------------------------------------

        /// <summary>本示例用到的 10 个材质（原型是把它们内嵌在场景里的）</summary>
        private class MaterialSet
        {
            public Material Ground;
            public Material PlatformA;
            public Material PlatformB;
            public Material Ramp;
            public Material Wall;
            public Material Obstacle;
            public Material Pylon;
            public Material Bump;
            public Material Body;
            public Material Wheel;

            public Material Pick(CourseKind kind)
            {
                switch (kind)
                {
                    case CourseKind.Ground:    return Ground;
                    case CourseKind.PlatformA: return PlatformA;
                    case CourseKind.PlatformB: return PlatformB;
                    case CourseKind.Ramp:      return Ramp;
                    case CourseKind.Wall:      return Wall;
                    case CourseKind.Obstacle:  return Obstacle;
                    case CourseKind.Pylon:     return Pylon;
                    default:                   return Bump;
                }
            }
        }

        private static MaterialSet CreateMaterialAssets()
        {
            return new MaterialSet
            {
                Ground    = GetOrCreateMaterial("VehicleGround", GroundColor),
                PlatformA = GetOrCreateMaterial("Platform_A",   PlatformAColor),
                PlatformB = GetOrCreateMaterial("Platform_B",   PlatformBColor),
                Ramp      = GetOrCreateMaterial("Ramp",         RampColor),
                Wall      = GetOrCreateMaterial("Wall",         WallColor),
                Obstacle  = GetOrCreateMaterial("Obstacle",     ObstacleColor),
                Pylon     = GetOrCreateMaterial("Pylon",        PylonColor),
                Bump      = GetOrCreateMaterial("Bump",         BumpColor),
                Body      = GetOrCreateMaterial("CarBody",      CarBodyColor),
                Wheel     = GetOrCreateMaterial("Wheel",        WheelColor),
            };
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = MaterialsFolder + "/" + name + ".mat";

            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.color = color;          // 已存在就同步颜色，重复执行不会产生副本
                EditorUtility.SetDirty(existing);
                return existing;
            }

            Shader shader = Shader.Find("Standard");                        // 内置管线的默认 PBR Shader
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");   // 万一在 URP 工程里执行
            if (shader == null)
            {
                Debug.LogError("[VehicleSystem] 找不到可用的默认 Shader（Standard / Universal Render Pipeline/Lit），材质无法创建。");
                return null;
            }

            Material material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // =====================================================================
        //  小工具
        // =====================================================================

        /// <summary>把若干 UnityEngine.Object 写进目标对象的数组字段（SerializedObject）</summary>
        private static void SetObjectArray(SerializedObject so, string propertyName, params UnityEngine.Object[] values)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError("[VehicleSystem] 找不到字段：" + propertyName + "（目标类可能已改动，请同步本脚本）");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static GameObject CreateEmpty(string name, Vector3 position, Quaternion rotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(go, "Create Vehicle Demo Object");
            return go;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create Vehicle Demo Object");
            return go;
        }

        private static GameObject CreatePrimitiveChild(string name, PrimitiveType shape, Transform parent,
                                                       Vector3 localPosition, Quaternion localRotation,
                                                       Vector3 localScale, bool removeCollider)
        {
            GameObject go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Create Vehicle Demo Object");

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;
            go.transform.localScale = localScale;

            if (removeCollider)
            {
                Collider collider = go.GetComponent<Collider>();
                if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            }

            return go;
        }

        private static void ApplyMaterial(GameObject go, Material material)
        {
            if (material == null) return;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        /// <summary>确保工程内目录存在（形如 Assets/A/B，逐级创建）</summary>
        private static void EnsureFolder(string projectRelativeFolder)
        {
            string full = projectRelativeFolder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(full))
            {
                return;
            }

            string[] parts = full.Split('/');
            string current = parts[0];   // "Assets"
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
