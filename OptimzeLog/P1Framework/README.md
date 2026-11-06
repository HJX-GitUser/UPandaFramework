# P1：服务装配、所有权、模块边界与基础能力

日期：2026-11-06。范围对应 P1 三项：依赖与所有权、模块边界、输入/暂停/配置/场景协调。P2 的 CI/CD 和发布流水线未纳入。

## 改动清单

| 模块 | 实施结果 |
| --- | --- |
| Contracts | IAssetService、IAudioService、ISaveService、ISceneService、IInputService、IPauseService、IGameClock、IGameServices、ILifetimeScope；公开 AssetLease |
| GameRoot / GameServices | 启动装配具体服务，向场景中的 IGameServicesConsumer 注入；运行中服务集合不可替换；关闭逆序清理 |
| 资源 / AB | 显式持有目标包及依赖包；最后持有者归还后卸载托管包；旧代凭据不会影响新代；兼容原始加载接口 |
| 音频 / UI / 对象池 | 接入 IAssetService；UI 预制体凭据绑定实例销毁；作用域池可以清理借出实例 |
| LifetimeScope / 事件 | 先取消任务，再逆序释放；两套事件均支持作用域订阅，每个注册独立；迟到资源立即归还 |
| 场景 | 请求队列、请求 ID、独立进度、超时与取消；原生操作结束前不放行下一请求；场景 handle 对应独立作用域 |
| 输入 / 时间 | 动作映射、输入上下文栈、嵌套暂停、恢复先前时间缩放与音频状态；UI 输入可在暂停时继续使用 |
| 配置 / Editor | schemaVersion、AB 构建目标、输入映射；启动配置快照与校验；构建场景含根节点时校验 Player 配置 |
| 程序集 | Contracts、Runtime、Extensions、Visuals、Samples、Editor、Tests 单向依赖；示例脚本移动时保留 .meta GUID |
| 业务接入 | 车辆输入、任务示例玩家控制器改用动作接口；无框架根的独立示例保留本地输入后端 |

## 装配和关闭

启动顺序：Configuration → Logging → Data → ContentUpdate → Assets → Services → UI。
Services 装配 AssetService、FileSaveService、GameTimeService、InputService、AudioMgr、SceneService。
根节点就绪后对当前已加载场景及以后 sceneLoaded 的对象执行 IGameServicesConsumer.Configure。
动态 Instantiate 的对象应由创建者显式 Configure；场景注入不扫描每一帧，也不会在服务未就绪时提前交付。

UI 先关闭，接着 SceneService 取消排队请求并等待原生场景操作收尾、释放各场景作用域，再释放根节点管理的 AudioMgr、输入与暂停服务，最后关闭 AssetsLoader 和内容更新。
P1 中 AudioMgr 的兼容单例被纳入根节点服务生命周期；不要在业务中手动 Release 该实例。独立 new 的音频实例仍由创建者管理。
旧单例入口保留，不要求已有项目一次迁移完；新业务使用 IGameServices 或更窄的服务接口，不使用运行中全局注册表。

## 资源所有权

IAssetService.AcquireAsync 返回 AssetLease<T>，调用者负责 Dispose，或交给场景作用域 Own。Retain 创建另一个持有者。
Resources 凭据绑定原缓存记录；AB 凭据同时持有目标包及全部依赖。并发请求先登记持有数，避免另一个请求结束时过早卸载。
手动 UnLoadAB 在对应包有托管持有者时返回 false。ClearAB/Shutdown 是强制维护入口，会让旧代凭据失效；它们不能当成普通业务的释放方式。
AB 最后归还时卸载包容器，沿用 Unload(false)，不会强制销毁仍被 Unity 场景对象引用的资源。最终 Unity 对象回收仍遵循 Unity 引用和场景生命周期。
AssetDatabase 模式资源由编辑器拥有，凭据表达调用者生命周期，不卸载编辑器资产。

原始 LoadAsync/GetAssetBundle 没有归还凭据，属于显式管理的兼容模式。被原始 API 使用过的包及依赖不会因托管凭据归零而自动卸载，需原有 UnLoadAB/ClearAB 清理。
旧第三方 IAssetsLoader 实现无法凭空获得释放语义；兼容分支仍按原实现管理，不能假设它支持引用计数。

取消加载等待只取消交付；若原生加载迟到，AssetService 会观察并归还迟到凭据。
作用域池由 CreatePool(scope) 创建，关闭时清理空闲和借出实例；普通 Pool.Dispose/Clear 保持旧的借出对象持有语义。
注入对象池的同步 GetObj 只允许复用已准备好的缓存，冷加载使用 GetObjAsync/Prewarm。

## 场景作用域和队列

GetScope(scene) 按 handle 建立作用域，重复加载同一路径的 Additive 场景互不共享。
托管加载先准备场景 AB，再提交原生加载，包凭据归属于实际产生的场景，直到卸载事件才释放。
取消/超时后等待 Task 为取消态；已提交给 Unity 的原生操作继续完成，期间队列锁保留。已经加载出来的场景不会被伪装成回滚，其资源继续归该场景拥有。
排队时取消的请求不触发后端。后端失败不会阻止后续请求。
手动 UnloadAsync 会取消该场景任务，资源在原生卸载结束后归还。外部 SceneManager 卸载也会经 sceneUnloaded 清理作用域。
退出后事件退订、作用域面板关闭、资源归还以及任务取消均由同一个作用域负责。任务须实际使用 scope.Token 才能被取消。

进度包含 RequestId、Scene、Phase、Progress；可通过单请求回调或服务事件接收。旧 SceneMgr 进度事件保留原 progress 字段并添加请求信息。
根节点装配后 SceneMgr 异步入口转发到同一服务；AssetsLoader 的旧场景入口也转发完整路径，保留资源准备 → 场景完成的回调顺序。
兼容场景名会解析清单中唯一的 .unity 路径；重名必须使用完整路径。
受管理请求尚未结束时，旧同步加载/原始卸载入口拒绝绕过队列。未装配根节点时，SceneMgr 继续保留原协程行为。
直接调用 Unity SceneManager 提交加载不经过本服务队列，业务应统一使用 ISceneService。

## 输入与暂停

输入动作由 UPGameRootConfig.inputBindings 配置，默认覆盖移动、跳跃、交互、手刹、视角、暂停和 UI 确认/取消。
InputService 依赖 IInputBackend；当前适配 Unity 旧输入 API，替换输入后端无需改业务动作调用。此次没有新增 Input System 包。
PushContext("UI") 屏蔽下层 Gameplay，上层允许的 UI 动作仍可读取；句柄可乱序释放。
Pause(reason) 返回独立凭据。首个暂停记录原时间缩放和 AudioListener.pause；最后一个恢复，或服务关闭时恢复。暂停期间 SetTimeScale 更新恢复后的目标速度。
Gameplay 输入在暂停时返回零/false，UI 输入不受影响。Clock.DeltaTime 为游戏时间，Clock.UnscaledDeltaTime 为界面/真实时间；定时器、HTTP、下载不因 timeScale=0 自动暂停。
PauseMenuController 可挂在场景控制节点，配置现有 menuRoot；Open/Close 拥有并归还暂停与 UI 上下文凭据，停用自动恢复。动态创建时由创建者 Configure。
OnApplicationPause 另持有独立暂停凭据，恢复应用不会误解除菜单暂停。

## 配置与构建校验

读取面板/JSON 后创建深拷贝运行快照。Config 在启动完成冻结后返回副本，SetConfig 拒绝修改；Inspector 的运行时字段修改不改变已装配服务。
启动校验 schemaVersion、加载模式、超时、路径、HTTP 地址、AB 构建目标、热更容量、AES 长度及重复/空输入映射。错误终止启动并走 P0 回滚。
缺少旧 JSON 新字段会保留默认值；现有 schemaVersion 为 1。非法 JSON 的既有读取降级行为保留，字段语义错误不会降级成成功启动。
bundleTarget 使用 Unity BuildTarget 名称，例如 StandaloneWindows64、Android、WebGL，且应与实际 AB 产物一致。
构建处理器只对实际构建场景中存在 UPGameRoot 的场景验证发布配置，禁止 Editor 加载模式，检查目标平台。仅导入框架工具的无根节点构建不会被强加启动配置。
运行时动态创建根节点的项目仍由启动校验兜底；该构建处理器不能静态推断任意业务脚本是否将动态创建根节点。
菜单 `UPandaGF/框架/校验启动配置` 用于 Editor 配置检查；发布环境校验由构建处理器执行。

## 程序集边界

```text
UPandaGF.Contracts ← UPandaGF.Runtime ← UPandaGF.Extensions
        ↑                   ↑                  ↑
        └──────────────── UPandaGF.Samples ─────┘
UPandaGF.Visuals 独立；Editor/Tests 引用被检查的相应程序集。
```

Runtime 包含平台服务和启动装配；不再包含车辆、任务、回放或演示代码。
Samples 通过 .asmref 汇集原 Demo/Example/Test/Study 目录，并将零散示例移动到 Samples 下；脚本 GUID 保留。
业务脚本命名空间与序列化 DTO 全名不变。VersionedDataSerializer 按类型全名处理，既有迁移回归通过；硬编码 CLR 程序集名称的外部反射代码需要更新。
核心单独编译验证实际未复制 Game、Shader、Samples 目录，证明 Runtime/Contracts 可以脱离业务扩展编译。

## 文件和验证

- [迁移与使用示例](MIGRATION.md)
- [验证记录](VALIDATION.md)
- Runtime/Contracts：服务契约与凭据
- Runtime/Manager/Services：装配、作用域、资源/场景/存档服务、输入与时间
- Tests/PlayMode/P1RegressionTests.cs：新增 P1 回归

未改变网络协议、业务存档格式、渲染方案或热更事务格式；未新增 DI/输入第三方依赖。
