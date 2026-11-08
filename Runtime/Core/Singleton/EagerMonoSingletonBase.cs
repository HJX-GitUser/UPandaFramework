using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{

/// <summary>
/// 「饿汉」命名的 MonoBehaviour 单例基类。
/// <para>注意：实际行为是**懒加载** —— 首次访问 <see cref="Instance"/> 时才创建实例
/// （原先模拟饿汉的预热方法存在致命副作用，已删除，原因见其保留的注释）。
/// 与 <see cref="LazyMonoSingletonBase{T}"/> 的差别仅在于创建路径是否加锁。</para>
/// </summary>
/// <typeparam name="T">单例类型，必须是自己：<c>class X : EagerMonoSingletonBase&lt;X&gt;</c></typeparam>
[DisallowMultipleComponent]
public abstract class EagerMonoSingletonBase<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _applicationIsQuitting = false;
    private static int _sessionId;
    static EagerMonoSingletonBase() { }

    /// <summary>
    /// 跨播放会话时丢弃上一次运行残留的静态状态。
    /// <para>
    /// 关闭 Project Settings &gt; Editor &gt; Enter Play Mode Options 里的 Domain Reload 后，
    /// 静态字段会保留上一次运行的取值，<see cref="_applicationIsQuitting"/> 残留 true
    /// 会让 <see cref="Instance"/> 恒返回 null。
    /// 重置钩子放在非泛型的 <see cref="SingletonSession"/> 上，是因为泛型类里挂
    /// <c>[RuntimeInitializeOnLoadMethod]</c> 不会被 Unity 调用（收集机制不返回泛型类型定义上的方法）。
    /// 这里只在会话号变化时做一次比较与丢弃。
    /// </para>
    /// </summary>
    private static void EnsureSession()
    {
        if (_sessionId == SingletonSession.Id) return;
        _sessionId = SingletonSession.Id;
        _instance = null;
        _applicationIsQuitting = false;
    }

    /// <summary>
    /// 单例实例
    /// </summary>
    public static T Instance
    {
        get
        {
            EnsureSession();

            if (_applicationIsQuitting)
            {
                Debug.LogWarning($"[{typeof(T)}] 实例已在应用程序退出时被销毁，返回null。");
                return null;
            }

            if (_instance == null)
            {
                // 在场景中查找是否已存在实例。注意：只搜索**已激活**对象，
                // 挂在 inactive 物体上的实例会被忽略（2022.2+ 用 FindAnyObjectByType，旧版本用 FindObjectOfType）。
#if UNITY_2022_2_OR_NEWER
                _instance = FindAnyObjectByType<T>();
#else
                _instance = FindObjectOfType<T>();
#endif

                if (_instance == null)
                {
                    // 编辑模式下不自动创建：否则点一下编辑器工具就会往当前场景里塞一个
                    // XXX_EagerSingleton 空对象（DontDestroyOnLoad 还会报错）。
                    // 能在场景里找到实例时，编辑模式下仍照常返回。
                    if (!Application.isPlaying)
                    {
                        Debug.LogWarning($"[{typeof(T)}] 编辑模式下找不到场景实例，Instance 返回 null（不自动创建）。");
                        return null;
                    }

                    GameObject singletonObject = new GameObject();
                    _instance = singletonObject.AddComponent<T>();
                    singletonObject.name = typeof(T).ToString() + "_EagerSingleton";
                    Debug.Log($"[{typeof(T)}] 创建单例实例(Eager)");
                }
                else
                {
                    Debug.Log($"[{typeof(T)}] 使用场景中已存在的实例");
                }

                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(_instance.gameObject);
                }
            }
            return _instance;
        }
    }

    // 这里曾有一份 [RuntimeInitializeOnLoadMethod(BeforeSceneLoad)] 预热，用于模拟饿汉模式。
    // 已删除，且**不要恢复**——它会造成事故：
    //   BeforeSceneLoad 阶段会先创建一个没有 Inspector 配置的空实例并 DontDestroyOnLoad，
    //   场景加载后场景里那个「带配置的正主」在 Awake 时发现 _instance 已被占用 → 被 Destroy，
    //   最终留下空壳、销毁正主（UPGameRoot 的 config / logConsole 等序列化引用全部丢失）。

    private void Awake()
    {
        EnsureSession();

        // 重复实例：只销毁自己，**绝不执行 OnAwake()**。
        // 历史事故（2026-09-24 修复）：旧实现在 Destroy(gameObject) 之后仍无条件调用 OnAwake()，
        // 而 Destroy 要到帧末才生效，于是重复实例的初始化照跑 —— UPGameRoot 的 InitAsync await 链
        // 会跑完并二次广播 GFLoadedEvent，表现为「加载界面 / 首场景流程走两遍 + 一堆 MissingReference」。
        // 当时靠 UPGameRoot.OnAwake 里的 `if (Instance != this) return;` 打补丁，现已移除。
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning($"[{typeof(T)}] 检测到重复实例，销毁新实例");
            Destroy(gameObject);
            return;
        }

        if (_instance == null)
        {
            T self = this as T;
            if (self == null)
            {
                // 泛型参数写错的情形，如 class Y : EagerMonoSingletonBase<Z>。
                // 不拦住的话 _instance 恒为 null → 每次访问 Instance 都会新建一个 GameObject。
                Debug.LogError($"[{typeof(T)}] 类型不匹配：{GetType().Name} 不是 {typeof(T).Name}，单例未建立。");
                return;
            }
            _instance = self;
            DontDestroyOnLoad(gameObject);
            Debug.Log($"[{typeof(T)}] EagerMonoSingleton Awake初始化");
        }

        OnAwake();
    }

    /// <summary>
    /// 退出流程（**密封**：子类重写后一旦忘调 base，单例清理就会静默失效）。
    /// 子类请改用 <see cref="OnApplicationQuitting"/>。
    /// </summary>
    private void OnApplicationQuit()
    {
        _applicationIsQuitting = true;
        OnApplicationQuitting();
    }

    /// <summary>应用退出时的钩子，替代重写 <c>OnApplicationQuit</c></summary>
    protected virtual void OnApplicationQuitting() { }

    /// <summary>
    /// 销毁流程（**密封**，理由同上）。顺序刻意是「先跑子类钩子、再清 <see cref="_instance"/>」，
    /// 这样子类清理期间访问 <see cref="Instance"/> 仍能拿到自己（与改造前行为一致）。
    /// 子类请改用 <see cref="OnDestroyed"/>。
    /// </summary>
    private void OnDestroy()
    {
        OnDestroyed();
        if (_instance == this)
        {
            _instance = null;
        }
    }

    /// <summary>被销毁时的钩子，替代重写 <c>OnDestroy</c></summary>
    protected virtual void OnDestroyed() { }

    /// <summary>Awake 完成后的钩子（子类唯一的初始化入口）</summary>
    protected virtual void OnAwake() { }

}

}