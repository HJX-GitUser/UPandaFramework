# P0：启动、关闭与状态机优化记录

日期：2026-11-06。范围：P0；未实施 P1/P2/P3。

## 改动模块

| 模块 | 原问题 | 实施 |
| --- | --- | --- |
| GameRoot | 启动仅有事件入口；热更执行与组件装配混杂 | 增加 StartupLifecycle，提供 InitializeAsync、WaitUntilReadyAsync、ShutdownAsync；抽离 ContentUpdateService |
| 内容更新 | 根节点直接控制批次下载、事务、恢复、统计 | 服务接收配置与 Downloader，保留原下载校验、事务恢复和旧版本降级逻辑 |
| AssetsLoader | 缺少显式关闭入口 | Shutdown 失效初始化代次、清除 AB 状态及清单；销毁复用该入口 |
| UIManager | 清理仅位于销毁回调 | 幂等 Shutdown，使待打开请求返回 null，销毁面板；销毁复用该入口 |
| StateMachine | 返回历史被用作当前上下文，ID/路径跳转约束不同 | 活动叶与历史分离；预校验路径；统一祖先约束；成功后记录历史；跨分支激活祖先 |
| BaseState | 非 BaseState 子节点的路径拼接优先级错误 | 为回退表达式加括号 |

## 启动与关闭协议

StartupLifecycle 是主线程协调器，按显式注册顺序启动：Configuration → Logging → Data → ContentUpdate → Assets → UI。
步骤开始前登记回滚，因此步骤自身只完成部分初始化后抛错，也执行其释放回调。失败后不启动后续步骤。
逆序执行所有已进入步骤的清理；一个释放失败不会阻止其他释放，异常以 AggregateException 保留。
配置、日志初始化器和数据初始化器目前没有独立的外部资源释放动作，不伪造 Dispose。
根节点只拥有这里装配的启动服务，不会清空进程内所有事件、音频或其他全局单例。

状态：Created → Initializing → Ready；初始化失败为 Failed；关闭为 Stopping → Stopped。
同一协调器只执行一次初始化与关闭，失败后重复初始化返回原失败；关闭后需重新创建根节点，未增加原地重试。
取消一个调用者的等待不会取消共享启动；ShutdownAsync 才会取消整个启动生命周期。
ShutdownAsync 等待启动终止后执行逆序释放。已经交给 Unity 的原生 I/O 不保证立即停止，等待其终态后才完成关闭。
OnDestroy 无法阻塞 Unity，故只发起关闭并观察异常；主动卸载前应 await ShutdownAsync。
应用进程退出时 Unity 不保证继续调度异步代码，因此不能把退出回调当作可靠的异步落盘入口。

成功、失败事件保留；晚到调用者使用 Task，不需要依赖事件补发。取消不广播失败事件。
GetAssetsLoader 在未就绪、失败或关闭后抛 InvalidOperationException，这是有意收紧的接口行为。
UI Shutdown 是终态，不支持在同一 UIManager 上重新初始化。
资源关闭不强制同步等待所有原生 AssetBundle 请求；底层 ABLoadMgr 继续负责迟到请求与原生资源回收。

```csharp
var root = UPGameRoot.Instance;
await root.WaitUntilReadyAsync(cancellationToken); // 只取消当前等待
var assets = root.GetAssetsLoader();
// 离开应用运行环境前：
await root.ShutdownAsync();
```

## 状态机协议

ID 在一台状态机中唯一。支持 `A1`、`A/A1`、`Root/A/A1`；路径每一段必须为直接父子关系。
校验完整路径后，从真实活动叶向根逐层执行 CanTransitionTo(最终目标ID)，ID 与路径入口一致。
未通过路径或约束校验时，没有进入/退出副作用，也不修改历史。
跨分支切换按根至目标的路径激活祖先；新进入分支的默认子状态仍由 OnEnter 负责。
显式返回已经活跃的祖先会关闭其活动后代；重复切换当前叶不会重入。
历史保存成功切换前的活动叶。GoBack 成功才移除历史，失败保留历史；返回不再新增历史。
进入、退出、约束判断和变更通知期间拒绝重入切换。监听器异常隔离。
生命周期回调抛错时尝试恢复旧活动结构并记录异常，但无法撤销业务已执行的外部副作用；回调应避免抛错。
销毁退出当前根与其活动后代，清空历史和监听器。
公开 children/Parent 等兼容入口仍保留；调用者不得绕过管理器破坏树结构，此类封装收紧留待后续阶段。

## 验证

新增 Tests/PlayMode/P0RegressionTests.cs，覆盖共享启动、逆序关闭、失败步骤回滚、启动取消、独立等待取消、释放异常继续清理、根节点晚等待及中途销毁，以及状态机路径/ID约束、跨分支、连续返回、无效路径、重入和默认子状态。
使用 Unity 2022.3.48f1c1 的隔离验证项目执行，不退出或改动正在打开的主项目场景。
首轮发现隔离项目缺少日志配置，已为启动测试增加配置备份、写入和恢复夹具。
最终结果见本目录 VALIDATION.md。
