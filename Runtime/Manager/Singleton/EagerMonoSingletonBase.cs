using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 饿汉模式单例基类
/// 在类加载时就创建实例
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class EagerMonoSingletonBase<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _applicationIsQuitting = false;
    static EagerMonoSingletonBase() { }

    /// <summary>
    /// 单例实例
    /// </summary>
    public static T Instance
    {
        get
        {
            if (_applicationIsQuitting)
            {
                Debug.LogWarning($"[{typeof(T)}] 实例已在应用程序退出时被销毁，返回null。");
                return null;
            }

            if (_instance == null)
            {
                // 在场景中查找是否已存在实例
                _instance = FindObjectOfType<T>();

                if (_instance == null)
                {
                    // 创建新的GameObject来挂载单例组件
                    GameObject singletonObject = new GameObject();
                    _instance = singletonObject.AddComponent<T>();
                    singletonObject.name = typeof(T).ToString() + "_EagerSingleton";

                    // 立即初始化
                    DontDestroyOnLoad(singletonObject);
                    Debug.Log($"[{typeof(T)}] 创建单例实例(Eager)");
                }
                else
                {
                    Debug.Log($"[{typeof(T)}] 使用场景中已存在的实例");
                }
            }
            return _instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeOnLoad()
    {
        // 在场景加载前强制初始化实例（模拟饿汉模式）
        if (_instance == null)
        {
            // 触发Instance属性的getter来创建实例
            var temp = Instance;
            if (temp != null)
            {
                Debug.Log($"[{typeof(T)}] EagerMonoSingleton预初始化完成");
            }
        }
    }

    protected virtual void Awake()
    {
        if (_instance == null)
        {
            _instance = this as T;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[{typeof(T)}] EagerMonoSingleton Awake初始化");
        }
        else if (_instance != this)
        {
            Debug.LogWarning($"[{typeof(T)}] 检测到重复实例，销毁新实例");
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
