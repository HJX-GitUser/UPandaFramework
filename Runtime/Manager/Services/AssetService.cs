using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>统一持有协议；取消等待时，迟到资源仍会归还。</summary>
    public sealed class AssetService : IAssetService
    {
        private readonly ResourcesLoader resources;
        private readonly AssetsLoader assets;
        public AssetService(ResourcesLoader resources, AssetsLoader assets)
        { this.resources = resources; this.assets = assets; }
        public Task<AssetLease<T>> AcquireAsync<T>(string path, AssetLoadMethod method = AssetLoadMethod.Resources,
            CancellationToken token = default) where T : UnityEngine.Object
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("资源路径不能为空。", nameof(path));
            if (method == AssetLoadMethod.Resources)
            {
                if (resources == null) throw new ObjectDisposedException(nameof(ResourcesLoader));
                return WaitForLeaseAsync(resources.AcquireAsync<T>(path), token);
            }
            if (method != AssetLoadMethod.AssetBundle) throw new ArgumentOutOfRangeException(nameof(method));
            if (assets == null) throw new ObjectDisposedException(nameof(AssetsLoader));
            return WaitForLeaseAsync(assets.AcquireAsync<T>(path), token);
        }
        public static async Task<AssetLease<T>> WaitForLeaseAsync<T>(Task<AssetLease<T>> loading, CancellationToken token) where T : UnityEngine.Object
        {
            try { await StartupLifecycle.WaitAsync(loading, token); }
            catch
            {
                _ = ReleaseLateAsync(loading);
                throw;
            }
            var lease = await loading;
            if (token.IsCancellationRequested) { lease?.Dispose(); token.ThrowIfCancellationRequested(); }
            return lease;
        }
        private static async Task ReleaseLateAsync<T>(Task<AssetLease<T>> loading) where T : UnityEngine.Object
        {
            try { (await loading)?.Dispose(); } catch (Exception) { /* 已取消调用的失败已观察，避免重复日志。 */ }
        }
    }
}
