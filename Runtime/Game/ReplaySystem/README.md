# ReplaySystem 录制与回放系统

通用的「录制 / 持久化 / 回放」系统，用于记录场景中所有激活物体每帧的 Transform 与 Animator float 参数，并按时间轴回放。

## 文件清单

| 文件 | 说明 |
|------|------|
| `ReplayManager.cs` | 主控制器：录制、回放、变速、暂停/继续/停止、Seek 跳转 |
| `RecordFrame.cs` | 数据结构：一帧的录制数据（time + 快照列表） |
| `ObjectSnapshot.cs` | 数据结构：单个物体快照（instanceID / Transform / animatorParams） |
| `FileManager.cs` | 保存 / 加载本地文件（二进制序列化） |
| `ReplayUI.cs` | UI 绑定与事件处理 |
| `IRecordableComponent.cs` | 扩展接口（预留，用于 Rigidbody / ParticleSystem / AudioSource 等） |
| `RigidbodyVelocityRecorder.cs` | 扩展示例：录制 Rigidbody 速度 |

## 场景搭建步骤

1. **挂主控制器**：在场景中新建空物体（如 `ReplaySystem`），挂上 `ReplayManager` 组件。

2. **搭建 UI（Canvas）**：
   - `GameObject > UI > Canvas`；
   - 顶部一个 `Text`（显示 "00:00 / 00:00"）；
   - 一个 `Slider` 作为进度条（0~1）；
   - 一排 `Button`：录制、停止录制、回放、暂停/继续、停止回放；
   - 一个 `Slider` 控制速度（0.1x ~ 5x）+ 旁边 `Text` 显示倍速；
   - 一个「保存录制」按钮 + 一个「加载回放」按钮。

3. **挂 UI 脚本**：任意物体上挂 `ReplayUI`，把 `manager` 拖入上面的 `ReplaySystem`，再把各 UI 元素拖到对应字段。

## 使用流程

1. 点「录制」→ 操作物体 / 播放动画；
2. 点「停止录制」→ 数据就绪；
3. 点「保存录制」→ 写入 `Application.persistentDataPath/ReplayRecords/record_时间戳.dat`；
4. 点「加载回放」→ 编辑器下弹文件选择框；运行时回退到最近一次录制；
5. 拖速度 Slider 调倍速 → 点「回放」；
6. 回放中可拖进度条跳转、点「暂停/继续」或「停止回放」。

## 关键实现说明

- **录制优化**：每帧只记录「Transform 或 Animator 参数发生变化」的物体，与上一帧快照比较。
- **存储**：`List<RecordFrame>`，帧内为 `List<ObjectSnapshot>`，用 `BinaryFormatter` 二进制序列化。
- **回放**：按时间戳顺序播放，相邻两帧间做 `Lerp/Slerp` 插值；进度条拖拽即 `Seek` 到任意时间点。
- **物理**：回放时 `Physics.autoSimulation = false`，并将被记录物体的 `Rigidbody.isKinematic = true`；回放结束恢复。
- **物体标识**：以 `instanceID` 为主键；同时记录 `objectName`，跨会话加载时按名称回退匹配。

## 注意事项

- `instanceID` 仅在**当前运行会话内**稳定。跨会话（重新打开应用后）加载文件时，依赖 `objectName` 匹配，因此场景中物体名应尽量唯一。
- `BinaryFormatter` 在较新 .NET 中标记为过时，Unity 2021.3 仍可用；如需在 IL2CPP/Android 上长期维护，可将 `FileManager` 改为 JSON（JsonUtility + 纯字段 DTO）。
- 录制期间若物体被销毁，其缓存条目会失效；本系统面向「录制期间物体集合不变」的常见场景。
