# 应用服务与场景作用域

资源加载就绪后，`UPGameRoot` 装配 `GameServices`，并向场景中的 `IGameServicesConsumer` 提供接口集合。使用 Unity 对象、调用场景和作用域 API 时应在主线程。接入业务时可以通过构造函数或 `Configure` 传入所需接口，测试可替换后端。 [服务契约](../../Contracts/README.md) · [装配源码](../../Composition/GameServices.cs) · [启动流程](../../Composition/GameRoot/README.md)

## 平台能力服务（HTTP / 池 / 日志）

`GameServices` 同时装配 `Http`、`Pools`、`Log`：

- `HttpManager` 实现 `IHttpService`。容器创建宿主对象并在关闭时 `CancelAll` 后销毁；业务用 `services.Http.GetAsync<T>` 等 Task 接口，需要进度/`OnRetry` 时再取具体 `HttpManager`。
- `PoolService` 实现 `IPoolService`。`Shared` 随容器关闭而释放，`CreatePool(scope)` 随作用域释放；池内预制体统一经 `IAssetService` 取得租约。
- `LogService` 实现 `ILogService`，转调非 `[Conditional]` 的 `PLogger.Write`，因此未定义 `OPEN_PLOG` 时注入日志仍可观测；每帧/高频路径继续直接用 `PLogger`。

根关闭顺序：`SceneService → Flow → Pools → Http（取消并销毁宿主）→ 摘计时器驱动 → Timer → AppLifecycle → Audio → Input → Time`。 [平台契约](../../Contracts/PlatformServices.cs) · [池服务](../../Assets/PoolService.cs) · [日志实现](../../Diagnostics/LogService.cs)

## 场景、资源和订阅

`Scenes.LoadAsync` 接受场景名或路径、加载模式、取消令牌、超时和单请求进度回调。AB 场景优先使用清单中带 `.unity` 的完整路径；编辑器直读模式由 Unity 场景系统加载。全局 `Progress` 会收到所有请求的进度。场景加载和卸载通过同一个队列串行执行。调用方取消或超时后，已经提交给 Unity 的原生加载仍可能完成并切换场景。 [场景服务](SceneService.cs) · [场景 provider 测试](../../../Tests/EditMode/SceneServiceAssetProviderTests.cs)

`Scenes.GetScope(scene)` 以已加载场景的 handle 查找作用域。作用域可持有 `IDisposable`，例如资源租约、暂停凭据或订阅凭据。释放时先取消令牌，再按登记的逆序归还持有物。`Cancel` 只发取消信号，`ScopeSubscriptions` 注册的监听仍会一直保留到 Dispose。应用代码可用场景作用域组织如下流程。 [作用域](LifetimeScope.cs) · [订阅](ScopeSubscriptions.cs)

```csharp
var scene = await services.Scenes.LoadAsync("Assets/Scenes/Workshop.unity", token: token);
var scope = services.Scenes.GetScope(scene);
var map = await services.AcquireInScopeAsync<Texture2D>(scope, "Icons/Map");
var pool = services.CreatePool(scope);
```

将示例路径替换成实际资源并判空。`AcquireInScopeAsync` 把加载取消及持有登记绑定到作用域，`CreatePool` 在作用域释放时销毁池和借出实例。自行管理资源时，`Assets.AcquireAsync<T>` 返回的租约需要由调用方释放。取消等待后，`AssetService` 会观察底层任务，并归还迟到的租约。 [资源服务](../../Assets/AssetService.cs) · [作用域扩展](../../Composition/GameServices.cs)

`UnloadAsync` 在开始原生卸载前取消现有作用域。原生卸载失败时，场景可能保留已经取消的作用域。尤其应避免尝试卸载 Unity 当前唯一的已加载场景。 [卸载实现](SceneService.cs)

## 输入、暂停和计时

`InputService` 按上下文查找动作。默认后端使用 Unity 旧 Input Manager，输入轴和键位由 `UPGameRootConfig.inputBindings` 提供。`PushContext("UI")` 返回上下文凭据，释放后移除该上下文。最上层命中的上下文决定输入，阻断层可屏蔽下面的上下文。暂停还会屏蔽 Gameplay 动作。 [输入实现](InputAndTime.cs)

`Pause.Pause(reason)` 返回可嵌套的暂停凭据。首个凭据冻结 `Time.timeScale` 和 `AudioListener.pause`，最后一份释放后恢复旧值。使用 `Clock.SetTimeScale` 可以在暂停期间更新待恢复的倍率。`PauseMenuController` 可挂在菜单外的常驻节点，将 `menuRoot` 绑定到菜单。控制器持有暂停和 UI 上下文凭据，关闭或禁用时归还。 [时间与暂停](InputAndTime.cs) · [菜单控制器](PauseMenuController.cs)

```csharp
using (services.Pause.Pause("Transition"))
{
    await services.Timer.DelayAsync(0.3f, scope.Token, scaled: false);
}
scope.Own(services.Timer.Every(1f, RefreshHud, scaled: true));
```

默认 scaled 定时器用游戏时钟，暂停会让正常计时停止。`Delay(0)` 在下一次 Tick 执行。周期计时遇到大步长时当帧只触发一次，回调异常会被记录并隔离。计时器由 `PublicMono` 帧驱动，测试可手动调用 `Tick`。当前实现在回调中直接释放整个 `TimerService` 可能使 Tick 遍历失效，应由外层安排关闭。 [计时器](TimerService.cs) · [帧驱动](../PublicMono/README.md)

## 配置、流程和应用生命周期

| 服务 | 入口及调用约定 |
| --- | --- |
| `Config` | 用 `Register<T>(key, value, version, validator)` 登记，`ApplyOverrides` 原地覆盖对象，`Validate` 返回问题清单。调用方自行决定 Error 是否阻断进入游戏。配置服务保留传入对象引用，覆盖按条处理，解析失败只记日志。 |
| `Flow` | 注册 `IGameState`，用 `StartAsync` 开始，通过 `GoToAsync` 切换。依次执行进入守卫、旧状态退出、新状态进入。守卫拒绝时保留当前状态。进入失败会标记 Failed，但旧状态已执行过退出；旧状态退出本身抛错会留下 Transitioning。 |
| `AppLifecycle` | 订阅 `PauseChanged`、`FocusChanged`、`LowMemory`、`Quitting`。后台暂停和菜单使用同一套暂停凭据。退出通知是同步的，关键存档应在退出前完成或在通知中同步保存。根节点在就绪前不转发暂停、焦点和低内存通知。 |
| `Saves` | `Save/Load/Exists/Delete` 同步访问 `Application.persistentDataPath/Saves` 下的槽位文件。槽位名需合法，首次缺文件返回 `null`，解析或 I/O 错误会抛出。读取旧格式时不会自动重写槽位。 |

`EnterAsync` 和 `ExitAsync` 执行期间持有流程切换闸门；后续状态切换由外部在当前切换完成后发起。根关闭时先关闭 Scenes，再退出当前流程，状态的退出清理须适配这一顺序。 [配置](ConfigService.cs) · [流程](GameFlowController.cs) · [生命周期](AppLifecycleService.cs) · [存档](../../Save/FileSaveService.cs)

WebGL 上 `Save` 写入 Unity 的虚拟文件系统。需要确认浏览器持久化时，在保存后于正常运行阶段等待 `PlatformStorage.FlushAsync()`，因为 `ISaveService.Save` 自身不会等待提交到 IndexedDB。 [平台存储](../../Assets/AssetsMgr/PlatformStorage.cs)

## 诊断和验证

`FrameworkDiagnostics.Capture(UPGameRoot.Instance)` 可读取场景请求和作用域、流程、前后台状态、面板与资源计数。日志控制台的框架诊断页使用同一份快照。基础服务可在 EditMode 验证，作用域、原生场景和面板所有权用 PlayMode 测试覆盖。 [诊断入口](../../Composition/FrameworkDiagnostics.cs) · [基础服务测试](../../../Tests/EditMode/FoundationServiceTests.cs) · [作用域与场景回归](../../../Tests/PlayMode/P1RegressionTests.cs)
