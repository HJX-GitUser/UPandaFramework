# 场景录制与回放

`ReplayManager` 录制 Transform、Animator 的 float 参数及注册的组件状态，支持变速、暂停、跳转和持久化。代码在 `UPandaGF.Gameplay` 的 `ReplaySystem` 命名空间内。核心可单独挂在场景对象，`ReplayUI` 可选，用 UGUI 绑定按钮、进度条和速度控件。 [回放管理器](Runtime/ReplayManager.cs) [回放 UI](Runtime/ReplayUI.cs)

## 从目标录制到播放

```csharp
using ReplaySystem;

// manager 是场景中的 ReplayManager，player 是要录制的对象。
manager.recording.targets = ReplayTargetMode.Registered;
manager.RegisterTarget(player.transform, includeChildren: true);
manager.StartRecording();
// 操作结束后调用
manager.StopRecording();

string path = await manager.SaveAsync();
if (path != null && await manager.LoadAsync(path))
{
    manager.StartPlayback();
    manager.Seek(0.5f);
}
```

在 Unity 主线程调用管理器。默认 Scene 模式每秒重新发现活动 Transform，且排除 Canvas 与其子物体。Registered 模式只查登记的目标及选定的子树。使用 `UnregisterTarget` 取消登记，`RefreshTargets()` 请求在下次采样刷新路径、Animator 和目标。录制用游戏时间，默认 30 次/秒，`samplesPerSecond = 0` 改为每个 LateUpdate 采样。系统只为变化创建快照，并保留静止区间的锚点。 [录制实现](Runtime/ReplayManager.Recording.cs) [录制参数](Runtime/ReplayRecordingSettings.cs)

## 预算、扩展和释放

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| `maximumDuration` | 600 秒 | 录制的游戏时长 |
| `maximumFrames` | 18,000 | 录制帧数 |
| `maximumSnapshots` | 250,000 | 快照引用数 |
| `maximumMemoryMB` | 64 MiB | 数据量估算值 |

各项预算为 0 时关闭该项。触顶停止并保留完整前缀，可用 `LastRecordingStopReason`、`Statistics` 查看原因和统计。估算量不能作为进程内存上限。自定义组件可注册 `IRecordableComponent`，用 `IReplayMemoryEstimator` 提供估算或用 `IReplayChangeRecorder` 跳过无变化的采样。持久化自定义状态前调用 `FileManager.RegisterComponentState<MyState>(key)`，交给它的 DTO 应由 JsonUtility 支持且不持有 Unity 对象引用。Capture 返回不可变值或独立副本。 [组件状态契约](Runtime/IRecordableComponent.cs) [文件管理器](Runtime/FileManager.cs)

停止或禁用会释放采样缓存并保留录制数据。重新录制与 `ClearRecording()` 会取消在途文件操作，后者还释放时间索引、帧数据与绑定缓存，目标登记可继续使用。销毁会清空登记。 [运行状态](Runtime/ReplayManager.cs)

## 文件和场景边界

- `SaveAsync(CancellationToken token = default)` 与 `LoadAsync(string path, CancellationToken token = default)` 支持取消。保存固定调用时的帧前缀，重复保存合并。最新加载请求生效，失败保留原录制。同步 `Save()` 和 `Load(string path)` 会阻塞调用线程。
- 文件位于 `persistentDataPath/ReplayRecords`，采用版本 2 JSON 与临时文件提交，保留旧 NRBF 文件只读兼容。WebGL 异步保存会等待平台持久化，文件解析和写入仍受单线程限制。文件上限 128 MiB，单帧读取上限 16 Mi 字符、写入上限 16 MiB UTF-8 字节，单快照组件 JSON 合计读取上限 1 Mi 字符。加载还受管理器预算限制。
- 同会话按实例 ID 绑定，跨会话尝试场景名与层级路径，再回退到唯一名称。层级或名称变化可能导致目标丢失或错配。回放只驱动现有对象，不重建已销毁的对象，也不保存 Active 状态和 Animator 控制器资产。
- 回放期间修改全局物理开关并让录制目标的 Rigidbody 暂时变为 kinematic，停止时恢复。一个 Unity 进程同一时间只应由一个 ReplayManager 控制回放。录制帧仍保存在内存中。

[I/O 入口](Runtime/ReplayManager.IO.cs) [播放与对象绑定](Runtime/ReplayManager.Playback.cs) [流式文件实现](Runtime/FileManager.Streaming.cs)

## 验证

用 `UPandaGF → Runtime → ReplaySystem → 创建回放示例场景（新建场景）` 生成场景。现有回归覆盖采样预算、逆向 Seek、物理状态恢复、旧格式、异步文件和取消。历史隔离工程完成 Windows Mono Player 和回放专项回归，后续跨平台探针也记录了 Windows IL2CPP 与 WebGL 的核心回放结果。业务场景的目标绑定、帧耗时及 Android 真机流程需要单独核对。 [示例生成器](../../../Editor/ReplaySystemEditor/ReplaySystemDemoBuilder.cs) [回放测试](../../../Tests/PlayMode/ReplayPerformanceTests.cs) [回放历史数据](../../../OptimzeLog/ReplayPerformance/README.md) [跨平台历史数据](../../../OptimzeLog/CrossPlatform/README.md)
