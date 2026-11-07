# AssetBundle 打包与发布工具

菜单 `UPandaGF → AB包工具 → AssetBundle Browser` 打开框架扩展的 AssetBundle Browser。它通过 Unity 的 AssetDatabase 和 BuildPipeline 管理资源分组、构建、查看包内容，并生成运行时使用的资源清单。 [窗口入口](AssetEditor/AssetBundleBrowserMain.cs)

| 页签 | 用途 |
| --- | --- |
| Configure | 查看、分配 AssetBundle 名和 Variant，检查包内资源及依赖。 |
| Build Set | 选择目标平台、输出目录与构建参数，配置主包和各包的加载位置。 |
| Inspect | 检查磁盘上的包和其中的资源。 |
| UpLoadAB | 复制到本地部署目录，或通过 FTP、HTTP 工具上传。 |

## 生成一套可用资源

1. 在 Configure 中分配包名。首个业务场景若通过 AB 加载，确认它也在对应包内，并记录 `Assets/.../Scene.unity` 完整路径。
2. 在 Build Set 中确认目标平台和输出目录，使用 `Build All` 构建整套包。Unity 主包的名称取输出目录最后一级，例如 `AssetBundles/Android/Android`。
3. 检查主包与各包的 `loadPath`，把随包文件放进 `Assets/StreamingAssets/<LoadAssetPath>/`。`Build All` 后分类面板会刷新并生成清单，也可用「更新配置」重生成。清单位置是 `Assets/StreamingAssets/Data/assetData.assetref`。只登记工具确认已构建的包，主包缺失时跳过写入并保留旧清单。
4. 检查 `assetData.assetref` 和 Unity 主包来自同一批产物。特别是右键单包构建或只构建依赖子集后，目标目录可能同时留有旧包。发布前以全量构建产物重新核对依赖。
5. 在 `UPGameRoot` 使用 AssetBundle 模式。确认 `bundleTarget`、`LoadAssetPath`、`remoteURL` 和 AES 选项与这次产物相符。先在目标平台验证关闭热更时的随包启动，再验证启用热更的配置。

[构建与分类](AssetEditor/AssetBundleClassificationWindow.cs) · [AssetDatabase 数据源](AssetEditor/AssetBundleDataSource/AssetDatabaseABDataSource.cs) · [根配置](../../Runtime/Manager/GameRoot/README.md)

## 清单与路径

| 内容 | 作用 |
| --- | --- |
| Unity 主包 | 内含 `AssetBundleManifest`，运行时据此解析包依赖。 |
| `assetData.assetref` | 记录主包、已构建包的名称、来源、大小和 MD5，以及资源完整路径到包和资源名的映射。资源名取去除扩展名的文件名，同包内避免使用重复名称。当前写入版本化格式。 |
| `StreamingAssetsPath` | 从 `StreamingAssets/<LoadAssetPath>/` 加载，资源随 Player 提供。 |
| `PersistentDataPath` | 从 `persistentDataPath/<LoadAssetPath>/` 加载，启用启动热更时按清单检查并下载。 |
| `RemotePath` | 从 `<remoteURL>/<LoadAssetPath>/` 直接读取。运行时加载这个来源的包时需要远端可用。 |

远程清单的请求路径固定为 `<remoteURL>/AssetBundles/assetData.assetref`，包文件路径包含 `LoadAssetPath`。同一服务器同时发布多个平台时，应分别配置远端根地址或发布命名空间，避免不同平台共享这个固定清单位置。清单加密启用时，工具与运行时要使用相同配置。包的 MD5 用于大小与内容比对，清单格式校验与发布来源的可信度需要分别考虑。 [数据结构](../../Runtime/Manager/GameRoot/SourcesLoadMgr/ABSourcesRelated.cs) · [清单编码](../../Runtime/Manager/StorageDataMgr/Serialization/README.md) · [热更实现](../../Runtime/Manager/GameRoot/HotUpdate/ContentUpdateService.cs)

## 本地复制与远端发布

- **本地复制**会递归清空选中的目标目录，再复制全部源文件。目标应为单独的部署目录，和源目录互不包含。操作前检查绝对路径和现有内容。
- **FTP** 的批量上传与清单上传分别发起。界面上的「上传AB包和清单」无法保证清单最后可见。需要有序发布时，可用独立发布步骤上传并验证包，再通过「只上传清单」切换入口。工具把 FTP 配置保存在 EditorPrefs。
- **HTTP** 页签打开 Nginx 上传窗口。它依赖外部服务提供接收 multipart 的接口，并会把单个文件读入内存。大包上传需要单独核对内存、请求限制和发布结果。

Player 构建菜单只负责 Player 和启动 JSON 对齐。资源包由本工具单独构建，当前构建校验只检查 JSON。发布验收应同时检查目标包、主包、首场景和启动配置。 [上传页签](AssetEditor/UpLoad/UpLoadABEditor.cs) · [FTP](AssetEditor/UpLoad/UpLoadFTP.cs) · [HTTP](AssetEditor/UpLoad/NginxUploader.cs) · [Player 构建](../Build/FrameworkBuildPipeline.cs)
