# UPandaFramework 剩余优化工作汇总（2026-11-09）

> 基线：`package.json` **0.6.0** + `[Unreleased]`，HEAD `e1e3ec6`，工作树干净，仓库无 tag。
> 本次核对（2026-11-09 21:40）：Unity 2021.3.45f2c1 编译 0 个 `error CS`；EditMode **53/53**、PlayMode **324/324**（工程副本，原始 XML 见 `P0-P1-2026-11-09/`）。
> 本文取代 `REMAINING-2026-11-08.md`（该文件此前被二次编码损坏，已按可恢复内容修复编码）。

## 0. 一页结论

| 结论 | 内容 |
|---|---|
| 已完成 | 0.2–0.6 的 P0/P1 主体；11-09 描边原创重写、存档加固、代理池停用报错修复 |
| 剩余 | P0 四项、P1 六项、P2 八项、P3 五项；另有 4 项 11-09 后新增 |
| 发布 0.7.0 的真正阻塞 | 第三方许可结论（P0-2）、真机手测（P0-3）、三平台构建（P0-4）、空工程 UPM 安装（P1-3）、版本与 tag（P1-6） |
| 建议节奏 | 先 P0-1/P0-4 复验，再许可与真机，然后版本/tag 与 CI 实跑；P2 按“发布风险 > 性能 > 整洁”推进 |

### 执行状态（2026-11-09 晚更新）

| 项 | 状态 |
|---|---|
| P0-1 | 完成（隔离副本）：53/53、324/324、0 编译错误；原工程仍被编辑器占用，关闭后复跑 |
| P0-2 | 部分完成：许可全文与声明入库，依赖链确认；`ExcelDataReader 2.1.2` 待替换/授权 |
| P0-3 | 阻塞：无真机；清单已交付 |
| P0-4 | 完成：Windows / Android / WebGL 构建成功，Player 冒烟无异常 |
| P1-1 | 阻塞：无法在本机执行 GitHub Actions；工作流与门禁已加固 |
| P1-2 | 完成：计数标注 + CI 用例数下限门禁 |
| P1-3 | 完成（`file:` 方式）；Git URL 未验证 |
| P1-4 | 未完成：`Samples~` 迁移需先解耦 Editor/Tests 引用 |
| P1-5 | 完成：asmdef 校验脚本 + CI 步骤 + UPM 探针 |
| P1-6 | 完成：0.7.0 定版、CHANGELOG、发布说明、tag |

> 详细证据与复现命令见 [P0P1-2026-11-09/README.md](P0P1-2026-11-09/README.md)。

## 1. 本次核对到的状态

| 项 | 实测结果 |
|---|---|
| 版本/提交 | `0.6.0`；HEAD `e1e3ec6`；`git status` 干净；`git tag` 为空 |
| 测试 | EditMode 53/53、PlayMode 324/324、0 失败 0 跳过（2026-11-09 13:28Z 起跑） |
| `Start.unity` | 0 个 Missing Script；`LogConsole` 已绑定 `UPandaLogConsole`（guid `563673e0…`）；`DebugerInit.logConsole` 已连线 |
| `VALIDATION.md` | 已写 53/53、324/324，但“0.6.0 待完成”仍要求在**原工程**复跑后才收尾 |
| `package.json` | 无 `samples` 字段；Samples 仍作为包内目录直接导入 |
| Diagnostics 程序集 | `autoReferenced: true`；仅有 `FrameworkConventionValidator` 静态检查，无用例锁定 |
| CI | `ci.yml` 存在（2 版本 × 2 测试模式 + 3 平台构建），无运行记录；构建 job 的 `unityVersion` 固定为 2021.3 |
| 静态单例 | `LogListenerManager.Instance` 仍在；`TaskDataManager.Instance` 被 `TaskEntityBase`、`DemoTaskHud` 使用；`PlayerRewardService.Instance` 无调用点 |
| 日志视图 | `UPandaLogConsole.View.cs` 1097 行；637/644/646/650/656/657/968 行在绘制路径上分配字符串 |

## 2. P0：发布阻塞（4 项）

| # | 事项 | 现状 / 剩余动作 | 验收标准 |
|---|---|---|---|
| P0-1 | 原工程最终验证 | 场景已清理并绑定新控制台；剩余：保存并关闭编辑器里的场景修改 → 在 `Framework Validation` 原工程跑编译 + EditMode + PlayMode → 更新 `VALIDATION.md` 与计划计数 | 原工程 53/53、324/324，0 编译错误 |
| P0-2 | 第三方许可闭环 | QuickOutline 已随描边重写移除（已消除）。仍待：SharpZipLib `0.86.0.518`（GPLv2+链接例外，有源码提供义务，建议升级 1.x MIT 或移除）、AssetBundleBrowser（Unity Companion License，本地版本未核对）、ExcelDataReader `2.1.2`（许可文本未核对）、Tunneling Vignette（版本未核对）、素材（体积云贴图 / `WaterBump.jpeg` / `Sword.rar` 来源未知） | 每项有版本、SHA/来源、许可证和“可再分发 / 仅内部 / 替换”结论；不可公开分发的资产从发布包剔除 |
| P0-3 | 日志面板真机手测 | 目前只有 batchmode GUI 冒烟测试；触摸拖动、DPI、刘海安全区、横竖屏、滚动条拖动、深色对比度未验证 | Android/iOS 至少各一台（或 Device Simulator）通过：开关、搜索、级别过滤、滚动条/列表拖动、设置页、诊断页、导出、换场清理 |
| P0-4 | 当前树三平台构建 | 上次三平台产物是 0.5 结构期；11-09 改动了 Outline Shader 与存档路径 | Windows/WebGL/Android 构建成功、可启动并打开控制台，无 `error CS` / Missing Script / 白底列表 |

## 3. P1：发布工程与一致性（6 项）

| # | 事项 | 现状 / 剩余动作 | 验收标准 |
|---|---|---|---|
| P1-1 | CI 实跑 | 工作流齐备，但需配置 `UNITY_PROJECT_REPOSITORY` 与 Unity 许可 secrets；2022.3 只跑测试不构建（构建 job 固定 2021.3） | 2 版本 × 2 模式全绿；三平台 build job 有产物；失败时 XML/日志可下载 |
| P1-2 | 计数与文档一致性 | `VALIDATION.md` 已统一为 53/324；`OPTIMIZATION_PLAN.md` 仍混有 24/24、294/294、47/47、302/302、48/48、307/307（历史行未标版本）；`check-test-results` 只查“0 用例/失败>0”，不校验期望用例数 | 当前版本只保留一个计数；历史行标注对应版本；CI 断言期望用例数 |
| P1-3 | 空工程 UPM 安装 | 未在全新 Unity 工程通过 Git URL 或本地 `file:` 安装 0.6.0 | 空工程安装后编译 0 错、`UPandaLogConsole` Inspector 正常、Diagnostics Auto Referenced 勾选、控制台可创建 |
| P1-4 | `package.json` 完整化 | 缺 `samples` 字段；Samples 仍是包内目录 | 增加 `samples` 元数据（或拆 `Samples~` 独立样例）；导入时可选且不进 Player |
| P1-5 | Auto Referenced 门禁 | `autoReferenced` 已为 true，但只有编辑器校验，无自动化用例 | EditMode/CI 断言 `UPandaGF.Diagnostics` 的 `autoReferenced` 必须为 true，改 false 时门禁失败 |
| P1-6 | 0.7.0 发布收尾 | `[Unreleased]` 含破坏性描边变更；版本仍是 0.6.0，无 tag、无发布说明 | 版本号与 CHANGELOG 定版、打 tag、生成 UPM release notes，确认包内无 Windows 绝对路径与未跟踪临时文件 |

## 4. P2：代码质量与性能（8 项）

| # | 事项 | 现状 | 建议动作 |
|---|---|---|---|
| P2-1 | 剩余静态单例 | `LogListenerManager.Instance`、`TaskDataManager.Instance`、`PlayerRewardService.Instance`（后者无调用点） | 改为服务注入；短期无法改的标注为可选玩法的局部单例并加 `[Obsolete]` 说明 |
| P2-2 | 日志视图性能与拆分 | View 1097 行；绘制路径每帧 `ToString("HH:mm:ss")`、`Replace/Substring`、状态栏 `string.Format` 均分配 | 缓存格式化后的行列文本、仅变更时重建；按页签拆分 View 文件；移动端 Profiler 对比 GC |
| P2-3 | 图标/皮肤清理 | `LogConsoleIcons` 仍声明 `evenLogImage`、`oddLogImage`（0 处引用）；`Icons` 目录保留对应资产 | 确认设计源稿归属后删除运行时无用字段与资产，或标注“仅设计归档” |
| P2-4 | 旧迁移工具退役 | `Editor/PLogger/LogConsoleMigration.cs` 仍靠对象名 `Reporter` 识别旧对象 | 所有宿主工程迁移完成后移除，或改为一次性脚本并标注弃用版本 |
| P2-5 | 命名空间/目录一致性 | `Editor` 下并存 `UPandaGF`、`UPandaGF.EditorTools`、`UPandaGF.GFEditor`、`UPandaGF.AssetBundleBrowser*`、`UPandaGF.ResourcePathCopyTool`，还混入 `UPandaGF.Gameplay.InteractiveScoring` | 统一 Editor 命名空间边界，明确 `Runtime/Composition` 组合根职责，停止新增同名工具入口 |
| P2-6 | 发布包裁剪 | `Gameplay.*`、Samples、回放/评分演示与主包同仓 | 确认发布包只含所需模块；示例不进 Player；必要时拆可选包 |
| P2-7 | 移动端 UI 细节 | 小屏（360dp）按钮换行、超长堆栈裁剪、输入法遮挡搜索框、横屏分栏比例未调 | 真机逐项调整并记录截图 |
| P2-8 | 导出与诊断数据 | `SaveVisible()` 只导出当前可见日志；诊断每 0.5 秒采样但没有“复制/导出诊断快照” | 增加“复制诊断信息”和“导出完整日志”两个入口 |

## 5. P3：1.0 前产品化（5 项）

| # | 事项 |
|---|---|
| P3-1 | API 冻结：`UPandaLogConsole`、`FrameworkDiagnosticsSnapshot`、各服务接口公开成员做一次兼容性审查 |
| P3-2 | 安全：HTTPS 强制、清单签名、存档 HMAC、日志脱敏、构建密钥注入 |
| P3-3 | 性能：BasePanel 绑定源生成、BinaryDataMgr 强类型 reader、EventBus 高频事件、UI/场景对象池 |
| P3-4 | 可观测性：启动/资源/热更/网络指标统一导出，诊断面板展示最近错误 |
| P3-5 | 文档：模块“已知限制”总表、升级指南、平台兼容矩阵（2021.3 / 2022.3 / Unity 6） |

## 6. 11-09 之后新增（4 项）

| # | 事项 | 说明 |
|---|---|---|
| N-1 | Unity 2022.3 复验 | 11-09 只在 2021.3 跑过；2022.3 上次验证停留在 0.6.0（48/48、307/307），描边重写后需复跑 |
| N-2 | URP/HDRP 目标管线验收 | 导入期无 Shader 编译错误，但 URP 分支依赖项目深度/模板配置，HDRP 模板从未在目标管线编译；需在对应工程验收五种模式与模板缓冲 |
| N-3 | 文档编码 | `REMAINING-2026-11-08.md` 曾二次编码损坏（已修复）；后续中文文档统一 UTF-8 直写，避免经 GBK 管道转换 |
| N-4 | 工作区清理 | 根目录 `_backup`（P1 前程序集备份）、`_verify`（P1Verify/P1Verify2022/AndroidPathFix 脚手架）、误命名的 `Framework` 日志与 `x.log`，确认无引用后归档或删除 |

## 7. 建议执行顺序（约两周）

1. **P0-1 + P0-4**（半天–2 天）：原工程复跑，再对当前树出 Windows/WebGL/Android 产物，一次覆盖到 0.7.0 收尾。
2. **P0-2**（1–2 天）：先出 SharpZipLib / AssetBundleBrowser / ExcelDataReader 的“可再分发/替换/剔除”决策，这是唯一需要外部判断的项。
3. **P0-3**（1 天，依赖设备）：日志面板真机清单。
4. **P1-6 + P1-3**（半天）：定版 0.7.0、打 tag，空工程验证安装。
5. **P1-1 + P1-2 + P1-5**（1 天）：CI 实跑、计数门禁、Auto Referenced 门禁。
6. **P2-2 / P2-1 / P2-3**（3–5 天）：日志视图 GC 与拆分、单例收口、无用资产清理。
7. 其余 P2/P3 按下个版本排期。

## 8. 证据索引

- 11-09 验证：[P0-P1-2026-11-09/README.md](P0-P1-2026-11-09/README.md) · [playmode-fix](P0-P1-2026-11-09/playmode-fix-2026-11-09.xml) · [editmode-fix](P0-P1-2026-11-09/editmode-fix-2026-11-09.xml)
- 0.6.0 日志面板：[LogConsole-2026-11-08/README.md](LogConsole-2026-11-08/README.md)
- 结构/服务/数据/CI：[P1Structure-2026-11-08/README.md](P1Structure-2026-11-08/README.md) · [P1Services-2026-11-08/README.md](P1Services-2026-11-08/README.md) · [P1-2026-11-08/README.md](P1-2026-11-08/README.md)
- 计划与验收：[OPTIMIZATION_PLAN.md](../OPTIMIZATION_PLAN.md) · [VALIDATION.md](../../Framework%20Validation/VALIDATION.md) · [CHANGELOG](../CHANGELOG.md)

> 维护规则：每关闭一项在此表标注日期与证据；未通过验收不得标记为已解决。
