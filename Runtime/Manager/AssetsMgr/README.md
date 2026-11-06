# ResourcesLoader 生命周期约定

所有 API 在 Unity 主线程调用，现有同步、回调、Type 和泛型接口保留。

- 同一路径、同一类型、同一种 API 共用加载中的请求。泛型与 Type API 各自计数，需配对释放。
- 失败移除缓存并对等待者回调 null，下次可重新请求；ClearDic/销毁也会结束尚未完成的回调。
- 回调前提交内部状态，逐个隔离回调异常，允许回调内重新加载或释放。
- UnLoadAsset 的 isDel=false 在引用为零时保留缓存，true 移除；引用计数不会降到负数。
- 预制体资源不执行 Destroy。实例由调用方销毁，移除缓存后的预制体由 UnLoadUnUseAssets/Unity 未使用资源回收流程处理。
- 缓存键包含完整类型身份，避免不同命名空间中的同名类型冲突。
- ClearDic 是全局清理，会重置全部引用记录；业务模块通常应逐项释放。

`FileUpdateTransaction` 是不依赖 Unity 的文件批次提交实现，由启动热更和二进制存档共用。仅用于应用独占的本地目录，不支持多进程并发提交。

## 内部持有凭据

对象池和音频使用内部 Acquire/AcquireAsync 与 AssetLease：保留对原缓存记录的身份校验，多个实例可共享一次加载。最后一个持有者释放时才归还计数；ClearDic 或加载器重建后旧凭据不能误减新记录。现有公开 Load/UnLoadAsset API 保留，业务仍需对这些手动 API 成对使用。

## 清单编解码

AB 清单的格式、结构检查与 AES 包裹统一由 AssetReferenceSerializer 处理；新写入使用版本化字段图，旧 NRBF 只读兼容。生成端升级或切换加密配置时保留旧原始文件。规则及 API 见 `Runtime/Manager/StorageDataMgr/Serialization/README.md`。

## 运行诊断

`ResourcesLoader.GetDiagnostics()` 在主线程返回缓存条目、加载中条目、总引用和零引用条目数。Reporter 信息页通过 `FrameworkDiagnostics` 展示这些计数；它们是缓存状态，不是资源内存估算。

## P0 资源入口关闭（2026-11-06）

AssetsLoader 新增 Shutdown()，使初始化代次和就绪状态失效，清除已有 AB 状态；销毁复用同一入口。底层 ABLoadMgr 继续处理迟到原生请求。热更执行已从 UPGameRoot 抽离至 ContentUpdateService。见 [P0 记录](../../../OptimzeLog/P0Startup/README.md)。
