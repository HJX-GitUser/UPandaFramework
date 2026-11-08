# 资源入口与场景来源

`AssetsLoader` 是 `UPGameRoot` 管理的资源入口，实现旧 `IAssetsLoader` 和场景准备接口 `ISceneAssetProvider`。使用前等待根节点就绪。新业务通过 `root.Services.Assets` 获取租约，通过 `root.Services.Scenes` 加载场景。 [启动入口](../../Composition/GameRoot/README.md) [资源所有权](../AssetsMgr/README.md)

## 应用级加载模式

| `UPGameRootConfig.method` | 初始化 | 资源路径 |
| --- | --- | --- |
| `Editor` | Unity 编辑器直接通过 `EditorSourcesMgr` 读 `AssetDatabase`，不需要 AB 清单 | 项目中的 `Assets/...` 路径 |
| `Assetbundles` | 使用启动热更传入的清单，否则读取 `StreamingAssets/Data/assetData.assetref`，随后加载主包和 Unity 的 `AssetBundleManifest` | 清单登记的 `Assets/...` 路径 |

`ABSourcesRelated.sourcesDic` 以项目资源路径为键，关联包名与包内资源名。`bundleInfo` 按包名登记各自的 `ABLoadPath`，`mainBundleInfo` 定位主包。生成端与运行端使用同一 [清单编解码](../Serialization/README.md)，开启 AES 时两侧的设置须一致。`AssetsLoader.IsInitialized` 反映底层 AB 上下文是否仍可用。Player 禁止 `Editor` 资源模式。 [AssetsLoader.cs](AssetsLoader.cs) [ABSourcesRelated.cs](ABSourcesRelated.cs) [EditorSourcesMgr.cs](EditorSourcesMgr.cs)

## 场景

调用 `root.Services.Scenes.LoadAsync(scenePath, token: token)`，其中 `scenePath` 是目标场景在项目中的完整资源路径。Editor 直读模式直接走 Unity，需要在 Build Settings 启用对应场景。AB 模式优先匹配清单并准备场景包，场景作用域持有租约。清单未登记、Unity 可直接加载时，按随包场景处理。清单支持唯一短名，重名应给完整路径。若一个场景同时登记在清单和 Build Settings，框架会准备 AB 并记录警告。Unity 场景加载仍收到调用方路径，因此短名与其他同名场景冲突时应传完整路径。 [场景分类](AssetsLoader.cs) [场景服务](../../Core/Services/SceneService.cs)

## 兼容接口

- `root.GetAssetsLoader()` 返回 `IAssetsLoader`。旧 `LoadAsync<T>` 或 `LoadAsync(path, type)` 支持 Task 和回调，查不到资源时返回 `null`。旧调用使用 AB 容器管理语义。需要与业务生命周期绑定时使用资源服务租约。
- 旧 `LoadSceneAsync` 转发给 `ISceneService`。准备回调在进入场景加载阶段时执行，完成回调仅在成功时执行。旧签名没有失败回调，需捕捉失败或取消时直接 await `ISceneService.LoadAsync`。
- `LoadAssemblyAsync` 在 Player 从 `TextAsset` 字节调用 `Assembly.Load`。Editor 查找当前已加载的同名程序集。程序集热更依赖项目按目标平台配置 HybridCLR 和 AOT 元数据，普通资源加载本身不会装配这些依赖。
- `Shutdown` 使初始化代次失效并清理场景与 AB 引用。AB 模式下 `ClearAB` 也会使入口失去就绪状态，调用方需重新装配才可继续使用。

在 Unity Test Runner 运行 [路径分类测试](../../../Tests/EditMode/ScenePathClassificationTests.cs) 和 [场景资源提供者测试](../../../Tests/EditMode/SceneServiceAssetProviderTests.cs)，再用实际首场景分别验证 Editor 直读与目标 Player AB 启动。 [兼容接口源码](IAssetsLoader.cs)
