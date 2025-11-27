using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 懒汉模式单例基类
/// 第一次访问时才会创建实例
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class LazyMonoSingletonBase<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static readonly object _lock = new object();
    private static bool _applicationIsQuitting = false;

    public static T Instance
    {
        get
        {
            if (_applicationIsQuitting)
            {
                Debug.LogWarning($"[{typeof(T)}] 实例已在应用程序退出时被销毁，返回null。");
                return null;
            }

            lock (_lock)
            {
                if (_instance == null)
                {
                    // 在场景中查找是否已存在实例
                    _instance = FindObjectOfType<T>();

                    if (_instance == null)
                    {
                        // 创建新的GameObject来挂载单例组件
                        GameObject singletonObject = new GameObject();
                        _instance = singletonObject.AddComponent<T>();
                        singletonObject.name = typeof(T).ToString() + "_LazySingleton";

                        // 标记为不销毁，跨场景保持
                        DontDestroyOnLoad(singletonObject);

                        Debug.Log($"[{typeof(T)}] 创建单例实例(Lazy)");
                    }
                    else
                    {
                        Debug.Log($"[{typeof(T)}] 使用场景中已存在的实例");
                    }
                }
                return _instance;
            }
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Debug.LogWarning($"[{typeof(T)}] 存在重复实例，销毁新实例");
            Destroy(gameObject);
        }
        OnAwake();
    }

    protected virtual void OnApplicationQuit()
    {
        _applicationIsQuitting = true;
    }

    protected virtual void OnDestroy()
    {
        if (_instance == this)
        {
            _instance = null;
        }
    }

    protected virtual void OnAwake() { }

}
