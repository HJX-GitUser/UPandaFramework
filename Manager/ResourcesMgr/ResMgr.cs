using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Resources加载方案
/// </summary>
public class ResMgr : BaseMonoSingletonAuto<ResMgr>
{
    //同步加载资源
    public T Load<T>(string path) where T : UnityEngine.Object
    {
        T res = Resources.Load<T>(path);
        //如果对象是一个GameObject类型的 我把他实例化后 再返回出去 外部 直接使用即可
        if (res is GameObject)
            return GameObject.Instantiate(res);
        else
            return res;
    }
    /// <summary>
    /// Type同步加载资源
    /// </summary>
    /// <param name="path"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public UnityEngine.Object Load(string path, System.Type type)
    {
        UnityEngine.Object res = Resources.Load(path, type);
        if(res is GameObject)
            return GameObject.Instantiate(res);
        else
            return res;
    }
    //异步加载资源
    public void LoadAsync<T>(string name, UnityAction<T> callback) where T : UnityEngine.Object
    {
        StartCoroutine(ReallyLoadAsync(name, callback));
    }

    private IEnumerator ReallyLoadAsync<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object
    {
        ResourceRequest r = Resources.LoadAsync<T>(path);
        yield return r;
        if (r.asset is GameObject)
            callback(GameObject.Instantiate(r.asset) as T);
        else
            callback(r.asset as T);
    }
    /// <summary>
    /// Type异步加载资源
    /// </summary>
    /// <param name="name"></param>
    /// <param name="callback"></param>
    public void LoadAsync(string name,UnityAction<UnityEngine.Object> callback)
    {
        StartCoroutine(ReallyTypeLoadAsync(name, callback));
    }
    private IEnumerator ReallyTypeLoadAsync(string path, UnityAction<UnityEngine.Object> callback)
    {
        ResourceRequest r = Resources.LoadAsync(path);
        yield return r;
        if (r.asset is GameObject)
            callback(GameObject.Instantiate(r.asset));
        else
            callback(r.asset);
    }
}
