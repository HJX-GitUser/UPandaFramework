# P1 程序集拆分与命名空间收敛（0.5.0，2026-11-08）

对应 `OPTIMIZATION_PLAN.md` P1-1、P1-2。目标：把单体 `UPandaGF.Runtime` 拆成可独立引用的模块程序集，并把全部命名空间收敛到 `UPandaGF.*`。

## 程序集结构

```
Contracts → Diagnostics → Core → Assets → UI / Audio / Save → Runtime（组合根）
                                                   ↘ Gameplay / .Tasks / .InteractiveScoring / .Replay
Editor → 全部运行时模块 + Samples
Tests  → 全部运行时模块 + Editor
```

| 程序集 | 内容 | 直接依赖 |
| --- | --- | --- |
| `UPandaGF.Contracts` | 接口、枚举（含 `AssetLoaddingMethod`）、租约 | — |
| `UPandaGF.Diagnostics` | PLogger / LogService / LogConfig / uGUI 日志面板 | Contracts |
| `UPandaGF.Core` | StartupLifecycle、时间/输入/计时器/配置/流程/场景/作用域、事件、单例、PublicMono、HTTP、诊断数据 | Contracts、Diagnostics |
| `UPandaGF.Assets` | Resources/AB/StreamingAssets、下载、热更、对象池、清单与版本化序列化、AES | Contracts、Core、Diagnostics |
| `UPandaGF.UI` | UIManager、面板、UI 辅助 | Contracts、Core、Assets、Diagnostics、TMP |
| `UPandaGF.Audio` | AudioMgr | Contracts、Core、Assets、Diagnostics |
| `UPandaGF.Save` | 槽位存档、PlayerPrefs、Excel 表读取 | Contracts、Core、Assets、Diagnostics |
| `UPandaGF.Runtime` | 组合根：UPGameRoot / GameServices / 日志控制台 / FrameworkDiagnostics | 以上全部 |
| `UPandaGF.Gameplay` | 状态机、描边、CCDIK、载具、相机、暗角（可选） | Contracts、Core、Assets、UI、Save、Diagnostics |
| `UPandaGF.Gameplay.Tasks` / `.InteractiveScoring` / `.Replay` | 任务 / 交互评分 / 回放（可选） | 同上 |
| `UPandaGF.Samples` | 示例（`autoReferenced = false`） | 全部模块 |
| `UPandaGF.Editor` | Editor 工具 | 全部 + Samples + Excel DLL |

设计说明：`UPGameRoot` 与 `GameServices` 是组合根，必须 `new` 出各模块的具体服务，因此留在 `UPandaGF.Runtime`；`UPandaGF.Core` 只放不依赖具体模块的启动生命周期与服务原语。这样“任一模块可单独引用”成立，同时避免 Core↔模块循环依赖。

## 命名空间

| 旧 | 新 |
| --- | --- |
| 无命名空间（运行时 31 个文件） | `UPandaGF` |
| `UPandaGF.RunTime.TaskSystem` | `UPandaGF.Gameplay.Tasks` |
| `UPandaGF.RunTime.InteractiveTaskScoringSystem` | `UPandaGF.Gameplay.InteractiveScoring` |
| `UPandaGF.RunTime.RobotCCDIK` | `UPandaGF.Gameplay.RobotCCDIK` |
| `UPandaGF.RunTime.ShaderCtr` | `UPandaGF.Gameplay.ShaderControl` |
| `UPandaGF.StateMechine` | `UPandaGF.Gameplay.StateMachine` |
| `ReplaySystem` | `UPandaGF.Gameplay.Replay` |
| `AssetBundleBrowser`（19 个文件） | `UPandaGF.AssetBundleBrowser` |
| 无命名空间（Editor） | `UPandaGF.GFEditor` |
| 无命名空间（Samples） | `UPandaGF.Samples` |
| 无命名空间（PlayMode 测试） | `UPandaGF.Tests` |

目录修正：`Runtime/Save/StorageDataMgr/BineryMgr` → `BinaryMgr`；`Runtime/Manager/*` 拆到 `Runtime/Core|Assets|UI|Audio|Save|Diagnostics|Composition`。

## 跨程序集可见性调整

拆分后以下成员从 `internal` 改为 `public`（仅用于组合根/诊断，属于最小暴露）：

- `SceneService.DiagnosticScopeCount` / `DiagnosticPendingRequestCount`
- `UIManager.DiagnosticOpenPanelCount` / `DiagnosticPendingPanelCount`
- `AssetsLoader.BundleLoader`
- `FileUpdateTransaction`、`HotUpdateFileGate`
- `ResourcesLoader.InvokeCallbacks`
- `AssetLoaddingMethod` 移入 Contracts；`FrameworkModuleState` / `FrameworkModuleDiagnostic` / `ResourceLoaderDiagnostics` / `AssetBundleDiagnostics` 移入 Core

## 验证

环境：Unity `2021.3.45f2c1`、`2022.3.53f1c1`；宿主 `_verify/P1Verify`、`_verify/P1Verify2022`。

| 项 | 结果 |
| --- | --- |
| 2021.3 编译 | 0 个 `error CS`；17 个 `UPandaGF*.dll` |
| 2022.3 编译 | 0 个 `error CS`；17 个 `UPandaGF*.dll` |
| EditMode 2021.3 | **47 / 47** |
| PlayMode 2021.3 | **302 / 302** |
| 命名空间审计 | 全包 `.cs` 无命名空间文件 **0** |
| 文档链接 | 移动后自动修复 91 条相对链接；最终全文链接检查 **0 条断链** |

原始文件：`editmode-2021.xml`、`playmode-2021.xml`、`compile-2021-summary.txt`、`compile-2022-summary.txt`、`assemblies-2021.txt`、`namespace-audit.txt`。

## Framework Validation 同步验证

`Framework Validation` 工程原先停留在 0.3.0。本轮用 `Tools/ci/sync-package.ps1` 同步到 0.5.0（工程路径含空格，顺带修复了 Unity 参数引用），随后在该工程内完成：

| 项 | 结果 |
| --- | --- |
| 同步后版本 | `0.5.0`，`Runtime/Manager` 已被 `Core/Assets/.../Composition` 取代 |
| 编译 | 0 个 `error CS`，生成 16 个模块/测试程序集 |
| EditMode | **47 / 47** |
| PlayMode | **302 / 302** |

原始文件：`framework-validation-editmode.xml`、`framework-validation-playmode.xml`、`framework-validation-compile-summary.txt`；工程内同时保留 `Logs/p1-structure-*.log` 与更新后的 `VALIDATION.md`。

## 已知边界

- **破坏性变更**：命名空间没有 `[Obsolete]` 转发（C# 不支持命名空间别名转发），升级必须全局替换 `using` 与程序集引用；迁移表见 `Documentation~/MIGRATION-0.5.md`。
- `Samples` 改为 `autoReferenced=false`：依赖示例脚本的业务工程需要显式引用 `UPandaGF.Samples`；Player 构建不再自动包含示例。
- `UPandaGF.Runtime` 仍是“全家桶”组合根，单个模块的消费者应直接引用 `UPandaGF.Core/Assets/UI/...`，不要再引用 Runtime 以免把组合根一起带入。
- 2022.3 本轮只做了编译验证；2022.3 的 EditMode/PlayMode 全量仍由 CI 矩阵覆盖（本机未执行 Actions）。
- 链接检查覆盖 `OptimzeLog` 之外的文档；历史 `OptimzeLog` 记录保留当时的路径，不回写。
- 运行时行为变化（legacy AB pin、FTP 密码、Excel v2）见 `CHANGELOG.md` 0.4.0/0.5.0。
