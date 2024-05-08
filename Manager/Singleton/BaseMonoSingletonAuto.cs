using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 自动生成到场景的单例基类
/// 需要时直接用
/// 继承这种单例模式可以让其一直存在在场景中
/// </summary>
/// <typeparam name="T"></typeparam>
public class BaseMonoSingletonAuto<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T instance;
    public static T Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject obj = new GameObject();
                //设置脚本挂载对象脚本名
                obj.name = typeof(T).Name;
                DontDestroyOnLoad(obj);
                instance = obj.AddComponent<T>();

            }
            return instance;
        }
    }
    protected virtual void Awake()
    {
        instance = this as T;
        DontDestroyOnLoad(instance);
    }
}
