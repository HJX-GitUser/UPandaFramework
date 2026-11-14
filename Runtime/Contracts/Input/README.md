# 输入契约（Contracts/Input）

本目录是输入系统的**契约层**（`UPandaGF.Contracts` 程序集），实现位于
[`Runtime/Core/Input`](../../Core/Input/README.md)。

| 文件 | 内容 |
| --- | --- |
| `IInputService.cs` | 对外输入服务：查询、`Bind/Unbind/GetBindings`、`PushContext`、`TryRebind/TryCaptureKey/ResetToDefaults/ExportOverrides/ApplyOverrides`、`BindingsChanged` |
| `InputBinding.cs` | 绑定模型（action/context/axis/positive/negative）、`Clone()`、`Defaults()` |
| `InputContexts.cs` | 上下文常量 `Gameplay/UI/Rebind/Cutscene` |
| `InputRebindRules.cs` | 保留键（默认 F1–F12）与同上下文冲突检测 |
| `InputRebindResult.cs` | `TryRebind` 结果（Success/InvalidBinding/Conflict/ReservedKey） |
| `InputBindingOverlay.cs` | 玩家键位覆盖层 POCO（JSON 持久化、只保存相对默认表的变化） |

约定：

- 业务只依赖 `IInputService`，不要 `as InputService` 强转；
- `IGameServices.Input` 暴露本契约；
- `Bind` 为拷贝语义，`GetBindings` 返回快照；
- 详细架构、后端能力矩阵与扩展方法见 [输入系统 README](../../Core/Input/README.md)。
