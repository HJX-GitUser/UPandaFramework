# AssetBundle 加载

`ABLoadMgr` 是 AB 容器的主线程加载器，通常由 `UPGameRoot` 经 `AssetsLoader` 装配。业务可使用 `root.Services.Assets.AcquireAsync<T>(path, AssetLoadMethod.AssetBundle, token)` 获取资源租约，场景通过 `root.Services.Scenes.LoadAsync` 进入。Editor 直读模式会由资源入口处理。 [AssetsLoader](../../SourcesLoadMgr/README.md)

## 清单、依赖与加载位置

框架清单 `assetData.assetref` 给出主包、资源所属包和每个包的 `ABLoadPath`。初始化时主包必须包含 Unity 构建生成的 `AssetBundleManifest`，依赖关系从该对象读取。若传入了框架清单，每个依赖都必须有自己的来源登记。旧低层调用没有传清单时，依赖沿用目标包位置。

| `ABLoadPath` | 位置与加载方式 |
| --- | --- |
| `StreamingAssetsPath` | 随包资源，从 `StreamingAssets/<LoadAssetPath>/<包名>` 走 UnityWebRequestAssetBundle |
| `PersistentDataPath` | 本地可写目录，从 `persistentDataPath/<LoadAssetPath>/<包名>` 走 `AssetBundle.LoadFromFileAsync` |
| `RemotePath` | 从 `<remoteURL>/<LoadAssetPath>/<包名>` 走 UnityWebRequestAssetBundle |

同名且来源相同的在途包请求共享结果。已加载同名包从不同来源请求时，当前调用会失败。切换来源需要在消费者结束使用后重新初始化资源上下文。`requestTimeout` 默认 30 秒，`0` 表示不配置 UnityWebRequest 超时。 [ABLoadMgr.cs](ABLoadMgr.cs)

## 所有权与卸载

- `AcquireAsync<T>` 和 `AcquireBundleAsync` 持有目标包与传递依赖。最后一个相关租约释放后，管理器会尝试卸载无其他持有者的容器。主包保留用于依赖查询。
- 旧 `GetAssetBundle`、`LoadResAsync` 及相应回调接口也走同一套 pin 计数：加载时把包（含依赖）各 pin 一次，`IsLegacyPinned` 可查询，`ReleaseLegacyBundles()` 统一释放。pin 期间 `UnLoadAB` 返回 `false`，不再有“标为旧式使用就永不卸载”的旁路集合。旧回调可能同步完成，调用方要允许回调重入。
- `UnLoadAB` 遇到持有租约（含 legacy pin）或仍有相关加载时可能返回 `false`，业务可等待使用结束再请求卸载。实际卸载使用 `Unload(false)`，已加载资产继续由其使用者和 Unity 资源回收管理。
- `ClearAB` 和销毁会推进代次并完成旧等待者，Task 和回调可收到 `null`。Unity 原生文件和资源加载可能继续收尾，同名新请求等待旧操作退役。整体清理会重置现有租约计数，调用前应结束业务消费者。

`ABLoadProgressEvent` 提供逐包进度，`GetDiagnostics()` 提供缓存、在途资源、退役包、租约计数与 legacy pin 数。进度由 `ABLoadMgr.Update` 在存在在途加载时统一采样；原生加载本身使用 `AsyncOperation.completed` 完成回调，不再用 `Task.Yield` 逐帧轮询。租约计数包含共享依赖，只用于观察生命周期。 [ABLoadMgr.cs](ABLoadMgr.cs)

## 热更协调与验证

`HotUpdateFileGate` 协调框架加载和启动期文件替换。维护期间新 AB 加载异步等待。维护申请发现框架内正在加载时会失败。低层直接调用 Unity 加载 API 的操作不会加入该门禁。旧 `ABUpdata/AssetBundelUpdataMgr` 是独立 FTP 路径，会直接覆盖包且先删除部分旧文件。框架启动使用 [ContentUpdateService](../../HotUpdate/README.md)。 [HotUpdateFileGate.cs](HotUpdateFileGate.cs) [旧 FTP 入口](ABUpdata/AssetBundelUpdataMgr.cs)

使用 [AB 编辑器工具](../../../../Editor/AssetBundleTools/README.md) 为目标平台构建包与清单，确认依赖登记和清单 AES 设置与运行端一致。用 [生命周期回归](../../../../Tests/PlayMode/LifecycleRegressionTests.cs) 检查请求合并、混合来源依赖和清理收尾。切换平台后应在实际 Player 验证主包、首场景及一项有依赖的资源。
