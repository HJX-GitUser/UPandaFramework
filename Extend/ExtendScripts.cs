using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 脚本扩展
/// </summary>
public static class ExtendScripts
{
    /// <summary>
    /// 获取组件的扩展方法
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="trs"></param>
    /// <param name="t"></param>
    /// <returns></returns>
    public static T GetOrAddComponent<T>(this GameObject trs) where T : Component
    {
        T t = trs.GetComponent<T>();
        if (t == null) t = trs.AddComponent<T>();
        return t;
    }
}
