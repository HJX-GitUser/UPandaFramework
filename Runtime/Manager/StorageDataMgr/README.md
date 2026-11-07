# 存档、Excel 数据与 PlayerPrefs

本目录包含旧版通用存档、Excel 二进制表、PlayerPrefs 对象适配和版本化序列化。新业务通过 `IGameServices.Saves` 访问槽位文件。数据文件放在应用的可写目录，Excel 导出与发布要单独安排。

| 用途 | 入口 | 位置与格式 |
| --- | --- | --- |
| 游戏进度与存档槽位 | `root.Services.Saves` | 默认 `persistentDataPath/Saves/<slot>.save`，版本化字段图 |
| 已有二进制存档 | `BinaryDataMgr.Instance.Save` / `Load<T>` | 默认 `persistentDataPath/Data/<name>.binary`，兼容旧 NRBF |
| Excel 生成的查表数据 | `BinaryDataMgr.LoadTable<T, K>` / `GetTable<T>` | 按类型名读取 ExcelTool 生成的二进制表 |
| 简单设置与旧示例 | `PlayerPrefsDataMgr.SaveData` / `LoadData` | 每个字段拆成若干 PlayerPrefs 键 |

[版本化格式和迁移](Serialization/README.md) [服务实现](../Services/FileSaveService.cs)

## 槽位存档

```csharp
using System;
using System.Threading.Tasks;
using UPandaGF;

[Serializable]
public sealed class PlayerSave
{
    public int coins;
    public string lastScene;
}

public static class SaveExample
{
    public static async Task<PlayerSave> RoundTripAsync(UPGameRoot root)
    {
        await root.WaitUntilReadyAsync();
        var saves = root.Services.Saves;
        saves.Save("slot1", new PlayerSave { coins = 10, lastScene = "Workshop" });
        return saves.Load<PlayerSave>("slot1");
    }
}
```

槽名传纯文件名，不附扩展名。`Load<T>` 在槽位缺失时返回 `null`，无效槽名和读取失败会抛异常。调用方应决定读取失败时的提示与回退。`FileSaveService` 的写入使用事务，当前读入口直接解析槽文件。若进程在覆盖阶段被终止，仅调用 `Load<T>` 不会先重放恢复日志。需要手工排查此情况时，保留槽文件及其 `.recovery` 目录。WebGL 的同步写入需在主线程等待 `PlatformStorage.FlushAsync()` 才能检查浏览器写回结果。 [存档实现](../Services/FileSaveService.cs) [平台存储](../AssetsMgr/README.md)

旧 `BinaryDataMgr.Load<T>` 会先恢复事务，成功读到旧格式后尝试备份迁移。读失败记录日志并返回默认值。`BinaryDataMgr.Save` 与 `Load<T>` 的文件名应使用受控名称，旧接口没有新槽位服务的路径校验。两个入口的目录与扩展名不同，迁移旧玩家时应显式读旧槽、校验数据，再保存到新槽。 [BinaryDataMgr.cs](BineryMgr/BinaryDataMgr.cs) [初始化组件](BineryMgr/BinaryDataMgrInit.cs)

## Excel 二进制表

在 [ExcelTool](../../../Editor/Excel/ExcelTool.cs) 生成数据类、容器和 `.binary`。`K` 是生成的行类型，`T` 是有 `dataDic` 字段的容器类型。`LoadTable<T, K>()` 读入并缓存，之后使用 `GetTable<T>()`，同一个容器类型重复加载会遇到缓存键冲突。

工具把文件写到 `StreamingAssets/Data/BinaryData/`；运行时的可写目录为 `persistentDataPath/Data/BinaryData/`。桌面/主机首次启动会按文件长度自动安装表；Android/WebGL 的 StreamingAssets 不是普通目录，需要走索引清单或 AB 安装（P1）。生成端与读取端共用 `ExcelBinaryFormat` 的常量：bool 固定 4 字节，行数使用 `BEGIN_INDEX`，写入前截断文件。 [表读取实现](BineryMgr/BinaryDataMgr.cs) [格式常量](ExcelBinaryFormat.cs)

## PlayerPrefs 对象适配

`PlayerPrefsDataMgr` 按调用方键名、类型名、字段类型及字段名拼接键。支持 `int`、`float`、`string`、`bool`、枚举、数组，以及由这些类型或简单对象组成的具体泛型列表及字典；空引用会删除对应键。保存会调用 `PlayerPrefs.Save()`，读取缺失的键会用对应类型的默认值填充。用法见 [示例](PlayerPrefs/Test/PlayerPrefsDataTest.cs)。

读取通过公开无参构造和公开实例字段还原对象；静态字段不参与存档。字段改名会改变存储键，业务已有这类数据时应先验证旧键读回，再安排格式迁移。 [PlayerPrefsDataMgr.cs](PlayerPrefs/PlayerPrefsDataMgr.cs)
