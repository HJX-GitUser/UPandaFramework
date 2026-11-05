# AB、对象池、音频生命周期修复记录

日期：2026-11-05。基于本目录原审查结论，按 **AB → 对象池 → 音频 → 联合验证** 顺序修复。原审查的 8 项 P1、3 项 P2 均已处理；联合回归另发现并修复一项 PublicMono 监听器错误。

## 修复范围

| 原编号 | 问题 | 当前行为 |
|---|---|---|
| 1 | 依赖位置继承目标包位置 | 提供清单时逐依赖使用配置的 loadPath；缺失配置明确失败。未提供清单的底层调用保留兼容规则 |
| 2 | Task / 回调重复原生加载 | 共用进行中操作，回调适配同一 Task；同名同来源只启动一次原生加载 |
| 3 | Clear 后旧 Task 写回缓存 | 先失效上下文并完成等待者，旧结果只清理；外层 AssetsLoader 就绪状态跟随底层 |
| 4 | 宿主销毁后回调悬挂 | 清理时以 null 完成等待者；销毁不依赖协程自然结束 |
| 5 | Dispose 后预热复活旧池 | Dispose 为终止状态；Clear / Dispose 取消旧条目的请求，迟到资源只归还 |
| 6 | IPoolable 异常污染池状态 | OnDespawn 失败允许重试；OnSpawn 失败销毁并返回 null；拒绝同对象回调重入归还 |
| 7 | 旧音频请求覆盖新播放意图 | 字符串、直接 AudioClip、同曲提前返回入口均推进请求代次 |
| 8 | StopAllSound 后待加载音效仍播放 | 同批待加载音效以 null 完成一次，迟到资源归还；取消回调可发起新请求 |
| 9 | ClearAB 影响外部拥有的包 | 仅卸载本管理器拥有的包，不调用全局 UnloadAllAssetBundles |
| 10 | 对象池资源引用无法配对 | 每个路径与加载方式共持有一次预制体引用；借出实例持有子凭据，最后一个实例销毁后归还 |
| 11 | PublicMono 重建后音频不更新 | 记录实际宿主身份，再次调用播放入口时重新绑定；Dispose 从原宿主解绑 |

PublicMono 的追加修复：原派发逻辑把 owner 为 null 的强引用监听器也移除了。现在显式区分“未提供 owner”和“指定的 Unity owner 已销毁”；Update / FixedUpdate / LateUpdate 共用修复后的监听表。回归实际等待帧推进，确认音频淡变完成，而非仅断言注册数量。

## 跨模块持有与清理

- `AssetLease<T>` 是内部主线程共享持有凭据，不改变公开 Load / UnLoadAsset 接口。Resources 释放绑定到原加载器与原缓存记录，旧实例不能误减 ClearDic 后新记录的计数。
- 对象池的条目与实例分别持有凭据。Clear 销毁空闲实例并释放条目，借出实例继续有效；旧代实例归还时销毁。Release 后通过新管理器归还旧管理器的借出实例也会销毁并归还持有。
- AB 原生文件加载与资源加载无法中途停止时，先结束托管等待者，再等待原生操作收尾。同名新加载等待旧操作退出；原生资源请求完成后才卸载其包容器。
- 音频停播先解除 AudioSource.clip，再释放持有。Dispose 先清理状态，最后通知取消，避免取消回调重入复活旧实例。

## 兼容性变化

公开方法、重载及主要配置入口保留，但以下行为需要调用方遵守：

1. Clear / 销毁导致的异步取消返回 null，调用方必须判空。回调可能同步完成，应允许重入。
2. 对象池 Clear 后允许继续使用；Dispose 后应通过 Release / Instance 取得新管理器。音频 Dispose 同样为终止状态。
3. 新建池实例也会执行 OnSpawn；预热执行配对的 OnSpawn / OnDespawn。
4. 池内对象及借出实例的 Resources 引用由管理器配对，不应再额外手动 UnLoadAsset 抵消池的加载次数。
5. 池的 loadTimeout 同时作用于 Resources 与 AB，<=0 关闭池层超时。AB 网络 requestTimeout 默认 30 秒，0 关闭网络超时。
6. 同名 AB 的来源冲突会失败，切换来源应先 Init / 清理。UnLoadAB 在包或资源请求仍进行时返回 false。

模块细节见 ABLoadMgr、ObjPool、AudioMgr、AssetsMgr 与 PublicMono 各目录的 README。

## 验证与证据

验证使用独立临时 Unity 工程，未关闭用户的 Unity 编辑器，未改动原项目场景与业务资源。环境：**Unity 2022.3.48f1c1，Windows x64**。

| 验证 | 结果与覆盖 |
|---|---|
| Unity PlayMode 正常行为回归 | **50/50 通过，0 失败，0 跳过**；26 项新增生命周期用例，24 项原有用例 |
| 原生 AB | 真实构建主包、预制体与材质依赖包；混合来源、Task/回调并发、延迟 HTTP 取消、宿主销毁、资源加载中清理后重建、外部包所有权 |
| 对象池与 Resources | 并发单次持有、空闲清理、借出保留、最后实例销毁释放、旧代归还、跨管理器归还、异常与重入、超时与迟到结果、旧凭据不减新缓存 |
| 音频与调度器 | 最新意图优先、同曲提前返回取消旧请求、停止/Dispose 完成回调、取消回调重入、宿主重建后实际逐帧淡变、共享音频对照 |
| Windows Development Player | 构建成功；独立进程冒烟 **PASS，退出码 0**：AB 并发共用、外部包保留、共享预制体持有、借出实例生命周期、音频帧更新 |
| 原工程 C# 编译 | `dotnet build UPandaGF.Editor.csproj --no-restore`：0 错误，7 警告 |
| 差异与编码 | git diff --check；修改的 C# 为 UTF-8 BOM + CRLF，补齐新增文件的 Unity meta |

正式回归源码：`Tests/PlayMode/LifecycleRegressionTests.cs`、`LifecycleFixtures.cs`。夹具在测试前生成唯一 Assets 目录，测试后仅删除本轮生成目录；AB 产物留在 Temp 中。用 Unity Test Runner 运行 `UPandaGF.Tests` 的 PlayMode 测试即可复验；原生夹具当前针对 Windows Editor。

可保留的结果文件：

- [regression-results.xml](regression-results.xml)：正常行为回归 50 项结果。
- [player-smoke-result.txt](player-smoke-result.txt)：Windows Player 冒烟结果。
- 原 `probe-results.xml` / `observations.jsonl` 保留为修复前复现证据，不代表当前行为。

项目根目录的临时验证记录：`Temp/lifecycle-fix-verified.log`、`Temp/lifecycle-player-build-verified.log`、`Temp/lifecycle-player-smoke.log`；临时 Player 位于 `Temp/LifecycleAuditProject/LifecycleBuild/`。Player 用 `-batchmode -nographics` 运行，日志中的 Null 图形设备 Shader 不支持提示不属于本次逻辑验证；未验证画面或听感。

## 仍保留的边界

- AB 包仍由 UnLoadAB / ClearAB 显式管理；未引入自动依赖引用计数、版本热切换或完整消费者卸载策略。Unload(false) 保留已加载资产，整体未使用资源回收仍由业务在合适时机安排。
- ResourcesLoader.ClearDic 是全局清理入口，调用方应先停止消费者；旧凭据身份校验只防止误减新记录，不保证全局强制清理后的资源继续可用。
- Pool 根节点仍属场景；借出对象跨场景持久化由业务处理。PoolInstanceLease 为内部组件，不应单独移除。
- 未验证 Android / iOS / WebGL、远端异常的全部组合、长时间内存曲线、所有共享依赖拓扑。本轮通过表示所列生命周期回归与 Windows 冒烟通过。
