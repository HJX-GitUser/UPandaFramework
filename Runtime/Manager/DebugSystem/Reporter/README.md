# Reporter 运行时日志面板

`Reporter` 是常驻的 IMGUI 日志面板。它监听 Unity 日志，提供过滤、折叠、查看调用栈、导出和框架诊断。显示与捕获独立，隐藏面板时，只要 Reporter 组件仍启用就会继续接收日志。 [Reporter.cs](Reporter.cs)

## 放置和打开

- 使用 `UPGameRoot` Inspector 的调试模式创建或绑定 Reporter，确认引用指向场景中的常驻 Reporter。
- 独立使用时，将 Reporter 放在场景顶层对象。`Awake` 会调用 `Initialize()`，同一时间只保留一个实例。
- 调用 `ShowLogWindows()` 或 `HideLogWindows()` 控制界面。FPS 入口由 `DebugerInit` 提供。运行时的过滤可通过 `SetFilter(string)`、`SetCollapse(bool)` 和 `SetSeverityFilters(bool, bool, bool)` 配置。

## 界面与数据

| 页签 | 内容 |
| --- | --- |
| 日志 | 按日志级别和文本筛选，折叠重复项，查看选中条目的时间、场景、消息与调用栈。支持暂停滚动查看、复制和保存。 |
| 设置 | 配置展示与过滤选项。偏好存入 PlayerPrefs。 |
| 框架诊断 | 每 0.5 秒刷新启动状态和步骤耗时，显示 Resources、AB、场景、UI 等计数。没有启动根的场景打开此页时，`UPGameRoot.Instance` 可能创建默认根。 |

手动保存将保留中的日志写入 `Application.persistentDataPath/logs.txt`。导出范围受当前保留预算约束。WebGL 端另需验证虚拟文件系统与 IndexedDB 的持久化行为。 [界面实现](Reporter.View.cs) · [日志文件保存](Reporter.cs) · [平台存储](../../AssetsMgr/PlatformStorage.cs)

## 预算与生命周期

线程日志先截断到 `maxMessageLength`，再受 `maxQueuedLogs` 和 `maxQueuedMemoryMB` 限制。队列超过预算时会淘汰较早记录。主线程每帧最多接收 `maxLogsPerFrame` 条，保留集合继续按 `maxLogCount` 与 `maxSize` 淘汰最早记录。内存值是日志对象和文本的估算。`DroppedLogCount`、`PendingLogCount` 与 `RetainedLogCount` 可用于观察积压。

`OnDisable` 退订 Unity 回调，`OnEnable` 重新订阅，`OnDestroy` 释放视图资源。旧的 `ReporterMessageReceiver` 仍可接收 `OnPreStart` 和日志通知，观察回调在主线程排队处理时执行。 [存储和折叠](ReporterLogStore.cs) · [兼容观察组件](ReporterMessageReceiver.cs)

## 验证

在 PlayMode 中可检查隐藏时捕获、筛选、队列上限、销毁后退订及重复初始化。历史跨平台探针包含独立 Player 的日志基本路径，显示效果与平台导出仍应以目标设备为准。 [Reporter 回归](../../../../Tests/PlayMode/ReporterRegressionTests.cs) · [跨平台验证](../../../../OptimzeLog/CrossPlatform/README.md)
