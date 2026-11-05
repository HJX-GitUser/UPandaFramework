using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>主线程共享持有凭据；每个持有者只归还一次，最后一个持有者触发原加载记录的释放。</summary>
    internal sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private sealed class State { public T asset; public Action release; public int references = 1; }
        private State state;
        public T Asset => state != null ? state.asset : null;
        public AssetLease(T asset, Action release) { state = new State { asset = asset, release = release }; }
        private AssetLease(State value) { state = value; value.references++; }
        public AssetLease<T> Retain() { return state != null ? new AssetLease<T>(state) : null; }
        public void Dispose()
        {
            var value = state; state = null;
            if (value == null || --value.references > 0) return;
            var release = value.release; value.release = null; value.asset = null;
            release?.Invoke();
        }
    }
}
