using UnityEngine;

/// <summary>
/// 全局「播放会话号」，每次进入播放模式（SubsystemRegistration 阶段）自增一次。
///
/// <para>为什么需要这个中间类：四个单例基类都是**泛型类**，而
/// <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 的收集机制（Editor 走 UnityEditor.TypeCache、
/// 构建期走 IL 扫描）不会返回泛型类型定义上的方法 —— 把重置钩子直接写在
/// <c>EagerMonoSingletonBase&lt;T&gt;</c> 这类泛型类里，它不会被调用，
/// 于是"重置了静态字段"就成了一种虚假的安全感。这里用一个**非泛型**类承接该钩子，
/// 泛型基类再通过比对自己记录的会话号来惰性丢弃残留状态（见各基类的 <c>EnsureSession()</c>）。</para>
///
/// <para>解决的问题：在 Project Settings &gt; Editor &gt; Enter Play Mode Options 里关闭
/// Domain Reload 后，静态字段会保留上一次运行的取值，表现为第二次进播放模式时
/// 单例要么恒返回 null（<c>_applicationIsQuitting</c> 残留），要么直接返回上一个会话的
/// 旧实例（<c>_instance</c> 残留）。</para>
/// </summary>
internal static class SingletonSession
{
    private static int s_id;

    /// <summary>当前播放会话号</summary>
    internal static int Id
    {
        get { return s_id; }
    }

    /// <summary>
    /// 进入播放模式时把会话号 +1，让所有泛型单例基类知道「自己的静态状态已过期」。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void AdvanceOnPlayModeEnter()
    {
        s_id++;
    }
}
