# 框架示例

`UPandaGF.Samples` 引用 Contracts、Runtime、Extensions 和 TextMeshPro。该程序集没有 Editor-only 平台限制。业务场景若引用其中的组件，运行时仍需编译对应示例程序集并保留脚本 GUID。核心 Runtime 与 Contracts 可单独使用，Editor 中的示例构建器依赖 Samples。 [程序集](UPandaGF.Samples.asmdef) [迁移说明](../OptimzeLog/P1Framework/MIGRATION.md)

| 示例 | 如何使用 |
| --- | --- |
| [GameLaunchExample](Startup/GameLaunchExample.cs) | 挂在启动场景，与 UPGameRoot 同场景。等待成功事件后打开加载界面，可选顺序加载 `HybridCLRScriptsList`，再经 `ISceneService` 加载 `firstScene` |
| [TestPoolMgr](Pooling/TestPoolMgr.cs) | 用对象池演示预热、Task 或回调取实例、回收和统计。默认资源名是 `Obj1`、`Obj2`，可由对象池示例生成器布置场景 |
| [HowUseMonoMgr](Coroutines/HowUseMonoMgr.cs) | 展示普通 C# 类注册帧回调、托管协程和取消。同文件的 MonoBehaviour 在启用与禁用时管理 owner 回调 |
| [StateMachineExample](StateMachine/StateMachineExample.cs) | 用数字键切换状态，B 回退，T 打印状态树 |
| [TaskTriggerExample](Tasks/TaskTriggerExample.cs) | 转发 Unity `OnMouse*` 到交互任务实体。对象需满足相机拾取和 Collider 条件 |

## 启动示例约定

`GameLaunchExample` 在 Awake 订阅 `GFLoadedEvent` 和 `GFLoadedFailedEvent`。应在根完成前挂载，晚挂载不会补收已广播的事件。`firstScene` 推荐填写 Unity 项目内完整路径。Editor 直读模式由 Build Settings 提供场景，AssetBundle 模式可由清单提供。仅在 AOT 元数据、程序集和实际包产物已按业务配置时打开 `loadHotUpdateScripts`。`onAssemblyLoaded` 在列表加载完后回调。该示例提供调用顺序，项目的 HybridCLR 发布配置仍需单独准备。 [启动根文档](../Runtime/Composition/GameRoot/README.md) [场景服务](../Runtime/Core/Services/README.md)

## 分散的 Demo 与资源

任务系统的 `Demo`、交互评分的 `Example`、排序的 `Study` 以及旧 BinaryDataMgr、PlayerPrefs 的 `Test` 用法脚本通过 asmref 加入 Samples。后两个目录内的是会读写实际本地数据的演示脚本。Test Runner 的测试位于单独的 Tests 程序集。 [任务系统](../Runtime/Game/TaskSystem/README.md) [交互评分](../Runtime/Game/InteractiveTaskScoringSystem/ReadMe.md) [排序学习](../Runtime/Game/Study/README.md) [测试目录](../Tests/)

车辆、回放、对象池和评分场景可通过 Editor 工具生成，通用任务工具会向当前场景写入演示内容。QuickOutline 与 RobotCCDIK 的随附场景有旧资源引用，使用前按各自文档重建或重新绑定。生成后的场景是否随包交付取决于项目的 Build Settings、AB 清单与资源规则。 [车辆示例](../Runtime/Game/VehicleSystem/README.md) [回放示例](../Runtime/Game/ReplaySystem/README.md) [对象池示例生成器](../Editor/ObjPoolEditor/ObjPoolDemoBuilder.cs) [描边示例](../Runtime/Game/QuickOutline/README.md) [机械臂示例](../Runtime/Game/RobotCCDIK/README.md)
