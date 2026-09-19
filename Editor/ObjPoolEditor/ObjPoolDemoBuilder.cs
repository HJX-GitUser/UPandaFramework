//
//  ObjPoolDemoBuilder.cs
//  路径：Assets/Scripts/upanda-framework/Editor/ObjPoolEditor/
//
// =============================================================================
//  作用：一键重建 ObjPool（对象池）的示例场景
//        （原型：Runtime/Manager/ObjPool/Example/PoolUseScene.unity）
//
//  菜单：
//    UPandaGF/Runtime/ObjPool/创建对象池示例场景（新建场景）
//    UPandaGF/Runtime/ObjPool/在当前场景追加对象池演示物体
//
//  ---- 原型场景结构（= 本脚本重建出来的结构）----
//
//    Main Camera        pos(0,1,-10)  ClearFlags=SolidColor bg(0.374,0.416,0.481)  Tag=MainCamera
//    Directional Light  pos(0,3,0)    rot(50,-30,0)  软阴影（原型没把它登记为 RenderSettings.sun）
//    Plane              pos(0,-18,0)  scale(10,10,10)  MeshCollider（Plane 图元自带）+ 默认材质
//    TestPoolMgr        pos(0.12,-1.18,0.40)
//                       LoadAsync=true / loadMethod=Resources / obj1Path="Obj1" / obj2Path="Obj2" / prewarmCount=10
//                       （演示 UI 走 OnGUI：创建 Obj1 / 创建 Obj2 / 回收所有 + 池内空闲数量）
//    UPGameRoot         pos(0,0,0)     框架启动根节点，子物体：
//      ├ DebugerInit        showFPS=true + LogConfig 全套开关
//      ├ Downloader         maxConcurrentDownloads=1 / defaultTimeout=30 / defaultRetryCount=0 / retryDelay=1 / enableResume=false
//      ├ AssetsLoader       AssetAESConfig（与 UPGameRoot 上的保持同一份）
//      ├ BinaryDataMgrInit  savePath="/Data/" / extension=".binary"
//      ├ UIManager          子物体 UICamera + Canvas（下面这两个是 UIManager.Init() 自己建的）
//      │   ├ UICamera       ClearFlags=Depth / depth=10 / cullingMask=仅 UI 层
//      │   └ Canvas         RenderMode=ScreenSpaceCamera(worldCamera=UICamera) / CanvasScaler 1920x1080 Expand
//      │                    └ Bot / Mid / Top / System（4 个撑满屏幕的空 RectTransform，UI 层，框架的面板层级）
//      └ EventSystem        EventSystem + StandaloneInputModule（原型里它挂在 UPGameRoot 下）
//
//    ⇒ 进 Play 后：UPGameRoot 初始化完毕会派发 GFLoadedEvent，TestPoolMgr 收到后按 prewarmCount
//      预热两个预制体；随后用左上角的 IMGUI 按钮即可取/回收对象。（演示预制体：
//      Runtime/Manager/ObjPool/Resources/Obj1.prefab、Obj2.prefab）
//
//  ---- 与原型的差异（有意为之）----
//    1. UPGameRoot.method：原型存的是 Assetbundles(1)（需要先烘好 AssetBundle + 清单才能跑）。
//       一键创建默认用 Editor(0) —— 编辑器里直接可跑；要复刻原型请改 RootLoadMethod 常量。
//    2. AES 配置：原型里 UPGameRoot.enable=0 而 AssetsLoader.enable=1（互相矛盾）；
//       但运行时 UPGameRoot.Init 会用 Root 的值覆盖 AssetsLoader 的，所以这里把两处写成同一份
//       （enable=false），避免配置误导。
//    3. 不设置 RenderSettings.sun（原型 m_Sun 为空）。
//    4. Reporter 字段原型为 null，这里也不挂。
//    5. EventSystem 沿用原型同款旧版 StandaloneInputModule。
//
//  ---- ⚠️ 实现注意（改这个脚本前务必先读）----
//    编辑器里 AddComponent<UPGameRoot>() 会立刻触发 UPGameRoot.Reset() → SetComponent()，
//    而 SetComponent() 会：
//      · 用 InitComponent<T>() 自动补齐 DebugerInit / Downloader / AssetsLoader / BinaryDataMgrInit / UIManager
//        （名字就是类型名，挂成 UPGameRoot 的子物体）；
//      · 调用 UIManager.Init() —— 这一步会连带创建 UICamera、Canvas、Bot/Mid/Top/System、以及 EventSystem。
//    这些自动产物与原型**结构完全一致**，所以本脚本对它们统一采取「已存在就复用」（GetOrCreateComponent），
//    否则会出现两份同名管理器 / 两个 Canvas。
// =============================================================================

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UPandaGF;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// ObjPool 示例场景一键生成器
    /// </summary>
    public static class ObjPoolDemoBuilder
    {
        // ---------------- 菜单与路径 ----------------

        private const string MenuRoot = "UPandaGF/Runtime/ObjPool/";

        private const string DefaultSceneFolder = "Assets/Scripts/upanda-framework/Runtime/Manager/ObjPool/Demo";
        private const string DefaultSceneName   = "PoolUseSceneDemo.unity";

        /// <summary>
        /// 一键创建时 UPGameRoot 使用的资源加载方式。
        /// 原型场景存的是 <see cref="AssetLoaddingMethod.Assetbundles"/>（需先打包 AB + 生成清单才能跑）；
        /// 演示场景默认用 <see cref="AssetLoaddingMethod.Editor"/>，编辑器里按下 Play 就能用。
        /// </summary>
        private const AssetLoaddingMethod RootLoadMethod = AssetLoaddingMethod.Editor;

        // 演示预制体（位于 Runtime/Manager/ObjPool/Resources/ 下，按 Resources 相对路径加载）
        private static readonly string[] DemoPrefabPaths = { "Obj1", "Obj2" };

        // =====================================================================
        //  菜单入口 1：新建场景
        // =====================================================================

        [MenuItem(MenuRoot + "创建对象池示例场景（新建场景）", false, 10)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return; // 用户取消
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildSceneContents(includeBootstrap: true);

            EnsureFolder(DefaultSceneFolder);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(DefaultSceneFolder + "/" + DefaultSceneName);
            bool saved = EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            if (saved)
            {
                Debug.Log("[ObjPool] 示例场景已创建：" + scenePath +
                          "\n玩法：Play → 等 UPGameRoot 初始化完成（Console 打印 “GameRoot Initialization completed!”）" +
                          "→ 用左上角 IMGUI 按钮「创建Obj1 / 创建Obj2 / 回收所有」，标签实时显示池中空闲对象数。" +
                          "\n（UGameRoot 加载方式：" + RootLoadMethod + "；要出包时记得把本场景拖进 Build Settings）");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath));
            }
            else
            {
                Debug.LogError("[ObjPool] 场景保存失败：" + scenePath + "，请检查目录是否可写。");
            }
        }

        // =====================================================================
        //  菜单入口 2：追加到当前场景（只加演示物体，不动 UPGameRoot 等启动设施）
        // =====================================================================

        [MenuItem(MenuRoot + "在当前场景追加对象池演示物体", false, 11)]
        public static void AppendToCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[ObjPool] 当前没有打开的场景，无法追加。");
                return;
            }

            CreateGroundPlane();
            CreateTestPoolMgr();

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.LogWarning("[ObjPool] 已向场景 \"" + scene.name + "\" 追加：Plane + TestPoolMgr。" +
                             "\n注意：TestPoolMgr 依赖 UPGameRoot（Awake 里会取 UPGameRoot.Instance 和 GFLoadedEvent），" +
                             "当前场景若没有 UPGameRoot，请改用菜单「创建对象池示例场景（新建场景）」。");
        }

        // =====================================================================
        //  场景内容构建
        // =====================================================================

        private static void BuildSceneContents(bool includeBootstrap)
        {
            if (includeBootstrap)
            {
                CreateMainCamera();
                CreateDirectionalLight();
                CreateUPGameRoot();
            }

            CreateGroundPlane();
            CreateTestPoolMgr();

            WarnIfDemoPrefabsMissing();
        }

        // ---------------------------------------------------------------------
        //  相机 / 灯光 / 地面 / 演示物体
        // ---------------------------------------------------------------------

        private static void CreateMainCamera()
        {
            GameObject go = CreateEmpty("Main Camera", new Vector3(0f, 1f, -10f), Quaternion.identity);
            Camera camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.374466f, 0.41594726f, 0.4811321f, 0f);
            go.AddComponent<AudioListener>();
            go.tag = "MainCamera";
        }

        private static void CreateDirectionalLight()
        {
            GameObject go = CreateEmpty("Directional Light", new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;
            // 原型没有把平行光登记为 RenderSettings.sun，这里保持一致
        }

        private static void CreateGroundPlane()
        {
            // Plane 图元自带 MeshCollider，与原型的 Plane 一致
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "Plane";
            Undo.RegisterCreatedObjectUndo(go, "Create ObjPool Demo Object");
            go.transform.localPosition = new Vector3(0f, -18f, 0f);
            go.transform.localScale = new Vector3(10f, 10f, 10f);
        }

        private static void CreateTestPoolMgr()
        {
            GameObject go = CreateEmpty("TestPoolMgr",
                                        new Vector3(0.12196481f, -1.1817453f, 0.39806843f), Quaternion.identity);

            TestPoolMgr pool = go.AddComponent<TestPoolMgr>();
            pool.LoadAsync = true;                          // true = Task 形式取对象
            pool.loadMethod = AssetLoadMethod.Resources;    // 演示预制体在 Resources 下
            pool.obj1Path = "Obj1";
            pool.obj2Path = "Obj2";
            pool.prewarmCount = 10;                         // 收到 GFLoadedEvent 后各预热 10 个
        }

        // ---------------------------------------------------------------------
        //  UPGameRoot 及其子管理器
        // ---------------------------------------------------------------------

        private static void CreateUPGameRoot()
        {
            GameObject go = CreateEmpty("UPGameRoot", Vector3.zero, Quaternion.identity);

            // ⚠️ 这一行会触发 UPGameRoot.Reset() → SetComponent()：子管理器与 UI 子树可能已经被它建好了，
            //    所以下面每个 Create* 都是「已存在就复用」（见文件头说明）。
            UPGameRoot gameRoot = go.AddComponent<UPGameRoot>();

            gameRoot.Config.method = RootLoadMethod;
            gameRoot.Config.enableAssetUpdate = false;                       // 原型：不启动资源更新
            gameRoot.Config.LoadAssetPath = "AssetBundles/StandaloneWindows/";
            gameRoot.Config.remoteURL = "http://127.0.0.1:80/";
            gameRoot.Config.EnableDebugModel = false;

            // AES 配置：UPGameRoot 与 AssetsLoader 各有一份，运行时 Root 会覆盖 Loader 那份，
            // 所以这里统一写成同一份（原型两处不一致：enable 0 / 1）
            gameRoot.Config.AssetAESConfig = CreateAesConfig();

            Transform parent = go.transform;

            // 子管理器：顺序与原型一致
            CreateDebugerInit(parent);
            CreateDownloader(parent);
            CreateAssetsLoader(parent);
            CreateBinaryDataMgrInit(parent);
            CreateUIManager(parent);
            CreateEventSystem(parent);   // UIManager.Init() 通常已经建好了，这里只是兜底
        }

        private static AssetBundleClassificationWindowConfig CreateAesConfig()
        {
            return new AssetBundleClassificationWindowConfig
            {
                enable = false,                                   // 演示不加密
                AESKEY = "111a222aaabbbccc",
                AESIV  = "111b222aaabbbccc",
                mainBundleLoadPath = ABLoadPath.StreamingAssetsPath
            };
        }

        /// <summary>日志系统：showFPS + LogConfig（数值取自原型）</summary>
        private static void CreateDebugerInit(Transform parent)
        {
            DebugerInit debugerInit = GetOrCreateComponent<DebugerInit>(parent, "DebugerInit");

            debugerInit.showFPS = true;
            debugerInit.logConfig = new LogConfig
            {
                openLog = true,
                openWarning = true,
                openError = true,
                addHeadFix = true,
                logHeadFix = "###",
                openTime = true,
                showThreadID = true,
                maxLogLength = 0,
                logSave = false,
                saveOverwrite = false,
                logFileSavePath = "Output Log/"
            };
        }

        private static void CreateDownloader(Transform parent)
        {
            Downloader downloader = GetOrCreateComponent<Downloader>(parent, "Downloader");

            downloader.maxConcurrentDownloads = 1;
            downloader.defaultTimeout = 30;
            downloader.defaultRetryCount = 0;
            downloader.retryDelay = 1f;
            downloader.enableResume = false;   // ⚠️ 断点续传默认关闭（与 MD5 增量清单冲突，见模块文档）
        }

        private static void CreateAssetsLoader(Transform parent)
        {
            AssetsLoader loader = GetOrCreateComponent<AssetsLoader>(parent, "AssetsLoader");
            loader.AssetAESConfig = CreateAesConfig();
        }

        private static void CreateBinaryDataMgrInit(Transform parent)
        {
            BinaryDataMgrInit binaryData = GetOrCreateComponent<BinaryDataMgrInit>(parent, "BinaryDataMgrInit");
            binaryData.savePath = "/Data/";
            binaryData.extension = ".binary";
        }

        /// <summary>
        /// UI 管理器。它自己的 <see cref="UIManager.Init"/> 就是原型那套 UI 结构的唯一来源：
        /// UICamera（ClearFlags=Depth / depth=10 / 只渲染 UI 层）、Canvas（ScreenSpaceCamera +
        /// CanvasScaler 1920x1080 Expand）、Bot/Mid/Top/System 四个撑满屏幕的 UI 层节点，
        /// 以及场景里没有 EventSystem 时顺手建一个。所以这里只调用 Init()，不自己造 UI 物体。
        /// </summary>
        private static void CreateUIManager(Transform parent)
        {
            UIManager uiManager = GetOrCreateComponent<UIManager>(parent, "UIManager");
            uiManager.Init();   // 幂等：已有 uiCamera/uiCanvas 就只重新找各层
        }

        /// <summary>EventSystem：原型里它挂在 UPGameRoot 下，这里保持一致</summary>
        private static void CreateEventSystem(Transform parent)
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return;   // 场景里已有（多半是 UIManager.Init() 建的）就不重复创建
            }

            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create ObjPool Demo Object");
        }

        // ---------------------------------------------------------------------
        //  校验
        // ---------------------------------------------------------------------

        /// <summary>演示依赖 Resources/Obj1.prefab 与 Obj2.prefab，缺失时给明确告警</summary>
        private static void WarnIfDemoPrefabsMissing()
        {
            foreach (string path in DemoPrefabPaths)
            {
                if (Resources.Load<GameObject>(path) == null)
                {
                    Debug.LogWarning("[ObjPool] 找不到演示预制体：" + path +
                                     "（应在 Runtime/Manager/ObjPool/Resources/" + path + ".prefab）" +
                                     "，运行时点「创建" + path + "」会失败。");
                }
            }
        }

        // =====================================================================
        //  小工具
        // =====================================================================

        /// <summary>
        /// 取 UPGameRoot 子树里已有的组件；没有才新建子物体并挂上。
        /// 目的：避开 UPGameRoot.Reset()/SetComponent() 自动补齐造成的重复。
        /// </summary>
        private static T GetOrCreateComponent<T>(Transform parent, string childName) where T : Component
        {
            T existing = parent.GetComponentInChildren<T>(true);
            if (existing != null)
            {
                return existing;
            }

            GameObject go = CreateChild(childName, parent);
            return go.AddComponent<T>();
        }

        private static GameObject CreateEmpty(string name, Vector3 position, Quaternion rotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(go, "Create ObjPool Demo Object");
            return go;
        }

        private static GameObject CreateChild(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(go, "Create ObjPool Demo Object");
            return go;
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
