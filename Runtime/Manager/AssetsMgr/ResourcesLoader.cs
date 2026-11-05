using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UPandaGF;

[Serializable]
public class AssetBundleClassificationWindowConfig
{
    /// <summary>
    /// 是否对资源清单做 AES 加密。
    /// <para>⚠ 默认 <c>false</c>：随包清单 assetData.assetref 是明文，运行端误开会让清单解密失败、
    /// AssetBundle 模式整体不可用。需要加密时必须"打包端加密 + 运行端开启"成对配置。</para>
    /// </summary>
    public bool enable = false;
    public string AESKEY = "111a222aaabbbccc";
    public string AESIV = "111b222aaabbbccc";
    public ABLoadPath mainBundleLoadPath = ABLoadPath.StreamingAssetsPath;
}

/// <summary>资源缓存记录。加载失败不保留记录；引用计数不允许为负。</summary>
public abstract class ResInfoBase
{
    public int refCount;
    internal virtual UnityEngine.Object AssetObject => null;
    internal virtual void CancelPending() { }
}

public class ResInfo<T> : ResInfoBase
{
    public T asset;
    public UnityAction<T> callback;
    public Coroutine coroutine;
    public bool isDel;
    internal bool loading;
    internal override UnityEngine.Object AssetObject => asset as UnityEngine.Object;
    public void AddRefCount() { ++refCount; }
    public void SubRefCount()
    {
        if (refCount > 0) --refCount;
        else Debug.LogWarning("资源引用计数已为 0，请检查重复释放。");
    }
    internal override void CancelPending()
    {
        var pending = callback;
        callback = null;
        coroutine = null;
        loading = false;
        ResourcesLoader.InvokeCallbacks(pending, default(T));
    }
}

/// <summary>主线程 Resources 加载服务。泛型与 Type API 保留独立的引用计数。</summary>
public class ResourcesLoader : LazyMonoSingletonBase<ResourcesLoader>
{
    private readonly Dictionary<string, ResInfoBase> resDic = new Dictionary<string, ResInfoBase>();
    private bool destroyed;
    private static string Key(string path, Type type, bool generic)
    {
        return path + (generic ? "_T_" : "_O_") + type.AssemblyQualifiedName;
    }

    internal static void InvokeCallbacks<T>(UnityAction<T> callbacks, T value)
    {
        if (callbacks == null) return;
        foreach (UnityAction<T> callback in callbacks.GetInvocationList())
        {
            try { callback(value); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    // 内部持有接口绑定到原始缓存记录，ClearDic/重建后不会误减新一代同路径的引用。
    internal AssetLease<T> Acquire<T>(string path) where T : UnityEngine.Object
    {
        if (destroyed) return null;
        string key = Key(path, typeof(T), true);
        var info = GetLeaseEntry<T>(key);
        T asset = LoadCore<T>(path, typeof(T), key);
        return asset != null ? new AssetLease<T>(asset, () => ReleaseExact(key, info)) : null;
    }
    internal Task<AssetLease<T>> AcquireAsync<T>(string path) where T : UnityEngine.Object
    {
        var done = new TaskCompletionSource<AssetLease<T>>();
        if (destroyed) { done.SetResult(null); return done.Task; }
        string key = Key(path, typeof(T), true);
        var info = GetLeaseEntry<T>(key);
        LoadAsyncCore<T>(path, typeof(T), key, asset => done.TrySetResult(asset != null
            ? new AssetLease<T>(asset, () => ReleaseExact(key, info)) : null));
        return done.Task;
    }
    private ResInfo<T> GetLeaseEntry<T>(string key)
    {
        ResInfoBase entry;
        if (resDic.TryGetValue(key, out entry)) return (ResInfo<T>)entry;
        var info = new ResInfo<T>(); resDic.Add(key, info); return info;
    }
    private void ReleaseExact<T>(string key, ResInfo<T> info)
    {
        ResInfoBase current;
        if (!destroyed && resDic.TryGetValue(key, out current) && ReferenceEquals(current, info))
            Release<T>(key, true, null);
    }

    public T Load<T>(string path) where T : UnityEngine.Object
    {
        return LoadCore<T>(path, typeof(T), Key(path, typeof(T), true));
    }
    public UnityEngine.Object Load(string path, Type type)
    {
        return LoadCore<UnityEngine.Object>(path, type, Key(path, type, false));
    }
    private T LoadCore<T>(string path, Type type, string key) where T : UnityEngine.Object
    {
        if (destroyed) return null;
        ResInfoBase entry;
        ResInfo<T> info;
        if (resDic.TryGetValue(key, out entry))
        {
            info = (ResInfo<T>)entry;
            info.AddRefCount();
            if (info.asset != null) return info.asset;
            if (info.coroutine != null) StopCoroutine(info.coroutine);
        }
        else
        {
            info = new ResInfo<T>();
            info.AddRefCount();
            resDic.Add(key, info);
        }
        T asset = null;
        try { asset = Resources.Load(path, type) as T; }
        catch (Exception e) { Debug.LogException(e); }
        Complete(key, info, asset);
        return asset;
    }

    public void LoadAsync<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object
    {
        LoadAsyncCore(path, typeof(T), Key(path, typeof(T), true), callback);
    }
    public void LoadAsync(string path, Type type, UnityAction<UnityEngine.Object> callback)
    {
        LoadAsyncCore(path, type, Key(path, type, false), callback);
    }
    private void LoadAsyncCore<T>(string path, Type type, string key, UnityAction<T> callback) where T : UnityEngine.Object
    {
        if (destroyed) { InvokeCallbacks(callback, default(T)); return; }
        ResInfoBase entry;
        ResInfo<T> info;
        if (resDic.TryGetValue(key, out entry))
        {
            info = (ResInfo<T>)entry;
            info.AddRefCount();
            if (info.asset != null) { InvokeCallbacks(callback, info.asset); return; }
        }
        else
        {
            info = new ResInfo<T>();
            info.AddRefCount();
            resDic.Add(key, info);
        }
        info.callback += callback;
        if (!info.loading)
        {
            info.loading = true;
            var coroutine = StartCoroutine(LoadRoutine(path, type, key, info));
            // Unity 会立即执行到第一个 yield，失败时 Complete 可能已经执行。
            if (info.loading) info.coroutine = coroutine;
        }
    }
    private IEnumerator LoadRoutine<T>(string path, Type type, string key, ResInfo<T> info) where T : UnityEngine.Object
    {
        ResourceRequest request = null;
        try { request = Resources.LoadAsync(path, type); }
        catch (Exception e) { Debug.LogException(e); }
        if (request != null) yield return request;
        ResInfoBase current;
        // ClearDic 后同路径可重新加载，旧请求不能覆盖新记录。
        if (resDic.TryGetValue(key, out current) && ReferenceEquals(current, info))
            Complete(key, info, request != null ? request.asset as T : null);
    }
    private void Complete<T>(string key, ResInfo<T> info, T asset) where T : UnityEngine.Object
    {
        info.asset = asset;
        info.loading = false;
        info.coroutine = null;
        var callbacks = info.callback;
        info.callback = null; // 先提交状态再执行用户回调，允许回调中重入加载/卸载。
        if (asset == null)
        {
            resDic.Remove(key);
            info.refCount = 0;
            Debug.LogError("资源加载失败：" + key);
        }
        else if (info.refCount == 0 && info.isDel)
        {
            RemoveAsset(key, asset);
        }
        InvokeCallbacks(callbacks, info.refCount > 0 ? asset : null);
    }

    public void UnLoadAsset<T>(string path, bool isDel = false, UnityAction<T> callBack = null)
    {
        Release(Key(path, typeof(T), true), isDel, callBack);
    }
    public void UnLoadAsset(string path, Type type, bool isDel = false, UnityAction<UnityEngine.Object> callBack = null)
    {
        Release(Key(path, type, false), isDel, callBack);
    }
    private void Release<T>(string key, bool isDel, UnityAction<T> callback)
    {
        ResInfoBase entry;
        if (!resDic.TryGetValue(key, out entry)) return;
        var info = (ResInfo<T>)entry;
        info.SubRefCount();
        info.isDel = isDel;
        if (info.loading && callback != null) info.callback -= callback;
        if (!info.loading && info.refCount == 0 && isDel) RemoveAsset(key, info.AssetObject);
    }
    private void RemoveAsset(string key, UnityEngine.Object asset)
    {
        resDic.Remove(key);
        // Prefab 是资源，不能 Destroy。清除缓存后由 UnloadUnusedAssets 回收。
        if (asset == null || asset is GameObject || asset is Component) return;
        foreach (var info in resDic.Values)
            if (info.AssetObject == asset) return;
        Resources.UnloadAsset(asset);
    }
    public int GetAssetsRefCount<T>(string path) { return RefCount(Key(path, typeof(T), true)); }
    public int GetAssetsRefCount(string path, Type type) { return RefCount(Key(path, type, false)); }
    private int RefCount(string key)
    {
        ResInfoBase info;
        return resDic.TryGetValue(key, out info) ? info.refCount : 0;
    }

    public void UnLoadUnUseAssets(UnityAction callback = null)
    {
        // 加载中的记录必须保留，以便完成请求并通知等待者。
        var keys = new List<string>();
        foreach (var pair in resDic)
            if (pair.Value.refCount == 0 && pair.Value.AssetObject != null) keys.Add(pair.Key);
        foreach (var key in keys) resDic.Remove(key);
        StartCoroutine(UnloadUnused(callback));
    }
    private IEnumerator UnloadUnused(UnityAction callback)
    {
        yield return Resources.UnloadUnusedAssets();
        if (callback != null)
            foreach (UnityAction handler in callback.GetInvocationList())
                try { handler(); } catch (Exception e) { Debug.LogException(e); }
    }
    public void ClearDic(UnityAction callBack = null)
    {
        CancelPendingLoads();
        if (!destroyed) StartCoroutine(UnloadUnused(callBack));
    }
    private void CancelPendingLoads()
    {
        var entries = new List<ResInfoBase>(resDic.Values);
        resDic.Clear();
        StopAllCoroutines();
        foreach (var entry in entries) entry.CancelPending();
    }
    protected override void OnDestroyed()
    {
        destroyed = true;
        CancelPendingLoads();
        base.OnDestroyed();
    }
}
