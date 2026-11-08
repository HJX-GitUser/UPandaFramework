using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace UPandaGF
{
    public sealed class HotUpdateBundle
    {
        public string Name { get; internal set; }
        public string StagedPath { get; internal set; }
        public string TargetPath { get; internal set; }
        public string Md5 { get; internal set; }
        public long Size { get; internal set; }
    }
    public sealed class HotUpdatePlan
    {
        public ABSourcesRelated Manifest { get; internal set; }
        public IReadOnlyList<HotUpdateBundle> Downloads { get; internal set; }
        public long AdditionalDiskBytes { get; internal set; }
        public long StagingBytes { get; internal set; }
        internal HotUpdateStorage owner;
        internal HotUpdateStorage.CacheState next;
        internal byte[] indexBytes;
        internal string manifestHash;
        internal HashSet<string> protectedFiles;
    }

    /// <summary>热更文件阶段：仅操作显式路径和数据，不调用 Unity API。一个会话持有进程/文件互斥。</summary>
    public sealed class HotUpdateStorage : IDisposable
    {
        [Serializable] internal sealed class OwnedFile { public string name, md5; public long size; }
        [Serializable] internal sealed class Snapshot { public string id; public List<string> references = new List<string>(); }
        [Serializable] internal sealed class CacheState
        {
            public string root;
            public List<OwnedFile> owned = new List<OwnedFile>();
            public List<Snapshot> history = new List<Snapshot>();
        }
        private const long MiB = 1024 * 1024;
        private const string OwnerPrefix = "UPANDA_HOTUPDATE_STAGE:1\n";
        private readonly object ioSync = new object(), closeSync = new object();
        private readonly string persistent, control, bundleRoot, indexPath, stageRoot, recovery;
        private readonly string rootId;
        private readonly HotUpdateSettings settings;
        private readonly Func<string, long?> availableSpace;
        private readonly bool platformSpace;
        private long? latestSpace;
        private readonly CancellationToken token;
        private IDisposable maintenance;
        private CancellationTokenRegistration cancellation;
        private FileStream lockFile, stageLease;
        private CacheState state;
        private bool closed, planned, ledgerTrusted = true;
        private Task closeTask;
        private readonly byte[] readBuffer = new byte[256 * 1024];
        private readonly List<string> warnings = new List<string>();
        public HotUpdateStatistics Statistics { get; } = new HotUpdateStatistics();
        public string ManifestPath { get; }
        public string StagePath { get; private set; }
        public string StagedManifestPath => Path.Combine(StagePath, AssetDataNames.assetData);
        public long ManifestDownloadLimit { get; private set; }
        public bool RecoveryPending => File.Exists(Path.Combine(recovery, "journal"));
        public IReadOnlyList<string> Warnings { get { lock (ioSync) return warnings.ToArray(); } }
        private static StringComparison Comparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private HotUpdateStorage(string persistentRoot, string relativeBundleRoot, HotUpdateSettings options,
            CancellationToken cancellationToken, Func<string, long?> spaceProbe, IDisposable maintenanceScope)
        {
            persistent = Path.GetFullPath(persistentRoot).TrimEnd(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relativeBundleRoot) || Path.IsPathRooted(relativeBundleRoot) || relativeBundleRoot.Contains(":")) throw new InvalidDataException("资源根路径必须是相对路径");
            bundleRoot = Path.GetFullPath(Path.Combine(persistent, relativeBundleRoot.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(bundleRoot, persistent)) throw new InvalidDataException("资源根路径越界");
            control = Path.Combine(persistent, "AssetBundles"); recovery = Path.Combine(control, ".update-recovery");
            rootId = HashBytes(Encoding.UTF8.GetBytes(Path.DirectorySeparatorChar == '\\' ? bundleRoot.ToUpperInvariant() : bundleRoot)).Substring(0, 32);
            indexPath = Path.Combine(control, ".update-state", rootId, "ledger.data");
            stageRoot = Path.Combine(control, ".update-stage", rootId);
            ManifestPath = Path.Combine(control, AssetDataNames.assetData);
            foreach (string reserved in new[] { recovery, Path.Combine(control, ".update-state"), Path.Combine(control, ".update-stage"), ManifestPath, Path.Combine(control, ".update.lock") })
                if (AtOrWithin(bundleRoot, reserved)) throw new InvalidDataException("资源根目录与更新控制区冲突");
            settings = (options ?? new HotUpdateSettings()).Snapshot(); token = cancellationToken;
            platformSpace = spaceProbe == null && PlatformStorage.RequiresMainThreadSpaceProbe;
            availableSpace = spaceProbe ?? DefaultAvailableSpace; maintenance = maintenanceScope;
        }
        public static async Task<HotUpdateStorage> OpenAsync(string persistentRoot, string relativeBundleRoot, HotUpdateSettings options,
            CancellationToken token = default, Func<string, long?> availableSpace = null, IDisposable maintenanceScope = null)
        {
            HotUpdateStorage store = null;
            try
            {
                store = new HotUpdateStorage(persistentRoot, relativeBundleRoot, options, token, availableSpace, maintenanceScope);
                await store.Work(() => { store.Open(); return true; }).ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads);
                store.cancellation = token.Register(() => { _ = store.DisposeAsync(); });
                return store;
            }
            catch
            {
                if (store != null) await store.DisposeAsync().ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); else maintenanceScope?.Dispose();
                throw;
            }
        }
        public static Task<bool> RecoveryRequiredAsync(string persistentRoot)
        { return Background(() => File.Exists(Path.Combine(Path.GetFullPath(persistentRoot), "AssetBundles", ".update-recovery", "journal"))); }
        public static Task<ABSourcesRelated> DecodeManifestAsync(byte[] bytes, AssetBundleClassificationWindowConfig aes = null)
        { return Background(() => AssetReferenceSerializer.Decode(bytes, aes)); }
        private static Task<T> Background<T>(Func<T> action)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Inline(action);
#else
            return Task.Run(action);
#endif
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        private static async Task<T> Inline<T>(Func<T> action) { await Task.Yield(); return action(); }
#endif
        private Task<T> Work<T>(Func<T> action)
        {
            return Background(() => { lock (ioSync) { token.ThrowIfCancellationRequested(); if (closed) throw new ObjectDisposedException(nameof(HotUpdateStorage)); Statistics.ioThreadId = Thread.CurrentThread.ManagedThreadId; return action(); } });
        }
        private void Open()
        {
            var clock = Stopwatch.StartNew(); Check(control); Check(bundleRoot); Directory.CreateDirectory(control);
            string mutex = Path.Combine(control, ".update.lock"); Check(mutex);
            lockFile = new FileStream(mutex, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Check(recovery);
            if (Directory.Exists(recovery)) foreach (string file in Directory.GetFiles(recovery)) Check(file);
            FileUpdateTransaction.RecoverCancelable(recovery, token, ValidateTarget);
            LoadState(); CleanupStages();
            Statistics.prepareMilliseconds += clock.ElapsedMilliseconds;
        }
        public async Task BeginStageAsync()
        { await RefreshSpace().ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); await BeginStageCoreAsync().ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); }
        private Task BeginStageCoreAsync()
        {
            return Work(() =>
            {
                if (StagePath != null) throw new InvalidOperationException("每个热更会话只能创建一次暂存");
                Check(stageRoot); Directory.CreateDirectory(stageRoot);
                StagePath = Path.Combine(stageRoot, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(StagePath);
                stageLease = new FileStream(Path.Combine(StagePath, ".owner"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                byte[] marker = Encoding.UTF8.GetBytes(OwnerPrefix + rootId); stageLease.Write(marker, 0, marker.Length); stageLease.Flush(true);
                long cap = VersionedDataSerializer.MaxFileBytes;
                if (settings.maximumStagingMB > 0) cap = Math.Min(cap, settings.maximumStagingMB * MiB);
                long? free = Free();
                if (settings.minimumFreeSpaceMB > 0)
                {
                    if (!free.HasValue) throw new IOException("无法查询可用磁盘空间；本批次未下载");
                    cap = Math.Min(cap, free.Value - settings.minimumFreeSpaceMB * MiB);
                }
                if (cap <= 0) throw new IOException("磁盘余量不足，无法暂存清单");
                ManifestDownloadLimit = cap; return true;
            });
        }
        public Task<ABSourcesRelated> ReadLocalAsync(AssetBundleClassificationWindowConfig aes, bool verifyFiles = false)
        {
            return Work(() =>
            {
                Check(ManifestPath); if (!File.Exists(ManifestPath)) return null;
                var local = AssetReferenceSerializer.Decode(VersionedDataSerializer.ReadFile(ManifestPath), aes);
                if (verifyFiles) foreach (var info in Persistent(local)) if (!Verify(Target(info.bundleName), info.size, info.md5)) return null;
                return local;
            });
        }
        public async Task<HotUpdatePlan> PrepareAsync(AssetBundleClassificationWindowConfig aes, ABSourcesRelated bundled)
        { await RefreshSpace().ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); return await PrepareCoreAsync(aes, bundled).ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); }
        private Task<HotUpdatePlan> PrepareCoreAsync(AssetBundleClassificationWindowConfig aes, ABSourcesRelated bundled)
        {
            return Work(() =>
            {
                if (StagePath == null || planned) throw new InvalidOperationException("请先创建暂存；每个会话只能生成一个提交计划");
                var clock = Stopwatch.StartNew(); Check(StagedManifestPath);
                byte[] bytes = VersionedDataSerializer.ReadFile(StagedManifestPath);
                var remote = AssetReferenceSerializer.Decode(bytes, aes);
                var next = Clone(state); var ownedFiles = next.owned.ToDictionary(item => item.name, StringComparer.OrdinalIgnoreCase); var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var changes = new List<HotUpdateBundle>(); var current = Persistent(remote).ToList();
                foreach (var info in current)
                {
                    token.ThrowIfCancellationRequested(); string name = Relative(info.bundleName), target = Target(name);
                    if (info.size <= 0 || string.IsNullOrEmpty(info.md5)) throw new InvalidDataException("热更包必须提供正数大小和 MD5：" + name);
                    if (!names.Add(name)) throw new InvalidDataException("重复热更包：" + name);
                    bool valid = Verify(target, info.size, info.md5);
                    OwnedFile owned; ownedFiles.TryGetValue(name, out owned);
                    if (!valid)
                    {
                        changes.Add(new HotUpdateBundle { Name = name, TargetPath = target, StagedPath = Path.Combine(StagePath, changes.Count + ".bundle"), Size = info.size, Md5 = info.md5 });
                        if (owned == null) { owned = new OwnedFile { name = name }; next.owned.Add(owned); ownedFiles.Add(name, owned); }
                    }
                    if (owned != null) { owned.size = info.size; owned.md5 = info.md5; }
                }
                // 不能同时把 a 和 a/b 当作文件；也不能把包写入元数据控制区。
                foreach (string name in names) { string parent = name; while (parent.Contains("/")) { parent = parent.Substring(0, parent.LastIndexOf('/')); if (names.Contains(parent)) throw new InvalidDataException("资源文件/目录路径冲突"); } }
                string id = HashBytes(bytes);
                next.history.RemoveAll(item => item.id == id); next.history.Insert(0, new Snapshot { id = id, references = names.ToList() });
                if (next.history.Count > settings.retainedManifestCount) next.history.RemoveRange(settings.retainedManifestCount, next.history.Count - settings.retainedManifestCount);
                var protectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var snapshot in next.history) protectedFiles.UnionWith(snapshot.references);
                bool canCollect = settings.collectUnusedBundles && ledgerTrusted && bundled != null;
                if (bundled != null) { AssetReferenceValidation.Validate(bundled); foreach (var info in Persistent(bundled)) protectedFiles.Add(Relative(info.bundleName)); }
                Statistics.garbageCollectionSkipped = !canCollect;
                var preCommitProtection = new HashSet<string>(protectedFiles, StringComparer.OrdinalIgnoreCase);
                // 提交前保留磁盘上的当前清单引用，保证任何失败仍可回退。
                try { if (File.Exists(ManifestPath)) foreach (var info in Persistent(AssetReferenceSerializer.Decode(VersionedDataSerializer.ReadFile(ManifestPath), aes))) preCommitProtection.Add(Relative(info.bundleName)); }
                catch (Exception) { canCollect = false; Statistics.garbageCollectionSkipped = true; }
                if (canCollect) Collect(next, preCommitProtection, changes.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
                byte[] index = VersionedDataSerializer.Serialize(next, "hotupdate-cache");
                long stage = checked(bytes.LongLength + index.LongLength + changes.Sum(item => item.Size));
                var stagedSizes = changes.Select(item => Tuple.Create(item.TargetPath, item.Size)).ToList();
                stagedSizes.Add(Tuple.Create(indexPath, index.LongLength)); stagedSizes.Add(Tuple.Create(ManifestPath, bytes.LongLength));
                long extra = checked(changes.Sum(item => item.Size) + index.LongLength + BackupAndGrowth(stagedSizes) + MiB + stagedSizes.Count * 8192L);
                long managed = Projected(next, changes);
                if (settings.maximumManagedCacheMB > 0 && managed > settings.maximumManagedCacheMB * MiB) throw new IOException("登记缓存预算不足；受保护文件不会被强制删除");
                if (settings.maximumStagingMB > 0 && stage > settings.maximumStagingMB * MiB) throw new IOException("本批次暂存预算不足");
                RequireSpace(extra);
                Statistics.estimatedAdditionalBytes = extra; Statistics.stagedBytes = stage; Statistics.managedCacheBytes = managed;
                Statistics.downloadedBundles = changes.Count; Statistics.verifyMilliseconds += clock.ElapsedMilliseconds;
                planned = true;
                return new HotUpdatePlan { owner = this, Manifest = remote, Downloads = changes.AsReadOnly(), next = next, indexBytes = index,
                    manifestHash = id, protectedFiles = protectedFiles, AdditionalDiskBytes = extra, StagingBytes = stage };
            });
        }
        public async Task CommitAsync(HotUpdatePlan plan)
        {
            await RefreshSpace().ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); await CommitCoreAsync(plan).ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads);
            try { await PlatformStorage.FlushAsync().ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); }
            catch (Exception e) { lock (ioSync) warnings.Add("文件已提交，但持久化未完成：" + e.Message); throw; }
        }
        private Task CommitCoreAsync(HotUpdatePlan plan)
        {
            return Work(() =>
            {
                if (Statistics.committed) throw new InvalidOperationException("当前会话已提交");
                if (plan == null || plan.owner != this) throw new ArgumentException("热更计划不属于此会话");
                var verify = Stopwatch.StartNew();
                foreach (var item in plan.Downloads) if (!Verify(item.StagedPath, item.Size, item.Md5)) throw new InvalidDataException("下载文件大小或 MD5 不匹配：" + item.Name);
                if (HashBytes(VersionedDataSerializer.ReadFile(StagedManifestPath)) != plan.manifestHash) throw new InvalidDataException("暂存清单已变化");
                Statistics.verifyMilliseconds += verify.ElapsedMilliseconds;
                string indexStage = Path.Combine(StagePath, "ledger.data"); Check(indexStage); File.WriteAllBytes(indexStage, plan.indexBytes);
                var sources = plan.Downloads.Select(item => item.StagedPath).ToList(); var targets = plan.Downloads.Select(item => item.TargetPath).ToList();
                sources.Add(indexStage); targets.Add(indexPath); sources.Add(StagedManifestPath); targets.Add(ManifestPath);
                var sizes = sources.Select((path, i) => Tuple.Create(targets[i], new FileInfo(path).Length)).ToList();
                RequireSpace(checked(BackupAndGrowth(sizes) + MiB + targets.Count * 8192L));
                var commit = Stopwatch.StartNew();
                FileUpdateTransaction.CommitCancelable(sources, targets, recovery, token, ValidateTarget, count => Statistics.copiedBytes += count);
                Statistics.commitMilliseconds += commit.ElapsedMilliseconds; Statistics.committed = true; state = plan.next;
                // 回收属于提交后的维护，不让其失败把已提交的新清单误报为回滚。
                try
                {
                    var cleanup = Stopwatch.StartNew();
                    int previousCount = state.owned.Count;
                    if (!Statistics.garbageCollectionSkipped) Collect(state, plan.protectedFiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    if (state.owned.Count != previousCount)
                    {
                        Check(indexStage); File.WriteAllBytes(indexStage, VersionedDataSerializer.Serialize(state, "hotupdate-cache"));
                        FileUpdateTransaction.CommitCancelable(new[] { indexStage }, new[] { indexPath }, recovery, token, ValidateTarget);
                    }
                    Statistics.cleanupMilliseconds += cleanup.ElapsedMilliseconds;
                }
                catch (Exception e) { warnings.Add("新版本已提交，缓存维护未完成：" + e.Message); }
                return true;
            });
        }
        private static IEnumerable<AssetBundleLoadInfo> Persistent(ABSourcesRelated data)
        {
            if (data == null) yield break;
            foreach (var item in data.bundleInfo.Values) if (item.loadPath == ABLoadPath.PersistentDataPath) yield return item;
            if (data.mainBundleInfo.loadPath == ABLoadPath.PersistentDataPath) yield return data.mainBundleInfo;
        }
        private void Collect(CacheState index, HashSet<string> protectedFiles, HashSet<string> pendingWrites)
        {
            foreach (var owned in index.owned.ToArray())
            {
                token.ThrowIfCancellationRequested(); if (protectedFiles.Contains(owned.name) || pendingWrites.Contains(owned.name)) continue;
                string path = Target(owned.name);
                if (!File.Exists(path)) { index.owned.Remove(owned); continue; }
                if (!Verify(path, owned.size, owned.md5)) { index.owned.Remove(owned); warnings.Add("未回收已被外部修改的登记文件：" + owned.name); continue; }
                Check(path); long bytes = new FileInfo(path).Length;
                try { File.Delete(path); index.owned.Remove(owned); Statistics.reclaimedBytes += bytes; Statistics.removedBundles++; }
                catch (IOException e) { warnings.Add("文件仍被占用，保留缓存：" + e.Message); }
                catch (UnauthorizedAccessException e) { warnings.Add("缓存暂时无法回收：" + e.Message); }
            }
        }
        private long Projected(CacheState index, IReadOnlyList<HotUpdateBundle> changes)
        {
            long total = 0;
            var replacements = changes.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var owned in index.owned)
            {
                HotUpdateBundle replacement; replacements.TryGetValue(owned.name, out replacement);
                string path = Target(owned.name);
                total = checked(total + (replacement != null ? replacement.Size : File.Exists(path) ? new FileInfo(path).Length : 0));
            }
            return total;
        }
        private long BackupAndGrowth(IEnumerable<Tuple<string, long>> files)
        {
            long total = 0;
            foreach (var file in files)
            { Check(file.Item1); long old = File.Exists(file.Item1) ? new FileInfo(file.Item1).Length : 0; total = checked(total + old + Math.Max(0, file.Item2 - old)); }
            return total;
        }
        private bool Verify(string path, long size, string expected)
        {
            Check(path); if (!File.Exists(path) || size <= 0 || new FileInfo(path).Length != size || string.IsNullOrEmpty(expected)) return false;
            using (var stream = File.OpenRead(path))
            using (var hash = MD5.Create())
            {
                byte[] buffer = readBuffer; int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0) { token.ThrowIfCancellationRequested(); hash.TransformBlock(buffer, 0, count, buffer, 0); Statistics.verifiedBytes += count; }
                hash.TransformFinalBlock(new byte[0], 0, 0);
                return Hex(hash.Hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
            }
        }
        private void RequireSpace(long additional)
        {
            if (settings.minimumFreeSpaceMB <= 0) return;
            long? free = Free(); if (!free.HasValue || free.Value < checked(additional + settings.minimumFreeSpaceMB * MiB)) throw new IOException("磁盘可用空间不足或无法查询，未开始提交");
        }
        private async Task RefreshSpace()
        { if (platformSpace && settings.minimumFreeSpaceMB > 0) latestSpace = await PlatformStorage.AvailableSpaceAsync(persistent).ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); }
        private long? Free() { try { return platformSpace ? latestSpace : availableSpace(persistent); } catch (Exception) { return null; } }
        private static long? DefaultAvailableSpace(string root) { return PlatformStorage.AvailableDiskSpace(root); }
        private void LoadState()
        {
            state = new CacheState { root = bundleRoot }; Check(indexPath); if (!File.Exists(indexPath)) return;
            try
            {
                bool legacy; var parsed = VersionedDataSerializer.Deserialize<CacheState>(VersionedDataSerializer.ReadFile(indexPath), "hotupdate-cache", out legacy);
                if (legacy || parsed.root != bundleRoot || parsed.owned == null || parsed.history == null) throw new InvalidDataException("缓存登记结构无效");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in parsed.owned) if (item == null || item.size <= 0 || string.IsNullOrEmpty(item.md5) || Relative(item.name) != item.name || !seen.Add(item.name)) throw new InvalidDataException("缓存登记条目无效");
                foreach (var snapshot in parsed.history) { if (snapshot == null || snapshot.references == null) throw new InvalidDataException("缓存历史无效"); foreach (string name in snapshot.references) Target(name); }
                state = parsed;
            }
            catch (Exception e) { ledgerTrusted = false; warnings.Add("缓存登记不可用，本轮不回收旧包：" + e.Message); }
        }
        private static CacheState Clone(CacheState value)
        {
            return new CacheState { root = value.root, owned = value.owned.Select(item => new OwnedFile { name = item.name, md5 = item.md5, size = item.size }).ToList(),
                history = value.history.Select(item => new Snapshot { id = item.id, references = new List<string>(item.references) }).ToList() };
        }
        private string Target(string name)
        {
            name = Relative(name); string path = Path.GetFullPath(Path.Combine(bundleRoot, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(path, bundleRoot)) throw new InvalidDataException("资源路径越界");
            if (path.Equals(ManifestPath, Comparison) || AtOrWithin(path, Path.Combine(control, ".update-state")) || AtOrWithin(path, Path.Combine(control, ".update-stage"))
                || AtOrWithin(path, recovery) || path.Equals(Path.Combine(control, ".update.lock"), Comparison)) throw new InvalidDataException("资源路径与更新控制区冲突");
            Check(path); return path;
        }
        private static string Relative(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("资源名为空"); name = name.Replace('\\', '/');
            if (name.StartsWith("/", StringComparison.Ordinal) || name.Contains(":") || name.Split('/').Any(part => part.Length == 0 || part == "." || part == "..")) throw new InvalidDataException("非法资源名：" + name);
            foreach (string part in name.Split('/'))
            {
                if (part.TrimEnd(' ', '.') != part || part.Any(c => c < 32 || "<>\"|?*".IndexOf(c) >= 0)) throw new InvalidDataException("资源名包含非便携文件字符");
                string stem = part.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9')) throw new InvalidDataException("资源名是设备保留名称");
            }
            return name;
        }
        private void ValidateTarget(string path)
        {
            string full = Path.GetFullPath(path);
            if (full != path || (!Within(full, bundleRoot) && !full.Equals(ManifestPath, Comparison) && !full.Equals(indexPath, Comparison))) throw new InvalidDataException("恢复/提交目标不属于当前资源上下文");
            if (!full.Equals(ManifestPath, Comparison) && !full.Equals(indexPath, Comparison))
                Target(full.Substring(bundleRoot.TrimEnd(Path.DirectorySeparatorChar).Length + 1));
            Check(full);
        }
        private static bool AtOrWithin(string path, string root) { return path.Equals(root, Comparison) || Within(path, root); }
        private static bool Within(string path, string root) { return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, Comparison); }
        private void Check(string path)
        {
            string full = Path.GetFullPath(path);
            if (!full.Equals(persistent, Comparison) && !Within(full, persistent)) throw new InvalidDataException("更新文件越过 persistent 根目录");
            for (string current = full; current != null; current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("更新路径包含链接或重解析点");
                if (current.Equals(persistent, Comparison)) break;
            }
        }
        private void CleanupStages()
        {
            Check(stageRoot); if (!Directory.Exists(stageRoot)) return;
            foreach (string folder in Directory.GetDirectories(stageRoot)) { token.ThrowIfCancellationRequested(); TryDeleteStage(folder); }
        }
        private void TryDeleteStage(string folder)
        {
            if (!Within(Path.GetFullPath(folder), stageRoot)) return;
            string marker = Path.Combine(folder, ".owner");
            try
            {
                Check(folder); Check(marker); if (!File.Exists(marker)) return;
                using (var claim = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    if (claim.Length > 256) return;
                    var bytes = new byte[(int)claim.Length]; if (claim.Read(bytes, 0, bytes.Length) != bytes.Length || Encoding.UTF8.GetString(bytes) != OwnerPrefix + rootId) return;
                    DeleteContents(folder, marker);
                }
                File.Delete(marker); Directory.Delete(folder, false); Statistics.removedStages++;
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (InvalidDataException) { }
        }
        private void DeleteContents(string folder, string marker)
        {
            Check(folder);
            foreach (string file in Directory.GetFiles(folder)) { if (file == marker) continue; Check(file); File.Delete(file); }
            foreach (string child in Directory.GetDirectories(folder)) { Check(child); DeleteContents(child, marker); Directory.Delete(child, false); }
        }
        private static string HashBytes(byte[] bytes) { using (var hash = SHA256.Create()) return Hex(hash.ComputeHash(bytes)); }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
        public Task DisposeAsync()
        {
            lock (closeSync)
            {
                if (closeTask != null) return closeTask;
                return closeTask = CloseAndFlushAsync();
            }
        }
        private async Task CloseAndFlushAsync()
        {
            await Background(() =>
                {
                    lock (ioSync)
                    {
                        if (closed) return true; closed = true;
                        cancellation.Dispose();
                        try { stageLease?.Dispose(); stageLease = null; if (StagePath != null) TryDeleteStage(StagePath); }
                        finally { lockFile?.Dispose(); lockFile = null; maintenance?.Dispose(); maintenance = null; }
                        return true;
                    }
                }).ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads);
            try { await PlatformStorage.FlushAsync().ConfigureAwait(!PlatformStorage.SupportsBackgroundThreads); }
            catch (Exception e) { lock (ioSync) warnings.Add("暂存清理尚未持久化：" + e.Message); }
        }
        public void Dispose() { _ = DisposeAsync(); }
    }
}
