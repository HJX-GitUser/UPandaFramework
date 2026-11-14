# 输入系统（Input）

> 0.8.2 起，输入系统代码集中在本目录（实现）与 [`Runtime/Contracts/Input`](../../Contracts/Input/README.md)（契约）。
> 本目录是阅读/扩展输入系统的入口；后台映射、上下文、暂停与 UI 联动都在这里说明。

## 1. 定位

输入系统解决两件事：

1. **业务按“动作名”取输入**：业务只调用 `services.Input.GetAxis/GetButton/GetButtonDown/GetButtonUp`，
   不直接接触 `Input` 或 `Keyboard.current`，从而同时适配旧 Input Manager 与新 Input System。
2. **新旧输入后端可切换**：绑定仍是“轴名 + KeyCode”，通过 `InputBackendFactory` 在
   `UnityLegacyInputBackend` 与 `InputSystemBackend` 之间按工程 Active Input Handling 自动选择，
   `UPGameRootConfig.inputBindings` 无需改写。

它不是 InputAction 体系：没有 Action Asset、组合键、Hold/Tap、设备控制方案等能力（见 §9）。

## 2. 分层与目录

```text
业务 / UI / PauseMenuController
        │ IInputService（Contracts/Input）
        ▼
InputService（本目录）—— 绑定表 + 上下文栈 + 暂停屏蔽
        │ IInputBackend
        ▼
InputBackendFactory
   ├─ InputSystemBackend（Keyboard/Mouse/Gamepad 直读）
   └─ UnityLegacyInputBackend（UnityEngine.Input）
        ▲
UPGameRootConfig.inputBindings ← GameRootConfig.json / Inspector
```

| 路径 | 程序集 | 职责 |
| --- | --- | --- |
| [`Runtime/Contracts/Input/IInputService.cs`](../../Contracts/Input/IInputService.cs) | Contracts | 对外契约：查询、上下文、运行时绑定/改键/覆盖层 |
| [`Runtime/Contracts/Input/InputBinding.cs`](../../Contracts/Input/InputBinding.cs) | Contracts | 绑定模型、`Clone()`、`Defaults()` |
| [`Runtime/Contracts/Input/InputContexts.cs`](../../Contracts/Input/InputContexts.cs) | Contracts | `Gameplay/UI/Rebind/Cutscene` 上下文常量 |
| [`Runtime/Contracts/Input/InputRebindRules.cs`](../../Contracts/Input/InputRebindRules.cs) | Contracts | 保留键与同上下文冲突检测规则 |
| [`Runtime/Contracts/Input/InputRebindResult.cs`](../../Contracts/Input/InputRebindResult.cs) | Contracts | 改键结果枚举 |
| [`Runtime/Contracts/Input/InputBindingOverlay.cs`](../../Contracts/Input/InputBindingOverlay.cs) | Contracts | 玩家键位覆盖层 JSON POCO |
| [`InputService.cs`](InputService.cs) | Core | 动作映射、上下文栈、暂停屏蔽、绑定管理、改键/捕获/覆盖层 |
| [`InputBackend.cs`](InputBackend.cs) | Core | `IInputBackend` 抽象 + 旧 Input Manager 实现 + 可捕获键表 |
| [`InputSystemBackend.cs`](InputSystemBackend.cs) | Core | 新 Input System 后端、工厂、`InputKeyMapping` |
| [`InputBindingOverlayStore.cs`](InputBindingOverlayStore.cs) | Core | 覆盖层 JSON 原子写/备份读取（轻量持久化） |
| [`PauseMenuController.cs`](PauseMenuController.cs) | Core | 可复用暂停菜单（暂停凭据 + UI 上下文 + Pause/Cancel） |
| [`Runtime/UI/UIMgr/UIFocusInputBinder.cs`](../../UI/UIMgr/UIFocusInputBinder.cs) | UI | 把 `IInputService` UI 动作接到 `UIFocusNavigator` 的组件 |
| [`Runtime/Core/Services/GameTimeService.cs`](../Services/GameTimeService.cs) | Core | 可嵌套暂停凭据/游戏时钟（被 InputService 读取 IsPaused） |
| [`Runtime/Assets/UPGameRootConfig.cs`](../../Assets/UPGameRootConfig.cs) | Assets | `inputBindings` 配置、JSON 读写与校验 |
| [`Runtime/UI/UIMgr/UIFocusNavigator.cs`](../../UI/UIMgr/UIFocusNavigator.cs) | UI | 焦点导航 API（需宿主把 IInputService 动作接进来） |
| [`Tests/EditMode/Input`](../../../Tests/EditMode/Input) | Tests | 映射/契约/配置校验测试 |

## 3. 快速使用

```csharp
// 查询（context 默认 InputContexts.Gameplay）
float move = services.Input.GetAxis("MoveVertical");
if (services.Input.GetButtonDown("Jump")) Jump();
if (services.Input.GetButtonUp("Jump")) OnJumpReleased();

// 压入 UI 上下文：Gameplay 被阻断，UI 动作仍可读
using (services.Input.PushContext(InputContexts.UI))
{
    if (services.Input.GetButtonDown("Cancel", InputContexts.UI)) Close();
}
```

运行时改键 / 枚举绑定（设置系统用）：

```csharp
services.Input.Bind(new InputBinding
{
    action = "Jump",
    context = InputContexts.Gameplay,
    positive = KeyCode.Space
});

foreach (var binding in services.Input.GetBindings())
{
    // 展示/保存到玩家覆盖层
}

services.Input.Unbind("Jump", InputContexts.Gameplay);
```

注意：

- `Bind` 是拷贝语义；要覆盖同一 `context/action` 再调用一次 `Bind` 即可。
- `Bind/Unbind/PushContext` 在 `IInputService.Dispose` 后抛 `ObjectDisposedException`；
  `Get*` 返回 0/false。
- 轴类动作（`MoveVertical/MoveHorizontal`）的轴名不可运行时改绑（旧 Input Manager 限制）。

### 3.1 运行时改键（设置面板）

```csharp
// 用 TryRebind：自动做保留键与同上下文冲突校验
var result = services.Input.TryRebind(new InputBinding
{
    action = "Jump", context = InputContexts.Gameplay, positive = KeyCode.Q
});
switch (result)
{
    case InputRebindResult.Success: /* 已生效，可落盘 */ break;
    case InputRebindResult.Conflict: /* 提示“该键已被其它动作占用” */ break;
    case InputRebindResult.ReservedKey: /* F1–F12 等保留键 */ break;
    case InputRebindResult.InvalidBinding: /* 配置非法 */ break;
}

// 捕获态：压 Rebind 上下文屏蔽普通操作，然后每帧尝试捕获
using (services.Input.PushContext(InputContexts.Rebind))
{
    while (capturing)
    {
        if (services.Input.TryCaptureKey(out var key)) { Apply(key); break; }
        yield return null;
    }
}

// 恢复默认 / 导出与恢复玩家覆盖层
services.Input.ResetToDefaults();
var overrides = services.Input.ExportOverrides();
string json = InputBindingOverlay.ToJson(new InputBindingOverlay { bindings = overrides });
InputBindingOverlayStore.Save(InputBindingOverlayStore.DefaultPath, new InputBindingOverlay { bindings = overrides }, out var error);
var loaded = InputBindingOverlayStore.Load(InputBindingOverlayStore.DefaultPath, out error);
if (loaded != null) services.Input.ApplyOverrides(loaded.bindings);
```

- `InputRebindRules.DefaultReservedKeys` 默认 F1–F12；`TryRebind(..., allowReservedKey: true)` 可放行。
- `ExportOverrides()` 只导出相对默认表变化的部分，新版本新增默认动作时旧覆盖层不会误覆盖。
- `InputBindingOverlayStore` 是可选轻量持久化（临时文件 + `.bak` 回退）；设置系统也可用自己的存储/云同步。

## 4. 上下文与暂停

- 上下文是字符串栈，栈顶优先；`PushContext(ctx, blockLower: true)` 会阻断更低层上下文，
  直到该凭据 `Dispose()`。
- 暂停（`IPauseService.IsPaused`）只自动屏蔽 `InputContexts.Gameplay`；`UI` 等其它上下文不屏蔽。
  这是“打开菜单后 Gameplay 失效、菜单还能操作”的基础。
- `PauseMenuController` 是标准组合示例：`Pause("Menu")` + `PushContext(InputContexts.UI)` + 显示菜单，
  关闭时逆序释放；`OnDisable` 兜底，避免 `timeScale` 卡 0。
- `UIManager` 的模态面板（`modal=true`）打开时自动压 `UI` 上下文、关闭释放；
  `pauseGame=true` 则另外申请暂停凭据。

完整时序与边界示例见 [`Runtime/Core/Services/README.md`](../Services/README.md)。

## 5. 后端与能力矩阵

后端由编译宏自动选择（`InputBackendFactory.Create()`）：

| Active Input Handling | 宏 | 后端 |
| --- | --- | --- |
| Input Manager (Old) | `ENABLE_LEGACY_INPUT_MANAGER` | `UnityLegacyInputBackend` |
| Input System (New) | `ENABLE_INPUT_SYSTEM` | `InputSystemBackend` |
| Both | 两者都有 | `InputSystemBackend`（优先新系统） |

`InputBackendFactory.UsesInputSystemBackend/SupportsAxis/SupportsKey` 与 `InputKeyMapping`
（`MapToKeyValue/IsSupported`）可用于设置 UI 展示和配置校验。

新后端 0.8.2 支持面：

- **轴**：`Vertical`（W/S + 方向键 + 左摇杆）、`Horizontal`（A/D + 方向键 + 左摇杆）、
  `Mouse X`、`Mouse Y`、`Mouse ScrollWheel`。其它轴名返回 0 并只警告一次。
- **键盘**：A–Z、Alpha0–9、F1–F12、小键盘、方向键、标点、修饰键、
  `Insert/CapsLock/NumLock/ScrollLock/Print/Pause/Menu` 等。
- **鼠标**：Mouse0/1/2 + Mouse3（后退）/Mouse4（前进）；Mouse5/6 不支持。
- **手柄**：`KeyCode.JoystickButton0–15` → A/B/X/Y、LB/RB、Back/Start、L3/R3、Dpad、LT/RT
  （框架约定顺序，非平台原生顺序）；其他玩家槽位 `JoystickButtonN` 暂不支持。
- 未映射的键会打印一次告警；轴名同样只警告一次。完整边界见
  [`RELEASE-NOTES-0.8.2.md`](../../../Documentation~/RELEASE-NOTES-0.8.2.md)。

## 6. 默认绑定

`InputBinding.Defaults()`（`Contracts/Input`）提供 14 条默认动作；实际值可被
`UPGameRootConfig.inputBindings` / `GameRootConfig.json` 覆盖：

| 动作 | 上下文 | 默认键位 |
| --- | --- | --- |
| MoveVertical / MoveHorizontal | Gameplay | 轴 Vertical / Horizontal |
| Handbrake | Gameplay | Space |
| CameraCycle | Gameplay | C |
| Camera1/2/3 | Gameplay | Alpha1/2/3 |
| Pause | Gameplay | Escape |
| Interact | Gameplay | E |
| Jump | Gameplay | Space |
| Confirm / Cancel | UI | Return / Escape |
| NavigateVertical / NavigateHorizontal | UI | 轴 Vertical / Horizontal（UIFocusInputBinder 使用） |

## 7. 配置与校验

配置对象是 `UPGameRootConfig.inputBindings`（Player 从 `StreamingAssets/Data/GameRootConfig.json` 读，
Editor 以面板为准）。`UPGameRootConfigFile.Validate` 会对输入做快速失败校验：

- `action/context` 非空、同 `context/action` 不重复；
- `KeyCode` 合法、动作未绑定检查；
- `axis` 与 `positive/negative` 二选一；
- 输入数组不能为空；
- 新输入后端下：轴名/按键必须在支持列表内（`InputBackendFactory.SupportsAxis/SupportsKey`）。

校验失败会阻止 `SnapshotAndValidate` 返回，避免“配置合法但运行期静默无输入”。

## 8. 测试

| 范围 | 位置 |
| --- | --- |
| 映射回归（含 Alpha0–9 P0 回归、方向键、小键盘、标点、鼠标/手柄支持面） | [`Tests/EditMode/Input/InputMappingTests.cs`](../../../Tests/EditMode/Input/InputMappingTests.cs) |
| 契约/上下文/暂停/Dispose/配置校验 | [`Tests/EditMode/Input/InputServiceContractTests.cs`](../../../Tests/EditMode/Input/InputServiceContractTests.cs) |
| 改键/冲突/保留键/捕获/重置/覆盖层持久化 | [`Tests/EditMode/Input/InputRebindTests.cs`](../../../Tests/EditMode/Input/InputRebindTests.cs) |
| 后端工厂选型 | [`Tests/EditMode/Input/InputSystemIntegrationTests.cs`](../../../Tests/EditMode/Input/InputSystemIntegrationTests.cs) |
| UI 焦点输入接线 | [`Tests/PlayMode/UIFocusInputBinderTests.cs`](../../../Tests/PlayMode/UIFocusInputBinderTests.cs) |
| PlayMode 中的暂停/上下文/模态集成 | `Tests/PlayMode/P1RegressionTests.cs`、`UIPhase2LayerStackTests.cs` |

0.8.2 已在 Old（Input Manager）与 Both 工程回归通过；同时新增 `_verify/P1VerifyNew`（`activeInputHandler=1`）
验证 New-only 分支：EditMode 163/163、PlayMode 384/384（证据见工作区 `优化记录`，工程位置 `_verify/P1VerifyNew`）。

## 9. 已知限制与后续方向

- 手柄按钮映射是框架约定顺序；`Joystick1..8ButtonN`、多手柄/玩家槽位不支持。
- 无 InputAction 资产/组合键/Hold/Tap/触屏/虚拟摇杆/设备热插拔通知；
  这些需要后续 InputAction 双轨方案。
- 手柄摇杆死区使用 Input System `axisDeadzone` 默认值（0.125/0.925），
  与旧 InputManager `dead=0.19` 不完全一致，尚未暴露配置。
- 改键在输入层已具备（冲突/保留键/捕获/重置/覆盖层），但**没有内置设置面板 UI**；
  设置模块负责界面、持久化时机与“交换/覆盖”交互。
- `UIFocusInputBinder` 是可选组件，不自动挂载；UI 导航动作仅在新工程/更新配置后存在
  （老 `GameRootConfig.json` 需要补 `NavigateVertical/NavigateHorizontal` 两条绑定）。
- 多面板同时启用 binder 会争抢焦点，应只让当前活动面板启用。

## 10. 扩展指南

| 需求 | 改动位置 |
| --- | --- |
| 新增一个默认动作/键位 | `Contracts/Input/InputBinding.Defaults()` + 业务查询；必要时更新 `UPGameRootConfig` 默认 JSON |
| 新增上下文常量 | `Contracts/Input/InputContexts.cs`（暂停屏蔽仍只识别 `Gameplay`） |
| 新增后端可读键/轴 | `InputSystemBackend.ToKey/SupportsKey/Axis`（补测试） |
| 增加新后端（如 InputAction） | 新增 `IInputBackend` 实现 + `InputBackendFactory` 分支；保留 `InputService` 不变 |
| 设置系统改键 | `IInputService.Bind/Unbind/GetBindings`；玩家覆盖层持久化与冲突检测建议放在 Settings 模块 |
| UI 焦点/返回键 | 宿主组件监听 `IInputService` → `UIFocusNavigator.Navigate/Submit/Cancel` |

## 11. 相关文档

- `UPandaFramework-输入系统分析.md`（工作区根目录，0.8.1 基线 + 优化跟踪）
- `优化记录/OptimizeLog/InputSystem-2026-11-13/README.md`（工作区根目录，改动与验证证据）
- [`RELEASE-NOTES-0.8.2.md`](../../../Documentation~/RELEASE-NOTES-0.8.2.md)
- [`Runtime/Core/Services/README.md`](../Services/README.md)（暂停/时钟/计时）
- [`Runtime/Contracts/Input/README.md`](../../Contracts/Input/README.md)（契约入口）
