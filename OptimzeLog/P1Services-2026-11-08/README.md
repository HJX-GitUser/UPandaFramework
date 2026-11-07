# P1 服务层收口（0.3.0，2026-11-08）

对应审查报告 §3.2 与 `OPTIMIZATION_PLAN.md` P1-3：补齐 `IHttpService`、`IPoolService`、`ILogService`，并让 `UIManager` / `AudioMgr` / `QuestManager` 只通过注入获得依赖。按用户确认的方案执行：**B 彻底去单例 + Q-b 任务存档走 ISaveService + L2 三个类日志全部注入**。

## 改动范围

### 新增契约与实现

| 项 | 文件 |
| --- | --- |
| `IHttpService` / `HttpResult<T>` / `HttpRequestOptions` | `Runtime/Contracts/PlatformServices.cs` |
| `IPoolService` / `IPool` | 同上 |
| `ILogService` / `LogLevel` | 同上 |
| `IUIService` / `IUIServiceConsumer`（Runtime 程序集） | `Runtime/Manager/UIMgr/IUIService.cs` |
| `HttpManager : IHttpService`（Task 接口 + 取消桥接） | `Runtime/Manager/HTTPTool/HttpManager.cs` |
| `PoolService : IPoolService` | `Runtime/Manager/Services/PoolService.cs` |
| `LogService : ILogService` | `Runtime/Manager/DebugSystem/LogService.cs` |
| `PLogger.Write/WriteException/IsEnabled`（非 Conditional 桥） | `Runtime/Manager/DebugSystem/PLogger.cs` |
| `ISaveService.Delete` | `Runtime/Contracts/Services.cs` / `FileSaveService.cs` |
| `services.Inject(GameObject, IUIService)` | `Runtime/Manager/Services/GameServices.cs` |

### 移除的静态单例入口（破坏性）

- `UIManager.Instance`
- `AudioMgr.Instance` / `AudioMgr.Release()`
- `QuestManager.Instance` / `InstanceOrNull` / `AutoCreateInstance`
- `HttpManager.Instance`
- `GameObjectPoolMgr.Instance`

实例归属：`UIManager` 由 `UPGameRoot` 创建；`AudioMgr` / `HttpManager` / 池由 `GameServices` 创建；`QuestManager` 由场景持有。

### 依赖注入

- `UIManager.Configure(IAssetService, ILogService)`；UI 资源只走 `IAssetService`；`BasePanel.uiManager` 由 `UIManager` 在实例化后注入（`OnOpen` 起可用）。`SetResourcesLoader` 保留为 `[Obsolete]` 迁移桥。
- `AudioMgr(IAssetService, PublicMono, ILogService, Func<PublicMono>)`：资源、帧驱动、日志全部构造注入；没有 `ResourcesLoader.Instance` / `UPGameRoot.Instance` 回退。
- `QuestManager : IGameServicesConsumer`：`Configure(IGameServices)` 注入日志与存档；`RewardReceiver` 必须显式赋值；模板加载可用 `DefinitionLoader` 替换；存档走 `ISaveService` 槽位（默认 `quests`），旧的 `SaveToFile/LoadFromFile/quests.json` API 移除。
- `QuestHudTracker` / `QuestLogPanel` / `QuestDemoDriver` 通过序列化引用获得 `QuestManager`；`QuestDemoInstaller` 自动创建并绑定。
- `GameServices` 装配 `Http` / `Pools` / `Log`；关闭顺序更新为 `Scenes → Flow → Pools → Http（取消并销毁宿主）→ Timer → AppLifecycle → Audio → Input → Time`。

### 示例与测试迁移

- `TestPoolMgr` 改用 `IGameServicesConsumer` + `services.Pools.Shared`。
- `GameLaunchExample` 改用 `IUIServiceConsumer`。
- `FrameworkRegressionTests` 改用 `Configure(new AssetService(loader, null))`；`LifecycleRegressionTests` 用 `LifecycleDelayedAssetService` 替换反射注入 `assetsLoader`。
- 新增 `Tests/PlayMode/ServicesContractRegressionTests.cs`：LogService 级别/运行时开关、池未注入失败、作用域池释放、HTTP 成功/失败/取消、Quest 注入存档与奖励。

## 验证

环境：Unity `2021.3.45f2c1`、`2022.3.53f1c1`；隔离工程 `_verify/P1Verify`、`_verify/P1Verify2022`（框架以 `Assets/upanda-framework` 导入）。

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 编译 2021.3 | `Unity.exe -batchmode -nographics -quit -projectPath <project> -logFile compile-2021.log` | 0 个 `error CS`（见 `compile-2021-summary.txt`） |
| EditMode 2021.3 | `-runTests -testPlatform EditMode` | 24 / 24 |
| PlayMode 2021.3 | `-runTests -testPlatform PlayMode` | 300 / 300 |
| 编译 2022.3 | 同上 | 0 个 `error CS`（见 `compile-2022-summary.txt`） |

原始文件：`editmode-2021.xml`、`playmode-2021.xml`、`compile-2021.log`、`compile-2022.log`。

## 已知边界

- 旧的 `quests.json` 不会自动迁移到 `ISaveService` 槽位；需要保留旧档时，调用 `LoadFromJson(File.ReadAllText(...))` 后再 `SaveToSlot()` 做一次性迁移。
- 旧场景里已存在的任务 UI / Driver 未绑定 `QuestManager` 时会报错；重新执行 `UPandaGF → Runtime → 任务系统 → 一键搭建` 即可自动绑定。
- `ILogService` 不参与 `OPEN_PLOG` 编译期剔除，调用点实参总会求值；高频路径仍应使用 `PLogger`。
- `AudioMgr` 没有帧驱动时会立即完成淡变（避免停在 0 音量）；这是测试/无 `PublicMono` 环境的明确行为。
- `services.Inject` 只注入 `IGameServicesConsumer` / `IUIServiceConsumer`，不会递归处理已禁用对象之外的延迟创建逻辑。
