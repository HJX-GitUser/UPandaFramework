# 日志与运行诊断

本模块提供日志格式化、可选文件输出和两套运行时面板。入口是 `PLogger`，根节点启动时由 `DebugerInit` 读取 `StreamingAssets/Data/LogConfig.json`。 [PLogger.cs](PLogger.cs) · [DebugerInit.cs](DebugerInit.cs)

## 输出开关

| 开关 | 行为 |
| --- | --- |
| `OPEN_PLOG` | 控制普通日志、颜色日志、警告和文件模块初始化调用的编译。菜单在 `UPandaGF → 日志系统` 下。各构建目标的宏需要单独核对。 |
| `openLog` | PLogger 的运行时总开关。 |
| `openWarning` / `openError` | 控制警告与错误、异常的输出。错误与异常方法始终编译，运行时仍受配置过滤。 |
| `addHeadFix`、`logHeadFix`、`openTime`、`showThreadID` | 控制格式化。 |
| `maxLogLength` | 大于零时截断 PLogger 格式化的消息，零表示不限。 |
| `logSave`、`saveOverwrite`、`logFileSavePath` | 启用文件写入、选择固定或带时间戳的文件名、指定文件输出目录。 |

Unity 原生日志和异常可独立进入 Console。Reporter 和文件监听订阅的是 Unity 日志回调，因此它们还能收到其他组件的 Unity 日志。 [LogConfig.cs](Data/LogConfig.cs)

在业务脚本的方法内可直接调用以下入口，脚本需导入 `UPandaGF` 命名空间。

```csharp
PLogger.Log("场景加载开始");
PLogger.LogWarning("使用随包清单");
PLogger.LogError("资源初始化失败");
```

## 可注入日志（ILogService）

需要把日志作为依赖注入（或希望在未定义 `OPEN_PLOG` 时仍然可观测）时，使用 `IGameServices.Log`：

```csharp
services.Log.Info("场景加载开始");
services.Log.Warning("使用随包清单");
services.Log.Error("资源初始化失败");
services.Log.LogException(exception);
```

`LogService` 转调 `PLogger.Write/WriteException`（**不带** `[Conditional]`），因此调用不会被编译期剔除，但仍受 `openLog/openWarning/openError` 运行时开关控制。代价是调用点实参总会求值：每帧/高频路径继续使用 `PLogger.Log/LogWarning`，把编译期零成本留在热路径上。 [LogService.cs](LogService.cs)

## 文件输出

启用当前目标的 `OPEN_PLOG` 和 `logSave` 后，`PLogger` 将 `logFileSavePath` 原样交给 `PLogHelper`。当前默认值 `Output Log/` 由 `Path.GetFullPath` 相对进程工作目录解析。诊断页展示的 `PLogHelper.LogFilePath` 是实际位置，按 `LogConfig.LogFileSavePath` 推导的目录可能不同。移动端还需确认目录可写。`PLogHelper` 用有界队列接收线程日志，常规平台由后台线程写入，WebGL 使用每帧处理预算。停止时会尝试退订、排空并关闭文件。 [PLogger.cs](PLogger.cs) · [PLogHelper.cs](PLogHelper.cs) · [平台存储](../AssetsMgr/README.md)

文件输出辅助对象独立常驻。需要重建根节点或手动重新调用 `InitLog` 的项目，要考虑旧文件输出对象的生命周期。多次初始化可创建多个文件监听者。

## 运行时面板

| 面板 | 使用方式 | 数据路径 |
| --- | --- | --- |
| Reporter | 在 `UPGameRoot` Inspector 打开调试模式并检查 `reporter` 引用，运行后通过 FPS 按钮或 `ShowLogWindows()` 打开。 | 直接捕获 Unity 日志，有独立队列、日志保留预算与框架诊断页。 |
| uGUI 日志面板 | 使用 `UPandaGF → 日志系统 → UGUI日志面板创建器` 生成预制体，按面板需要设置日志文件路径。 | 可跟读日志文件并展示、过滤、导出。大量积压会在一次更新中取走队列并重建文本，适合调试。 |

`EnableDebugModel` 和 `showFPS` 控制显示。`UPGameRoot.Config` 仍返回防御性副本（每次访问都会复制，适合诊断/低频读取）；`DebugerInit` 只在初始化时取一次 `EnableDebugModel`，更新/绘制路径不再触发配置 JSON 往返。运行中切换该开关需要重新进入播放。uGUI 面板的 `F12`/错误自动弹出依赖管理器保持激活，面板内容单独显示/隐藏。 [Reporter](Reporter/README.md) · [uGUI 面板](UGUIDebug/LogListenerManager.cs)

## 诊断和验证

`FrameworkDiagnostics.Capture(root)` 读取根状态、启动步骤耗时、资源引用、AB 在途请求、场景作用域和 UI 面板计数。诊断计数是采样结果，判断服务可用性仍以根状态和实际调用结果为准。编辑器工具提供宏开关、配置面板、诊断窗口与日志自检。 [FrameworkDiagnostics.cs](FrameworkDiagnostics.cs) · [日志自检](../../../Editor/PLogger/PLoggerSelfCheck.cs) · [Reporter 回归](../../../Tests/PlayMode/ReporterRegressionTests.cs)
