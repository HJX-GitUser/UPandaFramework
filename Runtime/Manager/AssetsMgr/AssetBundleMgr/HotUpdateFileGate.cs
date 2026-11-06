using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>热更启动维护与框架 AB 原生加载互斥；等待不阻塞 Unity 主线程。</summary>
    internal static class HotUpdateFileGate
    {
        private static readonly object sync = new object();
        private static readonly Queue<TaskCompletionSource<IDisposable>> waiting = new Queue<TaskCompletionSource<IDisposable>>();
        private static int readers;
        private static bool maintenance;
        private sealed class Lease : IDisposable
        {
            private bool released; private readonly bool writer;
            public Lease(bool writer) { this.writer = writer; }
            public void Dispose()
            {
                List<TaskCompletionSource<IDisposable>> resume = null;
                lock (sync)
                {
                    if (released) return; released = true;
                    if (!writer) { readers--; return; }
                    maintenance = false; resume = new List<TaskCompletionSource<IDisposable>>(waiting); waiting.Clear(); readers += resume.Count;
                }
                foreach (var task in resume) task.TrySetResult(new Lease(false));
            }
        }
        public static IDisposable TryMaintenance()
        { lock (sync) { if (maintenance || readers != 0) return null; maintenance = true; return new Lease(true); } }
        public static Task<IDisposable> EnterLoad()
        {
            lock (sync)
            {
                if (!maintenance) { readers++; return Task.FromResult<IDisposable>(new Lease(false)); }
                var task = new TaskCompletionSource<IDisposable>(); waiting.Enqueue(task); return task.Task;
            }
        }
    }
}
