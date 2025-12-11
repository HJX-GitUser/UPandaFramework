using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 资源信息基类
/// </summary>
public abstract class ResInfoBase
{
    /// <summary>
    /// 引用计数
    /// </summary>
    public int refCount;
}
/// <summary>
/// 资源信息
/// </summary>
/// <typeparam name="T"></typeparam>
public class ResInfo<T> : ResInfoBase
{
    /// <summary>
    /// 资源
    /// </summary>
    public T asset;
    /// <summary>
    /// 异步加载完成的回调
    /// </summary>
    public UnityAction<T> callback;
    /// <summary>
    /// 存储异步时的协同程序
    /// </summary>
    public Coroutine coroutine;
    /// <summary>
    /// 引用计数为0，该资源是否移除
    /// </summary>
    public bool isDel;
    public void AddRefCount()
    {
        ++refCount;
    }

    public void SubRefCount()
    {
        --refCount;
        if (refCount < 0) Debug.Log($"{asset}:该资源引用计数小于0！需自行检查加载和卸载的执行次数");
    }


}


/// <summary>
/// Resources加载资源
/// </summary>
public class ResMgr : LazyMonoSingletonBase<ResMgr> 
{
    /// <summary>
    /// 存储 加载过 或 加载中 的资源
    /// key是资源名 （路径+资源类型）拼接
    /// </summary>
    private Dictionary<string, ResInfoBase> resDic = new Dictionary<string, ResInfoBase>();
    /// <summary>
    /// 同步加载资源
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="path"></param>
    /// <returns></returns>
    public T Load<T>(string path) where T : UnityEngine.Object
    {
        string resName = path + "_" + typeof(T).Name;
        ResInfo<T> info;
        if (!resDic.ContainsKey(resName))
        {
            T res = Resources.Load<T>(path);
            info = new ResInfo<T>();
            info.asset = res;
            info. AddRefCount();
            resDic.Add(resName, info);
            return res;
        }
        else//如果字典里有这个资源，需要判断是否处于加载中的状态
        {
            info = resDic[resName] as ResInfo<T>;
            info.AddRefCount();
            if (info.asset == null)
            {
                //关掉正在异步加载资源状态的协程，直接用同步的方式加载
                StopCoroutine(info.coroutine);
                T res = Resources.Load<T>(path);
                info.asset = res;
                info.callback?.Invoke(res);
                info.callback = null;
                info.coroutine = null;
                return res;//如果资源时GameObject类中需要自己再实例化
            }
            else
            {
                return info.asset;
            }
        }
        
    }
    /// <summary>
    /// Type同步加载资源
    /// </summary>
    /// <param name="path"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    [Obsolete("\n警告!\n禁止和Load<T>方式混用，所有资源加载必须用同一种加载方式")]
    public UnityEngine.Object Load(string path, System.Type type)
    {
        string resName = path + "_" + type.Name;
        ResInfo<UnityEngine.Object> info;
        if (!resDic.ContainsKey(resName))
        {
            UnityEngine.Object res = Resources.Load(path, type);
            info = new ResInfo<UnityEngine.Object>();
            info.asset = res;
            info.AddRefCount();
            resDic.Add(resName, info);
            return res;
        }
        else//如果字典里有这个资源，需要判断是否处于加载中的状态
        {
            info = resDic[resName] as ResInfo<UnityEngine.Object>;
            info.AddRefCount();
            if (info.asset == null)
            {
                //关掉正在异步加载资源状态的协程，直接用同步的方式加载
                StopCoroutine(info.coroutine);
                UnityEngine.Object res = Resources.Load(path, type);
                info.asset = res;
                info.callback?.Invoke(res);
                info.callback = null;
                info.coroutine = null;
                return res;
            }
            else
            {
                return info.asset;
            }
        }
    }
    /// <summary>
    /// 异步加载资源
    /// </summary>
    /// <typeparam name="T">资源类型</typeparam>
    /// <param name="path">资源路径</param>
    /// <param name="callback">回调</param>
    public void LoadAsync<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object
    {
        string resName = path + "_" + typeof(T).Name;
        ResInfo<T> info;
        if (!resDic.ContainsKey(resName))//如果字典里不包含该资源
        {
            info = new ResInfo<T>();
            info.AddRefCount();
            resDic.Add(resName, info);

            info.callback += callback;
            info.coroutine = StartCoroutine(ReallyLoadAsync<T>(path));
        }
        else
        {
            info = resDic[resName] as ResInfo<T>;
            info.AddRefCount();
            //等于null 说明资源还没有加载完 还在异步加载中
            if (info.asset == null)
            {
                info.callback += callback;
            }
            else//加载完成的资源
            {
                callback?.Invoke(info.asset);
            }
        }
    }

    private IEnumerator ReallyLoadAsync<T>(string path) where T : UnityEngine.Object
    {
        ResourceRequest r = Resources.LoadAsync<T>(path);
        yield return r;
        string resName = path + "_" + typeof(T).Name;
        if (resDic.ContainsKey(resName))
        {
            ResInfo<T> resInfo = resDic[resName] as ResInfo<T>;
            //记录加载完成的资源
            resInfo.asset = r.asset as T;

            if (resInfo.refCount == 0)//如果引用为0则删除
            {
                UnLoadAsset_NoSubAssetsCount<T>(path, resInfo.isDel);
            }
            else
            {
                resInfo.callback?.Invoke(resInfo.asset);//通过委托把加载好的资源传出去

                //资源传出去后把回调和协程置空 避免引用占用
                resInfo.callback = null;
                resInfo.coroutine = null;
            }
        }
        //if (r.asset is GameObject)
        //    callback(GameObject.Instantiate(r.asset) as T);
        //else
        //    callback(r.asset as T);
    }
    /// <summary>
    /// 禁止和LoadAsync<T>方式混用。
    /// 所有资源加载必须同意加载方式
    /// </summary>
    /// <param name="path">资源路径</param>
    /// <param name="type">类型，用typeof(class)得到</param>
    /// <param name="callback">回调 </param>
    [Obsolete("\n警告!\n禁止和LoadAsync<T>方式混用，所有资源加载必须用同一种加载方式")]
    public void LoadAsync(string path, Type type, UnityAction<UnityEngine.Object> callback)
    {
        string resName = path + "_" + type.Name;
        ResInfo<UnityEngine.Object> info;
        if (!resDic.ContainsKey(resName))//如果字典里不包含该资源
        {
            info = new ResInfo<UnityEngine.Object>();
            info.AddRefCount();
            resDic.Add(resName, info);

            info.callback += callback;
            info.coroutine = StartCoroutine(ReallyTypeLoadAsync(path, type));
        }
        else
        {
            info = resDic[resName] as ResInfo<UnityEngine.Object>;
            info.AddRefCount();
            //等于null 说明资源还没有加载完 还在异步加载中
            if (info.asset == null)
            {
                info.callback += callback;
            }
            else//加载完成的资源
            {
                callback?.Invoke(info.asset);
            }
        }
        //
    }
    private IEnumerator ReallyTypeLoadAsync(string path, Type type)
    {
        ResourceRequest r = Resources.LoadAsync(path, type);
        yield return r;
        string resName = path + "_" + type.Name;
        if (resDic.ContainsKey(resName))
        {
            ResInfo<UnityEngine.Object> resInfo = resDic[resName] as ResInfo<UnityEngine.Object>;
            //记录加载完成的资源
            resInfo.asset = r.asset;

            if (resInfo.refCount == 0)//如果引用为0则删除
            {
                UnLoadAsset_NoSubAssetsCount(path, type, resInfo.isDel);
            }
            else
            {
                resInfo.callback?.Invoke(resInfo.asset);//通过委托把加载好的资源传出去
                //资源传出去后把回调和协程置空 避免引用占用
                resInfo.callback = null;
                resInfo.coroutine = null;
            }

        }
        //if (r.asset is GameObject)
        //    callback(GameObject.Instantiate(r.asset));
        //else
        //    callback(r.asset);
    }
    /// <summary>
    /// 卸载指定资源
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="path"></param>
    /// <param name="isDel">引用为0时，是否移除该资源</param>
    /// <param name="callBack"></param>
    public void UnLoadAsset<T>(string path, bool isDel = false, UnityAction<T> callBack = null)
    {
        string resName = path + "_" + typeof(T).Name;
        if (resDic.ContainsKey(resName))
        {
            ResInfo<T> resInfo = resDic[resName] as ResInfo<T>;
            resInfo.SubRefCount();
            resInfo.isDel = isDel;
            if (resInfo.asset != null && resInfo.refCount == 0 && resInfo.isDel)//资源已加载结束的状态
            {
                resDic.Remove(resName);
                Resources.UnloadAsset(resInfo.asset as UnityEngine.Object);
            }
            else if (resInfo.asset == null)//资源异步加载中的状态
            {
                //把待移除状态改为true,等异步加载完成再开始卸载
                //resInfo.isDel = true;

                if (callBack != null)
                    resInfo.callback -= callBack;
            }
        }
    }
    /// <summary>
    /// ResMgr私有方法，该方法调用不减少资源计数
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="path"></param>
    /// <param name="isDel">引用为0时，是否移除该资源</param>
    /// <param name="callBack"></param>
    private void UnLoadAsset_NoSubAssetsCount<T>(string path, bool isDel = false, UnityAction<T> callBack = null)
    {
        string resName = path + "_" + typeof(T).Name;
        if (resDic.ContainsKey(resName))
        {
            ResInfo<T> resInfo = resDic[resName] as ResInfo<T>;
            //resInfo.SubRefCount();
            resInfo.isDel = isDel;
            if (resInfo.asset != null && resInfo.refCount == 0 && resInfo.isDel)//资源已加载结束的状态
            {
                resDic.Remove(resName);
                Resources.UnloadAsset(resInfo.asset as UnityEngine.Object);
            }
            else if (resInfo.asset == null)//资源异步加载中的状态
            {
                //把待移除状态改为true,等异步加载完成再开始卸载
                //resInfo.isDel = true;

                if (callBack != null)
                    resInfo.callback -= callBack;
            }
        }
    }

    /// <summary>
    /// 卸载指定资源 (type)
    /// </summary>
    /// <param name="path"></param>
    /// <param name="type"></param>
    /// <param name="isDel">引用为0时，是否移除该资源</param>
    /// <param name="callBack"></param>
    public void UnLoadAsset(string path, Type type, bool isDel = false, UnityAction<UnityEngine.Object> callBack = null)
    {
        string resName = path + "_" + type.Name;
        if (resDic.ContainsKey(resName))
        {
            ResInfo<UnityEngine.Object> resInfo = resDic[resName] as ResInfo<UnityEngine.Object>;
            resInfo.SubRefCount();
            resInfo.isDel = isDel;
            if (resInfo.asset != null && resInfo.refCount == 0)//资源已加载结束的状态
            {
                resDic.Remove(resName);
                Resources.UnloadAsset(resInfo.asset);
            }
            else if (resInfo.asset == null)//资源异步加载中的状态
            {
                //resInfo.isDel = true;
                if (callBack != null)
                    resInfo.callback -= callBack;
            }
        }
    }
    /// <summary>
    ///  ResMgr私有方法，该方法调用不减少资源计数
    /// </summary>
    /// <param name="path"></param>
    /// <param name="type"></param>
    /// <param name="isDel">引用为0时，是否移除该资源</param>
    /// <param name="callBack"></param>
    private void UnLoadAsset_NoSubAssetsCount(string path, Type type, bool isDel = false, UnityAction<UnityEngine.Object> callBack = null)
    {
        string resName = path + "_" + type.Name;
        if (resDic.ContainsKey(resName))
        {
            ResInfo<UnityEngine.Object> resInfo = resDic[resName] as ResInfo<UnityEngine.Object>;
            //resInfo.SubRefCount();
            resInfo.isDel = isDel;
            if (resInfo.asset != null && resInfo.refCount == 0)//资源已加载结束的状态
            {
                resDic.Remove(resName);
                Resources.UnloadAsset(resInfo.asset);
            }
            else if (resInfo.asset == null)//资源异步加载中的状态
            {
                //resInfo.isDel = true;
                if (callBack != null)
                    resInfo.callback -= callBack;
            }
        }
    }

    /// <summary>
    /// 卸载没有使用的资源
    /// </summary>
    /// <param name="callback"></param>
    public void UnLoadUnUseAssets(UnityAction callback = null)
    {
        StartCoroutine(IE_UnLoadUnUseAssets(callback));
    }
    private IEnumerator IE_UnLoadUnUseAssets(UnityAction callback = null)
    {
        List<string> tempList = new List<string>();
        foreach (string path in resDic.Keys)
        {
            if (resDic[path].refCount == 0)
            {
                tempList.Add(path);
            }
        }
        foreach (string path in tempList)
        {
            resDic.Remove(path);
        }

        AsyncOperation ao = Resources.UnloadUnusedAssets();
        yield return ao;
        callback?.Invoke();
    }
    /// <summary>
    /// 获取资源的引用计数
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="path"></param>
    /// <returns></returns>
    public int GetAssetsRefCount<T>(string path)
    {
        string resName = path + "_" + typeof(T).Name;
        if (resDic.ContainsKey(resName))
        {
            return (resDic[resName] as ResInfo<T>).refCount;
        }
        return 0;
    }
    /// <summary>
    /// 清空所有资源记录，并卸载没有使用的资源
    /// </summary>
    /// <param name="callBack"></param>
    public void ClearDic(UnityAction callBack = null)
    {
        resDic.Clear();
        StartCoroutine(ReallyClearDic(callBack));
    }

    private IEnumerator ReallyClearDic(UnityAction callBack)
    {
        AsyncOperation ao = Resources.UnloadUnusedAssets();
        yield return ao;
        callBack?.Invoke();
    }
}
