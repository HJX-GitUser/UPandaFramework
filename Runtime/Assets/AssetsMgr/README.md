# 资源加载与平台文件

新业务通过 `UPGameRoot.Services.Assets` 获取资源租约，通过 `Services.Scenes` 切换场景。所有会触碰 Unity 资源的调用放在主线程，并先等待框架就绪。

## 入口与路径

| 入口 | 路径 | 生命周期 |
| --- | --- | --- |
| `IAssetService.AcquireAsync<T>`，`Resources` | 相对任意 `Resources` 目录，不写扩展名，例如 `UI/InventoryIcon` | 释放 `AssetLease<T>` 或交给作用域 |
| `IAssetService.AcquireAsync<T>`，`AssetBundle` | 清单中的项目资源路径，例如 `Assets/UI/InventoryIcon.png` | Editor 直读模式走 AssetDatabase，AB 模式持有包及依赖 |
| `StreamingAssetsLoader.LoadTextFileAsync` / `LoadBinaryDataAsync` | 相对 `StreamingAssets`，例如 `Data/GameRootConfig.json` | 直接返回字符串或字节数组 |
| `ISceneService.LoadAsync` | 优先使用完整场景路径 | SceneService 将场景包租约交给场景作用域 |

```csharp
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UPandaGF;

public static class IconLoading
{
    public static async Task<Sprite> LoadIntoAsync(
        UPGameRoot root, ILifetimeScope scope, CancellationToken token)
    {
        await root.WaitUntilReadyAsync(token);
        var lease = await root.Services.Assets.AcquireAsync<Sprite>(
            "UI/InventoryIcon", AssetLoadMethod.Resources, token);
        return scope.Own(lease)?.Asset;
    }
}
```

资源找不到时，租约入口可返回 `null`。保留资源供多个独立持有者使用时，可用 `Retain()` 取得自己的租约。场景作用域结束或持有者调用 `Dispose()` 后，应停止使用对应资源。取消等待不会强制终止 Unity 原生请求，迟到的租约会自动归还。相关契约见 [AssetLease](../../Contracts/AssetLease.cs)、[AssetService](../AssetService.cs) 和 [场景服务](../../Core/Services/README.md)。

## Resources 缓存与旧接口

`ResourcesLoader.Instance.Load<T>`、`LoadAsync<T>` 及接收 `Type` 的重载仍可用。缓存键由路径、完整类型身份和 API 形态组成，泛型与 `Type` 版本各自计数。每次成功的旧式加载都应配对调用同一形态的 `UnLoadAsset`。重复的异步请求共享加载，失败回调返回 `null`，下次可以重试。命中缓存时回调可同步执行。

`UnLoadAsset(..., isDel: false)` 在计数为零时保留缓存，`isDel: true` 尝试移除缓存。加载器不会主动销毁 Prefab 实例。`UnLoadUnUseAssets` 清理无引用的缓存并触发 Unity 的未使用资源回收。`ClearDic` 会取消等待并重置全部缓存计数，应在整体关闭时调用。`GetDiagnostics()` 提供缓存条目、在途条目和引用数，Reporter 可展示这些计数。 [ResourcesLoader.cs](ResourcesLoader.cs)

## StreamingAssets 与平台存储

- Windows 等桌面平台的 Task 读取用文件 IO，Android 和 WebGL 用 UnityWebRequest。Android 的 APK 内部资源及 WebGL 的 URL 不适用同步 `File.Exists`。在这两个平台使用异步 API，`CheckFile` 的返回值只表示可以继续尝试读取。`StreamingAssetsLoader` 的读取没有超时与取消参数。 [StreamingAssetsLoader](StreamingAssetsLoadTool.cs)
- `AssetLocation.Combine` 接收原始相对文件名，URL 路径按段编码，文件路径保留原始文件名。不要预先编码空格、`#` 或 `%`。`AssetLocation.AsUrl` 可把本地文件路径转换为文件 URL。 [AssetLocation.cs](AssetLocation.cs)
- `PlatformStorage.AvailableSpaceAsync` 在 Windows 查询磁盘、在 Android 调用 `StatFs`、在 WebGL 查询当前站点的存储配额。WebGL 的同步文件写入在需要跨刷新保留时，应从 Unity 主线程等待 `PlatformStorage.FlushAsync()`。浏览器拒绝存储、配额不足或离开页面都可能影响提交。Android 的空间查询与 WebGL 的存储桥从 Unity 主线程调用。 [PlatformStorage.cs](PlatformStorage.cs) [WebGL 存储桥](../../Plugins/WebGL/UPandaStorage.jslib)
- `PlatformTasks.Delay` 在桌面使用 `Task.Delay`，在 WebGL 通过 Unity 帧循环等待真实时间。WebGL 的文件校验和复制仍占用主线程。 [PlatformTasks.cs](PlatformTasks.cs)

## 包和文件提交

[AssetBundle 加载](AssetBundleMgr/README.md) 说明依赖、缓存与包租约。[资源入口](../SourcesLoadMgr/README.md) 说明 Editor 和 AB 模式的初始化与场景路由。[启动热更](../HotUpdate/README.md) 使用 `FileUpdateTransaction` 在独占目录中备份、提交和恢复文件。旧 `FileUtility.SaveAsAndDeleteOriginal` 只执行复制再删除，适用于独立文件的简单转移。 [事务实现](FileUpdateTransaction.cs) [FileUtility.cs](FileUtility.cs)

通过 Unity Test Runner 运行 [资源回归](../../../Tests/PlayMode/FrameworkRegressionTests.cs) 与 [平台兼容测试](../../../Tests/PlayMode/PlatformCompatibilityTests.cs)。发布前用目标 Player 验证随包文件实际可读取。WebGL 的持久化验证应包含页面刷新后的读回。
