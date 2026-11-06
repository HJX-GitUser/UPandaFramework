# 应用服务与场景作用域

服务实现入口。契约位于 `Runtime/Contracts`，根节点在 Assets 就绪后装配 `GameServices`，并向场景 `IGameServicesConsumer` 注入。
所有 Unity 对象操作、场景与作用域 API 均在主线程使用；外部依赖通过构造函数或 `Configure` 注入。

| 文件 | 职责 |
| --- | --- |
| `GameServices.cs` | 显式装配及逆序关闭；场景资源、对象池扩展方法；计时器帧驱动 |
| `AssetService.cs` | Resources/AB 统一凭据、取消等待和迟到资源释放 |
| `SceneService.cs` | 原生后端、串行队列、请求进度、场景 handle 作用域 |
| `LifetimeScope.cs` / `ScopeSubscriptions.cs` | 取消、逆序释放及两套事件的独立作用域订阅 |
| `InputAndTime.cs` | 输入动作映射、上下文栈、嵌套暂停、游戏时钟 |
| `TimerService.cs` | 统一计时器：`Delay` / `Every` / `DelayAsync`，可手动 `Tick` 便于纯逻辑测试 |
| `ConfigService.cs` | 配置注册中心：注册、JSON 覆盖、统一校验 |
| `GameFlowController.cs` | 游戏流程状态机：状态注册、进入守卫、串行切换、失败状态 |
| `AppLifecycleService.cs` | 前后台 / 焦点 / 低内存 / 退出事件，前后台暂停接入 `IPauseService` |
| `PauseMenuController.cs` | 绑定现有界面的暂停菜单行为 |
| `FileSaveService.cs` | 现有版本化序列化的存档适配 |

## 计时器（ITimerService）

```csharp
// 延时回调；scaled=true 时暂停/timeScale=0 不会推进
var handle = services.Timer.Delay(2f, () => PLogger.Log("2 秒到"));

// 周期回调；Cancel/Dispose 停止
var tick = services.Timer.Every(0.5f, RefreshHud);

// 可 await、可取消；场景作用域退出时自动取消
await services.Timer.DelayAsync(1.5f, scope.Token, scaled: false);
```

- `Delay(0)` 也会推迟到下一次 `Tick`，避免在调用栈内重入。
- 回调异常会被隔离并记录错误日志，不影响其他计时器；`Every` 大 delta 只补触发一次。
- `TimerService.Tick(scaledDelta, unscaledDelta)` 由 `GameServices` 挂到 `PublicMono.Update`；测试可直接手动推进。

## 配置中心（IConfigService）

```csharp
services.Config.Register("battle", battleConfig, version: 1, validator: (cfg, issues) =>
{
    if (cfg.maxHp <= 0) issues.Add(new ConfigIssue("battle", "maxHp 必须为正数", ConfigIssueLevel.Error));
});

// 环境 / 渠道差异：JSON 覆盖字段（JsonUtility.FromJsonOverwrite）
services.Config.ApplyOverrides(new Dictionary<string, string> {
    { "battle", "{\"maxHp\":200}" }
});

var issues = services.Config.Validate(); // 启动期统一校验，Error 级应阻断进入游戏
var cfg = services.Config.Get<BattleConfig>("battle");
```

适用于 ScriptableObject 之外的**运行期可覆盖配置**。SO 配置仍可在启动时读出数据后注册进来，获得同样的覆盖与校验能力。

## 游戏流程（IGameFlowService）

```csharp
public sealed class LoginState : IGameState
{
    public string Id => "Login";
    public bool CanEnter(GameFlowContext ctx, out string reason) { reason = null; return true; }
    public async Task EnterAsync(GameFlowContext ctx, CancellationToken token)
    { await ctx.Services.Scenes.LoadAsync("Assets/Scenes/Login.unity", token: token); }
    public Task ExitAsync(CancellationToken token) => Task.CompletedTask;
}

services.Flow.Register(new LoginState());
await services.Flow.StartAsync("Login");
await services.Flow.GoToAsync("Lobby");
```

- 切换串行化；`CanEnter` 拒绝时不会离开当前状态。
- `EnterAsync` 抛异常会把 `Status` 置为 `Failed`，并保留原 `CurrentStateId`，由上层进入恢复状态。
- **不要在 `EnterAsync/ExitAsync` 内调用 `GoToAsync`**（切换锁已持有），应返回后由外层驱动。
- 与 `StartupLifecycle` 分工：`StartupLifecycle` 管“框架怎么起来”，`IGameFlowService` 管“游戏怎么玩”。

## 应用生命周期（IAppLifecycle）

`UPGameRoot` 把 `OnApplicationPause / Focus / LowMemory / Quit` 统一转发到 `AppLifecycleService`，业务订阅
`PauseChanged / FocusChanged / LowMemory / Quitting` 即可。前后台暂停仍通过 `IPauseService` 的嵌套凭据实现，不会重复暂停。
`Quitting` 是同步通知：Unity 不会等待异步操作，退出前的关键存档应在收到通知时同步落盘，或提前在流程状态中完成。

## 关闭顺序

`SceneService → GameFlow → 移除计时器帧驱动 → Timer → AppLifecycle → Audio → Input → Time`。
任一步失败会聚合成 `AggregateException`，不会中断后续释放。

[完整设计与兼容说明](../../../OptimzeLog/P1Framework/README.md) · [使用示例](../../../OptimzeLog/P1Framework/MIGRATION.md) · [本轮落地记录](../../../OptimzeLog/FoundationHardening/README.md)
