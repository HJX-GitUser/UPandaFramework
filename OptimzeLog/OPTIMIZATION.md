# 框架优化记录（2026-11-05）

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
- 热更本地校验、备份与提交包含同步 IO；暂存和备份增加峰值磁盘占用，适合启动阶段单实例使用。未验证断电耐久性、移动平台真机与 WebGL 文件系统。
- 保留旧包，尚未实现版本垃圾回收；热更降级仍要求旧版本或随包资源本身可用。
- 新回放文件能由新代码读取，旧代码无法读取新 JSON 文件。自定义组件需注册持久化类型；旧文件从未保存的组件数据无法恢复。
- 回放仍每帧扫描场景 Transform；长时间录制需要后续按目标对象注册和内存预算优化。自定义 Capture 状态应是不可变值或提供按值 Equals，避免变化检测遗漏。
- 对象池/音频/AB 联合修复已完成，见 `LifecycleAudit/FIXES.md`；HTTP/事件/PublicMono/SceneMgr 联合修复已完成，见 `CoordinationAudit/README.md`。后续继续推进热更 IO/磁盘预算、回放性能、玩法系统与编辑器工具。

## 追加验证（2026-11-06）

HTTP、事件、公共调度、场景切换已完成“旧行为复现 → 修复 → 正常行为回归”。当前 UPandaGF.Tests 为 92 项，包含 42 项本轮新增用例和前轮 50 项。具体证据、Player 冒烟与兼容性变化见 [联合修复记录](CoordinationAudit/README.md)。

## 旧序列化迁移阶段（2026-11-06）

AB 清单、通用存档及两份 AB 工具缓存写入已改为版本化字段图；回放保留版本 2 JSON，旧格式统一走受限 NRBF 数据记录读取。Runtime / Editor 生产代码不再调用 BinaryFormatter。新增 59 项迁移回归，累计 151 项；实际缓存副本与独立 Windows Player 也已验证。详见 [迁移记录](SerializationMigration/README.md)。
