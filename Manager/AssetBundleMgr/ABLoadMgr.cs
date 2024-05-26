using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// AB包加载管理器
/// </summary>
public class ABLoadMgr : BaseMonoSingletonAuto<ABLoadMgr>
{
    //主包
    private AssetBundle mainAB = null;
    //主包依赖获取配置文件
    private AssetBundleManifest manifest = null;

    //选择存储 AB包的容器
    //AB包不能够重复加载 否则会报错
    //字典用来存储 AB包对象
    private Dictionary<string, AssetBundle> abDic = new Dictionary<string, AssetBundle>();

    /// <summary>
    /// 获取AB包加载路径
    /// </summary>
    private string PathUrl
    {
        get
        {
            return Application.streamingAssetsPath + "/";
        }
    }
    /// <summary>
    /// 主包名 根据平台不同 包名不同
    /// </summary>
    private string MainName
    {
        get
        {
#if UNITY_ANDROID
            return "Android";
#endif
            return "PC";
        }
    }

    /// <summary>
    /// 加载主包 和 配置文件
    /// 因为加载所有包是 通过它才能得到依赖信息
    /// </summary>
    private void LoadMainAB()
    {
        if (mainAB == null)
        {
            mainAB = AssetBundle.LoadFromFile(PathUrl + MainName);
            manifest = mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
        }
    }

    /// <summary>
    /// 加载指定包的依赖包
    /// </summary>
    /// <param name="abName"></param>
    private void LoadDependencies(string abName)
    {
        //加载主包
        LoadMainAB();
        //获取依赖包
        string[] strs = manifest.GetAllDependencies(abName);
        for (int i = 0; i < strs.Length; i++)
        {
            if (!abDic.ContainsKey(strs[i]))
            {
                AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + strs[i]);
                abDic.Add(strs[i], ab);
            }
        }
    }

    #region 同步加载 (不再支持同步加载)
    ///// <summary>
    ///// 泛型资源同步加载
    ///// </summary>
    ///// <typeparam name="T"></typeparam>
    ///// <param name="abName">ab包名</param>
    ///// <param name="resName">资源名</param>
    ///// <returns></returns>
    //public T LoadRes<T>(string abName, string resName) where T : Object
    //{
    //    //加载依赖包
    //    LoadDependencies(abName);
    //    //加载目标包
    //    if (!abDic.ContainsKey(abName))
    //    {
    //        AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + abName);
    //        abDic.Add(abName, ab);
    //    }
    //    //得到加载出来的资源
    //    T obj = abDic[abName].LoadAsset<T>(resName);
    //    //如果是GameObject 因为GameObject 100%都是需要实例化的
    //    //所以直接实例化
    //    if (obj is GameObject)
    //        return Instantiate(obj);
    //    else
    //        return obj;
    //}
    ///// <summary>
    /////  Type同步加载指定资源
    /////  这个主要是给Lua脚本调用
    ///// </summary>
    ///// <param name="abName"></param>
    ///// <param name="resName"></param>
    ///// <param name="type"></param>
    ///// <returns></returns>
    //public Object LoadRes(string abName, string resName, System.Type type)
    //{
    //    //加载依赖包
    //    LoadDependencies(abName);
    //    //加载目标包
    //    if (!abDic.ContainsKey(abName))
    //    {
    //        AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + abName);
    //        abDic.Add(abName, ab);
    //    }
    //    //得到加载出来的资源
    //    Object obj = abDic[abName].LoadAsset(resName, type);
    //    if (obj is GameObject)
    //        return Instantiate(obj);
    //    else
    //        return obj;

    //}
    ///// <summary>
    ///// 名字 同步加载指定资源
    ///// </summary>
    ///// <param name="abName"></param>
    ///// <param name="resName"></param>
    ///// <returns></returns>
    //public Object LoadRes(string abName, string resName)
    //{
    //    //加载依赖包
    //    LoadDependencies(abName);
    //    //加载目标包
    //    if (!abDic.ContainsKey(abName))
    //    {
    //        AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + abName);
    //        abDic.Add(abName, ab);
    //    }

    //    //得到加载出来的资源
    //    Object obj = abDic[abName].LoadAsset(resName);
    //    //如果是GameObject 因为GameObject 100%都是需要实例化的
    //    //所以我们直接实例化
    //    if (obj is GameObject)
    //        return Instantiate(obj);
    //    else
    //        return obj;
    //}
    #endregion


    #region 异步加载
    /// <summary>
    /// 泛型异步加载资源
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="abName"></param>
    /// <param name="resName"></param>
    /// <param name="callBack"></param>
    public void LoadResAsync<T>(string abName, string resName, UnityAction<T> callBack) where T : Object
    {
        StartCoroutine(ReallyLoadResAsync<T>(abName, resName, callBack));
    }
    private IEnumerator ReallyLoadResAsync<T>(string abName, string resName, UnityAction<T> callBack) where T : Object
    {
        //加载依赖包
        //加载主包
        LoadMainAB();
        //获取依赖包
        string[] strs = manifest.GetAllDependencies(abName);
        for (int i = 0; i < strs.Length; i++)
        {
            if (!abDic.ContainsKey(strs[i]))
            {
                abDic.Add(strs[i], null);
                //AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + strs[i]);
                //abDic.Add(strs[i], ab);
                AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(PathUrl + strs[i]);
                yield return req;

                abDic[strs[i]] = req.assetBundle;
            }
            else
            {
                while (abDic[strs[i]] == null)//如果为null说明资源正在异步加载中
                {
                    yield return 0;
                }
            }
        }


        //加载目标包
        if (!abDic.ContainsKey(abName))
        {
            abDic.Add(abName, null);
            //AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + abName);
            AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(PathUrl + abName);
            yield return req;

            abDic[abName] = req.assetBundle;
        }
        else
        {
            while (abDic[abName] == null)
            {
                yield return 0;
            }
        }
        AssetBundleRequest abq = abDic[abName].LoadAssetAsync<T>(resName);
        yield return abq;
        callBack(abq.asset as T);
    }
    /// <summary>
    /// Type异步加载资源
    /// </summary>
    /// <param name="abName"></param>
    /// <param name="resName"></param>
    /// <param name="type"></param>
    /// <param name="callBack"></param>
    public void LoadResAsync(string abName, string resName, System.Type type, UnityAction<Object> callBack)
    {
        StartCoroutine(ReallyLoadResAsync(abName, resName, type, callBack));
    }
    private IEnumerator ReallyLoadResAsync(string abName, string resName, System.Type type, UnityAction<Object> callBack)
    {
        //加载依赖包
        //加载主包
        LoadMainAB();
        //获取依赖包
        string[] strs = manifest.GetAllDependencies(abName);
        for (int i = 0; i < strs.Length; i++)
        {
            if (!abDic.ContainsKey(strs[i]))
            {
                abDic.Add(strs[i], null);
                //AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + strs[i]);
                //abDic.Add(strs[i], ab);
                AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(PathUrl + strs[i]);
                yield return req;

                abDic[strs[i]] = req.assetBundle;
            }
            else
            {
                while (abDic[strs[i]] == null)//如果为null说明资源正在异步加载中
                {
                    yield return 0;
                }
            }
        }
        //加载目标包
        if (!abDic.ContainsKey(abName))
        {
            abDic.Add(abName, null);
            //AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + abName);
            AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(PathUrl + abName);
            yield return req;

            abDic[abName] = req.assetBundle;
        }
        else
        {
            while (abDic[abName] == null)
            {
                yield return 0;
            }
        }
        //异步加载包中资源
        AssetBundleRequest abq = abDic[abName].LoadAssetAsync(resName, type);
        yield return abq;
        callBack(abq.asset);
    }
    /// <summary>
    /// 名字 异步加载 指定资源
    /// </summary>
    /// <param name="abName"></param>
    /// <param name="resName"></param>
    /// <param name="callBack"></param>
    public void LoadResAsync(string abName, string resName, UnityAction<Object> callBack)
    {
        StartCoroutine(ReallyLoadResAsync(abName, resName, callBack));
    }

    private IEnumerator ReallyLoadResAsync(string abName, string resName, UnityAction<Object> callBack)
    {
        //加载依赖包
        //加载主包
        LoadMainAB();
        //获取依赖包
        string[] strs = manifest.GetAllDependencies(abName);
        for (int i = 0; i < strs.Length; i++)
        {
            if (!abDic.ContainsKey(strs[i]))
            {
                abDic.Add(strs[i], null);
                //AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + strs[i]);
                //abDic.Add(strs[i], ab);
                AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(PathUrl + strs[i]);
                yield return req;

                abDic[strs[i]] = req.assetBundle;
            }
            else
            {
                while (abDic[strs[i]] == null)//如果为null说明资源正在异步加载中
                {
                    yield return 0;
                }
            }
        }
        //加载目标包
        if (!abDic.ContainsKey(abName))
        {
            abDic.Add(abName, null);
            //AssetBundle ab = AssetBundle.LoadFromFile(PathUrl + abName);
            AssetBundleCreateRequest req = AssetBundle.LoadFromFileAsync(PathUrl + abName);
            yield return req;

            abDic[abName] = req.assetBundle;
        }
        else
        {
            while (abDic[abName] == null)
            {
                yield return 0;
            }
        }
        //异步加载包中资源
        AssetBundleRequest abq = abDic[abName].LoadAssetAsync(resName);
        yield return abq;
        callBack(abq.asset);
    }
    #endregion

    /// <summary>
    /// 卸载AB包的方法
    /// </summary>
    /// <param name="name"></param>
    /// <param name="callbackResult"></param>
    public void UnLoadAB(string name, UnityAction<bool> callbackResult = null)
    {
        if (abDic.ContainsKey(name))
        {
            if (abDic[name] == null)
            {
                Debug.Log("该资源正处于异步加载中，无法卸载！");
                callbackResult?.Invoke(false);
                return;
            }
            abDic[name].Unload(false);
            abDic.Remove(name);
            callbackResult?.Invoke(true);
        }
    }
    /// <summary>
    /// 清空AB包的方法
    /// </summary>
    public void ClearAB()
    {
        StopAllCoroutines();
        AssetBundle.UnloadAllAssetBundles(false);
        abDic.Clear();
        //卸载主包
        mainAB = null;
    }
}
