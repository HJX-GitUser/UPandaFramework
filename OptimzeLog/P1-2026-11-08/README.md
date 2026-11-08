# P1 数据 / 性能 / 编辑器 / 测试 / CI（0.4.0，2026-11-08）

对应 `OPTIMIZATION_PLAN.md` 的 P1-4..P1-8。P1-1（程序集拆分）与 P1-2（命名空间收敛）尚未开始，留待下一轮。

## 改动范围

### P1-4 Excel 二进制 v2

| 项 | 文件 |
| --- | --- |
| v2 编解码（magic/版本/列结构/CRC32、v1 判定、边界校验） | `Runtime/Manager/StorageDataMgr/ExcelBinaryCodec.cs` |
| 共享格式常量（magic、版本、最小长度） | `Runtime/Manager/StorageDataMgr/ExcelBinaryFormat.cs` |
| 运行端按列名映射 + legacy v1 读取 + `LoadTable(byte[])` | `Runtime/Manager/StorageDataMgr/BineryMgr/BinaryDataMgr.cs` |
| 生成端 DataTable → 强类型行（可测试、无 IO） | `Editor/Excel/ExcelTableBuilder.cs` |
| ExcelTool v2 生成、自检、批量校验按钮、Assets 路径校验、覆盖确认 | `Editor/Excel/ExcelTool.cs` |

- v2 列按名字映射到数据类字段：Excel 调换列顺序不破坏读取；表缺字段用默认值并告警，数据类缺列忽略。
- 读取端校验 magic/版本/列结构/行数/CRC32；截断、尾部冗余、CRC 不符都抛 `InvalidDataException`，不返回半张表。
- 生成端写盘前用同一份 codec 解码自检；工具页签新增「校验二进制表（v2 自检）」。
- legacy v1（无文件头、按字段顺序）继续可读。

### P1-5 AB 加载性能

| 项 | 文件 |
| --- | --- |
| `AsyncOperation.completed` 完成回调替代 `Task.Yield` 轮询；`Update` 统一推进进度 | `Runtime/Manager/AssetsMgr/AssetBundleMgr/ABLoadMgr.cs` |
| legacy pin 进入租约计数；移除 `legacyBundles` 旁路集合 | 同上 |
| `ReleaseLegacyBundles()` / `IsLegacyPinned()` / `AssetBundleDiagnostics.LegacyPins` | 同上 + `Runtime/Manager/DebugSystem/FrameworkDiagnostics.cs` |

- `AssetBundle.LoadFromFileAsync` / `UnityWebRequest.SendWebRequest()` / `LoadAssetAsync` 全部改为完成回调驱动；进度由 `ABLoadMgr.Update` 在存在在途加载时采样（无在途操作时立即返回）。
- 旧 `GetAssetBundle` / `LoadResAsync` 对目标包与依赖各 pin 一次；`UnLoadAB` 在存在 legacy pin 时拒绝，`ReleaseLegacyBundles()` 统一释放。旧接口行为变化属于破坏性调整。

### P1-6 编辑器工具安全

| 项 | 文件 |
| --- | --- |
| 覆盖目录/上传确认（目标地址协议校验、文件数） | `Editor/EditorTools/EditorDestructiveGuard.cs` |
| FTP 异步顺序上传；全部 AB 成功后提交清单；密码不落盘 | `Editor/AssetBundleTools/AssetEditor/UpLoad/UpLoadFTP.cs` |
| 上传按钮等待、上传中禁用、进度条、清单单独上传 | `Editor/AssetBundleTools/AssetEditor/UpLoad/UpLoadABEditor.cs` |
| 强制重构建/复制 StreamingAssets 使用护栏 | `Editor/AssetBundleTools/AssetEditor/AssetBundleBuildTab.cs` |
| 立方体贴图覆盖确认 | `Editor/CubemapTextureGenerator.cs` |
| HTTP 上传前确认 | `Editor/AssetBundleTools/AssetEditor/UpLoad/NginxUploader.cs` |

- FTP 密码只保存在当前编辑器会话内存；可用环境变量 `UPANDA_FTP_PASSWORD` 提供。旧版本 `UpLoadABEditor_Ftp_Password` 明文键读取一次后删除。
- 「上传AB包和清单」先上传并等待全部 AB；任一失败则中止，远端保留旧清单。

### P1-7 测试补齐

| 测试 | 覆盖 |
| --- | --- |
| `Tests/EditMode/ExcelBinaryV2Tests.cs`（11） | 编解码往返、类型解析、列名映射、legacy 判定、CRC/截断/尾部冗余、类型/主键/宽度错误 |
| `Tests/EditMode/ExcelGenerationPipelineTests.cs`（5） | DataTable → v2 → BinaryDataMgr 全链路、主键识别、非法类型/单元格 |
| `Tests/EditMode/EditorGuardTests.cs`（7） | 路径包含/前缀判定、新文件覆盖、`BundleTargetName`、AB 产物目录/manifest 校验 |
| `Tests/PlayMode/P1RegressionTests.cs` 新增 | `UIManager.Shutdown` 关闭面板并归还租约；legacy pin 受租约保护、`ReleaseLegacyBundles` 释放 |

### P1-8 CI

| 项 | 文件 |
| --- | --- |
| 包同步、Unity 调用、测试/结果校验、三平台构建（PowerShell/bash） | `Tools/ci/*.ps1`、`Tools/ci/*.sh` |
| GitHub Actions（测试矩阵 + 三平台构建 + artifact） | `.github/workflows/ci.yml` |
| CI 构建入口（退出码 0/1/2、Android ASCII 路径前置校验） | `Editor/Build/FrameworkBuildPipeline.cs` |

- 测试结果以 NUnit XML 判定：`total == 0` 或 `failed > 0` 返回 1（Unity 退出码不作为唯一依据）。
- 同步包后可选预热编译，避免 Unity 增量导入时序导致首轮编译漏文件。
- 本机未执行 GitHub Actions（缺宿主仓库与 CI License）；脚本本身已在隔离宿主上逐项验证。

## 验证

环境：Unity `2021.3.45f2c1`（Windows 构建与 2021 测试）、`2022.3.53f1c1`（WebGL/Android 构建）；宿主 `_verify/P1Verify`、`_verify/P1Verify2022`；Android 在 ASCII 路径副本 `D:\UnityCISmoke\P1Verify2022` 构建。

| 项 | 命令 | 结果 |
| --- | --- | --- |
| 编译 2021.3 | `Unity.exe -batchmode -nographics -quit -projectPath _verify/P1Verify` | 0 个 `error CS` |
| EditMode 2021.3 | `Tools/ci/run-tests.ps1 -TestPlatform EditMode` | 47 / 47 |
| PlayMode 2021.3 | 同上 `-TestPlatform PlayMode` | 302 / 302 |
| CI 结果门禁 | `check-test-results.ps1`（失败 XML / 空 XML） | 均返回 1 |
| Windows 构建 | `build-player.ps1 -Target StandaloneWindows64`（2021.3） | `UPandaGF.exe`（653,824 B） |
| WebGL 构建 | `build-player.ps1 -Target WebGL`（2022.3） | `Build/CIWebGL/index.html`（产物约 9.0 MB） |
| Android 构建 | `build-player.ps1 -Target Android`（2022.3，ASCII 路径） | `UPandaGF.apk`（22,026,634 B） |

原始文件：`editmode-2021.xml`、`playmode-2021.xml`、`compile-2021-summary.txt`、`builds-summary.txt`、`ci-scripts-check.txt`。

## 已知边界

- **Android 工程路径必须全 ASCII**：本机原路径含中文时 Unity 抛 `Invalid project path`；已把校验前移到 `FrameworkBuildPipeline`，并在 ASCII 路径副本完成 APK 构建。CI runner 的路径通常是 ASCII。
- 2022.3 只做了编译 + WebGL/Android 构建，未跑 2022.3 的 EditMode/PlayMode 全量；测试矩阵的 2022.3 两个模式由 CI 工作流覆盖（本机未执行 Actions）。
- CI 工作流需要宿主工程仓库变量 `UNITY_PROJECT_REPOSITORY` 与 `UNITY_LICENSE` secret；空工程无法跑完整 PlayMode（fixture 依赖宿主场景/资源）。
- **破坏性变更**：legacy AB pin 现在计入租约，`UnLoadAB` 会被拒绝，必须先 `ReleaseLegacyBundles()`；FTP 密码不再写入 EditorPrefs，旧键只迁移一次。
- v1 表只保留读取；ExcelTool 只生成 v2。旧的 v1 表需要重新生成才能获得列名映射/CRC 校验。
- 进度事件由 `ABLoadMgr.Update` 采样：`ABLoadMgr` 被禁用或不在场景时不会推进进度（加载本身仍可完成）。
