# 服务契约

UPandaGF.Contracts 只包含 Unity 数据类型、服务接口、AssetLoadMethod 和公开的 AssetLease<T>，不引用 Runtime、业务扩展或示例。
业务依赖 IAssetService、IAudioService、ISceneService 等窄接口，启动装配代码使用 IGameServices。
ILifetimeScope 表达取消与资源持有，具体实现位于 Runtime。
AssetLease<T> 是主线程共享凭据，Retain 增加持有者，Dispose 幂等归还；由具体后端提供释放回调。

[设计与迁移](../../OptimzeLog/P1Framework/README.md)
