# P0/P1 执行记录（2026-11-09）

> 目标：按 `../REMAINING-2026-11-09.md` 的建议顺序执行 P0 与 P1，并留下可复现证据。
> 结果：P0-1/P0-4 完成（P0-1 已在原工程复跑）；P0-2 基本完成（ExcelDataReader 已换 MIT、Sword.rar 已移除）；P0-3 阻塞（无真机）。P1-2/P1-3/P1-5/P1-6 完成；P1-1 阻塞（无远端 Actions）；P1-4 完成（示例改为 Samples~ 可选导入）。

## 1. 环境

| 项 | 值 |
|---|---|
| 测试 Unity | `2021.3.45f2c1` |
| 构建 Unity | `2022.3.53f1c1`（本机 2021.3 只装了 Windows 模块，Android/WebGL 模块在 2022.3） |
| 测试工程 | `_verify/P0Validation-2026-11-09`（原 `Framework Validation` 被编辑器占用，副本内容与仓库一致） |
| 构建工程 | `D:\upanda_build_p0p1`（ASCII 路径，满足 Android 工具链要求；配置改为 `NoAssetBundle` 以便 Player 直接启动） |
| 包 | 仓库工作树镜像到两个工程的 `Assets/upanda-framework`，与 `0.7.0` 源码一致 |

## 2. P0 结果

| 项 | 结果 | 证据 |
|---|---|---|
| P0-1 原工程终验 | **完成（原工程）**：编辑器关闭后在 `Framework Validation` 直接复跑，编译 0 `error CS`；EditMode **53/53**；PlayMode **324/324**（与隔离副本结果一致） | `compile-original.log` · `editmode-original.xml` · `playmode-original.xml` |
| P0-2 第三方许可 | **基本完成**：许可全文入库；`ExcelDataReader` 升级为 3.6.0 + DataSet 3.6.0（MIT，含中文 xlsx 实测），删除 GPL 的 `SharpZipLib 0.86`；`Sword.rar` 已移除。剩余：体积云/水面等教学贴图来源待确认，ABB/Tunneling 版本待核对 | `THIRD_PARTY_NOTICES.md` · `Documentation~/LICENSES/` · `excel-probe.log` |
| P0-3 真机手测 | **阻塞**：本机无 Android/iOS 设备。验收清单已交付 | `../LogConsoleDeviceChecklist-2026-11-09.md` |
| P0-4 三平台构建 | **完成**（2022.3）：Windows 39s / Android 107s（APK 21.1 MB）/ WebGL 90s，均 0 编译错误；Windows Player 冒烟无异常，配置以 `NoAssetBundle` 加载 | `builds/build-summary.txt` · `builds/build-*.log` · `builds/player-windows-final.log` |

## 3. P1 结果

| 项 | 结果 | 证据 |
|---|---|---|
| P1-1 CI 实跑 | **阻塞**：本机无法执行 GitHub Actions。工作流已改为 2021.3/2022.3 双版本构建矩阵，接入 asmdef/用例数门禁，并支持通过 `UNITY_IMAGE_2021_3` / `UNITY_IMAGE_2022_3` 传入中国版镜像；本地已通过 YAML 解析、Shell 语法与脚本干跑 | `.github/workflows/ci.yml` · `Tools/ci/README.md` |
| P1-2 计数与文档 | **完成**：`OPTIMIZATION_PLAN` 历史计数标注版本；`check-test-results` 支持 `--min`（用例数下限），CI 按 editmode 53 / playmode 324 设门禁 | `Tools/ci/check-test-results.ps1` · `.sh` |
| P1-3 UPM 安装 | **完成（file: 方式）**：空工程 2021.3 通过 `file:` 安装，编译 0 错误；`Assembly-CSharp` 直接引用 `UPandaGF.Diagnostics`，证明 `autoReferenced` 生效。Git URL 方式未验证（本地提交尚未推送） | `D:\upanda_upm_probe` · 探针 `Assets/ProbeAutoReference.cs` |
| P1-4 Samples 隔离 | **完成**：`Samples/` → `Samples~`，拆成 5 个可选样例（各自 asmdef）；Editor/Tests 去掉 `UPandaGF.Samples` 引用；模块内演示改为独立 `UPandaGF.Samples.*` 程序集；对象池/交互评分演示工具移入样例，输出到 `Assets/UPandaFrameworkDemos/`。验证：EditMode 53/53、PlayMode 324/324；UPM 探针默认导入无教程示例、手动导入 Pooling 后编译通过 | `Samples~/` · `package.json` · `editmode-samples.xml` · `playmode-samples.xml` |
| P1-5 Auto Referenced 门禁 | **完成**：新增 `check_asmdef_settings.py`，CI `package` job 执行；本地 4 项全过。UPM 探针额外验证了运行侧引用 | `Tools/ci/check_asmdef_settings.py` |
| P1-6 0.7.0 收尾 | **完成**：`package.json` → `0.7.0`，CHANGELOG 定版，新增 `Documentation~/RELEASE-NOTES-0.7.0.md`，提交后打 tag | `package.json` · `CHANGELOG.md` · `Documentation~/RELEASE-NOTES-0.7.0.md` |

## 4. 本轮发现并修复的问题

1. **WebGL 构建编译失败（真缺陷）**：`PLogHelper` 在 `UNITY_WEBGL && !UNITY_EDITOR` 分支引用 `UPandaGF.Assets` 的 `PlatformStorage`，而 Diagnostics 与 Assets/Core 已有引用环，无法简单加引用。已把 `PlatformStorage.cs` 移到 `UPandaGF.Contracts`（命名空间不变，所有使用方都引用 Contracts），WebGL 构建恢复。首次 WebGL 构建日志：`builds/build-WebGL.log`（移动前），成功日志：`builds/build-WebGL.log`（最后一次运行覆盖）。
2. **构建缓存陷阱**：移动源文件后，`Library/Bee` 缓存仍指向旧路径，导致 `error CS2001`；清 `Library/Bee` + `Library/ScriptAssemblies` 后恢复。已在执行脚本中固定该步骤。
3. **UPM `file:` 相对路径基准**：相对路径以工程 `Packages/` 目录为基准，`file:../X` 会解析到 `<project>/X`；正确写法是 `file:../../X`。
4. **空目录 meta 警告**：包内若只有 `.meta` 没有文件（`Runtime/Resources/Materials` 等空目录），UPM 安装时会产生 “folder can't be found” 警告。建议后续删除空目录及其 meta（P2 清理项）。
5. **Windows Player 冒烟**：`-nographics` 下 Shader 报 “not supported on this GPU” 属预期；无异常、无空引用，`UPGameRoot` 成功加载 `GameRootConfig.json`。

## 5. 复现命令

```powershell
# 测试（原工程或副本，关闭编辑器后执行）
D:\Unity\2021.3.45f2c1\Editor\Unity.exe -batchmode -nographics -projectPath "<工程>" -quit -logFile "<工程>\compile.log"
D:\Unity\2021.3.45f2c1\Editor\Unity.exe -batchmode -nographics -projectPath "<工程>" -runTests -testPlatform EditMode -testResults "<out>\editmode.xml" -logFile "<out>\editmode.log"
D:\Unity\2021.3.45f2c1\Editor\Unity.exe -batchmode -nographics -projectPath "<工程>" -runTests -testPlatform PlayMode -testResults "<out>\playmode.xml" -logFile "<out>\playmode.log"

# 三平台构建（2022.3，ASCII 路径工程）
$env:UPANDA_BUILD_TARGET='Android'; $env:UPANDA_BUILD_OUTPUT='Build/Android'
D:\Unity\2022.3.53f1c1\Editor\Unity.exe -batchmode -nographics -projectPath "D:\upanda_build_p0p1" `
  -executeMethod UPandaGF.GFEditor.FrameworkBuildPipeline.BuildFromCommandLine -quit -logFile "build-android.log"
```

## 6. P1-4 实施记录

1. `Samples/` → `Samples~`，`package.json` 新增 `samples`（Startup / Pooling / Coroutines / StateMachine / Tasks）。
2. 每个样例独立 asmdef（`UPandaGF.Samples.*`，`autoReferenced=false`）；Editor 与 Tests 移除 `UPandaGF.Samples` 引用。
3. `创建UPGameRoot` 不再自动挂 `GameLaunchExample`；对象池/交互评分演示工具移入样例 Editor 目录，生成内容改写到 `Assets/UPandaFrameworkDemos/`。
4. 原来用 `.asmref` 并入 Samples 的 5 个模块演示目录改为独立程序集；`QuestDemoInstaller` 随任务演示移到 `Runtime/Game/TaskSystem/Demo/Editor`。
5. 验证：2021.3 编译 0 错、EditMode 53/53、PlayMode 324/324；UPM 探针默认不导入教程示例，手动导入 Pooling 后 `UPandaGF.Samples.Pooling(.Editor).dll` 正常编译。
6. 遗留：原 `Framework Validation` 工程又被编辑器占用，未做最后一次同步；关闭编辑器后需重新 sync 并删除 `Start.unity` 里的 `OnLoadCompleteExample` 旧对象（脚本已随示例移出包）。
## 7. 残留与下一步

- **原工程同步**：P1-4 改了包结构，`Framework Validation` 仍被编辑器占用（22:41 重新打开）；关闭后重新 sync，并删除 `Start.unity` 里的 `OnLoadCompleteExample` 旧对象，再跑一次 53/53 与 324/324。
- **P0-2**：剩余体积云/水面等教学贴图来源确认；ABB 与 Tunneling Vignette 的本地版本号核对。
- **P0-3**：按清单在 Android/iOS 真机执行。
- **P1-1**：推送仓库并配置 `UNITY_PROJECT_REPOSITORY`、Unity 许可证 secrets、以及中国版镜像变量 `UNITY_IMAGE_2021_3` / `UNITY_IMAGE_2022_3`（GameCI 官方镜像不含中国版）。
