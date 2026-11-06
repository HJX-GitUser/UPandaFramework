using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>
    /// 游戏流程控制器。所有切换经 SemaphoreSlim 串行化；
    /// Enter 失败会回到 Failed 状态并保留原 CurrentStateId，由上层决定进入哪个恢复状态。
    /// </summary>
    public sealed class GameFlowController : IGameFlowService, IDisposable
    {
        private readonly Dictionary<string, IGameState> states = new Dictionary<string, IGameState>();
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private IGameServices services;
        private IGameState current;
        private bool disposed;

        public string CurrentStateId => current?.Id;
        public GameFlowStatus Status { get; private set; } = GameFlowStatus.Idle;
        public event Action<GameFlowContext> Changed;

        public void Configure(IGameServices value) => services = value;
        public void Register(IGameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(state.Id)) throw new ArgumentException("状态 Id 不能为空。");
            if (states.ContainsKey(state.Id)) throw new InvalidOperationException("重复注册流程状态：" + state.Id);
            states.Add(state.Id, state);
        }
        public Task StartAsync(string initialStateId, object payload = null, CancellationToken token = default)
        {
            if (current != null) throw new InvalidOperationException("流程已启动，请使用 GoToAsync 切换。");
            return GoToAsync(initialStateId, payload, token);
        }
        public async Task GoToAsync(string stateId, object payload = null, CancellationToken token = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameFlowController));
            if (string.IsNullOrWhiteSpace(stateId)) throw new ArgumentException("目标状态不能为空。", nameof(stateId));
            if (!states.TryGetValue(stateId, out var next)) throw new KeyNotFoundException("未注册的流程状态：" + stateId);
            await gate.WaitAsync(token);
            try
            {
                if (ReferenceEquals(current, next) && Status == GameFlowStatus.Ready) return;
                var context = new GameFlowContext(services, current?.Id, stateId, payload);
                if (!next.CanEnter(context, out string reason))
                    throw new InvalidOperationException($"流程状态 {stateId} 拒绝进入：{reason}");
                Status = GameFlowStatus.Transitioning;
                var previous = current;
                if (previous != null) await previous.ExitAsync(token);
                current = next;
                try { await next.EnterAsync(context, token); }
                catch { current = previous; Status = GameFlowStatus.Failed; throw; }
                Status = GameFlowStatus.Ready;
                NotifyChanged(context);
            }
            finally { gate.Release(); }
        }
        private void NotifyChanged(GameFlowContext context)
        {
            if (Changed == null) return;
            foreach (Action<GameFlowContext> handler in Changed.GetInvocationList())
                try { handler(context); }
                catch (Exception e) { PLogger.LogError("[GameFlow] 状态变更监听异常：" + e); }
        }
        public async Task ShutdownAsync()
        {
            if (disposed) return;
            await gate.WaitAsync();
            try
            {
                if (current != null)
                {
                    try { await current.ExitAsync(CancellationToken.None); }
                    catch (Exception e) { PLogger.LogError("[GameFlow] 退出状态异常：" + e); }
                    current = null;
                }
                Status = GameFlowStatus.Idle;
            }
            finally { gate.Release(); }
        }
        public void Dispose() { disposed = true; states.Clear(); gate.Dispose(); }
    }
}
