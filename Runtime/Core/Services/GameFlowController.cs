/* ==========================================================================================
 * GameFlowController —— 游戏流程状态机（Login → Lobby → Battle ...）
 *
 * 【职责】
 *   管理“游戏处于哪个流程”。每个流程实现 IGameState，控制器负责：
 *   调 CanEnter 守卫 → 退出旧状态 → 进入新状态 → 广播变更。
 *
 * 【关键语义】
 *   1) 切换串行化（SemaphoreSlim）：并发 GoToAsync 会排队，不会两个状态同时进门。
 *   2) CanEnter 拒绝时：抛异常且不离开当前状态（不会出现“旧状态已退出、新状态没进”）。
 *   3) EnterAsync 抛异常时：current 回滚到旧状态，Status = Failed，保留 CurrentStateId，
 *      由上层决定恢复到哪个状态。
 *   4) 不要在 EnterAsync/ExitAsync 内部再调 GoToAsync —— 切换锁已被持有，会死锁。
 *
 * 【分工】StartupLifecycle 管“框架怎么起来”；本类管“游戏怎么玩”。
 * ========================================================================================== */

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
        /// <summary>状态 Id → 状态实现。Id 由 IGameState.Id 提供，必须唯一。</summary>
        private readonly Dictionary<string, IGameState> states = new Dictionary<string, IGameState>();
        /// <summary>切换闸门：保证同一时刻只有一个状态切换在跑（含 Exit + Enter）。</summary>
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        /// <summary>装配时注入的服务集合，进入状态时通过 GameFlowContext 传给状态。</summary>
        private IGameServices services;
        /// <summary>当前状态；StartAsync 前为 null。</summary>
        private IGameState current;
        private bool disposed;

        /// <summary>当前状态 Id；未启动为 null。</summary>
        public string CurrentStateId => current?.Id;
        /// <summary>当前流程状态：Idle / Transitioning / Ready / Failed。</summary>
        public GameFlowStatus Status { get; private set; } = GameFlowStatus.Idle;
        /// <summary>状态成功切换后广播；监听者异常被逐个隔离。</summary>
        public event Action<GameFlowContext> Changed;

        /// <summary>注入服务集合（通常由 GameServices 构造时调用）。</summary>
        public void Configure(IGameServices value) => services = value;
        /// <summary>注册一个流程状态。Id 重复会抛异常（避免后注册静默覆盖）。</summary>
        public void Register(IGameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrWhiteSpace(state.Id)) throw new ArgumentException("状态 Id 不能为空。");
            if (states.ContainsKey(state.Id)) throw new InvalidOperationException("重复注册流程状态：" + state.Id);
            states.Add(state.Id, state);
        }
        /// <summary>启动流程（只能调一次）。之后用 GoToAsync 切换。</summary>
        public Task StartAsync(string initialStateId, object payload = null, CancellationToken token = default)
        {
            if (current != null) throw new InvalidOperationException("流程已启动，请使用 GoToAsync 切换。");
            return GoToAsync(initialStateId, payload, token);
        }
        /// <summary>
        /// 切换到目标状态。
        /// <para>执行顺序：排队 → 同状态直接返回 → CanEnter 守卫 → ExitAsync(旧) → EnterAsync(新) → Ready → Changed。</para>
        /// <para>Enter 失败会回滚 current 并把 Status 置为 Failed（旧状态的对象已退出，需要上层决定恢复策略）。</para>
        /// </summary>
        public async Task GoToAsync(string stateId, object payload = null, CancellationToken token = default)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameFlowController));
            if (string.IsNullOrWhiteSpace(stateId)) throw new ArgumentException("目标状态不能为空。", nameof(stateId));
            if (!states.TryGetValue(stateId, out var next)) throw new KeyNotFoundException("未注册的流程状态：" + stateId);
            await gate.WaitAsync(token);
            try
            {
                // 已经是目标状态且处于 Ready：幂等返回（避免重复 Enter）
                if (ReferenceEquals(current, next) && Status == GameFlowStatus.Ready) return;
                // 上下文里带“从哪来/到哪去/payload”，状态实现可据此做差异化处理
                var context = new GameFlowContext(services, current?.Id, stateId, payload);
                // 进入守卫：拒绝时直接抛，当前状态保持不变
                if (!next.CanEnter(context, out string reason))
                    throw new InvalidOperationException($"流程状态 {stateId} 拒绝进入：{reason}");
                Status = GameFlowStatus.Transitioning;
                var previous = current;
                // 先退出旧状态（旧状态自己负责停协程/放资源）
                if (previous != null) await previous.ExitAsync(token);
                current = next;
                try { await next.EnterAsync(context, token); }
                catch { current = previous; Status = GameFlowStatus.Failed; throw; }
                Status = GameFlowStatus.Ready;
                NotifyChanged(context);
            }
            finally { gate.Release(); }
        }
        /// <summary>广播状态变更。逐个监听者 try/catch：业务 UI 抛异常不能把流程切换带崩。</summary>
        private void NotifyChanged(GameFlowContext context)
        {
            if (Changed == null) return;
            foreach (Action<GameFlowContext> handler in Changed.GetInvocationList())
                try { handler(context); }
                catch (Exception e) { PLogger.LogError("[GameFlow] 状态变更监听异常：" + e); }
        }
        /// <summary>关闭：退出当前状态并回到 Idle。退出异常只记日志，保证关闭流程能走完。</summary>
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
        /// <summary>释放：清空状态表并释放闸门。释放后 GoToAsync 抛 ObjectDisposedException。</summary>
        public void Dispose() { disposed = true; states.Clear(); gate.Dispose(); }
    }
}
