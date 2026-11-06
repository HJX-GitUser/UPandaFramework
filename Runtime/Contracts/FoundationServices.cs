using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>计时器句柄；Dispose 与 Cancel 等价，可重复调用。</summary>
    public interface ITimerHandle : IDisposable
    {
        bool IsCompleted { get; }
        bool IsCanceled { get; }
        void Cancel();
    }

    /// <summary>
    /// 统一计时服务。scaled 计时器受暂停/timeScale 影响，unscaled 不受影响。
    /// 回调在主线程执行；Delay(0) 也会推迟到下一次 Tick，避免在调用栈内重入。
    /// </summary>
    public interface ITimerService
    {
        ITimerHandle Delay(float seconds, Action callback, bool scaled = true);
        ITimerHandle Every(float interval, Action callback, bool scaled = true);
        Task DelayAsync(float seconds, CancellationToken token = default, bool scaled = true);
    }

    public enum ConfigIssueLevel { Warning, Error }

    public readonly struct ConfigIssue
    {
        public readonly string Key;
        public readonly string Message;
        public readonly ConfigIssueLevel Level;
        public ConfigIssue(string key, string message, ConfigIssueLevel level)
        { Key = key; Message = message; Level = level; }
        public override string ToString() => $"[{Level}] {Key}: {Message}";
    }

    /// <summary>
    /// 配置注册中心：集中持有业务配置、按 key 应用 JSON 覆盖（环境/渠道差异）、统一执行校验。
    /// 启动期 Register，运行期只读；修改配置请走 ApplyOverrides 并重新 Validate。
    /// </summary>
    public interface IConfigService
    {
        void Register<T>(string key, T value, int version = 1, Action<T, List<ConfigIssue>> validator = null) where T : class;
        T Get<T>(string key) where T : class;
        bool TryGet<T>(string key, out T value) where T : class;
        int GetVersion(string key);
        void ApplyOverrides(IReadOnlyDictionary<string, string> jsonOverrides);
        IReadOnlyList<ConfigIssue> Validate();
    }

    public enum GameFlowStatus { Idle, Transitioning, Ready, Failed }

    public readonly struct GameFlowContext
    {
        public readonly IGameServices Services;
        public readonly string From;
        public readonly string To;
        public readonly object Payload;
        public GameFlowContext(IGameServices services, string from, string to, object payload)
        { Services = services; From = from; To = to; Payload = payload; }
    }

    /// <summary>游戏流程状态（如 Boot/Login/Lobby/Battle/Result）。Enter/Exit 内不要再调用 GoToAsync，切换由外层驱动。</summary>
    public interface IGameState
    {
        string Id { get; }
        bool CanEnter(GameFlowContext context, out string reason);
        Task EnterAsync(GameFlowContext context, CancellationToken token);
        Task ExitAsync(CancellationToken token);
    }

    /// <summary>
    /// 游戏流程控制器：状态注册、进入守卫、串行切换、失败状态可见。
    /// 与 StartupLifecycle 的区别：StartupLifecycle 管"框架怎么起来"，本服务管"游戏怎么玩"。
    /// </summary>
    public interface IGameFlowService
    {
        string CurrentStateId { get; }
        GameFlowStatus Status { get; }
        event Action<GameFlowContext> Changed;
        void Register(IGameState state);
        Task StartAsync(string initialStateId, object payload = null, CancellationToken token = default);
        Task GoToAsync(string stateId, object payload = null, CancellationToken token = default);
    }

    /// <summary>应用级生命周期：前后台、焦点、低内存、退出。由 UPGameRoot 的 Unity 回调驱动。</summary>
    public interface IAppLifecycle
    {
        bool IsPaused { get; }
        bool IsFocused { get; }
        event Action<bool> PauseChanged;
        event Action<bool> FocusChanged;
        event Action LowMemory;
        event Action Quitting;
    }
}
