using System;
using UnityEngine;

namespace UPandaGF
{
    [Serializable]
    public class HotUpdateSettings
    {
        [Tooltip("设备至少保留的可用空间（MiB）；0 关闭设备余量探测")]
        public int minimumFreeSpaceMB = 64;
        [Tooltip("本批次暂存上限（MiB）；0 不限，清单仍限制 64 MiB")]
        public int maximumStagingMB;
        [Tooltip("登记缓存的保留上限（MiB）；0 不限，未登记文件不会自动删除")]
        public int maximumManagedCacheMB;
        [Tooltip("保留最近几份已提交清单的引用（含当前）；不是旧包内容的完整回滚版本")]
        public int retainedManifestCount = 2;
        [Tooltip("只回收本更新器登记且未被清单保护、内容未被外部修改的文件")]
        public bool collectUnusedBundles = true;
        public HotUpdateSettings Snapshot()
        {
            return new HotUpdateSettings { minimumFreeSpaceMB = Math.Max(0, minimumFreeSpaceMB), maximumStagingMB = Math.Max(0, maximumStagingMB),
                maximumManagedCacheMB = Math.Max(0, maximumManagedCacheMB), retainedManifestCount = Math.Max(1, Math.Min(10, retainedManifestCount)), collectUnusedBundles = collectUnusedBundles };
        }
    }
    [Serializable]
    public class HotUpdateStatistics
    {
        public long totalMilliseconds, manifestDownloadMilliseconds, bundleDownloadMilliseconds;
        public long prepareMilliseconds, verifyMilliseconds, commitMilliseconds, cleanupMilliseconds;
        public long verifiedBytes, copiedBytes, reclaimedBytes, estimatedAdditionalBytes, stagedBytes, managedCacheBytes;
        public int downloadedBundles, removedBundles, removedStages, ioThreadId;
        public bool committed, garbageCollectionSkipped;
    }
}
