# 框架优化记录（2026-11-05）

> P1 阶段记录（2026-11-08）：服务层收口见 `P1Services-2026-11-08/`；Excel v2 / AB 性能 / 编辑器护栏 / 测试 / CI 见 `P1-2026-11-08/`；程序集拆分与命名空间收敛见 `P1Structure-2026-11-08/`；日志面板自研替换见 `LogConsole-2026-11-08/`。
>
> 剩余优化项与发布阻塞见 [REMAINING-2026-11-08.md](REMAINING-2026-11-08.md)。

原记录按资源生命周期 → 启动热更 → UI → 存档回放 → 模块依赖与性能实施。保留现有公开加载/面板 API 与序列化字段名，未修改业务场景、预制体和已有 AB 清单格式。

| 顺序 | 本轮完成 |
|---|---|
| 1 | Resources 失败可重试、清理结束等待者、回调异常隔离、完整类型缓存键、两套卸载规则一致、避免 Destroy prefab |
| 2 | AssetsLoader/ABLoadMgr 就绪检查；包文件暂存、大小/MD5 校验、备份及恢复日志、清单最后提交；批次独立终态等待和超时取消；下载取消/销毁结束 Task |
| 3 | 面板并发合并、关闭取消、失败回调、实例销毁归还资源引用；修正拖拽、材质副本与 InputField 事件 |
| 4 | 回放版本化 JSON、旧 DTO 读取兼容、扩展组件持久化、损坏文件保护、物体时间索引与逆向 Seek；二进制存档精确长度与恢复式写入 |
| 5 | Runtime 不再自动引用全部第三方 DLL，Editor 仅显式引用 Excel/SharpZipLib；TMP 使用程序集名；UI 去除逐帧 Task.Yield 等待，回放去除逐帧索引字典分配 |

## 原轮次验证方式（2026-11-05）

Unity 2022.3.48f1c1 临时独立工程复制完整框架及其测试，启用 OPEN_PLOG，运行 UPandaGF.Tests PlayMode 测试。测试位于 `Tests/PlayMode/`，使用 Unity Test Framework；不进入正常 Player 构建。

测试包括缺失资源重复请求、异常监听者隔离、清空后重载、Type/泛型引用配对、面板并发打开/关闭竞态、回放 JSON 与旧 DTO、损坏数据、跨帧 Seek、文件提交成功/失败及恢复、下载取消/销毁、资源初始化失败。

验证结果：

- 15/15 PlayMode 回归测试通过（启用 OPEN_PLOG）。
- 独立工程 Windows x64 Development Player 构建成功，包含完整 Runtime 和资源；验证场景为临时空场景，不代表业务场景已通过功能验收。
- 原工程 `dotnet build UPandaGF.Editor.csproj --no-restore` 成功，Runtime 与 Editor 均无编译错误；保留现有依赖/弃用 API 警告。
- `git diff --check` 通过。

本次本机产物（项目根目录）：`Temp/framework-tests.xml`、`Temp/framework-verification-final.log`、`Temp/framework-player-build.log`。临时工程位于 `Temp/UPandaFrameworkVerification`，未关闭或更改原工程当前打开的场景。

## 后续工作与边界

- 本轮是针对已确认问题的优化，不代表 175 个 C# 文件与全部 Shader 已逐文件审查完成。
- 未直接拆分现有 Runtime 大程序集。玩法脚本与业务场景可能依赖现有程序集身份，拆分前需先完成依赖矩阵与资源迁移验证。
- 原轮次保留的 AB / BinaryDataMgr BinaryFormatter 入口已于 2026-11-06 完成迁移，见 `SerializationMigration/README.md`。新产物需要配套运行端升级，旧客户端不兼容。
- 热更校验、恢复、备份、提交与回收已于 2026-11-06 转入后台文件阶段，并加入暂存/缓存/可用空间预算，见 `HotUpdateStorage/README.md`。移动真机、WebGL 与断电耐久性尚未验证。
- 热更已实现按归属、摘要与清单引用回收旧包；旧版未登记文件继续保留。保留历史引用不等同于完整版本回滚；降级仍要求旧版本或随包资源可用。
- 新回放文件能由新代码读取，旧代码无法读取新 JSON 文件。自定义组件需注册持久化类型；旧文件从未保存的组件数据无法恢复。
- 回放已于 2026-11-06 增加目标注册/周期发现、采样与容量预算、静止帧合并、索引共享和分块异步文件处理，见 `ReplayPerformance/README.md`。自定义 Capture 状态仍需不可变或独立副本，并按值比较；大状态需提供容量估算。
- 对象池/音频/AB 联合修复已完成，见 `LifecycleAudit/FIXES.md`；HTTP/事件/PublicMono/SceneMgr 联合修复已完成，见 `CoordinationAudit/README.md`。热更 IO/磁盘预算已完成本轮优化；回放性能与内存已完成本轮优化；后续继续推进玩法系统、编辑器工具、跨平台及业务长期验证。

## 追加验证（2026-11-06）

HTTP、事件、公共调度、场景切换已完成“旧行为复现 → 修复 → 正常行为回归”。当前 UPandaGF.Tests 为 92 项，包含 42 项本轮新增用例和前轮 50 项。具体证据、Player 冒烟与兼容性变化见 [联合修复记录](CoordinationAudit/README.md)。

## 旧序列化迁移阶段（2026-11-06）

AB 清单、通用存档及两份 AB 工具缓存写入已改为版本化字段图；回放保留版本 2 JSON，旧格式统一走受限 NRBF 数据记录读取。Runtime / Editor 生产代码不再调用 BinaryFormatter。新增 59 项迁移回归，累计 151 项；实际缓存副本与独立 Windows Player 也已验证。详见 [迁移记录](SerializationMigration/README.md)。

## 热更耗时与磁盘管理（2026-11-06）

校验、恢复、备份、提交和回收转入后台；新增暂存/登记缓存/可用空间预算、下载写入上限、受清单保护的旧包回收及归属 stage 清理。提交取消会恢复旧文件；启动维护与原生 AB 使用互斥。新增 40 项回归，累计 191/191 通过，并完成真实 AB 的 Windows Player 更新、加载与失败回退冒烟。详见 [热更优化记录](HotUpdateStorage/README.md)。

## 回放性能与内存（2026-11-06）

目标注册/周期发现、默认 30Hz 与录制预算、静止空帧合并、快照共享索引、游标/二分 Seek，以及版本 2 JSON 分块异步读写已完成。新增 39 项回归，累计 230/230 通过；Windows Player 真实 Animator、旧文件、销毁取消与 20 轮有界录制/清空均通过。200 个静止目标、100 次 Player 采样测得 GC 分配块由 161,200 降至 0。详见 [优化记录](ReplayPerformance/README.md)。

## 跨平台验证与兼容（2026-11-06）

Windows x64 与 WebGL 的 IL2CPP High 构建、实际运行各 12 项通过，WebGL 刷新后 IndexedDB 检查点通过；Android ARM64 High APK 构建成功，尚无真机运行；iOS 未构建。修复裁剪后的字典反射、资源 URL 转义、平台空间查询、WebGL 日志/等待与持久化。新增 18 项回归，累计 248/248 通过。详见 [跨平台记录](CrossPlatform/README.md)。

## 基础设施落地（2026-11-06，FoundationHardening）

按"交付基线 → 契约与模块边界 → 核心服务 → 测试与构建骨架"的优先级推进：

- **P0**：`.gitignore` 不再忽略 `.meta`；`PLogger` 错误日志改为常驻（不受 `OPEN_PLOG` 与 `openLog` 剔除）；`StartupLifecycle` 增加逐步耗时指标；`SceneMgr` 标记 `[Obsolete]`。
- **P1**：Contracts 新增 `ITimerService / IConfigService / IGameFlowService / IAppLifecycle`；`StartupLifecycle` 支持 `IStartupModule` 与 `UPGameRoot.ConfigureLifecycle` 外部自注册；新增 `FrameworkConventionValidator` 编码规范检查。
- **P2**：实现 `TimerService / ConfigService / GameFlowController / AppLifecycleService` 并接入 `GameServices` 与 `UPGameRoot`（前后台、焦点、低内存、退出统一转发）。
- **P3**：新增 `Tests/EditMode`（12 个纯逻辑单测）与 `FrameworkBuildPipeline` 一键构建入口（`UPANDA_VERSION` 注入版本号）。

详见 [落地清单、验收与未落地项](FoundationHardening/README.md)。本轮未做 Runtime 程序集拆分、New Input System、本地化、WebSocket、CI 实际接入，原因与后续顺序见该文档。
