# UPandaFramework CI

本目录提供与平台无关的 Unity 批处理脚本，GitHub Actions 工作流在仓库根目录 `.github/workflows/ci.yml`。目标是让“编译 / EditMode / PlayMode / Windows / Android / WebGL”在 CI 与本地用同一条命令，且**结果判定不依赖 Unity 的退出码**（Unity 在某些测试失败/收尾挂起场景仍返回 0）。

## 前置条件

| 项 | 说明 |
| --- | --- |
| Unity | 设置 `UNITY_PATH` 指向 `Unity.exe`（Windows）或 `Unity`（Linux/macOS）。CI 用 GameCI 镜像，无需手动安装。 |
| 宿主工程 | 测试与构建必须在一个真实 Unity 工程里跑：本仓库是 UPM 包，不包含 `ProjectSettings`、测试场景和测试资源。脚本会把包同步到 `<工程>/Assets/upanda-framework`。 |
| License | CI 需要 `UNITY_LICENSE` secret（或 `UNITY_EMAIL` / `UNITY_PASSWORD` / `UNITY_SERIAL`）。本地已登录的编辑器不需要。 |
| AB 产物 | 构建 Player 前，宿主工程的 `StreamingAssets/<LoadAssetPath>` 必须有 AB 与 `.manifest`；`method=Editor` 的工程不要求。 |

PlayMode 回归依赖宿主工程里的测试场景/资源（`LifecycleFixtures`、`CoordinationFixtures` 等会读取 `Assets/Scenes` 与预构建 AB），因此不能在一个空工程里直接跑全量 PlayMode。

## 脚本

| 脚本 | 用途 |
| --- | --- |
| `sync-package.ps1` / `.sh` | 把本包镜像到宿主工程 `Assets/upanda-framework`（排除 `.git`/`Library`/`Temp`/`Logs`/`obj`）。 |
| `invoke-unity.ps1` | 内部使用的 Unity 批处理调用器：清锁、轮询退出标记、按进程树清理、输出退出码。 |
| `run-tests.ps1` / `.sh` | 运行 EditMode 或 PlayMode，轮询结果 XML（避免 Unity 收尾挂死），然后调用结果校验。 |
| `check-test-results.ps1` / `.sh` | 解析 NUnit XML：`total == 0` 或 `failed > 0` 返回非 0。 |
| `build-player.ps1` / `.sh` | 调 `FrameworkBuildPipeline.BuildFromCommandLine`，按目标校验产物是否存在。 |

PowerShell 脚本执行策略受限时，用 `powershell -ExecutionPolicy Bypass -File ...` 运行。

带 `-PackagePath` 时，`run-tests.ps1` / `build-player.ps1` 会在同步后先做一次 `-quit` 预热编译（`-SkipWarmup` 可关闭），避免 Unity 增量导入时序导致首轮编译漏文件。`invoke-unity.ps1` 会为含空格的工程/结果路径加引号，因此 `Framework Validation` 这类带空格的工程也可以直接批处理。

## 本地用法（Windows）

```powershell
$unity = 'D:\Unity\2021.3.45f2c1\Editor\Unity.exe'

# EditMode
powershell -ExecutionPolicy Bypass -File Tools/ci/run-tests.ps1 `
    -UnityPath $unity -ProjectPath D:\work\MyGame -PackagePath . -TestPlatform EditMode

# PlayMode
powershell -ExecutionPolicy Bypass -File Tools/ci/run-tests.ps1 `
    -UnityPath $unity -ProjectPath D:\work\MyGame -PackagePath . -TestPlatform PlayMode

# Windows Player
powershell -ExecutionPolicy Bypass -File Tools/ci/build-player.ps1 `
    -UnityPath $unity -ProjectPath D:\work\MyGame -PackagePath . `
    -Target StandaloneWindows64 -OutputPath Build
```

Linux/macOS：

```bash
export UNITY_PATH=/opt/unity/Editor/Unity
export PACKAGE_PATH="$PWD"
Tools/ci/run-tests.sh EditMode /path/to/MyGame test-results/EditMode.xml test-results/EditMode.log
Tools/ci/run-tests.sh PlayMode /path/to/MyGame test-results/PlayMode.xml test-results/PlayMode.log
Tools/ci/build-player.sh Android /path/to/MyGame Build build-android.log
```

## 退出码

| 码 | 含义 |
| --- | --- |
| `0` | 成功 |
| `1` | 测试存在失败 / 构建失败 / 产物缺失 |
| `2` | 参数、Unity 路径或工程路径无效 |
| `124` | 超时（脚本已强制结束 Unity 进程） |

## GitHub Actions

`.github/workflows/ci.yml` 需要配置：

- 仓库变量 `UNITY_PROJECT_REPOSITORY`：宿主 Unity 工程仓库（`owner/repo`）。
- Secret `HOST_PROJECT_TOKEN`（私有宿主仓库时）与 `UNITY_LICENSE`。

工作流行为：

1. 校验 `package.json` / `CHANGELOG.md` 元数据；
2. 矩阵跑 Unity `2021.3.45f2c1` 与 `2022.3.53f1c1` 的 EditMode + PlayMode；
3. 对每个平台（Windows / Android / WebGL）构建 Player 并归档 `Build/`；
4. 测试 XML 与构建日志作为 artifact 上传；
5. 测试结果由 `check-test-results` 判定，0 个用例也失败。

> 未配置宿主仓库时，`test`/`build` 作业会在“检出宿主工程”一步失败并提示配置 `UNITY_PROJECT_REPOSITORY`；`package` 元数据校验仍可独立通过。
