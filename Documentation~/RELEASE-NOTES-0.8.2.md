# UPandaFramework 0.8.2 发布说明

> 发布日期：2026-11-13 · 版本：`0.8.2` · 前一版本：`0.8.1`

本版本是**输入系统优化**版本：修复 0.8.1 新输入后端的默认路径错误，补齐键盘/鼠标/手柄映射，
扩展输入契约以支撑后续设置系统的改键需求，并治理 New-only 工程中的 `UnityEngine.Input` 直读。

## 修复

- **数字键映射错误（P0）**：`InputSystemBackend` 原先把 `KeyCode.Alpha1–Alpha9` 映射到
  `LeftShift/RightShift/LeftAlt/…/ContextMenu`，导致默认 `Camera1/2/3`（Alpha1/2/3）失效、
  按 Shift/Alt/Ctrl/Meta 反而切视角。现在显式映射到 `Key.Digit0/Digit1…Digit9`。
- 新后端 `Vertical/Horizontal` 补齐方向键（↑↓←→），与旧 InputManager 默认轴一致。
- 未映射按键不再静默：每个不支持的 `KeyCode` 只警告一次（轴名原先已有一次性警告）。
- `InputService.Dispose` 后的行为与注释统一：`Get*` 返回 0/false、`GetBindings` 返回空列表，
  `Bind/Unbind/PushContext` 抛 `ObjectDisposedException`。

## 新增

- **键盘映射补齐**：小键盘（0–9/运算符）、标点符号、`Insert/CapsLock/NumLock/ScrollLock/Print/Pause/Menu`、
  左右修饰键/Windows/Command 键。
- **鼠标侧键**：`Mouse3`（后退）与 `Mouse4`（前进）可读取；`Mouse5/6` 明确不支持并告警。
- **手柄按钮**：`KeyCode.JoystickButton0–15` 映射到标准 Gamepad 按钮（A/B/X/Y、LB/RB、Back/Start、
  L3/R3、Dpad、LT/RT，顺序见 `InputSystemBackend` 注释）；`JoystickButton16–19` 与其他玩家槽位
  `Joystick1Button0…` 暂不支持（一次性告警）。
- **契约扩展**：
  - `IInputService` 新增 `GetButtonUp`、`Bind`、`Unbind`、`GetBindings`；
  - `InputBinding` 下沉到 `UPandaGF.Contracts` 并新增 `Clone()`；
  - 新增 `InputContexts` 常量（`Gameplay`/`UI`/`Rebind`/`Cutscene`）；
  - `InputBackendFactory` 新增 `UsesInputSystemBackend`、`SupportsAxis`、`SupportsKey`；
  - 新增 `InputKeyMapping`（测试/配置校验用的 `KeyCode→Key` 查询）。
- **配置快速失败**：`UPGameRootConfigFile.Validate` 现在校验
  “axis 与 positive/negative 二选一”、“输入映射为空数组”，以及新输入后端下不支持的轴名/按键。
- **测试**：新增 `InputMappingTests`（覆盖 70+ 映射与 P0 回归）、`InputServiceContractTests`
  （契约/上下文/暂停/Dispose）与 `InputConfigValidationTests`（配置校验）。

## 变更

- 绑定表改为“上下文 → 动作”两级字典并维护有序快照：每帧查询不再拼接 `"context/action"` 字符串，
  消除固定 GC 分配；`GetBindings()` 返回按注册顺序的拷贝。
- Runtime/示例中的 7 个文件、34 行 `UnityEngine.Input` 直读迁移到
  `InputBackendFactory.Create()` 的后端（Local/诊断/任务演示/相机/日志控制台），New-only 工程不再逐帧抛异常；
  `LogListenerManager`（Diagnostics 程序集不能引用 Core）在 New-only 下禁用快捷键并只告警一次。
- `VehicleInput`/`DemoPlayerController` 的本地 fallback 改用 `InputBackendFactory.Create()`，
  不再硬编码旧后端。
- `UIManager` 模态输入上下文与 `PauseMenuController` 改用 `InputContexts` 常量；
  `axis` 类动作的鼠标/手柄读取语义保持旧版观感，但相机等示例的轴读取从 `Input.GetAxis`（平滑）
  改为 `IInputBackend.Axis`（旧后端即 `GetAxisRaw`），手感可能有细微差异，宿主可自行加平滑。

## 运行时改键与 UI 焦点接线

- `IInputService` 新增：`TryRebind`（保留键 F1–F12 + 同上下文冲突校验）、`TryCaptureKey`（捕获下一键）、
  `ResetToDefaults`、`GetDefaultBindings`、`ExportOverrides/ApplyOverrides`、`BindingsChanged` 事件。
- 新增 `InputBindingOverlay`（只保存相对默认表变化）与 `InputBindingOverlayStore`（JSON 原子写 + `.bak` 回退）。
- 新增可选组件 `UIFocusInputBinder`：把 `NavigateVertical/NavigateHorizontal/Confirm/Cancel` 接到
  `UIFocusNavigator`，支持按住方向键重复导航；默认绑定表新增两条 UI 导航轴绑定（总 14 条）。
- 新测试：`InputRebindTests`（冲突/保留键/捕获/重置/覆盖层往返/备份回退）、`UIFocusInputBinderTests`。

## 代码组织

输入系统代码已集中与拆分，便于查找和扩展：

- 实现：`Runtime/Core/Input/`（`InputService`、`InputBackend`、`InputSystemBackend`、`PauseMenuController`）；
  新增 [`README.md`](../Runtime/Core/Input/README.md) 作为输入系统入口文档。
- 契约：`Runtime/Contracts/Input/`（`IInputService`、`InputBinding`、`InputContexts`）。
- 原 `Runtime/Core/Services/InputAndTime.cs` 拆分：输入部分迁到 `Runtime/Core/Input/`，
  暂停/时钟 `GameTimeService` 保留在 `Runtime/Core/Services/GameTimeService.cs`。
- 输入相关 EditMode 测试集中到 `Tests/EditMode/Input/`。
- 命名空间与公开 API 不变，仅文件位置调整；程序集边界不变（Contracts/Core）。

## 已知限制

- 手柄按钮映射是**框架约定顺序**，不等同于各平台原生 `JoystickButtonN` 顺序；多手柄/玩家槽位、
  InputAction 资产、触屏/虚拟摇杆、组合键、Hold/Tap、运行时重绑 UI 仍需后续 InputAction 方案。
- 新后端仍只支持 5 个旧轴名（Vertical/Horizontal/Mouse X/Mouse Y/Mouse ScrollWheel），
  自定义轴直接返回 0 并警告一次。
- 手柄摇杆死区仍使用 Input System 默认 `axisDeadzone`（0.125/0.925），
  与旧 InputManager 的 `dead=0.19` 不完全一致（待后续设置系统统一暴露死区配置）。
- New-only 下 `LogListenerManager` 的旧快捷键（默认 F12）禁用；`UIFocusNavigator` 仍需要宿主自行把
  `IInputService` 动作接到导航 API，框架不内置输入接线。

## 验证

> 本版本在本机 `Framework Validation`（Both）与 `_verify/P1Verify`（Old）工程同步后验证；
> 详细结果见仓库外 `优化记录/OptimizeLog/InputSystem-2026-11-13/` 与 `Framework Validation/VALIDATION.md`。

| 项 | 结果 |
| --- | --- |
| Both（New+Old）编译 | **0 error CS**（`_verify/InputVerify`，Unity 2021.3.45f2c1，Input System 1.11.2） |
| Old（仅 Input Manager）编译 | **0 error CS**（`_verify/P1Verify`） |
| Both · EditMode | 输入相关用例 **96/96**（映射 70 + 契约 10 + 改键 10 + 配置 5 + 集成 1）；全量 163 中 162 通过，唯一失败为 FV 宿主脚本生成的 `TestUIWindow` 预制体缺失（与本轮无关） |
| Both · PlayMode | **384 / 384** |
| Old · EditMode | **91 / 91**（含新增 13 个 Old 下可运行输入契约/改键用例） |
| Old · PlayMode | **384 / 384** |
| New-only 工程 | **EditMode 163/163、PlayMode 384/384**（`_verify/P1VerifyNew`，`activeInputHandler=1`，Input System 1.11.2） |
