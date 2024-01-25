using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 对象池中的容器类，把对象都分好类，方便看
/// </summary>
public class PoolData
{
    public GameObject fatherObj;//容器的父对象
    public List<GameObject> poolList;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="obj">需要存入的对象</param>
    /// <param name="poolObj">对象池</param>
    public PoolData(GameObject obj, GameObject poolObj)
    {
        fatherObj = new GameObject(obj.name + "_F");
        fatherObj.transform.SetParent(poolObj.transform);
        poolList = new List<GameObject>();
        PushObj(obj);
    }

    /// <summary>
    /// 往容器里放对象
    /// </summary>
    /// <param name="obj"></param>
    public void PushObj(GameObject obj)
    {
        obj.SetActive(false);
        poolList.Add(obj);
        obj.transform.SetParent(fatherObj.transform);
    }

    /// <summary>
    /// 从容器里取出对象
    /// </summary>
    /// <returns></returns>
    public GameObject GetObj()
    {
        GameObject obj = null;
        obj = poolList[0];
        poolList.RemoveAt(0);
        obj.SetActive(true);
        obj.transform.parent = null;
        return obj;
    }
}

/// <summary>
/// 对象池模块
/// </summary>
public class PoolMgr : BaseSingleton<PoolMgr>
{
    //对象池容器
    public Dictionary<string, PoolData> poolDic = new Dictionary<string, PoolData>();
    //对象池Obj
    private GameObject poolObj;
    /// <summary>
    /// 取对象
    /// </summary>
    /// <param name="name"></param>
    /// <param name="callback"></param>
    public void GetObj(string name, UnityAction<GameObject> callback)
    {
        if (poolDic.ContainsKey(name) && poolDic[name].poolList.Count > 0)
        {
            callback(poolDic[name].GetObj());
        }
        else
        {
            //通过异步加载资源 创建对象给外部用
            //这里也可以改成用AB包加载
            ResMgr.Instance.LoadAsync<GameObject>(name, (obj) =>
            {
                obj.name = name;
                callback(obj);
            });
        }
    }
    /// <summary>
    /// 往对象池放对象
    /// </summary>
    /// <param name="name">对象池内容器的名字</param>
    /// <param name="obj"></param>
    public void PushObj(string name,GameObject obj)
    {
        if(poolObj == null) poolObj = new GameObject("Pool");
        //对象池里有该容器
        if(poolDic.ContainsKey(name))
        {
            poolDic[name].PushObj(obj);
        }
        //对象池里没有有该容器
        else
        {
            poolDic.Add(name, new PoolData(obj, poolObj));
        }
    }

    /// <summary>
    /// 清空缓存池的方法 
    /// 主要用在 场景切换时
    /// </summary>
    public void Clear()
    {
        poolDic.Clear();
        poolObj = null;
    }
}
