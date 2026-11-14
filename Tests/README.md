# 框架测试与验证

测试分为 `UPandaGF.Tests.EditMode` 与 `UPandaGF.Tests`。使用 Unity Test Framework，在 `Window → General → Test Runner` 分别选择 EditMode 和 PlayMode。需要聚焦单一模块时，可以按类名或程序集筛选。 [EditMode 程序集](EditMode/UPandaGF.Tests.EditMode.asmdef) · [PlayMode 程序集](PlayMode/UPandaGF.Tests.asmdef)

## 用例索引

| 范围 | 入口 |
| --- | --- |
| 计时、配置、流程、启动指标和场景归属 | [FoundationServiceTests](EditMode/FoundationServiceTests.cs) · [场景分类](EditMode/ScenePathClassificationTests.cs) · [场景提供者](EditMode/SceneServiceAssetProviderTests.cs) |
| 数据管线与编辑器护栏 | [Excel 二进制 v2](EditMode/ExcelBinaryV2Tests.cs) · [Excel 生成端链路](EditMode/ExcelGenerationPipelineTests.cs) · [编辑器护栏/AB 构建校验](EditMode/EditorGuardTests.cs) · [PlayerPrefs](EditMode/ExcelAndPlayerPrefsTests.cs) |
| 启动、回滚、状态机、槽位存档恢复和根装配 | [P0RegressionTests](PlayMode/P0RegressionTests.cs) · [P1RegressionTests](PlayMode/P1RegressionTests.cs) |
| 输入映射/契约/配置校验 | [InputMappingTests](EditMode/Input/InputMappingTests.cs) · [InputServiceContractTests](EditMode/Input/InputServiceContractTests.cs) · [InputRebindTests](EditMode/Input/InputRebindTests.cs) · [InputSystemIntegrationTests](EditMode/Input/InputSystemIntegrationTests.cs) |
| 输入改键与 UI 焦点接线 | [InputRebindTests](EditMode/Input/InputRebindTests.cs) · [UIFocusInputBinderTests](PlayMode/UIFocusInputBinderTests.cs) |
| 资源、请求协调、事件与销毁 | [FrameworkRegressionTests](PlayMode/FrameworkRegressionTests.cs) · [LifecycleRegressionTests](PlayMode/LifecycleRegressionTests.cs) · [CoordinationRegressionTests](PlayMode/CoordinationRegressionTests.cs) |
| UI 面板生命周期与打开竞态 | [UIPhase1RegressionTests](PlayMode/UIPhase1RegressionTests.cs) |
| UI 五层栈、模态、返回与 Toast | [UIPhase2LayerStackTests](PlayMode/UIPhase2LayerStackTests.cs) |
| UI 事件绑定/解绑与控件缓存 | [UIEventScopeTests](PlayMode/UIEventScopeTests.cs) |
| UI 面板池与诊断快照 | [UIPhase2LayerStackTests](PlayMode/UIPhase2LayerStackTests.cs)（`PooledPanelReusesInstanceWithoutReload`） |
| UI Observable 与数据绑定/安全区 | [UIBindingTests](PlayMode/UIBindingTests.cs) |
| UI 行池/设置描述表/焦点导航/UI 缩放 | [UIListAndFocusTests](PlayMode/UIListAndFocusTests.cs) |
| UI 面板校验器/脚本生成器 | [UIPanelToolingTests](EditMode/UIPanelToolingTests.cs) |
| UI 相机渲染管线适配 | [UICameraPipelineTests](PlayMode/UICameraPipelineTests.cs) |
| 系统设置服务/面板/迁移/安全模式 | [SettingsServiceTests](EditMode/Settings/SettingsServiceTests.cs) · [SettingsPresenterTests](EditMode/Settings/SettingsPresenterTests.cs) · [SettingsMigrationAndSafeModeTests](EditMode/Settings/SettingsMigrationAndSafeModeTests.cs) · [SettingsPanelSmokeTests](PlayMode/SettingsPanelSmokeTests.cs) |
| 本地化服务/CSV/场景刷新 | [LocalizationServiceTests](EditMode/Localization/LocalizationServiceTests.cs) · [LocalizationCsvTests](EditMode/Localization/LocalizationCsvTests.cs) · [LocalizationPlayModeTests](PlayMode/LocalizationPlayModeTests.cs) |
| 帧率统计 | [FrameRateCounterTests](EditMode/FrameRateCounterTests.cs) |
| 描边代理渲染、网格共享与层级接管 | [OutlinePlayModeTests](PlayMode/OutlinePlayModeTests.cs) |
| 平台、热更、序列化、回放与日志 | [PlatformCompatibilityTests](PlayMode/PlatformCompatibilityTests.cs) · [HotUpdateRegressionTests](PlayMode/HotUpdateRegressionTests.cs) · [SerializationMigrationTests](PlayMode/SerializationMigrationTests.cs) · [ReplayPerformanceTests](PlayMode/ReplayPerformanceTests.cs) · [LogConsoleRegressionTests](PlayMode/LogConsoleRegressionTests.cs) · [日志图标绑定](EditMode/LogConsoleIconTests.cs) |

部分 PlayMode fixture 会生成临时资产、构建 AssetBundle 或暂时修改场景列表。整套测试适合在隔离宿主中运行，并核对 fixture 清理结果。Editor 菜单中的日志、事件和交互评分自检是额外验证入口。 [生命周期 fixture](PlayMode/LifecycleFixtures.cs) · [协调 fixture](PlayMode/CoordinationFixtures.cs) · [回放 fixture](PlayMode/ReplayPerformanceFixtures.cs)

CI 用 `Tools/ci/run-tests.*` 跑 EditMode/PlayMode，并由 `check-test-results.*` 解析 NUnit XML：**0 个用例或 `failed > 0` 都返回非 0**（不只看 Unity 退出码）。同步包后脚本会先做一次 `-quit` 预热编译，避免 Unity 增量导入时序导致首轮漏文件。 [CI 说明](../Tools/ci/README.md)

## 历史与发布验证

优化计划与历史回归 XML、探针和平台结果归档在框架外（维护者本地 `优化记录`，不随包发布）。跨平台记录在隔离工程中验证了 Windows IL2CPP 和 WebGL Edge 基础路径，Android 当轮完成 APK 构建，设备运行需独立验收。历史覆盖和本地当前版本的运行结果应分别记录。实际发布还需要测试宿主首场景、目标平台主包与清单、可选热更、存储恢复和所用玩法。
