# uGUI 面板管理

`UIManager` 按资源路径管理面板。框架启动时由根节点创建，注入 `IAssetService`、`IGameServices` 与 `ILogService`，初始化 Canvas、相机和 Background、Main、Popup、Guide、Toast 五个层级。`UIManager` 不再是单例；需要打开面板的场景组件实现 `IUIServiceConsumer`，由根节点注入 `IUIService`。`BasePanel` 在 Awake 收集子控件，在 `OnOpen`、`OnPause`、`OnResume`、`OnClose` 调用业务逻辑。 [管理器](UIManager.cs) · [面板基类](BasePanel.cs) · [UI 契约](IUIService.cs)

## 接入一个面板

1. 在 `Resources/UI/InventoryPanel.prefab` 创建预制体，或将它纳入 AssetBundle 清单。预制体根节点须是 `RectTransform`；默认隐藏的预制体会在打开时先激活。
2. 编写 `BasePanel` 子类，用 `UILoadInfo` 标注层级、资源方式和路径。Resources 路径不带扩展名，AB 路径使用清单中的全路径。
3. 在框架就绪后，从 Unity 主线程调用 `OpenAsync<T>`（需要句柄/状态）或兼容入口 `ShowPanelAsync<T>`。如果面板随场景销毁，传入场景的作用域。对空结果作失败或关闭处理。

```csharp
using UPandaGF;

[UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/InventoryPanel")]
public sealed class InventoryPanel : BasePanel
{
    public override void OnOpen(object panelArg = null)
    {
        // 根据参数刷新显示。
    }

    public override void OnPause()
    {
        // 被上层模态面板遮挡：停输入/动画。
    }

    public override void OnResume()
    {
        // 上层模态关闭：恢复。
    }

    public override void OnClose()
    {
        // 清理面板开启时登记的监听。
    }
}
```

```csharp
// 场景组件同时实现两个消费者接口：根节点在场景加载后注入。
public sealed class InventoryLauncher : MonoBehaviour, IUIServiceConsumer, IGameServicesConsumer
{
    private IUIService ui;
    private IGameServices services;

    public void ConfigureUi(IUIService value) { ui = value; }
    public void Configure(IGameServices value) { services = value; }

    public async Task OpenInSceneScope()
    {
        var scope = services.Scenes.GetScope(gameObject.scene);
        var handle = await ui.OpenInScopeAsync<InventoryPanel>(scope);
        if (handle == null)
            return;

        // 主动关闭：await handle.CloseAsync()，或 ui.ClosePanel<InventoryPanel>()。
    }
}
```

`BasePanel.uiManager`、`BasePanel.UI` 与 `BasePanel.Services` 由 `UIManager` 在实例化后注入，`OnOpen`/`OnClose` 中可用；Awake/OnEnable 阶段还没有注入，不要在这两个方法里访问它们。通过 `UIManager.Configure(IGameServices)` 装配时，面板整棵子树中实现 `IGameServicesConsumer`/`IUIServiceConsumer` 的组件也会一并收到注入。

`GetControl<T>(name)` 按 GameObject 名称和控件类型检索子控件。为业务控件使用唯一且稳定的名称。`ShowPanel<T>(panelArg, callBack)` 是回调兼容入口，`GetPanel<T>()` 查询已存在的面板。 [面板声明](UIAttribute/UILoadInfoAttribute.cs) · [加载面板示例](SimpleLoadUI.cs)

`UILoadInfo.placement` 控制根节点布局：`FullScreen`（默认，铺满层级）、`Window`（保留预制体尺寸/锚点并居中）、`Preserve`（完全保留预制体布局）。`modal` 开启模态遮挡，`pauseGame` 申请暂停凭据，`closeOnBack` 控制返回键（默认 true），`order` 控制同层排序，`pool = true` 让面板关闭后进入空闲池复用。

## 层、栈、模态与返回

| 层 | 语义 |
| --- | --- |
| `Background` | 背景/常驻底图 |
| `Main` | 主页面、HUD 页面 |
| `Popup` | 弹窗、设置、确认框（通常配 `modal = true`） |
| `Guide` | 新手引导/强制说明 |
| `Toast` | 轻提示；不参与返回栈 |

- `OpenAsync<T>` 返回 [`UIPanelHandle<T>`](UIPanelHandle.cs)；`handle.CloseAsync()` 只关闭该代次的面板，旧句柄不会误关重开的新实例。
- `GetOpenPanels()` 按层与同层顺序返回快照；`CloseTop(layer)` 关闭指定层栈顶；`CloseAll(layer?)` 关闭全部/指定层。
- 模态面板会调用下方未暂停面板的 `OnPause`，并把它们的 CanvasGroup 设为不可交互/不拦截射线；关闭后恢复并调用 `OnResume`。
- `HandleBack()` 只处理栈顶可关闭面板；顶层面板 `closeOnBack=false` 时会拦截，不会越过它关闭下层。Toast 不参与返回栈。
- `ShowToastAsync<T>(arg, duration)` 要求面板声明 `E_UI_Layer.Toast`，返回后按 duration 自动关闭。

## 生命周期

- 同路径并发打开共享一份加载和面板实例，只有首次成功打开执行 `OnOpen`；已打开时再次请求会置顶并调用 `OnRefresh(panelArg)`，不会重复执行 `OnOpen`。同一路径须对应一个面板类型和一个作用域；跨作用域或跨类型请求会记录错误并返回空。
- 主动关闭或释放所属作用域会先从登记表移除面板，调用 `OnClose` 后销毁。加载中的请求关闭时会取消本地 CTS，等待者返回空；底层加载的迟到租约仍会归还。面板资源租约由实例销毁时归还，业务无需再手动卸载。外部直接销毁面板时也会清理记录并补调一次 `OnClose`。
- `Shutdown()` 结束等待并销毁面板；当前实现会先对已打开面板调用 `OnClose()`，再销毁对象，单个面板回调异常会被隔离。
- 默认隐藏的预制体会在绑定控件前被激活，确保 `Awake` 先完成控件缓存。`OnOpen` 内允许自关闭或重新打开；管理器会用记录身份复核，避免把已关闭实例当成功返回。`Awake/OnEnable` 阶段仍建议只做自身初始化，不要发起同路径打开/关闭。
- 面板的 `Events`/`OpenEvents` 是 [`UIEventScope`](UIEventScope.cs)：`Listen`/`Subscribe`/`Add` 登记的事件在关闭/销毁时由 UIManager 统一释放；`AddCustomEventListener` 也走同一作用域。`BasePanel` 的控件缓存现在一次遍历完成，并支持 TMP_InputField、TMP_Dropdown、Scrollbar、ToggleGroup。

## 面板池与诊断

- `[UILoadInfo(..., pool = true)]` 的面板关闭后不销毁，而是从层级移入 `UIPanelPool`；再次打开优先复用实例和控件缓存。
- `UIManager.MaxPooledPerPath` 控制每个路径的空闲实例上限（默认 2，`0` 关闭缓存）。池化实例在 `ResetForHotUpdate`、`Shutdown` 或池清空时销毁并归还预制体租约。
- 复用实例保留 `Events` 控件绑定；每次关闭释放 `OpenEvents` 并调用 `OnClose`，每次打开重新调用 `OnOpen`。业务订阅应放在 `OpenEvents`，不要放进 `Events`。
- `UIManager.CaptureDiagnostics()` 返回 `UIDiagnosticsSnapshot`，包含面板状态/层级/排序/模态/暂停/池标记、加载与打开耗时、池命中/未命中/创建/回收/销毁；`FrameworkDiagnostics.Capture(root).UI` 和日志控制台诊断页也会展示。

## 数据绑定与安全区

- `Observable<T>`（以及热更友好的 `BoolObservable`/`FloatObservable`/`IntObservable`/`StringObservable`/`ColorObservable`）只在值变化时推送；UI 不轮询。
- `UIBinder` 提供 Text、Toggle、Slider、Dropdown、Selectable、GameObject、Graphic、Button 的常用绑定；TMP_Text/TMP_Dropdown 在 `TMPUIBinder` 中，避免不引用 TMP 的程序集被动依赖。绑定返回 `IDisposable`，推荐加入 `BasePanel.OpenEvents` 自动释放。
- Toggle/Slider/Dropdown 使用 `SetValueWithoutNotify` 防止双向绑定回环；ui 改动会回写 Observable。
- 本地化：`LocalizedText`（uGUI）/`LocalizedTMPText`（TMP）填 key 即可，组件由 `IGameServicesConsumer` 注入 `ILocalizationService`，语言切换自动刷新；动态代码可用 `LocalizationBinder.Bind(localization, text, key, args)`，返回 `IDisposable` 加入 `BasePanel.OpenEvents`。语言行/设置面板的 key 约定见 [本地化 README](../../Localization/README.md)。
- 设置面板自带 `UIFocusNavigator`/`UIFocusInputBinder`（手柄/键盘焦点、Cancel 关闭）；`accessibility.uiScale` 由 `UIManager.Configure(IGameServices)` 自动消费，移动端自动挂 `UISafeArea`。
- `UIManager.AutoCreateEventSystem`（默认 `true`）控制场景没有 EventSystem 时是否自动创建；通过 `UPGameRootConfig.autoCreateEventSystem` 配置。关闭后需在首个 UI 打开前预置 EventSystem 和输入模块，否则点击/焦点导航不可用。
- 设置面板多选项统一使用下拉；列表在 ScrollRect 外的 `DropdownOverlay` 上生成，避免被 viewport 的 `RectMask2D` 裁剪。`SettingsDropdownCanvasFixer` 把列表 Canvas 的 worldCamera/planeDistance 对齐根 Canvas，避免 Scene 可见但 Game 不可见；Template/Item 使用显式 `anchoredPosition + sizeDelta`，不要用负高度 offset。
- `UISafeArea` 把 RectTransform 锚定到 `Screen.safeArea`，仅在安全区/分辨率变化时重算。

```csharp
StringObservable title = new StringObservable("Settings");
UIBinder.AddTo(OpenEvents, UIBinder.BindText(titleLabel, title));
BoolObservable muted = new BoolObservable(false);
UIBinder.AddTo(OpenEvents, UIBinder.BindToggle(muteToggle, muted));
```

## 动态列表与设置行

- [`UISettingItem`](UISettingItem.cs) 是设置行的视图描述（Key/Label/Description/控件类型/Observable/选项/范围），不依赖 `ISettingsService`；业务 Presenter 负责把设置服务包装成 `BoolObservable`/`FloatObservable`/`IntObservable`/`StringObservable`。
- [`UISettingList`](UISettingList.cs) 按控件类型从行池取用行预制体；同索引同类型只重新绑定（不重新实例化），控件类型变化时换行，行数减少时回到池中。`CreatedRows`/`ReusedRows`/`ReleasedRows`/`PooledRows` 用于诊断复用效果。
- 行预制体根节点挂 [`UISettingToggleRow`](UISettingRows.cs)/`UISettingSliderRow`/`UISettingDropdownRow`/`UISettingButtonRow`/`UISettingReadOnlyRow`；`Key` 未单独注册时回退到 Button 行。`Label`/`Value`/`Description` 可按名称自动查找，uGUI Text 与 TMP_Text 都支持。
- Slider 支持 Min/Max/Step 吸附与数值文本；Dropdown 选项来自描述；Button/Key 的点击由 `Action` 承接。行的 `Bind` 会先释放上一次订阅，池化复用不会叠加监听。
- [`UIItemPool`](UIItemPool.cs) 是不限于设置面板的通用行池，也可用于背包/任务等滚动列表；空闲上限、清理与计数见代码。
- 框架内置系统设置面板：[`SettingsPresenter`](SettingsPresenter.cs) 把 `ISettingsService` 描述表转成 `UISettingItem`（含键位捕获、预览确认、分区过滤）；[`SettingsPanel`](SettingsPanel.cs) 在运行时构建布局，预制体只需根节点（`Resources/UI/SettingsPanel`）。打开方式 `services.UI.ShowPanelAsync<SettingsPanel>()`，新增设置项不需要改面板。

```csharp
UISettingList list = settingsPanel.GetComponentInChildren<UISettingList>();
list.SetRowPrefab(UISettingControl.Toggle, toggleRowPrefab); // 也可在 Inspector 注册
list.SetItems(new[]
{
    UISettingItem.CreateToggle("audio.mute", "静音", muted),
    UISettingItem.CreateSlider("audio.volume", "音量", volume, 0f, 1f, 0.05f),
    UISettingItem.CreateDropdown("display.mode", "显示模式", mode, new[] { "全屏", "窗口" })
});
```

## 焦点导航与 UI 缩放

- [`UIFocusNavigator`](UIFocusNavigator.cs) 管理面板/页签内焦点：`FocusFirst` 默认选中、`Navigate` 方向移动、`NavigateOrAdjust` 在滑块上左右微调、`Submit` 确认、`Cancel` 走 `BackRequested`、`RememberFocus`/`RestoreFocus` 切页恢复；只接管本节点子树，不会抢其它面板的选中项。
- [`UIFocusInputBinder`](UIFocusInputBinder.cs) 可选组件：把 `IInputService` 的 UI 上下文动作（`NavigateVertical/NavigateHorizontal/Confirm/Cancel`）接到导航器，支持按住方向键重复导航；通用面板按需挂载，内置设置面板会自动挂载。
- 导航器不直接读输入设备，由面板把 `IInputService` 的动作映射到 `Navigate`/`Submit`/`Cancel`/`Adjust`，旧 Input Manager 与 Input System 都适用。
- [`UICanvasScale`](UICanvasScale.cs) 把设计分辨率按缩放系数换算到 `CanvasScaler.referenceResolution`（0.5–2，1 为基准）；`UIManager.SetUiScale`/`IUIService.UiScale` 控制框架 Canvas，也可 `Bind(FloatObservable)` 跟随设置项。它只改参考分辨率，不改 match 模式或像素密度。`UIManager.Configure(IGameServices)` 会自动消费 `accessibility.uiScale`。
- 与 [`UISafeArea`](UISafeArea.cs) 组合覆盖 0.8–1.3 缩放、刘海屏与 21:9。

```csharp
navigator.SetDefault(firstButton);
navigator.BackRequested += () => UI.ClosePanel<SettingsPanel>();

// 面板的输入控制器里：
if (services.Input.GetButtonDown("Cancel", "UI")) navigator.Cancel();
```

## 渲染管线适配

- `UIManager` 在创建或接收 UI 相机后自动调用 [`UICameraPipeline`](UICameraPipeline.cs) 协商相机关系：
  - **Built-in**：保证 `depth >= 10`，保持 Canvas 的 `ScreenSpaceCamera`。
  - **URP（方案 B）**：若场景存在 `Camera.main`，把 UI 相机设为 `Overlay` 并加入主相机的 Camera Stack，关闭 UI 相机的后处理；主相机保持 `Base`。主相机尚未加载时先回退 Base，随后在 `SceneManager.sceneLoaded`、管线切换或手动 `RefreshCameraStack()` 时重试。
  - **HDRP**：反射追加 `HDAdditionalCameraData` 并开启 `clearDepth`。
- 不引用 URP/HDRP 程序集：未安装对应管线时只记录一次警告，Built-in 工程照常工作。`RenderPipelineManager.activeRenderPipelineTypeChanged` 会在运行时切换 SRP 后重新适配，无需重建 Canvas。
- `UICameraPipeline.Detect()` 返回 `BuiltIn`/`Universal`/`HighDefinition`/`Unknown`；`ConfigureOverlay`/`RemoveFromStack`/`MakeBase` 可供自定义相机流程使用。`UIManager.UiCameraStackOwner` 可查询当前持有 UI 相机的主相机。
- 通过 `UICameraRegister`/`ResetCamera` 注册的场景 UI 相机走同一流程；若相机已预先配置为 Overlay 并加入 stack，`UICameraPipeline` 会保留该配置，不会改回 Base。

## 校验与脚本生成

- 菜单 **UPandaGF/UI/校验面板配置** 扫描运行时程序集里的 `BasePanel` 子类：`[UILoadInfo]`、`loadPath` 唯一性与格式、层级/加载方式、Resources 预制体根节点是否为 `RectTransform`、重名交互控件；错误会在 Console 中逐条列出。
- CI/批处理入口：[`UIPanelValidator.ValidateForCI`](../../../Editor/UIEditor/UIPanelValidator.cs) 或 `Tools/ci/validate-ui-panels.ps1` / `.sh`；EditMode 用例 `PackagePanelsPassValidation` 也会在 CI 中失败。默认跳过测试/示例/Editor 程序集，宿主 `Assembly-CSharp` 的业务面板会纳入。
- 菜单 **UPandaGF/UI/面板脚本生成器**（或 `GameObject/UPandaGF/UI/生成面板脚本`）生成 `partial BasePanel` 脚本：`[UILoadInfo]`、控件字段、`OnAwake` 控件缓存、`OnOpen`/`OnClose` 模板与可选事件桩。
- 生成器使用 `TryGetControl` 按 GameObject 名称取控件，不依赖层级路径；TMP 类型（`TMP_InputField`/`TMP_Dropdown`/`TextMeshProUGUI`）已支持，不再残留 `EditorApplication.update` 回调。
- 生成后按提示把组件挂到预制体根节点（或让 `UIManager` 按 Type 添加），并运行面板校验。旧项目升级见 [MIGRATION-UI.md](../../../Documentation~/MIGRATION-UI.md)。

## 热更新接入

- 面板脚本可放在热更新程序集；推荐预制体只保留 AOT 视觉节点，由 `UIManager` 在打开时通过 Type 添加面板组件。管理器使用 `AddComponent(Type)`，不会从 AOT 代码调用 `AddComponent<T>`。
- 热更程序集加载完成后调用 `RegisterPanels(assembly)` 或 `RegisterPanel(type)`；`GameLaunchExample` 已在加载每个程序集后注册。按 `UILoadInfo.loadPath` 打开时，未显式注册也会从已加载程序集兜底扫描。
- 热更代码使用非泛型入口：`OpenAsync(Type)`、`OpenInScopeAsync(Type, scope)`、`OpenAsync(loadPath)`、`ClosePanel(Type)`、`GetPanel(Type)`、`ShowToastAsync(Type)`。泛型 API 只用于 AOT 已知类型。
- 卸载/重载程序集前调用 `ResetForHotUpdate()`：取消加载中请求、关闭面板、清空 Toast 队列和类型缓存；之后注册新程序集。不要继续使用旧句柄或旧 Type。
- HybridCLR 的 AOT 元数据/泛型注册仍由宿主启动流程负责；UI 程序集不直接依赖 HybridCLR API。

[面板加载和关闭](UIManager.cs) · [资源凭据](PanelResourceLease.cs) · [Phase 1 UI 回归](../../../Tests/PlayMode/UIPhase1RegressionTests.cs) · [Phase 2 层栈回归](../../../Tests/PlayMode/UIPhase2LayerStackTests.cs) · [列表/焦点回归](../../../Tests/PlayMode/UIListAndFocusTests.cs) · [作用域回归](../../../Tests/PlayMode/P1RegressionTests.cs)

## 场景 UI 组件

| 组件 | 使用方法和边界 |
| --- | --- |
| [UICanvasRegister](UICanvasRegister.cs)、[UICameraRegister](UICameraRegister.cs) | 在 Start 注册场景的 Canvas 或 Camera。自定义 Canvas 缺少层级时会自动补层（新名 `Background/Main/Popup/Guide/Toast`，兼容旧名 `Bot/Mid/Top/System`）；已有面板或加载中请求时 `ResetCanvas` 会拒绝切换，避免面板落到旧 Canvas。EventSystem 缺失时会创建，并按当前 Input Handling 选择默认输入模块。 |
| [SimpleLoadUI](SimpleLoadUI.cs) | 使用随框架提供的 `UI/SimpleLoadUI` 预制体，调用 `SetMessage(progress, message)` 推进显示。由业务连接场景进度，预制体需保留 `ProgressSlider`、`message` 和 CanvasGroup。 |
| [DragArea](UITool/DragArea.cs) | 将脚本挂到带 Image 的交互节点，指定要拖动的 RectTransform。使用其父 RectTransform 的坐标系更新位置，拖拽范围及放置规则由业务实现。 |
| [WorldSpaceOverlayUI](UIRender/WorldSpaceOverlayUI.cs) | 在 Start 为指定或当时找到的 Graphic、TextMeshProUGUI 复制材质并设置 `unity_GUIZTestMode`。效果取决于 shader 是否读取该属性。组件销毁时恢复材质；后来生成的控件需要单独接入。 |
| [UnityBuiltinFont](UnityBuiltinFont.cs) | 编辑器生成资产可用 `Get()` 获取内置字体。运行期 `GetDefault()` 优先尝试系统 CJK 字体，设备无相应字形时回退。`ReplaceBuiltinFonts(root)` 只处理普通 uGUI Text。 |

[加载面板预制体](../../Resources/UI/SimpleLoadUI.prefab)
