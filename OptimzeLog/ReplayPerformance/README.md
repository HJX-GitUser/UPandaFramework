# 回放性能与内存优化

日期：2026-11-06。基线：`5b9fe9e`。沿用“复现问题 → 修复 → 正常行为回归”，保留 ReplayManager 的录制、播放、变速、Seek 与同步 Save / Load 入口，未改动业务场景或预制体。

## 复现与修复

| 旧实现的行为 | 当前实现 |
|---|---|
| 每个采样调用 FindObjects 搜索全场景 | Scene 模式默认每 1 秒发现一次；Registered 模式只遍历注册范围，不搜索全场景 |
| 静止物体也先生成快照、层级路径和 Animator 数组再比较 | 缓存目标、路径、Animator 参数名/哈希/数值，比较后才创建变化快照；静止 Rigidbody 扩展复用旧装箱值；缺失组件查询使用 TryGetComponent |
| 静止录制持续新增空帧 | 合并末尾空帧，保留时间跨度；发生变化前补保持锚点，避免跨静止区间错误插值 |
| 数据列表无限增长 | 增加采样率、帧数、快照引用数量、估算数据字节和时长预算；达到上限停止，保留完整的已录制前缀 |
| 播放索引再构造一份 ObjectSnapshot 和 Animator 字典 | 时间索引引用原快照；兼容旧文件的稀疏 Animator 状态，仅必要时合并；顺序播放使用游标，逆向 Seek 使用二分 |
| 重新录制仍保留旧索引；禁用仍保留采样缓存 | 新会话立即清理旧索引；停止/禁用释放采样缓存；ClearRecording / 销毁释放帧、索引、绑定缓存和主要容器容量 |
| 保存构造整份 DTO 和 JSON，读文件一次性读取整个 JSON | 维持版本 2 JSON：逐快照写入、逐帧解析；新增 SaveAsync / LoadAsync，保存按数量、字符量及主线程时间片分块 |
| 后到的文件结果可能覆盖新内容 | 清空/重新录制/销毁取消旧文件操作；并发保存合并；最新异步加载生效；取消删除当前唯一临时文件，完成后才发布 .dat |
| UI 每帧拼接文案并查找暂停按钮文字，销毁后监听仍可能留在外部按钮 | 按显示变化更新文案，缓存标签，移除自身监听；保存/加载按钮改用异步入口并显示忙碌状态 |

控制器切换时还会刷新 Animator 参数列表；新快照使用可选的 `animatorStateComplete` 字段区分完整状态与历史稀疏状态，避免已删除参数从旧索引混入。字段保存在现有版本 2 JSON 内，旧文件缺失时按历史稀疏语义读取；旧读取器会忽略此新增字段。

## 验证结果

- 旧行为复现：**4/4 不满足目标行为**，包括静止空帧增长、旧索引保留、禁用后缓存保留及静止采样分配。见 [baseline-results.xml](baseline-results.xml)；这是预期失败的复现证据，不是回归通过结果。
- 正常行为回归：**230/230 通过，0 失败、0 跳过**，其中本轮新增 39 项、前轮 191 项全部保留通过。见 [regression-results.xml](regression-results.xml)。
- 原工程编译：0 错误、7 项已有警告；C# BOM/CRLF、Git 空白及框架 meta GUID 唯一性检查通过。
- Windows x64 Development Player 构建成功并实际运行通过。覆盖注册范围、1000 个无关物体、静止段插值、逆向 Seek、真实 Animator 参数、异步版本 2 文件、旧 NRBF 文件、销毁取消，以及 20 轮有界录制/清空。见 [player-smoke-result.txt](player-smoke-result.txt)。

### 测量

| 测量 | 基线 | 优化版 |
|---|---:|---:|
| Player：200 个静止目标，100 次采样，GC 分配块 | 161,200 | 0 |
| 同一次 Player 测量的调用耗时 | 49 ms | 6 ms |
| Editor 正式用例：200 个静止目标，100 次强制采样，GC 分配块 | 旧复现 240,401 | 100（来自测试内反射获取帧列表） |

Player 基线使用 `5b9fe9e` 的 ReplayManager 与 RigidbodyVelocityRecorder 源码副本，仅改类名，与优化版共用当前 DTO；计时包含两种实现各自的发现/采样方式。此测试展示目标范围、发现策略和分配变化，不是固定帧率、渲染或真机吞吐基准。

20 轮压力测试每轮模拟调用采样 600 次，录制到 512 帧预算后停止，再清空全部数据。累计 12,000 次采样调用；第 1/5/10/20 轮清空并测试性 GC 后，Mono 使用量分别为 2,940,928 / 724,992 / 724,992 / 724,992 字节。后续检查点稳定，不代表任意扩展或业务场景都无泄漏。GC.Collect 只在测试驱动中调用，生产代码不主动触发 GC。

本机 Mono 的 GC.GetAllocatedBytesForCurrentThread 实测恒为 0，未作为零分配依据；复现和正式用例均改用 Unity GC.Alloc Recorder 记录分配块数量，不把块数当作字节数。

## 默认配置与兼容性

| recording 字段 | 默认值 | 含义 |
|---|---:|---|
| targets | Scene | 周期发现激活物体；Registered 仅遍历 RegisterTarget 的范围 |
| samplesPerSecond | 30 | 0 表示每个 LateUpdate 采样；停止时补最后一次采样 |
| discoveryInterval | 1 秒 | 真实时间刷新目标、组件和路径；0 每次刷新。RefreshTargets 请求下次采样立即刷新 |
| maximumDuration | 600 秒 | 使用游戏时间；0 不限 |
| maximumFrames | 18,000 | 静止空帧合并后计算；0 不限 |
| maximumSnapshots | 250,000 | 包含保持锚点的快照引用数量；0 不限 |
| maximumMemoryMB | 64 MiB | 已录制数据的保守估算预算；0 不限 |

新默认值会改变采样频率、动态对象发现延迟和长录制停止条件。需要旧采样节奏时设置 samplesPerSecond=0、discoveryInterval=0；预算可按实际数据调整。旧文件超过当前 Manager 预算时明确拒绝并保留现有数据，需要提高对应预算后再加载。

内存预算不是整个托管堆或进程的绝对上限：它估算帧、快照、字符串和组件状态的保留量，不包含 Unity 原生对象、播放绑定缓存、所有容器开销与文件处理瞬时内存。自定义大状态必须提供 IReplayMemoryEstimator；无法反射猜测任意对象图的真实大小。帧数与快照数量另有确定的数量上限。

达到预算采取停止策略，不循环覆盖旧帧，不在此阶段引入持续落盘或磁盘流式播放。分块文件读写仍然加载完整的有效录制数据到内存后播放。

## 文件与线程约定

- 继续写 `UPANDA_REPLAY:2` + JSON，旧 NRBF DTO 仍由受限读取器只读兼容。自定义组件类型仍需显式注册，文件不能指定 CLR 类型执行反序列化。
- 单文件限制 128 MiB；单帧读取限制 16 Mi JSON 字符，写入还受 16 MiB UTF-8 字节限制；单快照全部组件 JSON 合计限制 1 Mi 字符。无效数据、超限和取消不发布部分 .dat。
- 同步 Save / Load 保留同步行为。UI 与自动保存使用新异步接口；FileManager.SaveAsync 与 Manager API 应在主线程发起。
- 异步保存先捕获帧引用、时间及快照数量，固定调用时的前缀；继续录制产生的新帧、补锚点和末尾时间更新不会混入这次保存。框架不修改已创建快照；调用方不能在保存期间直接修改快照或其字典，自定义 Capture 状态应不可变或返回独立副本。
- 主线程转换一批 DTO，默认最多 128 个快照、约 1 Mi 组件/身份字符或约 4 ms 的转换时间片后让出；后台只处理纯 DTO 的 JsonUtility 和文件 IO。单个自定义组件编码调用无法抢占，不保证每次调用严格小于 4 ms。
- 异步加载在工作线程逐帧解析；采用最新请求的结果，替换前做预算检查，转换估算阶段协作让出主线程。失败不清空已有录制。
- Unity 文档明确允许纯数据的 [JsonUtility.ToJson](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/JsonUtility.ToJson.html) 与字符串版 [FromJson](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/JsonUtility.FromJson.html) 在线程中调用；输入执行期间不可被改动。组件持久化 DTO 应只含纯数据，不依赖 Unity 对象实例引用或主线程回调。

## 使用入口

```csharp
manager.recording.targets = ReplayTargetMode.Registered;
manager.RegisterTarget(player.transform, includeChildren: true);
manager.recording.samplesPerSecond = 30;
manager.StartRecording();
// ……
manager.StopRecording();
string path = await manager.SaveAsync();
if (path != null && await manager.LoadAsync(path))
    manager.StartPlayback();
// 不再需要数据时：
manager.ClearRecording();
```

RegisterTarget / UnregisterTarget 控制注册范围，RefreshTargets 刷新动态层级和组件；ClearRecording 保留注册配置供下次使用。Statistics 提供采样数、全场景扫描数、访问目标数、生成快照数、估算字节、已保留快照引用和索引数量。createdSnapshots 包含最后被预算拒绝的分配尝试。

## 产物与尚未验证范围

正式用例：`Tests/PlayMode/ReplayPerformanceTests.cs`、`ReplayPerformanceFixtures.cs`。文件职责分为 ReplayManager 主控制、Recording、Playback、IO，以及 FileManager 的 DTO/接口与 Streaming，原 MonoBehaviour 文件与 meta 保留。

本目录包含复现、最终 XML、Player 驱动/构建器和基线源码副本、[源码摘要与汇总](verification-summary.json)。临时工程为项目根 `Temp/LifecycleAuditProject`，构建与运行日志为 `Temp/replay-player-build.log`、`Temp/replay-player-run.log`，未关闭原工程编辑器。

尚未验证移动端、IL2CPP、WebGL、真实业务场景的长期运行及所有自定义扩展。WebGL 保留单线程兼容路径，文件读取/解析不提供线程性能保证。回放继续恢复现有对象状态，不重建销毁的对象或还原 Active 状态、Animator 控制器资产及完整物理世界；同一时刻应只有一个管理器控制全局物理回放。层级/组件变化按发现周期生效，必要时显式刷新。

## 后续跨平台验证（2026-11-06）

本阶段之后已完成 Windows/WebGL 的 IL2CPP High 核心探针运行，以及 Android ARM64 构建；Android 真机、iOS 等仍未验证。修复和详细矩阵见 [跨平台记录](../CrossPlatform/README.md)。
