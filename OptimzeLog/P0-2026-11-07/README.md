# P0 验证记录（2026-11-07）

## 环境

| 版本 | 工程 | 依赖 |
|---|---|---|
| Unity `2022.3.53f1c1` | `_verify/UPandaVerify`（隔离空工程） | TMP 3.0.6 / Test Framework 1.1.33 / uGUI 1.0.0 |
| Unity `2021.3.45f2c1` | `_verify2021`（由 `Framework Validation` 工程复制） | TMP 3.0.6 / Test Framework 1.1.33 / uGUI 1.0.0 |

框架均复制到 `Assets/upanda-framework`（含 `.meta`）。未启用 `OPEN_PLOG`；Error 日志常驻，测试已改为无条件 `LogAssert.Expect`。

## 命令

```powershell
# 1) 编译（导入 + 程序集构建）
Unity.exe -batchmode -nographics -quit -projectPath <project> -logFile compile.log

# 2) EditMode
Unity.exe -batchmode -nographics -projectPath <project> -runTests -testPlatform EditMode `
  -testResults editmode-results.xml -logFile editmode.log

# 3) PlayMode
Unity.exe -batchmode -nographics -projectPath <project> -runTests -testPlatform PlayMode `
  -testResults playmode-results.xml -logFile playmode.log
```

## 最终结果

| 版本 | 编译 | EditMode | PlayMode |
|---|---|---|---|
| Unity 2022.3.53f1c1 | **0 个 `error CS`**，8 个程序集生成，正常退出 | **24 / 24** | **294 / 294**（约 9 秒） |
| Unity 2021.3.45f2c1 | **0 个 `error CS`**，8 个程序集生成，正常退出 | **24 / 24** | **294 / 294**（约 8 秒） |

测试基础设施修复：6 个 `async Task` EditMode 用例改为同步/线程池执行；5 处 `#if OPEN_PLOG` 错误断言改为无条件，避免无宏工程误报。

2021 兼容改动：单例查找在 2022.2+ 用 `FindAnyObjectByType`、旧版本用 `FindObjectOfType`；PlayMode 测试的对象清理使用 `TestObjectSearch` 兼容封装。

## 附件

- `editmode-results.xml` / `playmode-results.xml`：Unity 2022.3 结果
- `editmode-2021-results.xml` / `playmode-2021-results.xml`：Unity 2021.3 结果
- `compile-summary.txt`：2022.3 编译摘要

## 说明

- 这是隔离空工程的框架级验证，不代表具体业务场景/真机通过。
- Android/iOS/WebGL 构建与真机验证未在本轮执行，见 `OPTIMIZATION_PLAN.md` P1/P2。
- 编辑器破坏性操作护栏、AB 构建校验、Excel 生成器属于 Editor 手工流程，本轮通过编译与代码审查验证，未做 UI 点击演练。
