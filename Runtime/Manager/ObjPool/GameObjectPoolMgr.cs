using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>空闲实例容器。管理器使用原始出入栈，在状态提交后统一分发业务回调。</summary>
    public class PoolData
    {
        private readonly string containerName;
        private readonly Stack<GameObject> poolList = new Stack<GameObject>();
        private GameObject fatherObj;
        public int Count => poolList.Count;
        public GameObject FatherObj => fatherObj;
        public PoolData(string containerName) { this.containerName = string.IsNullOrEmpty(containerName) ? "Pool" : containerName; }
        internal void PushRaw(GameObject obj, GameObject root, bool layout)
        {
            obj.SetActive(false);
            if (layout && root != null)
            {
                if (fatherObj == null) { fatherObj = new GameObject(containerName + "_F"); fatherObj.transform.SetParent(root.transform); }
                obj.transform.SetParent(fatherObj.transform);
            }
            poolList.Push(obj);
        }
        internal GameObject PopRaw()
        {
            while (poolList.Count > 0)
            {
                var obj = poolList.Pop();
                if (obj == null) continue;
                if (fatherObj != null && obj.transform.parent == fatherObj.transform) obj.transform.SetParent(null);
                return obj;
            }
            return null;
        }
        internal void Remove(GameObject obj)
        {
            if (poolList.Count == 0) return;
            var items = poolList.ToArray(); poolList.Clear();
            for (int i = items.Length - 1; i >= 0; i--)
                if (items[i] != null && !ReferenceEquals(items[i], obj)) poolList.Push(items[i]);
        }
        public bool PushObj(GameObject obj, GameObject poolRoot, bool openLayout, int maxCount)
        {
            if (obj == null) return false;
            try { NotifyDespawn(obj); }
            catch (Exception e) { Debug.LogException(e); return false; }
            if (maxCount > 0 && Count >= maxCount) { UnityEngine.Object.Destroy(obj); return false; }
            PushRaw(obj, poolRoot, openLayout); return true;
        }
        public GameObject GetObj()
        {
            var obj = PopRaw(); if (obj == null) return null;
            try { obj.SetActive(true); NotifySpawn(obj); return obj; }
            catch (Exception e) { Debug.LogException(e); UnityEngine.Object.Destroy(obj); return null; }
        }
        public void Dispose()
        {
            while (poolList.Count > 0) { var obj = poolList.Pop(); if (obj != null) UnityEngine.Object.Destroy(obj); }
            if (fatherObj != null) UnityEngine.Object.Destroy(fatherObj);
            fatherObj = null;
        }
        internal static void NotifySpawn(GameObject obj) { obj.GetComponent<IPoolable>()?.OnSpawn(); }
        internal static void NotifyDespawn(GameObject obj) { obj.GetComponent<IPoolable>()?.OnDespawn(); }
    }

    /// <summary>主线程对象池。一个条目持有一份预制体加载引用，实例共享凭据直到各自销毁。</summary>
    public class GameObjectPoolMgr : LazySingletonBase<GameObjectPoolMgr>, IDisposable
    {
        private sealed class Entry
        {
            public string path;
            public AssetLoadMethod method;
            public int generation;
            public PoolData data;
            public AssetLease<GameObject> prefab;
            public TaskCompletionSource<AssetLease<GameObject>> loading;
            public readonly TaskCompletionSource<bool> canceled = new TaskCompletionSource<bool>();
        }
        public static bool isOpenLayout = true;
        public int maxCountPerKey;
        public float loadTimeout = 15f;
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly HashSet<GameObject> pooledObjects = new HashSet<GameObject>();
        private readonly HashSet<PoolInstanceLease> instances = new HashSet<PoolInstanceLease>();
        private GameObject poolObj;
        private ResourcesLoader resourcesLoader;
        private IAssetsLoader assetsLoader;
        private int generation, creating;
        private bool disposed;
        private static string Key(string path, AssetLoadMethod method) { return (int)method + ":" + path; }
        public int TotalPooledCount
        {
            get { int count = 0; foreach (var entry in entries.Values) count += entry.data.Count; return count; }
        }
        public int PooledCount(string path)
        {
            int count = 0; foreach (var entry in entries.Values) if (entry.path == path) count += entry.data.Count; return count;
        }
        public bool Contains(string path)
        {
            foreach (var entry in entries.Values) if (entry.path == path) return true; return false;
        }
        private Entry EntryFor(string path, AssetLoadMethod method)
        {
            Entry entry; string key = Key(path, method);
            if (!entries.TryGetValue(key, out entry))
            {
                entry = new Entry { path = path, method = method, generation = generation, data = new PoolData(path) };
                entries.Add(key, entry);
            }
            return entry;
        }
        private bool Current(Entry entry) { return !disposed && entry.generation == generation; }
        public async Task<GameObject> GetObjAsync(string path, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (disposed || string.IsNullOrEmpty(path)) return null;
            var entry = EntryFor(path, loadMethod);
            if (entry.data.Count > 0) return SpawnCached(entry);
            var prefab = await Prefab(entry);
            return Current(entry) && prefab != null ? Create(entry, prefab) : null;
        }
        public void GetObjAsync(string path, UnityAction<GameObject> callback, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (callback != null) _ = Deliver(GetObjAsync(path, loadMethod), callback);
        }
        private static async Task Deliver(Task<GameObject> task, UnityAction<GameObject> callback)
        {
            GameObject result = null;
            try { result = await task; } catch (Exception e) { Debug.LogException(e); }
            ResourcesLoader.InvokeCallbacks(callback, result);
        }
        [Obsolete("使用 AssetBundle 时请改用 GetObjAsync")]
        public GameObject GetObj(string path, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (disposed || string.IsNullOrEmpty(path) || loadMethod != AssetLoadMethod.Resources) return null;
            var entry = EntryFor(path, loadMethod);
            if (entry.data.Count > 0) return SpawnCached(entry);
            if (entry.prefab != null && entry.prefab.Asset == null) { entry.prefab.Dispose(); entry.prefab = null; }
            if (entry.prefab == null)
            {
                var loader = ResourcesLoaderRef;
                var acquired = loader != null ? loader.Acquire<GameObject>(path) : null;
                // 同步接管异步 Resources 请求时，异步续体可能已先提交同一条目。
                if (entry.prefab == null) entry.prefab = acquired;
                else acquired?.Dispose();
            }
            return Current(entry) && entry.prefab != null ? Create(entry, entry.prefab) : null;
        }
        public async Task Prewarm(string path, int count, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (disposed || string.IsNullOrEmpty(path) || count <= 0) return;
            var entry = EntryFor(path, loadMethod);
            if (maxCountPerKey > 0) count = Math.Min(count, Math.Max(0, maxCountPerKey - entry.data.Count));
            if (count == 0) return;
            var prefab = await Prefab(entry);
            if (prefab == null || !Current(entry)) return;
            for (int i = 0; i < count && Current(entry); i++)
            {
                if (maxCountPerKey > 0 && entry.data.Count >= maxCountPerKey) break;
                var obj = Create(entry, prefab);
                if (obj == null) break;
                PushObj(path, obj);
            }
        }
        private Task<AssetLease<GameObject>> Prefab(Entry entry)
        {
            if (!Current(entry)) return Task.FromResult<AssetLease<GameObject>>(null);
            if (entry.prefab != null && entry.prefab.Asset != null) return Task.FromResult(entry.prefab);
            entry.prefab?.Dispose(); entry.prefab = null;
            if (entry.loading != null) return entry.loading.Task;
            var completion = new TaskCompletionSource<AssetLease<GameObject>>();
            entry.loading = completion;
            _ = LoadPrefab(entry, completion);
            return completion.Task;
        }
        private async Task LoadPrefab(Entry entry, TaskCompletionSource<AssetLease<GameObject>> completion)
        {
            AssetLease<GameObject> lease = null;
            var timeoutCancellation = new System.Threading.CancellationTokenSource();
            try
            {
                Task<AssetLease<GameObject>> native = LoadLease(entry.path, entry.method);
                Task winner = loadTimeout > 0
                    ? await Task.WhenAny(native, entry.canceled.Task, PlatformTasks.Delay(TimeSpan.FromSeconds(loadTimeout), timeoutCancellation.Token))
                    : await Task.WhenAny(native, entry.canceled.Task);
                if (winner != native)
                {
                    _ = DisposeLate(native);
                    if (Current(entry) && winner != entry.canceled.Task) PLogger.LogError("对象池资源加载超时：" + entry.path);
                    return;
                }
                lease = await native;
                if (!Current(entry)) return;
                if (entry.prefab == null) { entry.prefab = lease; lease = null; }
                completion.TrySetResult(entry.prefab);
            }
            catch (Exception e) { Debug.LogException(e); }
            finally
            {
                timeoutCancellation.Cancel(); timeoutCancellation.Dispose();
                lease?.Dispose();
                if (ReferenceEquals(entry.loading, completion)) entry.loading = null;
                completion.TrySetResult(null);
            }
        }
        private static async Task DisposeLate(Task<AssetLease<GameObject>> task)
        {
            try { (await task)?.Dispose(); } catch (Exception e) { Debug.LogException(e); }
        }
        private async Task<AssetLease<GameObject>> LoadLease(string path, AssetLoadMethod method)
        {
            if (method == AssetLoadMethod.Resources)
            {
                var loader = ResourcesLoaderRef;
                return loader != null ? await loader.AcquireAsync<GameObject>(path) : null;
            }
            if (method != AssetLoadMethod.AssetBundle) return null;
            var assets = AssetsLoaderRef;
            var prefab = assets != null ? await assets.LoadAsync<GameObject>(path) : null;
            return prefab != null ? new AssetLease<GameObject>(prefab, null) : null;
        }
        private GameObject Create(Entry entry, AssetLease<GameObject> prefab)
        {
            var retained = prefab.Retain();
            GameObject obj = null;
            try
            {
                if (retained == null || retained.Asset == null) return null;
                creating++;
                try { obj = UnityEngine.Object.Instantiate(retained.Asset); }
                finally { creating--; }
                if (!Current(entry)) { UnityEngine.Object.Destroy(obj); return null; }
                var tag = obj.AddComponent<PoolInstanceLease>(); tag.hideFlags = HideFlags.HideInInspector;
                tag.owner = this; tag.path = entry.path; tag.method = entry.method; tag.generation = generation;
                tag.lease = retained; retained = null; tag.transitioning = true;
                instances.Add(tag);
                obj.SetActive(true); PoolData.NotifySpawn(obj);
                if (!Current(entry) || obj == null) { if (obj != null) UnityEngine.Object.Destroy(obj); return null; }
                tag.transitioning = false;
                return obj;
            }
            catch (Exception e) { Debug.LogException(e); if (obj != null) UnityEngine.Object.Destroy(obj); return null; }
            finally { retained?.Dispose(); }
        }
        private GameObject SpawnCached(Entry entry)
        {
            var obj = entry.data.PopRaw();
            if (obj == null) return null;
            if (!Current(entry)) { UnityEngine.Object.Destroy(obj); return null; }
            var tag = obj.GetComponent<PoolInstanceLease>();
            pooledObjects.Remove(obj);
            tag.container = null; tag.cached = false; tag.transitioning = true;
            try
            {
                obj.SetActive(true); PoolData.NotifySpawn(obj);
                if (!Current(entry) || obj == null) { if (obj != null) UnityEngine.Object.Destroy(obj); return null; }
                tag.transitioning = false; return obj;
            }
            catch (Exception e) { Debug.LogException(e); if (obj != null) UnityEngine.Object.Destroy(obj); return null; }
        }
        public void PushObj(string name, GameObject obj)
        {
            if (obj == null || string.IsNullOrEmpty(name)) return;
            var tag = obj.GetComponent<PoolInstanceLease>();
            if (tag != null && tag.owner != this)
            {
                if (tag.owner != null && tag.owner.disposed) UnityEngine.Object.Destroy(obj);
                else PLogger.LogWarning("实例属于另一个对象池，拒绝归还");
                return;
            }
            if (tag != null && tag.path != name) { PLogger.LogWarning("归还路径与实例来源不一致：" + name); return; }
            if (disposed || (tag != null && tag.generation != generation))
            { if (tag != null && tag.owner == this) UnityEngine.Object.Destroy(obj); return; }
            if (tag == null)
            {
                if (creating > 0) { PLogger.LogWarning("实例化期间不能归还尚未交付的对象"); return; }
                tag = obj.AddComponent<PoolInstanceLease>(); tag.hideFlags = HideFlags.HideInInspector;
                tag.owner = this; tag.path = name; tag.method = AssetLoadMethod.Resources; tag.generation = generation; instances.Add(tag);
            }
            if (tag.cached || tag.transitioning) { PLogger.LogWarning("忽略重复或重入归还：" + name); return; }
            var entry = EntryFor(name, tag.method);
            tag.transitioning = true;
            try
            {
                PoolData.NotifyDespawn(obj);
                if (!Current(entry) || obj == null) { if (obj != null) UnityEngine.Object.Destroy(obj); return; }
                if (maxCountPerKey > 0 && entry.data.Count >= maxCountPerKey) { UnityEngine.Object.Destroy(obj); return; }
                entry.data.PushRaw(obj, GetPoolRoot(), isOpenLayout);
                if (!Current(entry) || obj == null) { if (obj != null) UnityEngine.Object.Destroy(obj); return; }
                tag.container = entry.data; tag.cached = true; pooledObjects.Add(obj);
            }
            catch (Exception e) { Debug.LogException(e); } // 未提交入池，仍由调用方持有，可以重试归还。
            finally { if (tag != null) tag.transitioning = false; }
        }
        internal void Forget(PoolInstanceLease tag)
        {
            pooledObjects.Remove(tag.gameObject); instances.Remove(tag);
        }
        public void Clear()
        {
            generation++;
            var old = new List<Entry>(entries.Values); entries.Clear(); pooledObjects.Clear();
            var root = poolObj; poolObj = null;
            foreach (var entry in old)
            {
                entry.canceled.TrySetResult(true);
                entry.loading?.TrySetResult(null);
                entry.data.Dispose(); entry.prefab?.Dispose(); entry.prefab = null;
            }
            if (root != null) UnityEngine.Object.Destroy(root);
        }
        public void Dispose() { if (disposed) return; disposed = true; Clear(); }
        private GameObject GetPoolRoot()
        {
            if (!isOpenLayout) return null;
            if (poolObj == null) poolObj = new GameObject("Pool");
            return poolObj;
        }
        private ResourcesLoader ResourcesLoaderRef => resourcesLoader != null ? resourcesLoader : (resourcesLoader = ResourcesLoader.Instance);
        private IAssetsLoader AssetsLoaderRef
        {
            get
            {
                if (assetsLoader is UnityEngine.Object unity && unity == null) assetsLoader = null;
                if (assetsLoader != null) return assetsLoader;
                var root = UPGameRoot.Instance;
                return root != null && root.IsInited ? (assetsLoader = root.GetAssetsLoader()) : null;
            }
        }
    }
}
