/* ==========================================================================================
 * LifetimeScope —— 主线程生命周期作用域
 *
 * 【它是什么】
 *   一个“作用域”= 1 个 CancellationTokenSource + 1 份持有物列表。
 *   场景、UI 面板、一次性业务流程都可以各开一个作用域，用 Own 登记 IDisposable。
 *
 * 【两条铁律】
 *   1) 先取消，再逆序释放：先让异步任务/订阅/计时器停下，再还资源，避免“释放后回调还在跑”。
 *   2) 迟到的持有物立即归还：作用域已关时再 Own，会当场 Dispose 并抛异常，防止泄漏。
 *
 * 【与 DisposableAction 的关系】
 *   大量场景只需要“一个 lambda 的释放逻辑”，用 DisposableAction 包一层就能 Own 进作用域。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 把一个 Action 包装成 IDisposable。
    /// <para>特性：可重复 Dispose（只执行一次），且执行后释放引用，避免 lambda 长期持有上下文。</para>
    /// </summary>
    public sealed class DisposableAction : IDisposable
    {
        private Action action;
        public DisposableAction(Action action) { this.action = action; }
        public void Dispose() { var value = action; action = null; value?.Invoke(); }
    }
    /// <summary>
    /// 主线程作用域。先取消任务，再逆序释放持有物；迟到的持有物立即归还。
    /// <para>典型用法：场景作用域（SceneService 自动建）、UI 面板作用域、一次性流程作用域。</para>
    /// <para>不是线程安全的：只能在 Unity 主线程使用。</para>
    /// </summary>
    public sealed class LifetimeScope : IDisposable, ILifetimeScope
    {
        /// <summary>该作用域的取消源：Cancel/Dispose 时取消，异步任务应把它的 Token 传下去。</summary>
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken token;
        /// <summary>持有物列表。Dispose 时从后往前释放（后登记的先释放，通常是“依赖方”）。</summary>
        private readonly List<IDisposable> owned = new List<IDisposable>();
        public string Name { get; }
        public CancellationToken Token => token;
        public bool IsDisposed { get; private set; }
        public LifetimeScope(string name) { Name = name; token = lifetime.Token; }
        /// <summary>
        /// 登记一个持有物。
        /// <para>已释放 → 立即 Dispose 并抛 ObjectDisposedException（调用方有 bug，必须暴露）。</para>
        /// <para>已取消但未释放 → 立即 Dispose 并抛 OperationCanceledException（拒绝在收尾阶段再接管新资源）。</para>
        /// </summary>
        public T Own<T>(T value) where T : IDisposable
        {
            if (IsDisposed) { value?.Dispose(); throw new ObjectDisposedException(Name); }
            if (token.IsCancellationRequested) { value?.Dispose(); token.ThrowIfCancellationRequested(); }
            if (value != null) owned.Add(value);
            return value;
        }
        /// <summary>订阅事件总线，并把“退订”作为持有物挂到本作用域（作用域释放即退订）。</summary>
        public IDisposable Subscribe<T>(EventBus bus, IEventListener<T> listener) where T : struct
        {
            if (IsDisposed) throw new ObjectDisposedException(Name);
            return ScopeSubscriptions.Subscribe(this, bus, listener);
        }
        /// <summary>只取消、不释放：用于“先让任务停下，但资源还要继续用一会儿”的场合（如场景卸载中）。</summary>
        public void Cancel()
        {
            if (!IsDisposed && !token.IsCancellationRequested) lifetime.Cancel();
        }
        /// <summary>
        /// 关闭作用域：先 Cancel，再逆序 Dispose 全部持有物，最后释放 CTS。
        /// <para>释放过程中的异常不会中断其他持有物的释放，全部收集后聚合成 AggregateException 抛出。</para>
        /// </summary>
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var errors = new List<Exception>();
            try { lifetime.Cancel(); } catch (Exception e) { errors.Add(e); }
            for (int i = owned.Count - 1; i >= 0; i--)
                try { owned[i].Dispose(); } catch (Exception e) { errors.Add(e); }
            owned.Clear(); lifetime.Dispose();
            if (errors.Count > 0) throw new AggregateException("作用域释放失败：" + Name, errors);
        }
    }
}
