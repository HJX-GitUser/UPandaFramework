using System;

namespace UPandaGF
{
    /// <summary>
    /// 应用级生命周期。UPGameRoot 的 Unity 回调只负责转发到这里；
    /// 业务订阅事件即可，不必各自实现 OnApplicationPause/Focus/LowMemory。
    /// </summary>
    public sealed class AppLifecycleService : IAppLifecycle, IDisposable
    {
        private readonly IPauseService pause;
        private IDisposable applicationPause;
        private bool disposed;

        public bool IsPaused { get; private set; }
        public bool IsFocused { get; private set; } = true;
        public event Action<bool> PauseChanged;
        public event Action<bool> FocusChanged;
        public event Action LowMemory;
        public event Action Quitting;

        public AppLifecycleService(IPauseService pause) { this.pause = pause; }

        public void NotifyPause(bool paused)
        {
            if (disposed || IsPaused == paused) return;
            IsPaused = paused;
            if (paused && applicationPause == null) applicationPause = pause?.Pause("Application");
            else if (!paused) { applicationPause?.Dispose(); applicationPause = null; }
            Invoke(PauseChanged, paused, nameof(PauseChanged));
        }
        public void NotifyFocus(bool focused)
        {
            if (disposed || IsFocused == focused) return;
            IsFocused = focused;
            Invoke(FocusChanged, focused, nameof(FocusChanged));
        }
        public void NotifyLowMemory() { if (!disposed) Invoke(LowMemory, nameof(LowMemory)); }
        public void NotifyQuitting() { if (!disposed) Invoke(Quitting, nameof(Quitting)); }

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
