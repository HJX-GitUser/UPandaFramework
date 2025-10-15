using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单例模式基类
/// </summary>
/// <typeparam name="T"></typeparam>
public class BaseSingleton<T> where T : new()
{
    private static T instance;
    private static readonly object lockObj = new object();

    public static T Instance
    {
        get
        {
            // 双重检查锁定
            // 第一次检查，如果实例已经创建，直接返回
            if (instance == null)
            {
                lock (lockObj) // 锁住确保线程安全
                {
                    // 第二次检查，避免多次创建实例
                    if (instance == null)
                    {
                        instance = new T();
                    }
                }
            }
            return instance;
        }
    }
}
