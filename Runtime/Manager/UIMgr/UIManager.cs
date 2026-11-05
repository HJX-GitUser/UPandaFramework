using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// UI层级
    /// </summary>
    public enum E_UI_Layer
    {
        Bot,
        Mid,
        Top,
        System
    }

    /// <summary>
    /// UI管理器
    /// 1.管理所有显示的面板
    /// 2.提供给外部 显示和隐藏等等接口
    /// </summary>
    public class UIManager : LazyMonoSingletonBase<UIManager>
    {
        public Dictionary<string, BasePanel> panelDic = new Dictionary<string, BasePanel>();

        private sealed class PendingPanel
        {
            public readonly TaskCompletionSource<BasePanel> completion = new TaskCompletionSource<BasePanel>();
        }
        private readonly Dictionary<string, PendingPanel> pendingPanels = new Dictionary<string, PendingPanel>();
        private bool destroyed;

        private Transform bot;
        private Transform mid;
        private Transform top;
        private Transform system;

        public Camera uiCamera;
        public Canvas uiCanvas;
        private EventSystem uiEventSystem;
        private RectTransform canvasRectTransform;

        private ResourcesLoader resourcesLoader;
        private IAssetsLoader assetsLoader;

        /// <summary>
        /// 重置Canvas
        /// </summary>
        /// <param name="arg"></param>
        public void ResetCanvas(Canvas arg)
        {
            if (uiCanvas != null) uiCanvas.gameObject.SetActive(false);
            uiCanvas = arg;
            canvasRectTransform = uiCanvas.transform as RectTransform;
            //找到各层
            bot = canvasRectTransform.Find("Bot");
            mid = canvasRectTransform.Find("Mid");
            top = canvasRectTransform.Find("Top");
            system = canvasRectTransform.Find("System");
        }

        /// <summary>
        /// 重置相机
        /// </summary>
        /// <param name="arg"></param>
        public void ResetCamera(Camera arg)
        {
            if (uiCamera != null) uiCamera.gameObject.SetActive(false);
            uiCamera = arg;
        }

        public void Init()
        {
            if (uiCamera == null)
            {
                ResetCamera(InitCamera());
            }
            if (uiCanvas == null)
            {
                uiCanvas = InitUICanvas(uiCamera);
            }
            canvasRectTransform = uiCanvas.transform as RectTransform;
            //找到各层
            bot = canvasRectTransform.Find("Bot");
            mid = canvasRectTransform.Find("Mid");
            top = canvasRectTransform.Find("Top");
            system = canvasRectTransform.Find("System");
        }

        public void SetResourcesLoader(IAssetsLoader arg1, ResourcesLoader arg2)
        {
            assetsLoader = arg1;
            resourcesLoader = arg2;
        }

        private Camera InitCamera()
        {
            GameObject obj = new GameObject("UICamera");
            obj.transform.SetParent(transform);
            Camera camera = obj.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Depth;
            camera.cullingMask = 1 << LayerMask.NameToLayer("UI");
            camera.depth = 10;
            return camera;
        }

        private Canvas InitUICanvas(Camera camera)
        {
            GameObject obj = new GameObject("Canvas", typeof(RectTransform));
            RectTransform canvasRect = obj.GetComponent<RectTransform>();
            canvasRect.SetParent(transform);
            canvasRect.localScale = Vector3.one;
            obj.layer = LayerMask.NameToLayer("UI");
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
            RectTransform Bot = new GameObject("Bot", typeof(RectTransform)).GetComponent<RectTransform>();
            RectTransform Mid = new GameObject("Mid", typeof(RectTransform)).GetComponent<RectTransform>();
            RectTransform Top = new GameObject("Top", typeof(RectTransform)).GetComponent<RectTransform>();
            RectTransform System = new GameObject("System", typeof(RectTransform)).GetComponent<RectTransform>();
            SetRect(Bot, canvasRect);
            SetRect(Mid, canvasRect);
            SetRect(Top, canvasRect);
            SetRect(System, canvasRect);
            //EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
            EventSystem eventSystem = FindObjectOfType<EventSystem>();
            if (eventSystem == null)
            {
                GameObject eventObj = new GameObject("EventSystem");
                eventObj.transform.SetParent(transform.parent);
                eventSystem = eventObj.AddComponent<EventSystem>();
                eventObj.AddComponent<StandaloneInputModule>();
            }
            return canvas;
        }

        private void SetRect(RectTransform rect, RectTransform parent)
        {
            rect.SetParent(parent);
            rect.localPosition = Vector3.zero;
            rect.localScale = Vector3.one;
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// 通过层级枚举 得到对应层级的父对象
        /// </summary>
        /// <param name="layer"></param>
        /// <returns></returns>
        public Transform GetLayerFather(E_UI_Layer layer)
        {
            switch (layer)
            {
                case E_UI_Layer.Bot:
                    return this.bot;
                case E_UI_Layer.Mid:
                    return this.mid;
                case E_UI_Layer.Top:
                    return this.top;
                case E_UI_Layer.System:
                    return this.system;
            }
            return null;
        }


        /// <summary>回调与 Task API 共用加载流程，同一路径并发请求只创建一个面板。</summary>
        public async void ShowPanel<T>(object panelArg = null, UnityAction<T> callBack = null) where T : BasePanel
        {
            T panel = null;
            try { panel = await ShowPanelAsync<T>(panelArg); }
            catch (Exception e) { Debug.LogException(e); }
            ResourcesLoader.InvokeCallbacks(callBack, panel);
        }

        public async Task<T> ShowPanelAsync<T>(object panelArg = null) where T : BasePanel
        {
            if (destroyed) return null;
            var info = typeof(T).GetCustomAttribute<UILoadInfoAttribute>();
            if (info == null || string.IsNullOrEmpty(info.loadPath))
            {
                Debug.LogError(typeof(T).Name + " 必须标记有效的 UILoadInfo 特性");
                return null;
            }
            try
            {
                BasePanel panel;
                if (!panelDic.TryGetValue(info.loadPath, out panel) || panel == null)
                {
                    panelDic.Remove(info.loadPath);
                    PendingPanel pending;
                    if (!pendingPanels.TryGetValue(info.loadPath, out pending))
                    {
                        pending = new PendingPanel();
                        pendingPanels.Add(info.loadPath, pending);
                        _ = CreatePanel<T>(info, pending);
                    }
                    panel = await pending.completion.Task;
                }
                if (destroyed || panel == null) return null;
                T typed = panel as T;
                if (typed == null) throw new InvalidOperationException("同一路径不能绑定不同面板类型：" + info.loadPath);
                typed.OnOpen(panelArg);
                return typed != null ? typed : null;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        private async Task CreatePanel<T>(UILoadInfoAttribute info, PendingPanel pending) where T : BasePanel
        {
            GameObject instance = null;
            ResourcesLoader loader = null;
            bool acquired = false;
            try
            {
                GameObject prefab = null;
                switch (info.loadMethod)
                {
                    case AssetLoadMethod.Resources:
                        loader = resourcesLoader != null ? resourcesLoader : ResourcesLoader.Instance;
                        if (loader == null) throw new InvalidOperationException("ResourcesLoader 不可用");
                        var loaded = new TaskCompletionSource<GameObject>();
                        loader.LoadAsync<GameObject>(info.loadPath, asset => loaded.TrySetResult(asset));
                        prefab = await loaded.Task;
                        acquired = prefab != null;
                        break;
                    case AssetLoadMethod.AssetBundle:
                        if (assetsLoader == null) throw new InvalidOperationException("AssetsLoader 尚未初始化");
                        prefab = await assetsLoader.LoadAsync<GameObject>(info.loadPath);
                        break;
                }
                PendingPanel current;
                if (destroyed || !pendingPanels.TryGetValue(info.loadPath, out current)
                    || !ReferenceEquals(current, pending)) return;
                if (prefab == null) throw new InvalidOperationException("UI 资源加载失败：" + info.loadPath);
                Transform parent = GetLayerFather(info.ui_Layer);
                if (parent == null) { Init(); parent = GetLayerFather(info.ui_Layer); }
                if (parent == null) throw new InvalidOperationException("UI 层级不存在：" + info.ui_Layer);
                instance = Instantiate(prefab, parent, false);
                var rect = instance.transform as RectTransform;
                if (rect == null) throw new InvalidOperationException("UI 预制体根节点需要 RectTransform");
                rect.localPosition = Vector3.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                T panel = instance.GetOrAddComponent<T>();
                if (acquired)
                {
                    var lease = instance.AddComponent<PanelResourceLease>();
                    lease.Initialize(loader, info.loadPath);
                    acquired = false; // 从此由实例销毁时归还引用，支持外部直接 Destroy。
                }
                panelDic[info.loadPath] = panel;
                instance = null;
                pendingPanels.Remove(info.loadPath);
                pending.completion.TrySetResult(panel);
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                if (instance != null) Destroy(instance);
                if (acquired && loader != null) loader.UnLoadAsset<GameObject>(info.loadPath, true);
                PendingPanel current;
                if (pendingPanels.TryGetValue(info.loadPath, out current) && ReferenceEquals(current, pending))
                    pendingPanels.Remove(info.loadPath);
                pending.completion.TrySetResult(null);
            }
        }

        /// <summary>关闭已显示面板，并使尚未完成的打开请求返回 null。</summary>
        public void ClosePanel<T>() where T : BasePanel
        {
            var info = typeof(T).GetCustomAttribute<UILoadInfoAttribute>();
            if (info == null) throw new ArgumentException(typeof(T).Name + " 必须标记 UILoadInfo 特性");
            PendingPanel pending;
            if (pendingPanels.TryGetValue(info.loadPath, out pending))
            {
                pendingPanels.Remove(info.loadPath);
                pending.completion.TrySetResult(null);
            }
            BasePanel panel;
            if (!panelDic.TryGetValue(info.loadPath, out panel)) return;
            panelDic.Remove(info.loadPath); // 用户 OnClose 可重入打开。
            if (panel == null) return;
            try { panel.OnClose(); }
            finally { if (panel != null) Destroy(panel.gameObject); }
        }

        protected override void OnDestroyed()
        {
            destroyed = true;
            var requests = new List<PendingPanel>(pendingPanels.Values);
            pendingPanels.Clear();
            foreach (var request in requests) request.completion.TrySetResult(null);
            foreach (var panel in panelDic.Values)
                if (panel != null) Destroy(panel.gameObject);
            panelDic.Clear();
            base.OnDestroyed();
        }

        /// <summary>
        /// 得到某一个已经显示的面板 方便外部使用
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="panelName"></param>
        /// <returns></returns>
        public T GetPanel<T>() where T : BasePanel
        {
            UILoadInfoAttribute uiLoadInfo = typeof(T).GetCustomAttribute<UILoadInfoAttribute>();
            // 检查当前类是否包含特性
            if (uiLoadInfo == null)
            {
                Debug.LogError($"继承自BasePanel的类:({typeof(T).Name})必须标记UILoadInfo特性！！！");
                return null;
            }

            if (panelDic.ContainsKey(uiLoadInfo.loadPath))
            {
                return panelDic[uiLoadInfo.loadPath] as T;
            }
            return null;
        }

        /// <summary>
        /// 给控件添加自定义事件监听
        /// </summary>
        /// <param name="control">控件对象</param>
        /// <param name="type">事件类型</param>
        /// <param name="callBack">事件的响应函数</param>
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
