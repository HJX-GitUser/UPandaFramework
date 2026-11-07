# 单例基类

| 基类 | 获取和释放 | 使用场景 |
| --- | --- | --- |
| [EagerMonoSingletonBase<T>](EagerMonoSingletonBase.cs) | 场景组件在 Awake 登记。访问 `Instance` 时会先找已激活的组件，Play 模式找不到时创建并保持跨场景。 | 需要依靠场景完成装配的 `UPGameRoot`，以及帧驱动 `PublicMono`。 |
| [LazyMonoSingletonBase<T>](LazyMonoSingletonBase.cs) | 同样优先找到已激活组件，缺失时可在 Play 模式首次访问创建。 | 按需使用的 Unity 组件，如 `ResourcesLoader`、`ABLoadMgr`。 |
| [EagerSingletonBase<T>](EagerSingletonBase.cs) | 在 CLR 类型初始化时创建实例。`Release()` 清空后，下次读取会重建。 | 无需挂到场景的 C# 对象。 |
| [LazySingletonBase<T>](LazySingletonBase.cs) | 首次读取时按需创建并缓存。`Release()` 会调用 `IDisposable.Dispose()`，清空后可重新创建。 | `EventCenter`、`BinaryDataMgr` 等 C# 管理器。 |

## MonoBehaviour 接入

泛型参数传入组件本身。子类将启动、销毁和退出逻辑放在 `OnAwake()`、`OnDestroyed()`、`OnApplicationQuitting()`。基类的 Unity 回调先处理唯一实例、常驻与退出标记，再调用这些钩子。避免在子类声明同名的 Unity 回调覆盖基类消息入口。所有 Unity 对象操作以及 Mono 单例的访问都在主线程进行。

```csharp
public sealed class RuntimeHost : LazyMonoSingletonBase<RuntimeHost>
{
    protected override void OnAwake()
    {
        // 建立当前实例的依赖。
    }

    protected override void OnDestroyed()
    {
        // 归还当前实例持有的资源。
    }
}
```

编辑模式下访问 `Instance` 不会创建对象。Play 模式访问时仅查找已激活实例；需要 Inspector 引用的根节点应放在首场景中，并在其载入后再从业务访问。否则可能先创建默认实例，场景对象随后被判为重复并销毁。重复实例会销毁整个 GameObject，其他重要组件应避免挂在可重复的单例对象上。`DontDestroyOnLoad` 需要根 GameObject。已处于其他常驻根下面的子物体由其父根维持生命周期。 [根节点启动](../GameRoot/README.md)

## 纯 C# 对象与 Play 会话

纯 C# 基类构造时会调用虚拟 `OnInit()`，初始化逻辑应避免依赖派生类构造函数已经执行，也应避免构造期间再次请求自身单例。惰性基类为创建和释放加锁；业务状态及 Unity API 仍需遵循各模块线程约定。管理对象实现 `IDisposable` 时，`Release()` 会先释放该实例再清空缓存。

`SingletonSession` 在每次进入 Play 时更新会话号。各基类在访问或登记时比较编号，丢弃上次会话的静态状态，使禁用 Domain Reload 的工作流也能重新取用。会话切换时丢弃引用不会替业务执行资源释放。重建应用根节点应先等待旧根的 `ShutdownAsync()` 再销毁和创建新根，确保共享服务的清理已经结束。 [会话实现](SingletonSession.cs) · [根关闭测试](../../../Tests/PlayMode/P0RegressionTests.cs)
