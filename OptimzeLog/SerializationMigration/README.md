# 旧序列化格式迁移

日期：2026-11-06。本轮在前两次生命周期修复基础上完成旧序列化入口迁移，不改业务场景、已有预制体或 Excel 表格二进制布局。

## 完成范围

| 入口 | 新写入 | 旧数据处理 |
|---|---|---|
| AB 分类工具 → AssetsLoader / UPGameRoot | 共用 AssetReferenceSerializer、版本 1 字段图、可选 AES | 旧清单只读兼容；工具再次生成时保留原始备份并提交新文件 |
| BinaryDataMgr.Save / Load | 保留 API 和扩展名，加入类别/版本/类型/长度/SHA-256 | Load 成功后备份并迁移；直接 Save 覆盖旧格式也备份；失败保留原内容 |
| AssetBundleBrowserBuild.dat | 版本化构建配置 | 下次打开工具自动迁移，保留选项、枚举及路径 |
| AssetBundleBrowserInspect.dat | 版本化检视配置 | 自动迁移私有路径/文件夹/忽略项集合；损坏配置不在关闭时覆盖 |
| ReplaySystem 旧 DTO | 继续使用现有版本 2 JSON 写入 | 移除 BinaryFormatter / LegacyBinder 读取，改为受限 NRBF 数据记录读取；不覆盖旧录制文件 |

Runtime / Editor 的生产源码已不再调用 BinaryFormatter.Serialize / Deserialize。BinaryFormatter 仅留在测试及临时 Editor 夹具生成器，用于产生真实的旧数据。

## 实现选择

没有直接把原对象交给 JsonUtility：通用存档和清单包含 Dictionary、私有字段、引用共享与循环关系，不能通过静默忽略这些成员完成迁移。

新增 VersionedDataSerializer、FieldGraphCodec、LegacyNrbfReader，以及 AB 编解码/结构检查。新字段图显式记录值类型、对象 ID、引用、字段和容器；类别与根类型都校验，根/泛型字段标识不包含程序集版本，不增加外部包依赖。

旧 NRBF 按记录读取，只有调用方声明/注册的类型参与恢复，不根据文件加载程序集，也不执行 DTO 构造或序列化回调。长度、记录数量、数组数量和深度在分配/递归前限制。旧清单 GetBuffer 的零填充尾部保留兼容，非零额外数据拒绝。

读取器参照 Microsoft 的 [MS-NRBF 记录与结构规范](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-nrbf/ca3ad2bc-777b-413a-a72a-9ba6ced76bc3)、[成员类型信息](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-nrbf/aa509b5a-620a-4592-a5d8-7e9613e0a03e)及[长度与类型限制说明](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-nrbf/9cc35343-b8dc-440c-8c3a-2616457cbd0f)。旧 DateTime 按原始 ticks/kind 解码，不套用新格式使用的 ToBinary/FromBinary 规则，参见 [DateTime 布局](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-nrbf/f05212bd-57f4-4c4b-9d98-b84c7c658054)。

## 数据保护与兼容性

1. 新内容先完整编码；迁移读取后还会复验新内容，随后保存原始 `.legacy.bak` 并进行可恢复提交。
2. 已有备份永不覆盖；不同历史内容使用 `.legacy.<guid>.bak`。AES 备份保留改写前的原磁盘字节，原文件加密时备份仍是密文。
3. 迁移前检查源文件未变化。未知版本、损坏数据、字段/根类型不符、非法清单引用及不支持布局不改写源文件。
4. 清单的包名、加载位置、资源引用、大小、摘要和相对路径由生成端/运行端共同检查；热更清单文件读取增加大小上限。
5. Runtime 的 AB 清单读取及旧回放读取不隐式写盘。客户端持续兼容旧发布产物，清单生成端升级后才生成新格式。
6. 新运行端能读旧/新格式，旧运行端不能读新格式；发布新清单前必须安排运行端升级或按客户端版本分发兼容清单。

保留的限制：自定义 ISerializable、任意系统对象、自定义比较器、非支持容器及非零下界数组需专用转换；多态具体类型需注册。DTO 构造/回调不执行，新增字段为 CLR 默认值。旧夏令时歧义标记明确拒绝，避免悄悄改变时间。文件/图规模限制和更详细 API 见 [序列化模块约定](../../Runtime/Manager/StorageDataMgr/Serialization/README.md)。

## 验证结果

环境：Unity **2022.3.48f1c1、Windows x64、Mono Development Player**。隔离工程位于项目根目录 `Temp/LifecycleAuditProject`，未关闭用户编辑器。

| 验证 | 结果 |
|---|---|
| 正式 PlayMode 回归 | **151/151 通过，0 失败，0 跳过**；59 项本轮迁移用例，92 项前轮用例 |
| 数据等价 | 私有字段、枚举、Unicode、各值类型、字典比较器、共享/循环引用、多态、矩形数组、泛型标识及 DateTime 三种 Kind |
| 迁移与失败 | 精确备份、重复读取不重复备份、已有备份不覆盖、直接 Save、源变化检测、只读兼容、未知版本、截断、篡改、非法数组/引用/类型、禁止序列化回调 |
| AB 两端 | 旧/新清单、旧/新 AES 包裹、加密开关切换、资源关系校验、无效清单不替换有效文件 |
| 编辑器缓存 | 两个实际类型的迁移测试；另复制项目真实 Library 缓存，验证所有字段的新编码一致、原始备份一致、源文件未被验证过程修改 |
| 独立 Windows Player | 构建成功；实际运行 **PASS，退出码 0**，覆盖旧/新存档、readonly 字段、备份、引用图、比较器、本地时间、Unity 值、公共存档 API、清单 AES 与旧/JSON 回放 |
| 原工程编译 | `dotnet build UPandaGF.Editor.csproj --no-restore`：0 错误、7 警告 |
| 文件检查 | 差异空白、C# UTF-8 BOM + CRLF、新文件 meta、GUID 唯一性及测试临时目录清理 |

本目录结果：

- [regression-results.xml](regression-results.xml)：最终 151 项回归。
- [editor-cache-results.json](editor-cache-results.json)：真实缓存副本验证元数据，不包含缓存中的用户路径内容。
- [player-smoke-result.txt](player-smoke-result.txt)：独立 Player 结果。
- [player-smoke.cs.txt](player-smoke.cs.txt)、[player-builder.cs.txt](player-builder.cs.txt)：临时驱动与旧数据夹具生成源码，不参与正常框架编译。

正式测试源码：`Tests/PlayMode/SerializationMigrationTests.cs`。项目根目录最终临时记录：`Temp/serialization-verified.log`、`Temp/serialization-player-build-verified.log`、`Temp/serialization-player-smoke-verified.log`。Player 在 `Temp/LifecycleAuditProject/SerializationBuild/`，以 `-batchmode -nographics` 运行。真实缓存验证优先使用编辑器已保留的旧 `.legacy.bak` 作为只读来源，在临时副本上转换；不改写该来源。

尚未验证 IL2CPP、Android/iOS/WebGL、全部第三方 DTO 布局、长期磁盘/内存曲线与断电耐久性。备份没有自动回收；继续遵守 FileUpdateTransaction 的单应用独占目录约定。
