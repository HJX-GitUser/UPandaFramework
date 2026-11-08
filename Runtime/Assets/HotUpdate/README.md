# 启动内容更新

`ContentUpdateService` 在 `UPGameRoot` 的 AB 初始化之前运行。它协调清单、下载与回退。`HotUpdateStorage` 管理暂存、空间预算、恢复与缓存账本。资源更新由 `enableAssetUpdate` 控制，未开启时仍会检查上次事务是否需要恢复。程序集热更是 [独立的加载入口](../SourcesLoadMgr/README.md)。

## 接入和发布

1. 用 [AB 编辑器工具](../../../Editor/AssetBundleTools/README.md) 按目标平台生成主包、依赖包与 `assetData.assetref`。
2. 在根节点选择 `AssetBundle`、填写 `LoadAssetPath` 和 `remoteURL`，并启用 `enableAssetUpdate`。清单 AES 设置须与生成端一致。
3. 将远端清单发布为 `<remoteURL>/AssetBundles/assetData.assetref`，包发布为 `<remoteURL>/<LoadAssetPath>/<包名>`。远端根地址应按平台或发布渠道区分，因为清单地址固定且清单没有平台字段。
4. 在目标 Player 验证随包首启、更新后的首场景，以及更新失败后退回旧版本。随包基线使用 `StreamingAssets/Data/assetData.assetref`。

| 来源 | 行为 |
| --- | --- |
| `StreamingAssetsPath` | 用随包文件，启动下载阶段跳过 |
| `PersistentDataPath` | 校验本地大小和 MD5，缺失或内容变化时下载 |
| `RemotePath` | 加载资源时直连远端，启动下载阶段跳过 |

上述规则也适用于主包。远端发布改动主包或依赖关系时，需让清单对应的主包和依赖在各自登记的位置可用。提交前的磁盘校验只覆盖 `PersistentDataPath`，其余来源的问题可能在资源初始化或使用时暴露。 [来源模型](../SourcesLoadMgr/ABSourcesRelated.cs) [更新入口](ContentUpdateService.cs)

## 下载、提交与恢复

更新先取得维护凭据并尝试恢复旧事务。清单下载到本轮独立暂存目录，解析后计算增量和空间预算。仅下载待替换的本地包。暂存包通过长度和 MD5 校验后，按包文件、账本、清单的顺序提交，最后清除恢复日志。下载或校验失败时，优先使用已验证的本地清单，否则退到随包清单。恢复失败会中止启动，避免在混合文件状态下装载资源。

下载超时由 `downloadBatchTimeout` 限制，`0` 关闭批次等待上限。批次只观察并取消自己的下载任务。热更会拒绝在已有 AB 加载的上下文覆盖内容。提交后 `LastHotUpdateStatistics` 记录清单、包下载、校验、提交与回收开销，可从根节点查看。

| 设置 | 含义 |
| --- | --- |
| `minimumFreeSpaceMB` | 默认 64 MiB，空间不足或无法查询时拒绝提交，`0` 关闭设备余量探测 |
| `maximumStagingMB` | 本轮暂存预算，`0` 不设逻辑上限，清单仍受 64 MiB 文件上限限制 |
| `maximumManagedCacheMB` | 登记缓存上限，`0` 不限，未知文件不计为可回收内容 |
| `retainedManifestCount` | 默认保留最近 2 份清单的引用，范围 1 到 10 |
| `collectUnusedBundles` | 默认只清理更新器登记且未被保护的旧包，外部修改的文件会保留 |

保留清单引用的作用是保护仍引用的文件。同名新包提交后，旧包内容没有形成完整的可回滚发行版本。恢复日志绑定原资源目录，改 `LoadAssetPath` 前先验证旧事务恢复。清单与包摘要用于完整性检查。发布来源认证仍依赖部署方案。

[HotUpdateSettings.cs](HotUpdateSettings.cs) [HotUpdateStorage.cs](HotUpdateStorage.cs) [文件提交](../AssetsMgr/FileUpdateTransaction.cs)

## 平台与验证

支持后台线程的平台将文件验证和提交放到工作线程。WebGL 仍在主线程执行文件阶段，提交等待 IndexedDB 写回。Android 通过 `StatFs` 查询余量，WebGL 查询站点配额。相关接口见 [平台存储](../AssetsMgr/README.md)。

在 Unity Test Runner 运行 [热更回归](../../../Tests/PlayMode/HotUpdateRegressionTests.cs)，重点验证同大小损坏包、提交失败恢复、缓存保护、预算、取消和恢复日志。用实际目标 Player 覆盖 HTTP 不可用、随包启动及 WebGL 刷新后读取。本模块的阶段性验证记录见 [热更存储审计](../../../OptimzeLog/HotUpdateStorage/README.md)。
