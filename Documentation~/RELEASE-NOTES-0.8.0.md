# UPandaFramework 0.8.0 发布说明

> 发布日期：2026-11-11 · 版本：`0.8.0` · 前一版本：`0.7.1`

## 新增

**新输入系统（Input System）支持**，为后续玩法开发做准备：

- `package.json` 新增依赖 `com.unity.inputsystem@1.11.2`；通过 UPM 安装框架时会自动解析该包，`UPandaGF.Core` 程序集已引用 `Unity.InputSystem`，业务代码可直接 `using UnityEngine.InputSystem`。
- 新增 `InputSystemBackend`：把现有 `IInputBackend` 契约（`InputBinding` 的轴名与 `KeyCode`）映射到 `Keyboard` / `Mouse` / `Gamepad`，因此 `UPGameRootConfig.inputBindings`、`InputService` 的上下文栈与暂停规则**无需改动**。
- 输入后端由 `InputBackendFactory` 按工程的 Active Input Handling 自动选择：
  - 启用新 Input System（New 或 Both）→ `InputSystemBackend`；
  - 仅旧 Input Manager → `UnityLegacyInputBackend`（保持 0.7.x 行为）。
- 新增 EditMode 回归 `InputSystemIntegrationTests`，在两种工程设置下分别断言选中的后端。

## 轴映射

| 轴名 | 新后端来源 |
|---|---|
| `Vertical` | 键盘 W/S + 手柄左摇杆 Y |
| `Horizontal` | 键盘 A/D + 手柄左摇杆 X |
| `Mouse X` / `Mouse Y` | 鼠标 delta × 0.1（与旧版默认灵敏度一致） |
| `Mouse ScrollWheel` | 滚轮 × 0.1/120（与旧版一格观感一致） |

未映射的轴名返回 0 并只提示一次；自定义轴建议直接在业务层使用 `InputAction`。

## 验证

| 项 | 结果 |
|---|---|
| Unity 2021.3 编译（Active Input Handling = Both） | 0 `error CS`，`UPandaGF.Core` 引用 `Unity.InputSystem` |
| EditMode | 54 / 54（新增输入后端用例） |
| PlayMode | 324 / 324 |
| 旧输入路径 | 条件编译回退分支保留，仅旧 Input Manager 的工程仍编译为 `UnityLegacyInputBackend` |

## 升级提示

- 升级后首次打开工程会提示安装 Input System；如项目暂时不想启用新输入，保持 Active Input Handling 为 `Input Manager (Old)` 即可，框架会自动使用旧后端。
- 若项目同时使用 uGUI 的 `StandaloneInputModule`，切换 Input Handling 后需要把 EventSystem 的输入模块换成 `InputSystemUIInputModule`（Unity 官方要求，非本框架强制）。
