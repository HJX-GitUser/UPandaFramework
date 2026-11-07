# 通用任务系统

`TaskSystem` 用于主线、支线和可重复任务。任务定义、玩家进度、奖励接收和界面分别维护。代码位于 `UPandaGF.Extensions`，命名空间为 `UPandaGF.RunTime.TaskSystem`。任务日志和 HUD 使用 UGUI，任务推进本身可脱离 UI 使用。 [任务定义](Core/QuestDefinition.cs) [任务管理器](Manager/QuestManager.cs)

## 在场景里接入

1. 从 Assets 的创建菜单选 `UPandaGF → 任务系统 → 任务定义 QuestDefinition`，将定义放到任意 `Resources/Quests` 目录。设置唯一且稳定的 `questId`，填目标类型、目标 ID、数量和奖励。前置任务填其他任务的 ID。
2. 把 `QuestManager` 组件放到场景里（一键搭建会自动放）。它不再自动创建，也不再有静态 `Instance`；根节点会在场景加载后通过 `IGameServicesConsumer` 注入日志与存档服务。`autoLoadSaveOnAwake` 在注入完成后读取默认槽位。
3. 在击杀、收集、触发区或对话结束的位置上报事件。确保事件在任务被接受后发出，目标 ID 和任务配置一致。没有开启自动接受时，主动调用 `AcceptQuest`。
4. 目标满足后任务变为 `Completed`。调用 `TurnInQuest` 发奖并解锁后继任务。可重复任务提交后立即回到可接受状态。演示的重复规则没有按日期刷新。

```csharp
using UPandaGF.RunTime.TaskSystem;

// 场景里的 QuestManager 通过 Inspector 引用传给业务脚本；不再使用静态单例。
public QuestManager quests;

if (quests != null && quests.AcceptQuest("Quest_KillWolves"))
{
    // 在击杀实际发生时调用。第二个参数可以大于 1。
    GameplayEventBus.RaiseEnemyKilled("wolf", 1);
}
```

其余事件入口为 `RaiseItemCollected(string itemId, int amount = 1)`、`RaiseLocationReached(string locationId)` 和 `RaiseNpcTalked(string npcId)`。管理器启用时订阅，禁用时解除。已有自己的总线时也可以直接调用 `Report(ObjectiveType type, string targetId, int amount = 1)`。 [事件入口](Events/GameplayEventBus.cs) [目标与状态类型](Core/QuestTypes.cs)

## UI、奖励和存档

| 场景 | 入口 |
| --- | --- |
| 查询与追踪 | `GetQuest`、`GetQuestsByStatus`、`GetTrackedQuests`、`TrackQuest` |
| UI 与通知 | `QuestLogPanel` 默认用 J 开关，`QuestHudTracker` 默认显示追踪中的任务。两者需要 Inspector 指向同一个 `QuestManager`（一键搭建会自动绑定） |
| 发奖 | 必须设置 `QuestManager.RewardReceiver` 为自己的 `IRewardReceiver`；不再回退到演示的 `PlayerRewardService`。未设置时只记录警告、不发放 |
| 业务存档 | `SaveToJson(bool pretty = true)`、`LoadFromJson(string json)`，可把 JSON 交给业务存储 |
| 槽位存档 | `SaveToSlot(string slot = null)`、`LoadFromSlot(...)`、`HasSave(...)`、`DeleteSave(...)`，走注入的 `ISaveService`，默认槽位 `quests` |

存档按任务 ID 恢复现有定义的进度，加载时先重置当前进度。应为需要跨版本保留的任务和目标维护稳定的 ID。槽位存档使用框架的版本化存档服务（`.save` 文件），旧的 `quests.json` 不会自动迁移。发奖先于提交状态变更，自定义奖励接收器在部分发放后抛错可能造成重试重复发放。运行中调用 `Initialize(forceReload: true)` 或替换已存在的定义会重建对应实例。 [奖励接口](Rewards/IRewardReceiver.cs) [任务日志](UI/QuestLogPanel.cs) [任务 HUD](UI/QuestHudTracker.cs) [存档模型](Core/QuestSaveData.cs)

## 演示与核对

随附四份模板的脚本 GUID 与当前 QuestDefinition 的 meta 不一致。使用业务任务时新建定义。试用演示时，先保留要继续使用的任务配置，再用 `UPandaGF → Runtime → 任务系统 → 一键搭建（任务资产 + UI + 演示场景）` 重建固定路径下的任务资产，并在当前场景搭好演示 UI 和物体。先完成一项任务并提交，再观察前置解锁、可重复任务及存档恢复。框架历史跨平台探针未覆盖整套任务流程。 [演示安装器](../../../Editor/CustomInspector/TaskSystem/QuestDemoInstaller.cs) [随附任务定义](Resources/Quests/) [平台验证边界](../../../OptimzeLog/CrossPlatform/README.md)
