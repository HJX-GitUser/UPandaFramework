using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace UPandaGF
{
    /// <summary>主线程 AB 加载服务。所有入口共享操作，清理先结束等待者，再收尾原生操作。
    /// <para>原生加载用 <see cref="AsyncOperation.completed"/> 完成回调驱动，不再用 <c>Task.Yield</c> 逐帧轮询；
    /// 进度由本组件的 <see cref="Update"/> 统一推进，只在有在途加载时产生开销。</para></summary>
    public class ABLoadMgr : LazyMonoSingletonBase<ABLoadMgr>
    {
        private sealed class BundleOperation
        {
            public string name, source;
            public ABLoadPath path;
            public int generation;
            public UnityWebRequest request;
            public readonly TaskCompletionSource<AssetBundle> result = new TaskCompletionSource<AssetBundle>();
            public readonly TaskCompletionSource<bool> settled = new TaskCompletionSource<bool>();
        }
        private sealed class AssetOperation
        {
            public readonly TaskCompletionSource<Object> result = new TaskCompletionSource<Object>();
            public readonly TaskCompletionSource<bool> settled = new TaskCompletionSource<bool>();
        }
        private AssetBundle mainAB;
        private AssetBundleManifest manifest;
        private readonly Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        private readonly Dictionary<string, string> loadedSources = new Dictionary<string, string>();
        private readonly Dictionary<string, BundleOperation> operations = new Dictionary<string, BundleOperation>();
        private readonly Dictionary<string, Task> retiring = new Dictionary<string, Task>();
        private readonly HashSet<AssetOperation> assetOperations = new HashSet<AssetOperation>();
        private ABSourcesRelated sourceRef;
        private readonly Dictionary<string, int> leasePins = new Dictionary<string, int>();
        private readonly HashSet<string> managedBundles = new HashSet<string>();
        /// <summary>被旧接口（GetAssetBundle / LoadResAsync）占用的包。它们也计入 <see cref="leasePins"/>，
        /// 由 <see cref="ReleaseLegacyBundles"/> 统一释放，不再维护第二套“永不卸载”的集合。</summary>
        private readonly HashSet<string> legacyPinned = new HashSet<string>();

        /// <summary>在途 AsyncOperation → 进度回调；只在有加载时非空。</summary>
        private readonly Dictionary<AsyncOperation, Action<float>> progressPumps = new Dictionary<AsyncOperation, Action<float>>();
        private readonly List<AsyncOperation> progressBuffer = new List<AsyncOperation>();

        public int GetLeaseCount(string name) => leasePins.TryGetValue(name, out var count) ? count : 0;

        /// <summary>该包是否被旧接口占用（需要 <see cref="ReleaseLegacyBundles"/> 才会释放）。</summary>
        public bool IsLegacyPinned(string name) => !string.IsNullOrEmpty(name) && legacyPinned.Contains(name);

        public AssetBundleDiagnostics GetDiagnostics()
        {
            int pins = 0, retiringCount = 0;
            foreach (var count in leasePins.Values) pins += count;
            foreach (var task in retiring.Values) if (!task.IsCompleted) retiringCount++;
            return new AssetBundleDiagnostics(_loadedBundles.Count, operations.Count, assetOperations.Count, retiringCount, pins, legacyPinned.Count);
        }
        private int generation;
        private bool destroyed;
        public bool IsInitialized => !destroyed && mainAB != null && manifest != null;
        [HideInInspector] public string PathUrl;
        [HideInInspector] public string MainName;
        public ABLoadPath MainPackageLoadPath = ABLoadPath.StreamingAssetsPath;
        public string remoteURL = "http://127.0.0.1:8090/";
        [Min(0)] public int requestTimeout = 30;

        /// <summary>每帧推进在途加载的进度事件；没有在途操作时立即返回。</summary>
        private void Update()
        {
            if (progressPumps.Count == 0) return;
            progressBuffer.Clear();
            foreach (var pair in progressPumps) progressBuffer.Add(pair.Key);
            for (int i = 0; i < progressBuffer.Count; i++)
            {
                AsyncOperation operation = progressBuffer[i];
                Action<float> callback;
                if (operation == null || !progressPumps.TryGetValue(operation, out callback)) continue;
                try { callback(operation.progress); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        public async Task Init(string pathUrl, string mainName, ABLoadPath mainLoadPath)
        {
            if (destroyed) return;
            ClearAB();
            int epoch = generation;
            PathUrl = pathUrl; MainName = mainName; MainPackageLoadPath = mainLoadPath;
            var bundle = await LoadBundle(mainName, mainLoadPath);
            if (!Current(epoch) || bundle == null) return;
            try
            {
                var loadedManifest = bundle.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                if (!Current(epoch)) return;
                if (loadedManifest == null) { Debug.LogError("主包缺少 AssetBundleManifest：" + mainName); return; }
                mainAB = bundle; manifest = loadedManifest;
            }
            catch (Exception e) { Debug.LogException(e); }
        }
        public void SetABSourcesRelated(ABSourcesRelated arg) { sourceRef = arg; }
        private bool Current(int epoch) { return !destroyed && epoch == generation; }
        private string GetFullPath(ABLoadPath path, string name)
        {
            switch (path)
            {
                case ABLoadPath.PersistentDataPath: return CombineLocalPath(Application.persistentDataPath, PathUrl, name);
                case ABLoadPath.RemotePath: return CombineUrl(remoteURL, PathUrl, name);
                default:
                    return AssetLocation.AsUrl(AssetLocation.Combine(Application.streamingAssetsPath, PathUrl, name));
            }
        }
        public static string CombineUrl(params string[] segments)
        {
            if (segments == null || segments.Length == 0) return string.Empty;
            string result = segments[0] ?? string.Empty;
            for (int i = 1; i < segments.Length; i++)
                result = AssetLocation.Combine(result, segments[i]);
            return result;
        }
        public static string CombineLocalPath(params string[] segments)
        {
            if (segments == null || segments.Length == 0) return string.Empty;
            string result = segments[0] ?? string.Empty;
            for (int i = 1; i < segments.Length; i++)
                result = AssetLocation.Combine(result, segments[i]);
            return result;
        }
        private Task<AssetBundle> LoadBundle(string name, ABLoadPath path)
        {
            if (destroyed || string.IsNullOrEmpty(name)) return Task.FromResult<AssetBundle>(null);
            string source = GetFullPath(path, name);
            AssetBundle cached;
            if (_loadedBundles.TryGetValue(name, out cached) && cached != null)
            {
                if (loadedSources[name] == source) return Task.FromResult(cached);
                Debug.LogWarning("同名 AB 已从其它来源加载，请先清理或重新初始化：" + name);
                return Task.FromResult<AssetBundle>(null);
            }
            BundleOperation pending;
            if (operations.TryGetValue(name, out pending))
                return pending.source == source ? pending.result.Task : Task.FromResult<AssetBundle>(null);
            var operation = new BundleOperation { name = name, source = source, path = path, generation = generation };
            Task preceding;
            retiring.TryGetValue(name, out preceding);
            if (preceding != null && preceding.IsCompleted) { retiring.Remove(name); preceding = null; }
            operations.Add(name, operation);
            _ = RunBundle(operation, preceding);
            return operation.result.Task;
        }
        private async Task RunBundle(BundleOperation operation, Task preceding)
        {
            AssetBundle bundle = null;
            IDisposable fileLease = null;
            try
            {
                // 已取消的文件加载不能中途停止；同名新请求先等它完成清理，避免原生重复加载。
                if (preceding != null) await preceding;
                if (!Current(operation.generation)) return;
                fileLease = await HotUpdateFileGate.EnterLoad();
                if (!Current(operation.generation)) return;
                if (operation.path == ABLoadPath.PersistentDataPath)
                {
                    var native = AssetBundle.LoadFromFileAsync(operation.source);
                    if (native == null) return;
                    await WaitOperation(native, value => Progress(operation, value));
                    bundle = native.assetBundle;
                }
                else
                {
                    using (var request = UnityWebRequestAssetBundle.GetAssetBundle(operation.source))
                    {
                        operation.request = request;
                        request.timeout = Math.Max(0, requestTimeout);
                        var webOperation = request.SendWebRequest();
                        await WaitOperation(webOperation, value => Progress(operation, value));
                        if (request.result == UnityWebRequest.Result.Success) bundle = DownloadHandlerAssetBundle.GetContent(request);
                        else if (Current(operation.generation)) Debug.LogError("AssetBundle 加载失败：" + operation.source + "\n" + request.error);
                        operation.request = null;
                    }
                }
                if (!Current(operation.generation)) { if (bundle != null) bundle.Unload(false); bundle = null; return; }
                if (bundle == null) { Debug.LogError("AssetBundle 加载失败：" + operation.source); return; }
                _loadedBundles[operation.name] = bundle;
                loadedSources[operation.name] = operation.source;
                Progress(operation, 1, true);
                operation.result.TrySetResult(Current(operation.generation) ? bundle : null);
            }
            catch (Exception e) { if (Current(operation.generation)) Debug.LogException(e); }
            finally
            {
                operation.request = null;
                fileLease?.Dispose();
                BundleOperation current;
                if (operations.TryGetValue(operation.name, out current) && ReferenceEquals(current, operation)) operations.Remove(operation.name);
                // 原生收尾完成后才允许下一代同名加载启动。
                operation.settled.TrySetResult(true);
                operation.result.TrySetResult(null);
                CollectLeaseBundles();
            }
        }
        private void Progress(BundleOperation operation, float value, bool complete = false)
        {
            if (Current(operation.generation)) EventCenter.Instance.EventTrigger(new ABLoadProgressEvent(operation.name, operation.path, value, complete));
        }
        /// <summary>旧接口：加载并“占用”包（计入租约计数），需要 <see cref="ReleaseLegacyBundles"/> 或 <see cref="UnLoadAB"/> 之前手动释放占用。</summary>
        public Task<AssetBundle> GetAssetBundle(string abName, ABLoadPath loodPath)
        {
            PinLegacy(abName);
            return GetAssetBundleCore(abName, loodPath);
        }
        private async Task<AssetBundle> GetAssetBundleCore(string abName, ABLoadPath loodPath)
        {
            int epoch = generation;
            var dependencies = manifest;
            if (string.IsNullOrEmpty(abName)) return null;
            if (!IsInitialized) { if (!destroyed) PLogger.LogError("主包未加载"); return null; }
            foreach (var dependency in dependencies.GetAllDependencies(abName))
            {
                ABLoadPath path = loodPath;
                if (sourceRef != null)
                {
                    AssetBundleLoadInfo info;
                    if (sourceRef.bundleInfo == null || !sourceRef.bundleInfo.TryGetValue(dependency, out info) || info == null)
                    { Debug.LogError("清单缺少依赖包配置：" + dependency); return null; }
                    path = info.loadPath;
                }
                if (await LoadBundle(dependency, path) == null || !Current(epoch)) return null;
            }
            if (!Current(epoch)) return null;
            var bundle = await LoadBundle(abName, loodPath);
            return Current(epoch) ? bundle : null;
        }
        public void GetAssetBundle(string abName, ABLoadPath loodPath, UnityAction<AssetBundle> callback)
        { _ = Deliver(GetAssetBundle(abName, loodPath), callback); }
        public async Task<T> LoadResAsync<T>(string abName, string resName, ABLoadPath loodPath) where T : Object
        { return await LoadResAsync(abName, resName, typeof(T), loodPath) as T; }
        public async Task<Object> LoadResAsync(string abName, string resName, Type type, ABLoadPath loodPath)
        {
            int epoch = generation;
            var bundle = await GetAssetBundle(abName, loodPath);
            if (!Current(epoch) || bundle == null) return null;
            var operation = new AssetOperation();
            assetOperations.Add(operation);
            _ = RunAsset(operation, bundle, resName, type, epoch);
            return await operation.result.Task;
        }
        private async Task RunAsset(AssetOperation operation, AssetBundle bundle, string name, Type type, int epoch)
        {
            try
            {
                var request = bundle.LoadAssetAsync(name, type);
                if (request == null) return;
                await WaitOperation(request, null);
                operation.result.TrySetResult(Current(epoch) ? request.asset : null);
            }
            catch (Exception e) { if (Current(epoch)) Debug.LogException(e); }
            finally { assetOperations.Remove(operation); operation.settled.TrySetResult(true); operation.result.TrySetResult(null); CollectLeaseBundles(); }
        }
        public void LoadResAsync<T>(string abName, string resName, ABLoadPath loodPath, UnityAction<T> callBack) where T : Object
        { _ = Deliver(LoadResAsync<T>(abName, resName, loodPath), callBack); }
        public void LoadResAsync(string abName, string resName, Type type, ABLoadPath loodPath, UnityAction<Object> callBack)
        { _ = Deliver(LoadResAsync(abName, resName, type, loodPath), callBack); }
        public void LoadResAsync(string abName, string resName, ABLoadPath loodPath, UnityAction<Object> callBack)
        { LoadResAsync(abName, resName, typeof(Object), loodPath, callBack); }
        private static async Task Deliver<T>(Task<T> task, UnityAction<T> callback) where T : Object
        {
            T result = null;
            try { result = await task; } catch (Exception e) { Debug.LogException(e); }
            ResourcesLoader.InvokeCallbacks(callback, result);
        }
        public async Task<AssetLease<T>> AcquireAsync<T>(string abName, string resName, ABLoadPath path) where T : Object
        {
            var bundleLease = await AcquireBundleAsync(abName, path);
            if (bundleLease == null) return null;
            int epoch = generation;
            try
            {
                var operation = new AssetOperation();
                assetOperations.Add(operation);
                _ = RunAsset(operation, bundleLease.Asset, resName, typeof(T), epoch);
                var asset = await operation.result.Task as T;
                if (!Current(epoch) || asset == null) return null;
                var owned = bundleLease;
                bundleLease = null;
                return new AssetLease<T>(asset, owned.Dispose);
            }
            finally { bundleLease?.Dispose(); }
        }
        public async Task<AssetLease<AssetBundle>> AcquireBundleAsync(string name, ABLoadPath path)
        {
            if (!IsInitialized || string.IsNullOrEmpty(name)) return null;
            int epoch = generation;
            var names = new List<string>(manifest.GetAllDependencies(name));
            if (!names.Contains(name)) names.Add(name);
            foreach (var item in names) { leasePins[item] = GetLeaseCount(item) + 1; managedBundles.Add(item); }
            bool transferred = false;
            try
            {
                var bundle = await GetAssetBundleCore(name, path);
                if (!Current(epoch) || bundle == null) return null;
                transferred = true;
                return new AssetLease<AssetBundle>(bundle, () => ReleasePins(names, epoch));
            }
            finally { if (!transferred) ReleasePins(names, epoch); }
        }
        private void ReleasePins(List<string> names, int epoch)
        {
            if (!Current(epoch)) return;
            foreach (string name in names)
            {
                int count = GetLeaseCount(name);
                if (count <= 1) leasePins.Remove(name); else leasePins[name] = count - 1;
            }
            CollectLeaseBundles();
        }
        /// <summary>
        /// 把旧接口（GetAssetBundle / LoadResAsync）占用的包挂到租约计数上。
        /// <para>同一个包只 pin 一次；重复调用不会累加计数，因此不会被旧接口永久占住。</para>
        /// </summary>
        private void PinLegacy(string abName)
        {
            if (string.IsNullOrEmpty(abName)) return;
            PinLegacyName(abName);
            if (manifest != null)
                foreach (string dependency in manifest.GetAllDependencies(abName)) PinLegacyName(dependency);
        }
        private void PinLegacyName(string name)
        {
            if (string.IsNullOrEmpty(name) || !legacyPinned.Add(name)) return;
            leasePins[name] = GetLeaseCount(name) + 1;
            managedBundles.Add(name);
        }
        /// <summary>释放所有旧接口占用的包；返回释放的包数量。未释放的包会继续留在内存里。</summary>
        public int ReleaseLegacyBundles()
        {
            if (legacyPinned.Count == 0) return 0;
            int released = legacyPinned.Count;
            foreach (string name in legacyPinned)
            {
                int count = GetLeaseCount(name);
                if (count <= 1) leasePins.Remove(name); else leasePins[name] = count - 1;
            }
            legacyPinned.Clear();
            CollectLeaseBundles();
            return released;
        }
        private void CollectLeaseBundles()
        {
            if (assetOperations.Count > 0 || operations.Count > 0) return;
            foreach (string name in new List<string>(managedBundles))
                if (GetLeaseCount(name) == 0 && name != MainName)
                { UnLoadAB(name); managedBundles.Remove(name); }
        }
        public bool UnLoadAB(string name)
        {
            if (GetLeaseCount(name) > 0 || operations.ContainsKey(name) || assetOperations.Count > 0) return false;
            AssetBundle bundle;
            if (_loadedBundles.TryGetValue(name, out bundle))
            {
                _loadedBundles.Remove(name); loadedSources.Remove(name);
                if (name == MainName) { mainAB = null; manifest = null; }
                if (bundle != null) bundle.Unload(false);
            }
            return true;
        }
        public void ClearAB()
        {
            generation++;
            leasePins.Clear(); managedBundles.Clear(); legacyPinned.Clear();
            progressPumps.Clear(); progressBuffer.Clear();
            var completed = new List<string>();
            foreach (var pair in retiring) if (pair.Value.IsCompleted) completed.Add(pair.Key);
            foreach (var name in completed) retiring.Remove(name);
            var bundles = new Dictionary<string, AssetBundle>(_loadedBundles);
            var pending = new List<BundleOperation>(operations.Values);
            var assets = new List<AssetOperation>(assetOperations);
            _loadedBundles.Clear(); loadedSources.Clear(); operations.Clear(); assetOperations.Clear();
            mainAB = null; manifest = null; sourceRef = null;
            foreach (var operation in pending) retiring[operation.name] = operation.settled.Task;
            var nativeAssets = new List<Task>();
            foreach (var operation in assets) nativeAssets.Add(operation.settled.Task);
            Task retired = Retire(bundles, nativeAssets);
            foreach (var pair in bundles) retiring[pair.Key] = retired;
            // 所有内部状态先提交；等待者续体可重入 Init/Clear。
            foreach (var operation in pending)
            {
                if (operation.request != null) operation.request.Abort();
                operation.result.TrySetResult(null);
                CollectLeaseBundles();
            }
            foreach (var operation in assets) operation.result.TrySetResult(null);
        }
        private static async Task Retire(Dictionary<string, AssetBundle> bundles, List<Task> assets)
        {
            if (assets.Count > 0) await Task.WhenAll(assets);
            foreach (var bundle in bundles.Values)
                if (bundle != null) bundle.Unload(false);
        }

        /// <summary>
        /// 等待 AsyncOperation 完成（completed 回调驱动），不逐帧 Task.Yield。
        /// <para>进度回调注册到 <see cref="progressPumps"/>，由 Update 统一采样；完成时自动摘除。</para>
        /// </summary>
        private Task WaitOperation(AsyncOperation operation, Action<float> onProgress)
        {
            if (operation == null) return Task.CompletedTask;
            if (operation.isDone)
            {
                onProgress?.Invoke(1f);
                return Task.CompletedTask;
            }
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (onProgress != null) progressPumps[operation] = onProgress;
            operation.completed += _ =>
            {
                progressPumps.Remove(operation);
                if (onProgress != null)
                {
                    try { onProgress(1f); } catch (Exception e) { Debug.LogException(e); }
                }
                completion.TrySetResult(true);
            };
            return completion.Task;
        }

        protected override void OnDestroyed() { destroyed = true; ClearAB(); base.OnDestroyed(); }
    }
    public class ABLoadProgressEvent : EventArgBase
    {
        public string abName;
        public ABLoadPath loadPath;
        public float progress;
        public bool isDown;
        public ABLoadProgressEvent(string abName, ABLoadPath loadPath, float progress, bool isDown)
        { this.abName = abName; this.loadPath = loadPath; this.progress = progress; this.isDown = isDown; }
    }
}
