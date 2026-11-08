/* ==========================================================================================
 * AppLifecycleService —— 应用级生命周期事件中枢
 *
 * 【职责】
 *   把 Unity 的 OnApplicationPause / OnApplicationFocus / OnLowMemory / OnApplicationQuit
 *   收敛成 4 个 C# 事件，业务侧只订阅事件，不必各自实现一遍 Unity 回调。
 *
 * 【为什么需要】
 *   Unity 的这几个回调只会发给挂了脚本的 MonoBehaviour；多系统各写一遍既重复又容易漏。
 *   统一转发到这里还能保证“某个监听者抛异常不会影响其他监听者”。
 *
 * 【前后台暂停的协作】
 *   NotifyPause(true) 会向 IPauseService 申请一个名为 "Application" 的暂停凭据，
 *   与 UI 菜单暂停共用同一套嵌套计数 —— 所以“后台 + 打开菜单”不会把 timeScale 恢复错。
 *
 * 【线程】全部在主线程调用（Unity 回调本身就在主线程）。
 * 【退出】Quitting 是同步通知：Unity 不会等异步操作，关键存档必须在收到通知时同步落盘。
 * ========================================================================================== */

using System;

namespace UPandaGF
{
    /// <summary>
    /// 应用级生命周期。UPGameRoot 的 Unity 回调只负责转发到这里；
    /// 业务订阅事件即可，不必各自实现 OnApplicationPause/Focus/LowMemory。
    /// <para>事件回调逐个隔离异常；前后台暂停通过 <see cref="IPauseService"/> 的嵌套凭据实现，不会重复暂停。</para>
    /// </summary>
    public sealed class AppLifecycleService : IAppLifecycle, IDisposable
    {
        /// <summary>暂停服务（通常是 GameTimeService）：用于把“应用进入后台”映射成一次嵌套暂停。</summary>
        private readonly IPauseService pause;
        /// <summary>当前持有的“后台暂停”凭据；重复 NotifyPause(true) 不会重复申请。</summary>
        private IDisposable applicationPause;
        /// <summary>已释放标记：释放后的所有通知都直接丢弃。</summary>
        private bool disposed;

        /// <summary>应用是否在后台（以 Unity 的 pause 回调为准）。</summary>
        public bool IsPaused { get; private set; }
        /// <summary>应用是否拥有焦点（切出去看别的窗口时为 false）。默认 true。</summary>
        public bool IsFocused { get; private set; } = true;
        /// <summary>事件：前后台切换。参数为 true 表示进入后台。</summary>
        public event Action<bool> PauseChanged;
        /// <summary>事件：焦点变化。参数为 true 表示重新获得焦点。</summary>
        public event Action<bool> FocusChanged;
        /// <summary>事件：系统低内存告警（此时应释放缓存、卸载非必要资源）。</summary>
        public event Action LowMemory;
        /// <summary>事件：应用即将退出。<b>同步</b>通知，不能在这里 await。</summary>
        public event Action Quitting;

        /// <param name="pause">暂停服务；允许为 null（则前后台不联动暂停，仅发事件）。</param>
        public AppLifecycleService(IPauseService pause) { this.pause = pause; }

        /// <summary>
        /// 前后台变化。
        /// <para>幂等：值未变就直接返回，所以 Unity 重复回调不会重复申请/释放暂停凭据。</para>
        /// <para>进入后台时申请一份 "Application" 暂停凭据（首次暂停才会真正改 timeScale），
        /// 回到前台时释放它；最后一个凭据释放时才恢复时钟。</para>
        /// </summary>
        public void NotifyPause(bool paused)
        {
            if (disposed || IsPaused == paused) return;
            IsPaused = paused;
            // 只在“真进入后台”时持有一份凭据；多次 pause 回调不会重复持有
            if (paused && applicationPause == null) applicationPause = pause?.Pause("Application");
            else if (!paused) { applicationPause?.Dispose(); applicationPause = null; }
            Invoke(PauseChanged, paused, nameof(PauseChanged));
        }
        /// <summary>焦点变化（Alt+Tab、弹窗等）。只发事件，不涉及暂停。</summary>
        public void NotifyFocus(bool focused)
        {
            if (disposed || IsFocused == focused) return;
            IsFocused = focused;
            Invoke(FocusChanged, focused, nameof(FocusChanged));
        }
        /// <summary>低内存告警：转给订阅者去做缓存清理。</summary>
        public void NotifyLowMemory() { if (!disposed) Invoke(LowMemory, nameof(LowMemory)); }
        /// <summary>即将退出：同步通知所有订阅者。此处只能做同步落盘，不能等待异步任务。</summary>
        public void NotifyQuitting() { if (!disposed) Invoke(Quitting, nameof(Quitting)); }

        /// <summary>
        /// 逐个调用 bool 参数事件的监听者。
        /// <para>为什么要逐个 try/catch：直接 evt(value) 时，第一个监听者抛异常会中断后续监听者，
        /// 且异常会冒到 Unity 回调里；这里隔离成“只记日志”。</para>
        /// </summary>
        private static void Invoke(Action<bool> evt, bool value, string name)
        {
            if (evt == null) return;
            foreach (Action<bool> handler in evt.GetInvocationList())
                try { handler(value); }
                catch (Exception e) { PLogger.LogError($"[AppLifecycle] {name} 监听异常：" + e); }
        }
        private static void Invoke(Action evt, string name)
        {
            if (evt == null) return;
            foreach (Action handler in evt.GetInvocationList())
                try { handler(); }
                catch (Exception e) { PLogger.LogError($"[AppLifecycle] {name} 监听异常：" + e); }
        }
        public void Dispose()
        {
            disposed = true;
            applicationPause?.Dispose();
            applicationPause = null;
        }
    }
}
