# 跨平台验证与兼容优化

日期：2026-11-06。Unity 2022.3.48f1c1。本轮验证前两轮热更、回放，以及现有序列化、日志、下载和资源加载的核心跨平台路径。所有构建和测试使用 `Temp/LifecycleAuditProject`，未更改原工程的 Player Settings、业务场景或预制体。

## 实际验证矩阵

| 目标 | 后端 / 裁剪 | 构建 | 实际运行 |
|---|---|---|---|
| Windows x64 | IL2CPP / High | 成功 | 独立 Player **12/12 通过** |
| WebGL | IL2CPP / High | 成功 | Edge 无界面浏览器、SwiftShader，首次及刷新后均 **12/12 通过**；IndexedDB 数据读回通过 |
| Android ARM64 | IL2CPP / High，API 23–34 | **APK 成功** | 未连接设备，**未做真机运行** |
| Windows Editor PlayMode | Mono | 编译成功 | **248/248 通过**，新增 18 项，前轮 230 项均通过 |
| iOS / macOS / Linux / UWP | — | 本轮未构建 | 无对应运行验收；iOS 缺少 macOS/Xcode 和构建模块 |

Android 包信息：`com.upanda.crossplatformprobe`、仅 arm64-v8a、minSdk 23、targetSdk 34，详见 [包信息与 SHA256](android-package.txt)。可安装 APK 在项目根 `Temp/LifecycleAuditProject/CrossPlatformAndroid/PlatformProbe.apk`，运行结果由测试程序输出到 persistentDataPath/platform-result.json，并以 CROSS_PLATFORM_RESULT 标记写入 Unity 日志；尚未安装到任何设备。

## 复现 → 修复

| 确认的问题 | 修复 | 验证证据 |
|---|---|---|
| Windows 高裁剪后字典的 Comparer / EqualityComparer.Default 反射元数据被移除，存档写入和热更登记 NullReference | Serialization/link.xml 精确保留属性/getter；元数据不可用时报告明确的 InvalidData，而非空引用 | 基线存档、迁移、热更登记失败；修复后新旧存档、比较器、引用图、readonly、二维数组和热更登记通过 |
| WebGL 同一反射空引用导致 Wasm memory access out of bounds，应用中止 | 同一保留规则与空检查 | 基线浏览器日志保留 fatal 栈；修复后完成全部探针且刷新后再次通过 |
| URL 文件名中的 # 被解释为 fragment，Unicode/空格路径请求 404；Path.Combine 会在 Windows 的 jar/http URL 中加入反斜杠 | 新增 AssetLocation：原始相对文件名按段转义，只在本地路径使用文件分隔符；StreamingAssets、AB 和 Root 热更 URL 共用规则 | 浏览器真实读取 `空 格/内容#1.txt`；URL、jar、百分号、本地路径和越界输入回归 |
| Windows IL2CPP 的原 DriveInfo 查询无法取得空间；Unix 根分区也可能不是目标分区 | Windows 使用 GetDiskFreeSpaceExW；Unix fallback 选择最长匹配挂载点；Android 在主线程使用 StatFs；WebGL 查询 origin 配额 | Windows 默认热更空间策略通过；浏览器配额策略通过；Android 分支编译通过，仍待真机 |
| PLogHelper 直接创建线程，WebGL 无相应托管线程运行路径 | 主日志写入器增加协作式 LateUpdate 排空；队列默认最多 4096 条、每帧默认 128 条，可观察丢弃数；禁用/销毁关闭句柄，重复初始化不覆盖当前会话 | Editor 有界排空/关闭/重入测试及浏览器真实文件写入通过 |
| 旧 UGUI 日志查看器同样直接 new Thread | WebGL 使用协作式有界尾读分支 | WebGL 编译通过；未单独做旧查看器的 UI 交互验收 |
| WebGL 的延时和后台续体不能依赖托管线程能力 | 新增 PlatformTasks.Delay，WebGL 以帧循环按真实时间等待；下载、热更批次和对象池使用该入口；对象池结束时取消计时任务；热更 WebGL 续体保留 Unity 上下文 | 浏览器等待、下载与热更完成；暂停时间和取消回归；前轮生命周期回归均通过 |
| WebGL 的 FileStream.Flush 不等同于 IndexedDB 提交 | 新增 PlatformStorage.FlushAsync 与 WebGL jslib 串行提交桥；热更提交、暂存清理及异步回放保存等待提交 | 真实浏览器保存检查点，Page.reload 后从 IndexedDB 读回；不是仅验证内存文件系统 |
| 通过 MakeGenericMethod 调 AddComponent 增加 AOT 泛型依赖 | 改用 GameObject.AddComponent(Type)，拒绝抽象/未闭合泛型类型 | Windows / WebGL 中创建已编译的 Component 通过 |

字典保留规则仅保留两个所需属性，不关闭裁剪，也不把整个框架或 mscorlib 全部保留。它依据 Unity 的 [托管裁剪与 link.xml 约定](https://docs.unity3d.com/2022.3/Documentation/Manual/ManagedCodeStripping.html)。IL2CPP/AOT 平台的动态执行限制见 [脚本限制](https://docs.unity3d.com/2022.3/Documentation/Manual/ScriptingRestrictions.html)，WebGL 单线程等边界见 [平台技术说明](https://docs.unity3d.com/2022.3/Documentation/Manual/webgl-technical-overview.html)。

## 平台 API 与使用约定

```csharp
// 相对路径提供原始文件名，勿预先把空格/#/% 编码。
string location = AssetLocation.Combine(root, "Data/空 格/内容#1.txt");
byte[] bytes = await StreamingAssetsLoader.LoadBinaryDataAsync("/Data/config.bin");

// WebGL 同步存档、普通下载等写完后，在业务存储检查点显式等待：
await PlatformStorage.FlushAsync();
```

- Android / WebGL 的 StreamingAssets 必须使用异步/协程读取；同步入口明确拒绝，避免 UnityWebRequest 忙等锁死主线程。不要在这些平台先用 File.Exists 判断包内文件。
- 路径参数为未转义的相对文件名；#、%、空格等仅在 URL 分支按路径段转义。根 URL 应是资源基地址，不在相对名称中传驱动器、绝对 URL 或 `..`。
- PlatformStorage.AvailableSpaceAsync 的 Android JNI 与 WebGL 桥入口在 Unity 主线程调用；热更在暂存、规划、提交前刷新。Windows 设备可用字节和浏览器 origin 配额是不同来源，不能把后者当作设备物理空闲空间。查询失败仍拒绝有余量要求的更新，逻辑暂存/缓存预算保持生效。
- 热更 CommitAsync 和回放 SaveAsync 在 WebGL 等待持久化完成。热更提交点后的持久化错误停止初始化，不再误报“保留旧版本”。暂存清理的持久化失败记录警告。
- 原同步存档 API、普通下载和自行 File.Write 等只保证当前虚拟文件系统状态；如需跨浏览器刷新保留，应显式等待 FlushAsync。不能依赖页面关闭时仍有机会完成异步操作。
- WebGL 刷新验证使用相同 origin 和页面路径。更换站点/端口/目录可能使用不同 persistentDataPath，遵循 [Unity 路径约定](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Application-persistentDataPath.html)。浏览器禁用存储、清站点数据、配额耗尽或拒绝存储时仍可能失败，接口会报告失败。
- 新增队列上限使日志在积压过大时丢弃后到日志，DroppedCount 可诊断；不是无限保留所有日志。单条正文/堆栈各最多 32768 字符。桌面/移动端默认后台线程，可设置 useBackgroundThread=false 使用协作路径。

## 证据与产物

- [Windows 基线](windows-il2cpp-baseline.json)：11 项中 4 项失败，不能作为当前通过结果。
- [WebGL 基线](webgl-baseline-result.json)、[崩溃与 URL 观察](webgl-baseline-observations.txt)：应用中止，未执行到的步骤没有伪记为通过。
- [Windows IL2CPP 结果](windows-il2cpp-result.json)、[WebGL 首次运行](webgl-first-load-result.json)、[刷新后结果](webgl-result.json)。
- [完整回归](regression-results.xml)、[矩阵/源码摘要](verification-summary.json)、player-probe.cs.txt / player-builder.cs.txt。
- 原工程 dotnet build：0 错误；全量编译仍有 7 项已有警告，增量构建可为 0 警告。未删除警告对应功能。
- C# BOM/CRLF、Git 空白、新文件 meta 及 GUID 唯一性检查通过。

本机运行位置：`Temp/LifecycleAuditProject/CrossPlatformWindows`、`CrossPlatformWebGL`、`CrossPlatformAndroid`。最终日志为 `Temp/platform-windows-fixed-build.log`、`platform-windows-fixed-run.log`、`platform-webgl-fixed-build.log`、`platform-webgl-fixed-browser.log`、`platform-android-final-build.log`、`platform-regression-final.log`。浏览器由独立临时 profile 启动，未操作用户浏览器会话。

## 仍需验证

1. Android 真机安装、包内资源、JNI 空间查询、暂停/恢复、强杀恢复、听感与渲染；iOS 需 macOS/Xcode 后构建并验收。
2. WebGL 目前验证 Edge 桌面软件渲染，未覆盖 Safari、Firefox、移动浏览器、存储拒绝/配额耗尽、跨标签更新竞争及大文件长时间性能。
3. 不因功能通过宣称 WebGL 有后台 C# 性能；其文件校验、解析等仍在单线程运行，较大数据需要单独测量帧耗时。
4. 业务 DTO 的纯反射成员、多态注册、额外 AOT 泛型及裁剪规则需随实际类型验证；本轮覆盖框架清单、登记、回放和探针 DTO，不代表任意第三方对象布局都受支持。
5. 已编译 Component 的 Type 创建通过，不等于 IL2CPP 原生可以执行任意新 DLL；HybridCLR 等运行时热更接入与 AOT 元数据补齐未在本轮验收。
6. 任务/评分等玩法模块、自定义 UI、着色器和编辑器工具的完整平台业务流程不在这 12 项核心探针中；后续继续专项审查。
