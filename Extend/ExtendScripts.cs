using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 脚本扩展
/// </summary>
public static class ExtendScripts
{
    /// <summary>
    /// 给Transform添加的扩展方法，用out参数把组件返回
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="trs"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    public static Transform GetOrAddComponent<T>(this Transform trs,out T t) where T : Component
    {
        t = trs.GetComponent<T>();
        if(t == null) t = trs.gameObject.AddComponent<T>();
        return trs.transform;
    }
}
