# UPandaFramework

Unity 的前端（客户端）框架，提供统一的启动入口、单例、事件、资源热更、UI、日志、任务评分、回放、状态机等常用系统，并配套完整的编辑器工具链。

项目地址：

- https://gitee.com/he-jinxian/upanda-framework.git
- https://github.com/UserHandJ/UPandaFramework.git

## 特性一览

- 统一启动入口 `UPGameRoot`，自动挂载并初始化各子系统
- 四种单例基类（急加载 / 懒加载 × MonoBehaviour / 纯 C#）
- 两套事件系统：委托式 `EventCenter`、接口式 `EventBus`
- 双模式资源管理（编辑器直读 / AssetBundle），支持本地、远程加载与热更下载
- 可编译期剔除的日志系统，支持本地输出与真机日志面板
- 分层 UI 管理与 Canvas 自动生成
- 玩法系统：任务系统、交互式任务评分、回放、分层状态机、机械臂 CCD 逆运动学、载具控制
- 内置 Shader 学习文档与效果案例库（流光、描边、卡通、消融、水波等）
- 编辑器工具链：AB 打包、Excel 数据表、UI 自动生成、HybridCLR 热更、常用路径快捷打开

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

> `UPGameRoot` 的 Inspector 面板可配置：资源加载方式（Editor / Assetbundles）、是否启用资源热更、远程地址、AES 加密配置等。

## 示例场景

框架自带的示例都放在各模块的 `Sample/` 目录下，可直接打开运行：

| 模块 | 场景 | 说明 |
|---|---|---|
| 机械臂 IK | `Runtime/Game/RobotCCDIK/Sample/Sample.unity` | 6 轴机械臂，编辑器内拖动 Target 实时求解，可录制 / 回放姿态 |
| 载具控制 | `Runtime/Game/VehicleSystem/Sample/VerticalScene.unity` | Rigidbody + WheelCollider 车辆，`W/S/A/D` 驾驶、`空格` 手刹漂移、`C` 切视角 |
| 高亮描边 | `Runtime/Game/QuickOutline/Samples/Scenes/QuickOutline.unity` | 描边效果演示（`OutlineMask` / `OutlineFill`） |
| 回放系统 | `Runtime/Game/ReplaySystem/Sample/HFScene.unity` | 录制 / 回放演示场景 |

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
│   ├── CustomInspector/         自定义 Inspector（UPGameRoot、评分系统等）
│   ├── EditorTools/             编辑器小工具（资源路径复制、自动碰撞体、常用路径快捷打开）
│   ├── EditorConfig/            编辑器配置
│   ├── EditorStu/               编辑器学习示例
│   └── UPandaGF.Editor.asmdef   编辑器程序集定义
├── Runtime/                     运行时（UPandaGF.Runtime 程序集）
│   ├── Manager/                 核心管理器层
│   │   ├── GameRoot/            UPGameRoot 启动入口 + SourcesLoadMgr 资源抽象
│   │   ├── Singleton/           单例基类
│   │   ├── DebugSystem/         日志系统
│   │   ├── EventCenterModule/   事件系统
│   │   ├── AssetsMgr/           资源管理（Resources / AB / StreamingAssets）
│   │   ├── DownloadMgr/         下载器
│   │   ├── UIMgr/               UI 管理
│   │   ├── AudioMgr/            音频管理
│   │   ├── SceneMgr/            场景管理
│   │   ├── HTTPTool/            网络请求
│   │   ├── ObjPool/             对象池
│   │   ├── StorageDataMgr/      数据存储
│   │   ├── PublicMono/          公共 Mono（协程 / Update 托管）
│   │   └── Extend/              扩展方法
│   ├── Game/                    玩法系统层
│   │   ├── TaskSystem/          任务系统
│   │   ├── InteractiveTaskScoringSystem/  交互式任务评分系统
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

- **UPGameRoot**：框架根节点，`Awake` 时自动挂载并初始化 `DebugerInit`（日志）、`Downloader`（下载）、`AssetsLoader`（资源）、`BinaryDataMgrInit`（存储）、`UIManager`（UI）五个子系统；开启热更时会下载远程资源清单、做 MD5 对比并增量下载，全部完成后广播 `GFLoadedEvent`。
- **GameLaunchExample**：进入游戏的示例入口，演示"热更程序集加载 → 场景加载"的完整流程，并内置完整的失败兜底。
- **SourcesLoadMgr**：资源加载抽象层，编辑器下用 `EditorSourcesMgr` 直读资源，出包后切换为 `AssetsLoader` 走 AssetBundle。

#### 启动流程（`GameLaunchExample.OnGFLoadedEvent`）

1. 打开加载界面 `SimpleLoadUI`（`UIManager.ShowPanelAsync`）。
2. 取 `UPGameRoot.Instance.GetAssetsLoader()` 得到 `IAssetsLoader`。
3. 若勾选 `loadHotUpdateScripts`：遍历 `HybridCLRScriptsList` 逐个 `LoadAssemblyAsync`，统计成功数量并提示"程序热更完成 / 失败"，随后触发 `onAssemblyLoaded`（供泛型注册使用）。
4. 校验 `firstScene` 非空后调用 `LoadSceneAsync`，同时监听 `ABLoadProgressEvent` 刷新"加载 / 下载"进度，进入场景后关闭加载界面。

整个流程包在 `try/catch` 中：`UIManager` / `SimpleLoadUI` / `UPGameRoot` / 资源加载器 / 热更列表 / `firstScene` 任一缺失都会打印明确的 `PLogger.LogError` 并中止，不会抛空引用异常。

### 单例系统（Manager/Singleton）

| 基类 | 说明 |
|---|---|
| `EagerMonoSingletonBase<T>` | MonoBehaviour + 急加载，自动创建 GameObject、`DontDestroyOnLoad`、重复实例销毁 |
| `LazyMonoSingletonBase<T>` | MonoBehaviour + 懒加载 |
| `EagerSingletonBase<T>` | 纯 C# + 急加载 |
| `LazySingletonBase<T>` | 纯 C# + 懒加载，带双检锁与 `Release()` |

### 日志系统（Manager/DebugSystem）

- **PLogger**：对 `Debug` 的封装，通过 `OPEN_PLOG` 宏在编译期剔除日志，避免运行时字符串拼接造成的性能损耗；支持彩色日志、时间戳、线程 ID 前缀、本地文件输出，由 `LogConfig` 统一配置。
  - 启动 / 剔除日志：菜单 `UPandaGF -> 启动日志/剔除日志`。
- **Reporter（LogView）**：项目出包后可在真机开启日志面板查看日志与调试信息。

### 事件系统（Manager/EventCenterModule）

提供两套实现，按需选择：

**① EventCenter —— 委托式全局事件中心**

```csharp
using UnityEngine;
using UPandaGF;

// 声明事件对象
public class EventTest1 : EventArgBase
{
    public string arg0 { get; private set; }
    public EventTest1(string arg) { arg0 = arg; }
}

// 注册 / 注销
public class Listener : MonoBehaviour
{
    private void OnEnable()
        => EventCenter.Instance.AddEventListener<EventTest1>(OnEvent);

    private void OnDestroy()
        => EventCenter.Instance.RemoveEventListener<EventTest1>(OnEvent);

    private void OnEvent(EventArgBase arg)
    {
        EventTest1 e = arg as EventTest1;
        PLogger.Log($"收到事件，参数：{e.arg0}");
    }
}

// 触发
EventCenter.Instance.EventTrigger(new EventTest1("触发测试"));
```

**② EventBus —— 接口式事件总线**

订阅者实现 `IEventListener<T>`，消息类型为 `struct`，内部使用 `WeakReference` 防内存泄漏，可创建多个实例做上下文 / 场景隔离。

### 资源管理（Manager/AssetsMgr + SourcesLoadMgr）

1. **AB 包打包工具**
   - 基于官方 `AssetBundle Browser` 扩展，新增资源上传页签，并创建资源对比文件用于热更新。
   - 路径：`UPandaGF -> AB包工具 -> AssetBundle Browser`。

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
   - `ABLoadMgr` 负责 AB 的本地 / 远程加载，`AssetBundelUpdataMgr` 负责资源热更。

6. **FileUtility**
   - 文件工具：另存 / 移动（先复制后删除，可指定是否覆盖）等常用文件操作。

### 下载器（Manager/DownloadMgr）

- **Downloader**：支持断点续传、多任务并发、下载队列与实时进度回调，提供 `async/await` 接口与事件通知。
- **DownloadHandlerFile**：继承 `DownloadHandlerScript` 的落盘下载句柄，以追加写 + 文件流实现断点续传。

### UI 系统（Manager/UIMgr）

- **UIManager**：按 `Bot / Mid / Top / System` 四层管理面板，自动创建 `UICamera`、`Canvas`（1920×1080 适配）与 `EventSystem`。
- **BasePanel**：面板基类；**SimpleLoadUI**：加载界面；**WorldSpaceOverlayUI**：世界空间 UI；**DragArea**：拖拽组件。
- **UICameraRegister / UICanvasRegister**：把场景中的相机 / Canvas 注册给 UI 系统（`Reset()` 时自动填充引用）。
- **UILoadInfoAttribute**（`UIAttribute`）：标记面板所属层级与资源加载方式，供自动加载使用。
- **UIAutoGenerator**（Editor）：UI 自动生成工具。

### 音频（Manager/AudioMgr）

- 背景音乐播放 / 暂停 / 停止 / 音量调节；唯一音效（播放前自动关闭上一个）；多音效列表播放与自动回收；支持 Resources 与 AssetBundle 两种加载方式。

### 场景管理（Manager/SceneMgr）

- 同步 / 异步场景加载，支持 `Single` 与 `Additive` 模式，异步加载进度通过 `SceneMgr_SceneAsynLoadProgress` 事件广播。

### 网络请求（Manager/HTTPTool）

- **HttpManager**：基于 `UnityWebRequest` 的 HTTP 封装：`GET / POST / PUT / DELETE`、全局请求头、泛型 JSON 反序列化（`JsonUtility`）、带状态码回调、失败重试（含重试间隔）、下载进度回调、请求取消与并发控制；按需懒加载单例（不在 `UPGameRoot` 的初始化列表中）。
- 模块内暂无调用点；已修复原实现的"重试空引用导致请求队列死锁""全局请求头未接入"等问题，API 说明、用法示例与修复记录见 `Runtime/Manager/HTTPTool/README.md`。

### 对象池（Manager/ObjPool）

- **GameObjectPoolMgr**：GameObject 对象池，支持按层级收纳布局，配合资源加载器使用。

### 数据存储（Manager/StorageDataMgr）

- **PlayerPrefsDataMgr**：基于 PlayerPrefs 的轻量存储。
- **BinaryDataMgr**：二进制序列化存储。

### 公共 Mono（Manager/PublicMono）

- **PublicMono**：非 MonoBehaviour 对象可借此托管协程与 Update 监听。

### 玩法系统（Runtime/Game）

| 模块 | 说明 |
|---|---|
| `TaskSystem` | 通用任务 / 条件 / 奖励 / 分组框架 |
| `InteractiveTaskScoringSystem` | 交互式任务评分系统：步骤管理、操作判定、串行 / 并行操作组、操作记录与评分，面向教学实训场景 |
| `ReplaySystem` | 回放系统：按帧录制对象 Transform 数据，序列化 + 压缩存储到本地；`IRecordableComponent` 可扩展录制其他数据（如 `RigidbodyVelocityRecorder`），`ReplayUI` 提供回放界面 |
| `StateMachine` | 分层状态机：状态注册、状态栈、路径切换（如 `Movement/Run`） |
| `RobotCCDIK` | 串联机械臂 CCD 逆运动学：位置 / 姿态双目标、关节限位、姿态录制回放；原理与用法见该目录 `README.md` |
| `VehicleSystem` | 载具控制：`VehicleController`（Rigidbody + WheelCollider，前轮转向 / 后轮驱动 / 手刹漂移）、`VehicleInput`（键盘输入）、`CameraModeManager`（第三人称 / 第一人称 / 环绕三视角） |
| `QuickOutline` | 物体高亮描边（配套 `OutlineMask` / `OutlineFill` Shader 与材质，示例场景 `QuickOutline/Samples/Scenes/QuickOutline.unity`） |
| `SimpleCameraControl` | 简单相机控制 |
| `Vignette` | 隧道暗角后处理效果（`TunnelingVignette`） |
| `Study` | 学习示例（`SortStu` 等） |

#### 机械臂逆运动学（RobotCCDIK）

`Runtime/Game/RobotCCDIK` 是一套**串联机械臂 IK** 方案，附带示例场景与独立文档：

- **`CCDIKController`**：CCD（循环坐标下降）迭代求解，位置 + 姿态双目标；每个关节只绕**单一局部轴**旋转并可设行程限位；内置平滑插值（协程自实现，不依赖任何第三方补间插件）、复位与调试绘制。
- **`RobotAngleRecoder`**：编辑器辅助组件，Play 模式下拖动 Target 实时驱动 IK，并录制 / 回放关节姿态（`AnglePos` 列表，面板上一键"获取轴数据 / 添加到 AnglePos"）。
- **角度约定**：所有关节角都是"相对复位姿态（`Awake` 时记录）、绕 `rotationAxis` 的增量角"；关节被外部改动后，下一次求解会自动对齐。
- **示例**：`Runtime/Game/RobotCCDIK/Sample/Sample.unity`（6 轴机械臂）。
- **完整原理、API、参数调优与常见问题**：见 `Runtime/Game/RobotCCDIK/README.md`。

最小用法：

```csharp
ccdik.SolveIK(target.position, target.rotation);           // 实时求解
ccdik.MoveTo(target.position, target.rotation, 1f);        // 平滑移动到目标
ccdik.AngleLerp(new float[] { 0, 30, -20, 0, 45, 0 }, 1f); // 平滑改变关节角（增量角）
ccdik.ResetAngle();                                        // 复位
```

### Shader 学习资料（Runtime/Shader）

框架内置一套 Shader 学习文档与效果案例库，适合学习内置（Built-in）渲染管线的 Shader 编写：

- **UnityShader学习文档**：`Shader基础.md` 从零讲解 ShaderLab 结构、数据类型与语义、坐标空间、光照模型、纹理、混合、深度 / 模板测试等完整知识树，附图文与可运行代码。
- **效果案例库**：流光、纹理动画、模型描边、卡通风格、噪声、水波、消融、物体切割、素描风格、翻页、遮挡半透明等 11 个可运行案例。
- **ShaderLibrary**：内置渲染管线常用 Shader 库（`Library/BuildIn`）。

### 编辑器工具链（Editor）

- **FrameWorkInitEditor**：一键初始化目录结构、创建 `UPGameRoot`、清理 Prefab 缺失脚本。
- **AssetBundleTools**：AB 打包、管理与上传（FTP / Nginx）。
- **ExcelTool**（`Editor/Excel/`）：根据 Excel 配置生成数据类与数据容器类，依赖 `Libraries/Excel.dll`。
- **UIAutoGenerator**：UI 自动生成。
- **HyBridCLREditor**：HybridCLR 热更程序集拷贝配置（`DLLCopyConfigWindow`）。
- **CustomInspector**：`UPGameRoot`、评分系统等自定义 Inspector。
- **EditorTools**：编辑器小工具
  - `ResourcePathCopy`：复制资源路径。
  - `AutoColliderGenerator`：自动生成碰撞体。
  - `FolderQuickOpen`：菜单 `UPandaGF/Tools/打开常用路径/`，一键打开项目根目录、`Assets`、`StreamingAssets`、`persistentDataPath`、`temporaryCachePath`、AB 打包输出目录等常用路径（Windows 下用资源管理器打开，其他平台回退 `RevealInFinder`），同时把路径复制到剪贴板。
- **EditorStu / EditorConfig**：编辑器学习示例与框架配置（`UPandaGFConfig`）。


## 贡献

欢迎提交 Pull Request 或 Issue 来帮助改进框架。

## 许可证

本项目使用 MIT 许可证。