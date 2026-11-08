# 版本化序列化与旧数据迁移

`VersionedDataSerializer` 负责普通存档、AB 清单、AB 工具缓存和热更缓存账本的版本化字段图。`AssetReferenceSerializer` 为清单补充结构校验和可选 AES 封装。Excel 二进制表使用 [单独的数据布局](../../Save/StorageDataMgr/README.md)。回放的新录制使用其自身格式，旧回放可走受限 NRBF 读取。 [序列化入口](VersionedDataSerializer.cs) [清单编解码](AssetReferenceSerializer.cs) [回放文件](../../Game/ReplaySystem/README.md)

## 数据模型

- DTO 使用 `[Serializable]`，记录实例字段，包括私有字段、继承字段、readonly 字段和自动属性的后备字段。静态字段与 `[NonSerialized]` 字段不参与保存。
- 字段可使用基本数值、字符串、`decimal`、`DateTime`、`TimeSpan`、`Guid`、枚举、零下界数组、`List<T>` 和 `Dictionary<TKey, TValue>`。支持共享引用和循环引用。字典支持默认比较器，以及字符串的 `Ordinal`、`OrdinalIgnoreCase`。
- 新格式显式支持 `Vector2/3/4`、`Quaternion`、`Color/Color32`、`Rect`、`Bounds`、整数向量和边界、`Matrix4x4`。编码只访问字段。旧 NRBF 兼容取决于旧运行端实际写过的类型布局。
- 多态的具体类型在读写前通过 `RegisterKnownType<T>()` 登记。`UnityEngine.Object`、委托、自定义 `ISerializable`、未支持的容器或比较器会被拒绝。

```csharp
using System;
using UPandaGF;

[Serializable]
public sealed class SaveData
{
    public int level;
    public string profile;
}

public static class SerializationExample
{
    public static SaveData RoundTrip(string path)
    {
        // path 由调用方选择，确保目录由应用独占。
        VersionedDataSerializer.SaveFile(path, new SaveData { level = 3 }, "save");
        return VersionedDataSerializer.LoadFile<SaveData>(path, "save");
    }
}
```

读入时按代码提供的根类型、字段图和已注册类型解析数据。DTO 的构造函数、字段初始化器和序列化回调不会执行。新增字段从 CLR 默认值开始，业务应在读回后补齐需要的默认值并验证。类型或字段改名、删字段及布局变化需要保留旧 DTO，通过专门映射迁移。 [字段图](FieldGraphCodec.cs) [旧格式读取](LegacyNrbfReader.cs)

## 文件格式与异常

文件头为 `UPANDA_DATA\n`，随后是 Int32 版本号、类别、根类型标识、负载长度、32 字节 SHA-256 和负载。当前版本为 `1`。字符串使用 UTF-8 长度前缀，根类型标识不带程序集名或版本。类别包括 `save`、`assetref`、`ab-build-settings`、`ab-inspect-settings`、`hotupdate-cache`，用于防止跨用途读取。新格式校验失败会直接报错，旧 NRBF 走独立的受限解析器。SHA-256 用来检查数据完整性。

普通文件上限为 64 MiB，旧回放输入上限为 128 MiB。对象图限制 100 万项、嵌套深度 64。`ReadFile` 在分配前检查文件大小，解码会验证长度与结构。对外部数据应明确文件所属类别和根类型。业务 DTO 在 IL2CPP 裁剪后仍需保留反射所需的字段与类型，框架 [link.xml](link.xml) 主要保留字典比较器相关成员。

## 迁移和发布

1. 旧格式存档使用 `LoadFile<T>` 读取。默认先恢复未完成的提交，读成功后生成和复验新数据，再以事务替换文件。备份使用 `.legacy.bak`，已有备份内容不同时使用带唯一标识的备份名。迁移失败时返回已读对象并记录警告。
2. 先检查数据再迁移时可传 `migrateLegacy: false`，它跳过旧格式重写，仍会执行已有恢复日志。要完全从现有字节做只读解析，可使用 `Deserialize<T>(bytes, kind, out legacy)`。
3. `SaveFile` 直接覆盖旧格式时也会保留旧数据。提交依赖应用独占目录，同一槽位的并发写入需要调用方串行化。备份由应用按保留策略管理。旧 NRBF 中无法明确解释的字段，例如特定 DateTime 夏令时歧义状态，需要专用迁移。
4. AB 运行端通过 `AssetReferenceSerializer.Decode` 读取新旧清单。AB 工具生成新清单时会使用新版封装，必要时保留旧文件或原加密内容。发布时让构建端与运行端使用一致的 AES 设置。新客户端可读取旧清单，仍在使用旧解析器的客户端需继续访问兼容清单。 [清单校验](AssetReferenceValidation.cs)

在 Unity Test Runner 运行 [序列化迁移测试](../../../Tests/PlayMode/SerializationMigrationTests.cs)，覆盖共享引用、损坏文件和备份迁移。事务回滚另见 [框架回归](../../../Tests/PlayMode/FrameworkRegressionTests.cs)。已有的 [迁移验证](../../../OptimzeLog/SerializationMigration/README.md) 与 [跨平台验证](../../../OptimzeLog/CrossPlatform/README.md) 为历史记录。发布项目仍需对自己的存档 DTO、目标裁剪级别、旧存档样本和清单版本做实际读回验证。
