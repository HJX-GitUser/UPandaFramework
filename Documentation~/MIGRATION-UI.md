# UI 框架迁移指南（0.8 → 0.9）

> 适用于从 `UI Phase 0/1`（0.8.0 工作树）及更早版本升级到当前 UI Phase 4/5 的项目。
> 旧 API 仍保留到 1.0；本指南按“不改可运行 → 推荐改法”组织。

## 1. UIManager 不再是单例

旧代码：

```csharp
UIManager.Instance.ShowPanelAsync<SettingsPanel>();
```

新代码实现 `IUIServiceConsumer`（场景组件）或从 `UPGameRoot.UI` 取服务：

```csharp
public sealed class SettingsLauncher : MonoBehaviour, IUIServiceConsumer
{
    private IUIService ui;
    public void ConfigureUi(IUIService service) { ui = service; }

    public async Task OpenAsync()
    {
        UIPanelHandle<SettingsPanel> handle = await ui.OpenAsync<SettingsPanel>();
        if (handle != null) await handle.CloseAsync();
    }
}
```

`UIManager` 仍保留 `ShowPanelAsync<T>`/`ShowPanel<T>`/`GetPanel<T>` 兼容入口，但内部已转到句柄/状态机。新代码优先 `OpenAsync` 并检查返回是否为 null。

## 2. 生命周期

| 阶段 | 调用时机 | 说明 |
| --- | --- | --- |
| `Awake` / `OnAwake` | 实例创建/池复用时一次 | 只做自身初始化、控件缓存；此时 `UI`/`Services` 尚未注入 |
| `OnOpen` | 每次成功打开 | 应用参数、刷新显示；`OpenEvents` 可用 |
| `OnRefresh` | 已打开时再次请求 | 只置顶+刷新，不重复 `OnOpen` |
| `OnPause` / `OnResume` | 被上层模态遮挡/恢复 | 停输入、动画、计时器 |
| `OnClose` | 每次关闭 | 停止逻辑；`OpenEvents` 由框架释放 |

必须实现抽象的 `OnOpen(object panelArg = null)` 与 `OnClose()`。`OnAwake` 是可选的 `protected override`。

> 注意：`BasePanel.Awake` 负责一次性控件缓存与事件登记，业务不要覆盖 `Awake`；如需初始化重写 `OnAwake`。

## 3. 层级枚举

新名与旧名同值，序列化不会错位：

| 新名 | 旧名 | 值 | 用途 |
| --- | --- | --- | --- |
| `Background` | `Bot` | 0 | 背景/常驻底图 |
| `Main` | `Mid` | 1 | 主页面、HUD |
| `Popup` | `Top` | 2 | 弹窗、设置、确认框 |
| `Guide` | `System` | 3 | 引导、强制说明 |
| `Toast` | （新增） | 4 | 轻提示 |

场景节点同时兼容 `Background/Main/Popup/Guide/Toast` 与 `Bot/Mid/Top/System`；缺失节点由 `UIManager` 自动补建。新预制体/场景建议直接用新名。

```csharp
// 旧
[UILoadInfo(E_UI_Layer.Top, AssetLoadMethod.Resources, "UI/SettingsPanel")]
// 新（同值，推荐）
[UILoadInfo(E_UI_Layer.Popup, AssetLoadMethod.Resources, "UI/SettingsPanel", modal = true)]
```

`UILoadInfo` 新增可选属性：`placement`、`modal`、`pauseGame`、`closeOnBack`、`order`、`pool`；三参数构造函数保持不变。

## 4. 打开/关闭与竞态

- 同路径并发打开共享一次加载，只有首次成功打开执行 `OnOpen`；已打开时再次请求会置顶并调用 `OnRefresh`。
- 加载中关闭会取消本次请求；迟到的资源租约只归还，不再注册/`OnOpen`。
- 同一路径只能对应一个面板类型与一个作用域；跨类型/跨作用域请求会记录错误并返回 null。
- `OpenAsync<T>`/`OpenInScopeAsync<T>`/`OpenAsync(Type)`/`OpenAsync(loadPath)` 返回句柄；旧句柄带代次，不会误关重开的新实例。
- 返回键：`UIManager.HandleBack()` 只关栈顶可关闭面板；`closeOnBack=false` 会拦截。
- Toast：`ShowToastAsync<T>(arg, duration)` 要求面板声明 `E_UI_Layer.Toast`，队列串行、不参与返回栈。

```csharp
Task<UIPanelHandle<SettingsPanel>> opening = ui.OpenAsync<SettingsPanel>(arg);
// 用户立刻关闭
ui.ClosePanel<SettingsPanel>();
var handle = await opening; // null：已取消，不是异常
```

## 5. 事件自动解绑

旧写法：

```csharp
closeButton.onClick.AddListener(Close);
settings.Changed += OnSettingsChanged;
// OnClose 里手动 RemoveListener/退订，容易漏
```

新写法使用面板作用域：

```csharp
protected override void OnAwake()
{
    // 控件级、随面板实例存活
    Events.Listen(closeButton.onClick, Close);
}

public override void OnOpen(object panelArg = null)
{
    // 每次打开登记，关闭/池化复用时自动释放
    OpenEvents.Listen(slider.onValueChanged, OnVolumeChanged);
    OpenEvents.Subscribe(bus, this);
    OpenEvents.Add(settings.Changed.Subscribe(OnSettingsChanged));
}
```

`OnClose` 会自动 `Dispose` `OpenEvents`；`OnDeinit`/销毁时会释放 `Events`。不要在 `OnClose` 里重复退订同一事件；自定义 `IDisposable` 可直接 `OpenEvents.Add`。

`UIManager.AddCustomEventListener` 静态版本仍保留；面板内优先用 `Events.Add`/`BasePanel.AddCustomEventListener`，它们会随面板释放。

## 6. 面板池

```csharp
[UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/Inventory", pool = true)]
```

- 关闭后实例移入池，再次打开优先复用；`UIManager.MaxPooledPerPath` 控制空闲上限（默认 2，0 关闭）。
- 每次关闭调用 `OnClose` 并释放 `OpenEvents`；每次打开重新 `OnOpen`。
- 池化面板必须在 `OnClose`/`OnAwake` 重置可见状态（滚动位置、动画、计时器、临时文本），不要留下上次数据。
- 业务订阅放 `OpenEvents`，不要放 `Events`；否则复用时仍持有旧监听。

## 7. 数据绑定

旧写法：在 `Update` 里读 Model、手动刷新；或每次打开读一次。

新写法：

```csharp
private readonly BoolObservable muted = new BoolObservable();
private readonly FloatObservable volume = new FloatObservable();

public override void OnOpen(object panelArg = null)
{
    UIBinder.AddTo(OpenEvents, UIBinder.BindToggle(muteToggle, muted));
    UIBinder.AddTo(OpenEvents, UIBinder.BindSlider(volumeSlider, volume));
    UIBinder.AddTo(OpenEvents, UIBinder.BindText(volumeLabel, volume, v => (v * 100f).ToString("0") + "%"));
}
```

- `Observable<T>` 只在值变化时推送；UI 不轮询。
- Toggle/Slider/Dropdown 双向绑定使用 `SetValueWithoutNotify` 防回环。
- TMP 绑定在 `TMPUIBinder`；不引用 TMP 的程序集只依赖 `UIBinder`。
- 绑定返回 `IDisposable`，放进 `OpenEvents` 即可随关闭释放。

## 8. 动态列表与设置行

设置面板不再为每个设置项写 `if`：用描述表 + 行池。

```csharp
UISettingList list = GetControl<UISettingList>("SettingList");
list.SetItems(new[]
{
    UISettingItem.CreateToggle("audio.mute", "静音", muted),
    UISettingItem.CreateSlider("audio.volume", "音量", volume, 0f, 1f, 0.05f),
    UISettingItem.CreateDropdown("display.mode", "显示模式", mode, new[] { "全屏", "窗口" })
});
```

- 行预制体根节点挂 `UISettingToggleRow`/`UISettingSliderRow`/`UISettingDropdownRow`/`UISettingButtonRow`/`UISettingReadOnlyRow`，在 `UISettingList` Inspector 注册；`Key` 未注册时回退 Button 行。
- 新增设置项只加一条描述；控件类型变化时换行，行数减少时回收。
- `UIItemPool` 是通用行池，也可用于背包/任务列表。
- UI 框架不依赖 `ISettingsService`；Presenter 负责把设置服务包装成 Observable 并写回。

## 9. 焦点与缩放

```csharp
[SerializeField] private UIFocusNavigator navigator;

public override void OnOpen(object panelArg = null)
{
    navigator.SetDefault(firstButton);
    navigator.BackRequested += () => UI.ClosePanel<SettingsPanel>();
}
```

- `FocusFirst`/`Navigate`/`NavigateOrAdjust`/`Submit`/`Cancel`/`RememberFocus`/`RestoreFocus`；只接管自身子树，不抢其它面板焦点。
- 导航器不直接读输入设备；面板把 `IInputService` 动作映射进来，同时适配旧 Input Manager 与新 Input System。
- `UIManager.SetUiScale(1.25f)`（或 `UICanvasScale.Scale`/`Bind(FloatObservable)`）按反比换算 CanvasScaler 参考分辨率；与 `UISafeArea` 组合覆盖 0.8–1.3 缩放、刘海与 21:9。

### UI 相机与渲染管线

- `UIManager` 自建或通过 `UICameraRegister`/`ResetCamera` 接收的相机都会自动调用 `UICameraPipeline.Adapt`：Built-in 保证 `depth >= 10`；URP 追加 `UniversalAdditionalCameraData` 并设为 Base/priority；HDRP 追加 `HDAdditionalCameraData` 并开启 `clearDepth`。
- 不需要在业务里手动 `AddComponent<UniversalAdditionalCameraData>()`；包不引用 SRP 程序集，未安装对应管线时只警告一次。
- 运行时切换画质/渲染管线后会自动重新适配；URP 使用 Base+priority 方案，不主动维护 Camera Stack。若项目已有 Overlay UI 相机栈，请在注册前配置并确认 `UICameraRegister._camera` 指向该相机。

## 10. 校验器与脚本生成器

- 菜单 **UPandaGF/UI/校验面板配置**：扫描运行时程序集里的 `BasePanel` 子类，检查 `[UILoadInfo]`、路径唯一性/格式、层级、Resources 预制体根 RectTransform、重名交互控件。
- CI/批处理：`Tools/ci/validate-ui-panels.ps1` / `.sh`，或直接 `-executeMethod UPandaGF.GFEditor.UIPanelValidator.ValidateForCI`；EditMode 用例 `PackagePanelsPassValidation` 也会在 CI 中失败。
- 菜单 **UPandaGF/UI/面板脚本生成器** 或 **GameObject/UPandaGF/UI/生成面板脚本**：生成 `partial BasePanel` + `[UILoadInfo]` + `OnAwake` 控件缓存 + `OnOpen/OnClose` + 可选事件桩；不再使用 `EditorApplication.update`，无残留编辑器回调。
- 生成的脚本使用 `TryGetControl` 按 GameObject 名称取控件；请保证业务控件名唯一，重名时用 `UIPanelValidator` 发现。
- 生成脚本固定 `using TMPro;`；若放在自定义 asmdef 中，需要给该 asmdef 添加 `Unity.TextMeshPro` 引用。

## 11. 兼容性检查清单

- [ ] 所有 `BasePanel` 子类都有 `[UILoadInfo]` 与 `OnOpen`/`OnClose` 实现。
- [ ] 旧层级枚举名（`Bot/Mid/Top/System`）可继续编译；新代码改用新名。
- [ ] 面板内不再直接 `Resources.Load` / `FindObjectOfType<UIManager>`，改用 `IAssetService` / 注入的 `IUIService`。
- [ ] 手动 `AddListener`/`RemoveListener` 已迁移到 `Events`/`OpenEvents`（或确认成对且覆盖所有路径）。
- [ ] 池化面板的 `OnClose`/`OnAwake` 会重置滚动、动画、计时器与临时文本。
- [ ] 模态面板的 `OnPause`/`OnResume` 能停止/恢复输入与计时器。
- [ ] 设置面板用描述表 + 行池，不再新增 `if (key == ...)`。
- [ ] 手柄焦点、返回键、UI 缩放与安全区在目标设备复测。
- [ ] 运行 `UPandaGF/UI/校验面板配置`，0 error。

## 12. 已知限制

- 旧 `panelDic` 仍为公开字段以兼容读取，1.0 前会收口；新代码用 `GetPanel`/`GetOpenPanels`。
- `UIPanelHandle` 关闭后 `Panel` 为 null；不要缓存 `Panel` 引用越过关闭。
- HybridCLR 的 AOT 元数据/泛型注册仍由宿主启动流程负责；热更面板按 `RegisterPanels`/`RegisterPanel` 注册。
- Input System-only 宿主仍需用 `InputSystemUIInputModule`（UIManager 会尝试替换旧模块）。
- AssetBundle 预制体存在性不在编辑器校验器里检查，需在打包清单阶段验证。
- `Framework Validation` 工程为保证宿主脚本编译保留了 3 个 asmdef 的 `autoReferenced=true`；同步包时不要用 `sync-package` 的 `/MIR` 覆盖，或同步后改回。
