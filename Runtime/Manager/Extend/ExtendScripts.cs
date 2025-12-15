using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

    /// <summary>
    /// 通过反射添加热更组件
    /// </summary>
    /// <param name="gameObject">要添加组件的GameObject</param>
    /// <param name="hotfixTypeName">热更类型全名（包含命名空间）</param>
    /// <returns>添加的组件</returns>
    public static Component AddHotFixComponent(this GameObject gameObject, Assembly hotfixAssembly, string hotfixTypeName)
    {
        if (gameObject == null)
        {
            Debug.LogError("GameObject不能为空");
            return null;
        }

        // 1. 查找热更程序集
        if (hotfixAssembly == null)
        {
            Debug.LogError("未找到热更程序集");
            return null;
        }

        // 2. 获取热更类型
        Type hotfixType = hotfixAssembly.GetType(hotfixTypeName);
        if (hotfixType == null)
        {
            Debug.LogError($"未找到热更类型: {hotfixTypeName}");
            return null;
        }

        // 3. 检查是否是Component类型
        if (!typeof(Component).IsAssignableFrom(hotfixType))
        {
            Debug.LogError($"{hotfixTypeName} 不是Component类型");
            return null;
        }

        // 4. 通过反射调用AddComponent
        MethodInfo addComponentMethod = typeof(GameObject).GetMethod("AddComponent",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new Type[] { },
            null);

        if (addComponentMethod == null)
        {
            Debug.LogError("找不到AddComponent方法");
            return null;
        }

        // 5. 调用泛型方法
        MethodInfo genericAddComponent = addComponentMethod.MakeGenericMethod(hotfixType);
        Component component = genericAddComponent.Invoke(gameObject, null) as Component;

        Debug.Log($"成功添加热更组件: {hotfixTypeName}");
        return component;
    }
}
