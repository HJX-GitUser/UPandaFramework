# 交互任务评分系统

该模块用场景实体组织实训流程，支持按顺序或按完成数量推进操作、难度评分、引导、跳过和动作日志。代码位于 `UPandaGF.Gameplay`，命名空间为 `UPandaGF.Gameplay.InteractiveScoring`。步骤配置保存在 `TaskConfig`，`TaskProgress` 与 `TaskStepRuntime` 保存本轮运行状态。宿主借助框架 EventCenter 广播提示。 [任务配置](Data/TaskConfig.cs) [宿主与运行状态](Task/TaskDataManager.cs) [核心逻辑](Core/)

## 运行演示

在 Unity 菜单中选择 `UPandaGF → Runtime → 交互任务评分系统 → 创建演示场景`。生成器在 `Example/Demo` 下保存配置、实体预制体和场景。Play 后先点 A 再点 B，再以任意顺序点 C、D。G 引导，K 跳过当前步骤，S 保存动作日志。在同一菜单下可打开任务流程校验器、运行逻辑自检和回放复盘查看器。 [演示生成](../../../Editor/CustomInspector/InteractiveTaskScoringSystem/InteractiveTaskDemoBuilder.cs) [演示 HUD](Example/DemoTaskHud.cs) [Editor 工具](../../../Editor/CustomInspector/InteractiveTaskScoringSystem/)

## 搭建业务流程

1. 创建 `TaskConfig` 并填写步骤 ID、基础分等参数。在场景里只放置一个 `TaskDataManager`，将配置和 `TaskStepBase` 节点绑定；节点数需和配置步骤数一致。
2. 每个步骤下放一个顶层 `SeriesOperationGroup` 或 `ParallelOperationGroup`。串行按当前节点推进；平行组的 `completeCount` 为 0 或更小时需全部完成。组内可放叶操作或嵌套组。
3. 给叶操作配置 `OperatingStepID`，在对应实体的 `StepIDGroup` 中填同一 ID。编辑器的 Reset 可按父 ID 与同级序号生成操作 ID，重排层级后用校验器检查。交互对象必须把鼠标、射线或业务输入转给 `TaskEntityBase`，例如挂带 Collider 的对象并用 `TaskTriggerExample`。
4. 实体实现 `EnableInteractive`、`DisableInteractive`、`EnableGuide` 与 `Execute(UnityAction callback)` 等抽象方法。在动作真正结束时调用完成回调，否则操作会停留在执行阶段。需要额外判定时实现 `EntityOperationCheck`。
5. 在任务流程校验器中检查空组、重复 ID、缺失实体、数量或顺序错误。修改后重新进入 Play 复核推进与评分。

[实体接口](TaskInteractive/TaskEntityBase.cs) [串行操作组](Operation/SeriesOperationGroup.cs) [平行操作组](Operation/ParallelOperationGroup.cs) [鼠标触发示例](../../../Samples~/Tasks/TaskTriggerExample.cs)

## 评分与对外入口

- 默认评分下，0 次错误得基础分，1 次得一半，2 次及以上得 0。步骤完成后累加总分，跳过步骤不计分。可通过 `TaskDataManager.scorePolicy` 换成 `DifficultyScorePolicy` 或自定义 `IScorePolicy`。支持的扩展策略可按超时扣分。
- `OperationInstructions()` 引导当前步骤，`SkipTask()` 跳过当前步骤，`RestartTask()` 重建本轮运行状态。实体自己的表现状态由对应组件重置。
- `OnStepStarted`、`OnStepCompleted`、`OnTaskCompleted` 提供进度通知。读取 `currentStepIndex`、`totalScore` 或 `GetRuntime(int index)` 查看状态。
- `SaveReplayLog(string fileName = null)` 将动作时间线保存为 JSON，`LoadReplayLog(string path)` 读回。回放查看器用于复盘或导出，场景物体动作重演需另接 [ReplaySystem](../ReplaySystem/README.md)。

[默认评分策略](Core/ScorePolicy.cs) [难度策略](Core/DifficultyScorePolicy.cs) [动作日志](Core/ReplayLog.cs)

## 当前边界

实体通过全局 `TaskDataManager.Instance` 路由，实体注册表也是全局的。两个宿主同时运行时会互相覆盖，销毁其中一个可能清空另一个的实体表。动作日志直接写 `Application.streamingAssetsPath`，Android 包内路径和 WebGL URL 无法按普通文件写入，失败时返回 `null`。跨平台发布要单独核对存储路径。现有 Editor 自检覆盖核心逻辑，历史平台探针未覆盖实际评分场景。 [实体注册表](TaskInteractive/TaskEntityManager.cs) [日志保存实现](Task/TaskDataManager.cs) [运行逻辑自检](../../../Editor/CustomInspector/InteractiveTaskScoringSystem/InteractiveTaskSelfCheck.cs)
