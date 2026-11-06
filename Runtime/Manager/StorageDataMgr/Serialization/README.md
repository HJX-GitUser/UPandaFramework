# 版本化数据与旧格式迁移

新 AB 清单、BinaryDataMgr 存档和 AB 工具缓存共用版本 1 字段图封装。Runtime / Editor 不再使用 BinaryFormatter 读写；旧 NRBF 数据通过受限记录读取器恢复为代码声明的数据类型。

## 文件封装

依次包含 `UPANDA_DATA\n`、Int32 版本号、数据类别、根类型标识、负载长度、32 字节 SHA-256 和字段图负载。字符串长度使用 BinaryWriter 的 UTF-8 长度前缀；数值按其明确类型编码。类型标识不含程序集名称或版本，泛型字段标识也使用同一规则。

类别用于隔离 `save`、`assetref`、`ab-build-settings` 和 `ab-inspect-settings`；类型、类别、版本、长度或校验不符时拒绝读取，不降级为另一种新格式。SHA-256 检测内容损坏，不提供来源认证。

上限为 64 MiB、对象图 100 万项、嵌套深度 64。旧回放读取保留其 128 MiB 文件上限，也应用记录/对象图限制。调用方应给长时间录制和大数据表另行安排预算。

## 支持的数据

- `[Serializable]` DTO 的实例字段，包括私有字段、readonly 字段、继承字段与自动属性的后备字段；静态和 `[NonSerialized]` 字段不保存。
- 基本数值、字符串、decimal、DateTime、TimeSpan、Guid、枚举、零下界数组、List 与 Dictionary。
- 引用共享、循环引用；字典支持默认比较器以及字符串的 Ordinal / OrdinalIgnoreCase。
- 新格式显式支持 Vector2/3/4、Quaternion、Color/Color32、Rect、Bounds、整数向量/边界和 Matrix4x4，只访问字段，不遍历 normalized 等计算属性。这些 Unity 值类型不保证能由旧 BinaryFormatter 产生，因此不伪造其旧文件兼容性。

对象类型由调用方根类型及其字段图确定；多态具体类型在读写前注册：

```csharp
VersionedDataSerializer.RegisterKnownType<MyDerivedData>();

BinaryDataMgr.Instance.Save(saveData, "slot1");
var loaded = BinaryDataMgr.Instance.Load<MySaveData>("slot1");
```

不从文件执行 Type.GetType / Assembly.Load，不调用 DTO 构造函数或序列化回调。新增字段缺失时是 CLR 默认值，字段初始化器也不会执行；需要默认值修复时在读取后显式验证和升级。

UnityEngine.Object、委托、自定义 ISerializable、任意系统对象、非支持容器和自定义字典比较器会明确失败，不静默丢字段。类型/字段改名、删除字段或自定义序列化布局需要保留旧 DTO 读取后显式映射。旧 DateTime 的夏令时歧义标记不自动近似转换，遇到时保留原文件并要求专用迁移。

## 文件迁移

- BinaryDataMgr.Load 保留公开接口。成功读旧文件后，先生成并复验新内容，再保留 `.legacy.bak` 并通过 FileUpdateTransaction 可恢复提交。迁移提交失败时仍返回已读数据，并保留原文件。
- Save 不要求先 Load；直接覆盖旧格式也会先备份。已有备份内容不同则另存 `.legacy.<guid>.bak`，不覆盖历史备份。
- `LoadFile<T>(path, kind, migrateLegacy: false)` 只读兼容；AB 运行端接收旧清单也是只读，不擅自改写随包或下载来源。
- AB 工具下次生成清单时写新格式并备份旧原文。AssetReferenceSerializer 统一生成/运行两端的验证和 AES 包裹；加密配置切换也保留旧原始字节。
- 两份 Library 编辑器缓存下次打开工具时自动迁移。读取损坏缓存时使用默认状态，但本次关闭不覆盖损坏原文件。
- 原回放继续写 `UPANDA_REPLAY:2` JSON；旧 DTO 读取改用同一受限 NRBF 读取器，不在读取时覆盖录制文件。

提交只适用于本应用独占的目录，不提供多进程协调或断电耐久性承诺。备份没有自动回收，请在确认升级后按项目策略管理。

## 部署与验证

新运行端可读旧/新格式，旧运行端不能读新格式。AB 工具、运行端及清单发布需要配套升级，已有旧客户端应继续获得其兼容清单。文件扩展名及 AB 清单字段/路径约定保持不变。

BinaryDataMgr.LoadTable / ExcelTool 的手写表格二进制流独立于通用 Save / Load，本轮不改其布局。

验证见 `OptimzeLog/SerializationMigration/README.md`。当前验证 Windows Mono；IL2CPP、裁剪和移动端需额外验证，反射使用的数据类型/字段须按项目的裁剪规则保留。
