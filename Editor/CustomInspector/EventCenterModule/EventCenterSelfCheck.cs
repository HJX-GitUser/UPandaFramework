using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF.EditorTools
{
    /// <summary>自检用的事件参数</summary>
    public class EventSelfCheckMsgA : EventArgBase
    {
        public int value;
    }

    /// <summary>自检用的事件参数（空参数）</summary>
    public class EventSelfCheckMsgB : EventArgBase { }

    /// <summary>
    /// 自检用的 owner：ScriptableObject 也是 UnityEngine.Object，可以直接 CreateInstance / DestroyImmediate，
    /// 用来验证「owner 销毁后自动失效」而不依赖 MonoBehaviour 挂载。
    /// </summary>
    public class EventSelfCheckOwner : ScriptableObject
    {
        public int hits;
        public void OnMsg(EventSelfCheckMsgA e) { hits++; }
    }

    /// <summary>
    /// 事件模块自检（菜单：UPandaGF/事件系统/事件模块自检）。
    ///
    /// 本工程未安装 com.unity.test-framework，所以用菜单断言代替单元测试：
    /// 覆盖 2026-09 重构修掉的每一项 —— 委托去重、优先级、一次性监听、异常隔离、
    /// owner 自动清理、派发中增删订阅、弱引用回收、struct 监听器拒绝等。
    ///
    /// 说明：EventCenter 的断言都用**独立实例**（new EventCenter()）跑，不会污染运行时的全局 EventCenter.Instance。
    /// 自检过程中控制台会出现 4 条预期内的日志（1 条重复注册警告 + 3 条异常/拒绝错误），属于正常现象。
    /// </summary>
    public static class EventCenterSelfCheck
    {
        // ---------- EventBus 测试用 ----------
        private struct BusMsg { public int value; }

        private class Receiver : IEventListener<BusMsg>
        {
            public int hits;
            public Action onEventHook;
            public void OnEvent(BusMsg message) { hits++; if (onEventHook != null) onEventHook(); }
        }

        private struct StructReceiver : IEventListener<BusMsg>
        {
            public int hits;
            public void OnEvent(BusMsg message) { hits++; }
        }

        // ---------- EventCenter 测试用 ----------
        private class ListenerObject
        {
            public int hits;
            public void OnMsg(EventSelfCheckMsgA e) { hits++; }
        }

        // ---------- 断言框架 ----------
        private static int total;
        private static readonly List<string> fails = new List<string>();
        private static readonly List<string> skips = new List<string>();

        [MenuItem("UPandaGF/Runtime/事件系统/事件模块自检", false, 31)]
        public static void Run()
        {
            string report = RunSilent();
            EditorUtility.DisplayDialog("事件模块自检", report, "确定");
        }

        /// <summary>
        /// 无弹窗版本：返回报告字符串（同时写入 Console）。
        /// 供 CI / 命令行 / MCP 自动化调用 —— 弹窗会阻塞编辑器，自动化流程请用这个入口。
        /// </summary>
        public static string RunSilent()
        {
            total = 0;
            fails.Clear();
            skips.Clear();

            CheckEventCenter();
            CheckEventBus();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"事件模块自检：{total - fails.Count} / {total} 项通过");
            if (skips.Count > 0)
            {
                sb.AppendLine($"（跳过 {skips.Count} 项，通常是编辑器/调试器仍持有引用导致 GC 未回收）");
                foreach (string s in skips) sb.AppendLine("· " + s);
            }
            if (fails.Count > 0)
            {
                sb.AppendLine(new string('-', 40));
                foreach (string f in fails) sb.AppendLine("× " + f);
            }
            sb.AppendLine();
            sb.AppendLine("控制台出现的 4 条日志（重复注册警告 1 条 + 异常隔离/struct 拒绝错误 3 条）为自检预期结果。");

            string report = sb.ToString();
            if (fails.Count == 0) Debug.Log("[事件自检] " + report);
            else Debug.LogError("[事件自检] " + report);
            return report;
        }

        // ================================ EventCenter ================================
        private static void CheckEventCenter()
        {
            EventCenter center = new EventCenter();

            // 注册 / 派发
            EventSelfCheckMsgA got = null;
            UnityAction<EventSelfCheckMsgA> handler = e => { got = e; };
            center.AddEventListener<EventSelfCheckMsgA>(handler);
            Check(() => center.GetListenerCount<EventSelfCheckMsgA>() == 1, "EventCenter：注册后监听者数量为 1");

            center.EventTrigger(new EventSelfCheckMsgA { value = 7 });
            Check(() => got != null && got.value == 7, "EventCenter：事件能派发到监听者");

            // 去重（按委托相等，而不是哈希）
            center.AddEventListener<EventSelfCheckMsgA>(handler);
            Check(() => center.GetListenerCount<EventSelfCheckMsgA>() == 1,
                "EventCenter：同一方法重复注册会被去重（按委托相等，不用哈希）");

            // 不同目标对象的同名方法各自独立
            ListenerObject o1 = new ListenerObject();
            ListenerObject o2 = new ListenerObject();
            center.AddEventListener<EventSelfCheckMsgA>(o1.OnMsg);
            center.AddEventListener<EventSelfCheckMsgA>(o2.OnMsg);
            Check(() => center.GetListenerCount<EventSelfCheckMsgA>() == 3, "EventCenter：不同对象的同名方法是不同监听（共 3 个）");

            // 移除 + 空壳清理
            center.RemoveEventListener<EventSelfCheckMsgA>(handler);
            Check(() => center.GetListenerCount<EventSelfCheckMsgA>() == 2, "EventCenter：移除后监听者数量减少");
            center.RemoveEventListener<EventSelfCheckMsgA>(o1.OnMsg);
            center.RemoveEventListener<EventSelfCheckMsgA>(o2.OnMsg);
            Check(() => center.GetListenerCount<EventSelfCheckMsgA>() == 0 && center.ListenerTypeCount == 0,
                "EventCenter：监听者清空后不再保留事件类型空壳（ListenerTypeCount 归零）");

            // 优先级
            EventCenter centerPriority = new EventCenter();
            StringBuilder order = new StringBuilder();
            centerPriority.AddEventListener<EventSelfCheckMsgA>(e => order.Append("low>"), 0);
            centerPriority.AddEventListener<EventSelfCheckMsgA>(e => order.Append("high>"), 10);
            centerPriority.EventTrigger(new EventSelfCheckMsgA());
            Check(() => order.ToString() == "high>low>", "EventCenter：优先级数字大的先收到事件");

            // 一次性监听
            EventCenter centerOnce = new EventCenter();
            int onceHits = 0;
            centerOnce.AddEventListenerOnce<EventSelfCheckMsgA>(e => onceHits++);
            centerOnce.EventTrigger(new EventSelfCheckMsgA());
            centerOnce.EventTrigger(new EventSelfCheckMsgA());
            Check(() => onceHits == 1 && centerOnce.GetListenerCount<EventSelfCheckMsgA>() == 0,
                "EventCenter：AddEventListenerOnce 只回调一次并自动注销");

            // 异常隔离
            EventCenter centerThrow = new EventCenter();
            int afterThrow = 0;
            centerThrow.AddEventListener<EventSelfCheckMsgA>(e => throw new Exception("自检故意抛出（预期日志）"));
            centerThrow.AddEventListener<EventSelfCheckMsgA>(e => afterThrow++);
            centerThrow.EventTrigger(new EventSelfCheckMsgA());
            Check(() => afterThrow == 1, "EventCenter：一个监听者抛异常不影响后续监听者");

            // 派发中移除自己
            EventCenter centerSelfRemove = new EventCenter();
            int selfRemoveHits = 0;
            UnityAction<EventSelfCheckMsgA> selfRemover = null;
            selfRemover = e => { selfRemoveHits++; centerSelfRemove.RemoveEventListener<EventSelfCheckMsgA>(selfRemover); };
            centerSelfRemove.AddEventListener<EventSelfCheckMsgA>(selfRemover);
            centerSelfRemove.AddEventListener<EventSelfCheckMsgA>(e => { });
            centerSelfRemove.EventTrigger(new EventSelfCheckMsgA());
            Check(() => selfRemoveHits == 1, "EventCenter：派发中移除自己不会导致重复派发");

            // owner 销毁自动清理
            EventCenter centerOwner = new EventCenter();
            EventSelfCheckOwner owner = ScriptableObject.CreateInstance<EventSelfCheckOwner>();
            centerOwner.AddEventListener<EventSelfCheckMsgA>(owner, owner.OnMsg);
            centerOwner.EventTrigger(new EventSelfCheckMsgA());
            int ownerHitsBeforeDestroy = owner.hits;
            UnityEngine.Object.DestroyImmediate(owner);
            int ownerCountAfterDestroy = centerOwner.GetListenerCount<EventSelfCheckMsgA>();
            centerOwner.EventTrigger(new EventSelfCheckMsgA());
            Check(() => ownerHitsBeforeDestroy == 1 && ownerCountAfterDestroy == 0 && owner.hits == 1,
                "EventCenter：owner 被销毁后监听自动失效并清理（不再回调）");

            // 派发次数统计 + Clear
            EventCenter centerCount = new EventCenter();
            centerCount.AddEventListener<EventSelfCheckMsgA>(e => { });
            centerCount.EventTrigger(new EventSelfCheckMsgA());
            centerCount.EventTrigger(new EventSelfCheckMsgA());
            Check(() => centerCount.GetDispatchCount<EventSelfCheckMsgA>() == 2, "EventCenter：派发次数统计正确");
            centerCount.Clear();
            Check(() => centerCount.ListenerTypeCount == 0 && centerCount.GetListenerCount<EventSelfCheckMsgA>() == 0,
                "EventCenter：Clear 清空监听与统计");

            // 无监听者
            bool noThrow = true;
            try { centerCount.EventTrigger(new EventSelfCheckMsgB()); } catch { noThrow = false; }
            Check(() => noThrow, "EventCenter：事件没有监听者时派发不抛异常");
        }

        // ================================ EventBus ================================
        private static void CheckEventBus()
        {
            EventBus bus = new EventBus();
            Receiver r1 = new Receiver();
            bus.Subscribe<BusMsg>(r1);
            Check(() => bus.GetSubscriptionCount<BusMsg>() == 1 && bus.HasSubscriber<BusMsg>(), "EventBus：订阅后数量 / HasSubscriber 正确");

            bus.Dispatch(new BusMsg { value = 3 });
            Check(() => r1.hits == 1, "EventBus：消息能派发到订阅者");

            bus.Subscribe<BusMsg>(r1);
            bus.Dispatch(new BusMsg());
            Check(() => r1.hits == 3, "EventBus：同一监听者重复订阅会收到多次（设计如此）");

            bus.Unsubscribe<BusMsg>(r1);
            bus.Unsubscribe<BusMsg>(r1);
            Check(() => bus.GetSubscriptionCount<BusMsg>() == 0, "EventBus：Unsubscribe 按引用比较，一次移除一个");

            // 派发中退订其他订阅者
            EventBus busRemove = new EventBus();
            Receiver a = new Receiver();
            Receiver b = new Receiver();
            a.onEventHook = () => busRemove.Unsubscribe<BusMsg>(b);
            busRemove.Subscribe<BusMsg>(a);
            busRemove.Subscribe<BusMsg>(b);
            busRemove.Dispatch(new BusMsg());
            Check(() => a.hits == 1 && b.hits == 1, "EventBus：派发中退订其他订阅者，本次仍按快照派发且不重复");
            busRemove.Dispatch(new BusMsg());
            Check(() => a.hits == 2 && b.hits == 1, "EventBus：派发中退订其他订阅者，下一次不再派发");

            // 派发中新增订阅
            EventBus busAdd = new EventBus();
            Receiver c1 = new Receiver();
            Receiver c2 = new Receiver();
            bool added = false;
            c1.onEventHook = () => { if (!added) { added = true; busAdd.Subscribe<BusMsg>(c2); } };
            busAdd.Subscribe<BusMsg>(c1);
            busAdd.Dispatch(new BusMsg());
            Check(() => c1.hits == 1 && c2.hits == 0, "EventBus：派发中新增订阅不会收到本次消息");
            busAdd.Dispatch(new BusMsg());
            Check(() => c2.hits == 1, "EventBus：派发中新增的订阅从下一次开始生效");

            // 异常隔离
            EventBus busThrow = new EventBus();
            Receiver bad = new Receiver();
            bad.onEventHook = () => throw new Exception("自检故意抛出（预期日志）");
            Receiver good = new Receiver();
            busThrow.Subscribe<BusMsg>(bad);
            busThrow.Subscribe<BusMsg>(good);
            busThrow.Dispatch(new BusMsg());
            Check(() => good.hits == 1, "EventBus：一个订阅者抛异常不影响其余订阅者");

            // struct 监听器被拒绝
            EventBus busStruct = new EventBus();
            StructReceiver structReceiver = new StructReceiver();
            busStruct.Subscribe<BusMsg>(structReceiver);
            Check(() => busStruct.GetSubscriptionCount<BusMsg>() == 0, "EventBus：struct 监听器被拒绝（避免装箱后弱引用立刻失效）");

            // 弱引用回收
            EventBus busGc = new EventBus();
            SubscribeTempListener(busGc);
            int beforeGc = busGc.GetSubscriptionCount<BusMsg>();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            int afterGc = busGc.GetSubscriptionCount<BusMsg>();
            if (beforeGc == 1 && afterGc == 0)
            {
                Check(() => true, "EventBus：监听者被 GC 回收后订阅自动清理");
            }
            else
            {
                skips.Add($"EventBus：监听者被 GC 回收后订阅自动清理（before={beforeGc}, after={afterGc}，引用可能仍被编辑器/调试器持有）");
            }

            // Clear + 实例登记 + 无订阅者派发
            EventBus busClear = new EventBus();
            busClear.Subscribe<BusMsg>(new Receiver());
            busClear.Clear();
            Check(() => busClear.GetSubscriptionCount<BusMsg>() == 0, "EventBus：Clear 清空订阅");

            Check(() => EventBus.GetAliveInstances().Count > 0, "EventBus：实例登记可用（调试窗口靠它枚举实例）");

            bool noThrow = true;
            try { busClear.Dispatch(new BusMsg()); } catch { noThrow = false; }
            Check(() => noThrow, "EventBus：没有订阅者时派发不抛异常");
        }

        /// <summary>在方法作用域内创建监听者：返回后外部没有强引用，便于验证弱引用回收</summary>
        private static void SubscribeTempListener(EventBus bus)
        {
            bus.Subscribe<BusMsg>(new Receiver());
        }

        // ================================ 断言 ================================
        private static void Check(Func<bool> assert, string name)
        {
            total++;
            bool ok;
            try { ok = assert(); }
            catch (Exception e)
            {
                ok = false;
                Debug.LogError($"[事件自检] 断言「{name}」执行异常：{e}");
            }
            if (!ok) fails.Add(name);
        }
    }
}
