# 0.5.0 迁移指南（P1：程序集拆分与命名空间收敛）

0.5.0 把原来的 `UPandaGF.Runtime` 大程序集拆成“基础模块 + 组合根”，并把所有命名空间统一到 `UPandaGF.*`。这是**破坏性变更**：程序集引用与 `using` 需要同步修改；场景/预制体里的脚本引用按 GUID 保存，重命名命名空间本身不会丢引用。

## 1. 程序集

| 0.4.0 | 0.5.0 |
| --- | --- |
| `UPandaGF.Runtime`（全部运行时） | `UPandaGF.Core`、`UPandaGF.Assets`、`UPandaGF.UI`、`UPandaGF.Audio`、`UPandaGF.Save`、`UPandaGF.Diagnostics`、`UPandaGF.Runtime`（组合根） |
| `UPandaGF.Extensions` | `UPandaGF.Gameplay` + `UPandaGF.Gameplay.Tasks` / `.InteractiveScoring` / `.Replay` |

| 程序集 | 内容 | 依赖 |
| --- | --- | --- |
| `UPandaGF.Diagnostics` | PLogger / LogService / LogConfig / uGUI 日志面板 | Contracts |
| `UPandaGF.Core` | 启动生命周期、服务原语、事件、计时器、单例、PublicMono、HTTP | Contracts、Diagnostics |
| `UPandaGF.Assets` | Resources/AB/StreamingAssets、下载、热更、对象池、清单与存档序列化 | Contracts、Core、Diagnostics |
| `UPandaGF.UI` | UIManager、面板基类、UI 辅助 | Contracts、Core、Assets、Diagnostics、TMP |
| `UPandaGF.Audio` | AudioMgr | Contracts、Core、Assets、Diagnostics |
| `UPandaGF.Save` | 槽位存档、PlayerPrefs、Excel 表读取 | Contracts、Core、Assets、Diagnostics |
| `UPandaGF.Runtime` | 组合根（UPGameRoot / GameServices / 日志控制台） | 以上全部 |
| `UPandaGF.Gameplay*` | 可选玩法模块（`autoReferenced = false`） | Contracts、Core、Assets、UI、Save、Diagnostics |
| `UPandaGF.Samples` | 示例（`autoReferenced = false`，不进入 Player） | 全部模块、TMP |

业务如果只需要资源、存档或 UI，可以只引用对应模块 + `UPandaGF.Contracts`，不必再引入整个框架。

## 2. 命名空间

| 0.4.0 | 0.5.0 |
| --- | --- |
| 无命名空间（`PublicMono`、`ResourcesLoader`、`HttpManager`、`PlayerPrefsDataMgr` 等） | `UPandaGF` |
| `UPandaGF.RunTime.TaskSystem` | `UPandaGF.Gameplay.Tasks` |
| `UPandaGF.RunTime.InteractiveTaskScoringSystem` | `UPandaGF.Gameplay.InteractiveScoring` |
| `UPandaGF.RunTime.RobotCCDIK` | `UPandaGF.Gameplay.RobotCCDIK` |
| `UPandaGF.RunTime.ShaderCtr` | `UPandaGF.Gameplay.ShaderControl` |
| `UPandaGF.RunTime` | `UPandaGF.Gameplay` |
| `UPandaGF.StateMechine` | `UPandaGF.Gameplay.StateMachine` |
| `ReplaySystem` | `UPandaGF.Gameplay.Replay` |
| `AssetBundleBrowser` / `.AssetBundleModel` / `.AssetBundleDataSource` | `UPandaGF.AssetBundleBrowser` / `.AssetBundleModel` / `.AssetBundleDataSource` |
| 编辑器工具无命名空间 | `UPandaGF.GFEditor` |
| 示例无命名空间 | `UPandaGF.Samples` |
| 测试无命名空间 | `UPandaGF.Tests`（PlayMode）、`UPandaGF.Tests.EditMode`（不变） |

命名空间改名没有提供 `[Obsolete]` 转发（C# 不支持命名空间别名转发）；请用全局替换处理 `using` 和类型限定名。拼写错误的目录 `BineryMgr` 已重命名为 `BinaryMgr`（类型名不变）。

## 3. 路径

| 0.4.0 | 0.5.0 |
| --- | --- |
| `Runtime/Manager/Singleton`、`PublicMono`、`EventCenterModule`、`Extend`、`HTTPTool` | `Runtime/Core/...` |
| `Runtime/Manager/ObjPool` | `Runtime/Assets/ObjPool` |
| `Runtime/Manager/AssetsMgr` | `Runtime/Assets/AssetsMgr` |
| `Runtime/Manager/DownloadMgr` | `Runtime/Assets/DownloadMgr` |
| `Runtime/Manager/GameRoot/SourcesLoadMgr` | `Runtime/Assets/SourcesLoadMgr` |
| `Runtime/Manager/GameRoot/HotUpdate` | `Runtime/Assets/HotUpdate` |
| `Runtime/Manager/UIMgr` | `Runtime/UI/UIMgr` |
| `Runtime/Manager/AudioMgr` | `Runtime/Audio/AudioMgr` |
| `Runtime/Manager/StorageDataMgr`（含 `BineryMgr`→`BinaryMgr`） | `Runtime/Save/StorageDataMgr` |
| `Runtime/Manager/StorageDataMgr/Serialization` | `Runtime/Assets/Serialization` |
| `Runtime/Manager/Services`（Core 类） | `Runtime/Core/Services` |
| `Runtime/Manager/Services/GameServices.cs` | `Runtime/Composition/GameServices.cs` |
| `Runtime/Manager/Services/AssetService.cs` | `Runtime/Assets/AssetService.cs` |
| `Runtime/Manager/Services/FileSaveService.cs` | `Runtime/Save/FileSaveService.cs` |
| `Runtime/Manager/Services/PoolService.cs` | `Runtime/Assets/PoolService.cs` |
| `Runtime/Manager/GameRoot/UPGameRootConfig.cs` | `Runtime/Assets/UPGameRootConfig.cs` |
| `Runtime/Manager/GameRoot/UPGameRoot.cs` | `Runtime/Composition/GameRoot/UPGameRoot.cs` |
| `Runtime/Manager/GameRoot/StartupLifecycle.cs` | `Runtime/Core/StartupLifecycle.cs` |
| `Runtime/Manager/DebugSystem`（日志原语/日志面板） | `Runtime/Diagnostics` |
| `Runtime/Manager/DebugSystem` 的日志面板、`DebugerInit`、`FrameworkDiagnostics` | `Runtime/Composition/...` |
| `Runtime/UPandaGF.Runtime.asmdef` | `Runtime/Composition/UPandaGF.Runtime.asmdef` |
| `Runtime/Game/UPandaGF.Extensions.asmdef` | `Runtime/Game/UPandaGF.Gameplay.asmdef` + 三个子 asmdef |

## 4. 行为变化

- 旧 AB 接口（`GetAssetBundle` / `LoadResAsync`）的 pin 计入租约：存在 legacy pin 时 `UnLoadAB` 返回 `false`，需要先调用 `ReleaseLegacyBundles()`。
- FTP 密码不再写入 `EditorPrefs`：使用会话内存或环境变量 `UPANDA_FTP_PASSWORD`；旧明文键读取一次后删除。
- ExcelTool 只生成 v2 表；v1 表仍可读取。
- `Samples` 不再自动引用，也不会进入 Player；示例脚本需要显式程序集引用。

## 5. 升级步骤

1. 在程序集定义中把 `UPandaGF.Extensions` 换成 `UPandaGF.Gameplay`（以及需要的 `.Tasks`/`.InteractiveScoring`/`.Replay`），并补上直接使用的模块程序集。
2. 全局替换 `UPandaGF.RunTime`、`UPandaGF.StateMechine`、`ReplaySystem`、`AssetBundleBrowser` 的 `using`/类型限定名。
3. 把业务代码里对框架目录的硬编码路径按第 3 节更新（框架不保证旧路径存在）。
4. 按第 4 节检查 AB 卸载、FTP 上传与 Excel 生成流程。
5. 在隔离宿主重新跑 EditMode/PlayMode，并重新构建 Player（尤其是依赖 Samples 的工程）。
