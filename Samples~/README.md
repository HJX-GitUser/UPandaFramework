# 示例（可选导入）

这些教学示例放在 `Samples~` 目录，**不会随包自动导入**。在 Unity 的 `Package Manager → UPandaFramework → Samples` 中按需点击 **Import**，示例会复制到 `Assets/Samples/UPandaFramework/<版本>/`。

| 示例 | 内容 | 依赖模块 |
|---|---|---|
| `Startup` | `GameLaunchExample`：UPGameRoot 启动、`OnLoadComplete`、首场景跳转 | Runtime / UI |
| `Pooling` | `TestPoolMgr`：对象池取用/回收，附带一键生成示例场景的编辑器工具 | Assets |
| `Coroutines` | `HowUseMonoMgr`：PublicMono 协程与线程调度 | Core |
| `StateMachine` | Idle/Move/Walk/Run/Attack 状态机示例 | Gameplay |
| `Tasks` | 交互任务评分演示场景生成器 + `TaskTriggerExample` | Gameplay.Tasks / InteractiveScoring |

每个示例目录自带 asmdef（`UPandaGF.Samples.*`），导入多个示例不会互相覆盖。示例编辑器工具（`Pooling/Editor`、`Tasks/Editor`）生成的内容写到工程内 `Assets/UPandaFrameworkDemos/`，不会写进只读的包目录。

## 模块内演示脚本

`Runtime/Game/TaskSystem/Demo`、`Runtime/Game/InteractiveTaskScoringSystem/Example`、`Runtime/Game/Study`、`Runtime/Save/StorageDataMgr/*/Test` 这些随模块分发的演示脚本已改为独立程序集（`UPandaGF.Samples.TaskSystemDemo` 等，`autoReferenced = false`）：它们随包编译，但不自动进入 Player，也不依赖可选示例。

迁移说明见 [0.7 迁移指南](../Documentation~/RELEASE-NOTES-0.7.0.md)。
