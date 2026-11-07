using System;

namespace UPandaGF
{
    /// <summary>
    /// <see cref="IPoolService"/> 的默认实现：创建并配置对象池，把"池的生命周期"交给调用方或作用域。
    ///
    /// <para>为什么不是全局单例：池的价值在于生命周期与场景/流程对齐。全局单例池会让
    /// 跨场景实例长期驻留，且无法判断"这个实例属于哪个场景"。需要跨场景复用时用 <see cref="Shared"/>。</para>
    ///
    /// <para>线程：主线程。GameObjectPoolMgr 内部所有入口都要求主线程。</para>
    /// </summary>
    public sealed class PoolService : IPoolService, IDisposable
    {
        private readonly IAssetService assets;
        private readonly ILogService log;
        private GameObjectPoolMgr shared;
        private bool disposed;

        public PoolService(IAssetService assets, ILogService log = null)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.log = log;
        }

        /// <summary>与整个服务容器同生命周期的共享池（首次访问时创建）。</summary>
        public IPool Shared
        {
            get
            {
                if (disposed) throw new ObjectDisposedException(nameof(PoolService));
                return shared ?? (shared = Create());
            }
        }

        /// <summary>
        /// 创建一个由作用域拥有的池：作用域 Dispose 时销毁空闲实例和已借出实例。
        /// <para>这是场景池的推荐用法；不要把它挂到比使用方更短的作用域上。</para>
        /// </summary>
        public IPool CreatePool(ILifetimeScope scope)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            if (disposed) throw new ObjectDisposedException(nameof(PoolService));
            if (scope.IsDisposed) throw new InvalidOperationException("作用域已释放，不能创建对象池。");
            var pool = Create();
            scope.Own(new DisposableAction(pool.DisposeAllInstances));
            return pool;
        }

        private GameObjectPoolMgr Create()
        {
            var pool = new GameObjectPoolMgr();
            pool.Configure(assets);
            return pool;
        }

        /// <summary>释放共享池；作用域池由各自作用域负责，这里不越权处理。</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var pool = shared;
            shared = null;
            if (pool == null) return;
            try { pool.DisposeAllInstances(); }
            catch (Exception e) { log?.LogException(e); }
        }
    }
}
