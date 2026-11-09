# 分层状态机

`StateMachineManager` 管理一棵状态树并通过 Unity 的 Update、FixedUpdate 驱动活动分支。`BaseState` 提供子状态、默认子状态与递归生命周期，`IState` 定义可实现的最小契约。组件位于 `UPandaGF.Gameplay`，沿用源码命名空间 `UPandaGF.Gameplay.StateMachine`。此模块适合角色等局部行为，应用级流程另见 [GameFlowService](../../Core/Services/README.md)。 [管理器](StateMachineManager.cs) [基础状态](BaseState.cs)

## 注册与切换

先注册唯一根，再按父 ID 注册子状态。状态 ID 在整棵树中唯一，根只能有一个，需要挂子状态的父节点须继承 `BaseState`。下面的入口可在业务组件的 Start 中使用。

```csharp
using UPandaGF.Gameplay.StateMachine;
using UnityEngine;

public class CharacterStates : MonoBehaviour
{
    private sealed class NamedState : BaseState
    {
        public NamedState(string id) { StateID = id; }
        public override string StateID { get; }
    }

    private void Start()
    {
        var machine = gameObject.AddComponent<StateMachineManager>();
        machine.RegisterState(new NamedState("Player"));
        machine.RegisterState(new NamedState("Idle"), "Player");
        var move = new NamedState("Move");
        machine.RegisterState(move, "Player");
        machine.RegisterState(new NamedState("Walk"), "Move");
        machine.RegisterState(new NamedState("Run"), "Move");
        move.SetDefaultChild("Walk");
        machine.SwitchState("Idle");
        machine.SwitchState("Move/Run");
        machine.GoBack();
    }
}
```

## 调用约定

| 入口 | 行为 |
| --- | --- |
| `RegisterState(IState state, string parentStateID = "")` | 注册根或子状态。重复 ID 会跳过 |
| `bool SwitchState(string statePath)` | 接受单个全局 ID 或 `Move/Run` 这样的层级路径，活动叶和祖先均可用 `CanTransitionTo(string stateID)` 阻止切换 |
| `bool GoBack()` | 回到上一条成功转换前的状态。转换被拒绝时保留历史项 |
| `ActiveStatePath`、`CurrentState`、`HistoryCount` | 查看活动路径、活动叶与历史长度 |
| `OnStateChanged`、`GetStateInfo()` | 订阅新旧路径，或打印状态树 |

重写 `OnEnter`、`OnExit`、`OnUpdate`、`OnFixedUpdate` 时，调用对应的基类方法以保留子状态递归行为。非法路径与守卫拒绝不会执行 Enter/Exit。业务生命周期抛异常时管理器尝试恢复活动结构，已执行的业务副作用由业务自行处理。`OnStateChanged` 通知期间的重入切换会被拒绝。

在场景挂 [StateMachineExample](../../../Samples~/StateMachine/StateMachineExample.cs)，用数字键演示状态切换，B 查看回退。现有 PlayMode 用例覆盖路径与守卫、默认子状态、返回、重入及销毁。 [P0 回归](../../../Tests/PlayMode/P0RegressionTests.cs) [历史验证](../../../OptimzeLog/P0Startup/VALIDATION.md)
