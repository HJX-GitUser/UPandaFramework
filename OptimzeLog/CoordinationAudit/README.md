# HTTP、事件、公共调度与场景切换联合审查及修复

日期：2026-11-06。源码基线：`89f63a0`。流程：目标行为复现 → 修复 → 正常行为回归 → 独立 Windows Player 冒烟。

## 范围

完整检查并修改 `HttpClient.cs`、`HttpManager.cs`、`HttpPack.cs`、`EventCenter.cs`、`EventBus.cs`、`PublicMono.cs`、`SceneMgr.cs`。沿调用链检查单例宿主、场景进度事件、PLogger 以及现有资源、启动、音频的调度调用。未修改业务场景、预制体或第三方插件。

测试和构建使用项目根目录 `Temp/LifecycleAuditProject` 隔离工程，不关闭用户的 Unity 编辑器。

## 复现与修复

| 模块 | 复现结果 | 修复后行为 |
|---|---|---|
| HTTP 请求头 | 请求级 Authorization 被全局默认值覆盖 | 全局值只填补缺省项，保留请求级配置 |
| HTTP 请求所有权 | 同一 HttpPack 能重复进入队列，并复用同一原生请求 | 数据包只能提交一次；拒绝重复与终态包，跨客户端提交也受约束 |
| HTTP 宿主销毁 | 排队和发送中的包没有完成清理，原生请求仍保留 | 停用/销毁结束所属旧批次并释放句柄；停用后重新启用可提交新包 |
| HTTP 回调异常 | 成功回调抛异常会转为失败；失败回调抛异常可能被调用两次 | 解析与业务回调分开；成功、失败、重试和进度的多播回调分别隔离异常 |
| HTTP 重试收尾 | 重建请求抛异常导致队列不能继续；OnRetry 取消后仍创建请求 | 槽位清理不依赖重建成功；下次真正发送前才创建，并再次检查取消与宿主状态 |
| HTTP 暂停重试 | timeScale=0 时 RetryDelay 不推进 | 重试等待采用非缩放时间，取消可以结束等待 |
| EventCenter Once | 较早监听者递归触发同一事件，外层旧快照又调用一次 Once | 一次性执行标记覆盖递归快照，保持先退订再回调 |
| EventCenter owner | 同轮前序回调销毁 owner 后仍会调用它；已销毁 owner 注册退化为无 owner | 调用前检查存活；拒绝已销毁 owner 注册 |
| EventBus Unity 订阅者 | MonoBehaviour 已销毁但接口引用仍存在，被继续调用且计数仍为 1 | 弱引用判定同时检查 Unity 原生存活状态 |
| 事件 Clear | 清空容器后旧局部快照继续派发 | Clear 推进批次代次，停止本轮尚未调用的旧监听者 |
| PublicMono 嵌套异常 | yield 出的子 IEnumerator 抛异常，外层 Task 悬挂 | 同一驱动接管迭代器栈，根与嵌套异常都进入原 Task |
| PublicMono 资源释放 | 取消只停止包装协程，已进入的子协程 finally 不执行 | 完成、失败、取消、超时均按内到外 Dispose 已进入的迭代器 |
| PublicMono 启动边界 | 预先取消仍执行用户代码；未激活宿主与首次 MoveNext 停用会留下等待 | 先验证可用状态，再登记任务，最后启动首次迭代；拒绝时返回已取消 Task |
| PublicMono 停止与重入 | 原 StopAllCoroutines 不结束托管 Task；完成续体可改变取消遍历的列表 | PublicMono 停止入口结束托管任务；可复用快照保护遍历，整批停止期间拒绝重入创建 |
| 帧监听重入 | 移除、重新注册、再次移除时可能重复找到旧标记；嵌套派发压缩影响外层 | 移除忽略旧标记，按派发深度决定压缩时机 |
| SceneMgr 失败/销毁 | 无效场景或宿主销毁后，轮询 isLoaded 的 Task 不结束 | 直接等待 PublicMono 托管任务；失败、取消、超时有明确终态 |

另补充验证资源收尾：已销毁 owner 从列表移除后，旧缓存快照仍强引用委托。现在快照失效时立即清空缓存引用，当前局部快照只存活到本轮派发结束。GC 对照用例确认回调载荷可回收。拒绝提交的已取消请求、创建请求期间销毁宿主也已纳入原生句柄清理。

Clear 立即终止本轮余下派发、RetryDelay 使用非缩放时间、同包只能提交一次，是本轮明确的行为约定；不把所有目标行为用例都视为旧版本已有的文档承诺。普通事件增删仍维持原快照语义：本轮已在快照中的普通退订对象仍会收到消息，后续派发才移除。

## 对调用方的影响

- HTTP 原公开入口和委托保留，新增 `HttpPack.IsCompleted`。它只表示客户端结束了请求，不区分成功/失败；成功与失败回调前已设置终态并释放并发槽位。
- HTTP 取消保持静默，不调用 OnSuccess / OnFailure / OnRetry。把这些回调包装为 Task 时，调用方仍需明确处理取消，不能等待一个永远不会执行的成功/失败回调。
- GenericHttpPack 的业务回调异常不再被当成 JSON 解析异常或请求失败。回调异常通过 Debug.LogException 记录，不妨碍其它多播监听者。
- SceneMgr 保留全部旧 Asyn 重载，新增 `LoadSceneAsync(sceneName, mode, cancellationToken, timeoutSeconds)`。旧回调仅成功时执行；需要处理失败/取消时使用 Task 入口。
- SceneMgr 取消/超时只终止等待与本次进度事件，不能撤销 Unity 已提交的原生场景加载；Single 场景仍可能切换，不承诺回滚或自动卸载。
- 托管协程使用 CancellationToken 或 PublicMono 类型上的 StopAllCoroutines。通过 MonoBehaviour 基类或反射绕过入口的原始停止 API，以及原始 StartCoroutine 返回的子 Coroutine 句柄，不提供嵌套 Task 结束与异常传递保证。
- 公共 API 仍在 Unity 主线程调用。CancellationToken 可以从工作线程请求取消，迭代器释放与 Task 通知回到 Unity 主线程执行。

## 验证结果

环境：**Unity 2022.3.48f1c1、Windows x64、Mono Development Player**。

| 阶段 | 结果 |
|---|---|
| 旧版本目标行为复现 | 22 项用例全部不满足目标行为，保留失败 XML；其中暂停重试还表现为预期的最终失败通知未出现 |
| 扩展资源收尾检查 | 2 项失败检查推动补齐快照强引用释放与拒绝提交的原生请求清理；保留修复前结果 |
| 最终 PlayMode 正常行为回归 | **92/92 通过，0 失败，0 跳过**；42 项本轮新增，50 项前轮回归 |
| Windows Player 构建 | 成功，日志包含 `COORDINATION_PLAYER_BUILD_SUCCEEDED` |
| 独立 Player 冒烟 | **PASS，退出码 0**：真实 HTTP 暂停时重试、HTTP 宿主销毁清理、递归 Once、Unity EventBus 对象销毁、协程释放/取消、真实场景完成与观察者宿主销毁 |
| 原工程 C# 编译 | `dotnet build UPandaGF.Editor.csproj --no-restore`：0 错误、7 警告 |
| 差异与文件约定 | git diff --check；修改的 C# 保持 UTF-8 BOM + CRLF，补齐新增文件 meta |

正式用例在 `Tests/PlayMode/CoordinationRegressionTests.cs`，运行 UPandaGF.Tests 的 PlayMode 测试即可复验。HTTP 使用仅监听 loopback 的动态端口测试服务器，不依赖外部服务或管理员 URL 绑定。测试场景在唯一 Assets 目录中生成，结束后删除本轮目录并恢复原 Build Settings。

本目录证据：

- [baseline-results.xml](baseline-results.xml)：旧版本复现的失败结果，**不是通过的回归结果**。
- [baseline-tests.cs.txt](baseline-tests.cs.txt)、[baseline-fixtures.cs.txt](baseline-fixtures.cs.txt)：复现源代码归档；COORDINATION_BASELINE 仅调整旧实现的预期日志类型。
- [release-checks-before.xml](release-checks-before.xml)：扩展收尾检查修复前结果。
- [regression-results.xml](regression-results.xml)：最终 92 项正常行为回归。
- [player-smoke-result.txt](player-smoke-result.txt)、[player-smoke.cs.txt](player-smoke.cs.txt)、[player-builder.cs.txt](player-builder.cs.txt)：独立 Player 结果及临时驱动/构建源码。

项目根目录临时记录：`Temp/coordination-baseline-verified.log`、`Temp/coordination-regression-verified.log`、`Temp/coordination-player-build.log`、`Temp/coordination-player-smoke.log`。Player 位于 `Temp/LifecycleAuditProject/CoordinationBuild/`，以 `-batchmode -nographics` 运行；不代表渲染或业务场景已经验收。

本轮未验证 Android/iOS/WebGL、IL2CPP、全部服务端异常组合与长时间性能曲线。未增加跨线程事件投递、HTTP 缓存/去重或原生场景加载回滚。
