using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace UPandaGF
{
    public enum StartupState { Created, Initializing, Ready, Failed, Stopping, Stopped }

    /// <summary>一个启动步骤的耗时记录；Completed 为 false 表示该步抛异常或被取消。</summary>
    public readonly struct StartupStepMetric
    {
        public readonly string Name;
        public readonly long Milliseconds;
        public readonly bool Completed;
        public StartupStepMetric(string name, long milliseconds, bool completed)
        { Name = name; Milliseconds = milliseconds; Completed = completed; }
    }

    /// <summary>启动模块：把"一类能力的初始化/释放"封装成可自注册的单元，避免所有步骤都堆在 UPGameRoot 里。</summary>
    public interface IStartupModule
    {
        string Name { get; }
        Task StartAsync(CancellationToken token);
        Task StopAsync();
    }

    /// <summary>主线程启动协调器。失败步骤也参与回滚；同一实例不可重试，关闭可重复等待。</summary>
    public sealed class StartupLifecycle
    {
        private sealed class Step
        {
            public string Name;
            public Func<CancellationToken, Task> Start;
            public Func<Task> Stop;
        }
        private readonly List<Step> steps = new List<Step>();
        private readonly Stack<Step> entered = new Stack<Step>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Task initialization, shutdown;
        private readonly List<StartupStepMetric> metrics = new List<StartupStepMetric>();
        public StartupState State { get; private set; } = StartupState.Created;
        public Exception Error { get; private set; }
        public string CurrentStep { get; private set; }
        /// <summary>已执行步骤的耗时（含失败步骤），用于定位启动瓶颈。</summary>
        public IReadOnlyList<StartupStepMetric> Metrics => metrics;

        public void Add(string name, Func<CancellationToken, Task> start, Func<Task> stop = null)
        {
            if (State != StartupState.Created) throw new InvalidOperationException("启动后不能注册步骤。");
            steps.Add(new Step { Name = name, Start = start ?? throw new ArgumentNullException(nameof(start)), Stop = stop });
        }
        public void Add(IStartupModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            Add(module.Name, module.StartAsync, module.StopAsync);
        }
        public Task InitializeAsync()
        {
            if (initialization != null) return initialization;
            if (State != StartupState.Created) return Task.FromException(new InvalidOperationException("协调器已关闭。"));
            State = StartupState.Initializing;
            return initialization = RunAsync();
        }
        private async Task RunAsync()
        {
            // 保证调用者先取得共享 Task，也给业务 Awake 留出订阅旧事件的时机。
            await Task.Yield();
            try
            {
                foreach (var step in steps)
                {
                    lifetime.Token.ThrowIfCancellationRequested();
                    CurrentStep = step.Name;
                    entered.Push(step);
                    var watch = Stopwatch.StartNew();
                    bool completed = false;
                    try { await step.Start(lifetime.Token); completed = true; }
                    finally
                    {
                        watch.Stop();
                        metrics.Add(new StartupStepMetric(step.Name, watch.ElapsedMilliseconds, completed));
                    }
                    lifetime.Token.ThrowIfCancellationRequested();
                }
                State = StartupState.Ready;
            }
            catch (Exception e)
            {
                Error = e;
                var cleanupErrors = await RollbackAsync();
                if (cleanupErrors.Count > 0)
                {
                    cleanupErrors.Insert(0, e);
                    Error = new AggregateException("启动失败且部分释放失败。", cleanupErrors);
                }
                if (State != StartupState.Stopping) State = StartupState.Failed;
                if (cleanupErrors.Count > 0) throw Error;
                throw;
            }
            finally { CurrentStep = null; }
        }
        public Task ShutdownAsync()
        {
            if (shutdown != null) return shutdown;
            State = StartupState.Stopping;
            lifetime.Cancel();
            return shutdown = StopAsync();
        }
        private async Task StopAsync()
        {
            await Task.Yield();
            try
            {
                if (initialization != null)
                    try { await initialization; } catch (Exception) { /* 原始结果仍保留在启动 Task。 */ }
                var errors = await RollbackAsync();
                if (errors.Count > 0) throw new AggregateException("服务释放失败。", errors);
            }
            finally { State = StartupState.Stopped; lifetime.Dispose(); }
        }
        private async Task<List<Exception>> RollbackAsync()
        {
            var errors = new List<Exception>();
            while (entered.Count > 0)
            {
                var step = entered.Pop();
                try { if (step.Stop != null) await step.Stop(); }
                catch (Exception e) { errors.Add(new InvalidOperationException("释放步骤失败：" + step.Name, e)); }
            }
            return errors;
        }
        /// <summary>取消一个等待者不会取消共享启动任务。</summary>
        public static async Task WaitAsync(Task task, CancellationToken token)
        {
            if (!token.CanBeCanceled) { await task; return; }
            token.ThrowIfCancellationRequested();
            var canceled = new TaskCompletionSource<bool>();
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, canceled.Task) != task) throw new OperationCanceledException(token);
                await task;
            }
        }
    }
}
