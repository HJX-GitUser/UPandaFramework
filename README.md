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
- 玩法系统：任务系统、交互式任务评分、回放、分层状态机
- 编辑器工具链：AB 打包、Excel 数据表、UI 自动生成、Json 编辑器、HybridCLR 热更

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

## 目录结构

```
upanda-framework/
├── Editor/                      编辑器工具（UPandaGF.Editor 程序集）
│   ├── FrameWorkInitEditor.cs   框架初始化（建目录 / 建 UPGameRoot / 清理缺失脚本）
│   ├── AssetBundleTools/        AB 打包工具（基于 AssetBundle Browser 扩展）
│   ├── UIEditor/                UI 自动生成
│   ├── PLogger/                 日志开关与 Reporter 编辑器
│   ├── ExcelDll/                Excel 数据表生成
│   ├── JsonEditor/              Json 编辑器窗口
│   ├── HyBridCLREditor/         HybridCLR 热更程序集拷贝配置
│   ├── CustomInspector/         自定义 Inspector（UPGameRoot、评分系统等）
│   ├── EditorTools/             编辑器小工具（资源路径复制、自动碰撞体）
│   ├── EditorConfig/            编辑器配置
│   └── EditorStu/               编辑器学习示例
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
│   │   ├── PlaybackSystem/      回放系统
│   │   ├── StateMachine/        分层状态机
│   │   ├── QuickOutline/        高亮描边
│   │   ├── SimpleCameraControl/ 相机控制
│   │   ├── Vignette/            隧道暗角效果
│   │   └── Study/               学习示例
│   ├── Resources/               框架内置资源（材质 / Shader / UI）
│   └── Shader/                  ShaderLibrary
├── README.md
└── LICENSE
```

## 模块介绍

### 启动入口（Manager/GameRoot）

- **UPGameRoot**：框架根节点，`Awake` 时自动挂载并初始化 `DebugerInit`（日志）、`Downloader`（下载）、`AssetsLoader`（资源）、`BinaryDataMgrInit`（存储）、`UIManager`（UI）五个子系统；开启热更时会下载远程资源清单、做 MD5 对比并增量下载，全部完成后广播 `GFLoadedEvent`。
- **GameLaunchExample**：进入游戏的示例入口，演示热更程序集加载与场景加载流程。
- **SourcesLoadMgr**：资源加载抽象层，编辑器下用 `EditorSourcesMgr` 直读资源，出包后切换为 `AssetsLoader` 走 AssetBundle。

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
   - AB 包加载接口。
   - 使用方式：

     ```csharp
     IAssetsLoader loader = UPGameRoot.Instance.GetAssetsLoader();
     ```

   - 开发过程中直接使用编辑器路径加载资源，`UPGameRoot` 的 Inspector 面板可切换为 Editor 模式；打包时再切换为 AssetBundle 模式。
   - AssetBundle 支持本地加载、远程加载，或资源热更下载到本地后加载。

3. **ResourcesLoader**
   - 封装 Unity 内置的 `Resources.Load`，避免资源重复加载，内置引用计数与异步加载状态管理。

4. **StreamingAssetsLoadTool**
   - `StreamingAssets` 路径下的资源加载工具，已处理跨平台兼容性，提供多种加载方式。

5. **AssetBundleMgr**
   - `ABLoadMgr` 负责 AB 的本地 / 远程加载，`AssetBundelUpdataMgr` 负责资源热更。

### 下载器（Manager/DownloadMgr）

- **Downloader**：支持断点续传、多任务并发、下载队列与实时进度回调，提供 `async/await` 接口与事件通知。

### UI 系统（Manager/UIMgr）

- **UIManager**：按 `Bot / Mid / Top / System` 四层管理面板，自动创建 `UICamera`、`Canvas`（1920×1080 适配）与 `EventSystem`。
- **BasePanel**：面板基类；**SimpleLoadUI**：加载界面；**WorldSpaceOverlayUI**：世界空间 UI；**DragArea**：拖拽组件。
- **UIAutoGenerator**（Editor）：UI 自动生成工具。

### 音频（Manager/AudioMgr）

- 背景音乐播放 / 暂停 / 停止 / 音量调节；唯一音效（播放前自动关闭上一个）；多音效列表播放与自动回收；支持 Resources 与 AssetBundle 两种加载方式。

### 场景管理（Manager/SceneMgr）

- 同步 / 异步场景加载，支持 `Single` 与 `Additive` 模式，异步加载进度通过 `SceneMgr_SceneAsynLoadProgress` 事件广播。

### 网络请求（Manager/HTTPTool）

- **HttpManager**：基于 `UnityWebRequest` 的 GET / POST 封装，支持全局请求头、泛型 JSON 反序列化、失败回调。

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
| `PlaybackSystem` | 回放系统：录制对象 Transform 数据，序列化 + 压缩存储到本地 |
| `StateMachine` | 分层状态机：状态注册、状态栈、路径切换（如 `Movement/Run`） |
| `QuickOutline` | 物体高亮描边 |
| `SimpleCameraControl` | 简单相机控制 |
| `Vignette` | 隧道暗角后处理效果 |

### 编辑器工具链（Editor）

- **FrameWorkInitEditor**：一键初始化目录结构、创建 `UPGameRoot`、清理 Prefab 缺失脚本。
- **AssetBundleTools**：AB 打包、管理与上传（FTP / Nginx）。
- **ExcelTool**：根据 Excel 配置生成数据类与数据容器类。
- **UIAutoGenerator**：UI 自动生成。
- **JsonEditor**：Json 编辑窗口。
- **HyBridCLREditor**：HybridCLR 热更程序集拷贝配置。
- **CustomInspector / EditorTools**：自定义 Inspector 与编辑器小工具。

## 贡献

欢迎提交 Pull Request 或 Issue 来帮助改进框架。

## 许可证

本项目使用 MIT 许可证。