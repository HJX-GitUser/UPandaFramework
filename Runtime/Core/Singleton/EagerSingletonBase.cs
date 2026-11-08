using System;
using UnityEngine;

namespace UPandaGF
{

public abstract class EagerSingletonBase<T> where T : class, new()
{
    private static T _instance = new T();
    private static int _sessionId;

    /// <summary>
    /// 跨播放会话时丢弃上一次运行残留的静态状态。
    /// <para>
    /// 关闭 Project Settings &gt; Editor &gt; Enter Play Mode Options 里的 Domain Reload 后，
    /// 静态字段会保留上一次运行的取值，<see cref="_instance"/> 会继续指向上一个会话的旧对象
    /// （内部状态、持有的资源均已失效）。这里直接丢弃引用，下次访问时重建。
    /// 刻意不在此处 Dispose：会话切换时执行用户代码（<see cref="Release"/> 那条路径）
    /// 可能反过来访问 <see cref="Instance"/>，风险大于收益。
    /// </para>
    /// <para>钩子放在非泛型的 <see cref="SingletonSession"/> 上，原因是泛型类里的
    /// <c>[RuntimeInitializeOnLoadMethod]</c> 不会被 Unity 调用。</para>
    /// </summary>
    private static void EnsureSession()
    {
        if (_sessionId == SingletonSession.Id) return;
        _sessionId = SingletonSession.Id;
        _instance = null;
    }

    /// <summary>
    /// 单例实例。首次访问类型时创建；<see cref="Release"/> 之后再次访问会重建。
    /// </summary>
    public static T Instance
    {
        get
        {
            EnsureSession();

            if (_instance == null)
            {
                // 构造函数里会调用 OnInit，因此这里拿到的一定是初始化完整的实例
                _instance = new T();
                Debug.Log($"[EagerSingleton] 创建 {typeof(T).Name} 实例");
            }
            return _instance;
        }
    }

    /// <summary>
    /// 静态构造函数（本质是钉住初始化时机）。
    /// <para>它自己不创建实例，作用是抑制 <c>beforefieldinit</c> 优化：若去掉，CLR 可以在程序集
    /// 加载阶段就初始化 <see cref="_instance"/>，等于在不可预期的时机提前执行 <see cref="OnInit"/>。
    /// 保留它才能把时机钉在「首次访问本类型时」。</para>
    /// </summary>
    static EagerSingletonBase()
    {
        Debug.Log($"[EagerSingleton] 初始化 {typeof(T).Name}");
    }

    /// <summary>
    /// 保护构造函数。<see cref="OnInit"/> 在此调用，因此 <c>new T()</c> 得到的永远是
    /// 初始化完整的实例（Editor 自检工具依赖这一点）。
    /// </summary>
    protected EagerSingletonBase()
    {
        OnInit();
    }

    protected virtual void OnInit() { }

    /// <summary>
    /// 释放单例实例：释放后置空，下次访问 <see cref="Instance"/> 会重建
    /// （与 <see cref="LazySingletonBase{T}.Release"/> 保持一致，避免"一调用就永久报废"）。
    /// </summary>
    public static void Release()
    {
        if (_instance is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _instance = null;
        Debug.Log($"[EagerSingleton] 释放 {typeof(T).Name} 实例");
    }
}

}