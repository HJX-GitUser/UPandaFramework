# 分层状态机（StateMachine）

一个基于 Unity 的 **分层状态机（Hierarchical State Machine，HSM）** 实现，支持状态嵌套、路径切换、状态返回、事件通知与调试打印。

## 目录结构

```
StateMachine/
├── BaseState.cs            # IState 接口 + BaseState 抽象基类
├── StateMachineManager.cs  # 分层状态管理器（驱动层）
└── StateMachineExample.cs  # 使用案例
```

## 核心概念

| 类型 | 职责 |
|------|------|
| `IState`（接口） | 状态契约：`StateID`、`Parent`、`OnEnter/OnExit/OnUpdate/OnFixedUpdate`、`CanTransitionTo` |
| `BaseState`（抽象基类） | 实现子状态树：`children` 字典、`activeChild`、`defaultChild` |
| `StateMachineManager` | 驱动层：管理根状态、注册表、状态栈、事件、路径 |

状态可以**嵌套**形成一棵树（父状态包含子状态），当前活跃路径形如 `"Player/Move/Run"`。

## 快速上手

1. **定义状态**：继承 `BaseState`，实现 `StateID`（唯一标识）及需要的生命周期方法：

```csharp
public class IdleState : BaseState
{
    public override string StateID => "Idle";
    public override void OnEnter() { /* 进入逻辑 */ }
    public override void OnUpdate(float deltaTime) { /* 每帧逻辑 */ }
    public override void OnExit() { /* 退出逻辑 */ }
}
```

2. **创建管理器并注册状态**：

```csharp
var manager = gameObject.AddComponent<StateMachineManager>();

manager.RegisterState(new PlayerState());                // 根状态
manager.RegisterState(new IdleState(), "Player");        // Player 的子状态
manager.RegisterState(new MoveState(), "Player");
manager.RegisterState(new RunState(), "Move");           // Move 的子状态
```

3. **切换状态**（支持单级与多级路径）：

```csharp
manager.SwitchState("Idle");      // 单级切换
manager.SwitchState("Move/Run");  // 多级路径切换
manager.GoBack();                 // 返回上一级
```

4. **监听状态变化**：

```csharp
manager.OnStateChanged += (oldPath, newPath) =>
    Debug.Log($"状态切换: {oldPath} -> {newPath}");
```

5. **调试**：

```csharp
Debug.Log(manager.GetStateInfo()); // 打印状态树与当前状态
```

## API 参考

### StateMachineManager

| 方法 / 成员 | 说明 |
|-------------|------|
| `RegisterState(IState state, string parentStateID = "")` | 注册状态；无父 ID 时为根状态 |
| `SwitchState(string statePath)` | 按路径切换状态（支持 `"A/B/C"`），失败自动回滚 |
| `GoBack()` | 返回上一级状态 |
| `ActiveStatePath` | 当前活跃状态完整路径（如 `"Player/Move/Run"`） |
| `OnStateChanged` | 状态改变事件（`Action<string, string>`，旧路径 → 新路径） |
| `GetStateInfo()` | 返回状态树调试信息 |
| `Update()` / `FixedUpdate()` | 自动驱动根状态（内部递归到活跃子状态） |

### BaseState

| 方法 / 成员 | 说明 |
|-------------|------|
| `StateID`（抽象） | 状态唯一标识 |
| `Parent` | 父状态 |
| `children` | 子状态字典 |
| `ActiveChild` | 当前活跃子状态（只读） |
| `OnEnter / OnExit / OnUpdate / OnFixedUpdate` | 生命周期，可重写 |
| `CanTransitionTo(string stateID)` | 是否允许切换到指定状态（默认返回 true） |
| `RegisterChild(IState childState)` | 注册子状态 |
| `SwitchToChild(string childID)` | 切换到指定子状态 |
| `SetDefaultChild(string childID)` | 设置默认子状态（下次 `OnEnter` 时自动激活） |
| `GetActivePath()` | 获取从本状态起的活跃路径 |

## 使用案例说明

`StateMachineExample.cs` 演示了完整的玩家状态机（`Player → Idle / Move / Attack`，`Move → Walk / Run`）。挂到空物体上运行后：

| 按键 | 功能 |
|------|------|
| 数字键 1 | 待机（单级切换） |
| 数字键 2 | 移动（单级切换，进入默认子状态 Walk） |
| 数字键 3 | 跑步（多级路径切换 `Move/Run`） |
| 数字键 4 | 攻击（单级切换） |
| B | 返回上一级（GoBack） |
| T | 打印状态树 |

## 设计要点

- **分层嵌套**：状态可包含子状态，用 `/` 分隔的路径寻址；
- **注册表查找**：`Dictionary<string, IState>` 实现 O(1) 状态查找；
- **状态栈**：记录切换历史，支持 `GoBack` 返回；
- **事件通知**：`OnStateChanged` 解耦 UI 与游戏逻辑；
- **失败回滚**：多级切换中途失败会逐级恢复原状态；
- **按需更新路径**：`ActiveStatePath` 仅在状态变化时重算，避免每帧字符串拼接。

## 注意事项

- 状态 `StateID` 必须**全局唯一**，且等于其在子状态字典中的键；
- `SetDefaultChild` 只在**下一次 `OnEnter`** 时生效，注册后需手动激活一次（见示例第 5 步）；
- 当前为单层状态机，暂不支持并行状态（同时激活多个子状态）。
