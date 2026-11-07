/* ==========================================================================================
 * AssetService —— 统一资源持有层（Resources / AssetBundle 两种后端）
 *
 * 【职责】
 *   向业务提供统一的“取资源”入口，返回 AssetLease<T>（持有凭据）。用完 Dispose 即归还。
 *   业务不需要知道底层是 Resources.Load 还是 AB。
 *
 * 【取消安全（本文件最关键的逻辑）】
 *   加载是异步的，调用方可能在加载途中就取消了等待。但底层加载不会因此停下：
 *   如果直接扔掉结果，就会造成“资源已加载、无人持有、永不释放”的泄漏。
 *   所以：等待被取消时，仍然在后台把加载结果拿到并存好，然后立即 Dispose。
 *   （ReleaseLateAsync 就是这个“迟到归还”的实现。）
 *
 * 【线程】主线程调用；内部不切线程。
 * ========================================================================================== */

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>统一持有协议；取消等待时，迟到资源仍会归还。</summary>
    public sealed class AssetService : IAssetService
    {
        /// <summary>Resources 后端（编辑器/小资源常用）。可能为 null：表示本工程不启用。</summary>
        private readonly ResourcesLoader resources;
        /// <summary>AssetBundle 后端（热更资源）。可能为 null：表示本工程不启用。</summary>
        private readonly AssetsLoader assets;
        public AssetService(ResourcesLoader resources, AssetsLoader assets)
        { this.resources = resources; this.assets = assets; }
        /// <summary>
        /// 按模式取一份资源持有权。
        /// <para>参数问题用同步异常报错（调用方代码错），后端未初始化用 ObjectDisposedException。</para>
        /// </summary>
        public Task<AssetLease<T>> AcquireAsync<T>(string path, AssetLoadMethod method = AssetLoadMethod.Resources,
            CancellationToken token = default) where T : UnityEngine.Object
        {
            // 已取消就不要发起加载；空路径也直接拒绝（避免后端报难懂的错）
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("资源路径不能为空。", nameof(path));
            if (method == AssetLoadMethod.Resources)
            {
                if (resources == null) throw new ObjectDisposedException(nameof(ResourcesLoader));
                return WaitForLeaseAsync(resources.AcquireAsync<T>(path), token);
            }
            // 防御未知枚举值：将来加了模式却没在这里分支，应该立即暴露
            if (method != AssetLoadMethod.AssetBundle) throw new ArgumentOutOfRangeException(nameof(method));
            if (assets == null) throw new ObjectDisposedException(nameof(AssetsLoader));
            return WaitForLeaseAsync(assets.AcquireAsync<T>(path), token);
        }
        /// <summary>
        /// 等待加载完成，但等待过程可取消。
        /// <para>三种结果：成功返回 lease；取消则归还迟到资源后抛；失败则归还迟到资源后抛原异常。</para>
        /// </summary>
        public static async Task<AssetLease<T>> WaitForLeaseAsync<T>(Task<AssetLease<T>> loading, CancellationToken token) where T : UnityEngine.Object
        {
            try { await StartupLifecycle.WaitAsync(loading, token); }
            catch
            {
                // 等待被取消/失败：底层加载可能仍在跑，不能一走了之 —— 交给 ReleaseLateAsync 盯着它完成并归还
                _ = ReleaseLateAsync(loading);
                throw;
            }
            var lease = await loading;
            // 走到这里说明加载完了，但令牌可能刚好在这瞬间被取消：归还后再抛出，否则调用方拿不到凭据会泄漏
            if (token.IsCancellationRequested) { lease?.Dispose(); token.ThrowIfCancellationRequested(); }
            return lease;
        }
        /// <summary>
        /// “迟到归还”：等底层加载真正结束，拿到结果后立即 Dispose。
        /// <para>加载自身的异常必须吞掉（调用方已经因取消而离开了，再报一次只会造成重复日志）。</para>
        /// </summary>
        private static async Task ReleaseLateAsync<T>(Task<AssetLease<T>> loading) where T : UnityEngine.Object
        {
            try { (await loading)?.Dispose(); } catch (Exception) { /* 已取消调用的失败已观察，避免重复日志。 */ }
        }
    }
}
