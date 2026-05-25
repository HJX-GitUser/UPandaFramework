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


        /// <summary>
        /// 显示面板的方法
        /// </summary>
        /// <param name="callBack">UI面板显示后的回调</param>
        public void ShowPanel<T>(object panelArg = null, UnityAction<T> callBack = null) where T : BasePanel
        {
            UILoadInfoAttribute uiLoadInfo = typeof(T).GetCustomAttribute<UILoadInfoAttribute>();
            // 检查当前类是否包含特性
            if (uiLoadInfo == null)
            {
                Debug.LogError($"继承自BasePanel的类:({GetType().Name})必须标记UILoadInfo特性！！！");
                callBack?.Invoke(null);
                return;
            }
            if (panelDic.ContainsKey(uiLoadInfo.loadPath))
            {
                panelDic[uiLoadInfo.loadPath].OnOpen(panelArg);
                // 处理面板创建完成后的逻辑
                callBack?.Invoke(panelDic[uiLoadInfo.loadPath] as T);
                //避免面板重复加载 如果存在该面板 即直接显示 调用回调函数后  直接return 不再处理后面的异步加载逻辑
                return;
            }
            else
            {
                loadUIObj(uiLoadInfo.loadPath, (perfab) =>
                {

                    if (perfab != null)
                    {
                        GameObject uiObj = Instantiate(perfab);
                        //把UI面板作为 Canvas的子对象
                        //设置它的相对位置
                        //找到父对象 底显示在对应的层
                        Transform father = this.bot;
                        switch (uiLoadInfo.ui_Layer)
                        {
                            case E_UI_Layer.Mid:
                                father = this.mid;
                                break;
                            case E_UI_Layer.Top:
                                father = this.top;
                                break;
                            case E_UI_Layer.System:
                                father = this.system;
                                break;
                        }
                        //设置父对象  设置相对位置和大小
                        uiObj.transform.SetParent(father);
                        uiObj.transform.localPosition = Vector3.zero;
                        uiObj.transform.localRotation = Quaternion.identity;
                        uiObj.transform.localScale = Vector3.one;
                        (uiObj.transform as RectTransform).offsetMax = Vector2.zero;
                        (uiObj.transform as RectTransform).offsetMin = Vector2.zero;
                        //得到预设体身上的面板脚本
                        T panel = uiObj.GetComponent<T>();
                        panel.OnOpen(panelArg);
                        panelDic.Add(uiLoadInfo.loadPath, panel);
                        callBack?.Invoke(panel);
                    }
                }, uiLoadInfo.loadMethod);
            }
        }

        public async Task<T> ShowPanelAsync<T>(object panelArg = null) where T : BasePanel
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
                panelDic[uiLoadInfo.loadPath].OnOpen(panelArg);
                //避免面板重复加载 如果存在该面板 即直接显示 调用回调函数后  直接return 不再处理后面的异步加载逻辑
                return panelDic[uiLoadInfo.loadPath] as T;
            }
            else
            {
                GameObject uiObj = await loadUIObj(uiLoadInfo.loadPath, uiLoadInfo.loadMethod);
                if (uiObj != null)
                {
                    Transform father = this.bot;
                    switch (uiLoadInfo.ui_Layer)
                    {
                        case E_UI_Layer.Mid:
                            father = this.mid;
                            break;
                        case E_UI_Layer.Top:
                            father = this.top;
                            break;
                        case E_UI_Layer.System:
                            father = this.system;
                            break;
                    }
                    //设置父对象  设置相对位置和大小
                    uiObj.transform.SetParent(father);
                    uiObj.transform.localPosition = Vector3.zero;
                    uiObj.transform.localRotation = Quaternion.identity;
                    uiObj.transform.localScale = Vector3.one;
                    (uiObj.transform as RectTransform).offsetMax = Vector2.zero;
                    (uiObj.transform as RectTransform).offsetMin = Vector2.zero;
                    //得到预设体身上的面板脚本
                    T panel = uiObj.GetOrAddComponent<T>();
                    panel.OnOpen(panelArg);
                    panelDic.Add(uiLoadInfo.loadPath, panel);
                    return panel;
                }
                return null;
            }
        }

        private async Task<GameObject> loadUIObj(string loadPath, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            GameObject uiObj = null;
            switch (loadMethod)
            {
                case AssetLoadMethod.Resources:
                    bool isLoaded = false;
                    ResourcesLoader.Instance.LoadAsync<GameObject>(loadPath, (res) =>
                    {
                        uiObj = res;
                        isLoaded = true;
                    });
                    while (!isLoaded)
                    {
                        await Task.Yield();
                    }
                    break;
                case AssetLoadMethod.AssetBundle:
                    if (assetsLoader == null) PLogger.LogError("assetsLoader is NULL");
                    else uiObj = await assetsLoader.LoadAsync<GameObject>(loadPath);
                    break;
            }
            if (uiObj == null)
            {
                PLogger.LogError($"加载失败：{loadPath} \n【{loadMethod}】");
                return null;
            }
            return Instantiate(uiObj);
        }

        private void loadUIObj(string loadPath, UnityAction<GameObject> callback, AssetLoadMethod loadMethod)
        {
            switch (loadMethod)
            {
                case AssetLoadMethod.Resources:
                    ResourcesLoader.Instance.LoadAsync(loadPath, callback);
                    break;
                case AssetLoadMethod.AssetBundle:
                    if (assetsLoader == null) PLogger.LogError("assetsLoader is NULL");
                    else assetsLoader.LoadAsync(loadPath, callback);
                    break;
            }
        }


        /// <summary>
        /// 隐藏面板
        /// 会直接把UI对象给杀掉
        /// 如果不想的话可以用GetPanel得到面板后直接调用该面板的OnClose方法
        /// </summary>
        /// <param name="panelName"></param>
        public void ClosePanel<T>() where T : BasePanel
        {
            UILoadInfoAttribute uiLoadInfo = typeof(T).GetCustomAttribute<UILoadInfoAttribute>();
            // 检查当前类是否包含特性
            if (uiLoadInfo == null)
            {
                throw new ArgumentException($"继承自BasePanel的类:({typeof(T).Name})必须标记UILoadInfo特性！！！");
            }
            if (panelDic.ContainsKey(uiLoadInfo.loadPath))
            {
                panelDic[uiLoadInfo.loadPath].OnClose();
                GameObject.Destroy(panelDic[uiLoadInfo.loadPath].gameObject);
                panelDic.Remove(uiLoadInfo.loadPath);
            }
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