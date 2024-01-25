using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 继承了MonoBehavior的单例基类
/// 过场景会移除,用的时候生成
/// 这个可以也自己手动管理挂载到场景上,
/// </summary>
/// <typeparam name="T"></typeparam>
public class BaseMonoSingleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T Instance
    {
        get
        {
            if(instance == null)
            {
                GameObject obj= new GameObject(typeof(T).Name);
                instance = obj.AddComponent<T>();
            }
            return instance;
        }
    }

    protected virtual void Awake()
    {
        instance = this as T;
    }
    public void OnDestroy()
    {
        instance = null;
    }
}
