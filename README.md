# UPandaFramework

Unity 客户端基础框架。`UPGameRoot` 负责启动和服务装配，运行时提供场景、资源、热更、UI、音频、存档、输入、HTTP、对象池、日志和帧调度，`Runtime/Game` 提供可选玩法组件，`Editor` 提供打包与内容工具。框架作为源码目录放在宿主项目的 `Assets/upanda-framework`，导入时应一并保留 `.meta`，以便 Unity 恢复脚本、场景、预制体和材质引用。

安装方式二选一：

- **UPM（推荐）**：Package Manager → Add package from git URL，填 `https://gitee.com/he-jinxian/upanda-framework.git#master`。需要宿主已安装 Git，并确认 TextMeshPro 依赖可解析。
- **源码拷贝**：把整个 `upanda-framework` 目录（含 `.meta`）复制到 `Assets/upanda-framework`。

要求 **Unity 2021.3 LTS 或更高**；2021.3.45f2c1 与 2022.3.53f1c1 均已编译并跑通 EditMode/PlayMode 回归。

[Gitee](https://gitee.com/he-jinxian/upanda-framework.git) · [GitHub](https://github.com/UserHandJ/UPandaFramework.git) · [变更记录](CHANGELOG.md) · [优化计划](OPTIMIZATION_PLAN.md)

## 集成到 Unity 项目
1. 导入框架目录及 `.meta`，在 Package Manager 确认 uGUI 和 TextMeshPro 可用。运行时输入的默认后端使用旧 Input Manager。使用 HybridCLR 程序集加载、URP 效果或运行测试时，再核对对应宿主依赖。
2. 在启动场景通过 `UPandaGF → 创建UPGameRoot` 创建根节点。菜单还会创建可选的 `GameLaunchExample`。每个应用只保留一条启动流程，将启动场景加入 Build Settings。
3. 在 Inspector 选择资源模式。编辑器直读使用 AssetDatabase，业务场景需要能被 Unity 直接加载。Player 使用 AssetBundle，先按目标平台打包并生成 `assetData.assetref`。保存场景会把根配置导出到 `Assets/StreamingAssets/Data/GameRootConfig.json`。
4. 在示例上设置 `firstScene`，或在自己的入口等待根就绪，再调用 `root.Services.Scenes.LoadAsync(...)`。AB 场景使用清单登记的完整路径。接入热更前，先验证随包资源能启动。

[启动与配置](Runtime/Composition/GameRoot/README.md) · [启动示例](Samples/Startup/GameLaunchExample.cs) · [AssetBundle 打包](Editor/AssetBundleTools/README.md)

```csharp
using System.Threading;
using System.Threading.Tasks;
using UPandaGF;

public static class AppEntry
{
    public static async Task OpenAsync(UPGameRoot root, string scenePath, CancellationToken token)
    {
        await root.WaitUntilReadyAsync(token);
        await root.Services.Scenes.LoadAsync(scenePath, token: token);
    }
}
```

`WaitUntilReadyAsync` 可等待已有的共享启动结果。调用者负责处理异常和自己的加载界面。接入了 `GameLaunchExample` 的场景由该示例负责首场景跳转。运行时实例化的业务对象用 `services.Inject(gameObject)` 补注入；已有场景消费者实现 `IGameServicesConsumer` / `IUIServiceConsumer`。 [服务装配](Runtime/Core/Services/README.md)

## 程序集

| 程序集 | 内容 |
| --- | --- |
| `UPandaGF.Contracts` | 服务接口、公共枚举、资源租约。 |
| `UPandaGF.Diagnostics` | 日志原语（PLogger / LogService / LogConfig）与 uGUI 日志面板。 |
| `UPandaGF.Core` | 启动生命周期、时间/输入/计时器/配置/流程/场景/作用域、事件、单例、PublicMono、HTTP。 |
| `UPandaGF.Assets` | Resources / AssetBundle / StreamingAssets、下载、热更、对象池、清单序列化。 |
| `UPandaGF.UI` | UIManager、面板基类、UI 辅助组件。 |
| `UPandaGF.Audio` | AudioMgr。 |
| `UPandaGF.Save` | 槽位存档、PlayerPrefs、Excel 表读取。 |
| `UPandaGF.Gameplay` / `.Tasks` / `.InteractiveScoring` / `.Replay` | 可选玩法模块；`autoReferenced = false`。 |
| `UPandaGF.Runtime` | 组合根（UPGameRoot / GameServices / Reporter），引用全部运行时模块。 |
| `UPandaGF.Visuals` | Shader 目录边界，具体效果仍受渲染管线和材质配置约束。 |
| `UPandaGF.Samples` | 启动、协程、池、状态机和任务示例；`autoReferenced = false`，不会进入 Player。 |
| `UPandaGF.Editor` | Editor 工具，引用运行时、玩法、示例，以及 Excel 和 SharpZipLib DLL。 |
| `UPandaGF.Tests.EditMode` / `UPandaGF.Tests` | EditMode 单元测试与 PlayMode 集成回归。 |

0.4.0 → 0.5.0 的程序集、命名空间与目录迁移见 [迁移指南](Documentation~/MIGRATION-0.5.md)；业务代码需要把 `using UPandaGF.RunTime.*` / `ReplaySystem` / `AssetBundleBrowser` 替换为新命名空间，并补充直接引用的模块程序集。

## 核心模块

| 模块 | 文档 |
| --- | --- |
| 启动和应用服务 | [GameRoot](Runtime/Composition/GameRoot/README.md) · [Contracts](Runtime/Contracts/README.md) · [服务与场景作用域](Runtime/Core/Services/README.md) |
| 资源与内容更新 | [资源与平台存储](Runtime/Assets/AssetsMgr/README.md) · [AB 加载](Runtime/Assets/AssetsMgr/AssetBundleMgr/README.md) · [资源入口与场景](Runtime/Assets/SourcesLoadMgr/README.md) · [启动热更](Runtime/Assets/HotUpdate/README.md) |
| HTTP 与文件下载 | [HTTPTool](Runtime/Core/HTTPTool/README.md) · [Downloader](Runtime/Assets/DownloadMgr/README.md) |
| UI、音频与复用 | [uGUI](Runtime/UI/UIMgr/README.md) · [音频](Runtime/Audio/AudioMgr/README.md) · [对象池](Runtime/Assets/ObjPool/README.md) |
| 运行基础 | [单例](Runtime/Core/Singleton/README.md) · [事件](Runtime/Core/EventCenterModule/README.md) · [PublicMono](Runtime/Core/PublicMono/README.md) · [扩展方法](Runtime/Core/Extend/README.md) |
| 存档与诊断 | [存档、Excel 与 PlayerPrefs](Runtime/Save/StorageDataMgr/README.md) · [序列化](Runtime/Assets/Serialization/README.md) · [日志](Runtime/Diagnostics/README.md) · [Reporter](Runtime/Composition/Reporter/README.md) |

## 玩法与制作工具

| 模块 | 文档 |
| --- | --- |
| 目标与流程 | [任务](Runtime/Game/TaskSystem/README.md) · [交互评分](Runtime/Game/InteractiveTaskScoringSystem/ReadMe.md) · [分层状态机](Runtime/Game/StateMachine/README.md) |
| 模拟与回放 | [场景回放](Runtime/Game/ReplaySystem/README.md) · [机械臂 CCD](Runtime/Game/RobotCCDIK/README.md) · [车辆与相机](Runtime/Game/VehicleSystem/README.md) |
| 视觉与示例 | [自由相机](Runtime/Game/SimpleCameraControl/README.md) · [描边](Runtime/Game/QuickOutline/README.md) · [暗角](Runtime/Game/Vignette/README.md) · [Shader 与案例](Runtime/Shader/README.md) · [排序示例](Runtime/Game/Study/README.md) |
| 工具链 | [Editor 工具索引](Editor/README.md) · [AssetBundle 打包与发布](Editor/AssetBundleTools/README.md) · [示例入口](Samples/README.md) |

## 构建与发布

框架的 Player 构建入口会对齐启动配置并执行 Unity 构建。发布前需要单独生成目标平台的 AB、主包和资源清单，使它们与 `bundleTarget`、`LoadAssetPath` 和首场景配置一致。编辑器直读和 Player 的资源来源不同，应分别验证首场景加载。CI 用 `FrameworkBuildPipeline.BuildFromCommandLine` 或 `Tools/ci/build-player.sh` 构建目标平台；Android 要求工程路径只含 ASCII 字符（Android Tools 限制）。 [构建入口](Editor/README.md) · [打包与发布](Editor/AssetBundleTools/README.md) · [CI 脚本](Tools/ci/README.md)

资源内容更新由 `enableAssetUpdate` 控制，`GameLaunchExample` 的程序集加载由 `loadHotUpdateScripts` 独立控制。按发布需要启用并分别验收。导入随附示例时，按模块文档检查序列化引用和渲染管线。 [内容更新](Runtime/Assets/HotUpdate/README.md) · [示例索引](Samples/README.md)

## 验证与源码维护

- 在 Unity Test Runner 中运行 EditMode 与 PlayMode。部分 fixture 会临时创建资源、AB 或修改构建场景，完整测试适合在隔离宿主运行。 [测试入口](Tests/README.md)
- CI 脚本在 `Tools/ci/`：EditMode/PlayMode、结果 XML 校验（0 用例或 `failed > 0` 返回非 0）与三平台构建；GitHub Actions 工作流见 `.github/workflows/ci.yml`。 [CI 说明](Tools/ci/README.md)
- `OptimzeLog` 保存阶段性验证结果。Windows IL2CPP 与 WebGL 的历史探针来自隔离工程，Android 当轮完成 APK 构建。集成后在目标项目与设备上单独验收。 [历史索引](OptimzeLog/OPTIMIZATION.md) · [跨平台记录](OptimzeLog/CrossPlatform/README.md)
- C# 与 Markdown 文件遵循仓库的编码和行尾配置。新增 Unity 资源或移动资源时保留正确的 `.meta`。运行时调用 UnityEditor API 要处于编辑器编译条件内。 [源码格式](.editorconfig)

框架原创部分采用 MIT 许可。随仓库提供的第三方代码、库和资产遵循各自的许可。 [LICENSE](LICENSE) · [第三方声明](THIRD_PARTY_NOTICES.md)
