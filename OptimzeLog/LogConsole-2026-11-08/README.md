# 运行时日志面板自研替换（0.6.0，2026-11-08）

## 目标

旧运行时日志面板的代码可追溯到 Unity Logs Viewer / Unity Asset Store EULA。本次将其**源码整体删除**，换成独立实现的 `UPandaLogConsole`；旧面板源码、编辑器钩子与第三方声明均已移除。图标是项目方委托设计的自有资产，迁移到新模块继续使用。

## 新增

| 文件 | 说明 |
| --- | --- |
| `Runtime/Composition/LogConsole/UPandaLogConsole.cs` | 组件、线程安全有界入队、主线程批量消费、容量、显隐/清空/复制/导出 API |
| `Runtime/Composition/LogConsole/UPandaLogConsole.View.cs` | 纯 IMGUI 视图：三页签（日志 / 显示 / 诊断）、级别过滤、搜索、折叠、行虚拟化、列显示、堆栈详情、安全区响应式布局；图标缺失时退回纯文字 |
| `Runtime/Composition/LogConsole/LogConsoleStore.cs` | 条数/字节双上限存储、折叠分组、可见区间计算（可单测） |
| `Runtime/Composition/LogConsole/LogConsoleIcons.cs` | 项目自有图标/滚动条皮肤的序列化容器（可空） |
| `Runtime/Composition/LogConsole/Icons/` | 36 个项目自有 PNG + `logConsoleScrollerSkin.guiskin`（原图标资产迁移，保留 GUID） |
| `Editor/PLogger/LogConsoleIconLoader.cs` | 编辑器创建控制台时按模块相对路径绑定图标 |
| `Editor/PLogger/UPandaLogConsoleEditor.cs` | 中文 Inspector：容量/行为/外观说明、运行状态、图标重绑与快捷操作 |
| `Editor/PLogger/LogConsoleIconDesign/` | 图标源稿、生成器、manifest、预览（原目录迁移并改名） |
| `Runtime/Composition/LogConsole/README.md` | 使用与 API 文档 |
| `Tests/PlayMode/LogConsoleRegressionTests.cs` | 10 项回归：淘汰、折叠、搜索、可见区间、线程突发、队列字节预算、显隐/过滤、错误自动打开、GUI 冒烟、显示设置/诊断页冒烟 |
| `Tests/PlayMode/NoAssetBundleModeTests.cs` | 4 项回归：NoAssetBundle 模式初始化/加载、配置校验、GameRoot 启动 |
| `Tests/EditMode/LogConsoleIconTests.cs` | 图标与 GUISkin 能按模块相对路径绑定 |
| `Documentation~/MIGRATION-0.6.md` | 旧字段/方法 → 新 API、场景与编辑器迁移步骤 |
| `Editor/PLogger/LogConsoleMigration.cs` | 菜单 `UPandaGF/Tools/迁移日志面板（清理旧对象）`：删除旧场景中 Missing Script 的旧对象并创建新控制台 |

## 删除 / 迁移

- `Runtime/Composition/Reporter/` 源码与旧 `Images` 目录名删除；用户自有图标迁移到 `Runtime/Composition/LogConsole/Icons/`（PNG/GUISkin 的 GUID 保持）
- `Editor/PLogger/ReporterEditor.cs`（含 `build_info` 编译钩子）
- `Editor/PLogger/ReporterIconDesign/` 旧目录名删除；源稿/生成器迁移到 `Editor/PLogger/LogConsoleIconDesign/`
- `Tests/PlayMode/ReporterRegressionTests.cs`
- `THIRD_PARTY_NOTICES.md` 的旧日志面板 / Unity Logs Viewer 条目

集成点改名：`UPGameRoot.reporter` → `UPGameRoot.logConsole`；`DebugerInit` 调用 `Show()` / `IsVisible`；`UPGameRootEditor` 根据 `EnableDebugModel` 创建/销毁 `LogConsole` 子节点，并通过 `LogConsoleIconLoader` 绑定项目自有图标。

## 显示设置与诊断补全

- **三页签**：`日志` / `显示` / `诊断`，头部图标、关闭、Esc、Ctrl+F 聚焦搜索。
- **显示设置**：时间/场景/内存/帧率列开关；搜索/复制/导出/换场清空工具栏开关；暂停捕获、自动跟随；界面缩放与面板不透明度。设置写入 `UPandaLogConsole.*` 的 PlayerPrefs，隐藏/禁用时保存。
- **诊断信息**：应用/包名/设备/系统/处理器/图形/内存/Unity/构建/会话/用户/场景/缓冲；框架生命周期、模块状态、启动步骤耗时、Resources、AssetBundle、场景作用域、UI 面板计数。框架数据来自 `FrameworkDiagnostics.Capture(UPGameRoot.Instance)`，每 0.5 秒采样一次。
- **复制/导出**：工具栏“复制选中”只复制当前选中日志（含堆栈）；显示设置页展示导出目录、最近导出路径，并提供“复制路径/导出当前日志”。\n- **可读性修复**：去掉日志列表 even/odd 白色行底与列表白底，面板改为深色表面，正文/表头/列使用灰白文字；选中行保留蓝底白字；关闭按钮仅显示一个 X 图标（不再叠加文字 ×）；滚动条改为 14px 无箭头样式，列表内容与滚动条均可拖动。
- **布局适配**：`Screen.safeArea` + 窄屏双行页签、操作按钮自动换行、宽屏左右分栏/窄屏上下分栏、行滚动虚拟化、详情独立滚动；移动端按 DPI 放大并支持内容拖拽滚动；选中行/级别图标使用项目自有图标。

## 验证

环境：Unity `2021.3.45f2c1`、`2022.3.53f1c1`；宿主 `_verify/P1Verify`、`_verify/P1Verify2022`。

| 项 | 结果 |
| --- | --- |
| 2021.3 编译 | 0 个 `error CS` |
| 2022.3 编译 | 0 个 `error CS` |
| EditMode 2021.3 | **48 / 48**（新增图标绑定测试） |
| PlayMode 2021.3 | **307 / 307**（`LogConsoleRegressionTests` 10 项 + `NoAssetBundleModeTests` 4 项） |
| 旧标识搜索 | `.cs` 中无旧日志面板类型/作者标识；仅迁移工具保留旧对象名 `Reporter` 用于识别，`MIGRATION-0.6` 保留旧 API 映射 |
| 文档链接 | 0 条断链 |

原始文件：`editmode-2021.xml`、`playmode-2021.xml`、`compile-2021-summary.txt`、`compile-2022-summary.txt`。

## 边界

- `Framework Validation` 工程在同步 0.6.0 时仍有 Unity 编辑器打开（`Start.unity` 有未保存修改且场景内还挂着旧面板组件），因此该工程的 0.6.0 编译/测试**尚未完成**。关闭编辑器后需要：删除 `Start` 场景里名为 `Reporter` 的旧对象 → 选中 `UPGameRoot` 重新同步日志控制台 → 跑编译 + EditMode/PlayMode。`_verify/P1Verify` 的 2021.3 全量测试已通过，`P1Verify2022` 编译通过。
- 旧面板的日志文件/历史记录没有迁移格式；新控制台只捕获运行时日志，导出到 `persistentDataPath/Logs/`。
- 旧工程里若直接引用 `Reporter` 类型，需要按 `MIGRATION-0.6` 替换为 `UPandaLogConsole`；旧类型不再保留 `[Obsolete]` 壳。
