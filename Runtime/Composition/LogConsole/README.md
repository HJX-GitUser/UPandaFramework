# 运行时日志控制台

`UPandaLogConsole` 是 UPandaFramework 自带的运行时日志面板：捕获 `Debug.Log` / `Warning` / `Error`，在游戏内以 IMGUI 显示，支持严重级别过滤、搜索、折叠、复制、导出和堆栈查看。图标使用项目自有资产（`Icons/`，由 `LogConsoleIconLoader` 绑定；缺失时退回纯文字），不依赖第三方贴图、GUISkin 或日志插件。

## 装配

`UPGameRootEditor` 在 `config.EnableDebugModel = true` 时会在 `DebugerInit` 子物体下创建 `LogConsole` 节点，并写入 `UPGameRoot.logConsole`。运行时点击左上角 FPS 按钮即可打开/关闭。也可以手动挂载：

```csharp
var console = gameObject.AddComponent<UPandaLogConsole>();
console.Show();
```

`UPGameRoot.logConsole` 为空且 `EnableDebugModel` 开启时，运行时会自动创建文本模式实例；正式包建议关闭 `EnableDebugModel`。日志/诊断模块 `UPandaGF.Diagnostics` 的 `Auto Referenced`（`autoReferenced: true`）默认开启；`UPandaLogConsole` 组件位于 `UPandaGF.Runtime`，两个程序集均无需手动添加引用，`FrameworkConventionValidator` 会校验 `UPandaGF.Diagnostics` 的开关。

通过 UPM/Git URL 导入时，项目不一定已经定义 `OPEN_PLOG`，场景里也可能没有序列化控制台。编辑器同步不再依赖 `OPEN_PLOG`，并去掉了对只读包设置脚本执行顺序的操作；若场景仍缺少控制台，`DebugerInit` 会在 `EnableDebugModel` 开启且根节点就绪后自动创建文本模式实例。需要图标时，在 `UPGameRoot` Inspector 重新同步一次即可。

Inspector 使用 `UPandaLogConsoleEditor`：容量、行为、外观、运行状态和图标绑定均有中文标签/说明，并在 Play Mode 提供显示/隐藏、清空、打开显示设置/诊断的快捷按钮。

## 主要 API

| 成员 | 说明 |
| --- | --- |
| `Show()` / `Hide()` / `Toggle()` | 显示、隐藏、切换 |
| `Clear()` | 清空已存日志与待处理队列 |
| `SetFilter(string)` / `SetSeverity(info, warning, error)` / `SetCollapse(bool)` | 搜索与视图过滤 |
| `Capture(message, stack, LogType/LogConsoleLevel)` | 线程安全追加一条日志（测试或自定义日志管线） |
| `ProcessPending(budget)` | 主线程消费队列；`Update` 每帧自动调用 |
| `SnapshotVisible()` / `CopyVisible()` / `SaveVisible()` | 取当前可见记录、复制到剪贴板、导出到 `persistentDataPath/Logs` |
| `ShowTime` / `ShowScene` / `ShowMemory` / `ShowFps` | 日志行右侧列开关（会持久化） |
| `PauseCapture` / `FollowTail` | 暂停消费 / 自动跟随（运行态，不持久化） |
| `ClearOnSceneLoad` | 换场清空（持久化） |
| `PanelOpacity` / `PanelScale` | 面板不透明度 / 界面缩放（持久化） |
| `OpenSettings()` / `OpenDiagnostics()` | 直接打开显示设置 / 框架诊断页 |

容量字段（`MaxStoredEntries`、`MaxStoredBytes`、`MaxQueuedEntries`、`MaxQueuedBytes`、`MaxDrainPerFrame`）同时限制存储和线程队列；超过上限时淘汰最旧记录或丢弃新日志，并累计 `DroppedCount`。

## 页面与显示设置

- **日志**：搜索、级别过滤、清空/折叠/暂停/跟随/换场清空/复制/导出；宽屏左右分栏，窄屏上下分栏。
- **显示**：应用/设备/系统/图形/内存/构建/会话/用户/场景/缓冲信息；时间、场景、内存、帧率列开关；搜索、复制、导出、换场清空工具栏开关；暂停捕获、自动跟随；界面缩放与不透明度滑块。
- **诊断**：框架生命周期、模块状态、启动步骤耗时、Resources、AssetBundle、场景作用域、UI 面板计数，以及平台/Unity/场景/FPS/GC/日志统计。数据来自 `FrameworkDiagnostics.Capture(UPGameRoot.Instance)`，0.5 秒采样一次。
- 布局使用 `Screen.safeArea`；窄屏页签自动换行，操作按钮按屏宽自动换行，日志行按固定行高虚拟化，详情独立滚动。移动端默认按 DPI 放大，并支持在列表/详情/设置页拖拽内容滚动。滚动条改为 14px 竖向滑轨 + 圆角 thumb，移除上下/左右箭头图标；拖拽区域避开滚动条，滚动条和日志内容都可拖动。
- 显示设置持久化到 `PlayerPrefs` 的 `UPandaLogConsole.*` 键；暂停/跟随属于运行态，不写盘。

## 图标与皮肤

`Icons/` 存放项目自有的 36 张 PNG 与 `logConsoleScrollerSkin.guiskin`（原图标资产迁移，保留 GUID）。编辑器创建控制台时由 `Editor/PLogger/LogConsoleIconLoader.cs` 按模块相对路径绑定；`LogConsoleIcons` 为空时视图自动退回纯文字按钮。图标源稿、生成器和预览在 `Editor/PLogger/LogConsoleIconDesign/`。日志行使用选中行背景与级别图标，页签、操作按钮和诊断信息行使用对应图标。

## 线程模型

`Application.logMessageReceivedThreaded` 的回调只做有界入队，不访问 Unity 对象；`Update` 在主线程批量消费（默认每帧 256 条），场景名与 FPS 在消费时采样。折叠按“级别 + 消息 + 堆栈”聚合，搜索同时匹配消息与堆栈。

## 测试

[日志控制台回归](../../../Tests/PlayMode/LogConsoleRegressionTests.cs) 覆盖条数/字节淘汰、折叠计数、严重级别与搜索、待处理队列上限与线程突发、清空、可见区间计算、显示设置/诊断页冒烟；[图标绑定回归](../../../Tests/EditMode/LogConsoleIconTests.cs) 校验 PNG 与 GUISkin 的模块相对路径加载。
