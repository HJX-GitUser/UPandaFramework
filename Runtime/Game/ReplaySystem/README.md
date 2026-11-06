# ReplaySystem 录制与回放

支持目标注册或周期发现、有界录制、Transform / Animator float / 自定义组件状态、变速暂停、Seek 及版本 2 JSON 持久化，兼容旧 NRBF 录制。

## 基本使用

```csharp
using ReplaySystem;

manager.recording.targets = ReplayTargetMode.Registered;
manager.RegisterTarget(player.transform, includeChildren: true);
manager.StartRecording();
// ……
manager.StopRecording();
string path = await manager.SaveAsync();
if (path != null && await manager.LoadAsync(path))
    manager.StartPlayback();
manager.Seek(0.5f);
// 不再保留本次数据：
manager.ClearRecording();
```

旧的 StartRecording / StopRecording / StartPlayback / PausePlayback / ResumePlayback / StopPlayback / Stop / SetSpeed / Seek / Save / Load API 保留。同步 Save / Load 仍会阻塞调用线程；ReplayUI 和自动保存已使用异步入口。公开 Manager API 与 SaveAsync 应在 Unity 主线程发起。

## 目标与采样

- `recording.targets=Scene`：默认每秒发现一次激活 Transform，继续默认排除 Canvas 与子物体。
- `Registered`：只遍历 RegisterTarget 的目标和可选子层级，不进行全场景搜索。重复注册会去重，UnregisterTarget 移除范围。
- `RefreshTargets()`：请求下次采样刷新新物体、层级路径、Animator 控制器和参数；两种模式都按 discoveryInterval 周期刷新。
- 默认 30 次/秒；samplesPerSecond=0 每个 LateUpdate 采样。录制使用游戏时间，暂停时间不会持续添加重复帧。
- 变化检测后才创建快照；静止末尾空帧合并。状态变化前保留保持锚点，Seek 不会把长静止段插值成持续移动。

## 录制预算

默认时长 600 秒、18,000 帧、250,000 个快照引用、64 MiB 估算数据量。字段位于 `recording`，各预算 0 表示关闭对应限制。达到上限停止并保留完整前缀，通过 LastRecordingStopReason 查看原因；不会覆盖最早帧。

估算字节不是进程或托管堆的硬上限。大型自定义状态应实现 IReplayMemoryEstimator，并且 Capture 返回不可变状态或独立副本；自定义扩展可实现 IReplayChangeRecorder 避免无变化时反复装箱。数量预算与估算预算同时生效。

停止/禁用释放采样缓存，保留录制供回放；ClearRecording 取消文件操作并释放数据、索引、绑定缓存与主要容器容量，保留目标注册；销毁执行清理。重新录制取消旧文件操作。Statistics 提供采样、扫描、快照和估算容量等统计。

## 播放与持久化

时间索引引用已有快照，顺序播放使用游标，逆向 Seek 使用二分；历史稀疏 Animator 状态在索引时补全。回放冻结物理并临时设定所需 Rigidbody.isKinematic，停止/禁用/销毁恢复状态。同一时刻只应有一个管理器控制全局物理回放。

文件继续写 `UPANDA_REPLAY:2` JSON，新增可选 animatorStateComplete 标记，旧文件缺失时按稀疏参数处理；旧 NRBF 仅只读兼容。分块保存与逐帧解析避免构造整个文件的 JSON/DTO 副本。异步保存固定调用时的帧前缀，重复保存合并；最新异步加载生效，失败保留现有数据。取消清理当前临时文件，完成后才发布 .dat。

自定义状态保存/加载前注册 `FileManager.RegisterComponentState<MyState>(key)`；DTO 必须是 JsonUtility 支持的纯数据，不持有 Unity 实例引用。调用方不得在保存期间直接修改快照或其字典。

单文件上限 128 MiB，逐帧解析上限 16 Mi 字符，写入另有单帧 16 MiB UTF-8 字节限制；单快照组件 JSON 合计上限 1 Mi 字符。旧文件超过当前 Manager 预算会拒绝加载，需提高预算后重试。默认设置与旧每帧扫描行为不同，需要旧节奏可把采样率、发现间隔设为 0。

instanceID 用于同会话绑定，跨会话使用场景/层级路径和唯一名称回退；层级变化后可能无法绑定。不重建已销毁对象，不恢复 Active 状态或 Animator 控制器资产。录制数据整体仍在内存中，不提供无限录制、环形覆盖或磁盘流式播放。

## 验证

Unity 2022.3.48f1c1：230/230 PlayMode 回归、独立 Windows/Mono Player 均通过；移动端、IL2CPP 和 WebGL 尚未验证。WebGL 仅保留单线程兼容，不保证后台性能。

完整复现、预算说明、测量及使用约定见 [回放性能优化记录](../../../OptimzeLog/ReplayPerformance/README.md)。
