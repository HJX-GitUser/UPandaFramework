> 编码修复：本文件曾因 UTF-8/GBK 二次转换损坏，2026-11-09 按可恢复内容重建；无法还原的字符以「?」保留。后续文档请直接以 UTF-8 写入。

# UPandaFramework 剩余优化项（2026-11-08）
> 状态基线：`0.6.0`，提�?`541da1c`；本整理文档为新增未提交变更�?
> 已通过：Unity 2021.3 编�??0 错、EditMode **48/48**、PlayMode **307/307**；Unity 2022.3 编�??0 错�??
> 本文件只整理剩余项，不代表已完成；验收前不要标记为已解决??
## P0：发�?验证阻塞

| # | 事�??| 现�??| 验收标准 |
|---|---|---|---|
| P0-1 | Framework Validation ??0.6.0 迁移与最终验??| 包已同步；该工程仍�??Unity 编辑器打开，`Start.unity` 里还有缺脚本的旧日志面板对象；`VALIDATION.md` 仍写�??EditMode 47/47 | 关�??保存编辑????执行 `UPandaGF ??Tools ??迁移日志面板（清理旧对象�????编�??+ EditMode/PlayMode 达�??**48/48??07/307**，并更新 VALIDATION 证�??|
| P0-2 | 第三方许可闭�?| `THIRD_PARTY_NOTICES.md` 仍列�?SharpZipLib 0.86、AssetBundleBrowser、ExcelDataReader 2.1.2、Tunneling Vignette、QuickOutline，以及体积�??水�??Sword 等待核对素�??| 每一项有明确版本、SHA/来源、许可证和“可再分�?仅内部使??替换”结论；不能公开分发的资产从发布包剔除或替�??|
| P0-3 | 0.6.0 日志面板真机/编辑器手??| 当前只有 batchmode GUI 冒烟测试，未在真机验证触摸拖动、DPI 缩放、刘??安全区、横竖屏、滚动条拖动和深色对比度 | Android/iOS 至少各一台真机（??Device Simulator）通过：打开/关闭、搜索、级别过滤、滚动条拖动、列表拖动、设置页、诊断页、导出、换场清??|
| P0-4 | 0.6.0 三平台构建回??| 上�??Windows/WebGL/Android 构建�?0.5 结构时期??.6.0 未重新跑 | Windows/WebGL/Android 构建成功，日志控制台可打开，�??`error CS`/Missing Script/白色列表背景 |

## P1：发布工程与一致�?
| # | 事�??| 现�??| 验收标准 |
|---|---|---|---|
| P1-1 | CI 全矩阵执??| `.github/workflows/ci.yml` 已存在，但本机没??GitHub Actions 运行记录??022.3 ??EditMode/PlayMode 未�??0.6.0 | 2021.3/2022.3 ??EditMode/PlayMode 全绿；三平�??build job 有产物；失败�?XML/日志可下??|
| P1-2 | 测试与文档计数统一 | `Framework Validation/VALIDATION.md` 仍混�?47/47??02/302 ??48/48??07/307；`OPTIMIZATION_PLAN` 历史行包含旧计数 | 当前版本只保留一??48/48??07/307；历史行明确标注旧版本；CI 自动校验 XML 用例�?|
| P1-3 | UPM 安装验证 | 未在全新 Unity 工程通过 Git URL/本�??`file:` 安�??0.6.0 | 空工程安装后：编??0 错、`UPandaLogConsole` Inspector 正常、`UPandaGF.Diagnostics` ??Auto Referenced 为勾选、日志面板可创建 |
| P1-4 | `package.json` 完整�?| ??`samples` 字段；Samples 仍是包内目�??| 增�??`samples` 元数据（或拆独立 Samples 包）；导入�??Samples 可选且不进??Player |
| P1-5 | 程序集默认值回�?| `UPandaGF.Diagnostics` 已�??`autoReferenced: true`，`FrameworkConventionValidator` 已检查；但没有自动化测试锁�??| 增�??EditMode/CI 校验：`UPandaGF.Diagnostics` ??`autoReferenced` 必须�?true，缺�???false 时门禁失??|
| P1-6 | 0.6.0 发布收尾 | 版本�?CHANGELOG 已�??0.6.0，尚未打 tag/生成发布说�??| ??tag、生�?UPM release notes，确认包内�??Windows 绝对路径、无未跟踪临时文??|

## P2：代码质量与性�??

| # | 事�??| 现�??目�??|
|---|---|---|
| P2-1 | 剩余静态单例收�?| `LogListenerManager.Instance`、`TaskDataManager.Instance`、`PlayerRewardService.Instance` 仍在；改为服务注入或明确标注为可选玩法局部单??|
| P2-2 | 日志控制台性�??可维护�?| `UPandaLogConsole.View.cs` 约千行；每帧�?`Timestamp.ToString`、`Message.Replace/Substring`、状态栏 `string.Format` 等分配。缓存格式化??列文本，移动端用 Profiler 对�??GC；按需拆分 View 文�??|
| P2-3 | 日志图标/皮肤清理 | `LogConsoleIcons` 仍保留不再使用的 `evenLogImage`、`oddLogImage`、`arrow` 等引用和资源；旧滚动条箭??PNG/SVG 已不�?skin 引用。确认是否保留设计源稿后，删除运行时无用资产或标注仅设计归�??|
| P2-4 | 旧迁移工具退�?| `LogConsoleMigration.cs` 仍靠对象�?`Reporter` 识别旧对象；`MIGRATION-0.6.md` 保留??API 映射。等所有宿主工程迁移完成后，评估移除迁移工具或改为一次性脚??|
| P2-5 | 命�??目录一致�?| `GFEditor` ??`EditorTools` 命名空间边界、`AssetBundleTools` 历史目录、`Runtime/Composition` 组合根职责需要一次统一梳理；避免继续新增同名工具入??|
| P2-6 | 平�??玩法模块归�??| 可�??`Gameplay.*`、Samples、QuickOutline、任�?评�??回放演示仍跟主包同仓；确认发布包只包含需要的模块，示例不进�??Player |
| P2-7 | 移动�?UI 细�??| 小屏�?360dp）页�?操作按钮换行、超长场景�??堆栈裁剪、输入法遮挡搜索框、横屏分栏比例仍需真机调�??|
| P2-8 | 日志导�??诊断数据 | `SaveVisible()` 只导出当前可见日志；诊断??0.5 秒采样但没有导�??复制诊断快照。评估增加“复制诊断信息”和导出完整日志选�??|

## P3??.0 前产品化

| # | 事�??|
|---|---|
| P3-1 | API 冻结：`UPandaLogConsole`、`FrameworkDiagnosticsSnapshot`、服务接口的公开成员做一次兼容性审??|
| P3-2 | 安全：HTTPS 强制、清单签名、存??HMAC、日志脱敏、构建密钥注�?|
| P3-3 | 性能：BasePanel 绑定源生成、BinaryDataMgr 强类�?reader、EventBus 高频事件、UI/场景对象�?|
| P3-4 | 可观测性：启动/资�??热�??网络指标统一导出，诊断面板展示最近错�?|
| P3-5 | 文档：模块“已知限制”总表、升级指南、平台兼容矩阵（2021.3/2022.3/Unity 6??|

## 建议执行顺序

1. **P0-1** 清掉 Framework Validation 旧场景对象并重�??48/307??2. **P0-2** 先处�?SharpZipLib / AssetBundleBrowser / ExcelDataReader 的再分发结论??3. **P0-3/P0-4** 真机手�??+ 三平台构建，确�??0.6.0 日志面板可交付�?4. **P1-1/P1-2/P1-3** ??CI、统一计数、空工程 UPM 安装�?5. 其�??P2 按“发布风??> 性能 > 代码整洁”推进�?
