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
    /// <summary>主线程 AB 加载服务。所有入口共享操作，清理先结束等待者，再收尾原生操作。</summary>
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
        private readonly HashSet<string> legacyBundles = new HashSet<string>();
        public int GetLeaseCount(string name) => leasePins.TryGetValue(name, out var count) ? count : 0;
        private int generation;
        private bool destroyed;
        public bool IsInitialized => !destroyed && mainAB != null && manifest != null;
        [HideInInspector] public string PathUrl;
        [HideInInspector] public string MainName;
        public ABLoadPath MainPackageLoadPath = ABLoadPath.StreamingAssetsPath;
        public string remoteURL = "http://127.0.0.1:8090/";
        [Min(0)] public int requestTimeout = 30;

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
            return AssetLocation.Combine(segments[0] ?? string.Empty, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(segments, 1)));
        }
        public static string CombineLocalPath(params string[] segments)
        {
            if (segments == null || segments.Length == 0) return string.Empty;
            return AssetLocation.Combine(segments[0] ?? string.Empty, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(segments, 1)));
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
                    while (!native.isDone) { Progress(operation, native.progress); await Task.Yield(); }
                    bundle = native.assetBundle;
                }
                else
                {
                    using (var request = UnityWebRequestAssetBundle.GetAssetBundle(operation.source))
                    {
                        operation.request = request;
                        request.timeout = Math.Max(0, requestTimeout);
                        request.SendWebRequest();
                        while (!request.isDone) { Progress(operation, request.downloadProgress); await Task.Yield(); }
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
        public Task<AssetBundle> GetAssetBundle(string abName, ABLoadPath loodPath)
        {
            if (!string.IsNullOrEmpty(abName))
            {
                legacyBundles.Add(abName);
                if (manifest != null) foreach (string name in manifest.GetAllDependencies(abName)) legacyBundles.Add(name);
            }
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
                while (!request.isDone) await Task.Yield();
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
        private void CollectLeaseBundles()
        {
            if (assetOperations.Count > 0 || operations.Count > 0) return;
            foreach (string name in new List<string>(managedBundles))
                if (GetLeaseCount(name) == 0 && !legacyBundles.Contains(name) && name != MainName)
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
            leasePins.Clear(); managedBundles.Clear(); legacyBundles.Clear();
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
