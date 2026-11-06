# 基础设施补强（2026-11-06）

本轮按“先交付基线，再补核心服务，最后补测试与构建入口”的顺序落地。目标是修正会影响项目交接和故障定位的问题，并让后续业务可以通过稳定接口使用计时、配置、游戏流程和应用生命周期能力。

## 1. 本轮改动清单

### P0：交付与诊断基线

| 改动 | 文件 | 结果 |
|---|---|---|
| 保留 Unity `.meta` | `.gitignore` | 删除 `**/*.meta` 整体忽略；继续忽略 `Temp/`、`Library/`、`Logs/`、`obj/` 等缓存 |
| 错误日志常驻 | `Runtime/Manager/DebugSystem/PLogger.cs` | `LogError` / `LogException` 不再被 `OPEN_PLOG` 宏剔除；`openLog=false` 不再静默 Error |
| 启动耗时指标 | `Runtime/Manager/GameRoot/StartupLifecycle.cs`、`UPGameRoot.cs` | 每一步记录名称、耗时和完成状态；失败步骤也有指标 |
| 旧场景入口标记 | `Runtime/Manager/SceneMgr/SceneMgr.cs` | `SceneMgr` 标为 `Obsolete`，新代码使用 `ISceneService`；框架内部兼容调用局部抑制 CS0618 |
| 文档同步 | 根 README 与各模块 README | 补齐 Contracts、Services、启动指标、新服务和验证边界 |

> `.gitignore` 只能防止今后再次忽略 `.meta`。历史缺失文件仍需在 Unity 完整宿主工程中重新导入，确认没有 Missing Script / 丢失材质后，将生成的 `.meta` 与资源一起提交。

### P1：契约与扩展边界

| 改动 | 文件 | 结果 |
|---|---|---|
| 基础服务契约 | `Runtime/Contracts/FoundationServices.cs` | 新增 Timer、Config、GameFlow、AppLifecycle 接口；Contracts 仍不依赖 Runtime |
| 服务总线扩展 | `Runtime/Contracts/Services.cs` | `IGameServices` 增加 `Timer / Config / Flow / AppLifecycle` |
| 启动模块自注册 | `StartupLifecycle.cs`、`UPGameRoot.cs` | 新增 `IStartupModule` 与 `UPGameRoot.ConfigureLifecycle`；外部模块在 UI 初始化之后、场景服务启动之前注册 |
| 编码规范检查 | `Editor/EditorConfig/FrameworkConventionValidator.cs` | 菜单 `UPandaGF/框架/校验编码规范`；检查直接 Input、Resources.Load、场景对象查找、旧 SceneMgr、Runtime 引用 UnityEditor；明确的内部兼容文件不重复报旧 SceneMgr |

模块自注册必须在根节点初始化前完成：

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void Register()
{
    UPGameRoot.ConfigureLifecycle += lifecycle => lifecycle.Add(new MyStartupModule());
}
```

`ConfigureLifecycle` 是静态事件；插件卸载或测试结束时应退订，避免重复注册和跨场景持有委托。

### P2：核心服务

| 服务 | 实现 | 已覆盖能力 |
|---|---|---|
| Timer | `Runtime/Manager/Services/TimerService.cs` | scaled / unscaled 延时与周期任务、句柄取消、可 await、取消令牌、回调异常隔离 |
| Config | `ConfigService.cs` | 配置注册、类型检查、只读访问、JSON 覆盖、统一校验、问题分级 |
| GameFlow | `GameFlowController.cs` | 注册流程状态、串行切换、守卫、生命周期、变更通知、失败状态与逆序关闭 |
| AppLifecycle | `AppLifecycleService.cs` | 前后台、焦点、低内存、退出通知；前后台继续复用嵌套 `IPauseService` |

四个服务由 `GameServices` 创建，通过 `IGameServices` 注入业务。Timer 使用 `PublicMono` 帧驱动和游戏时钟；`UPGameRoot` 统一转发 Unity 应用生命周期回调。服务关闭顺序为：场景服务 → GameFlow → 移除 Timer 帧驱动 → Timer → AppLifecycle → Audio → Input → Time。

### P3：测试与构建骨架

| 改动 | 文件 | 结果 |
|---|---|---|
| EditMode 测试程序集 | `Tests/EditMode/UPandaGF.Tests.EditMode.asmdef` | Editor-only；引用 Runtime 与 Contracts |
| 纯逻辑测试 | `Tests/EditMode/FoundationServiceTests.cs` | 12 个 NUnit 测试：Timer 6、Config 2、GameFlow 3、StartupLifecycle 1 |
| 构建入口 | `Editor/Build/FrameworkBuildPipeline.cs` | Windows x64 / WebGL / Android 菜单及公共 `Build` 方法；输出 BuildReport 摘要 |

测试无需启动完整 UPGameRoot，计时器由测试手动 `Tick`。原有 PlayMode 回归与真实 AB、存档、热更、回放测试保留。

## 2. 兼容性与接入注意

- `IGameServices` 新增四个属性。自定义实现或测试桩必须同步补成员；测试桩不需要这些服务时，可像 `AssetsOnlyServices` 一样返回 `null`。
- 未定义 `OPEN_PLOG` 时，`InitLog` 仍会被编译期剔除：Error 会进入 Unity Console / Reporter，但 PLogHelper 文件落盘不会初始化。需要发布包错误文件时，仍应启用宏并保留 `openError=true`。
- Timer 回调在主线程执行，不替代 Task 调度。周期任务遇到大 delta 只补触发一次，不追发全部遗漏周期。
- `Delay(0)` 也推迟到下一次 Tick，避免栈内重入。`Dispose` 会取消全部计时器并结束等待中的 Task。
- Config 的 `ApplyOverrides` 是**覆盖语义**，使用 `JsonUtility.FromJsonOverwrite`；字段名错误可能被 JsonUtility 静默忽略，必须接入校验器。
- GameFlow 与框架启动状态机分离：框架 Ready 不代表游戏已经进入 Gameplay。状态的 Enter/Exit 内不能再次调用 `GoToAsync`，否则会在同一切换锁上重入。
- AppLifecycle 的 `Quitting` 是同步通知，Unity 不会等待异步退出任务；关键存档应在退出前完成，并使用暂停/退出路径的既有同步兜底。
- `SceneMgr` 暂未删除，旧业务仍可运行；新代码直接依赖 `ISceneService`。

## 3. 验收清单

本轮环境只有框架源码，没有完整 Unity 宿主工程，因此**未运行 Unity 编译、EditMode / PlayMode 测试或实际构建**。代码与文档已落地，但不能据此宣称测试通过。

在 Unity 2022.3 宿主工程中按以下顺序验收：

1. 重新导入框架，补齐新增文件的 `.meta`；检查 Console 无编译错误，旧场景无 Missing Script。
2. 执行 `UPandaGF/框架/校验编码规范`，按报告处理新接入业务；框架兼容调用不应被机械替换。
3. 在 Test Runner 运行 EditMode `UPandaGF.Tests.EditMode`，应为 12/12 通过。
4. 运行现有 PlayMode 回归，重点检查资源 AB、场景、存档、热更、回放与启动关闭，不能只看测试数量。
5. 启动示例：确认 `GFLoadedEvent`、Ready 和 `StartupMetrics`；人为让某启动步骤失败，确认指标记录失败、已完成步骤逆序回滚。
6. Timer：验证暂停时 scaled 停止、unscaled 继续；句柄取消、`DelayAsync` 取消、回调异常隔离、Shutdown 后无残留帧驱动。
7. Config：注册非法类型、重复注册、错误 JSON、校验失败均有明确结果；合法覆盖后业务读取到新值。
8. GameFlow：验证启动、正常切换、守卫拒绝、Enter 失败和关闭；确认 Changed 监听异常不阻断其他监听者。
9. AppLifecycle：在目标平台验证前后台、焦点、低内存和退出；检查嵌套暂停恢复、存档与释放。
10. 关闭 `OPEN_PLOG` 构建开发包，制造 Error，确认 Console 仍有错误；再启用宏验证文件落盘。
11. 分别执行 Windows / WebGL / Android 构建入口，确认场景列表、版本号、输出目录、BuildReport 与产物可启动。

## 4. CI 接入模板

当前只提供可复用构建入口，没有伪造“已接入 CI”。实际接入需要完整 Unity 宿主工程、Unity 许可证 secrets、对应平台 Build Support 和持久化缓存。

推荐流水线：

```text
检出仓库（包含 .meta）
  → 恢复 Library / 包缓存
  → Unity batchmode 导入并检查编译
  → EditMode 测试
  → PlayMode 测试（按平台拆分）
  → FrameworkConventionValidator.Validate(log: true)，存量违例清零或建立基线后将新增违例设为失败
  → 注入 UPANDA_VERSION
  → FrameworkBuildPipeline.Build(...)
  → 检查 BuildResult.Succeeded
  → 保存测试结果、Editor.log、BuildReport 与安装包
```

GameCI 示例可按项目实际镜像调整：

```yaml
# 模板：需要在宿主仓库配置 UNITY_LICENSE 等 secrets，并固定 Unity 版本。
name: unity-validation
on: [pull_request]
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: game-ci/unity-test-runner@v4
        env:
          UNITY_LICENSE: ${{ secrets.UNITY_LICENSE }}
        with:
          projectPath: .
          unityVersion: 2022.3.48f1
          testMode: EditMode
          artifactsPath: editmode-results
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: editmode-results
          path: editmode-results
```

> 上述 YAML 是接入模板，不在本仓库声称可运行。GameCI 的 Windows / macOS 平台构建通常需要自托管 runner；Android、WebGL 还需安装对应模块和 SDK。密钥不得写入框架配置或 README。

## 5. 尚未落地的大项与后续顺序

| 优先级 | 后续项 | 本轮未做的原因 / 下一步 |
|---|---|---|
| 1 | 历史 `.meta` 修复与完整 Unity 验证 | 必须有宿主工程重新导入并核对 GUID；先完成第 3 节验收 |
| 2 | Runtime 程序集细分 | 当前只补 Contracts / Services 边界；进一步拆分需处理 Manager 双向依赖和旧 GUID，单独迁移 |
| 3 | 新 Input System 适配 | 现有 `InputService` 仍基于旧 Input Manager；应先稳定动作接口，再增加后端，不要业务散落 `Input.*` |
| 4 | 本地化 | 尚无多语言文本、字体、图片与切换流程；需明确表格来源、TMP 字体 fallback 和资源地址规范 |
| 5 | UI 数据绑定 / SafeArea、AudioMixer、通用对象池 | 在真实项目需求下补接口与实现，避免一次性抽象无使用者的框架层 |
| 6 | WebSocket 与联网队列 | 当前只有 Downloader / HTTP 文件能力；长连接、重连、心跳、取消和线程模型需独立设计 |
| 7 | CI/CD 实际运行与发布检查 | 用第 4 节模板接入宿主仓库，再补签名、符号、产物校验、回滚和渠道发布 |
| 8 | 安全与诊断上报 | 存档加密、密钥管理、崩溃/性能上报不能由本地“校验器”替代，需按发行平台单独选型 |

本轮没有创建 Git commit，没有删除旧模块，也没有声称本地化、WebSocket、New Input System、完整 MVVM、真实 CI/CD 已经实现。
