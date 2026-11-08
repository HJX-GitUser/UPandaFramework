# PublicMono 帧驱动与协程

`PublicMono` 是主线程上的常驻 MonoBehaviour。非 MonoBehaviour 模块可以订阅 Unity 的三类帧消息，也可把 `IEnumerator` 协程包装成可等待的 Task。音频和应用计时器通过它获得更新。 [源码](PublicMono.cs) · [音频](../../Audio/AudioMgr/README.md) · [应用服务](../Services/README.md)

## 注册和注销帧回调

| Unity 消息 | 注册 | 注销 |
| --- | --- | --- |
| Update | `AddUpdateListener(action)` 或 `AddUpdateListener(action, owner)` | `RemoveUpdateListener(action)` |
| FixedUpdate | `AddFixedUpdateListener(action)` 或 `AddFixedUpdateListener(action, owner)` | `RemoveFixedUpdateListener(action)` |
| LateUpdate | `AddLateUpdateListener(action)` 或 `AddLateUpdateListener(action, owner)` | `RemoveLateUpdateListener(action)` |

注册与注销应配对，并使用相同的方法引用或保存同一个 lambda 委托。传入 Unity owner 后，该对象销毁时会自动清理。owner 禁用时仍需主动注销。没传 owner 的委托由驱动强引用。

```csharp
private PublicMono driver;

private void OnEnable()
{
    driver = PublicMono.Instance;
    driver?.AddUpdateListener(Tick, this);
}

private void OnDisable()
{
    if (driver != null)
        driver.RemoveUpdateListener(Tick);
}

private void Tick()
{
    // 更新轻量的主线程状态。
}
```

同一委托重复登记会被忽略。派发中新增的回调从下一轮开始，派发中移除的回调在本轮剩余位置也会跳过，单个回调异常会记录并隔离。`ClearAllListeners()` 会移除三类监听，框架已经使用同一驱动时只注销自己登记的回调。旧代码中带拼写差异的 FixedUpdate 和 LateUpdate 方法仍保留兼容入口。 [帧监听实现](PublicMono.cs)

## 等待协程

```csharp
await PublicMono.Instance.RunCoroutine(
    LoadRoutine(), timeoutSeconds: 10f, cancellationToken: token);
```

`RunCoroutine` 可以只传迭代器，也可以加超时、取消令牌或两者。超时按实时时钟推进，返回 `TimeoutException`。预先取消、宿主停用、宿主销毁或托管停止会令任务取消。异常会使任务失败。嵌套 yield 的迭代器由同一驱动接管，使其异常能传到 Task，并在完成或停止时释放迭代器。调用入口和 Unity 对象操作应在主线程；其他线程触发 token 取消时只置标志，实际停止在主线程进行。

停止托管协程可取消自己的 token。要停止驱动上的所有协程，使用静态类型为 `PublicMono` 的 `StopAllCoroutines()`。若通过 `MonoBehaviour` 引用调用原生停止方法，原生协程停止后，托管 Task 可能仍未结束。无超时且无取消的协程也可能因其自身条件一直未满足而继续等待。 [协程示例](../../../Samples/Coroutines/HowUseMonoMgr.cs) · [协程和帧派发回归](../../../Tests/PlayMode/CoordinationRegressionTests.cs)
