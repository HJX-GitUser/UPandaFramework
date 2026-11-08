# 0.6.0 迁移指南（运行时日志面板替换）

0.6.0 删除了旧的运行时日志面板源码及其构建信息钩子，替换为独立实现的 `UPandaLogConsole`。旧面板源码已从包内移除，`THIRD_PARTY_NOTICES.md` 中对应条目也已删除；项目自有图标迁移到新模块继续使用。

## 运行时 API

| 0.5.0 | 0.6.0 |
| --- | --- |
| `UPGameRoot.reporter`（旧面板组件） | `UPGameRoot.logConsole`（`UPandaLogConsole`） |
| `Reporter.ShowLogWindows()` | `logConsole.Show()` |
| `Reporter.HideLogWindows()` | `logConsole.Hide()` |
| `Reporter.ClearLogs()` | `logConsole.Clear()` |
| `Reporter.SetFilter` / `SetSeverityFilters` / `SetCollapse` | `logConsole.SetFilter` / `SetSeverity` / `SetCollapse` |
| 无 | `logConsole.Toggle()`、`CopyVisible()`、`SaveVisible()`、`SnapshotVisible()` |

`UPandaLogConsole` 的容量字段：`MaxStoredEntries`、`MaxStoredBytes`、`MaxQueuedEntries`、`MaxQueuedBytes`、`MaxDrainPerFrame`；默认每帧最多消费 256 条，超过上限时淘汰最旧记录或丢弃新日志并累计 `DroppedCount`。

## 显示设置 / 诊断

- 新面板有三页：日志、显示设置、框架诊断。显示设置包含时间/场景/内存/帧率列、搜索/复制/导出/换场清空工具栏开关、暂停捕获、自动跟随、界面缩放和不透明度。
- 对应 API：`ShowTime`、`ShowScene`、`ShowMemory`、`ShowFps`、`PauseCapture`、`FollowTail`、`ClearOnSceneLoad`、`PanelOpacity`、`PanelScale`，以及 `OpenSettings()` / `OpenDiagnostics()`。
- 显示设置写入 `PlayerPrefs` 的 `UPandaLogConsole.*`；旧面板的 `Reporter_*` 键不迁移，避免旧配置把新布局带偏。暂停/跟随是运行态，不写盘。
- 诊断页读取 `FrameworkDiagnostics.Capture(UPGameRoot.Instance)`，包含框架生命周期、模块、启动步骤、Resources、AssetBundle、场景作用域和 UI 面板计数；应用/设备/系统信息在显示设置页。

## 场景与编辑器

- 旧场景中的旧面板组件会变成 Missing Script。执行菜单 `UPandaGF → Tools → 迁移日志面板（清理旧对象）`：会确认后删除名为 `Reporter` 且含 Missing Script 的对象，并为开启 `EnableDebugModel` 的 `UPGameRoot` 创建新的 `LogConsole` 子节点、写入 `logConsole`；也可在 Inspector 里关闭再打开“启动日志窗口”触发同一同步。
- `UPGameRootEditor` 不再加载旧 `Images/*.png` 路径；项目自有图标由 `LogConsoleIconLoader` 从新模块的 `Icons/` 绑定。
- 旧的编译期 `StreamingAssets/build_info` 写入钩子随旧编辑器脚本一起删除；需要构建时间请在构建流水线中自行记录（例如 `Application.buildGUID` 或 CI 环境变量）。
- 运行时打开方式不变：`EnableDebugModel` 开启后点左上角 FPS 按钮；也可以直接调用 `UPGameRoot.logConsole.Show()`。

## 旧代码引用

全局替换：

```text
Reporter            -> UPandaLogConsole
reporter            -> logConsole
ShowLogWindows()    -> Show()
HideLogWindows()    -> Hide()
ClearLogs()         -> Clear()
SetSeverityFilters  -> SetSeverity
```

旧 `Reporter.Log` / `Reporter.Sample` 等嵌套类型不再存在；需要日志数据时使用 `LogConsoleEntry` / `SnapshotVisible()`。

## 回滚

0.6.0 的删除是不可逆的（旧源码不再随包分发）。需要回滚到 0.5.0 时，从对应版本的 Git tag/提交恢复整个包，不要只回滚单个文件。
