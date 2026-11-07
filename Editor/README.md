# UPandaGF 编辑器工具

`UPandaGF.Editor` 只在 Unity Editor 中编译。工具管理宿主项目的配置、资源和场景，也包含模块自检与示例生成入口。 [程序集](UPandaGF.Editor.asmdef)

## 框架、配置和发布

| 功能 | 入口与输出 |
| --- | --- |
| 项目初始化 | `UPandaGF → Tools → 创建常用目录文件夹` 创建约定目录。`UPandaGF → 创建UPGameRoot` 添加启动对象和启动示例。清理丢失脚本的菜单处理当前选中的层级。 [FrameWorkInitEditor.cs](FrameWorkInitEditor.cs) |
| 启动配置 | `UPGameRoot` Inspector 配置加载方式、热更、AES 与调试。保存场景时，持久化工具取已打开场景中找到的根，写入 `Assets/StreamingAssets/Data/GameRootConfig.json`。多场景有多个根时，确认导出内容来自实际启动场景。 [Inspector](CustomInspector/UPGameRootEditor.cs) · [持久化](CustomInspector/UPGameRootConfigPersistence.cs) |
| Player 构建 | `UPandaGF → 构建` 提供 Windows x64、WebGL、Android 入口，也可从编辑器代码调用 `FrameworkBuildPipeline.Build(...)`。读取启用场景、对齐 JSON 并调用 Unity 构建。环境变量 `UPANDA_VERSION` 可设置版本。目标平台 AB 需要独立生成。 [构建入口](Build/FrameworkBuildPipeline.cs) · [配置校验](FrameworkConfigBuildValidator.cs) |
| AB 工具 | `UPandaGF → AB包工具 → AssetBundle Browser` 分组、构建、检查、生成清单与部署。 [使用说明](AssetBundleTools/README.md) |
| 编码规范 | `UPandaGF → 框架 → 校验编码规范` 检查部分直接输入、资源和对象查找调用。`FrameworkConventionValidator.Validate()` 返回命中数。检查基于文本规则，程序集可构建性仍需用 Unity 验证。 [规则](EditorConfig/FrameworkConventionValidator.cs) |
| 工具配置 | `UPandaGFConfig` 在宿主 `Assets/Editor/EditorConfig` 下读写工具 JSON。 [工具配置](EditorConfig/UPandaGFConfig.cs) |

## 内容和代码生成

| 工具 | 入口与边界 |
| --- | --- |
| Excel | `UPandaGF → Tools → Excel配置工具`。按约定的表头、类型和主键生成数据类、容器与二进制表。当前写入器的 `bool` 宽度与旧读取器不一致，含该字段的表需要验证生成物。 [ExcelTool.cs](Excel/ExcelTool.cs) · [数据读取](../Runtime/Manager/StorageDataMgr/README.md) |
| UI | 从 `UPandaGF → Tools → UI` 或 GameObject 右键菜单生成 UI 脚本。当前自动绑定依赖编译和延迟回调，生成后核对组件挂载、字段绑定和监听器。 [UIAutoGenerator.cs](UIEditor/UIAutoGenerator.cs) |
| HybridCLR | `UPandaGF → Tools → HybridCLR DLL Copy Tool` 按规则复制 DLL，可生成 `.dll.bytes`。启用清空时，规则会先删除目标目录中的文件。该工具用于准备拷贝产物，程序集生成和 AOT 元数据由宿主的 HybridCLR 流程处理。 [DLLCopyConfigWindow.cs](HyBridCLREditor/DLLCopyConfigWindow.cs) |
| Cubemap | `UPandaGF → Tools → 立方体纹理生成工具` 按相机六面渲染输出 Cubemap。覆盖同路径时会删除旧资产再创建，已有 GUID 引用需要核对。 [CubemapTextureGenerator.cs](CubemapTextureGenerator.cs) |
| 路径与碰撞体 | 资源路径工具复制 Resources 路径、完整路径或 GUID。目录工具打开常用位置。碰撞体生成器从 Renderer 或 Mesh 拟合多个 Collider 类型，快速模式会替换现有碰撞体。 [EditorTools](EditorTools/) |

## 模块工具

- 日志菜单提供编译宏、诊断、自检和 uGUI 面板生成。Reporter 的编辑器钩子也会在编译结束后生成 `StreamingAssets/build_info`，包含机器名和时间。 [日志模块](../Runtime/Manager/DebugSystem/README.md) · [BuildInfo](PLogger/ReporterEditor.cs)
- 事件系统有订阅调试窗口与自检。交互评分有流程校验器、Inspector、自检、日志复盘和演示生成。任务系统提供任务资产、UI 与 3D 演示安装器。 [事件工具](CustomInspector/EventCenterModule/) · [评分工具](CustomInspector/InteractiveTaskScoringSystem/) · [任务工具](CustomInspector/TaskSystem/)
- 对象池、回放、描边、载具可从 `UPandaGF → Runtime` 下对应菜单生成示例。可新建场景或向当前场景追加内容。描边模块另有材质修复工具。 [示例索引](../Samples/README.md)
- `EditorStu` 的控件备忘录和开发案例展示 IMGUI、SerializedObject、Undo、拖放、菜单和资产工作流。 [EditorStu](EditorStu/)
