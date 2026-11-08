# GameRoot 启动与配置

`UPGameRoot` 是框架在 Unity 场景中的装配入口。它管理日志、下载器、资源加载器、旧二进制数据管理器和 UI 子组件，并在启动期间创建 `IGameServices`。同一应用通常只放一个启动根，组件挂在场景顶层对象上，由单例基类跨场景保留。 [UPGameRoot.cs](UPGameRoot.cs)

## 接入

1. 用 `UPandaGF → 创建UPGameRoot` 创建启动对象。该菜单在找不到现有启动示例时也会创建 `GameLaunchExample`。
2. 在 Inspector 选择加载模式。编辑器直读时，资源使用 AssetDatabase，场景按 Unity 可直接加载的场景配置。AssetBundle 模式需要准备当前目标的资源包和 `assetData.assetref`。
3. 保存启动场景，并将启动场景加入 Build Settings。使用启动示例时，把 `firstScene` 指向业务场景。AB 场景使用完整的 `Assets/.../Scene.unity` 路径。
4. 业务脚本等待根就绪，再访问 `Services` 或兼容接口 `GetAssetsLoader()`。在 `Awake` 订阅的旧业务可继续使用 `GFLoadedEvent` 与 `GFLoadedFailedEvent`，并在销毁时退订。晚注册的业务可直接等待共享任务。 [启动示例](../../../Samples/Startup/GameLaunchExample.cs)

```csharp
using System.Threading;
using System.Threading.Tasks;
using UPandaGF;

public static class FirstScene
{
    public static async Task LoadAsync(UPGameRoot root, string scenePath, CancellationToken token)
    {
        await root.WaitUntilReadyAsync(token);
        await root.Services.Scenes.LoadAsync(scenePath, token: token);
    }
}
```

项目应只使用一条首场景加载流程。`GameLaunchExample` 已负责进度面板、可选程序集加载和首场景跳转，使用上例时由调用方负责触发、异常处理和自己的 UI。

## 启动与关闭

| 阶段 | 行为 |
| --- | --- |
| Configuration | 读取、复制并校验启动配置。 |
| Logging / Data | 初始化日志和旧二进制数据服务。 |
| ContentUpdate / Assets | AB 模式准备清单，再初始化主包和资源后端。 |
| Services / UI | 组装 `IGameServices`，把资源服务接入 UI。 |
| 外部步骤 | `ConfigureLifecycle` 允许注册 `IStartupModule`，注册完成后与内建步骤一同运行。 |
| 发布就绪 | 为已加载场景绑定 `IGameServicesConsumer`，广播 `GFLoadedEvent`。后续场景加载时继续扫描场景中的消费者。 |

`InitializeAsync()` 与 `WaitUntilReadyAsync()` 使用同一次启动。调用方 token 取消自己的等待，根级关闭使用 `ShutdownAsync()`。`State`、`InitializationError` 和 `StartupMetrics` 可定位运行阶段、错误和耗时。内建步骤失败后，协调器逆序执行已进入步骤注册的清理。对计划内重建，先等待旧根关闭，再销毁旧对象和创建新根。 [StartupLifecycle.cs](../../Core/StartupLifecycle.cs)

`IGameServicesConsumer.Configure(IGameServices)` 适合接收场景服务。初始场景的 `Awake` 可能早于注入，运行时实例化的对象需要显式装配。外部启动步骤已经位于启动任务内部，应使用自身接收的依赖，避免在 `StartAsync` 中再等待 `WaitUntilReadyAsync()`。 [服务契约](../../Contracts/Services.cs)

## 配置来源

| 环境 | 生效配置 |
| --- | --- |
| Editor Play | 当前启动根 Inspector。要采用磁盘值时，先在面板点击「从 Json 读取」。 |
| Player | 优先读取 `Assets/StreamingAssets/Data/GameRootConfig.json` 对应的随包文件。文件缺失、读不到或解析失败时，回退到场景序列化配置。读到的配置若在 schema、平台或字段上不合法，校验会终止启动。 |
| 保存场景与构建前 | 编辑器持久化工具从已打开场景查找根并写入 JSON。多场景编辑时，留意工具取的是找到的首个有效根。 |

`method` 区分编辑器直读与 AB。AB 模式还使用 `bundleTarget`、`LoadAssetPath`、`remoteURL`、AES 与热更预算。Player 仅接受 AB 模式，资源路径、主包与目标平台必须配套。运行中读取 `Config` 会得到防御性副本，适合诊断和低频读取。 [配置校验](../../Assets/UPGameRootConfig.cs) · [配置持久化](../../../Editor/CustomInspector/UPGameRootConfigPersistence.cs)

## 当前边界

- `SourceRef` 表示启动热更返回给根的清单。关闭热更时，`AssetsLoader` 可以自行读取随包清单，根的 `SourceRef` 此时仍可能为空。就绪以等待任务或根的就绪状态为准。
- 启动步骤的普通失败会发布失败事件。注册回调直接抛异常，或模块主动抛出取消异常，当前会绕过该通知的部分路径。需要掌握启动结果的业务应处理等待任务的异常。
- 场景绑定会执行用户的 `Configure`。若回调同步发起关停，当前成功通知仍可能发出。销毁根也只会发起异步清理，依赖清理完成的逻辑应显式等待关闭。
- `enableAssetUpdate` 控制内容包更新。`GameLaunchExample.loadHotUpdateScripts` 单独控制 DLL 加载。两者的资源准备、失败点和验证不同。

## 相关文档

[应用服务](../../Core/Services/README.md) · [资源与场景加载](../../Assets/SourcesLoadMgr/README.md) · [启动内容更新](../../Assets/HotUpdate/README.md) · [AssetBundle 工具](../../../Editor/AssetBundleTools/README.md) · [P0 启动验证记录](../../../OptimzeLog/P0Startup/VALIDATION.md)
