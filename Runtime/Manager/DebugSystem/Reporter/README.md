# Reporter 日志窗口重构

## 原实现检查结果

`Reporter.cs` 原来约 2,100 行，采集、采样、折叠、生命周期和 IMGUI 布局混在一个组件中。已确认的问题：

1. `maxLogCount` 只裁剪原始 logs，currentLog、collapsedLogs、samples、字符串缓存仍继续增长，条数上限不能约束整体内存。
2. OnDestroy 只退订场景事件，未退订 Application.logMessageReceivedThreaded；静态 created 未复位，销毁后重建也会被当作重复实例。
3. doShow 每次 AddComponent<ReporterGUI>，重复打开会重复绘制；关闭使用 DestroyImmediate。
4. 搜索使用 ToLower，逐条分配字符串，且只搜索消息。折叠键未包含日志级别，相同消息/堆栈的不同级别可能混在一起。
5. 后台线程日志列表无限增长；主线程持锁遍历并调用业务 OnLog，突发日志会长时间占锁并堵塞单帧。
6. sampleId 直接索引独立采样表，清屏/裁剪后可能不再对应有效采样。场景用 byte + Build Settings 数组查找，动态场景可能越界或名称不正确。
7. GUI 基于 Screen 的固定比例和 GUILayout 占位模拟可见区域，列表列宽和消息易重叠；GUI.skin 未恢复，重建样式产生的 GUISkin 也未释放。
8. 清屏强制 GC.Collect；可选 build_info 缺失被作为错误写回日志；导出先删除旧文件且缺少异常反馈。

## 结构

| 文件 | 责任 |
|---|---|
| Reporter.cs | 保留组件与公开接口；订阅/退订、线程队列、限量主线程处理、配置和导出 |
| ReporterLogStore.cs | 原始记录与折叠索引统一保留/淘汰、级别计数和搜索 |
| Reporter.View.cs | 原生 IMGUI 绘制、样式缓存、虚拟列表、详情、设备设置与框架诊断 |
| ReporterGUI.cs | 唯一绘制入口，显示/隐藏复用同一个组件 |

框架数据由 `../FrameworkDiagnostics.cs` 汇总，Reporter 不复制维护模块或资源计数。

## 新界面

- 顶部为标题、设备信息和关闭入口。
- 搜索支持消息和堆栈的 OrdinalIgnoreCase 匹配；三类级别按钮显示当前保留记录的总计数。
- 工具条支持清空、折叠、暂停视图、跟随、换场清空、可选元数据、复制、导出；窄屏可横向拖动工具条。
- 宽度 >= 1000px 时列表/详情左右分栏，窄屏上下分栏；分隔条可拖动调整比例。
- 支持 Screen.safeArea；只绘制可见行与少量缓冲行，详情文本高度按内容/宽度缓存。
- 日志正文禁用富文本解析；详情可选择复制，修改不会写回日志。复制按钮复制完整原文和堆栈。
- Ctrl/Cmd+F 聚焦搜索，Esc 关闭；列表滚轮/拖动、滚动条与跟随状态配合。
- 状态栏显示可见/保留条数、帧率、托管内存、待处理和溢出丢弃计数，以及导出结果。
- 信息页下方显示模块状态、启动逐步耗时及 Resources / AB / 场景 / UI 统计；仅页面可见时每 0.5 秒刷新。
- 使用已重设计的图标与浅色表面；缺少 images/GUISkin 配置时使用文字与运行时样式兜底。

## 兼容与 API

现有 Images 字段、Reporter 脚本 GUID、size/maxSize/maxLogCount/UserData/fps、ShowLogWindows、Initialize、OnGUIDraw、TotalMemUsage 以及 OnPreStart/OnShowReporter/OnHideReporter/OnLog 消息保留。`Initialized` 改为非序列化运行状态，避免旧场景保存 true 后跳过初始化。Sample 和 Log 的原有公共成员保留，Sample.sceneName 与 Log.sample 直接携带采样，sampleId 仅作为记录序号。

新增主线程 API：HideLogWindows、ClearLogs、SetFilter、SetCollapse、SetSeverityFilters；RetainedLogCount、PendingLogCount、DroppedLogCount 用于观察。

显示设置沿用 Reporter_* PlayerPrefs 键，关闭或退出时保存。移除旧的不可达 Snapshot 页面和未真正绘制曲线的 Graph 占位流程；不改变 ReplaySystem。

## 限制与语义

- 默认保留 3000 条 / 估算 20 MiB，取先达到的限制，淘汰最旧记录；设 0 可解除相应保留限制。
- 后台队列默认最多 2048 条 / 估算 4 MiB，每帧最多处理 256 条；超限丢弃最旧待处理记录并计数。
- 消息与堆栈各默认最多 32768 字符，超长会明确加 truncated 标识。内存值是估算，不包含全部对象/GUI/GPU 开销。
- “暂停”暂停向视图缓冲区消费日志；后台仍有限排队，长时间暂停可能溢出。恢复后分帧追赶。
- 折叠计数仅统计当前保留窗口；折叠行使用同组最近一次采样。导出全部保留原始记录，而非仅当前筛选结果。
- FPS 与 GC 内存每 0.25 秒更新，后台日志使用最近采样值；场景直接记录名称，避免 Build Settings 索引依赖。
- 框架资源诊断统计的是缓存、引用和请求数量，不是原生/GPU 内存；启动耗时使用 StartupLifecycle 的实际记录，不由 Reporter 重复计时。
- 同步 OnLog 接收方内产生的日志不再反向捕获，防止自反馈；其它线程正常日志不受影响。业务回调仍须避免长时间阻塞。
- 导出到 persistentDataPath/logs.txt，先写临时文件再使用可恢复提交；仍是同步本地 IO。
- 样式在首次绘制/尺寸变化时建立，关闭后复用，组件销毁释放。实际业务输入是否暂停由原有 OnShowReporter/OnHideReporter 回调决定。

## 验证

ReporterRegressionTests 覆盖保留窗口/折叠计数同步淘汰、级别隔离、堆栈搜索、内存上限、重复显示、禁用/重建、后台突发、队列字节预算、虚拟列表可见范围与缺图兜底。

本轮验证结果（Unity 2022.3.48f1c1）：

- 9 项 Reporter 回归测试通过，连同既有框架回归共 24/24 通过。
- Windows x64 Development Player 构建成功，实际 D3D11 运行未发现异常。
- 检查 1280×800 宽屏、430×900 窄屏、设备设置页实际截图；中文字体、级别图标、详情换行与选中态正常。
- 原工程 Runtime/Editor 编译无错误；`git diff --check` 通过。
- 截图位于 `Editor/PLogger/ReporterPreview/`，使用临时验证场景和合成测试日志；不代表已验证业务场景输入或移动端真机。
- 暂未针对 Android/iOS/WebGL 验证系统字体、软键盘、触摸拖动和文件导出，也未做完整性能基准；虚拟列表绘制数量由可见范围回归测试验证。

本机测试输出：项目根目录 `Temp/reporter-icon-redesign/reporter-tests-final.xml`、`reporter-build-final.log`、`reporter-player-final.log`。
