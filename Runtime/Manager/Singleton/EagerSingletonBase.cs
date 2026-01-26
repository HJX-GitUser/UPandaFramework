using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class EagerSingletonBase<T> where T : class, new()
{
    private static readonly T _instance = new T();
    private static bool _isDisposed = false;

    /// <summary>
    /// 单例实例
    /// </summary>
    public static T Instance
    {
        get
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(typeof(T).Name, "单例实例已被释放");
            }
            return _instance;
        }
    }

    /// <summary>
    /// 静态构造函数，确保线程安全的延迟初始化
    /// </summary>
    static EagerSingletonBase()
    {
        Debug.Log($"[EagerSingleton] 预创建 {typeof(T).Name} 实例");
    }

    /// <summary>
    /// 保护构造函数
    /// </summary>
    protected EagerSingletonBase()
    {
        OnInit();
    }
    protected virtual void OnInit() { }

    /// <summary>
    /// 释放单例实例
    /// </summary>
    public static void Release()
    {
        if (_instance is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _isDisposed = true;
        Console.WriteLine($"[EagerSingleton] 标记释放 {typeof(T).Name} 实例");
    }
}
