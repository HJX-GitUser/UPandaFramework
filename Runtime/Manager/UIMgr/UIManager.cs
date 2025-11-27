using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

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
public class UIManager : LazySingletonBase<UIManager>
{
    private const string UI_RESOURCES_PATH = "UI/";

    public Dictionary<string, BasePanel> panelDic = new Dictionary<string, BasePanel>();

    private Transform bot;
    private Transform mid;
    private Transform top;
    private Transform system;

    private Camera uiCamera;
    private Canvas uiCanvas;
    private EventSystem uiEventSystem;
    public RectTransform canvasRectTransform;

    public UIManager()
    {
        uiCamera = GameObject.Instantiate(ResMgr.Instance.Load<GameObject>(UI_RESOURCES_PATH + "UICamera")).GetComponent<Camera>();
        GameObject.DontDestroyOnLoad(uiCamera.gameObject);
        //创建Canvas 让其过场景的时候 不被移除
        GameObject obj = GameObject.Instantiate(ResMgr.Instance.Load<GameObject>(UI_RESOURCES_PATH + "Canvas"));
        uiCanvas = obj.GetComponent<Canvas>();
        uiCanvas.worldCamera = uiCamera;
        canvasRectTransform = obj.transform as RectTransform;
        GameObject.DontDestroyOnLoad(obj);



        //找到各层
        bot = canvasRectTransform.Find("Bot");
        mid = canvasRectTransform.Find("Mid");
        top = canvasRectTransform.Find("Top");
        system = canvasRectTransform.Find("System");

        //创建EventSystem 让其过场景的时候 不被移除
        uiEventSystem = GameObject.Instantiate(ResMgr.Instance.Load<GameObject>("UI/EventSystem")).GetComponent<EventSystem>();
        GameObject.DontDestroyOnLoad(uiEventSystem.gameObject);
    }

    public void Init() { }

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
    /// 如果字典里有就直接调用该面板的ShowMe方法，
    /// 如果没有就从资源文件夹里加载出来再调用，并且存入字典里
    /// </summary>
    /// <typeparam name="T">面板类</typeparam>
    /// <param name="panelName">面板名字</param>
    /// <param name="layer">显示在那个层</param>
    /// <param name="callBack">UI面板显示后的回调</param>
    public void ShowPanel<T>(string panelName, E_UI_Layer layer = E_UI_Layer.Mid, UnityAction<T> callBack = null) where T : BasePanel
    {
        if (panelDic.ContainsKey(panelName))
        {
            panelDic[panelName].ShowMe();
            // 处理面板创建完成后的逻辑
            if (callBack != null)
            {
                callBack(panelDic[panelName] as T);
            }
            //避免面板重复加载 如果存在该面板 即直接显示 调用回调函数后  直接return 不再处理后面的异步加载逻辑
            return;
        }

        ResMgr.Instance.LoadAsync<GameObject>(UI_RESOURCES_PATH + panelName, (res) =>
        {
            //把UI面板作为 Canvas的子对象
            //并且 要设置它的相对位置
            //找到父对象 底显示在对应的层
            Transform father = this.bot;
            switch (layer)
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
            res.transform.SetParent(father);
            res.transform.localPosition = Vector3.zero;
            res.transform.localScale = Vector3.one;
            (res.transform as RectTransform).offsetMax = Vector2.zero;
            (res.transform as RectTransform).offsetMin = Vector2.zero;
            //得到预设体身上的面板脚本
            T panel = res.GetComponent<T>();
            callBack?.Invoke(panel);

            panel.ShowMe();
            panelDic.Add(panelName, panel);
        });

    }
    /// <summary>
    /// 隐藏面板
    /// 会直接把UI对象给杀掉
    /// 如果不想的话可以用GetPanel得到面板后直接调用该面板的HideMe方法
    /// </summary>
    /// <param name="panelName"></param>
    public void HidePanel(string panelName)
    {
        if (panelDic.ContainsKey(panelName))
        {
            panelDic[panelName].HideMe();
            GameObject.Destroy(panelDic[panelName].gameObject);
            panelDic.Remove(panelName);
        }
    }
    /// <summary>
    /// 得到某一个已经显示的面板 方便外部使用
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="panelName"></param>
    /// <returns></returns>
    public T GetPanel<T>(string panelName) where T : BasePanel
    {
        if (panelDic.ContainsKey(panelName))
        {
            return panelDic[panelName] as T;
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
        EventTrigger trigger = control.GetComponent<EventTrigger>();
        if (trigger != null)
        {
            trigger = control.gameObject.AddComponent<EventTrigger>();
        }
        EventTrigger.Entry entry = new EventTrigger.Entry();
        entry.eventID = type;
        entry.callback.AddListener(callBack);

        trigger.triggers.Add(entry);
    }
}
