# GameObject 对象池

对象池以资源路径和加载方式区分条目。空闲实例按后进先出复用；池空时加载 prefab 并创建实例。同一条目共享一次预制体加载，池与借出实例分别保留共享租约。所有入口都在 Unity 主线程使用。池由 `IPoolService` 创建，依赖（`IAssetService`）由服务容器注入；`GameObjectPoolMgr` 不再有静态单例。 [管理器](GameObjectPoolMgr.cs) · [实例凭据](PoolInstanceLease.cs) · [池服务](../PoolService.cs)

## 选择池的生命周期

场景拥有的池通过服务创建。释放场景作用域会销毁该池的空闲实例和借出实例。需要跨场景复用时使用 `services.Pools.Shared`，它随服务容器关闭而释放；由使用方明确决定 `Clear()` 时机。池的层级收纳节点属于创建时的场景；借出的对象要跨场景存活，需要业务安排父节点或持久化。

```csharp
var scope = services.Scenes.GetScope(gameObject.scene);
var pool = services.Pools.CreatePool(scope);
pool.MaxCountPerKey = 16;
await pool.Prewarm("Props/Crate", 4);

var crate = await pool.GetObjAsync("Props/Crate");
if (crate == null)
    return;

// 使用结束后以同一路径归还。
pool.PushObj("Props/Crate", crate);
```

NoAssetBundle 模式使用 Resources 下不带扩展名的路径。AssetBundle 模式用清单中的带扩展名路径，调用异步入口。加载失败、取消或超时返回空，异步回调同样需要判空。 [场景池扩展](../../Composition/GameServices.cs) · [用法示例](../../../Samples~/Pooling/TestPoolMgr.cs)

## API 与行为

| API 或设置 | 行为 |
| --- | --- |
| `GetObjAsync(path, loadMethod)` | Task 入口，优先复用空闲实例。兼容重载为 `GetObjAsync(path, callback, loadMethod)`。 |
| `GetObj(path, loadMethod)` | 已标为过时的同步入口，只支持 Resources。注入资源服务的池没有可用缓存时，应改用异步入口或先预热。 |
| `PushObj(path, obj)` | 以原路径归还。拒绝重复、回调中重入或归还给其他仍有效的池。跨代实例归还时销毁。 |
| `Prewarm(path, count, loadMethod)` | 在容量限制内创建实例并归还。预热也会触发 `OnSpawn` 和 `OnDespawn`。 |
| `PooledCount(path)`、`TotalPooledCount`、`Contains(path)` | 查询空闲数量或条目存在性。相同路径下的不同加载方式会计入对应路径的查询。 |
| `Clear()`、`Dispose()`、`DisposeAllInstances()` | Clear 推进代次，取消等待并清空空闲实例，之后可重用。Dispose 终止该池，旧借出实例仍由原持有方处理。DisposeAllInstances 还销毁借出实例。 |
| `maxCountPerKey`、`loadTimeout`、`isOpenLayout` | 默认每条目不限空闲容量，默认加载超时为 15 秒，收纳层级默认开启。超时设置小于等于 0 时禁用池层超时。 |

## 组件和异常

将 `IPoolable` 实现挂在 prefab 根节点。新建与复用都在对象激活后调用 `OnSpawn`，回池时在对象停用前调用 `OnDespawn`。复用会恢复到实例首次交付时的局部 Transform；刚体速度、粒子、动画等状态仍由业务在 `OnSpawn` 中重置。对象被外部销毁时，实例凭据负责移除池记录并归还资源。租约还在使用中时无需调用底层卸载 API。 [池回调](IPoolable.cs) · [实例资源所有权](PoolInstanceLease.cs)

`OnSpawn` 抛异常会销毁本次对象并返回空。`OnDespawn` 抛异常时，手动归还的对象仍归调用者，可以修复后重试。预热过程中若 `OnDespawn` 抛异常，当前入口不会把未归还的对象交给业务；预热使用的回调应妥善处理自身错误。空闲池没有按时间自动收缩，使用场景池或在玩法结束时调用 `Clear` 可以明确回收。 [池实现](GameObjectPoolMgr.cs) · [资源、代次和重入回归](../../../Tests/PlayMode/LifecycleRegressionTests.cs)
