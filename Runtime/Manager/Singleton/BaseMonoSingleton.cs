using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 继承了MonoBehavior的单例基类
/// </summary>
/// <typeparam name="T"></typeparam>
public class BaseMonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                CreateInstance();
            }
            return instance;
        }
    }
    private static void CreateInstance()
    {
        // 查找场景中是否已有实例
        if (instance != null) return;

        // 创建新的 GameObject 用于挂载单例
        GameObject obj = new GameObject(typeof(T).Name);
        DontDestroyOnLoad(obj);

        // 添加并初始化单例
        instance = obj.AddComponent<T>();
    }


    // 销毁时清除单例引用，防止内存泄漏
    protected virtual void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    // 提供一个初始化方法来配置特定的 GameObject
    public static void CreateInstance(GameObject existingObject)
    {
        if (instance == null)
        {
            instance = existingObject.AddComponent<T>();
        }
    }

    /// <summary>
    /// 可选的初始化方法，如果有特殊初始化需求，可以覆盖该方法
    /// </summary>
    protected virtual void Initialize() { }


    protected void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this as T;
        DontDestroyOnLoad(gameObject);
        Initialize(); // 调用可选的初始化方法
    }

}
