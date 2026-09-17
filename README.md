# UPandaFramework

Unity 的前端（客户端）框架，提供统一的启动入口、单例、事件、资源热更、UI、日志、任务评分、回放、状态机等常用系统，并配套完整的编辑器工具链。

项目地址：

- https://gitee.com/he-jinxian/upanda-framework.git
- https://github.com/UserHandJ/UPandaFramework.git

## 特性一览

- 统一启动入口 `UPGameRoot`，自动挂载并初始化各子系统
- 四种单例基类（急加载 / 懒加载 × MonoBehaviour / 纯 C#）
- 两套事件系统：委托式 `EventCenter`、接口式 `EventBus`（选型与原理见事件模块文档）
- 双模式资源管理（编辑器直读 / AssetBundle），支持本地、远程加载与热更下载
- 可编译期剔除的日志系统，支持本地输出与真机日志面板
- 分层 UI 管理与 Canvas 自动生成
- 玩法系统：任务系统、交互式任务评分（含校验器 / 逻辑自检 / 回放复盘工具）、回放、分层状态机、机械臂 CCD 逆运动学、载具控制
- 内置 Shader 学习文档与效果案例库（流光、描边、卡通、消融、水波等）
- 编辑器工具链：AB 打包、Excel 数据表、UI 自动生成、HybridCLR 热更、事件调试窗口、常用路径快捷打开、模块示例场景一键生成（描边 / 回放 / 对象池 / 载具驾驶 / 评分）、描边材质 Shader 修复，以及编辑器控件速查 / 开发案例窗口

## 快速开始

1. **初始化目录结构**：菜单 `UPandaGF -> Tools -> 初始化项目目录结构`，自动创建 `3rd / ArtAssets / AssetBundles / Plugins / Resources / Scenes / Scripts / StreamingAssets` 等标准目录。

2. **创建启动节点**：菜单 `UPandaGF -> 创建UPGameRoot`（或 `GameObject -> UPandaGF -> 创建UPGameRoot`），生成 `UPGameRoot` 与示例脚本 `GameLaunchExample`。

3. **监听框架加载完成事件，进入游戏逻辑**：

```csharp
using UnityEngine;
using UPandaGF;

public class GameLaunchExample : MonoBehaviour
{
    private void Awake()
    {
        EventCenter.Instance.AddEventListener<GFLoadedEvent>(OnGFLoaded);
    }

    private void OnDestroy()
    {
        EventCenter.Instance.RemoveEventListener<GFLoadedEvent>(OnGFLoaded);
    }

    private void OnGFLoaded(GFLoadedEvent arg)
    {
        PLogger.Log("框架加载完成，进入游戏逻辑");
    }
}
```

> `UPGameRoot` 的 Inspector 面板可配置：资源加载方式（Editor / Assetbundles）、是否启用资源热更、远程地址、AES 加密配置等；面板底部有「保存到 Json」/「从 Json 读取」，配置落地在 `Assets/StreamingAssets/Data/GameRootConfig.json`，**启动时优先读这个 Json**（读不到才用面板值）。

## 示例场景

框架自带的示例都放在各模块的 `Sample/` 目录下，可直接打开运行：

| 模块 | 场景 | 说明 |
|---|---|---|
| 机械臂 IK | `Runtime/Game/RobotCCDIK/Sample/Sample.unity` | 6 轴机械臂，编辑器内拖动 Target 实时求解，可录制 / 回放姿态 |
| 载具控制 | `Runtime/Game/VehicleSystem/Sample/VerticalScene.unity` | Rigidbody + WheelCollider 车辆，`W/S/A/D` 驾驶、`空格` 手刹漂移、`C` 切视角；也可用菜单 `UPandaGF -> Runtime -> VehicleSystem -> 创建驾驶示例场景（新建场景）` 一键重建 |
| 高亮描边 | `Runtime/Game/QuickOutline/Samples/Scenes/QuickOutline.unity` | 描边效果演示（`OutlineMask` / `OutlineFill`）；也可用菜单 `UPandaGF -> Runtime -> QuickOutline -> 创建描边示例场景（新建场景）` 一键重建 |
| 回放系统 | `Runtime/Game/ReplaySystem/Sample/HFScene.unity` | 录制 / 回放演示场景；也可用菜单 `UPandaGF -> Runtime -> ReplaySystem -> 创建回放示例场景（新建场景）` 一键重建 |
| 对象池 | 菜单 `UPandaGF -> Runtime -> ObjPool -> 创建对象池示例场景（新建场景）` 一键生成 | 重建 `ObjPool/Example/PoolUseScene.unity`：`UPGameRoot` 启动设施 + 演示地面 + `TestPoolMgr` 取 / 回收对象 |
| 交互式任务评分 | 菜单 `UPandaGF -> Runtime -> 交互任务评分系统 -> 创建演示场景` 一键生成 | 生成配置资产 + 实体预制体 + 可运行场景（落 `Example/Demo/`）；演示内容见模块文档 |

## 目录结构

```
upanda-framework/
├── Editor/                      编辑器工具（UPandaGF.Editor 程序集）
│   ├── FrameWorkInitEditor.cs   框架初始化（建目录 / 建 UPGameRoot / 清理缺失脚本）
│   ├── AssetBundleTools/        AB 打包工具（基于 AssetBundle Browser 扩展）
│   ├── UIEditor/                UI 自动生成
│   ├── PLogger/                 日志开关与 Reporter 编辑器
│   ├── Excel/                   Excel 数据表生成（ExcelTool，依赖 Libraries/Excel.dll）
│   ├── HyBridCLREditor/         HybridCLR 热更程序集拷贝配置
│   ├── CustomInspector/         自定义 Inspector
│   │   ├── UPGameRootEditor.cs            启动根节点面板
│   │   ├── EventCenterModule/              事件系统工具：事件调试窗口 / 事件模块自检
│   │   └── InteractiveTaskScoringSystem/   评分系统工具：校验器 / 逻辑自检 / 回放复盘 / 演示生成 / 组件 Inspector
│   ├── ObjPoolEditor/           对象池工具：示例场景一键生成
│   ├── QuickOutlineEditor/      高亮描边工具：示例场景一键生成、描边材质 Shader 修复
│   ├── ReplaySystemEditor/      回放系统工具：示例场景一键生成
│   ├── VerticalSceneEditor/     载具驾驶工具：示例场景一键生成
│   ├── EditorTools/             编辑器小工具（资源路径复制、自动碰撞体、常用路径快捷打开）
│   ├── EditorConfig/            编辑器配置
│   ├── EditorStu/               编辑器学习示例（控件速查窗口、开发案例窗口、示例资产与自定义 Inspector）
│   └── UPandaGF.Editor.asmdef   编辑器程序集定义
├── Runtime/                     运行时（UPandaGF.Runtime 程序集）
│   ├── Manager/                 核心管理器层
│   │   ├── GameRoot/            UPGameRoot 启动入口 + UPGameRootConfig（Json 配置）+ SourcesLoadMgr 资源抽象
│   │   ├── Singleton/           单例基类
│   │   ├── DebugSystem/         日志系统（含本目录 README.md）
│   │   ├── EventCenterModule/   事件系统（含本目录 README.md）
│   │   ├── AssetsMgr/           资源管理（Resources / AB / StreamingAssets）
│   │   ├── DownloadMgr/         下载器（含本目录 README.md）
│   │   ├── UIMgr/               UI 管理
│   │   ├── AudioMgr/            音频管理
│   │   ├── SceneMgr/            场景管理
│   │   ├── HTTPTool/            网络请求
│   │   ├── ObjPool/             对象池（含 Example 示例与本目录 README.md）
│   │   ├── StorageDataMgr/      数据存储
│   │   ├── PublicMono/          公共 Mono（协程 / Update 托管）
│   │   └── Extend/              扩展方法
│   ├── Game/                    玩法系统层
│   │   ├── TaskSystem/          任务系统（含本目录 README.md）
│   │   ├── InteractiveTaskScoringSystem/  交互式任务评分系统（含 ReadMe.md）
│   │   │   ├── Core/            纯逻辑层：运行状态 / 进度 / 评分策略 / 难度分级 / 回放日志 / 宿主接口
│   │   │   ├── Data/            任务与步骤配置（ScriptableObject）
│   │   │   ├── Task/            步骤调度（TaskDataManager）与步骤基类（TaskStepBase）
│   │   │   ├── Operation/       操作组与叶子操作（串联 / 并联）
│   │   │   ├── TaskInteractive/ 交互实体与实体注册表
│   │   │   └── Example/         示例实体与 HUD（DemoInteractiveEntity / DemoTaskHud）
│   │   ├── ReplaySystem/        回放系统（含 Sample 示例场景）
│   │   ├── StateMachine/        分层状态机
│   │   ├── QuickOutline/        高亮描边（含 Samples 示例场景）
│   │   ├── SimpleCameraControl/ 相机控制
│   │   ├── Vignette/            隧道暗角效果
│   │   ├── RobotCCDIK/          机械臂 CCD 逆运动学（含 Sample 示例与本目录 README.md）
│   │   ├── VehicleSystem/       载具控制与三视角相机（含 Sample 示例）
│   │   └── Study/               学习示例
│   ├── Resources/               框架内置资源（描边 Shader / 材质 / UI 预制体）
│   ├── Shader/                  Shader 学习文档（含效果案例）与 ShaderLibrary
│   └── UPandaGF.Runtime.asmdef  运行时程序集定义
├── Libraries/                   第三方库（Excel.dll、ICSharpCode.SharpZipLib.dll）
├── README.md
└── LICENSE
```

## 模块介绍

### 启动入口（Manager/GameRoot）

- **UPGameRoot**：框架根节点，`Awake` 时自动挂载并初始化 `DebugerInit`（日志）、`Downloader`（下载）、`AssetsLoader`（资源）、`BinaryDataMgrInit`（存储）、`UIManager`（UI）五个子系统；开启热更时下载远程清单 / 按 MD5 增量下载，**只有全部下载成功才提交新清单**（否则降级：上一次成功的本地清单 → 随包清单），完成后广播 `GFLoadedEvent`；失败则广播 `GFLoadedFailedEvent`（并提供 `IsInited` / `IsInitFailed` / `GetAssetsLoader()` 就绪约定），不会静默卡在加载界面。清单 AES 默认关闭（`config.AssetAESConfig.enable = false`）。配置集中在可序列化对象 `UPGameRootConfig`（`UPGameRoot.Config`）里，**启动第一步先读 `StreamingAssets/Data/GameRootConfig.json`**（面板改完点「保存到 Json」写回；Json 缺失 / 解析失败才降级用面板值）。详见 `Runtime/Manager/GameRoot/README.md`。
- **GameLaunchExample**：进入游戏的示例入口，演示"热更程序集加载 → 场景加载"的完整流程，并内置完整的失败兜底。
- **SourcesLoadMgr**：资源加载抽象层，编辑器下用 `EditorSourcesMgr` 直读资源，出包后切换为 `AssetsLoader` 走 AssetBundle。

#### 启动流程（`GameLaunchExample.OnGFLoadedEvent`）

1. 打开加载界面 `SimpleLoadUI`（`UIManager.ShowPanelAsync`）。
2. 取 `UPGameRoot.Instance.GetAssetsLoader()` 得到 `IAssetsLoader`。
3. 若勾选 `loadHotUpdateScripts`：遍历 `HybridCLRScriptsList` 逐个 `LoadAssemblyAsync`，统计成功数量并提示"程序热更完成 / 失败"，随后触发 `onAssemblyLoaded`（供泛型注册使用）。
4. 校验 `firstScene` 非空后调用 `LoadSceneAsync`，同时监听 `ABLoadProgressEvent` 刷新"加载 / 下载"进度，进入场景后关闭加载界面。

整个流程包在 `try/catch` 中：`UIManager` / `SimpleLoadUI` / `UPGameRoot` / 资源加载器 / 热更列表 / `firstScene` 任一缺失都会打印明确的 `PLogger.LogError` 并中止，不会抛空引用异常。

若框架初始化失败，会收到 `GFLoadedFailedEvent`（而不是一直等 `GFLoadedEvent`）：`GameLaunchExample` 会打错误日志并在加载界面上给出提示。

### 单例系统（Manager/Singleton）

| 基类 | 说明 |
|---|---|
| `EagerMonoSingletonBase<T>` | MonoBehaviour + 急加载，自动创建 GameObject、`DontDestroyOnLoad`、重复实例销毁 |
| `LazyMonoSingletonBase<T>` | MonoBehaviour + 懒加载 |
| `EagerSingletonBase<T>` | 纯 C# + 急加载 |
| `LazySingletonBase<T>` | 纯 C# + 懒加载，带双检锁与 `Release()` |

### 日志系统（Manager/DebugSystem）

- **PLogger**：日志门面 —— `[Conditional("OPEN_PLOG")]` 编译期剔除（关日志零成本、实参不求值）；运行时由 `LogConfig`（`StreamingAssets/Data/LogConfig.json`）控制总开关、Warning/Error 分级、前缀 / 时间 / 线程号与单条截断。
- **PLogHelper**：后台线程把日志落盘；**Reporter / LogListenerManager**：两套真机日志面板（IMGUI / UGUI）。
- 菜单 `UPandaGF -> 日志系统`：启动日志 / 剔除日志 / 日志系统诊断 / 日志自检（19 项断言）。

> 用法、日志链路、面板对比、FAQ 与已知限制：见 `Runtime/Manager/DebugSystem/README.md`。

### 事件系统（Manager/EventCenterModule）

两套实现，按需选择：

| 实现 | 形态 | 适用场景 |
|---|---|---|
| `EventCenter` | 委托式**单例**，参数为 `EventArgBase` 子类 | 跨模块 / 全局通知；支持带 owner 注册（owner 销毁自动注销）、优先级、一次性监听 |
| `EventBus` | 接口式，可 `new` 多实例，消息为 `struct` | 帧内高频（零 GC）、需要多套互不干扰的上下文；监听者用弱引用防泄漏 |

两者均已做到：监听者抛异常不中断其余监听者、派发中增删订阅不重复不漏派发、用户回调在锁外执行。配套工具：菜单 `UPandaGF -> Runtime -> 事件系统 -> 事件调试窗口 / 事件模块自检`。

> 用法示例、内部结构、执行语义、FAQ 与已知限制：见 `Runtime/Manager/EventCenterModule/README.md`。

### 资源管理（Manager/AssetsMgr + SourcesLoadMgr）

1. **AB 包打包工具**
   - 基于官方 `AssetBundle Browser` 扩展为 `Configure / Build Set / Inspect / UpLoadAB` 四个页签：可视化指定包名、单包或含依赖打包、逐包设置加载位置（StreamingAssets / Persistent / Remote）、生成资源清单 `assetData.assetref`（可 AES 加密），并支持 FTP / Nginx 上传。
   - 路径：`UPandaGF -> AB包工具 -> AssetBundle Browser`。

   > 打包 → 清单 → 上传 → 运行时加载 / 热更的完整流程、数据格式与已知限制：见 `Editor/AssetBundleTools/README.md`。

2. **IAssetsLoader**
   - AB 包加载接口，统一了"编辑器直读 / AssetBundle"两种模式。
   - 获取方式：

     ```csharp
     IAssetsLoader loader = UPGameRoot.Instance.GetAssetsLoader();
     ```

   - 接口方法：

     | 方法 | 说明 |
     |---|---|
     | `Task<T> LoadAsync<T>(string path)` | 泛型异步加载 |
     | `void LoadAsync<T>(string path, UnityAction<T> callback)` | 泛型回调式加载 |
     | `Task<Object> LoadAsync(string path, Type type)` | 按类型异步加载 |
     | `void LoadAsync(string path, Type type, UnityAction<Object> callback)` | 按类型回调式加载 |
     | `void LoadSceneAsync(string path, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null)` | 异步加载场景（另有 `LoadSceneMode` 重载） |
     | `void LoadAssemblyAsync(string path, UnityAction<Assembly> callback)` | 加载程序集（回调式） |
     | `Task<Assembly> LoadAssemblyAsync(string path)` | 加载程序集（异步） |
     | `void ClearAB()` | 清空已加载的 AB |

   - 开发过程中直接使用编辑器路径加载资源，`UPGameRoot` 的 Inspector 面板可切换为 Editor 模式；打包时再切换为 AssetBundle 模式。
   - AssetBundle 支持本地加载、远程加载，或资源热更下载到本地后加载。
   - **错误处理约定（调用方必须注意）**：
     - 资源缺失 / AB 初始化失败时，加载器打印错误日志并安全返回，**不抛异常**；异步 `LoadAsync` 返回 `null`、回调式则回调 `null`，调用方需自行判空。
     - `LoadSceneAsync` **没有失败回调**：AB 模式下资源缺失时只会回调 `assetLoadComplete`，`sceneLoadComplete` 不会触发，启动流程需自备兜底。
     - `LoadAssemblyAsync` 可能返回 `null`（内部已对 dll 资源与 `Assembly.Load` 加保护），调用方需判空。

3. **ResourcesLoader**
   - 封装 Unity 内置的 `Resources.Load`，避免资源重复加载，内置引用计数与异步加载状态管理。

4. **StreamingAssetsLoadTool**
   - `StreamingAssets` 路径下的资源加载工具，已处理跨平台兼容性，提供多种加载方式。

5. **AssetBundleMgr**
   - `ABLoadMgr` 负责 AB 的本地 / 远程加载（主包 / manifest / 依赖递归 / 卸载）。
   - 资源热更已由 `UPGameRoot.Config.enableAssetUpdate` + `Downloader` 按清单 MD5 增量完成（详见 AB 工具文档）；`AssetBundelUpdataMgr` 是**已废弃**的旧方案（文本对比文件 + FTP），勿再使用。

6. **FileUtility**
   - 文件工具：另存 / 移动（先复制后删除，可指定是否覆盖）等常用文件操作。

### 下载器（Manager/DownloadMgr）

- **Downloader**：下载队列 + 并发 + 超时重试，提供 `async/await` 接口与暂停 / 继续 / 取消；**DownloadHandlerFile** 为落盘下载句柄（延迟打开文件、兼容分块传输）。
- 断点续传默认关闭：增量清单按 MD5 对比生成，本地文件可能是“完整但过期”的旧版本，追加写入会损坏文件。
- 实现原理、API 参考与修复记录见 `Runtime/Manager/DownloadMgr/README.md`。

### UI 系统（Manager/UIMgr）

- **UIManager**：按 `Bot / Mid / Top / System` 四层管理面板，自动创建 `UICamera`、`Canvas`（1920×1080 适配）与 `EventSystem`。
- **BasePanel**：面板基类；**SimpleLoadUI**：加载界面；**WorldSpaceOverlayUI**：世界空间 UI；**DragArea**：拖拽组件。
- **UICameraRegister / UICanvasRegister**：把场景中的相机 / Canvas 注册给 UI 系统（`Reset()` 时自动填充引用）。
- **UILoadInfoAttribute**（`UIAttribute`）：标记面板所属层级与资源加载方式，供自动加载使用。
- **UIAutoGenerator**（Editor）：UI 自动生成工具。

### 音频（Manager/AudioMgr）

- 背景音乐支持**双路交叉淡入淡出**（切歌不硬切）、暂停 / 恢复 / 淡出停止与音量调节；唯一音效（新音效加载完成后才顶掉上一个，失败不打断）；叠加音效播完自动回收；支持 Resources 与 AssetBundle 两种加载方式，并在换曲 / 回收时**归还 `ResourcesLoader` 的引用计数**（避免 `AudioClip` 常驻内存）。用法与限制见 `Runtime/Manager/AudioMgr/README.md`。

### 场景管理（Manager/SceneMgr）

- 同步 / 异步场景加载，支持 `Single` 与 `Additive` 模式，异步加载进度通过 `SceneMgr_SceneAsynLoadProgress` 事件广播。

### 网络请求（Manager/HTTPTool）

- **HttpManager**：基于 `UnityWebRequest` 的 HTTP 封装：`GET / POST / PUT / DELETE`、全局请求头、泛型 JSON 反序列化（`JsonUtility`）、带状态码回调、失败重试（含重试间隔）、下载进度回调、请求取消与并发控制；按需懒加载单例（不在 `UPGameRoot` 的初始化列表中）。
- 模块内暂无调用点；已修复原实现的"重试空引用导致请求队列死锁""全局请求头未接入"等问题，API 说明、用法示例与修复记录见 `Runtime/Manager/HTTPTool/README.md`。

### 对象池（Manager/ObjPool）

- **GameObjectPoolMgr**：GameObject 对象池，按资源路径分容器缓存（LIFO），池空时自动加载并实例化；支持 Resources / AssetBundle 两种加载方式、Task 与回调两种异步形式、层级收纳、单容器容量上限、预热（`Prewarm`）与 `IPoolable` 出池 / 回池回调；同步接口仅支持 Resources（已标记过时）。
- 用法、配置项与注意事项见 `Runtime/Manager/ObjPool/README.md`；示例场景（`Example/PoolUseScene.unity`）可用菜单 `UPandaGF -> Runtime -> ObjPool -> 创建对象池示例场景（新建场景）` 一键重建（默认以 `Editor` 加载方式生成，便于直接在编辑器里运行）。

### 数据存储（Manager/StorageDataMgr）

- **PlayerPrefsDataMgr**：基于 PlayerPrefs 的轻量存储。
- **BinaryDataMgr**：二进制序列化存储。

### 公共 Mono（Manager/PublicMono）

- **PublicMono**：帧调度中心 —— 非 MonoBehaviour 对象可借此托管 `Update / FixedUpdate / LateUpdate` 与协程（`RunCoroutine` 返回可 `await` 的 `Task`）。帧回调逐个异常隔离（一个监听器出错不会连累其它模块）；监听器支持**带 owner 注册**（Unity 对象销毁后自动注销，杜绝僵尸监听者）、重复注册拦截、`ClearAllListeners()` 批量清理；`RunCoroutine` 的 `Task` 保证会结束（完成 / 异常 / 取消 / 超时）。详见 `Runtime/Manager/PublicMono/README.md`。

### 玩法系统（Runtime/Game）

| 模块 | 说明 |
|---|---|
| `TaskSystem` | 通用任务系统：ScriptableObject 模板 + 运行时实例、四类目标（击杀 / 收集 / 到达 / 对话）、事件驱动进度（无轮询）、任务链前置、可重复任务、JSON 存档、UGUI 任务日志与 HUD、编辑器一键搭建；详见该目录 `README.md` |
| `InteractiveTaskScoringSystem` | 交互式任务评分系统：步骤 / 操作组 / 评分 / 回放，面向教学实训场景；详见该目录 `ReadMe.md` |
| `ReplaySystem` | 回放系统：按帧录制 Transform（可扩展），序列化 + 压缩存储到本地；`ReplayUI` 提供回放界面；详见该目录 `README.md` |
| `StateMachine` | 分层状态机：状态注册、状态栈、路径切换（如 `Movement/Run`）；详见该目录 `README.md` |
| `RobotCCDIK` | 串联机械臂 CCD 逆运动学：位置 / 姿态双目标、关节限位、姿态录制回放；原理与用法见该目录 `README.md` |
| `VehicleSystem` | 载具控制：`VehicleController`（Rigidbody + WheelCollider，前轮转向 / 后轮驱动 / 手刹漂移）、`VehicleInput`（键盘输入）、`CameraModeManager`（第三人称 / 第一人称 / 环绕三视角） |
| `QuickOutline` | 物体高亮描边（配套 `OutlineMask` / `OutlineFill` Shader 与材质，示例场景 `QuickOutline/Samples/Scenes/QuickOutline.unity`） |
| `SimpleCameraControl` | 简单相机控制 |
| `Vignette` | 隧道暗角后处理效果（`TunnelingVignette`） |
| `Study` | 学习示例（`SortStu` 等）；详见该目录 `README.md` |

#### 交互式任务评分系统（InteractiveTaskScoringSystem）

把“考核流程”拆成若干**步骤**，每步由一个或多个**操作**（串联 / 并联）组成；只允许点击当前步骤激活的交互物，点错计一次错误，完成后按错误次数 / 耗时结算分数。

- **核心能力**：纯逻辑层 `Core/`（不依赖 `UnityEngine`，可单测）、配置与运行状态分离（可重玩 / 多实例 / 存档）、可插拔评分策略（难度分级 + 超时扣分）、动作级回放日志。
- **编辑器工具**（菜单 `UPandaGF -> Runtime -> 交互任务评分系统`）：任务流程校验器 / 运行逻辑自检 / 回放复盘查看器 / 一键生成演示场景。
- **演示快捷键**：`G` 引导、`K` 跳过当前步骤、`S` 保存回放日志。

> 架构与调用链、评分规则、回放与工具细节、扩展指南、已知限制与 FAQ：见 `Runtime/Game/InteractiveTaskScoringSystem/ReadMe.md`。

#### 机械臂逆运动学（RobotCCDIK）

`CCDIKController` 提供串联机械臂 IK（位置 + 姿态双目标、关节单轴行程限位、平滑插值、复位与调试绘制）；`RobotAngleRecoder` 可在编辑器内拖动 Target 实时驱动 IK 并录制 / 回放关节姿态。示例场景 `Runtime/Game/RobotCCDIK/Sample/Sample.unity`。

> 原理、角度约定、参数调优与常见问题：见 `Runtime/Game/RobotCCDIK/README.md`。

### Shader 学习资料（Runtime/Shader）

框架内置一套 Shader 学习文档与效果案例库，适合学习内置（Built-in）渲染管线的 Shader 编写：

- **UnityShader学习文档**：`Shader基础.md` 从零讲解 ShaderLab 结构、数据类型与语义、坐标空间、光照模型、纹理、混合、深度 / 模板测试等完整知识树，附图文与可运行代码。
- **效果案例库**：流光、纹理动画、模型描边、卡通风格、噪声、水波、消融、物体切割、素描风格、翻页、遮挡半透明等 11 个可运行案例。
- **ShaderLibrary**：内置渲染管线常用 Shader 库（`Library/BuildIn`）。

### 编辑器工具链（Editor）

> 菜单分组约定：**模块级工具**统一放在 `UPandaGF -> Runtime -> <模块名>`（交互任务评分系统 / 事件系统 / QuickOutline / ReplaySystem / ObjPool / VehicleSystem）；**框架级工具**在 `UPandaGF -> Tools / 日志系统 / AB包工具` 以及 `UPandaGF -> 创建UPGameRoot`。

- **FrameWorkInitEditor**：一键初始化目录结构、创建 `UPGameRoot`、清理 Prefab 缺失脚本。
- **AssetBundleTools**：AB 打包、管理与上传（FTP / Nginx），详见 `Editor/AssetBundleTools/README.md`。
- **ExcelTool**（`Editor/Excel/`）：根据 Excel 配置生成数据类与数据容器类，依赖 `Libraries/Excel.dll`。
- **UIAutoGenerator**：UI 自动生成。
- **HyBridCLREditor**：HybridCLR 热更程序集拷贝配置（`DLLCopyConfigWindow`）。
- **CustomInspector**：
  - `UPGameRootEditor`：启动根节点面板（资源加载方式、热更开关、远程地址、AES 配置等 + 配置文件的「保存到 Json」/「从 Json 读取」）。
  - `EventCenterModule/`：事件调试窗口 + 事件模块自检（菜单 `UPandaGF/Runtime/事件系统/…`），用法见事件模块文档。
  - `InteractiveTaskScoringSystem/`：任务流程校验器 / 运行逻辑自检 / 回放复盘查看器 / 演示场景生成器 + 各组件自定义 Inspector（菜单 `UPandaGF/Runtime/交互任务评分系统/…`），用法见评分系统文档。
- **模块示例场景生成器**（菜单 `UPandaGF/Runtime/<模块>/…`，每个都是「新建场景」+「在当前场景追加」两个入口，按原型场景逐项重建位置 / 参数 / 引用）：
  - `QuickOutlineEditor/`：描边示例场景（5 种描边模式各一个物体）；另含**描边材质 Shader 丢失修复**（菜单 `UPandaGF/Tools/QuictOutLine材质修复`）—— Unity 版本升级 / Samples 重建后 `Materials/OutlineMask|OutlineFill` 的 Shader 引用断链变玫红时，一键重挂并保存。
  - `ReplaySystemEditor/`：录制 / 回放示例场景（掉落 / 弹跳的物理物体 + 时间轴、进度、暂停、速度、存 / 取档等 UI 全部自动连线）。
  - `ObjPoolEditor/`：对象池示例场景（含 `UPGameRoot` 启动设施、UI 相机与 Canvas 层级、演示地面与 `TestPoolMgr`）。
  - `VerticalSceneEditor/`：载具驾驶示例场景（内建图元搭的赛道 + 车辆 + 三种视角管理器；`VehicleController` / `CameraModeManager` 的私有序列化字段由脚本自动连线）。
- **EditorTools**：编辑器小工具
  - `ResourcePathCopy`：复制资源路径。
  - `AutoColliderGenerator`：自动生成碰撞体。
  - `FolderQuickOpen`：菜单 `UPandaGF/Tools/打开常用路径/`，一键打开项目根目录、`Assets`、`StreamingAssets`、`persistentDataPath`、`temporaryCachePath`、AB 打包输出目录等常用路径（Windows 下用资源管理器打开，其他平台回退 `RevealInFinder`），同时把路径复制到剪贴板。
- **EditorStu**：编辑器学习示例（都能直接从菜单打开，源码每段都有中文说明）—— `EditorGUIExample`（控件速查窗口，菜单 `UPandaGF/Tools/EditorGUI控件备忘录`：12 个章节覆盖文本 / 数值 / 开关 / 向量 / 颜色曲线 / 枚举下拉 / 资源对象 / 布局与 `GUILayoutOption` / `GUIStyle` / 进度条 / `SerializedProperty` / 事件，带关键字搜索）、`EditorDevelopmentExample`（开发案例窗口，菜单 `UPandaGF/Tools/编辑器开发案例`，快捷键 `Ctrl+Shift+E`：`MenuItem` 写法与 validate、窗口生命周期、`SerializedObject` 与 `Undo`、资产与 `AssetDatabase`、拖拽 / 剪贴板 / JSON、`EditorPrefs`、`EditorApplication` 回调与场景视图 `Handles`、弹窗通知）、`EditorExampleAssets`（示例资产 + 自定义 `PropertyDrawer` / `CustomEditor`）。
- **EditorConfig**：框架配置（`UPandaGFConfig`）。

## 模块文档索引

各模块的详细文档（原理、API、示例、常见问题）与本文档并行维护：

| 文档 | 内容 |
|---|---|
| `Runtime/Manager/DebugSystem/README.md` | 日志系统：两级开关（编译期剔除 / 运行期过滤）、日志链路、落盘机制、两套真机面板、编辑器工具、FAQ、已知限制与变更记录 |
| `Runtime/Manager/EventCenterModule/README.md` | 事件系统：两套实现的内部结构、执行语义、选型建议、编辑器工具、FAQ、已知限制与变更记录 |
| `Runtime/Game/InteractiveTaskScoringSystem/ReadMe.md` | 交互式任务评分系统：架构与调用链、评分规则与难度分级、回放、编辑器工具链、扩展指南、已知限制、FAQ |
| `Runtime/Game/TaskSystem/README.md` | 任务系统：核心概念与状态机、事件驱动接入方式、JSON 存档、UGUI 面板与 HUD 用法、一键搭建菜单、常见问题与扩展点 |
| `Runtime/Game/RobotCCDIK/README.md` | 机械臂 CCD 逆运动学：原理、参数调优、姿态录制回放 |
| `Runtime/Game/ReplaySystem/README.md` | 回放系统：录制、存储与播放 |
| `Runtime/Game/StateMachine/README.md` | 分层状态机 |
| `Runtime/Game/Study/README.md` | 排序算法学习手册（`SortStu`） |
| `Runtime/Manager/ObjPool/README.md` | GameObject 对象池 |
| `Runtime/Manager/DownloadMgr/README.md` | 下载器：队列 / 并发 / 断点续传 / 超时重试 |
| `Runtime/Manager/HTTPTool/README.md` | 基于 UnityWebRequest 的 HTTP 请求模块 |
| `Runtime/Manager/AudioMgr/README.md` | 音频管理器：双路 BGM 交叉淡入淡出、唯一 / 叠加音效、引用计数归还、请求序号防竞态、已知限制与维护提示 |
| `Runtime/Manager/PublicMono/README.md` | 帧调度中心：帧监听（owner 自动注销 / 异常隔离 / 幂等注册）、协程托管（Task 保证结束 + 超时与取消）、已知限制与维护提示 |
| `Runtime/Manager/GameRoot/README.md` | 启动入口：启动时序图、三种 `loadPath` 的热更语义、清单提交与降级规则、事件与就绪状态、配置项（`UPGameRootConfig` + `StreamingAssets/Data/GameRootConfig.json`）、已知限制 |
| `Editor/AssetBundleTools/README.md` | AssetBundle 打包与热更系统：页签用法、清单格式、三种加载位置、热更语义、已知限制、FAQ |

## 源码约定

- **程序集划分**：`Runtime/` 属于 `UPandaGF.Runtime`，`Editor/` 属于 `UPandaGF.Editor`。Runtime 程序集**在 Player 构建时不引用 `UnityEditor`**，因此 Runtime 代码里任何 `using UnityEditor;` 或 `UnityEditor.*` 调用都必须用 `#if UNITY_EDITOR ... #endif` 包住，否则会出现 `CS0246` 打包阻断。
- **文本编码**：全仓库 `.cs` 已统一为 **UTF-8（带 BOM）** + **CRLF**（172/172 文件：Runtime 114 + Editor 58），并由根目录 `.editorconfig`、`.gitattributes` 固化。
- **命名沿袭**：为兼容既有配置与场景，部分历史命名未做改动（如 `OpearationExecute`、`AddErroTimes`、`erroTimes`、部分接口无 `I` 前缀、`TaskTriggerExample` 位于全局命名空间）。
- **改动后自检**：涉及交互式任务评分系统时，改完先跑「任务流程校验器」再跑「运行逻辑自检」，可覆盖绝大多数配置错误与逻辑回归。

## 贡献

欢迎提交 Pull Request 或 Issue 来帮助改进框架。

## 许可证

本项目使用 MIT 许可证。