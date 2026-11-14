using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// UI 层级。旧名 Bot/Mid/Top/System 与新名同值，保留源码与场景兼容。
    /// </summary>
    public enum E_UI_Layer
    {
        Background = 0,
        Main = 1,
        Popup = 2,
        Guide = 3,
        Toast = 4,

        Bot = Background,
        Mid = Main,
        Top = Popup,
        System = Guide
    }

    /// <summary>
    /// UI管理器：管理所有显示的面板，提供显示/隐藏等接口。
    /// <para>由 UPGameRoot 创建并持有（不再有静态单例）。依赖 IAssetService、IGameServices 与 ILogService，
    /// 都通过 <see cref="Configure(IAssetService, ILogService)"/> 或 <see cref="Configure(IGameServices)"/> 注入；
    /// 不再回退到 ResourcesLoader 全局实例。</para>
    /// <para>需要 UI 能力的场景组件实现 <see cref="IUIServiceConsumer"/>，由根节点注入 <see cref="IUIService"/>。</para>
    /// </summary>
    public class UIManager : MonoBehaviour, IUIService
    {
        /// <summary>
        /// 已打开面板的兼容视图。新代码请使用 <see cref="GetPanel{T}"/> 或诊断接口；
        /// 该字典在后续版本会收口为只读。
        /// </summary>
        public Dictionary<string, BasePanel> panelDic = new Dictionary<string, BasePanel>();

        private enum PanelState
        {
            Open,
            Closing,
            Closed
        }

        private sealed class PanelRecord
        {
            public string path;
            public Type panelType;
            public BasePanel panel;
            public ILifetimeScope scope;
            public PanelState state;
            public int generation;
            public UILoadInfoAttribute info;
            public E_UI_Layer layer;
            public int order;
            public int sequence;
            public bool modal;
            public bool pauseGame;
            public bool closeOnBack;
            public bool pool;
            public bool paused;
            public CanvasGroup group;
            public bool initialInteractable;
            public bool initialBlocksRaycasts;
            public IDisposable pauseLease;
            public IDisposable inputLease;
            public double loadMilliseconds;
            public double openMilliseconds;
        }

        private sealed class PendingPanel
        {
            public string path;
            public Type panelType;
            public object panelArg;
            public ILifetimeScope scope;
            public CancellationTokenSource requestCts;
            public CancellationTokenRegistration cancellation;
            public int generation;
            public bool closed;
            public float startedAt;
            public readonly TaskCompletionSource<BasePanel> completion =
                new TaskCompletionSource<BasePanel>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class ToastRequest
        {
            public Func<Task<UIPanelHandle>> open;
            public Action<UIPanelHandle> complete;
            public float duration;
        }

        private readonly Dictionary<string, PanelRecord> panelRecords = new Dictionary<string, PanelRecord>();
        private readonly Dictionary<string, PendingPanel> pendingPanels = new Dictionary<string, PendingPanel>();
        private readonly Dictionary<string, ILifetimeScope> panelScopes = new Dictionary<string, ILifetimeScope>();
        private readonly Dictionary<Type, UILoadInfoAttribute> loadInfoCache = new Dictionary<Type, UILoadInfoAttribute>();
        private readonly Dictionary<string, Type> panelTypesByPath = new Dictionary<string, Type>();
        private readonly Transform[] layerParents = new Transform[5];
        private readonly List<PanelRecord>[] layerStacks =
        {
            new List<PanelRecord>(),
            new List<PanelRecord>(),
            new List<PanelRecord>(),
            new List<PanelRecord>(),
            new List<PanelRecord>()
        };
        private readonly Queue<ToastRequest> toastQueue = new Queue<ToastRequest>();
        private readonly Dictionary<string, Stack<GameObject>> panelPool =
            new Dictionary<string, Stack<GameObject>>();
        [SerializeField] private int maxPooledPerPath = 2;
        private Transform panelPoolRoot;
        private int poolHits;
        private int poolMisses;
        private int instancesCreated;
        private int instancesReturned;
        private int instancesDestroyed;
        private int generationSeed;
        private int sequenceSeed;
        private bool toastPumpRunning;
        private bool destroyed;
        private bool pipelineHooked;
        private bool sceneLoadedHooked;
        private Camera uiCameraStackOwner;

        /// <summary>每个面板路径最多缓存的空闲实例数；0 表示不缓存。</summary>
        public int MaxPooledPerPath
        {
            get { return maxPooledPerPath; }
            set { maxPooledPerPath = Mathf.Max(0, value); }
        }

        public Camera uiCamera;
        public Canvas uiCanvas;
        private EventSystem uiEventSystem;
        private RectTransform canvasRectTransform;
        private bool ownsCanvas;
        private bool ownsCamera;
        private bool warnedInputModule;
        private float uiScale = 1f;

        private IAssetService assetService;
        private IGameServices gameServices;
        private ILogService log = LogService.Default;

        public int DiagnosticOpenPanelCount => panelRecords.Count;
        public int DiagnosticPendingPanelCount => pendingPanels.Count;
        public int DiagnosticPooledPanelCount
        {
            get
            {
                int count = 0;
                foreach (Stack<GameObject> stack in panelPool.Values) count += stack.Count;
                return count;
            }
        }

        /// <summary>采集面板状态表与对象池统计；主线程调用。</summary>
        public UIDiagnosticsSnapshot CaptureDiagnostics()
        {
            UIDiagnosticsSnapshot snapshot = new UIDiagnosticsSnapshot
            {
                OpenPanels = panelRecords.Count,
                PendingPanels = pendingPanels.Count,
                PooledPanels = DiagnosticPooledPanelCount,
                PoolHits = poolHits,
                PoolMisses = poolMisses,
                InstancesCreated = instancesCreated,
                InstancesReturned = instancesReturned,
                InstancesDestroyed = instancesDestroyed
            };

            List<PanelRecord> ordered = GetGlobalOrder();
            for (int i = 0; i < ordered.Count; i++)
            {
                PanelRecord record = ordered[i];
                if (record == null) continue;
                snapshot.Panels.Add(new UIPanelDiagnostic
                {
                    Key = record.path,
                    PanelType = record.panelType != null ? record.panelType.Name : "null",
                    Layer = record.layer,
                    State = record.state == PanelState.Open ? UIPanelState.Opened : UIPanelState.Closing,
                    Order = record.order,
                    Modal = record.modal,
                    Paused = record.paused,
                    Pooled = record.pool,
                    LoadMilliseconds = record.loadMilliseconds,
                    OpenMilliseconds = record.openMilliseconds,
                    Generation = record.generation
                });
            }

            foreach (KeyValuePair<string, Stack<GameObject>> pair in panelPool)
            {
                if (pair.Value == null || pair.Value.Count == 0) continue;
                snapshot.Pool.Add(new UIPoolEntryDiagnostic
                {
                    Key = pair.Key,
                    Count = pair.Value.Count
                });
            }
            return snapshot;
        }

        /// <summary>注入 UI 的资源与日志依赖。资源服务必填；日志为空时使用默认 LogService。</summary>
        public void Configure(IAssetService service, ILogService logger = null)
        {
            assetService = service ?? throw new ArgumentNullException(nameof(service));
            if (logger != null) log = logger;
        }

        /// <summary>
        /// 注入完整服务集合。面板实例化后会把 <see cref="IGameServicesConsumer"/> 与
        /// <see cref="IUIServiceConsumer"/> 注入到面板整棵子树。
        /// </summary>
        public void Configure(IGameServices services)
        {
            DetachSettingsEvents();
            gameServices = services ?? throw new ArgumentNullException(nameof(services));
            assetService = services.Assets ?? throw new ArgumentException("IGameServices.Assets 不能为空。", nameof(services));
            if (services.Log != null) log = services.Log;
            if (services.Settings != null)
            {
                services.Settings.Changed += OnSettingsChanged;
                ApplyUiScaleFromSettings();
            }
        }

        /// <summary>设置里的 accessibility.uiScale 是 UI 缩放的唯一玩家来源；手动 SetUiScale 仍可用于调试。</summary>
        private void OnSettingsChanged(SettingsChange change)
        {
            if (change.Key == "accessibility.uiScale") ApplyUiScaleFromSettings();
        }

        private void ApplyUiScaleFromSettings()
        {
            if (gameServices == null || gameServices.Settings == null || !gameServices.Settings.IsLoaded) return;
            try
            {
                float scale = gameServices.Settings.Get<float>("accessibility.uiScale");
                uiScale = UICanvasScale.ClampScale(scale);
                if (uiCanvas != null) ApplyUiScale();
            }
            catch (Exception exception)
            {
                log.Warning("[UIManager] 读取 accessibility.uiScale 失败：" + exception.Message);
            }
        }

        private void DetachSettingsEvents()
        {
            if (gameServices != null && gameServices.Settings != null)
                gameServices.Settings.Changed -= OnSettingsChanged;
        }


        /// <summary>
        /// 兼容旧接入：把旧的资源门面转成 IAssetService（内部仍然只有一条加载路径）。
        /// </summary>
        [Obsolete("UIManager 只通过 IAssetService 取资源；请改用 Configure(IAssetService) 或 services.Assets。")]
        public void SetResourcesLoader(IAssetsLoader loader, ResourcesLoader resources)
        {
            if (assetService != null) return;
            log.Warning("[UIManager] SetResourcesLoader 已过时：内部将转换为 IAssetService。");
            if (loader != null && !(loader is AssetsLoader))
                log.Warning("[UIManager] 传入的 IAssetsLoader 不是 AssetsLoader，无法转换，已忽略。");
            if (resources == null && !(loader is AssetsLoader)) return;
            Configure(new AssetService(resources, loader as AssetsLoader), log);
        }

        void Reset()
        {
            Init();
        }

        /// <summary>
        /// 重置Canvas。已有面板或加载中请求时拒绝切换，避免打开到一半的面板落在旧 Canvas 上。
        /// 自定义 Canvas 缺少层级节点时会自动补齐。
        /// </summary>
        public void ResetCanvas(Canvas arg)
        {
            if (arg == null)
            {
                log.Error("[UIManager] ResetCanvas 收到 null。");
                return;
            }

            if (ReferenceEquals(uiCanvas, arg))
            {
                canvasRectTransform = arg.transform as RectTransform;
                EnsureCanvasSettings();
                EnsureLayers();
                EnsureEventSystem();
                return;
            }

            if (panelRecords.Count > 0 || pendingPanels.Count > 0)
            {
                log.Error("[UIManager] 已有面板打开或正在加载，拒绝切换 Canvas；请先关闭面板。");
                return;
            }

            RectTransform rect = arg.transform as RectTransform;
            if (rect == null)
            {
                log.Error("[UIManager] ResetCanvas 的 Canvas 根节点需要 RectTransform。");
                return;
            }

            Canvas previous = uiCanvas;
            uiCanvas = arg;
            canvasRectTransform = rect;
            if (!arg.gameObject.activeSelf) arg.gameObject.SetActive(true);

            EnsureCanvasSettings();
            EnsureLayers();
            EnsureEventSystem();
            if (!Mathf.Approximately(uiScale, 1f)) ApplyUiScale();

            if (previous != null && previous != arg && ownsCanvas)
                previous.gameObject.SetActive(false);
            ownsCanvas = false;
        }

        /// <summary>
        /// 重置相机。只停用管理器自己创建的旧相机，避免误停用场景相机。
        /// </summary>
        public void ResetCamera(Camera arg)
        {
            if (arg == null)
            {
                log.Error("[UIManager] ResetCamera 收到 null。");
                return;
            }
            if (ReferenceEquals(uiCamera, arg))
            {
                ApplyUiCameraPipeline();
                return;
            }

            Camera previous = uiCamera;
            DetachOverlay();
            uiCamera = arg;
            if (uiCanvas != null && uiCanvas.renderMode == RenderMode.ScreenSpaceCamera)
                uiCanvas.worldCamera = arg;
            ApplyUiCameraPipeline();
            if (previous != null && previous != arg && ownsCamera)
                previous.gameObject.SetActive(false);
            ownsCamera = false;
        }

        public void Init()
        {
            if (destroyed) return;

            if (uiCamera == null)
            {
                uiCamera = InitCamera();
                ownsCamera = true;
            }
            HookPipelineChanges();
            HookSceneLoaded();
            ApplyUiCameraPipeline();
            if (uiCanvas == null)
            {
                uiCanvas = InitUICanvas(uiCamera);
                ownsCanvas = true;
            }

            canvasRectTransform = uiCanvas != null ? uiCanvas.transform as RectTransform : null;
            if (canvasRectTransform == null)
            {
                log.Error("[UIManager] Canvas 根节点缺少 RectTransform，UI 无法初始化。");
                return;
            }

            EnsureCanvasSettings();
            EnsureLayers();
            EnsureEventSystem();
            if (!Mathf.Approximately(uiScale, 1f)) ApplyUiScale();
        }

        private void HookPipelineChanges()
        {
            if (pipelineHooked) return;
            RenderPipelineManager.activeRenderPipelineTypeChanged += OnActiveRenderPipelineChanged;
            pipelineHooked = true;
        }

        private void UnhookPipelineChanges()
        {
            if (!pipelineHooked) return;
            RenderPipelineManager.activeRenderPipelineTypeChanged -= OnActiveRenderPipelineChanged;
            pipelineHooked = false;
        }

        /// <summary>画质档位切换 SRP 时补/更新 URP Camera Stack 与 HDRP 数据，不重建 Canvas。</summary>
        private void OnActiveRenderPipelineChanged()
        {
            if (destroyed) return;
            DetachOverlay();
            ApplyUiCameraPipeline();
            log.Info("[UIManager] 渲染管线变化为 " + UICameraPipeline.Detect() + "，已重新适配 UI 相机。");
            EnsureCanvasSettings();
        }

        private void HookSceneLoaded()
        {
            if (sceneLoadedHooked) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            sceneLoadedHooked = true;
        }

        private void UnhookSceneLoaded()
        {
            if (!sceneLoadedHooked) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            sceneLoadedHooked = false;
        }

        /// <summary>主相机通常随场景加载出现；场景切换后重新尝试加入 URP Camera Stack。</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (destroyed) return;
            ApplyUiCameraPipeline();
        }

        /// <summary>
        /// 重新协商 UI 相机与主相机的关系：
        /// URP 优先方案 B（UI 相机 Overlay + 主相机 Camera Stack）；没有主相机时回退 Base，
        /// Built-in/HDRP 继续使用 Base/HDRP 适配。
        /// </summary>
        private void ApplyUiCameraPipeline()
        {
            if (destroyed || uiCamera == null) return;

            UIRenderPipelineKind kind = UICameraPipeline.Detect();
            if (kind == UIRenderPipelineKind.Universal)
            {
                Camera main = Camera.main;
                if (main != null && main != uiCamera && main.isActiveAndEnabled)
                {
                    if (uiCameraStackOwner != null && uiCameraStackOwner != main)
                        UICameraPipeline.RemoveFromStack(uiCameraStackOwner, uiCamera);
                    if (UICameraPipeline.ConfigureOverlay(main, uiCamera))
                    {
                        uiCameraStackOwner = main;
                        return;
                    }
                }

                if (uiCameraStackOwner != null)
                {
                    UICameraPipeline.RemoveFromStack(uiCameraStackOwner, uiCamera);
                    uiCameraStackOwner = null;
                }
                UICameraPipeline.MakeBase(uiCamera);
                return;
            }

            DetachOverlay();
            UICameraPipeline.Adapt(uiCamera, kind);
        }

        /// <summary>手动重新适配（例如运行中才生成/切换主相机）。</summary>
        public void RefreshCameraStack()
        {
            ApplyUiCameraPipeline();
        }

        /// <summary>当前持有 URP Camera Stack 的主相机；未使用 Camera Stack 时为 null。</summary>
        public Camera UiCameraStackOwner => uiCameraStackOwner;

        private void DetachOverlay()
        {
            if (uiCameraStackOwner != null && uiCamera != null)
                UICameraPipeline.RemoveFromStack(uiCameraStackOwner, uiCamera);
            uiCameraStackOwner = null;
        }

        /// <summary>
        /// 设置 UI 缩放（0.5–2，1 为设计分辨率）。Canvas 尚未初始化时先记录，初始化后生效。
        /// 需要 Canvas 上有 CanvasScaler；没有时记录错误并保持原缩放。
        /// </summary>
        public void SetUiScale(float scale)
        {
            uiScale = UICanvasScale.ClampScale(scale);
            ApplyUiScale();
        }

        /// <summary>当前 UI 缩放；Canvas 上有 <see cref="UICanvasScale"/> 时以其值为准。</summary>
        public float UiScale
        {
            get
            {
                if (uiCanvas != null)
                {
                    UICanvasScale adapter = uiCanvas.GetComponent<UICanvasScale>();
                    if (adapter != null) return adapter.Scale;
                }
                return uiScale;
            }
        }

        private void ApplyUiScale()
        {
            if (destroyed || uiCanvas == null) return;
            if (uiCanvas.GetComponent<CanvasScaler>() == null)
            {
                log.Error("[UIManager] 当前 Canvas 没有 CanvasScaler，无法应用 UI 缩放。");
                return;
            }

            UICanvasScale adapter = uiCanvas.GetComponent<UICanvasScale>();
            if (adapter == null) adapter = uiCanvas.gameObject.AddComponent<UICanvasScale>();
            adapter.Scale = uiScale;
        }

        private Camera InitCamera()
        {
            GameObject obj = new GameObject("UICamera");
            obj.transform.SetParent(transform, false);
            Camera camera = obj.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Depth;
            camera.cullingMask = 1 << UiLayer;
            camera.depth = UICameraPipeline.DefaultDepth;
            UICameraPipeline.Adapt(camera);
            return camera;
        }

        private Canvas InitUICanvas(Camera camera)
        {
            GameObject obj = new GameObject("Canvas", typeof(RectTransform));
            RectTransform canvasRect = obj.GetComponent<RectTransform>();
            canvasRect.SetParent(transform, false);
            canvasRect.localScale = Vector3.one;
            obj.layer = UiLayer;
            Canvas canvas = obj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.sortingOrder = -10;
            CanvasScaler canvasScaler = obj.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvasScaler.referencePixelsPerUnit = 100;
            obj.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static void ApplyPlacement(RectTransform rect, UIPanelPlacement placement)
        {
            if (rect == null || placement == UIPanelPlacement.Preserve) return;

            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;

            if (placement == UIPanelPlacement.Window)
            {
                rect.anchoredPosition = Vector2.zero;
                return;
            }

            // FullScreen：保留旧行为，铺满所在层级。
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void EnsureCanvasSettings()
        {
            if (uiCanvas == null) return;
            if (!uiCanvas.gameObject.activeSelf) uiCanvas.gameObject.SetActive(true);
            if (uiCanvas.GetComponent<GraphicRaycaster>() == null)
                uiCanvas.gameObject.AddComponent<GraphicRaycaster>();
            if (uiCanvas.renderMode == RenderMode.ScreenSpaceCamera && uiCanvas.worldCamera == null)
                uiCanvas.worldCamera = uiCamera;
        }

        /// <summary>
        /// 查找层级节点；旧场景的 Bot/Mid/Top/System 与新命名 Background/Main/Popup/Guide/Toast 都兼容。
        /// 缺失节点会自动创建，避免 ResetCanvas 后 GetLayerFather 永久为 null。
        /// </summary>
        private void EnsureLayers()
        {
            if (canvasRectTransform == null) return;
            layerParents[0] = EnsureLayer("Background", "Bot", 0);
            layerParents[1] = EnsureLayer("Main", "Mid", 1);
            layerParents[2] = EnsureLayer("Popup", "Top", 2);
            layerParents[3] = EnsureLayer("Guide", "System", 3);
            layerParents[4] = EnsureLayer("Toast", null, 4);
        }

        private Transform EnsureLayer(string primaryName, string legacyName, int siblingIndex)
        {
            Transform node = canvasRectTransform.Find(primaryName);
            if (node == null && !string.IsNullOrEmpty(legacyName))
                node = canvasRectTransform.Find(legacyName);

            if (node == null)
            {
                GameObject go = new GameObject(primaryName, typeof(RectTransform));
                node = go.transform;
                node.SetParent(canvasRectTransform, false);
                SetRect((RectTransform)node, canvasRectTransform);
            }

            if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
            if (node is RectTransform rect) SetRect(rect, canvasRectTransform);
            node.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, canvasRectTransform.childCount - 1));
            return node;
        }

        private void SetRect(RectTransform rect, RectTransform parent)
        {
            rect.SetParent(parent, false);
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            rect.gameObject.layer = UiLayer;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static int UiLayer
        {
            get
            {
                int layer = LayerMask.NameToLayer("UI");
                return layer >= 0 ? layer : 5;
            }
        }

        /// <summary>确保场景中有可用的 EventSystem；默认输入模块按当前 Input Handling 选择。</summary>
        private void EnsureEventSystem()
        {
            if (uiEventSystem != null)
            {
                EnsureInputModule(uiEventSystem);
                return;
            }

            EventSystem found = FindEventSystem();
            if (found == null)
            {
                GameObject eventObj = new GameObject("EventSystem");
                Transform parent = transform.parent != null ? transform.parent : transform;
                eventObj.transform.SetParent(parent, false);
                eventObj.layer = UiLayer;
                found = eventObj.AddComponent<EventSystem>();
                AddDefaultInputModule(eventObj);
            }
            else
            {
                if (!found.gameObject.activeSelf)
                {
                    log.Warning("[UIManager] 找到未激活的 EventSystem，已激活：" + found.name);
                    found.gameObject.SetActive(true);
                }
                EnsureInputModule(found);
            }

            uiEventSystem = found;
        }

        private static EventSystem FindEventSystem()
        {
            EventSystem[] all = Resources.FindObjectsOfTypeAll<EventSystem>();
            for (int i = 0; i < all.Length; i++)
            {
                EventSystem candidate = all[i];
                if (candidate != null && candidate.gameObject.scene.IsValid()) return candidate;
            }
            return null;
        }

        private void EnsureInputModule(EventSystem eventSystem)
        {
            if (eventSystem == null) return;
            BaseInputModule module = eventSystem.GetComponent<BaseInputModule>();
            if (module != null)
            {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                // Input System-only 工程里 StandaloneInputModule 不能驱动 UGUI，替换为官方模块。
                if (module is StandaloneInputModule)
                {
                    module.enabled = false;
                    Destroy(module);
                    AddDefaultInputModule(eventSystem.gameObject);
                    log.Warning("[UIManager] 已将 StandaloneInputModule 替换为 InputSystemUIInputModule。");
                    return;
                }
#endif
#if ENABLE_INPUT_SYSTEM
                if (!warnedInputModule && module is StandaloneInputModule)
                {
                    warnedInputModule = true;
                    log.Warning("[UIManager] 当前 EventSystem 使用 StandaloneInputModule；New Input System 工程请改用 InputSystemUIInputModule。");
                }
#endif
                return;
            }
            AddDefaultInputModule(eventSystem.gameObject);
        }

        /// <summary>
        /// 按工程 Active Input Handling 给 EventSystem 添加默认输入模块：
        /// 启用新 Input System 时优先 InputSystemUIInputModule，否则/包缺失时回退 StandaloneInputModule。
        /// <para>公开给演示生成器/测试复用，避免在 New-only 工程里硬加 StandaloneInputModule。</para>
        /// </summary>
        public static void AddDefaultInputModule(GameObject eventSystem)
        {
#if ENABLE_INPUT_SYSTEM
            // 通过类型名引用，避免 UI 程序集硬依赖 Unity.InputSystem；
            // 包不存在时回退旧输入模块，保证嵌入源码的项目也能编译。
            Type inputSystemModule = Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModule != null)
            {
                eventSystem.AddComponent(inputSystemModule);
                return;
            }
#endif
            eventSystem.AddComponent<StandaloneInputModule>();
        }

        /// <summary>
        /// 通过层级枚举 得到对应层级的父对象
        /// </summary>
        public Transform GetLayerFather(E_UI_Layer layer)
        {
            if (layerParents[0] == null && canvasRectTransform != null) EnsureLayers();
            int index = (int)layer;
            return index >= 0 && index < layerParents.Length ? layerParents[index] : null;
        }

        /// <summary>回调与 Task API 共用加载流程，同一路径并发请求只创建一个面板。</summary>
        public async void ShowPanel<T>(object panelArg = null, UnityAction<T> callBack = null) where T : BasePanel
        {
            T panel = null;
            try { panel = await ShowPanelAsync<T>(panelArg); }
            catch (Exception e) { log.LogException(e); }
            ResourcesLoader.InvokeCallbacks(callBack, panel);
        }

        public Task<T> ShowPanelAsync<T>(object panelArg = null) where T : BasePanel => ShowPanelCoreAsync<T>(null, panelArg);

        public Task<T> ShowPanelInScopeAsync<T>(ILifetimeScope scope, object panelArg = null) where T : BasePanel
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            return ShowPanelCoreAsync<T>(scope, panelArg);
        }

        public async Task<UIPanelHandle<T>> OpenAsync<T>(object panelArg = null) where T : BasePanel
        {
            BasePanel panel = await ShowPanelCoreAsync<T>(null, panelArg);
            return CreateHandle<T>(panel);
        }

        public async Task<UIPanelHandle<T>> OpenInScopeAsync<T>(ILifetimeScope scope, object panelArg = null) where T : BasePanel
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            BasePanel panel = await ShowPanelCoreAsync<T>(scope, panelArg);
            return CreateHandle<T>(panel);
        }

        /// <summary>
        /// 非泛型打开：热更新程序集在运行时才知道面板类型时使用；只依赖 Type，不触发 AOT 泛型实例化。
        /// </summary>
        public async Task<UIPanelHandle> OpenAsync(Type panelType, object panelArg = null)
        {
            BasePanel panel = await ShowPanelCoreAsync(panelType, null, panelArg);
            return CreateHandle(panel);
        }

        public async Task<UIPanelHandle> OpenInScopeAsync(Type panelType, ILifetimeScope scope, object panelArg = null)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            BasePanel panel = await ShowPanelCoreAsync(panelType, scope, panelArg);
            return CreateHandle(panel);
        }

        /// <summary>按 UILoadInfo.loadPath 打开已注册的面板类型；适合热更配置/表驱动。</summary>
        public async Task<UIPanelHandle> OpenAsync(string loadPath, object panelArg = null)
        {
            Type panelType = ResolvePanelType(loadPath);
            if (panelType == null)
            {
                log.Error("[UIManager] 未注册的面板路径：" + loadPath);
                return null;
            }
            return await OpenAsync(panelType, panelArg);
        }

        public Task<UIPanelHandle<T>> ShowToastAsync<T>(object panelArg = null, float duration = 1.5f) where T : BasePanel
        {
            if (destroyed) return Task.FromResult<UIPanelHandle<T>>(null);

            UILoadInfoAttribute info = GetLoadInfo<T>();
            if (info == null || info.ui_Layer != E_UI_Layer.Toast)
            {
                log.Error("[UIManager] Toast 面板必须声明 UILoadInfo(E_UI_Layer.Toast)："
                          + typeof(T).Name);
                return Task.FromResult<UIPanelHandle<T>>(null);
            }

            TaskCompletionSource<UIPanelHandle<T>> completion =
                new TaskCompletionSource<UIPanelHandle<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
            toastQueue.Enqueue(new ToastRequest
            {
                open = async () => (UIPanelHandle)await OpenAsync<T>(panelArg),
                complete = handle => completion.TrySetResult(handle as UIPanelHandle<T>),
                duration = duration
            });
            if (!toastPumpRunning) _ = PumpToastQueueAsync();
            return completion.Task;
        }

        public Task<UIPanelHandle> ShowToastAsync(Type panelType, object panelArg = null, float duration = 1.5f)
        {
            if (destroyed) return Task.FromResult<UIPanelHandle>(null);

            UILoadInfoAttribute info = GetLoadInfo(panelType);
            if (info == null || info.ui_Layer != E_UI_Layer.Toast)
            {
                log.Error("[UIManager] Toast 面板必须声明 UILoadInfo(E_UI_Layer.Toast)："
                          + (panelType != null ? panelType.Name : "null"));
                return Task.FromResult<UIPanelHandle>(null);
            }

            TaskCompletionSource<UIPanelHandle> completion =
                new TaskCompletionSource<UIPanelHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
            toastQueue.Enqueue(new ToastRequest
            {
                open = () => OpenAsync(panelType, panelArg),
                complete = handle => completion.TrySetResult(handle),
                duration = duration
            });
            if (!toastPumpRunning) _ = PumpToastQueueAsync();
            return completion.Task;
        }

        private async Task PumpToastQueueAsync()
        {
            if (toastPumpRunning) return;
            toastPumpRunning = true;
            try
            {
                while (!destroyed && toastQueue.Count > 0)
                {
                    ToastRequest request = toastQueue.Dequeue();
                    UIPanelHandle handle = null;
                    try { handle = await request.open(); }
                    catch (Exception e) { log.LogException(e); }

                    request.complete?.Invoke(handle);
                    if (handle == null) continue;

                    if (request.duration > 0f)
                    {
                        try { await Task.Delay(TimeSpan.FromSeconds(request.duration)); }
                        catch (Exception e) { log.LogException(e); }
                    }
                    if (handle.IsOpen) await handle.CloseAsync();
                }
            }
            finally
            {
                toastPumpRunning = false;
                if (!destroyed && toastQueue.Count > 0) _ = PumpToastQueueAsync();
            }
        }

        private UIPanelHandle CreateHandle(BasePanel panel)
        {
            if (panel == null) return null;
            UILoadInfoAttribute info = GetLoadInfo(panel.GetType());
            if (info == null) return null;
            if (!panelRecords.TryGetValue(info.loadPath, out PanelRecord record)) return null;
            return new UIPanelHandle(this, record.path, record.panelType, record.layer, record.generation);
        }

        private UIPanelHandle<T> CreateHandle<T>(BasePanel panel) where T : BasePanel
        {
            UIPanelHandle handle = CreateHandle(panel);
            if (handle == null) return null;
            return new UIPanelHandle<T>(this, handle.path, handle.panelType, handle.layer, handle.generation);
        }

        private async Task<T> ShowPanelCoreAsync<T>(ILifetimeScope scope, object panelArg) where T : BasePanel
        {
            BasePanel panel = await ShowPanelCoreAsync(typeof(T), scope, panelArg);
            return panel as T;
        }

        private async Task<BasePanel> ShowPanelCoreAsync(Type panelType, ILifetimeScope scope, object panelArg)
        {
            if (destroyed || (scope != null && scope.Token.IsCancellationRequested)) return null;
            if (panelType == null || !typeof(BasePanel).IsAssignableFrom(panelType))
            {
                log.Error("[UIManager] 打开面板的类型必须继承 BasePanel：" + panelType);
                return null;
            }

            UILoadInfoAttribute info = GetLoadInfo(panelType);
            if (info == null || string.IsNullOrEmpty(info.loadPath))
            {
                log.Error(panelType.Name + " 必须标记有效的 UILoadInfo 特性");
                return null;
            }

            string path = info.loadPath;
            if (panelRecords.TryGetValue(path, out PanelRecord record))
            {
                if (record.panel == null)
                {
                    RemoveRecord(record);
                }
                else
                {
                    if (record.panelType != panelType)
                    {
                        log.Error("[UIManager] 同一路径不能绑定不同面板类型：" + path);
                        return null;
                    }
                    if (!ScopeMatches(record.scope, scope))
                    {
                        log.Error("[UIManager] 同一路径面板不能由不同作用域共享：" + path);
                        return null;
                    }
                    if (record.state != PanelState.Open) return null;

                    // 已打开：默认置顶并刷新，不重复执行 OnOpen，避免重复订阅。
                    BringRecordToFront(record);
                    try { record.panel.OnRefresh(panelArg); }
                    catch (Exception e) { log.LogException(e); }
                    return record.panel;
                }
            }

            if (pendingPanels.TryGetValue(path, out PendingPanel pending))
            {
                if (pending.panelType != panelType)
                {
                    log.Error("[UIManager] 同一路径正在加载不同面板类型：" + path);
                    return null;
                }
                if (!ScopeMatches(pending.scope, scope))
                {
                    log.Error("[UIManager] 同一路径面板不能由不同作用域共享：" + path);
                    return null;
                }

                BasePanel shared = await pending.completion.Task;
                return ValidateResult(path, shared, panelType, scope);
            }

            pending = new PendingPanel
            {
                path = path,
                panelType = panelType,
                panelArg = panelArg,
                scope = scope,
                generation = ++generationSeed,
                startedAt = Time.realtimeSinceStartup
            };
            pending.requestCts = scope != null
                ? CancellationTokenSource.CreateLinkedTokenSource(scope.Token)
                : new CancellationTokenSource();
            pendingPanels[path] = pending;
            if (scope != null)
                pending.cancellation = scope.Token.Register(() => CancelPending(path, pending));

            _ = CreatePanel(info, pending);

            BasePanel panel = await pending.completion.Task;
            return ValidateResult(path, panel, panelType, scope);
        }

        private async Task CreatePanel(UILoadInfoAttribute info, PendingPanel pending)
        {
            string path = info.loadPath;
            GameObject instance = null;
            AssetLease<GameObject> prefabLease = null;

            try
            {
                Transform parent = GetLayerFather(info.ui_Layer);
                if (parent == null)
                {
                    Init();
                    parent = GetLayerFather(info.ui_Layer);
                }
                if (parent == null) throw new InvalidOperationException("UI 层级不存在：" + info.ui_Layer);
                if (!parent.gameObject.activeSelf) parent.gameObject.SetActive(true);

                bool fromPool = info.pool && TryTakePooled(path, out instance);
                if (fromPool)
                {
                    instance.transform.SetParent(parent, false);
                }
                else
                {
                    if (info.pool) poolMisses++;
                    if (assetService == null)
                        throw new InvalidOperationException("UIManager 尚未注入 IAssetService：请通过 GameServices/UPGameRoot 装配后再打开面板。");

                    prefabLease = await assetService.AcquireAsync<GameObject>(path, info.loadMethod, pending.requestCts.Token);
                    if (!IsPendingActive(path, pending) || pending.requestCts.IsCancellationRequested) return;

                    GameObject prefab = prefabLease?.Asset;
                    if (prefab == null) throw new InvalidOperationException("UI 资源加载失败：" + path);

                    instance = Instantiate(prefab, parent, false);
                    instancesCreated++;
                }

                RectTransform rect = instance.transform as RectTransform;
                if (rect == null) throw new InvalidOperationException("UI 预制体根节点需要 RectTransform");

                // 默认隐藏的预制体在 inactive 状态下不会执行 Awake；先激活再绑定控件。
                if (!instance.activeSelf) instance.SetActive(true);
                ApplyPlacement(rect, info.placement);

                BasePanel panel = GetOrAddPanelComponent(instance, pending.panelType);
                panel.AttachManager(this);
                panel.AttachServices(gameServices);
                InjectPanel(panel.gameObject);

                if (!IsPendingActive(path, pending) || pending.requestCts.IsCancellationRequested) return;

                if (prefabLease != null)
                {
                    instance.AddComponent<PanelResourceLease>().Initialize(
                        prefabLease, () => OnPanelDestroyed(path, panel));
                    prefabLease = null;
                }

                CanvasGroup group = panel.GetComponent<CanvasGroup>();
                if (group == null) group = panel.gameObject.AddComponent<CanvasGroup>();

                PanelRecord record = new PanelRecord
                {
                    path = path,
                    panelType = pending.panelType,
                    panel = panel,
                    scope = pending.scope,
                    state = PanelState.Open,
                    generation = pending.generation,
                    info = info,
                    layer = info.ui_Layer,
                    order = info.order,
                    modal = info.modal,
                    pauseGame = info.pauseGame,
                    closeOnBack = info.closeOnBack,
                    pool = info.pool,
                    group = group,
                    initialInteractable = group.interactable,
                    initialBlocksRaycasts = group.blocksRaycasts,
                    loadMilliseconds = (Time.realtimeSinceStartup - pending.startedAt) * 1000.0
                };
                panelRecords[path] = record;
                panelDic[path] = panel;
                panelScopes[path] = pending.scope;
                pendingPanels.Remove(path);

                // 从这里开始面板对象由记录和销毁回调接管，finally 不再重复 Destroy。
                instance = null;

                try
                {
                    AddToLayer(record);
                    AcquirePanelState(record);
                }
                catch
                {
                    CloseRecord(record);
                    throw;
                }

                if (pending.scope != null)
                {
                    try
                    {
                        pending.scope.Own(new DisposableAction(() => CloseIfCurrent(path, panel, record)));
                    }
                    catch
                    {
                        CloseRecord(record);
                        throw;
                    }
                }

                float openStartedAt = Time.realtimeSinceStartup;
                try
                {
                    panel.OnOpen(pending.panelArg);
                }
                catch (Exception e)
                {
                    log.LogException(e);
                    record.openMilliseconds = (Time.realtimeSinceStartup - openStartedAt) * 1000.0;
                    CloseRecord(record);
                    pending.completion.TrySetResult(null);
                    return;
                }
                record.openMilliseconds = (Time.realtimeSinceStartup - openStartedAt) * 1000.0;

                // OnOpen 内可能自关闭或重开；只有记录仍是当前实例且仍处于 Open 才算成功。
                if (!IsCurrent(record) || record.state != PanelState.Open)
                {
                    pending.completion.TrySetResult(null);
                    return;
                }

                pending.completion.TrySetResult(panel);
            }
            catch (OperationCanceledException)
            {
                // 关闭或作用域取消：等待者由 ClosePanel/CancelPending 结束。
            }
            catch (Exception e)
            {
                log.LogException(e);
            }
            finally
            {
                pending.cancellation.Dispose();
                pending.requestCts.Dispose();
                prefabLease?.Dispose();
                if (instance != null)
                {
                    Destroy(instance);
                    instancesDestroyed++;
                }
                if (IsPendingActive(path, pending)) pendingPanels.Remove(path);
                pending.completion.TrySetResult(null);
            }
        }

        private BasePanel ValidateResult(string path, BasePanel panel, Type panelType, ILifetimeScope scope)
        {
            if (destroyed || panel == null) return null;
            if (scope != null && scope.Token.IsCancellationRequested) return null;
            if (!panelRecords.TryGetValue(path, out PanelRecord record) ||
                !ReferenceEquals(record.panel, panel) ||
                record.state != PanelState.Open)
                return null;

            if (panelType == null || !panelType.IsInstanceOfType(panel))
            {
                log.Error("[UIManager] 面板类型与请求不匹配：" + path);
                return null;
            }
            return panel;
        }

        private void CancelPending(string path, PendingPanel pending)
        {
            if (pending == null) return;
            pending.closed = true;
            if (pendingPanels.TryGetValue(path, out PendingPanel active) && ReferenceEquals(active, pending))
                pendingPanels.Remove(path);

            try { pending.requestCts?.Cancel(); }
            catch (Exception e) { log.LogException(e); }
            pending.completion.TrySetResult(null);
        }

        /// <summary>关闭已显示面板，并使尚未完成的打开请求返回 null。</summary>
        public void ClosePanel<T>() where T : BasePanel
        {
            ClosePanel(typeof(T));
        }

        /// <summary>按 Type 关闭面板；热更新代码使用此入口避免 AOT 泛型实例化。</summary>
        public void ClosePanel(Type panelType)
        {
            UILoadInfoAttribute info = GetLoadInfo(panelType);
            if (info == null) throw new ArgumentException((panelType != null ? panelType.Name : "null") + " 必须标记 UILoadInfo 特性");
            string path = info.loadPath;

            if (pendingPanels.TryGetValue(path, out PendingPanel pending))
                CancelPending(path, pending);

            if (!panelRecords.TryGetValue(path, out PanelRecord record)) return;
            if (record.panelType != panelType)
            {
                log.Error("[UIManager] 关闭类型与已打开面板不匹配：" + path);
                return;
            }
            CloseRecord(record);
        }

        public Task<bool> CloseAsync(UIPanelHandle handle)
        {
            if (handle == null || handle.owner != this) return Task.FromResult(false);

            if (pendingPanels.TryGetValue(handle.path, out PendingPanel pending) &&
                pending.generation == handle.generation)
            {
                CancelPending(handle.path, pending);
                return Task.FromResult(true);
            }

            if (!panelRecords.TryGetValue(handle.path, out PanelRecord record) ||
                record.generation != handle.generation)
                return Task.FromResult(false);

            CloseRecord(record);
            return Task.FromResult(true);
        }

        public bool CloseTop(E_UI_Layer layer)
        {
            PanelRecord record = GetTopRecord(layer);
            if (record == null) return false;
            CloseRecord(record);
            return true;
        }

        public int CloseAll(E_UI_Layer? layer = null)
        {
            List<PanelRecord> records = GetGlobalOrder();
            int closed = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                PanelRecord record = records[i];
                if (record == null) continue;
                if (layer.HasValue && record.layer != layer.Value) continue;
                if (!IsCurrent(record)) continue;
                CloseRecord(record);
                closed++;
            }
            return closed;
        }

        /// <summary>
        /// 返回键处理：Toast 不参与返回栈；顶层面板若 closeOnBack=false 则拦截，
        /// 不会越过它关闭更下层面板。
        /// </summary>
        public bool HandleBack()
        {
            if (destroyed) return false;

            for (int layerIndex = layerStacks.Length - 1; layerIndex >= 0; layerIndex--)
            {
                if ((E_UI_Layer)layerIndex == E_UI_Layer.Toast) continue;
                List<PanelRecord> stack = layerStacks[layerIndex];
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    PanelRecord record = stack[i];
                    if (record == null || record.panel == null) continue;
                    if (!record.closeOnBack) return false;
                    CloseRecord(record);
                    return true;
                }
            }
            return false;
        }

        public IReadOnlyList<UIPanelHandle> GetOpenPanels(E_UI_Layer? layer = null)
        {
            List<UIPanelHandle> result = new List<UIPanelHandle>();
            List<PanelRecord> records = GetGlobalOrder();
            for (int i = 0; i < records.Count; i++)
            {
                PanelRecord record = records[i];
                if (record == null) continue;
                if (layer.HasValue && record.layer != layer.Value) continue;
                result.Add(new UIPanelHandle(this, record.path, record.panelType, record.layer, record.generation));
            }
            return result;
        }

        internal UIPanelState GetPanelState(string path, int generation)
        {
            if (destroyed) return UIPanelState.Closed;
            if (pendingPanels.TryGetValue(path, out PendingPanel pending) && pending.generation == generation)
                return UIPanelState.Loading;
            if (!panelRecords.TryGetValue(path, out PanelRecord record) || record.generation != generation)
                return UIPanelState.Closed;

            switch (record.state)
            {
                case PanelState.Open: return UIPanelState.Opened;
                case PanelState.Closing: return UIPanelState.Closing;
                default: return UIPanelState.Closed;
            }
        }

        internal BasePanel GetPanelForHandle(string path, int generation)
        {
            if (!panelRecords.TryGetValue(path, out PanelRecord record)) return null;
            if (record.generation != generation) return null;
            return record.state == PanelState.Open ? record.panel : null;
        }

        private void CloseIfCurrent(string path, BasePanel expected, PanelRecord record)
        {
            if (!panelRecords.TryGetValue(path, out PanelRecord current)) return;
            if (!ReferenceEquals(current, record) || !ReferenceEquals(record.panel, expected)) return;
            CloseRecord(record);
        }

        private void CloseRecord(PanelRecord record)
        {
            if (record == null) return;
            if (record.state == PanelState.Closing || record.state == PanelState.Closed) return;

            record.state = PanelState.Closing;
            panelRecords.Remove(record.path);
            panelDic.Remove(record.path);
            panelScopes.Remove(record.path);
            RemoveFromLayer(record);
            ReleasePanelState(record);

            BasePanel panel = record.panel;
            if (panel == null)
            {
                record.state = PanelState.Closed;
                RefreshModalBlocking();
                return;
            }

            try { panel.OnClose(); }
            catch (Exception e) { log.LogException(e); }
            finally
            {
                bool pooled = record.pool && ReturnToPool(record, panel);
                try
                {
                    if (pooled) panel.DisposeOpenEvents();
                    else panel.DisposeEventScopes();
                }
                catch (Exception e) { log.LogException(e); }

                if (!pooled && panel != null)
                {
                    Destroy(panel.gameObject);
                    instancesDestroyed++;
                }
                record.state = PanelState.Closed;
                RefreshModalBlocking();
            }
        }

        private bool ReturnToPool(PanelRecord record, BasePanel panel)
        {
            if (!record.pool || panel == null || maxPooledPerPath <= 0) return false;
            if (!panelPool.TryGetValue(record.path, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                panelPool[record.path] = stack;
            }
            if (stack.Count >= maxPooledPerPath) return false;

            if (record.group != null)
            {
                record.group.interactable = record.initialInteractable;
                record.group.blocksRaycasts = record.initialBlocksRaycasts;
            }

            panel.transform.SetParent(GetPanelPoolRoot(), false);
            panel.gameObject.SetActive(false);
            stack.Push(panel.gameObject);
            instancesReturned++;
            return true;
        }

        private bool TryTakePooled(string path, out GameObject instance)
        {
            instance = null;
            if (!panelPool.TryGetValue(path, out Stack<GameObject> stack)) return false;
            while (stack.Count > 0)
            {
                GameObject candidate = stack.Pop();
                if (candidate == null) continue;
                instance = candidate;
                poolHits++;
                return true;
            }
            return false;
        }

        private Transform GetPanelPoolRoot()
        {
            if (panelPoolRoot == null)
            {
                GameObject root = new GameObject("UIPanelPool");
                root.transform.SetParent(transform, false);
                root.SetActive(false);
                panelPoolRoot = root.transform;
            }
            return panelPoolRoot;
        }

        private void ClearPanelPool()
        {
            foreach (Stack<GameObject> stack in panelPool.Values)
            {
                while (stack.Count > 0)
                {
                    GameObject instance = stack.Pop();
                    if (instance == null) continue;
                    BasePanel panel = instance.GetComponent<BasePanel>();
                    try { panel?.DisposeEventScopes(); }
                    catch (Exception e) { log.LogException(e); }
                    Destroy(instance);
                    instancesDestroyed++;
                }
            }
            panelPool.Clear();
            if (panelPoolRoot != null)
            {
                Destroy(panelPoolRoot.gameObject);
                panelPoolRoot = null;
            }
        }

        private void AddToLayer(PanelRecord record)
        {
            int index = (int)record.layer;
            if (index < 0 || index >= layerStacks.Length)
            {
                log.Error("[UIManager] 非法 UI 层级：" + record.layer);
                return;
            }
            record.sequence = ++sequenceSeed;
            List<PanelRecord> stack = layerStacks[index];
            stack.Remove(record);
            stack.Add(record);
            SortLayer(index);
            RefreshModalBlocking();
        }

        private void RemoveFromLayer(PanelRecord record)
        {
            int index = (int)record.layer;
            if (index < 0 || index >= layerStacks.Length) return;
            layerStacks[index].Remove(record);
            SortLayer(index);
        }

        private void SortLayer(int index)
        {
            if (index < 0 || index >= layerStacks.Length) return;
            List<PanelRecord> stack = layerStacks[index];
            stack.Sort((left, right) =>
            {
                int orderCompare = left.order.CompareTo(right.order);
                return orderCompare != 0 ? orderCompare : left.sequence.CompareTo(right.sequence);
            });
            for (int i = 0; i < stack.Count; i++)
            {
                if (stack[i].panel != null) stack[i].panel.transform.SetSiblingIndex(i);
            }
        }

        private void BringRecordToFront(PanelRecord record)
        {
            if (record == null) return;
            int index = (int)record.layer;
            if (index < 0 || index >= layerStacks.Length) return;

            int maxOrder = 0;
            List<PanelRecord> stack = layerStacks[index];
            for (int i = 0; i < stack.Count; i++)
                if (stack[i].order > maxOrder) maxOrder = stack[i].order;
            record.order = maxOrder == int.MaxValue ? int.MaxValue : maxOrder + 1;
            record.sequence = ++sequenceSeed;
            SortLayer(index);
            RefreshModalBlocking();
        }

        private PanelRecord GetTopRecord(E_UI_Layer layer)
        {
            int index = (int)layer;
            if (index < 0 || index >= layerStacks.Length) return null;
            List<PanelRecord> stack = layerStacks[index];
            for (int i = stack.Count - 1; i >= 0; i--)
                if (stack[i] != null && stack[i].panel != null) return stack[i];
            return null;
        }

        private List<PanelRecord> GetGlobalOrder()
        {
            List<PanelRecord> result = new List<PanelRecord>();
            for (int layerIndex = 0; layerIndex < layerStacks.Length; layerIndex++)
            {
                List<PanelRecord> stack = layerStacks[layerIndex];
                for (int i = 0; i < stack.Count; i++)
                {
                    if (stack[i] != null && stack[i].panel != null) result.Add(stack[i]);
                }
            }
            return result;
        }

        private void AcquirePanelState(PanelRecord record)
        {
            if (record == null || gameServices == null) return;
            if (record.pauseGame && gameServices.Pause != null)
            {
                try { record.pauseLease = gameServices.Pause.Pause("UI:" + record.path); }
                catch (Exception e) { log.LogException(e); }
            }
            if (record.modal && gameServices.Input != null)
            {
                try { record.inputLease = gameServices.Input.PushContext(InputContexts.UI); }
                catch (Exception e) { log.LogException(e); }
            }
        }

        private void ReleasePanelState(PanelRecord record)
        {
            if (record == null) return;
            if (record.paused) SetPaused(record, false);
            try { record.inputLease?.Dispose(); }
            catch (Exception e) { log.LogException(e); }
            try { record.pauseLease?.Dispose(); }
            catch (Exception e) { log.LogException(e); }
            record.inputLease = null;
            record.pauseLease = null;
        }

        private void RefreshModalBlocking()
        {
            if (destroyed) return;
            List<PanelRecord> ordered = GetGlobalOrder();
            int topModal = -1;
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i].modal && ordered[i].state == PanelState.Open) topModal = i;

            for (int i = 0; i < ordered.Count; i++)
                SetPaused(ordered[i], topModal >= 0 && i < topModal);
        }

        private void SetPaused(PanelRecord record, bool paused)
        {
            if (record == null || record.paused == paused) return;
            record.paused = paused;

            if (record.group != null)
            {
                record.group.interactable = paused ? false : record.initialInteractable;
                record.group.blocksRaycasts = paused ? false : record.initialBlocksRaycasts;
            }

            if (record.panel == null) return;
            try
            {
                if (paused) record.panel.OnPause();
                else record.panel.OnResume();
            }
            catch (Exception e) { log.LogException(e); }
        }

        private void RemoveRecord(PanelRecord record)
        {
            if (record == null) return;
            panelRecords.Remove(record.path);
            panelDic.Remove(record.path);
            panelScopes.Remove(record.path);
            RemoveFromLayer(record);
            ReleasePanelState(record);
            record.state = PanelState.Closed;
            RefreshModalBlocking();
        }

        private void OnPanelDestroyed(string path, BasePanel panel)
        {
            if (!panelRecords.TryGetValue(path, out PanelRecord record)) return;
            if (!ReferenceEquals(record.panel, panel)) return;
            if (record.state == PanelState.Closing || record.state == PanelState.Closed) return;

            // 外部销毁也走一次 OnClose，保证业务的关闭逻辑不会因绕开 ClosePanel 而丢失。
            record.state = PanelState.Closing;
            panelRecords.Remove(path);
            panelDic.Remove(path);
            panelScopes.Remove(path);
            RemoveFromLayer(record);
            ReleasePanelState(record);
            try { panel.OnClose(); }
            catch (Exception e) { log.LogException(e); }
            try { panel.DisposeEventScopes(); }
            catch (Exception e) { log.LogException(e); }
            record.state = PanelState.Closed;
            instancesDestroyed++;
            RefreshModalBlocking();
            log.Warning("[UIManager] 面板被外部销毁，已从管理器移除：" + path);
        }

        /// <summary>
        /// 非泛型获取/添加面板组件。热更新程序集里的面板类型不能在 AOT 代码中调用
        /// <c>AddComponent&lt;T&gt;</c>，必须走 Type 重载。
        /// </summary>
        private static BasePanel GetOrAddPanelComponent(GameObject instance, Type panelType)
        {
            if (instance == null || panelType == null)
                throw new ArgumentNullException(panelType == null ? nameof(panelType) : nameof(instance));

            BasePanel existing = instance.GetComponent<BasePanel>();
            if (existing != null)
            {
                if (!panelType.IsInstanceOfType(existing))
                    throw new InvalidOperationException("UI 预制体已挂载其他 BasePanel 类型：" + existing.GetType().Name);
                return existing;
            }

            Component component = instance.GetComponent(panelType);
            if (component == null) component = instance.AddComponent(panelType);
            BasePanel panel = component as BasePanel;
            if (panel == null)
                throw new InvalidOperationException("面板类型不是 BasePanel：" + panelType.FullName);
            return panel;
        }

        private void InjectPanel(GameObject root)
        {
            if (root == null) return;
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                try
                {
                    if (gameServices != null && behaviour is IGameServicesConsumer servicesConsumer)
                        servicesConsumer.Configure(gameServices);
                    if (behaviour is IUIServiceConsumer uiConsumer)
                        uiConsumer.ConfigureUi(this);
                }
                catch (Exception e)
                {
                    log.LogException(e);
                }
            }
        }

        private bool IsPendingActive(string path, PendingPanel pending)
        {
            return !destroyed &&
                   !pending.closed &&
                   pendingPanels.TryGetValue(path, out PendingPanel active) &&
                   ReferenceEquals(active, pending);
        }

        private bool IsCurrent(PanelRecord record)
        {
            return record != null &&
                   !destroyed &&
                   panelRecords.TryGetValue(record.path, out PanelRecord current) &&
                   ReferenceEquals(current, record);
        }

        private static bool ScopeMatches(ILifetimeScope left, ILifetimeScope right)
        {
            return ReferenceEquals(left, right);
        }

        private UILoadInfoAttribute GetLoadInfo<T>() where T : BasePanel
        {
            return GetLoadInfo(typeof(T));
        }

        private UILoadInfoAttribute GetLoadInfo(Type panelType)
        {
            if (panelType == null) return null;
            if (loadInfoCache.TryGetValue(panelType, out UILoadInfoAttribute cached)) return cached;
            UILoadInfoAttribute info = panelType.GetCustomAttribute<UILoadInfoAttribute>();
            loadInfoCache[panelType] = info;
            if (info != null && !string.IsNullOrEmpty(info.loadPath))
                panelTypesByPath[info.loadPath] = panelType;
            return info;
        }

        /// <summary>
        /// 注册一个面板类型。热更新程序集加载完成后调用；重复注册同类型是幂等的。
        /// </summary>
        public bool RegisterPanel(Type panelType)
        {
            if (panelType == null || !typeof(BasePanel).IsAssignableFrom(panelType) ||
                panelType.IsAbstract || panelType.ContainsGenericParameters)
            {
                log.Error("[UIManager] 注册面板失败，类型必须是可实例化的 BasePanel：" + panelType);
                return false;
            }

            UILoadInfoAttribute info = GetLoadInfo(panelType);
            if (info == null || string.IsNullOrEmpty(info.loadPath))
            {
                log.Error("[UIManager] 注册面板失败，缺少有效 UILoadInfo：" + panelType.Name);
                return false;
            }
            if (panelTypesByPath.TryGetValue(info.loadPath, out Type existing) &&
                existing != null && existing != panelType)
            {
                log.Error("[UIManager] 面板路径已被其他类型占用：" + info.loadPath
                          + "，现有 " + existing.Name + "，新 " + panelType.Name);
                return false;
            }

            panelTypesByPath[info.loadPath] = panelType;
            return true;
        }

        /// <summary>
        /// 扫描热更新程序集内的 BasePanel 类型并注册。反射失败按类型逐个隔离。
        /// </summary>
        public int RegisterPanels(Assembly assembly)
        {
            if (assembly == null) return 0;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types;
            }
            catch (Exception e)
            {
                log.LogException(e);
                return 0;
            }

            int registered = 0;
            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || !typeof(BasePanel).IsAssignableFrom(type)) continue;
                if (RegisterPanel(type)) registered++;
            }
            return registered;
        }

        private Type ResolvePanelType(string loadPath)
        {
            if (string.IsNullOrEmpty(loadPath)) return null;
            if (panelTypesByPath.TryGetValue(loadPath, out Type type) && type != null) return type;

            // 未显式注册时，从已加载程序集按路径兜底扫描一次（热更程序集加载后即可用）。
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null || assembly.IsDynamic) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }

                for (int j = 0; j < types.Length; j++)
                {
                    Type candidate = types[j];
                    if (candidate == null || !typeof(BasePanel).IsAssignableFrom(candidate)) continue;
                    UILoadInfoAttribute info = GetLoadInfo(candidate);
                    if (info != null && info.loadPath == loadPath)
                    {
                        RegisterPanel(candidate);
                        return candidate;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 热更新卸载/重载前的收口：取消 pending、关闭所有面板、清空 Toast 队列与类型缓存。
        /// 调用方应在卸载旧程序集之前调用；之后用 RegisterPanels 注册新程序集。
        /// </summary>
        public void ResetForHotUpdate()
        {
            if (destroyed) return;

            while (toastQueue.Count > 0)
            {
                ToastRequest toast = toastQueue.Dequeue();
                try { toast.complete?.Invoke(null); }
                catch (Exception e) { log.LogException(e); }
            }

            List<PendingPanel> requests = new List<PendingPanel>(pendingPanels.Values);
            for (int i = 0; i < requests.Count; i++)
                CancelPending(requests[i].path, requests[i]);

            CloseAll();
            ClearPanelPool();
            loadInfoCache.Clear();
            panelTypesByPath.Clear();
        }

        public void Shutdown()
        {
            if (destroyed) return;
            destroyed = true;
            DetachSettingsEvents();
            DetachOverlay();
            UnhookSceneLoaded();
            UnhookPipelineChanges();

            while (toastQueue.Count > 0)
            {
                ToastRequest toast = toastQueue.Dequeue();
                try { toast.complete?.Invoke(null); }
                catch (Exception e) { log.LogException(e); }
            }

            List<PendingPanel> requests = new List<PendingPanel>(pendingPanels.Values);
            pendingPanels.Clear();
            for (int i = 0; i < requests.Count; i++)
            {
                PendingPanel request = requests[i];
                if (request == null) continue;
                request.closed = true;
                try { request.requestCts?.Cancel(); }
                catch (Exception e) { log.LogException(e); }
                request.completion.TrySetResult(null);
                try { request.cancellation.Dispose(); }
                catch (Exception) { /* 取消登记已失效时无需处理。 */ }
            }

            List<PanelRecord> records = new List<PanelRecord>(panelRecords.Values);
            panelRecords.Clear();
            panelDic.Clear();
            panelScopes.Clear();
            for (int layerIndex = 0; layerIndex < layerStacks.Length; layerIndex++)
                layerStacks[layerIndex].Clear();

            for (int i = 0; i < records.Count; i++)
            {
                PanelRecord record = records[i];
                if (record == null) continue;
                record.state = PanelState.Closing;
                ReleasePanelState(record);
                BasePanel panel = record.panel;
                if (panel == null)
                {
                    record.state = PanelState.Closed;
                    continue;
                }
                try { panel.OnClose(); }
                catch (Exception e) { log.LogException(e); }
                try { panel.DisposeEventScopes(); }
                catch (Exception e) { log.LogException(e); }
                Destroy(panel.gameObject);
                instancesDestroyed++;
                record.state = PanelState.Closed;
            }

            ClearPanelPool();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        /// <summary>
        /// 得到某一个已经显示的面板 方便外部使用
        /// </summary>
        public T GetPanel<T>() where T : BasePanel
        {
            return GetPanel(typeof(T)) as T;
        }

        /// <summary>按 Type 查询已打开面板；热更新代码使用此入口。</summary>
        public BasePanel GetPanel(Type panelType)
        {
            UILoadInfoAttribute info = GetLoadInfo(panelType);
            if (info == null)
            {
                log.Error($"继承自BasePanel的类:({(panelType != null ? panelType.Name : "null")})必须标记UILoadInfo特性！！！");
                return null;
            }

            if (panelRecords.TryGetValue(info.loadPath, out PanelRecord record) &&
                record.state == PanelState.Open)
                return record.panel;
            return null;
        }

        /// <summary>
        /// 给控件添加自定义事件监听
        /// </summary>
        public static void AddCustomEventListener(UIBehaviour control, EventTriggerType type, UnityAction<BaseEventData> callBack)
        {
            EventTrigger trigger = control.gameObject.GetOrAddComponent<EventTrigger>();
            EventTrigger.Entry entry = new EventTrigger.Entry();
            entry.eventID = type;
            entry.callback.AddListener(callBack);
            trigger.triggers.Add(entry);
        }
    }
}
