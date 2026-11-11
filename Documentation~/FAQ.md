# UPandaFramework FAQ

> 适用：`0.8.0` 及以上，Unity 2021.3 LTS 及以上。  
> 维护：新问题先归入对应章节，答案写清“最短结论、边界、代码、验证方式”；不要只记录一次现场结论。  
> 本文是使用问答入口，模块细节仍以各目录的 `README` 为准。

## 0. 快速开始

### Q0.1 最短接入步骤是什么？

1. 通过 UPM Git URL 安装，或把整个 `upanda-framework`（含 `.meta`）复制到宿主工程的 `Assets/upanda-framework`。
2. 在启动场景执行菜单 `UPandaGF → 创建UPGameRoot`。
3. 在 Inspector 选择资源模式，配置首场景；使用 AB 时先准备目标平台资源包和清单。
4. 保存场景并加入 Build Settings；整个应用只保留一个启动根。
5. 业务等待根就绪后访问 `Services`；不要在 `Awake` 里直接访问尚未装配的服务。

```csharp
using System.Threading;
using System.Threading.Tasks;
using UPandaGF;

public static class AppEntry
{
    public static async Task StartAsync(UPGameRoot root, CancellationToken token)
    {
        await root.WaitUntilReadyAsync(token);
        await root.Services.Scenes.LoadAsync("Assets/Scenes/Main.unity", token: token);
    }
}
```

[启动文档](../Runtime/Composition/GameRoot/README.md) · [服务文档](../Runtime/Core/Services/README.md)

### Q0.2 遇到问题时先检查什么？

按顺序检查这四件事，能排除大部分接入问题：

1. **根是否就绪**：`UPGameRoot.State == StartupState.Ready`；`Services` 在未就绪时会抛异常。
2. **程序集是否引用**：使用 `UIManager`/`BasePanel` 的 asmdef 是否直接引用了 `UPandaGF.UI`，而不是只引用 `UPandaGF.Runtime`。
3. **资源路径与模式是否匹配**：Editor、NoAssetBundle、AssetBundle 的路径规则不同。
4. **是否在主线程**：Unity 对象、场景、UI 和资源服务都在主线程调用。

如果仍然失败，用 Q9 的排障表定位，并附上根状态、日志、资源模式、平台和复现步骤。

---

## 1. 安装、程序集与命名空间

### Q1.1 UPM 和源码拷贝怎么选？

- **UPM（推荐）**：Git URL 或本地 `file:` 安装；升级方便，包依赖由 Package Manager 解析。
- **源码拷贝**：把整个目录和 `.meta` 复制到 `Assets/upanda-framework`；便于直接改框架，但要自己维护升级和 `.meta`。

两种方式都不要只复制部分目录，否则脚本 GUID 和资源引用可能丢失。 [安装说明](../README.md)

### Q1.2 为什么框架中的程序集大多没有勾选 Auto Referenced？

`autoReferenced` 只影响 Unity 的预定义程序集（`Assembly-CSharp`、`Assembly-CSharp-Editor`）是否自动引用该 asmdef；它不会让所有 asmdef 自动互相可见。

当前设计：

| 自动引用 | 不自动引用 |
|---|---|
| `Contracts`、`Diagnostics`、`Runtime`、`Editor`、`Visuals` | `Core`、`Assets`、`UI`、`Audio`、`Save`、`Gameplay*`、`Samples*`、`Tests*` |

原因：

- 0.5.0 起按模块拆程序集，业务只引用真正使用的模块。
- 避免所有默认脚本被动依赖 UI、TMP、Assets、Gameplay 等模块。
- `Samples`/`Tests` 必须保持关闭，避免进入 Player。
- `Runtime` 是组合根，自动引用只保证默认程序集能看到组合根，不保证所有模块类型都可直接使用。

如果业务脚本在 `Assembly-CSharp` 中直接使用 `UIManager`/`BasePanel`，需要给业务建 asmdef 并引用 `UPandaGF.UI`（以及实际用到的 `Core`/`Contracts` 等）；单体工程也可以把所需模块的 `autoReferenced` 改为 `true`，但会失去模块隔离。 [迁移说明](MIGRATION-0.5.md) · [UI 程序集](../Runtime/UI/UPandaGF.UI.asmdef)

### Q1.3 `.meta` 必须保留吗？

**源码拷贝时必须保留**。Unity 用 `.meta` 中的 GUID 维持脚本、预制体、场景和材质引用；丢失后会出现 Missing Script 或引用错位。 UPM 安装由包管理和 Library 维护，不需要手工复制 `.meta`。

### Q1.4 命名空间是什么？

框架运行时统一在 `UPandaGF` 空间下。编辑器工具可能在 `UPandaGF.EditorTools`、`UPandaGF.GFEditor` 等命名空间；使用 `using UPandaGF;` 后再按模块引用具体工具。

### Q1.5 为什么 `using UPandaGF` 后还是找不到类型？

通常不是命名空间问题，而是**程序集引用**问题：

- `UIManager`、`BasePanel`、`UIBinder`：asmdef 需要 `UPandaGF.UI`。
- `IGameServices`、`IAssetService`：需要 `UPandaGF.Contracts`。
- `GameServices`、`UPGameRoot`：需要 `UPandaGF.Runtime`，它引用全部运行时模块。
- 只引用 `UPandaGF.Contracts` 只能使用接口，不能使用具体实现。

Unity 的 asmdef 引用不要依赖“传递引用”；**直接使用哪个模块，就显式引用哪个程序集**。

### Q1.6 框架依赖哪些 Unity 包？

`package.json` 声明 TextMeshPro、Input System 等依赖。若项目是源码拷贝而不是 UPM，需自行确认这些包已安装；UPM 安装会自动解析。

---

## 2. 启动与生命周期

### Q2.1 `WaitUntilReadyAsync` 和 `InitializeAsync` 有什么区别？

两者使用同一次启动任务；业务通常等待 `WaitUntilReadyAsync`。`InitializeAsync` 可供启动流程自己驱动初始化。不要同时创建两套启动流程。 [GameRoot 文档](../Runtime/Composition/GameRoot/README.md)

### Q2.2 应该用 `GFLoadedEvent` 还是 `await WaitUntilReadyAsync`？

- 旧业务在 `Awake` 订阅 `GFLoadedEvent`/`GFLoadedFailedEvent`，仍在销毁时退订。
- 新业务直接 `await root.WaitUntilReadyAsync(token)`，用 try/catch 处理失败，不要只等事件。

### Q2.3 可以放多个 `UPGameRoot` 吗？

不建议。一个应用只保留一个启动根；根会跨场景保留，重复根会造成服务重复创建、日志/资源/UI 多重初始化。重建根时先等待旧根 `ShutdownAsync()` 完成。

### Q2.4 关闭顺序是什么？

`GameServices` 按依赖逆序释放；UI 在根关闭流程中调用 `Shutdown()`，先 `OnClose` 再销毁面板。依赖清理完成的业务应显式等待 `ShutdownAsync()`，不要只依赖 `OnDestroy` 的异步收尾。 [服务关闭顺序](../Runtime/Core/Services/README.md)

### Q2.5 为什么 `Services` 访问报“请先等待框架就绪”？

`UPGameRoot.Services` 只在 `State == Ready` 时可用。先 `await WaitUntilReadyAsync`；运行时创建的业务对象还需要 `services.Inject(gameObject)` 补注入。

### Q2.6 启动配置从哪里来？

- Editor Play：当前根 Inspector；需要磁盘值时先从 Json 读取。
- Player：优先 `StreamingAssets/Data/GameRootConfig.json`，缺失/解析失败回退场景序列化配置。
- 保存场景/构建前：编辑器工具把根配置写入 JSON。

运行中修改配置请重建根；`Config` 返回的是防御性副本，适合诊断，不适合每帧读取。

### Q2.7 场景里已有组件什么时候注入服务？

根就绪/场景加载后扫描 `IGameServicesConsumer`/`IUIServiceConsumer`。运行时实例化的对象不会自动扫描，需调用：

```csharp
services.Inject(gameObject); // 同时注入两类消费者
```

### Q2.8 可以增加自定义启动步骤吗？

可以，通过 `UPGameRoot.ConfigureLifecycle`/`IStartupModule` 注册；步骤在启动任务内部运行，不能在里面再次 `await WaitUntilReadyAsync()`，否则会等待自身。 [启动步骤](../Runtime/Core/StartupLifecycle.cs)

---

## 3. 服务与依赖注入

### Q3.1 `IGameServices` 提供哪些能力？

`Assets`、`Audio`、`Saves`、`Scenes`、`Input`、`Pause`、`Clock`、`Timer`、`Config`、`Flow`、`AppLifecycle`、`Http`、`Pools`、`Log`。业务依赖接口，不依赖具体管理器；诊断/高级 API 再使用具体类型。 [服务契约](../Runtime/Contracts/Services.cs)

### Q3.2 组件如何拿到服务？

实现 `IGameServicesConsumer`：

```csharp
public sealed class InventoryView : MonoBehaviour, IGameServicesConsumer
{
    private IGameServices services;
    public void Configure(IGameServices value) { services = value; }
}
```

需要 UI 服务的组件实现 `IUIServiceConsumer`。场景对象的注入由根完成；运行时对象的注入见 Q2.7。

### Q3.3 为什么不建议再用静态单例？

`UIManager`、`AudioMgr`、`QuestManager` 等已经收口到服务容器；静态单例会让测试、场景切换和作用域释放难以隔离。旧静态入口若仍存在会被标注 `[Obsolete]`，新代码不要新增静态单例。

### Q3.4 场景作用域怎么用？

`services.Scenes.GetScope(scene)` 取得场景作用域；把资源租约、事件订阅、暂停/输入凭据登记进去，场景卸载时按“先取消、再逆序释放”回收。不要提前 Dispose 仍被使用的共享作用域。

### Q3.5 如何写服务测试？

实现接口替身，通过构造/`Configure` 注入；UI、资源、场景测试在 `Tests` 下有 fake 模式可参考。不要依赖真实 AB、设备或全局单例。

---

## 4. 资源、AssetBundle 与热更新

### Q4.1 新代码如何加载资源？

使用 `IAssetService.AcquireAsync<T>`，返回值是资源租约，必须 `Dispose`：

```csharp
using (AssetLease<GameObject> lease = await services.Assets.AcquireAsync<GameObject>(
           "UI/InventoryPanel", AssetLoadMethod.Resources))
{
    if (lease == null || lease.Asset == null) return;
    // 使用 lease.Asset
}
```

`AssetLease` 是共享持有凭据；最后一个持有者释放时才触发底层释放。业务不应再调用底层卸载接口。 [资源所有权](../Runtime/Assets/AssetsMgr/README.md)

### Q4.2 三种资源模式路径怎么填？

| 模式 | 路径 |
|---|---|
| `Editor` | `Assets/...` 工程路径，走 AssetDatabase |
| `NoAssetBundle` | `Resources` 下的相对路径，不写扩展名 |
| `AssetBundle` | 清单登记的完整 `Assets/...` 路径，含扩展名 |

Player 不接受 `Editor` 模式；AB 模式需要主包、清单与目标平台一致。 [资源入口](../Runtime/Assets/SourcesLoadMgr/README.md)

### Q4.3 为什么资源加载成功但资源引用一直不释放？

检查是否每个 `AssetLease<T>` 都 Dispose；`ResourcesLoader` 的旧引用计数入口与 `IAssetService` 租约是两套语义，不要混用。取消等待后 `AssetService` 仍会归还迟到租约，不需要业务再处理底层缓存。

### Q4.4 场景怎么加载/卸载？

统一走 `ISceneService`：

```csharp
Scene scene = await services.Scenes.LoadAsync(
    "Assets/Scenes/Workshop.unity", LoadSceneMode.Additive, token: token);
await services.Scenes.UnloadAsync(scene, token);
```

场景加载/卸载串行队列；取消/超时后原生加载仍可能完成。不要直接调用 `SceneManager` 绕过作用域和队列。 [场景服务](../Runtime/Core/Services/README.md)

### Q4.5 何时使用 `IAssetsLoader` 而不是 `IAssetService`？

新业务优先 `IAssetService`；`IAssetsLoader` 是兼容入口（旧回调、旧 AB 语义、程序集加载）。需要资源所有权时用租约，不要用旧回调入口管理新资源生命周期。

### Q4.6 AB 构建的最小步骤是什么？

1. 选择目标平台与 `bundleTarget`。
2. 通过 `UPandaGF` 的 AssetBundle 工具构建资源包、主包和 `assetData.assetref`。
3. 把产物放到与 `LoadAssetPath` 一致的位置（编辑器/Player）。
4. 在目标 Player 验证首场景和至少一个资源加载，不要只验证 Editor。

[AB 打包文档](../Editor/AssetBundleTools/README.md)

### Q4.7 内容热更和代码热更是一回事吗？

不是：

- **内容热更**：AB/远端清单/下载/校验/回退，`enableAssetUpdate` 控制。
- **代码热更**：`IAssetsLoader.LoadAssemblyAsync` 加载 DLL 字节，依赖 HybridCLR 和 AOT 元数据。

两者的资源准备、失败点和验收不同；不要用内容热更流程替代程序集加载。 [热更文档](../Runtime/Assets/HotUpdate/README.md)

### Q4.8 热更新程序集如何加载？

`GameLaunchExample` 的启动流程提供了示例：等待根就绪 → 可选加载 `HybridCLRScriptsList` → `onAssemblyLoaded` → 首场景。加载成功后应把热更面板/程序集注册给 UI（见 Q5.17）。HybridCLR 的 AOT 元数据和泛型注册由宿主配置，框架不替宿主完成。

### Q4.9 加载失败/取消后应该返回什么？

异步入口通常返回 `null` 或抛异常（取决于具体 API）；UI 打开失败返回 null，旧回调入口失败回调 null。对空结果要判空并展示失败，不要继续使用半初始化对象。

---

## 5. UI 框架

### Q5.1 最小面板需要什么？

1. 预制体根节点是 `RectTransform`。
2. 面板类继承 `BasePanel`，实现 `OnOpen`/`OnClose`。
3. 用 `[UILoadInfo(layer, method, path)]` 声明层级、加载方式和路径。
4. 通过 `IUIService.OpenAsync<T>` 或 `ShowPanelAsync<T>` 打开。

```csharp
[UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/InventoryPanel",
    pool = true)]
public sealed class InventoryPanel : BasePanel
{
    public override void OnOpen(object arg = null) { }
    public override void OnClose() { }
}
```

[UI 文档](../Runtime/UI/UIMgr/README.md)

### Q5.2 面板打不开，按什么顺序排查？

1. 类上是否有 `UILoadInfo`，`loadPath` 是否与预制体实际路径一致。
2. 加载模式与路径是否匹配（Resources 不带扩展名；AB 用清单全路径）。
3. Canvas 是否有对应层节点；自定义 Canvas 缺层会在 `ResetCanvas`/打开时补建，但已有面板时切换 Canvas 会被拒绝。
4. 根是否 Ready，`IUIService` 是否注入到调用方。
5. 是否在主线程；是否被同一路径的加载中请求/类型冲突拒绝。
6. 查日志中的 `UIManager` 错误和 `FrameworkDiagnostics.Capture().UI`。

### Q5.3 `OpenAsync<T>`、`ShowPanelAsync<T>`、`ShowPanel<T>` 怎么选？

- **新代码优先 `OpenAsync<T>`**：返回 `UIPanelHandle<T>`，可查询状态、按句柄关闭、避免旧句柄误关新实例。
- `ShowPanelAsync<T>`：兼容入口，返回面板或 null。
- `ShowPanel<T>(arg, callback)`：最旧的回调入口，保留兼容；新代码不要用。

热更新代码优先使用非泛型 `OpenAsync(Type)`/`OpenAsync(loadPath)`，避免 AOT 泛型实例化限制。

### Q5.4 同一个面板再次打开会怎样？

默认**置顶并调用 `OnRefresh(arg)`**，不会再次执行 `OnOpen`；这是为了避免重复订阅。需要在打开时重新初始化，应在业务层显式关闭再打开，或重写 `OnRefresh`。

### Q5.5 加载中关闭面板会怎样？

请求被取消，等待者得到 null；底层加载的迟到租约仍会归还。若同一路径立刻重开，会创建新的代次，旧句柄不会影响新面板。

### Q5.6 面板生命周期顺序是什么？

典型顺序：

```text
Awake/OnAwake（控件缓存，只一次）
→ OnOpen（每次打开）
→ OnPause/OnResume（被模态面板遮挡/恢复）
→ OnClose（每次关闭）
→ 事件作用域释放 / 销毁或回收池
```

池化面板保留 `Events` 控件绑定，每次关闭释放 `OpenEvents`；业务订阅应放进 `OpenEvents`。

### Q5.7 五个 UI 层是什么？

`Background`、`Main`、`Popup`、`Guide`、`Toast`。旧名 `Bot/Mid/Top/System` 与新名同值兼容。同层按 `order` + 打开顺序排序；`UILoadInfo.order` 越大越靠上。

### Q5.8 模态、暂停、输入上下文和返回键怎么配合？

- `modal = true`：遮挡下层，下层收到 `OnPause`，CanvasGroup 不可交互/不拦截射线；关闭后 `OnResume`。
- `pauseGame = true`：打开时申请 `IPauseService` 暂停凭据，关闭释放。
- 模态面板会压入 `UI` 输入上下文，关闭释放；业务自己申请的上下文/暂停要成对释放。
- `HandleBack()` 关闭栈顶可关闭面板；顶层面板 `closeOnBack=false` 会拦截，Toast 不参与返回栈。
- 当前没有在 `UIManager.Update` 自动轮询 `Cancel`，由输入层调用 `HandleBack()`，避免和 `PauseMenuController` 争抢 Cancel。

### Q5.9 Toast 怎么用？

Toast 面板必须声明 `E_UI_Layer.Toast`：

```csharp
await ui.ShowToastAsync<ToastPanel>("已保存", 1.5f);
```

内部串行队列，前一个关闭后再打开下一个；duration 后自动关闭。没有预制体时先创建 Toast 面板资源；空结果会返回 null。

### Q5.10 面板池怎么开启？为什么没有命中？

- 在 `UILoadInfo` 上设置 `pool = true`；默认不开启。
- `ui.MaxPooledPerPath` 必须大于 0（默认 2）。
- 同一路径的面板类型必须一致；外部销毁、`ResetForHotUpdate`、`Shutdown` 会清空池。
- 池实例保留预制体租约，池会占内存；不要对所有低频面板盲目开启。

### Q5.11 自定义 Canvas 怎么接？

实现 `UICanvasRegister` 或调用 `ui.ResetCanvas(canvas)`：
- Canvas 需要 `RectTransform`；缺少层节点会自动补层。
- 已有面板或加载中请求时 `ResetCanvas` 会拒绝切换，先关闭面板。
- 自定义 Canvas 的渲染模式/相机由项目配置；EventSystem 缺失时框架会创建。

### Q5.12 为什么 New Input System 下 UI 点不动？

- 检查 EventSystem 的输入模块：New Input System-only 应有 `InputSystemUIInputModule`；旧 Input Manager 用 `StandaloneInputModule`。
- 框架会按当前 Input Handling 选择默认模块，但已有 EventSystem 上的错误模块不会无条件强改。
- 确认 `GraphicRaycaster`、Canvas 相机、`EventSystem` 唯一且激活。
- 修改 Active Input Handling 后按 Unity 要求重启编辑器。

### Q5.13 `GetControl<T>` 找不到控件怎么办？

- 控件名字必须与 GameObject 名称一致，建议使用唯一稳定名称。
- `BasePanel` 只缓存 `UIBehaviour` 子类；`GetControl<T>` 的类型必须匹配。
- 通用名（`Label`/`Fill`/`Image` 等）有重复限制；业务控件不要用这些通用名。
- 嵌套 `BasePanel` 的子树不会登记到父面板。
- 需要不打印错误日志时用 `TryGetControl`。

### Q5.14 事件为什么在面板关闭后还在触发？

- 不要直接 `AddListener` 后不释放；使用 `Events`/`OpenEvents`：
  ```csharp
  OpenEvents.Listen(button.onClick, OnClick);
  OpenEvents.Subscribe(bus, listener);
  ```
- `OpenEvents` 在关闭时释放；`Events` 在销毁/池最终清理时释放。
- 关闭后才完成的异步回调要自行判断 `this != null` 和面板状态。

### Q5.15 数据绑定怎么用？

```csharp
StringObservable title = new StringObservable("Settings");
UIBinder.AddTo(OpenEvents, UIBinder.BindText(titleLabel, title));

BoolObservable muted = new BoolObservable(false);
UIBinder.AddTo(OpenEvents, UIBinder.BindToggle(muteToggle, muted));
```

TMP 使用 `TMPUIBinder`。绑定只订阅变化，不轮询；Toggle/Slider/Dropdown 双向且防止回环。 [绑定文档](../Runtime/UI/UIMgr/README.md)

### Q5.16 安全区、缩放和手柄焦点呢？

- `UISafeArea` 已提供安全区适配。
- CanvasScaler 的参考分辨率/匹配模式当前由项目 Canvas 配置；设置面板若要动态缩放，需要在宿主侧统一 CanvasScaler。
- `UIFocusNavigator` 尚未内置；手柄焦点/返回/滑块微调需要宿主接入 EventSystem 和 Input System。
- 这两项是 Phase 4 剩余工作，不要假定框架已自动处理。

### Q5.17 热更新面板怎么接？

1. 面板类放热更程序集；预制体推荐只放 AOT 视觉节点，由 UIManager 用 Type 添加面板组件。
2. 程序集加载成功后调用 `ui.RegisterPanels(assembly)` 或 `RegisterPanel(type)`。
3. 热更代码使用 `OpenAsync(Type)`、`OpenAsync(loadPath)`、`ClosePanel(Type)`、`ShowToastAsync(Type)`；不要依赖泛型 `OpenAsync<T>` 的 AOT 实例化。
4. 卸载/重载前 `ui.ResetForHotUpdate()`，关闭旧面板并清空类型/池缓存；不要继续使用旧句柄或旧 Type。

### Q5.18 如何查看 UI 诊断？

```csharp
UIDiagnosticsSnapshot ui = uiManager.CaptureDiagnostics();
// 或
FrameworkDiagnosticsSnapshot all = FrameworkDiagnostics.Capture(UPGameRoot.Instance);
UIDiagnosticsSnapshot ui2 = all.UI;
```

包含面板状态、层级、排序、加载/打开耗时、池命中/实例统计；日志控制台诊断页也会展示。

### Q5.19 外部销毁面板会怎样？

管理器会移除记录、归还资源租约并补调一次 `OnClose`；如果业务仍持有句柄，句柄状态变为 Closed。不要在 `OnClose` 里再假设对象一定存活。

### Q5.20 面板被其他 Canvas 盖住怎么办？

检查：
- 多个 Canvas 的 `sortingOrder`。
- `UIManager` 默认 Canvas 的 `sortingOrder` 与项目其他 Canvas 的关系。
- 层节点顺序和同层 `order`。
- 模态/Toast 是否在更高层。

### Q5.21 框架有内置设置面板吗？

没有完整的 `ISettingsService`/设置面板实现。`系统设置功能设计.md` 是独立设计；UI 层只提供面板、层栈、暂停/输入、Toast、数据绑定和安全区等接缝。设置存储/Applier/预览回滚需要宿主或后续设置模块实现。

---

## 6. 对象池、音频、存档、配置、输入与计时

### Q6.1 框架有两套对象池吗？

- **通用 GameObject 池**：`services.Pools`，适用于任何 prefab。
- **UI 面板池**：`UILoadInfo.pool = true`，只缓存面板实例和其资源租约。

两者生命周期不同；不要用 UI 面板池缓存普通业务对象。

### Q6.2 通用池怎么选作用域？

- 场景拥有的池：`services.Pools.CreatePool(sceneScope)`，场景释放时销毁池和借出实例。
- 跨场景共享：`services.Pools.Shared`，由容器关闭释放；明确调用 `Clear()` 控制回收时机。

### Q6.3 `IPoolable` 要重置什么？

`OnSpawn` 重置刚体速度、粒子、动画、计时器等运行态；`OnDespawn` 做停用前清理。池只恢复 Transform，不替业务重置所有状态。 [对象池文档](../Runtime/Assets/ObjPool/README.md)

### Q6.4 音频怎么播放/暂停？

```csharp
services.Audio.PlayBKMusic("Audio/Menu", fadeDuration: 0.5f);
services.Audio.PlayUniqueSound("Audio/Confirm");
```

暂停 BGM/独占音效、循环音效句柄等高级入口需要 `(AudioMgr)services.Audio`；全局 `AudioListener.pause` 与 BGM 自身暂停是两套状态。 [音频文档](../Runtime/Audio/AudioMgr/README.md)

### Q6.5 存档怎么用？

`services.Saves.Save/Load/Exists/Delete`，槽名是纯文件名不带扩展名；数据放在 `persistentDataPath/Saves`。主文件损坏才回退 `.bak`，IO/权限错误直接抛出。 [存档文档](../Runtime/Save/StorageDataMgr/README.md)

### Q6.6 WebGL 存档写不进去？

`ISaveService.Save` 是同步写入 Unity 虚拟文件系统，浏览器 IndexedDB 提交要另等 `PlatformStorage.FlushAsync()`；关键节点应显式等待。 [平台存储](../Runtime/Contracts/PlatformStorage.cs)

### Q6.7 设置、配置、存档有什么区别？

- **开发配置**：`UPGameRootConfig`/`IConfigService`，发布配置，玩家不可见。
- **玩家设置**：应独立存储、跨存档保留；当前框架没有完整设置服务。
- **游戏存档**：按槽位保存进度，删除存档不应删除玩家设置。

### Q6.8 输入上下文怎么用？

```csharp
using (services.Input.PushContext("UI"))
{
    // 只处理 UI 上下文，Gameplay 被阻断
}
```

上下文用 `IDisposable` 成对释放；暂停也会屏蔽 Gameplay 动作。 [输入与暂停](../Runtime/Core/Services/InputAndTime.cs)

### Q6.9 暂停为什么会泄漏？

`IPauseService.Pause` 返回可嵌套凭据，必须 Dispose；`PauseMenuController` 在 `OnDisable` 也兜底关闭。切换场景/销毁菜单时不要只隐藏 UI 而不释放凭据，否则 `timeScale` 可能一直为 0。

### Q6.10 计时器受暂停影响吗？

默认 `scaled: true` 使用游戏时钟，暂停会停；需要真实时间用 `scaled: false`。`Delay(0)` 在下一次 Tick 执行，回调异常会隔离。 [计时器](../Runtime/Core/Services/README.md)

### Q6.11 `EventCenter` 和 `EventBus` 怎么选？

- 全局、`EventArgBase`、需要优先级/owner：`EventCenter`。
- 局部上下文、struct 零 GC、弱引用监听者：`new EventBus()`。
- 两者都要可预期地退订；场景/面板可用 `scope.Subscribe` 或 `UIEventScope`.

### Q6.12 场景作用域和资源租约是什么关系？

作用域可以 `Own` 资源租约、暂停凭据、订阅凭据；Dispose 时先 Cancel 再逆序释放。业务自己 `AcquireAsync` 的租约要自己 Dispose，或通过 `AcquireInScopeAsync` 挂到作用域。 [作用域](../Runtime/Core/Services/LifetimeScope.cs)

---

## 7. 日志、诊断与性能

### Q7.1 日志为什么不输出？

- 普通 `PLogger.Log/LogWarning` 受 `OPEN_PLOG` 编译宏和运行时 `openLog/openWarning` 控制。
- 错误/异常方法始终编译，但仍受配置过滤。
- 需要“未定义 OPEN_PLOG 也可观测”的日志用注入的 `services.Log`，它走非 Conditional 的 `PLogger.Write`。
- 日志控制台订阅 Unity 日志回调，能看到其他组件的 Unity 日志。

[日志文档](../Runtime/Diagnostics/README.md)

### Q7.2 怎么打开日志控制台？

在 `UPGameRoot` Inspector 打开“启动日志窗口”并保存场景，或运行时点击 FPS 按钮/调用 `UPGameRoot.logConsole.Show()`。F12 行为取决于宿主配置与控制器激活状态。

### Q7.3 诊断快照包含什么？

`FrameworkDiagnostics.Capture(root)` 包含启动步骤、资源、AB、场景作用域、UI 面板计数；现在还包括 `UI` 面板状态表、加载/打开耗时和 UI 池统计。

### Q7.4 UI 性能排查看什么？

- 是否每帧访问 `UPGameRoot.Config`（每次复制，低频使用）。
- 面板是否重复打开/重复订阅；用 `OnRefresh` 而不是重复 `OnOpen`。
- `BasePanel` 控件缓存是否一次；检查 `GetComponentsInChildren` 是否被业务再次调用。
- 高频面板是否 `pool=true`；池上限/内存是否平衡。
- Canvas 重建：检查是否频繁修改 Graphic/布局；必要时拆分 Canvas。
- 资源租约、事件、暂停/输入上下文是否成对释放。

---

## 8. 测试、构建与发布

### Q8.1 怎么跑测试？

Unity Test Runner 选择 EditMode/PlayMode；完整测试适合隔离宿主，部分 fixture 会创建 AB、改场景或资源。CI 脚本在 `Tools/ci`。 [测试说明](../Tests/README.md)

### Q8.2 当前测试基线是多少？

以 `Framework Validation/VALIDATION.md` 和最新测试 XML 为准，不要在文档里长期写死历史用例数。UI 部分包含 Phase 1/2、热更新、池、事件绑定和数据绑定用例。

### Q8.3 构建 Player 前必须做什么？

确认资源模式、AB 产物、主包/清单、首场景和 `LoadAssetPath` 一致；Editor 直读能跑不代表 Player AB 能跑。使用 `FrameworkBuildPipeline` 或 `Tools/ci/build-player.*`。 [构建文档](../Editor/README.md)

### Q8.4 Android 构建路径为什么失败？

Android Tools 对工程路径有 ASCII 限制；工程路径含中文/空格可能失败。用纯 ASCII 路径构建或使用 CI 脚本。 [构建文档](../Editor/README.md)

### Q8.5 升级后场景出现 Missing Script 怎么办？

保留 `.meta`；按 `MIGRATION-0.5/0.6` 检查程序集、命名空间和日志面板替换。源码拷贝升级不要直接覆盖整个 `Assets` 目录，先备份场景和预制体引用。

### Q8.6 UPM Git URL 安装后编译失败？

检查：
- Unity 版本是否满足 2021.3；
- TextMeshPro/Input System 依赖是否解析；
- 业务 asmdef 是否引用实际使用的模块；
- 包内 `.meta` 是否完整（源码拷贝时）。

---

## 9. 常见错误速查

| 现象 | 常见原因 | 处理 |
|---|---|---|
| `namespace UPandaGF` 找不到 | asmdef 没有引用框架程序集 | 业务 asmdef 加 `UPandaGF.UI`/`Core`/`Contracts` |
| `UIManager` 为 null | IUIService 未注入、根未就绪 | 等 Ready、实现 `IUIServiceConsumer`、运行时对象 `Inject` |
| 面板不出现 | 路径/模式不匹配、缺 `UILoadInfo`、层不存在 | 查 UI 日志、检查资源与 Canvas 层 |
| `UI 层级不存在` | 自定义 Canvas 缺层节点 | 让 `ResetCanvas` 补层或手动建五层 |
| `ResetCanvas` 报“已有面板” | 面板/加载中请求存在 | 先 `CloseAll` 再切换 |
| UI 点不动 | EventSystem/输入模块/GraphicRaycaster/相机 | 检查 Q5.12 |
| 第二次 `OnOpen` 没执行 | 已打开面板走 `OnRefresh` | 重写 `OnRefresh` 或显式关闭再开 |
| 面板池不命中 | `pool=false`、池上限 0、类型不同、被外部销毁 | 检查 `UILoadInfo.pool`、`MaxPooledPerPath`、池诊断 |
| Toast 不显示 | 未声明 Toast 层/预制体路径错/队列前一个未关 | 检查 `UILoadInfo` 与 `ShowToastAsync` 返回值 |
| 关闭后仍收到事件 | 直接 AddListener/订阅未退订 | 用 `OpenEvents`/`Events` 或 scope |
| 暂停后 `timeScale=0` 回不来 | `Pause` 凭据未释放 | 成对 Dispose；检查 `PauseMenuController` |
| 资源引用一直不释放 | 租约未 Dispose/旧接口混用 | 检查 `AssetLease` |
| AB 加载失败 | 目标平台/清单/主包/路径不匹配 | 先 Editor 直读，再用目标 Player 验证 |
| 热更类型找不到 | 程序集未加载/未注册/泛型 AOT 限制 | `LoadAssemblyAsync` → `RegisterPanels` → Type API |
| 日志不输出 | `OPEN_PLOG`/运行时开关关闭 | 使用 `services.Log` 或检查 LogConfig |
| TextMeshPro 类型找不到 | 程序集没引用 TMP | 加 TMP 依赖/引用；UIBinder 与 TMPUIBinder 分开 |
| 升级后 Missing Script | 未保留 `.meta`/程序集改名 | 按迁移文档修复 |

---

## 10. 扩展与二次开发

### Q10.1 新增一个面板的步骤？

1. 建预制体（根 `RectTransform`）并放到 Resources/AB 清单。
2. 写 `BasePanel` 子类 + `[UILoadInfo]`。
3. 需要服务就实现 `IGameServicesConsumer`/`IUIServiceConsumer`。
4. 在业务入口用 `IUIService.OpenAsync` 打开。
5. 用 `Events/OpenEvents` 绑定事件，用 `UIBinder` 绑定数据。
6. 写 PlayMode/手工验证，检查池、模态、返回和关闭路径。

### Q10.2 新增一个服务？

在 Contracts 定义接口，在 Core/具体模块实现，由 `GameServices` 装配；业务通过 `IGameServices` 获取。新增服务要补生命周期、取消/释放、测试替身。不要新增静态单例。

### Q10.3 新增一层 UI？

当前 `E_UI_Layer` 固定五层。新增层需要同时修改枚举、`EnsureLayers`、`layerStacks` 数组、`GetLayerFather`、排序/返回策略和诊断表；属于破坏性变更，先评估是否能用 `order`/现有层表达。

### Q10.4 新增热更程序集？

在 HybridCLR 流程中生成 DLL/bytes、AOT 元数据；启动时 `LoadAssemblyAsync`，成功后 `ui.RegisterPanels(assembly)`；热更代码使用 Type/非泛型 UI API；重载前 `ResetForHotUpdate()`。

### Q10.5 新增数据绑定类型？

优先使用 `Observable<T>` + Binder；热更代码用具体 `Bool/Float/Int/StringObservable` 或新增具体类型，避免 AOT 泛型限制。新增控件绑定放在 `UIBinder`/`TMPUIBinder`，并补 `IDisposable` 释放测试。

---

## 11. 提问模板

提交问题/补 FAQ 时请附：

```text
Unity 版本：
平台（Editor/Windows/Android/iOS/WebGL）：
框架版本/commit：
资源模式（Editor/NoAssetBundle/AssetBundle）：
Input Handling（Old/New/Both）：
复现步骤：
期望结果/实际结果：
关键日志（含 UIManager/资源/热更错误）：
是否只有 Player/真机出现：
```

---

## 12. 维护约定

- 新问题优先补到最接近的章节，不新建重复主题。
- 涉及行为变化时，先更新对应模块 `README`，再在 FAQ 给短答案和链接。
- 不把“临时绕过方案”写成正式推荐；标注版本、平台和限制。
- 安全/隐私/存档损坏类问题必须给出回退或验证步骤，不能只给结论。
