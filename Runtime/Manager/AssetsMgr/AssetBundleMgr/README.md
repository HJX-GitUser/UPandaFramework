# ABLoadMgr 生命周期

## 统一加载

现有 Task 和回调 API 均通过同一包操作表加载。同名同来源的进行中请求共用结果；依赖按 ABSourcesRelated.bundleInfo 中各自的 loadPath 解析。未提供清单时，低层直接调用保留沿用目标位置的兼容行为；提供了清单但缺少依赖配置时明确失败。

缓存记录实际来源；同名包从不同路径/URL 加载时不会静默复用旧包，应先重新 Init。远程请求默认超时 30 秒，requestTimeout=0 可关闭该网络超时。

## 清理与取消

- ClearAB/销毁先推进代次并结束等待者，Task 返回 null，回调以 null 完成；不会把取消表现为永不回调。
- 只卸载本管理器拥有的包，不再调用 Unity 全局 UnloadAllAssetBundles。
- Unity 原生文件加载/资源加载不能中途取消时，后台操作继续收尾；结果不写回旧缓存。
- 同名新请求等待旧原生操作完成收尾，避免重复加载相同包。
- 资源原生加载仍在进行时，包容器延迟到原生操作完成后再 Unload(false)。
- UnLoadAB 对加载中的包或资源加载中的上下文返回 false，不会谎报已卸载。
- Init 开始新上下文，旧初始化不能覆盖新初始化；AssetsLoader 的 IsInitialized 会反映底层清理或销毁。

所有接口仍在 Unity 主线程调用。回调可能同步完成（命中缓存/立即取消），调用方需允许重入。

## 所有权边界

Unload(false) 保留已加载资产，既有实例/AudioSource 不因容器清理而被强制销毁。包仍通过现有 UnLoadAB/ClearAB 显式管理，本轮未引入自动的 AB 依赖引用计数或版本热切换；全局资源回收应由业务在关闭消费者后安排。

修复及验证见 `OptimzeLog/LifecycleAudit/FIXES.md`。
