# Changelog

所有 Notable 变更记录在此文件。版本号遵循 [SemVer](https://semver.org/lang/zh-CN/)；UI 框架的分阶段验证证据保存在仓库外的 `优化记录/`，不随 UPM 包发布。

## [Unreleased]

### Planned

- 设置面板纵切（依赖 `ISettingsService`）、1.0 前 API 冻结评审、旧 `panelDic` 收口。

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
