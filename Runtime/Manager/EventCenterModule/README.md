# 事件系统

| API | 用途 | 监听的持有者 |
| --- | --- | --- |
| `EventCenter.Instance` | 全局委托式事件中心，使用派生自 `EventArgBase` 的类型，支持优先级、一次性监听和 Unity owner。框架就绪通知使用这一套。 | 中心持有委托，业务主动退订，或让 owner 销毁后自动清理。 |
| `new EventBus()` | 可独立创建的类型化总线，发布 struct 消息给 `IEventListener<T>`。适合把事件限制在某个玩法实例或流程内。 | 总线用弱引用保存监听者，业务必须另外持有监听对象。 |

两个入口各自维护注册和分发。消息按调用时的泛型类型匹配，派生类型事件需要以相应类型订阅。 [EventCenter](EventCenter.cs) · [EventBus](EventBus.cs)

## EventCenter 接入

```csharp
public sealed class CoinsChanged : EventArgBase
{
    public int Total;
}

// MonoBehaviour 的 OnEnable / OnDisable
EventCenter.Instance.AddEventListener<CoinsChanged>(this, OnCoinsChanged, priority: 10);
EventCenter.Instance.RemoveEventListener<CoinsChanged>(OnCoinsChanged);

// 发布者
EventCenter.Instance.EventTrigger(new CoinsChanged { Total = 12 });
```

`AddEventListenerOnce<T>(callback)` 会先取消自身订阅再执行回调。优先级越高越先执行，同优先级按注册顺序。同一委托重复注册会被忽略。传 owner 处理销毁。希望组件禁用后立即停收时仍应显式退订。 [订阅和发布](EventCenter.cs)

## EventBus 接入

```csharp
public struct RoundEnded
{
    public int Score;
}

public sealed class RoundListener : IEventListener<RoundEnded>
{
    public void OnEvent(RoundEnded message)
    {
        UnityEngine.Debug.Log(message.Score);
    }
}

// 保持 bus 和 listener 存活到需要退订的时候。
var bus = new EventBus();
var listener = new RoundListener();
bus.Subscribe<RoundEnded>(listener);
bus.Dispatch(new RoundEnded { Score = 20 });
bus.Unsubscribe<RoundEnded>(listener);
```

监听者用 class 或 MonoBehaviour 实现。总线会拒绝值类型监听者，Unity 对象销毁或普通对象被 GC 后会清理对应弱引用。若已有场景或面板作用域，可用 `scope.Subscribe(bus, listener)` 或 `scope.Subscribe(eventCenter, callback)` 获得独立的退订凭据。作用域 Dispose 时自动退订，单独 Cancel 只发信号。 [泛型总线](EventBus.cs) · [作用域订阅](../Services/ScopeSubscriptions.cs)

## 分发和诊断

两种总线都在发布线程同步执行监听，锁保护注册表和快照。涉及 Unity 对象的消息由主线程发布。监听异常会记录，后续监听继续。分发中增删订阅沿用当前快照语义，`Clear()` 则通过代次终止剩余监听。Unity 菜单 `UPandaGF > Runtime > 事件系统` 提供事件调试窗口和事件模块自检。调试窗口可查看订阅者和 EventCenter 的派发次数。 [事件调试窗口](../../../Editor/CustomInspector/EventCenterModule/EventCenterDebugWindow.cs) · [编辑器自检](../../../Editor/CustomInspector/EventCenterModule/EventCenterSelfCheck.cs) · [派发回归](../../../Tests/PlayMode/CoordinationRegressionTests.cs)
