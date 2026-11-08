# UPandaFramework 优化计划（商业化 / 成熟稳定）

**基线**：`c6af6bd`（2026-11-07，master）
**目标**：把 UPandaFramework 从“高质量工程模板”推进到“可商用、可版本化、可验证、可维护”的 Unity 客户端框架。
**原则**：先正确、再性能；先护栏、再功能；每一阶段都必须有可复现的验收证据，不以文档声明代替验证。

---

## 0. “可商用 / 成熟稳定”的定义

本计划用以下 6 条作为完成标准，而不是“功能多”：

| 维度 | 可商用标准 | 验收方式 |
|---|---|---|
| 可交付 | 可通过 UPM Git URL 安装/升级；有版本号、CHANGELOG、依赖声明 | 在空 Unity 工程安装并编译通过 |
| 可验证 | 核心服务有自动化测试；编辑器和玩法有回归入口 | Unity batchmode 编译 + EditMode/PlayMode 测试报告 |
| 正确性 | 无数据损坏、启动失败、静默丢事件类缺陷 | 缺陷清单关闭 + 回归用例 |
| 安全性 | 编辑器破坏性操作有确认/白名单；热更/存档有完整性校验 | 代码审查 + 手工演练 |
| 性能 | 默认路径无每帧 JSON/反射/分配热点 | Profiler 采样与基线对比 |
| 合规 | 第三方代码/资产有明确许可与再分发结论 | `THIRD_PARTY_NOTICES` + 法务核对记录 |

当前评分（详见审查报告）：综合 3.7 / 5；运行时 4.5，编辑器工具 2.5，可复用交付 2.5。目标 v1.0 达到综合 4.5+，其中编辑器与交付不低于 3.5。

---

## 1. 工作流（Workstreams）

| 流 | 范围 | 关键产出 |
|---|---|---|
| A 交付与包化 | `package.json`、版本、CHANGELOG、Samples 隔离、程序集/命名空间 | 可 UPM 安装的 v0.2+ 包 |
| B 正确性 | 配置、Excel、存档、下载、HTTP、UI、对象池、状态机 | 缺陷修复 + 回归用例 |
| C 性能 | 每帧配置、UI 绑定、状态机分配、AB 轮询、表格加载 | Profiler 基线与优化记录 |
| D 编辑器安全 | 删除/覆盖/上传/构建 | 统一 DestructiveGuard + 白名单 |
| E 数据管线 | Excel → 二进制 → 运行时读取 → 发布 | 端到端可复现流程 |
| F 测试与 CI | EditMode/PlayMode、批处理、平台矩阵 | CI 脚本与测试报告 |
| G 合规 | SharpZipLib、AB Browser、素材（日志面板已替换为自研） | 替换/授权/NOTICE 结论 |
| H 文档与诊断 | 模块文档、已知限制、诊断面板 | 单一事实来源 + 诊断覆盖 |

---

## 2. 阶段计划

### P0：止血与可验证（1–2 周，当前执行）

目标：让默认路径“能跑对、不删库、可编译、可测”，并具备最小包元数据。

| ID | 任务 | 验收标准 | 状态 |
|---|---|---|---|
| P0-1 | 配置快照性能：`DebugerInit` 不再每帧通过防御性 `Config` 复制读取整份配置；保留 `Config` 的防御性语义 | 默认运行时每帧 0 次 `JsonUtility.ToJson/FromJsonOverwrite`；PlayMode 配置冻结测试保持通过 | 已完成 |
| P0-2 | Excel 管线：bool 统一 4 字节、行数用 `BEGIN_INDEX`、写入截断、空单元格/布尔文本容错、路径常量统一、桌面端自动安装到可写目录 | 含 bool/空串/中文表可生成并读回；Windows/Editor 无需手工拷贝 | 已完成 |
| P0-3 | 编辑器护栏：`EditorDestructiveGuard`（确认 + 工程内白名单 + 源/目标互斥）；DLL 工具先枚举后清理；清理 Missing Scripts 加确认/Undo | 误选工程根/源目录时操作被拒绝；DLL 源为空时不删除目标 | 已完成 |
| P0-4 | AB 构建目标：默认 `StandaloneWindows64`；构建前校验 `LoadAssetPath` 目录与主包存在；统一 `BundleTargetName` | 默认 Windows 流程可找到 AB；目录缺失时构建前明确报错 | 已完成 |
| P0-5 | 运行时修复：UGUI 日志面板不再自禁用；对象池出池重置 Transform；`UIManager.Shutdown` 调用 `OnClose` | F12 可打开面板；复用对象位置/旋转/缩放归零；关闭路径一致 | 已完成 |
| P0-6 | 数据/网络/状态机：`PlayerPrefsDataMgr` 支持 null/数组/enum；HTTP 按方法与状态码决定重试；状态机路径仅变化时重建 | 对应 EditMode/PlayMode 用例通过（或静态核对） | 已完成 |
| P0-7 | 包元数据：`package.json`、`CHANGELOG.md`、`OPTIMIZATION_PLAN.md`；版本 `0.2.0` | 元数据齐全；源码导入编译通过；Git URL 安装未在本轮网络验证 | 已完成（安装未验证） |
| P0-8 | 验证：Unity 2021.3/2022.3 批处理编译 + EditMode/PlayMode 测试；记录命令、耗时、结果 | 0 编译错误；Unity 2022.3 与 2021.3 均 EditMode 24/24、PlayMode 294/294 | 已完成 |

**P0 完成定义**：上述 8 项全部有代码/文档产出，Unity 编译通过，EditMode 测试通过；未通过项转入 P1 阻塞清单。

### P1：结构与可靠性（3–6 周）

1. **程序集拆分**：`UPandaGF.Core`（启动/服务/事件/计时器）、`Assets`、`UI`、`Audio`、`Save`、`Diagnostics`；`Game` 拆为可选 `Gameplay.*`；Samples 用 `Samples~` 或独立程序集。**状态：已完成（0.5.0，2026-11-08）**：`Core/Assets/UI/Audio/Save/Diagnostics` + 组合根 `UPandaGF.Runtime`；`Gameplay`/`.Tasks`/`.InteractiveScoring`/`.Replay` 可选；Samples `autoReferenced=false`。说明：`UPGameRoot`/`GameServices` 作为组合根必须引用全部模块，保留在 `UPandaGF.Runtime`；`Core` 放不依赖具体模块的启动生命周期与服务原语。
2. **命名空间收敛**：全部 `UPandaGF.*` 前缀；修正 `RunTime`、`StateMechine`、`BinaryMgr`（用 `[Obsolete]` 过渡）。**状态：已完成（0.5.0，2026-11-08）**：0 个无命名空间文件；`UPandaGF.RunTime.*`→`UPandaGF.Gameplay.*`、`StateMechine`→`Gameplay.StateMachine`、`ReplaySystem`→`Gameplay.Replay`、`AssetBundleBrowser`→`UPandaGF.AssetBundleBrowser`、`BineryMgr`→`BinaryMgr`。命名空间无法用 `[Obsolete]` 转发，迁移表见 `Documentation~/MIGRATION-0.5.md`。
3. **服务层收口**：新增 `IHttpService`、`IPoolService`、`ILogService`；`UIManager`/`AudioMgr`/`QuestManager` 改为注入；运行时实例化对象的注入入口。**状态：已完成（0.3.0，2026-11-08）**，同时移除了 `UIManager`/`AudioMgr`/`QuestManager`/`HttpManager`/`GameObjectPoolMgr` 的静态单例入口，并新增 `IUIService`/`IUIServiceConsumer` 与 `services.Inject(GameObject)`。
4. **Excel 二进制 v2**：文件头（magic/version/column schema），按列名映射，兼容旧格式；生成端/运行端共用 codec 与自检。**状态：已完成（0.4.0，2026-11-08）**：`ExcelBinaryCodec` + `ExcelTableBuilder`，CRC32 自检、列名映射、v1 兼容；EditMode 覆盖编解码/损坏/列顺序。
5. **AB 加载性能**：去掉 `Task.Yield` 轮询，改 `AsyncOperation` 完成回调；`legacyBundles` 收敛到租约模型。**状态：已完成（0.4.0，2026-11-08）**：完成回调 + `Update` 进度泵；旧接口 pin 计入租约，`ReleaseLegacyBundles()` 释放。
6. **Editor 工具安全**：所有删除/覆盖/上传统一护栏；上传顺序改为“AB 全部完成后提交清单”；FTP 凭据不落 EditorPrefs 明文。**状态：已完成（0.4.0，2026-11-08）**：`EditorDestructiveGuard` 扩展 + 有序上传 + 密码仅会话内存/环境变量。
7. **测试补齐**：Excel 往返、PlayerPrefs、UI 关闭、对象池、HTTP 重试策略、AB 目标校验；Editor 工具的最小集成测试。**状态：已完成（0.4.0，2026-11-08）**：EditMode 47（Excel v2/生成端/护栏/构建校验），PlayMode 302（新增 UI Shutdown、legacy pin）。
8. **CI**：Unity batchmode + EditMode/PlayMode + Windows/Android/WebGL 构建；失败返回非 0；测试 XML 归档。**状态：已完成（0.4.0，2026-11-08）**：`Tools/ci/` 与 `.github/workflows/ci.yml`；本机验证 Windows/WebGL/Android 三平台产物，XML 门禁 0 用例/失败均非 0。

### P2：产品化与性能（6–12 周）

1. `package.json` 完整化：`samples`、`documentationUrl`、`changelogUrl`、`licensesUrl`；独立 Samples 包。
2. 平台矩阵：Android 真机（热更/断电/配额）、iOS 构建、WebGL 刷新恢复；Unity 2021.3/2022.3 双版本回归；真机 Profiler。
3. 安全：HTTPS 强制、清单签名、存档 HMAC、日志脱敏、密钥构建注入。
4. 性能：BasePanel 绑定改为显式/源生成；BinaryDataMgr 强类型 reader；EventCenter 高频事件迁移到 EventBus；UI/场景加载对象池化。
5. 可观测性：启动/资源/热更/网络指标统一导出；诊断面板展示模块状态与最近错误。
6. 文档：模块“已知限制”总表；升级指南；从旧版本迁移手册。

### P3：持续（发布后）

- 版本节奏：`0.x` 每 2–4 周，`1.0` 前冻结 API；SemVer + 迁移说明。
- 兼容矩阵：Unity 2021.3 LTS / 2022.3 LTS / Unity 6；Windows / Android / WebGL / iOS。
- 缺陷 SLA：P0 24h、P1 3 天、P2 下个版本；每个修复附回归用例。
- 合规复核：每次新增第三方资产更新 `THIRD_PARTY_NOTICES` 并记录再分发结论。

---

## 3. 验收与验证策略

### 3.1 自动化

- **编译**：Unity 2022.3.53f1c1 batchmode，空工程 + 包目录，`-quit` 后检查 `Library/ScriptAssemblies` 与 Editor.log 中 `error CS`。
- **EditMode**：`-runTests -testPlatform EditMode -testResults <xml>`；要求 0 failed。
- **PlayMode**：优先跑与本次改动相关的 fixture（生命周期/协调/热更/序列化），全量在隔离宿主跑。
- **静态检查**：`FrameworkConventionValidator` 作为“基线不增加”门禁，不作为零违例门禁。

### 3.2 手工/演练

- 编辑器护栏：分别选工程根、Assets、源目录、正常目录，验证拒绝/确认/回滚。
- Excel：含 bool、空串、中文、负数、浮点、长文本的表，生成→读回比对。
- AB：Windows 默认流程构建→StreamingAssets 目录→Player 启动；故意改错 `LoadAssetPath` 验证报错。
- 日志面板：F12/Ctrl+F12 打开/关闭；错误自动弹出。
- 对象池：连续取/还 100 次，验证 Transform 与 `IPoolable` 回调。

### 3.3 证据留存

- `OptimzeLog/P0-<date>/` 保存：编译日志、测试 XML、Profiler 截图/数据、手工演练记录。
- 每个 P0 项在 CHANGELOG 有对应条目；在审查报告有“已修复/未修复”状态。

---

## 4. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| 无 Unity 自动化环境/授权 | 无法证明“可商用” | 本机 Unity 2022.3 批处理；CI 使用 Unity License 或 GameCI |
| 大改破坏现有项目 | 升级成本 | P0 只做局部修复；P1 程序集/命名空间用 `[Obsolete]` 过渡；提供迁移指南 |
| Excel 格式变更 | 旧数据不兼容 | 保留 legacy 读取；新格式带版本；生成端/运行端同版本发布 |
| 第三方许可 | 无法公开/商用分发 | P0 记录；P1 替换 SharpZipLib 或取得授权（日志面板已替换为自研） |
| 单维护者 | 交付节奏 | 自动化与文档优先；每个模块有 owner/验收清单 |
| 性能优化误伤 | 行为变化 | 先加基线采样，再改；每项优化附对比数据 |

---

## 5. 发布门槛

| 版本 | 门槛 |
|---|---|
| `0.2.0`（P0） | 编译通过；EditMode 全绿；P0 缺陷关闭；包可安装；CHANGELOG 完整 |
| `0.5.0`（P1） | 程序集拆分；核心服务注入；Excel v2；CI 三平台；PlayMode 核心回归全绿。**2026-11-08 已达成**：模块程序集 + 组合根、服务注入（0.3.0）、Excel v2、CI 脚本与三平台本机构建、PlayMode 302/302 |
| `1.0.0`（P2） | API 冻结；平台矩阵验证；安全/合规结论；性能基线达标；迁移文档 |

---

## 6. 当前执行记录

| 日期 | 项 | 结果 | 证据 |
|---|---|---|---|
| 2026-11-07 | 计划建立 | 基线 `c6af6bd`；P0 启动 | 本文件 |
| 2026-11-07 | P0-1..P0-8 | 完成 | 见 CHANGELOG、`OptimzeLog/P0-2026-11-07/` 与 Unity XML；2022.3/2021.3 双版本通过 |
| 2026-11-08 | P1-3 服务层收口 | 完成：补齐三个契约、移除五个静态单例、三类改为构造/Configure 注入、Quest 存档走 ISaveService | `OptimzeLog/P1Services-2026-11-08/`；2021.3 编译 0 错、EditMode 24/24、PlayMode 300/300 |
| 2026-11-08 | P1-4..P1-8 数据/性能/编辑器/测试/CI | 完成：Excel v2、AB 完成回调+租约、护栏+有序上传+密码不落盘、测试补齐、三平台 CI 脚本 | `OptimzeLog/P1-2026-11-08/`；2021.3 编译 0 错、EditMode 47/47、PlayMode 302/302；Windows/WebGL/Android 构建产物 |
| 2026-11-08 | 0.6 日志面板自研替换 | 完成：新增 `UPandaLogConsole`（有界线程队列/主线程存储/三页签/显示设置/诊断/搜索折叠/复制导出）与 `AssetLoaddingMethod.NoAssetBundle`（跳过 AB 初始化），删除旧日志面板代码、编辑器钩子与回归测试；项目自有图标迁移到新模块；移除对应第三方声明 | `OptimzeLog/LogConsole-2026-11-08/`；2021.3/2022.3 编译 0 错、EditMode 48/48、PlayMode 306/306 |
| 2026-11-08 | P1-1/P1-2 程序集与命名空间 | 完成：7 个运行时模块 + 组合根、4 个可选玩法程序集、Samples 隔离；全局命名空间 0 个；`RunTime`/`StateMechine`/`BineryMgr` 修正；迁移指南 | `OptimzeLog/P1Structure-2026-11-08/`；2021.3/2022.3 编译 0 错、EditMode 47/47、PlayMode 302/302 |

> 维护规则：每完成一项，更新本表与 `CHANGELOG.md`；未完成项不得标记为“已解决”。
