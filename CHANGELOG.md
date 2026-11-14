# Changelog

所有 Notable 变更记录在此文件。版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)；UI 框架的分阶段验证证据保存在仓库外的 `优化记录/`，不随 UPM 包发布。

## [Unreleased]

### Fixed

- **DebugerInit FPS 在暂停时不断变大**：原实现用 `Time.deltaTime` 做 EMA，设置面板 `pauseGame` 把
  `Time.timeScale` 设为 0 后 `deltaTime` 衰减到 0、FPS 被算成无穷大。改为 `FrameRateCounter` +
  `Time.unscaledDeltaTime` 的固定窗口统计，暂停时仍显示真实帧率。
- **出包后帧率上限无效**：`Application.targetFrameRate` 在 `QualitySettings.vSyncCount > 0` 时被平台忽略，
  而默认 `display.vsync = true`。新增 `FrameRateSettingsApplier`（Order 90，最后应用）；修改帧率时自动关闭
  VSync，冲突时记录警告；`Unlimited` 仍是平台默认值 `-1`，不保证真正无上限。

### Added

- **设置消费与合规补强**：
  - 新增 `graphics.fieldOfView`，新增 `SettingsCameraBinder`（相机 FOV）与 `SettingsInputProfile`
    （鼠标/手柄灵敏度、死区、反转、震动、瞄准方式），供控制器/相机读取。
  - `GraphicsSettingsApplier` 消费 `graphics.renderScale`（`ScalableBufferManager`）。
  - 新增 `SettingsAudioMixerBinder`，
    支持 AudioMixer 五通道、全部静音和失焦静音；新增设置面板“试听音效”（运行时生成短音）。
  - 新增 `display.monitor` 与显示器下拉；`DisplaySettingsApplier` 通过 `Screen.GetDisplayLayout` /
    `Screen.MoveMainWindowTo` 切换显示器。
  - 新增 `SettingsVolumeBrightnessBinder`：反射写 URP/HDRP `ColorAdjustments.postExposure`，
    不硬依赖渲染管线程序集。
  - 新增 `privacy.termsVersion` / `privacy.consentUtc` 与 `SettingsConsent` 扩展
    （`NeedsConsent` / `Accept` / `Decline`），首启隐私流程可直接复用。
  - 分辨率行升级为 uGUI 下拉（支持“自动”与平台分辨率列表）；所有多选项设置（枚举、语言、显示器、分辨率）统一使用下拉框。
  - 修复设置面板内下拉列表被 `ScrollRect` 的 `RectMask2D` 裁剪、以及 Game 视图不显示的问题：模板/列表生成到独立的 `DropdownOverlay`，`SettingsDropdownCanvasFixer` 对齐列表 Canvas 的相机/平面距离；不再关闭 viewport 遮罩。
  - 修复下拉列表项 RectTransform 高度为负导致列表只显示一条细线的问题；模板/Item 改为显式 `anchoredPosition + sizeDelta`。
  - `SettingsJsonMerge` 支持对象数组按 `key`/`id`/`name` 合并，保留元素未知字段。
- **显示健壮性 / 安全模式 / 迁移**：
  - `DisplaySettingsApplier` 通过 `Screen.resolutions` 选择精确或最近分辨率，不支持时回退并写回实际值；
    应用异常自动回退窗口化 1280×720；Player 下两帧后校验实际分辨率。
  - 设置面板把 `display.width/height/refreshRate` 合并成一行“分辨率”，通过下拉选择平台支持的模式。
  - 新增 `SettingsSafeMode`：连续启动失败 2 次后强制窗口化、1280×720、低画质、UI 缩放 1；
    完整启动成功后清除；设置面板显示安全模式提示。
  - 新增 `ISettingsMigration` / `SettingsMigrationChain`：旧文件缺少 `schemaVersion` 按 0 迁移，
    未来版本/缺步骤迁移失败时回退默认值且不覆盖原文件；`SettingsStore` 用探针读取原始版本。
  - 新增 `SettingsMigrationRegistry`，项目可在启动前注册自定义迁移；新增 `SettingsJsonMerge`，
    保存时保留未知字段（对象递归合并、对象数组按 key 合并、无 key 数组整体替换）。
  - 新增迁移、安全模式、未知字段保留、分辨率选择测试。
- **设置体验补强**：
  - `graphics.preset` 现在会展开到 AA/阴影/纹理/渲染缩放子项，修复“预设 Low 但面板子项仍显示 High、
    切 Custom 后效果跳变”的问题；加载旧数据时也会按非 Custom 预设归一化。
  - `accessibility.uiScale` 由 `UIManager` 直接消费：启动和设置变化时自动 `SetUiScale`，不再需要业务手动接。
  - 设置面板接入 `UIFocusNavigator` + `UIFocusInputBinder`（手柄/键盘焦点、Cancel 关闭），移动端自动挂
    `UISafeArea`；新增“恢复本页默认”和预览剩余秒数。
- **本地多语言模块（P0）**：新增 `UPandaGF.Localization` 程序集与 `ILocalizationService`
  （内置 `zh-Hans`/`en`、catalog 合并、项目覆盖表、系统语言回退、缺失 key 策略、`Get/Format`）。
  `GameServices` 暴露 `Localization`；`UPGameRoot` 在 `Settings` 与 `UI` 之间新增 `Localization` 启动步骤。
- 设置面板接入本地化：标题/分类/设置项/选项/改键提示全部走 key，语言行可切换“跟随系统 + catalog 语言”；
  `SettingsText` 仅保留为无本地化服务时的中文兜底。新增 `LocalizedText`、`LocalizedTMPText`、`LocalizationBinder`。
- 新增编辑器工具 `UPandaGF/本地化/导入 CSV...`：CSV（key + 语言列）→ 项目 catalog + JSON 表，校验重复 key、
  语言码、占位符一致性与缺失翻译；输出到 `Assets/UPandaFrameworkLocalization/Resources/LocalizationProject/`。
- 新增配置 `UPGameRootConfig.localization`（内置/项目 catalog 路径、加载方式、预加载、缺失策略），
  并加入配置校验。
- 新增测试：本地化服务（加载/回退/覆盖/设置联动/损坏表）、CSV 解析校验、PlayMode 真实 Resources + `LocalizedText` 刷新。
- **系统设置面板（P0）**：`SettingsPanel` + `SettingsPresenter`，由描述表自动生成分区/设置行，支持
  即时生效、预览确认条、恢复默认（二次确认）、键位捕获改绑与冲突提示；面板布局运行时构建，
  预制体只需根节点（`Runtime/Resources/UI/SettingsPanel.prefab`），新增设置项不改面板。
  生成器菜单：`UPandaGF/UI/生成设置面板预制体`。
- 新增 Presenter/预制体 EditMode 测试：行生成、双向绑定、分区过滤、移动端隐藏“显示”、键位列表、预制体存在性。
- **系统设置服务（P0 核心）**：`ISettingsService` + `SettingsData` + `SettingsRegistry` 描述表 +
  `SettingsStore`（JSON 原子写/`.bak` 回退）+ Applier 管线（音频/画质/显示/输入）+ 预览提交/回滚/
  恢复默认/切后台与退出保存。设置与存档分离，默认路径 `persistentDataPath/Settings/settings.json`。
- 契约新增 `ISettingsService Settings`（`IGameServices`）；`UPGameRoot` 在 `Services` 与 `UI` 之间新增
  `Settings` 启动步骤，保证语言/UI 缩放/画质/分辨率在首个面板与首场景之前生效。
- 新增 EditMode 测试 `SettingsServiceTests`：描述表自检、字段↔描述一致性、默认值、归一化、
  持久化、预览提交/超时回滚、损坏备份恢复、平台可用性、键位覆盖、生命周期保存。
- 新增 [`Runtime/Core/Settings/README.md`](Runtime/Core/Settings/README.md) 作为设置系统入口与扩展说明。
- 新增可选示例 `Samples~/Settings`：`SettingsDemoController` 演示打开设置面板、消费 UI 缩放/灯光亮度/鼠标灵敏度、
  推荐配置/提交预览/恢复默认；`Settings/Editor/SettingsDemoBuilder` 可一键生成示例场景
  （`Assets/UPandaFrameworkDemos/Settings/SettingsDemo.unity`）。已加入 `package.json` 的 Samples 列表。

### Changed

- `IGameServices` 新增 `ISettingsService Settings`：自定义实现与测试替身需补该成员（未使用时可返回 `null`），
  属于契约破坏性变更；`GameServices` 已装配，`UPGameRoot` 已在 `Services` 与 `UI` 之间插入 `Settings` 启动步骤。

### Planned

- 基于 `ISettingsService` 的设置面板纵切（UI 框架已具备 `UISettingList`/`UISettingRow` 与 Observable）、
  1.0 前 API 冻结评审、旧 `panelDic` 收口。

## [0.8.2] - 2026-11-13

### Fixed

- 修复新输入后端数字键映射：`KeyCode.Alpha1–Alpha9` 不再错误映射到
  `LeftShift/RightShift/LeftAlt/…/ContextMenu`，默认 `Camera1/2/3` 恢复可用。
- `InputSystemBackend.Vertical/Horizontal` 补齐方向键；未映射按键改为只警告一次，不再静默。
- `UPGameRootConfigFile.Validate` 增加输入映射快速失败：axis 与 positive/negative 互斥、
  空数组、新后端不支持的轴名/按键。
- `InputService.Dispose` 后的行为与注释统一（Get* 返回 0/false，Bind/Unbind/PushContext 抛异常）。

### Added

- 输入后端映射补齐：小键盘、标点、`Insert/CapsLock/NumLock/ScrollLock/Print/Pause/Menu`、修饰键、
  鼠标侧键 `Mouse3/4`、手柄按钮 `KeyCode.JoystickButton0–15`。
- 契约扩展：`IInputService.GetButtonUp/Bind/Unbind/GetBindings`；`InputBinding` 下沉
  `UPandaGF.Contracts` 并新增 `Clone()`；新增 `InputContexts` 常量。
- **运行时改键基础设施**：`TryRebind`（保留键 + 同上下文冲突校验）、`TryCaptureKey`（捕获下一键）、
  `ResetToDefaults`、`GetDefaultBindings`、`ExportOverrides/ApplyOverrides`、`BindingsChanged` 事件；
  `InputBindingOverlay` + `InputBindingOverlayStore`（JSON 原子写 + `.bak` 回退）。
- **UI 焦点输入接线**：新增可选组件 `UIFocusInputBinder`（方向重复导航/Confirm/Cancel），
  默认绑定新增 `NavigateVertical`/`NavigateHorizontal`（UI 上下文）。
- `InputBackendFactory.UsesInputSystemBackend/SupportsAxis/SupportsKey` 与 `InputKeyMapping` 查询接口。
- 新增 EditMode 测试：映射回归（含 P0 数字键）、契约/上下文/暂停/Dispose、配置校验。

### Changed

- `InputService` 绑定表改为“上下文 → 动作”两级字典并维护有序快照；每帧查询不再拼接
  `"context/action"` 字符串，消除固定 GC 分配。
- Runtime/示例 7 个文件、34 行 `UnityEngine.Input` 直读迁移到 `InputBackendFactory.Create()` 后端；
  `VehicleInput`/`DemoPlayerController` fallback 不再硬编码旧后端；`LogListenerManager` 在 New-only 下
  禁用旧快捷键并一次性告警。
- `PauseMenuController`/`UIManager` 改用 `InputContexts` 常量；输入相关文档与 FAQ 更新。
- **输入系统代码整理**：实现集中到 `Runtime/Core/Input/`（`InputService`/`InputBackend`/`InputSystemBackend`/`PauseMenuController`），
  契约集中到 `Runtime/Contracts/Input/`（`IInputService`/`InputBinding`/`InputContexts`）；
  `InputAndTime.cs` 拆分为 `InputService.cs`、`InputBackend.cs` 与 `Runtime/Core/Services/GameTimeService.cs`；
  新增 [`Runtime/Core/Input/README.md`](Runtime/Core/Input/README.md) 作为输入系统说明入口。

## [0.8.1] - 2026-11-12

### Added

- UI 面板对象池：`[UILoadInfo(..., pool = true)]`、`UIManager.MaxPooledPerPath`、池命中/创建/回收诊断。
- 五层 UI 栈（`Background/Main/Popup/Guide/Toast`）与 `UIPanelHandle`：`OpenAsync`/`OpenInScopeAsync`/`CloseAsync`、`CloseTop`/`CloseAll`、`HandleBack`、模态暂停/输入上下文、Toast 队列。
- 热更新友好入口：非泛型 `OpenAsync(Type)`/`ClosePanel(Type)`/`GetPanel(Type)`/`ShowToastAsync(Type)`、`RegisterPanel`/`RegisterPanels`、`ResetForHotUpdate`。
- `UIEventScope`：`BasePanel.Events`/`OpenEvents` 自动解绑 UnityEvent、EventBus、EventCenter 与任意 `IDisposable`。
- 数据绑定：`Observable<T>`、`BoolObservable`/`FloatObservable`/`IntObservable`/`StringObservable`/`ColorObservable`、`CompositeDisposable`、`UIBinder`、`TMPUIBinder`、`UISafeArea`。
- 动态列表与设置行：`UIItemPool`、`UISettingItem`、`UISettingRow`（Toggle/Slider/Dropdown/Button/ReadOnly）、`UISettingList`（描述表驱动 + 行复用）。
- 焦点与缩放：`UIFocusNavigator`（默认焦点/方向导航/Submit/Cancel/Remember/Restore）、`UICanvasScale`、`UIManager.SetUiScale`/`IUIService.UiScale`。
- UI 相机渲染管线适配：`UICameraPipeline` 自动识别 Built-in/URP/HDRP；URP 默认方案 B（UI 相机 Overlay + 主相机 Camera Stack，无主相机时回退 Base），HDRP 追加 `HDAdditionalCameraData`（clearDepth），运行时切换管线会重新适配且不硬依赖 SRP 程序集。
- 工具链：`UIPanelValidator`（菜单 `UPandaGF/UI/校验面板配置`，CI 入口 `ValidateForCI`）、重写的 `UIAutoGenerator`（partial BasePanel + UILoadInfo + 事件桩，TMP 类型，无 `EditorApplication.update` 残留）、`Tools/ci/validate-ui-panels.*`。
- 文档：`Documentation~/MIGRATION-UI.md`。

### Changed

- `UIManager` 不再依赖静态单例；通过 `IUIService` 注入；面板实例化后注入 `IGameServices`/`IUIService` 到整棵子树。
- 重复打开已存在面板默认置顶并调用 `OnRefresh`，不再重复执行 `OnOpen`。
- 加载中关闭会取消本地 CTS；迟到资源只归还，不再注册/打开。
- `E_UI_Layer` 新增语义名（与旧名同值）与 `Toast` 层；场景层节点兼容新旧命名。
- `BasePanel` 控件缓存改为一次遍历，支持 TMP_InputField/TMP_Dropdown/Scrollbar/ToggleGroup，并跳过嵌套面板子树。
- `UICanvasRegister`/`UICameraRegister` 不再覆盖 Inspector 赋值；`ResetCanvas` 拒绝在有面板/加载中时切换。

### Fixed

- 预配置为 Overlay 的 URP UI 相机被 `UICameraPipeline` 强制改回 Base；现在保留 Overlay 与 Camera Stack。
- `UIAutoGenerator` 生成的脚本缺少 `OnOpen`/`OnClose`、包含不存在的类型名、`EditorApplication.update` 退订无效。
- 自定义 Canvas 缺层、EventSystem 输入模块不匹配、默认隐藏预制体未先激活导致的打开失败。
- 面板被外部销毁后记录残留；`OnOpen` 内自关闭的返回值语义。

## [0.8.0] - 2026-11-11

### Added

- 新 Input System 支持：`com.unity.inputsystem@1.11.2` 依赖、`InputSystemBackend`、按 Active Input Handling 自动选择后端。
- `InputSystemIntegrationTests`。

### Changed

- 输入轴映射：`Vertical`/`Horizontal` 支持键盘与手柄左摇杆；`Mouse X/Y`、`Mouse ScrollWheel` 保持旧观感。

## [0.7.1] - 2026-11-10

### Fixed

- URP 下描边不可见：改为双单 Pass 代理 Renderer（模板 + 描边），按渲染队列顺序绘制。
- Built-in 保持双 Pass 方案；序列化字段与组件 API 不变。

## [0.7.0] - 2026-11-09

### Added

- MIT 许可的 ExcelDataReader、三平台构建校验、UPM 包校验、中国版 CI 镜像说明。

### Changed

- `Samples~` 按示例拆分程序集；移除 Sword.rar。
