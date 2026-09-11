using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.EditorTools
{
    /// <summary>
    /// 事件调试窗口（菜单：UPandaGF/事件系统/事件调试窗口）。
    ///
    /// 用来回答平时最难查的两个问题：
    ///   1) 「事件发了没人收」——哪个事件类型根本没有监听者？派发次数到底有没有涨？
    ///   2) 「谁在监听」——监听者是谁（方法名 / 目标类型 / 优先级 / 是否带 owner），有没有僵尸监听（owner 已销毁）。
    ///
    /// 数据来源：EventCenter.GetDebugSnapshot() 与 EventBus.GetAliveInstances()，不做反射、不读私有字段。
    /// </summary>
    public class EventCenterDebugWindow : EditorWindow
    {
        private const string MenuPath = "UPandaGF/Runtime/事件系统/事件调试窗口";

        private Vector2 scroll;
        private bool autoRefresh = true;
        private double lastRefreshTime;
        private readonly HashSet<string> expanded = new HashSet<string>();

        private List<EventTypeDebugInfo> centerSnapshot = new List<EventTypeDebugInfo>();
        private List<EventBus> buses = new List<EventBus>();

        [MenuItem(MenuPath, false, 30)]
        public static void Open()
        {
            EventCenterDebugWindow win = GetWindow<EventCenterDebugWindow>("事件调试");
            win.minSize = new Vector2(560f, 320f);
            win.Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void Update()
        {
            if (!autoRefresh) return;
            double now = EditorApplication.timeSinceStartup;
            if (now - lastRefreshTime < 0.5d) return;
            Refresh();
        }

        private void Refresh()
        {
            centerSnapshot = EventCenter.Instance.GetDebugSnapshot();
            buses = EventBus.GetAliveInstances();
            lastRefreshTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawEventCenterSection();
            EditorGUILayout.Space();
            DrawEventBusSection();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60f))) Refresh();
                autoRefresh = GUILayout.Toggle(autoRefresh, "自动刷新", EditorStyles.toolbarButton, GUILayout.Width(80f));
                GUILayout.Space(8f);

                bool warn = EventCenter.Instance.warnOnMissingListener;
                bool newWarn = GUILayout.Toggle(warn, "无监听者时告警", EditorStyles.toolbarButton, GUILayout.Width(110f));
                if (newWarn != warn) EventCenter.Instance.warnOnMissingListener = newWarn;

                if (GUILayout.Button("派发次数清零", EditorStyles.toolbarButton, GUILayout.Width(100f)))
                {
                    EventCenter.Instance.ResetDispatchCounts();
                    Refresh();
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空 EventCenter", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                {
                    if (EditorUtility.DisplayDialog("事件调试", "清空 EventCenter 会移除所有监听者，运行中执行会导致事件失效。确定继续？", "清空", "取消"))
                    {
                        EventCenter.Instance.Clear();
                        Refresh();
                    }
                }
            }
        }

        private void DrawEventCenterSection()
        {
            int listenerTotal = 0;
            for (int i = 0; i < centerSnapshot.Count; i++) listenerTotal += centerSnapshot[i].listenerCount;

            EditorGUILayout.LabelField($"EventCenter：{centerSnapshot.Count} 个事件类型，{listenerTotal} 个监听者", EditorStyles.boldLabel);
            if (centerSnapshot.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有任何事件监听者。若你确定已经 AddEventListener，请确认是否注册到了另一个 EventCenter 实例。", MessageType.Info);
                return;
            }

            for (int i = 0; i < centerSnapshot.Count; i++)
            {
                EventTypeDebugInfo info = centerSnapshot[i];
                string key = "EC:" + info.eventType;
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool open = expanded.Contains(key);
                    bool newOpen = EditorGUILayout.Foldout(open, $"{info.eventType}   监听 {info.listenerCount}   派发 {info.dispatchCount}", true);
                    if (newOpen != open)
                    {
                        if (newOpen) expanded.Add(key); else expanded.Remove(key);
                    }
                }

                if (!expanded.Contains(key)) continue;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    for (int j = 0; j < info.listeners.Count; j++)
                    {
                        EventListenerInfo l = info.listeners[j];
                        EditorGUILayout.LabelField(
                            $"  · {l.targetType}.{l.methodName}   优先级 {l.priority}{(l.hasOwner ? "   带 owner" : "")}",
                            EditorStyles.miniLabel);
                    }
                    if (info.listenerCount == 0) EditorGUILayout.LabelField("  （没有监听者）", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawEventBusSection()
        {
            EditorGUILayout.LabelField($"EventBus：{buses.Count} 个存活实例", EditorStyles.boldLabel);
            if (buses.Count == 0)
            {
                EditorGUILayout.HelpBox("当前没有存活的事件总线实例（EventBus 需要业务侧自己 new 并持有）。", MessageType.Info);
                return;
            }

            for (int i = 0; i < buses.Count; i++)
            {
                EventBus bus = buses[i];
                if (bus == null) continue;
                List<EventBusSubscriptionInfo> subs = bus.GetDebugSnapshot();

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"实例 #{i + 1}   消息类型 {subs.Count}", EditorStyles.miniBoldLabel);
                    if (subs.Count == 0)
                    {
                        EditorGUILayout.LabelField("  没有订阅", EditorStyles.miniLabel);
                        continue;
                    }
                    for (int j = 0; j < subs.Count; j++)
                    {
                        EventBusSubscriptionInfo info = subs[j];
                        EditorGUILayout.LabelField($"  {info.messageType}   订阅 {info.count}", EditorStyles.miniLabel);
                        for (int k = 0; k < info.subscribers.Count; k++)
                            EditorGUILayout.LabelField($"      · {info.subscribers[k]}", EditorStyles.miniLabel);
                    }
                }
            }
        }
    }
}
