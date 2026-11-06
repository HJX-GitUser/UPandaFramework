# 服务契约

UPandaGF.Contracts 只包含 Unity 数据类型、服务接口、AssetLoadMethod 和公开的 AssetLease<T>，不引用 Runtime、业务扩展或示例。
业务依赖窄接口，启动装配代码使用 IGameServices。ILifetimeScope 表达取消与资源持有，具体实现位于 Runtime。
AssetLease<T> 是主线程共享凭据，Retain 增加持有者，Dispose 幂等归还；由具体后端提供释放回调。

## 文件与接口

| 文件 | 内容 |
| --- | --- |
| `Services.cs` | `IAssetService` / `IAudioService` / `ISaveService` / `IInputService` / `IPauseService` / `IGameClock` / `ISceneService` / `ILifetimeScope` / `IGameServicesConsumer` / `IGameServices` |
| `FoundationServices.cs` | `ITimerService` / `IConfigService` / `IGameFlowService` / `IAppLifecycle`，以及 `ITimerHandle`、`ConfigIssue`、`GameFlowContext`、`IGameState` 等配套类型 |
| `AssetLease.cs` | `AssetLease<T>` 引用计数凭据 |
| `PublicEnum.cs` | `AssetLoadMethod` 等公共枚举 |

## IGameServices 成员

`Assets / Audio / Saves / Scenes / Input / Pause / Clock / Timer / Config / Flow / AppLifecycle`

- `Timer`：`Delay` / `Every` / `DelayAsync`，scaled 计时受暂停与 timeScale 影响。
- `Config`：配置注册、JSON 覆盖、统一校验。
- `Flow`：游戏流程状态机（Boot/Login/Lobby/Battle/Result 等），区别于管框架启动的 `StartupLifecycle`。
- `AppLifecycle`：前后台、焦点、低内存、退出事件。

> **契约扩展说明（2026-11-06）**：`Timer / Config / Flow / AppLifecycle` 为本轮新增成员。
> 若存在自定义 `IGameServices` 实现（例如测试桩），需要同步补这 4 个属性；只测局部能力的桩可返回 `null`。

[设计与迁移](../../OptimzeLog/P1Framework/README.md) · [本轮落地记录](../../OptimzeLog/FoundationHardening/README.md)
