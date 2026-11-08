using System;
using UnityEngine;

namespace UPandaGF
{

/// <summary>
/// 懒汉模式单例基类：第一次访问 <see cref="Instance"/> 时才创建实例。
/// </summary>
/// <typeparam name="T">单例类型，必须是自己：<c>class X : LazySingletonBase&lt;X&gt;</c></typeparam>
public abstract class LazySingletonBase<T> where T : class, new()
{
    private static T _instance;
    private static readonly object _lockObject = new object();
    private static bool _isInitialized = false;
    private static int _sessionId;

    /// <summary>
    /// 跨播放会话时丢弃上一次运行残留的静态状态。关闭 Domain Reload 后 <see cref="_instance"/>
    /// 的残留尤其危险：第二次进入播放模式会**直接返回上一个会话的旧实例**
    /// （状态脏、还订阅着已销毁的对象），比返回 null 更难排查。
    /// <para>钩子放在非泛型的 <see cref="SingletonSession"/> 上，原因是泛型类里的
    /// <c>[RuntimeInitializeOnLoadMethod]</c> 不会被 Unity 调用。</para>
    /// </summary>
    private static void EnsureSession()
    {
        if (_sessionId == SingletonSession.Id) return;
        _sessionId = SingletonSession.Id;
        _instance = null;
        _isInitialized = false;
    }

    /// <summary>
    /// 单例实例
    /// </summary>
    public static T Instance
    {
        get
        {
            EnsureSession();

            if (!_isInitialized)
            {
                lock (_lockObject)
                {
                    if (!_isInitialized)
                    {
                        _instance = new T();
                        _isInitialized = true;
                        Debug.Log($"[LazySingleton] 创建 {typeof(T).Name} 实例");
                    }
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// 保护构造函数。
    /// <para>⚠ 刻意**不禁止** <c>new T()</c>：事件模块自检
    /// （<c>Editor/CustomInspector/EventCenterModule/EventCenterSelfCheck.cs</c>，菜单
    /// UPandaGF/事件系统/事件模块自检）就是用 <c>new EventCenter()</c> 造独立实例来跑断言的。
    /// 原先这里有一句 <c>if (_isInitialized) throw</c>，会让该自检一旦在播放模式下运行就必然失败
    /// （那时单例已初始化），故删除。另注：<c>where T : class, new()</c> 本身就要求子类提供
    /// public 无参构造函数，从语言层面也禁止不了 <c>new</c>。</para>
    /// <para><see cref="OnInit"/> 在此调用，所以 <c>new T()</c> 得到的是初始化完整的实例，
    /// 独立实例与单例实例行为一致。</para>
    /// </summary>
    protected LazySingletonBase()
    {
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
            Debug.Log($"[LazySingleton] 释放 {typeof(T).Name} 实例");
        }
    }
}

}