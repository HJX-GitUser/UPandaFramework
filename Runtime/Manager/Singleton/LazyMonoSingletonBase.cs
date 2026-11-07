using UnityEngine;

/// <summary>
/// 懒汉模式单例基类：第一次访问 <see cref="Instance"/> 时才会创建实例。
/// </summary>
/// <typeparam name="T">单例类型，必须是自己：<c>class X : LazyMonoSingletonBase&lt;X&gt;</c></typeparam>
[DisallowMultipleComponent]
public abstract class LazyMonoSingletonBase<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _applicationIsQuitting = false;
    private static int _sessionId;

    /// <summary>
    /// 跨播放会话时丢弃上一次运行残留的静态状态（<see cref="_applicationIsQuitting"/> 残留 true
    /// 会让 <see cref="Instance"/> 恒返回 null）。重置钩子由非泛型的 <see cref="SingletonSession"/>
    /// 提供 —— 泛型类里挂 <c>[RuntimeInitializeOnLoadMethod]</c> 不会被 Unity 调用。
    /// </summary>
    private static void EnsureSession()
    {
        if (_sessionId == SingletonSession.Id) return;
        _sessionId = SingletonSession.Id;
        _instance = null;
        _applicationIsQuitting = false;
    }

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

            // 此处刻意不加锁：FindAnyObjectByType / new GameObject / AddComponent 都只能在主线程调用，
            // 锁保护不了任何东西；且 AddComponent 会同步触发子类 Awake → OnAwake，
            // 等于在锁内执行用户代码（旧实现的重入隐患）。
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
                    // 编辑模式下不自动创建（同 Eager 版：避免编辑器工具往场景里塞 XXX_LazySingleton 空对象）
                    if (!Application.isPlaying)
                    {
                        Debug.LogWarning($"[{typeof(T)}] 编辑模式下找不到场景实例，Instance 返回 null（不自动创建）。");
                        return null;
                    }

                    GameObject singletonObject = new GameObject();
                    _instance = singletonObject.AddComponent<T>();
                    singletonObject.name = typeof(T).ToString() + "_LazySingleton";
                    Debug.Log($"[{typeof(T)}] 创建单例实例(Lazy)");
                }
                else
                {
                    Debug.Log($"[{typeof(T)}] 使用场景中已存在的实例");
                }

                // 标记为不销毁，跨场景保持
                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(_instance.gameObject);
                }
            }
            return _instance;
        }
    }

    private void Awake()
    {
        EnsureSession();

        // 重复实例：只销毁自己，**绝不执行 OnAwake()**（与 Eager 版同样的历史缺陷，2026-09-24 一并修复）。
        if (_instance != null && _instance != this)
        {
            Debug.LogWarning($"[{typeof(T)}] 存在重复实例，销毁新实例");
            Destroy(gameObject);
            return;
        }

        if (_instance == null)
        {
            T self = this as T;
            if (self == null)
            {
                // 泛型参数写错的情形，如 class Y : LazyMonoSingletonBase<Z>。
                // 不拦住的话 _instance 恒为 null → 每次访问 Instance 都会新建一个 GameObject。
                Debug.LogError($"[{typeof(T)}] 类型不匹配：{GetType().Name} 不是 {typeof(T).Name}，单例未建立。");
                return;
            }
            _instance = self;
            DontDestroyOnLoad(gameObject);
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
