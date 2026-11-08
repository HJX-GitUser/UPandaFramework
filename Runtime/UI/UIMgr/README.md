# uGUI 面板管理

`UIManager` 按资源路径管理面板。框架启动时由根节点创建，注入 `IAssetService` 与 `ILogService`，初始化 Canvas、相机和 Bot、Mid、Top、System 四个层级。`UIManager` 不再是单例；需要打开面板的场景组件实现 `IUIServiceConsumer`，由根节点注入 `IUIService`。`BasePanel` 在 Awake 收集子控件，在 `OnOpen` 和 `OnClose` 调用业务逻辑。 [管理器](UIManager.cs) · [面板基类](BasePanel.cs) · [UI 契约](IUIService.cs)

## 接入一个面板

1. 在 `Resources/UI/InventoryPanel.prefab` 创建预制体，或将它纳入 AssetBundle 清单。预制体根节点须是 `RectTransform`。保持预制体及其画布父层级可激活，以便 Awake 初始化和销毁时归还资源。
2. 编写 `BasePanel` 子类，用 `UILoadInfo` 标注层级、资源方式和路径。Resources 路径不带扩展名，AB 路径使用清单中的全路径。
3. 在框架就绪后，从 Unity 主线程调用 `ShowPanelAsync<T>`。如果面板随场景销毁，传入场景的作用域。对空结果作失败或关闭处理。

```csharp
using UPandaGF;

[UILoadInfo(E_UI_Layer.Mid, AssetLoadMethod.Resources, "UI/InventoryPanel")]
public sealed class InventoryPanel : BasePanel
{
    public override void OnOpen(object panelArg = null)
    {
        // 根据参数刷新显示。
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
        var panel = await ui.ShowPanelInScopeAsync<InventoryPanel>(scope);
        if (panel == null)
            return;

        // 主动关闭也可用 ui.ClosePanel<InventoryPanel>()。
    }
}
```

`BasePanel.uiManager` 由 `UIManager` 在实例化后注入，`OnOpen`/`OnClose` 中可用；Awake/OnEnable 阶段还没有注入，不要在这两个方法里访问它。

`GetControl<T>(name)` 按 GameObject 名称和控件类型检索子控件。为业务控件使用唯一且稳定的名称。`ShowPanel<T>(panelArg, callBack)` 是回调兼容入口，`GetPanel<T>()` 查询已存在的面板。 [面板声明](UIAttribute/UILoadInfoAttribute.cs) · [加载面板示例](SimpleLoadUI.cs)

## 生命周期

- 同路径并发打开共享一份加载和面板实例，各成功调用会执行一次自己的 `OnOpen`。同一路径须对应一个面板类型和一个作用域；尝试跨作用域共享会记录异常并返回空。
- 主动关闭或释放所属作用域会先从登记表移除面板，调用 `OnClose` 后销毁。待加载请求关闭时，等待者返回空，迟到资源会被归还。面板资源租约由实例销毁时归还，业务无需再手动卸载。
- `Shutdown()` 结束等待并销毁面板；当前实现会先对已打开面板调用 `OnClose()`，再销毁对象，单个面板回调异常会被隔离。
- 面板的 Awake 和 OnEnable 在创建过程中同步执行。避免在这些方法里关闭或重新打开同路径面板，当前实现可能在请求被关闭后仍登记旧实例。由调用方在打开任务结束后协调二次开关。

[面板加载和关闭](UIManager.cs) · [资源凭据](PanelResourceLease.cs) · [UI 回归](../../../Tests/PlayMode/FrameworkRegressionTests.cs) · [作用域回归](../../../Tests/PlayMode/P1RegressionTests.cs)

## 场景 UI 组件

| 组件 | 使用方法和边界 |
| --- | --- |
| [UICanvasRegister](UICanvasRegister.cs)、[UICameraRegister](UICameraRegister.cs) | 在 Start 注册场景的 Canvas 或 Camera，替换时旧对象会停用，已打开的面板不会迁移。自定义 Canvas 保留四个层级，并为需要交互的场景提供 EventSystem。默认 EventSystem 只在管理器创建默认 Canvas 的路径里兜底。 |
| [SimpleLoadUI](SimpleLoadUI.cs) | 使用随框架提供的 `UI/SimpleLoadUI` 预制体，调用 `SetMessage(progress, message)` 推进显示。由业务连接场景进度，预制体需保留 `ProgressSlider`、`message` 和 CanvasGroup。 |
| [DragArea](UITool/DragArea.cs) | 将脚本挂到带 Image 的交互节点，指定要拖动的 RectTransform。使用其父 RectTransform 的坐标系更新位置，拖拽范围及放置规则由业务实现。 |
| [WorldSpaceOverlayUI](UIRender/WorldSpaceOverlayUI.cs) | 在 Start 为指定或当时找到的 Graphic、TextMeshProUGUI 复制材质并设置 `unity_GUIZTestMode`。效果取决于 shader 是否读取该属性。组件销毁时恢复材质；后来生成的控件需要单独接入。 |
| [UnityBuiltinFont](UnityBuiltinFont.cs) | 编辑器生成资产可用 `Get()` 获取内置字体。运行期 `GetDefault()` 优先尝试系统 CJK 字体，设备无相应字形时回退。`ReplaceBuiltinFonts(root)` 只处理普通 uGUI Text。 |

[加载面板预制体](../../Resources/UI/SimpleLoadUI.prefab)
