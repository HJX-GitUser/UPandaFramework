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
    /// <param name="obj"></param>
    /// <returns></returns>
    public static T GetOrAddComponent<T>(this GameObject obj) where T : Component
    {
        T t = obj.GetComponent<T>();
        if (t == null) t = obj.AddComponent<T>();
        return t;
    }
}
