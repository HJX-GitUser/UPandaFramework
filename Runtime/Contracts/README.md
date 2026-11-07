# 服务契约

`UPandaGF.Contracts` 定义运行时服务的调用接口、场景进度、加载方式和资源租约。该程序集直接使用 Unity 类型，程序集引用表没有其他项目程序集。服务由 `UPGameRoot` 通过 `GameServices` 装配。 [程序集定义](UPandaGF.Contracts.asmdef) · [服务装配](../Manager/Services/README.md)

## 接入

1. 使用 asmdef 的业务程序集添加对 `UPandaGF.Contracts` 的引用，然后在 C# 中引入 `UPandaGF` 命名空间。
2. 让场景组件实现 `IGameServicesConsumer.Configure(IGameServices services)`，保存实际需要的接口。启动完成后，根节点扫描当前场景，以后在场景加载通知中再次扫描。
3. 在组件的 `Configure` 或更晚阶段使用注入的服务。运行中创建的预制体需要由创建方显式注入。需要直接等待框架启动的程序集同时引用 `UPandaGF.Runtime`，并通过 `UPGameRoot.WaitUntilReadyAsync` 等待。

[消费者接口](Services.cs) · [根节点](../Manager/GameRoot/README.md)

## 接口索引

| 接口 | 能力 |
| --- | --- |
| `IAssetService` | `AcquireAsync<T>(path, method, token)`，取得可释放的 Unity 资源租约。 |
| `ISceneService`、`ILifetimeScope` | 串行场景请求、进度、场景作用域和作用域持有物。 |
| `IAudioService`、`ISaveService` | 背景音乐及独占音效，以及按槽位读写存档。叠加音效入口见具体音频管理器。 |
| `IInputService`、`IPauseService`、`IGameClock` | 动作输入、输入上下文、可嵌套暂停和时钟。 |
| `ITimerService` | `Delay`、`Every`、`DelayAsync`，支持 scaled 和 unscaled 时间。 |
| `IConfigService`、`IGameFlowService`、`IAppLifecycle` | 运行期配置、业务流程状态机、应用生命周期通知。 |

`IGameServices` 聚合这些接口。自定义实现和测试替身须实现当前成员；只验证部分功能的替身可以对未涉及的服务返回 `null`。 [通用契约](Services.cs) · [基础契约](FoundationServices.cs) · [音频管理器](../Manager/AudioMgr/README.md)

## 资源持有

`AssetLoadMethod.Resources` 对应相对 Resources 路径，通常省略扩展名。`AssetLoadMethod.AssetBundle` 对应清单中的资源路径，通常使用带扩展名的 `Assets/…` 全路径。应用级的资源模式由根节点配置，决定后一个入口走 AssetBundle 还是在编辑器中直读。 [加载方式](PublicEnum.cs) · [加载器](../Manager/GameRoot/SourcesLoadMgr/AssetsLoader.cs)

`AssetLease<T>` 的每个句柄可重复释放。调用 `Retain()` 可获得独立句柄，最后一份句柄释放时执行加载器的归还动作。读取资源、保留和释放都应在 Unity 主线程完成。底层找不到资源时租约可以为空；使用前同时检查租约及 `Asset`。需要超出当前方法生命周期时，把句柄交给明确的所有者，例如场景作用域。

```csharp
using (var lease = await assets.AcquireAsync<Texture2D>("Icons/Map", token: token))
{
    if (lease?.Asset != null)
        UseTexture(lease.Asset);
}
```

此片段假设 `UseTexture` 只在调用期间使用资源。跨帧保留资源的对象应继续持有自己的租约。 [租约实现](AssetLease.cs) · [作用域扩展](../Manager/Services/GameServices.cs)
