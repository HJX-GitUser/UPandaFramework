# UPandaFramework 剩余优化项（2026-11-08）

> 状态基线：`0.6.0`，提交 `541da1c`；本整理文档为新增未提交变更。  
> 已通过：Unity 2021.3 编译 0 错、EditMode **48/48**、PlayMode **306/306**；Unity 2022.3 编译 0 错。  
> 本文件只整理剩余项，不代表已完成；验收前不要标记为已解决。

## P0：发布/验证阻塞

| # | 事项 | 现状 | 验收标准 |
|---|---|---|---|
| P0-1 | Framework Validation 的 0.6.0 迁移与最终验证 | 包已同步；该工程仍有 Unity 编辑器打开，`Start.unity` 里还有缺脚本的旧日志面板对象；`VALIDATION.md` 仍写着 EditMode 47/47 | 关闭/保存编辑器 → 执行 `UPandaGF → Tools → 迁移日志面板（清理旧对象）` → 编译 + EditMode/PlayMode 达到 **48/48、306/306**，并更新 VALIDATION 证据 |
| P0-2 | 第三方许可闭环 | `THIRD_PARTY_NOTICES.md` 仍列有 SharpZipLib 0.86、AssetBundleBrowser、ExcelDataReader 2.1.2、Tunneling Vignette、QuickOutline，以及体积云/水面/Sword 等待核对素材 | 每一项有明确版本、SHA/来源、许可证和“可再分发/仅内部使用/替换”结论；不能公开分发的资产从发布包剔除或替换 |
| P0-3 | 0.6.0 日志面板真机/编辑器手测 | 当前只有 batchmode GUI 冒烟测试，未在真机验证触摸拖动、DPI 缩放、刘海/安全区、横竖屏、滚动条拖动和深色对比度 | Android/iOS 至少各一台真机（或 Device Simulator）通过：打开/关闭、搜索、级别过滤、滚动条拖动、列表拖动、设置页、诊断页、导出、换场清空 |
| P0-4 | 0.6.0 三平台构建回归 | 上次 Windows/WebGL/Android 构建是 0.5 结构时期；0.6.0 未重新跑 | Windows/WebGL/Android 构建成功，日志控制台可打开，无 `error CS`/Missing Script/白色列表背景 |

## P1：发布工程与一致性

| # | 事项 | 现状 | 验收标准 |
|---|---|---|---|
| P1-1 | CI 全矩阵执行 | `.github/workflows/ci.yml` 已存在，但本机没有 GitHub Actions 运行记录；2022.3 的 EditMode/PlayMode 未跑 0.6.0 | 2021.3/2022.3 × EditMode/PlayMode 全绿；三平台 build job 有产物；失败时 XML/日志可下载 |
| P1-2 | 测试与文档计数统一 | `Framework Validation/VALIDATION.md` 仍混有 47/47、302/302 与 48/48、306/306；`OPTIMIZATION_PLAN` 历史行包含旧计数 | 当前版本只保留一套 48/48、306/306；历史行明确标注旧版本；CI 自动校验 XML 用例数 |
| P1-3 | UPM 安装验证 | 未在全新 Unity 工程通过 Git URL/本地 `file:` 安装 0.6.0 | 空工程安装后：编译 0 错、`UPandaLogConsole` Inspector 正常、`UPandaGF.Diagnostics` 的 Auto Referenced 为勾选、日志面板可创建 |
| P1-4 | `package.json` 完整化 | 缺 `samples` 字段；Samples 仍是包内目录 | 增加 `samples` 元数据（或拆独立 Samples 包）；导入时 Samples 可选且不进入 Player |
| P1-5 | 程序集默认值回归 | `UPandaGF.Diagnostics` 已设 `autoReferenced: true`，`FrameworkConventionValidator` 已检查；但没有自动化测试锁定 | 增加 EditMode/CI 校验：`UPandaGF.Diagnostics` 的 `autoReferenced` 必须为 true，缺失/为 false 时门禁失败 |
| P1-6 | 0.6.0 发布收尾 | 版本号/CHANGELOG 已到 0.6.0，尚未打 tag/生成发布说明 | 打 tag、生成 UPM release notes，确认包内无 Windows 绝对路径、无未跟踪临时文件 |

## P2：代码质量与性能

| # | 事项 | 现状/目标 |
|---|---|---|
| P2-1 | 剩余静态单例收敛 | `LogListenerManager.Instance`、`TaskDataManager.Instance`、`PlayerRewardService.Instance` 仍在；改为服务注入或明确标注为可选玩法局部单例 |
| P2-2 | 日志控制台性能/可维护性 | `UPandaLogConsole.View.cs` 约千行；每帧有 `Timestamp.ToString`、`Message.Replace/Substring`、状态栏 `string.Format` 等分配。缓存格式化行/列文本，移动端用 Profiler 对比 GC；按需拆分 View 文件 |
| P2-3 | 日志图标/皮肤清理 | `LogConsoleIcons` 仍保留不再使用的 `evenLogImage`、`oddLogImage`、`arrow` 等引用和资源；旧滚动条箭头 PNG/SVG 已不被 skin 引用。确认是否保留设计源稿后，删除运行时无用资产或标注仅设计归档 |
| P2-4 | 旧迁移工具退场 | `LogConsoleMigration.cs` 仍靠对象名 `Reporter` 识别旧对象；`MIGRATION-0.6.md` 保留旧 API 映射。等所有宿主工程迁移完成后，评估移除迁移工具或改为一次性脚本 |
| P2-5 | 命名/目录一致性 | `GFEditor` 与 `EditorTools` 命名空间边界、`AssetBundleTools` 历史目录、`Runtime/Composition` 组合根职责需要一次统一梳理；避免继续新增同名工具入口 |
| P2-6 | 平台/玩法模块归档 | 可选 `Gameplay.*`、Samples、QuickOutline、任务/评分/回放演示仍跟主包同仓；确认发布包只包含需要的模块，示例不进入 Player |
| P2-7 | 移动端 UI 细节 | 小屏（<360dp）页签/操作按钮换行、超长场景名/堆栈裁剪、输入法遮挡搜索框、横屏分栏比例仍需真机调参 |
| P2-8 | 日志导出/诊断数据 | `SaveVisible()` 只导出当前可见日志；诊断页 0.5 秒采样但没有导出/复制诊断快照。评估增加“复制诊断信息”和导出完整日志选项 |

## P3：1.0 前产品化

| # | 事项 |
|---|---|
| P3-1 | API 冻结：`UPandaLogConsole`、`FrameworkDiagnosticsSnapshot`、服务接口的公开成员做一次兼容性审计 |
| P3-2 | 安全：HTTPS 强制、清单签名、存档 HMAC、日志脱敏、构建密钥注入 |
| P3-3 | 性能：BasePanel 绑定源生成、BinaryDataMgr 强类型 reader、EventBus 高频事件、UI/场景对象池 |
| P3-4 | 可观测性：启动/资源/热更/网络指标统一导出，诊断面板展示最近错误 |
| P3-5 | 文档：模块“已知限制”总表、升级指南、平台兼容矩阵（2021.3/2022.3/Unity 6） |

## 建议执行顺序

1. **P0-1** 清掉 Framework Validation 旧场景对象并重跑 48/306。
2. **P0-2** 先处理 SharpZipLib / AssetBundleBrowser / ExcelDataReader 的再分发结论。
3. **P0-3/P0-4** 真机手测 + 三平台构建，确认 0.6.0 日志面板可交付。
4. **P1-1/P1-2/P1-3** 跑 CI、统一计数、空工程 UPM 安装。
5. 其余 P2 按“发布风险 > 性能 > 代码整洁”推进。
