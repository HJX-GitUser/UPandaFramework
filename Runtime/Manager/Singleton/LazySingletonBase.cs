using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 懒汉模式单例基类
/// 第一次访问时才会创建实例
/// </summary>
/// <typeparam name="T"></typeparam>
public abstract class LazySingletonBase<T> where T : class, new()
{
    private static T _instance;
    private static readonly object _lockObject = new object();
    private static bool _isInitialized = false;

    /// <summary>
    /// 单例实例
    /// </summary>
    public static T Instance
    {
        get
        {
            if (!_isInitialized)
            {
                lock (_lockObject)
                {
                    if (!_isInitialized)
                    {
                        _instance = new T();
                        _isInitialized = true;
                        Console.WriteLine($"[LazySingleton] 创建 {typeof(T).Name} 实例");
                    }
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 保护构造函数，防止外部实例化
    /// </summary>
    protected LazySingletonBase()
    {
        if (_isInitialized)
        {
            throw new InvalidOperationException($"{typeof(T).Name} 已经是单例，不能重复创建");
        }
        OnInit();
    }

    protected virtual void OnInit() { }
    /// <summary>
    /// 释放单例实例
    /// </summary>
    public static void Release()
    {
        lock (_lockObject)
        {
            if (_instance is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _instance = null;
            _isInitialized = false;
            Console.WriteLine($"[LazySingleton] 释放 {typeof(T).Name} 实例");
        }
    }
}
