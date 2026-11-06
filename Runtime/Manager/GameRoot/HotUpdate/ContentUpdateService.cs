using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>启动期内容更新；负责下载、事务恢复、旧版本降级及统计。单实例只执行一次。</summary>
    public sealed class ContentUpdateService
    {
        private readonly UPGameRootConfig config;
        private readonly Downloader downloader;
        private readonly CancellationTokenSource hotUpdateLifetime = new CancellationTokenSource();
        private readonly List<DownloadItem> hotUpdateDownloads = new List<DownloadItem>();
        private bool destroyed;
        private Task<ABSourcesRelated> execution;
        public HotUpdateStatistics LastHotUpdateStatistics { get; private set; }
        public ContentUpdateService(UPGameRootConfig config, Downloader downloader)
        { this.config = config; this.downloader = downloader; }
        public Task<ABSourcesRelated> ExecuteAsync() => execution ?? (execution = UpdateAssets());
        public void Cancel()
        {
            if (destroyed) return;
            destroyed = true;
            hotUpdateLifetime.Cancel();
            CancelHotUpdateDownloads();
        }
        public async Task ShutdownAsync()
        {
            Cancel();
            try { if (execution != null) await execution; }
            catch (OperationCanceledException) { }
            finally { hotUpdateLifetime.Dispose(); }
        }
        private void ThrowIfDestroyed() => hotUpdateLifetime.Token.ThrowIfCancellationRequested();
        private async Task<ABSourcesRelated> UpdateAssets()
        {
            var totalClock = System.Diagnostics.Stopwatch.StartNew();
            ThrowIfDestroyed();
            // Unity 路径、配置和原生 AB 状态只在主线程读取；工作线程只接收快照。
            string persistent = Application.persistentDataPath, relativeRoot = config.LoadAssetPath, remoteUrl = config.remoteURL;
            bool updateEnabled = config.enableAssetUpdate;
            if (!updateEnabled && !await HotUpdateStorage.RecoveryRequiredAsync(persistent)) return null;
            ThrowIfDestroyed();
            var maintenance = HotUpdateFileGate.TryMaintenance();
            if (maintenance == null) throw new InvalidOperationException("已有 AB 加载或其他热更维护，不能覆盖正在使用的文件");
            if (AssetBundle.GetAllLoadedAssetBundles().Any())
            { maintenance.Dispose(); throw new InvalidOperationException("启动热更前已有已加载 AB；请在资源系统初始化前完成更新"); }
            var aes = new AssetBundleClassificationWindowConfig { enable = config.AssetAESConfig.enable,
                AESKEY = config.AssetAESConfig.AESKEY, AESIV = config.AssetAESConfig.AESIV };
            // 打开/恢复失败不得降级：可能仍有未恢复的混合版本。
            var store = await HotUpdateStorage.OpenAsync(persistent, relativeRoot, config.hotUpdate,
                hotUpdateLifetime.Token, maintenanceScope: maintenance);
            try
            {
                ThrowIfDestroyed();
                if (!updateEnabled) return null;
                var bundled = await LoadBundledSourceRef(aes);
                ThrowIfDestroyed();
                await store.BeginStageAsync();
                var manifest = new DownloadItem { url = CombineUrl(remoteUrl, "AssetBundles", UPGameRoot.assetData),
                    savePath = store.StagedManifestPath, fileName = UPGameRoot.assetData, enableResume = false, maximumBytes = store.ManifestDownloadLimit };
                var downloadClock = System.Diagnostics.Stopwatch.StartNew();
                bool manifestReady = await DownloadBatchAndWait(new List<DownloadItem> { manifest });
                store.Statistics.manifestDownloadMilliseconds = downloadClock.ElapsedMilliseconds;
                if (!manifestReady) return await LoadLocalSourceRef(store, aes);
                ThrowIfDestroyed();
                var plan = await store.PrepareAsync(aes, bundled);
                ThrowIfDestroyed();
                var downloads = plan.Downloads.Select(item => new DownloadItem {
                    url = CombineUrl(remoteUrl, relativeRoot, item.Name), savePath = item.StagedPath, fileName = item.Name,
                    fileSize = item.Size, maximumBytes = item.Size, enableResume = false }).ToList();
                downloadClock.Restart();
                bool bundlesReady = await DownloadBatchAndWait(downloads);
                store.Statistics.bundleDownloadMilliseconds = downloadClock.ElapsedMilliseconds;
                if (!bundlesReady) return await LoadLocalSourceRef(store, aes);
                ThrowIfDestroyed();
                await store.CommitAsync(plan);
                if (store.RecoveryPending) throw new IOException("缓存维护恢复未完成，停止资源初始化");
                ThrowIfDestroyed();
                return plan.Manifest;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                if (store.RecoveryPending || store.Statistics.committed) throw;
                PLogger.LogError("资源更新失败，保留旧版本：" + e.Message);
                return await LoadLocalSourceRef(store, aes);
            }
            finally
            {
                CancelHotUpdateDownloads();
                await store.DisposeAsync();
                store.Statistics.totalMilliseconds = totalClock.ElapsedMilliseconds;
                LastHotUpdateStatistics = store.Statistics;
                foreach (string warning in store.Warnings) if (!destroyed) PLogger.LogWarning(warning);
                if (!destroyed) PLogger.Log($"热更文件处理：校验 {store.Statistics.verifyMilliseconds}ms，提交 {store.Statistics.commitMilliseconds}ms，"
                    + $"回收 {store.Statistics.reclaimedBytes} 字节，更新 {store.Statistics.downloadedBundles} 个包，提交={store.Statistics.committed}");
            }
        }

        private async Task<ABSourcesRelated> LoadBundledSourceRef(AssetBundleClassificationWindowConfig aes)
        {
            try
            {
                if (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.WebGLPlayer
                    && !File.Exists(Path.Combine(Application.streamingAssetsPath, "Data", UPGameRoot.assetData))) return null;
                var bytes = await StreamingAssetsLoader.LoadBinaryDataAsync("/Data/" + UPGameRoot.assetData);
                return bytes == null ? null : await HotUpdateStorage.DecodeManifestAsync(bytes, aes);
            }
            catch (Exception e) { PLogger.LogWarning("随包清单不可用，本轮跳过旧包回收：" + e.Message); return null; }
        }
        private async Task<ABSourcesRelated> LoadLocalSourceRef(HotUpdateStorage store, AssetBundleClassificationWindowConfig aes)
        {
            ThrowIfDestroyed();
            try { return await store.ReadLocalAsync(aes, verifyFiles: true); }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { PLogger.LogWarning("本地清单或文件不可用，尝试随包清单：" + e.Message); return null; }
        }
        private void CancelHotUpdateDownloads()
        {
            if (downloader != null)
                foreach (var item in hotUpdateDownloads)
                    if (item.state != DownloadState.Completed && item.state != DownloadState.Failed && item.state != DownloadState.Canceled)
                        downloader.CancelDownload(item);
            hotUpdateDownloads.Clear();
        }

        /// <summary>
        /// 批量下载并等待完成。
        /// <para>成功与否**按本批次自己的任务状态**判定：下载器的完成事件与失败列表都是全局的，
        /// 别的批次的任务同样会影响它们（本流程通常只有本批次，但不能假设）。</para>
        /// <para>仅轮询本批次任务的终态；超时取消本批次，不依赖全局完成事件。</para>
        /// </summary>
        private async Task<bool> DownloadBatchAndWait(List<DownloadItem> downloadList)
        {
            if (downloadList == null || downloadList.Count == 0)
            {
                PLogger.Log("没有需要下载的资源");
                return true;
            }

            hotUpdateDownloads.AddRange(downloadList);
            if (downloader == null || !downloader.isActiveAndEnabled) return false;
            downloader.AddBatchDownloads(downloadList);
            float batchTimeout = config.downloadBatchTimeout;
            float start = Time.realtimeSinceStartup;
            while (!AllItemsTerminal(downloadList))
            {
                if (destroyed || downloader == null || (batchTimeout > 0f
                    && Time.realtimeSinceStartup - start >= batchTimeout))
                {
                    if (downloader != null)
                        foreach (var item in downloadList) downloader.CancelDownload(item);
                    PLogger.LogError("资源下载批次已取消或超时，保留旧版本。");
                    return false;
                }
                await PlatformTasks.Delay(TimeSpan.FromMilliseconds(50));
            }
            bool success = AllItemsCompleted(downloadList);
            if (!success) LogDownloadFailures(downloadList);
            return success;
        }

        private static bool AllItemsTerminal(List<DownloadItem> items)
        {
            foreach (var item in items)
                if (item != null && item.state != DownloadState.Completed
                    && item.state != DownloadState.Failed && item.state != DownloadState.Canceled) return false;
            return true;
        }

        /// <summary>本批次的任务是否全部成功（下载器完成后会把 state 置为 Completed）</summary>
        private static bool AllItemsCompleted(List<DownloadItem> downloadList)
        {
            for (int i = 0; i < downloadList.Count; i++)
            {
                DownloadItem item = downloadList[i];
                if (item == null)
                    continue;

                if (item.state != DownloadState.Completed)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 打印本批次未完成的资源（最多列出 10 个），便于定位是哪个资源导致的失败。
        /// 只统计本批次自己的任务，避免被其它批次的失败记录干扰。
        /// </summary>
        private static void LogDownloadFailures(List<DownloadItem> downloadList)
        {
            int failedCount = 0;
            int shown = 0;

            for (int i = 0; i < downloadList.Count; i++)
            {
                DownloadItem item = downloadList[i];
                if (item == null || item.state == DownloadState.Completed)
                    continue;

                failedCount++;
                if (shown < 10)
                {
                    PLogger.LogError($"下载未完成：{item.fileName}（{item.url}）状态：{item.state}，错误：{item.error}");
                    shown++;
                }
            }

            PLogger.Log_red($"资源下载未全部成功：共 {downloadList.Count} 个任务，未完成 {failedCount} 个");
        }

        /// <summary>
        /// 拼接远程 URL，避免 Path.Combine 在跨平台下产生错误的路径分隔符
        /// </summary>
        private static string CombineUrl(params string[] segments)
        {
            return ABLoadMgr.CombineUrl(segments);
        }

    }
}
