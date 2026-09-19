using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// 对象池容器：按 key（资源路径）归类保存同一类对象的空闲实例。
    /// 内部使用 LIFO 栈（最近归还的最先被复用），可选在 Hierarchy 中按层级收纳。
    /// </summary>
    public class PoolData
    {
        private readonly string containerName;                     // 容器名（即池的 key）
        private readonly Stack<GameObject> poolList = new Stack<GameObject>();

        private GameObject fatherObj;                              // 层级收纳用的父节点（开启收纳时惰性创建）

        /// <summary>当前缓存的空闲对象数量</summary>
        public int Count { get { return poolList.Count; } }

        /// <summary>层级收纳节点（未开启收纳或尚未创建时为 null）</summary>
        public GameObject FatherObj { get { return fatherObj; } }

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="containerName">容器名（一般为资源路径）</param>
        public PoolData(string containerName)
        {
            this.containerName = string.IsNullOrEmpty(containerName) ? "Pool" : containerName;
        }

        /// <summary>
        /// 对象入池：回调 OnDespawn -> 失活 -> 压栈 ->（开启收纳时）挂到容器节点下。
        /// 超出上限时直接销毁对象。
        /// </summary>
        /// <param name="obj">要回收的对象</param>
        /// <param name="poolRoot">对象池根节点（仅收纳模式需要，可为 null）</param>
        /// <param name="openLayout">是否按层级收纳</param>
        /// <param name="maxCount">允许缓存的空闲对象上限，0 = 不限（每次入池实时读取）</param>
        /// <returns>是否真的放进了池子（false 表示超出上限已被销毁）</returns>
        public bool PushObj(GameObject obj, GameObject poolRoot, bool openLayout, int maxCount)
        {
            if (obj == null) return false;

            // 超出上限：不缓存，直接销毁，避免池无限膨胀
            if (maxCount > 0 && poolList.Count >= maxCount)
            {
                UnityEngine.Object.Destroy(obj);
                return false;
            }

            NotifyDespawn(obj);          // 先给业务一次复位机会（此时对象还是激活状态）
            obj.SetActive(false);
            poolList.Push(obj);

            if (openLayout)
            {
                GameObject father = EnsureFatherObj(poolRoot);
                if (father != null)
                {
                    obj.transform.SetParent(father.transform);
                }
            }

            return true;
        }

        /// <summary>
        /// 对象出池：弹栈 -> 必要的话脱离父节点 -> 激活 -> 回调 OnSpawn。
        /// 池为空（或对象已被外部销毁）时返回 null。
        /// </summary>
        public GameObject GetObj()
        {
            while (poolList.Count > 0)
            {
                GameObject obj = poolList.Pop();

                // 对象可能在池里被外部 Destroy 掉了，跳过
                if (obj == null) continue;

                // 只脱离"本容器挂上去的"父节点：不再依赖布局开关，
                // 避免开关在运行期被改动后对象一直留在池节点下
                if (fatherObj != null && obj.transform.parent == fatherObj.transform)
                {
                    obj.transform.SetParent(null);
                }

                obj.SetActive(true);
                NotifySpawn(obj);
                return obj;
            }

            return null;
        }

        /// <summary>
        /// 销毁容器内所有空闲对象与容器节点。
        /// 注意：已经取出、正在使用中的对象不属于池，不会被销毁（由调用方负责）。
        /// </summary>
        public void Dispose()
        {
            while (poolList.Count > 0)
            {
                GameObject obj = poolList.Pop();
                if (obj != null) UnityEngine.Object.Destroy(obj);
            }

            if (fatherObj != null)
            {
                UnityEngine.Object.Destroy(fatherObj);
                fatherObj = null;
            }
        }

        /// <summary>按需创建层级收纳节点</summary>
        private GameObject EnsureFatherObj(GameObject poolRoot)
        {
            if (fatherObj != null) return fatherObj;
            if (poolRoot == null) return null;

            fatherObj = new GameObject(containerName + "_F");
            fatherObj.transform.SetParent(poolRoot.transform);
            return fatherObj;
        }

        /// <summary>回调根节点上的 IPoolable（不递归子物体）</summary>
        private static void NotifySpawn(GameObject obj)
        {
            IPoolable poolable = obj.GetComponent<IPoolable>();
            if (poolable != null) poolable.OnSpawn();
        }

        /// <summary>回调根节点上的 IPoolable（不递归子物体）</summary>
        private static void NotifyDespawn(GameObject obj)
        {
            IPoolable poolable = obj.GetComponent<IPoolable>();
            if (poolable != null) poolable.OnDespawn();
        }
    }

    /// <summary>
    /// GameObject 对象池（纯 C# 懒加载单例）。
    ///
    /// 特性：
    ///   - 按资源路径分容器缓存对象，池中有货时零加载、零分配（除出池本身）
    ///   - 支持 Resources / AssetBundle 两种加载方式；池空时自动加载并实例化
    ///   - 异步取对象提供 Task 与回调两种形式；同步取对象仅支持 Resources（已标记过时）
    ///   - 可选层级收纳（isOpenLayout），开发期便于在 Hierarchy 查看
    ///   - 支持单个容器的缓存上限（maxCountPerKey）与预热（Prewarm）
    ///   - 可选的 IPoolable 回调，用于复用对象时复位状态
    ///
    /// 详细说明与示例见同目录 README.md。
    /// </summary>
    public class GameObjectPoolMgr : LazySingletonBase<GameObjectPoolMgr>, IDisposable
    {
        /// <summary>
        /// 对象放回池后是否在 Hierarchy 中按层级收纳（<key>_F 容器挂到 Pool 节点下）。
        /// 开发期方便查看；打包时可设为 false 省一点开销。
        /// 说明：该开关只影响"收纳"，运行期开关切换不会再导致对象取不出来（出池时按实际父节点判断）。
        /// </summary>
        public static bool isOpenLayout = true;

        /// <summary>单个容器允许缓存的空闲对象上限，0 = 不限；超出上限的对象会被直接销毁</summary>
        public int maxCountPerKey = 0;

        /// <summary>Resources 异步加载超时（秒），超时视为加载失败</summary>
        public float loadTimeout = 15f;

        private Dictionary<string, PoolData> poolDic;
        private readonly HashSet<GameObject> pooledObjects = new HashSet<GameObject>();   // 已在池中的对象，防止重复入池导致"一个实例被取两次"
        private GameObject poolObj;                 // 池根节点（仅收纳模式需要）

        private ResourcesLoader resourcesLoader;    // 惰性获取
        private IAssetsLoader assetsLoader;         // 惰性获取（仅 AssetBundle 模式使用）

        protected override void OnInit()
        {
            poolDic = new Dictionary<string, PoolData>();
        }

        // ==================== 查询 ====================

        /// <summary>所有容器中缓存的空闲对象总数</summary>
        public int TotalPooledCount
        {
            get
            {
                int total = 0;
                if (poolDic == null) return total;

                foreach (KeyValuePair<string, PoolData> pair in poolDic)
                {
                    total += pair.Value.Count;
                }
                return total;
            }
        }

        /// <summary>取指定 key 的容器中空闲对象数量（没有该容器时返回 0）</summary>
        public int PooledCount(string path)
        {
            PoolData data;
            return TryGetPool(path, out data) ? data.Count : 0;
        }

        /// <summary>是否已经存在该 key 的容器</summary>
        public bool Contains(string path)
        {
            return poolDic != null && !string.IsNullOrEmpty(path) && poolDic.ContainsKey(path);
        }

        // ==================== 取对象 ====================

        /// <summary>
        /// 异步取对象（推荐）
        /// </summary>
        /// <param name="path">路径【Resources 路径下不需要后缀，AssetBundle 需要后缀】</param>
        /// <param name="loadMethod">加载方式</param>
        /// <returns>池中有货直接返回；否则加载并实例化。失败（含超时）返回 null</returns>
        public async Task<GameObject> GetObjAsync(string path, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError("[GameObjectPoolMgr] path 为空，无法取对象");
                return null;
            }

            GameObject pooled;
            if (TryGetFromPool(path, out pooled)) return pooled;

            GameObject prefab = await LoadObjAsync(path, loadMethod);
            return CreateInstance(path, prefab);
        }

        /// <summary>
        /// 异步取对象（回调形式，与框架其它异步 API 风格一致）。
        /// 失败时同样会以 null 回调，调用方需判空。
        /// </summary>
        /// <param name="path">路径【Resources 路径下不需要后缀，AssetBundle 需要后缀】</param>
        /// <param name="callback">结果回调（失败传 null）</param>
        /// <param name="loadMethod">加载方式</param>
        public void GetObjAsync(string path, UnityAction<GameObject> callback, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError("[GameObjectPoolMgr] path 为空，无法取对象");
                if (callback != null) callback(null);
                return;
            }

            GameObject pooled;
            if (TryGetFromPool(path, out pooled))
            {
                if (callback != null) callback(pooled);
                return;
            }

            switch (loadMethod)
            {
                case AssetLoadMethod.Resources:
                    ResourcesLoaderRef.LoadAsync<GameObject>(path, (asset) =>
                    {
                        if (callback != null) callback(CreateInstance(path, asset));
                    });
                    break;

                case AssetLoadMethod.AssetBundle:
                    IAssetsLoader loader = AssetsLoaderRef;
                    if (loader == null)
                    {
                        if (callback != null) callback(null);
                        return;
                    }
                    loader.LoadAsync<GameObject>(path, (asset) =>
                    {
                        if (callback != null) callback(CreateInstance(path, asset));
                    });
                    break;

                default:
                    PLogger.LogError($"[GameObjectPoolMgr] 不支持的加载方式：{loadMethod}");
                    if (callback != null) callback(null);
                    break;
            }
        }

        /// <summary>
        /// 取对象【同步】—— 仅支持 Editor/Resources 模式；AssetBundle 必须用异步。
        /// 内部走 ResourcesLoader，与框架的缓存/引用计数保持一致。
        /// </summary>
        /// <param name="path">Resources 路径（不需要后缀）</param>
        /// <param name="loadMethod">加载方式，传 AssetBundle 会直接失败返回 null</param>
        [Obsolete("使用AssetBundle时不支持该方法，请改用 GetObjAsync")]
        public GameObject GetObj(string path, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError("[GameObjectPoolMgr] path 为空，无法取对象");
                return null;
            }

            GameObject pooled;
            if (TryGetFromPool(path, out pooled)) return pooled;

            if (loadMethod == AssetLoadMethod.AssetBundle)
            {
                PLogger.LogError("使用AssetBundle加载不支持同步方式获取，请使用异步方法加载:【GetObjAsync(path, AssetLoadMethod.AssetBundle);】");
                return null;
            }

            return CreateInstance(path, ResourcesLoaderRef.Load<GameObject>(path));
        }

        // ==================== 回收 / 预热 / 清空 ====================

        /// <summary>
        /// 把对象放回对象池。
        /// </summary>
        /// <param name="name">池的 key（必须是当初取对象时用的同一个 path）</param>
        /// <param name="obj">要回收的对象；同一个对象重复入池会被忽略并给出警告</param>
        public void PushObj(string name, GameObject obj)
        {
            if (obj == null)
            {
                PLogger.LogError($"[GameObjectPoolMgr] PushObj 收到 null 对象，key:{name}");
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                PLogger.LogError($"[GameObjectPoolMgr] PushObj 的 key 为空，对象：{obj.name}");
                return;
            }

            // 同一个对象不允许重复入池（否则两次出池会拿到同一个实例）
            if (!pooledObjects.Add(obj))
            {
                PLogger.LogWarning($"[GameObjectPoolMgr] 对象 {obj.name} 已经在池中，忽略重复入池");
                return;
            }

            PoolData data;
            if (!poolDic.TryGetValue(name, out data))
            {
                data = new PoolData(name);
                poolDic.Add(name, data);
            }

            // 超出上限时 PoolData 会直接销毁对象，此时要把标记撤掉
            if (!data.PushObj(obj, GetPoolRoot(), isOpenLayout, maxCountPerKey))
            {
                pooledObjects.Remove(obj);
            }
        }

        /// <summary>
        /// 预热：提前加载并实例化若干个对象放入池中，避免首次取对象时的加载尖峰。
        /// </summary>
        /// <param name="path">路径【Resources 路径下不需要后缀，AssetBundle 需要后缀】</param>
        /// <param name="count">预实例化数量</param>
        /// <param name="loadMethod">加载方式</param>
        public async Task Prewarm(string path, int count, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (string.IsNullOrEmpty(path) || count <= 0) return;

            GameObject prefab = await LoadObjAsync(path, loadMethod);
            if (prefab == null)
            {
                PLogger.LogError($"[GameObjectPoolMgr] 预热失败，资源加载不出来。path:{path}");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                PushObj(path, GameObject.Instantiate(prefab));
            }
        }

        /// <summary>
        /// 清空对象池：销毁所有容器中缓存的空闲对象、容器节点与池根节点。
        /// 主要用于场景切换；已经取出、正在使用中的对象不会被销毁（由调用方负责）。
        /// </summary>
        public void Clear()
        {
            if (poolDic != null)
            {
                foreach (KeyValuePair<string, PoolData> pair in poolDic)
                {
                    if (pair.Value != null) pair.Value.Dispose();
                }
                poolDic.Clear();
            }

            pooledObjects.Clear();

            if (poolObj != null)
            {
                UnityEngine.Object.Destroy(poolObj);
                poolObj = null;
            }
        }

        /// <summary>供 LazySingletonBase.Release() 调用，等价于 Clear()</summary>
        public void Dispose()
        {
            Clear();
        }

        // ==================== 内部实现 ====================

        /// <summary>从池中取出一个对象；池中没有可用对象时返回 false</summary>
        private bool TryGetFromPool(string path, out GameObject obj)
        {
            obj = null;

            PoolData data;
            if (!TryGetPool(path, out data) || data.Count == 0) return false;

            obj = data.GetObj();
            if (obj == null) return false;

            pooledObjects.Remove(obj);
            return true;
        }

        /// <summary>取容器（顺带兜底 poolDic 未初始化的情况）</summary>
        private bool TryGetPool(string path, out PoolData data)
        {
            data = null;
            if (string.IsNullOrEmpty(path)) return false;

            if (poolDic == null) poolDic = new Dictionary<string, PoolData>();
            return poolDic.TryGetValue(path, out data) && data != null;
        }

        /// <summary>按需创建池根节点（关闭收纳时不需要）</summary>
        private GameObject GetPoolRoot()
        {
            if (!isOpenLayout) return null;

            if (poolObj == null) poolObj = new GameObject("Pool");
            return poolObj;
        }

        /// <summary>实例化预制体；prefab 为 null（加载失败）时打印错误并返回 null</summary>
        private GameObject CreateInstance(string path, GameObject prefab)
        {
            if (prefab == null)
            {
                PLogger.LogError($"[GameObjectPoolMgr] 加载失败，无法创建对象。path:{path}");
                return null;
            }

            // Instantiate 会自动复制预制体的名字，无需（也不应该）去改共享资源的名字
            return GameObject.Instantiate(prefab);
        }

        /// <summary>按加载方式加载预制体（不实例化）</summary>
        private async Task<GameObject> LoadObjAsync(string path, AssetLoadMethod loadMethod)
        {
            switch (loadMethod)
            {
                case AssetLoadMethod.Resources:
                    return await LoadByResourcesAsync(path);

                case AssetLoadMethod.AssetBundle:
                    IAssetsLoader loader = AssetsLoaderRef;
                    if (loader == null) return null;
                    return await loader.LoadAsync<GameObject>(path);

                default:
                    PLogger.LogError($"[GameObjectPoolMgr] 不支持的加载方式：{loadMethod}");
                    return null;
            }
        }

        /// <summary>
        /// Resources 异步加载：ResourcesLoader 只提供回调形式，
        /// 这里用 Task + 超时包一层，避免回调迟迟不来时永久等待。
        /// </summary>
        private async Task<GameObject> LoadByResourcesAsync(string path)
        {
            GameObject asset = null;
            bool isLoaded = false;

            ResourcesLoaderRef.LoadAsync<GameObject>(path, (obj) =>
            {
                asset = obj;
                isLoaded = true;
            });

            float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, loadTimeout);
            while (!isLoaded && Time.realtimeSinceStartup < deadline)
            {
                await Task.Yield();
            }

            if (!isLoaded)
            {
                PLogger.LogError($"[GameObjectPoolMgr] 加载超时（{loadTimeout} 秒）。path:{path}");
            }

            return asset;
        }

        /// <summary>资源加载器（惰性获取，Resources 模式不会触碰 UPGameRoot）</summary>
        private ResourcesLoader ResourcesLoaderRef
        {
            get
            {
                if (resourcesLoader == null) resourcesLoader = ResourcesLoader.Instance;
                return resourcesLoader;
            }
        }

        /// <summary>
        /// AssetBundle 资源加载器（惰性获取）。
        /// 注意：UPGameRoot 是饿汉单例，缺失时会自动创建实例，
        /// 因此只有真正用到 AssetBundle 模式时才会去取它。
        /// </summary>
        private IAssetsLoader AssetsLoaderRef
        {
            get
            {
                if (assetsLoader != null) return assetsLoader;

                UPGameRoot root = UPGameRoot.Instance;
                assetsLoader = root != null ? root.GetAssetsLoader() : null;
                if (assetsLoader == null)
                {
                    PLogger.LogError("[GameObjectPoolMgr] 资源加载器不可用（UPGameRoot 未初始化或资源系统尚未就绪），AssetBundle 模式无法加载");
                }
                return assetsLoader;
            }
        }
    }
}
