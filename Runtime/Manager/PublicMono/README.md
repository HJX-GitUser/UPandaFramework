# PublicMono — 帧调度中心（Update / FixedUpdate / LateUpdate + 协程托管）

`Runtime/Manager/PublicMono/PublicMono.cs`：让"不是 MonoBehaviour 的类"也能拿到帧更新与协程能力（纯 C# 单例 → 帧回调；协程 → 可 `await` 的 `Task`）。

## 1. 文件组成

| 文件 | 说明 |
|---|---|
| `PublicMono.cs` | 全部实现（含私有内部类型 `ListenerList` / `ListenerEntry` / `CoroutineJob`） |
| `HowUseMonoMgr.cs` | **用法示例**（文档性质，不参与业务逻辑，可整段删除） |

类型：`PublicMono : EagerMonoSingletonBase<PublicMono>`（全局命名空间）。场景里没有时自动创建 `PublicMono_EagerSingleton` 并 `DontDestroyOnLoad`；**退出播放时 `Instance` 返回 `null`**，调用方必须判空。

## 2. 设计要点（为什么不是"多播委托 + SetResult"这么简单）

1. **异常隔离**：多播委托是"顺序调用、遇异常立即中断"，一个监听器抛异常会让同一帧排在它后面的所有监听器全部被跳过 —— 等于"某个模块的小 bug 让整个框架停摆"。所以三个通道都改成逐个 `try/catch` 调用。
2. **监听器存在 `List` 里（不是多播委托）**：这样才能做到查重注册、按需注销、按 owner 存活状态自动清理、异常隔离、派发过程中增删安全。
3. **owner 自动注销**：注册时传一个 `UnityEngine.Object` 作为"存活依据"（MonoBehaviour 传 `this`）。owner 被销毁后，PublicMono 会在下一次派发时**自动跳过并移除**它 —— 避免"僵尸监听者"每帧抛 `MissingReferenceException`，也避免委托强引用让已销毁对象无法回收。
   不带 owner 时是**强引用**：必须在 `OnDisable`/`OnDestroy` 里显式注销，或用 `ClearAllListeners()`。
4. **幂等注册**：同一方法重复注册会被忽略并打警告（否则"每帧跑两遍"）。
5. **`RunCoroutine` 的 Task 一定会结束**：正常完成 / 协程异常 → Task 抛异常 / 宿主停用或销毁、`StopAllCoroutines` → Task 取消 / 超时 → `TimeoutException`。绝不会"没有异常也不返回"地永久挂起。

## 3. API 参考

### 3.1 帧监听

| 方法 / 属性 | 说明 |
|---|---|
| `AddUpdateListener(UnityAction fun)` | 注册帧更新（强引用，需手动注销） |
| `AddUpdateListener(UnityAction fun, UnityEngine.Object owner)` | 注册帧更新，owner 销毁后自动注销（推荐 MonoBehaviour 传 `this`） |
| `RemoveUpdateListener(UnityAction fun)` | 注销（**必须与注册时是同一个方法引用**） |
| `AddFixedUpdateListener` / `RemoveFixedUpdateListener` / `AddLateUpdateListener` / `RemoveLateUpdateListener` | 同上，各自带 owner 重载 |
| `ClearAllListeners()` | 清空三个通道（切场景 / 模块重置 / 单例 Release 时兜底） |
| `UpdateListenerCount` / `FixedUpdateListenerCount` / `LateUpdateListenerCount` | 监听器数量（调试用） |

> 旧名字 `AddFixedUpdatteEventListener` / `RemoveFixedUpdatteListener` / `AddLateUpdateEventListener` / `RemoveLateUpdateEventListener` 已标 `[Obsolete]`，仅为兼容保留，内部转发到新名字。

### 3.2 协程托管

| 方法 | 说明 |
|---|---|
| `RunCoroutine(IEnumerator routine)` | 启动并返回 `Task`（不限时、不可取消） |
| `RunCoroutine(IEnumerator routine, float timeoutSeconds)` | 超时（`<= 0` 不限时）会停止协程并抛 `TimeoutException` |
| `RunCoroutine(IEnumerator routine, CancellationToken token)` | 可取消；取消后 Task 抛 `OperationCanceledException` |
| `RunCoroutine(IEnumerator routine, float timeoutSeconds, CancellationToken token)` | 两者都支持 |
| `RunningCoroutineCount` | 仍在运行的托管协程数量 |
| `StartCoroutine(...)`（继承自 MonoBehaviour） | 不需要等待 / 不需要超时与取消时仍可直接用 |

> 取消令牌的回调只在工作线程上置一个标志，真正的停止动作在下一帧由主线程执行（避免在工作线程碰 Unity API），所以**取消最多延迟一帧生效**。

## 4. 使用示例

```csharp
// ① 普通 C# 对象：强引用注册，必须成对注销（注册/注销都用同一个命名方法）
PublicMono.Instance.AddUpdateListener(MyUpdate);
PublicMono.Instance.RemoveUpdateListener(MyUpdate);

// ② MonoBehaviour：带 owner 注册，对象销毁后自动注销
private void OnEnable()
{
    PublicMono mono = PublicMono.Instance;      // ⚠ 不要写 PublicMono.Instance?.AddUpdateListener(...)
    if (mono != null) mono.AddUpdateListener(OnUpdate, this);
}

// ③ 等待协程跑完（出错 / 超时 / 取消都能收到）
try
{
    await PublicMono.Instance.RunCoroutine(DoSomething(), 5f);
}
catch (TimeoutException) { /* 超时 */ }
catch (OperationCanceledException) { /* 宿主被销毁或被取消 */ }
catch (Exception e) { /* 协程内部出错 */ }
```

完整示例见同目录 `HowUseMonoMgr.cs`（含可直接挂到物体上试跑的 `PublicMonoSampleBehaviour`）。

## 5. 注意事项与已知限制

- **lambda / 闭包注销不掉**：`AddUpdateListener(() => {...})` 之后 `RemoveUpdateListener(() => {...})` 永远不生效（每次求值都是新的委托实例）→ 注册与注销必须用**同一个方法引用**（命名方法，或把委托存进字段）。
- **Unity 对象不要用 `?.` 判空**：`PublicMono.Instance?.AddUpdateListener(...)` 走的是 CLR 真 null 判断，绕过 Unity 重载的 `==`，对象"已销毁但引用还在"时会照样调用进去并报 `MissingReferenceException`。用 `var m = PublicMono.Instance; if (m == null) return;`。
- **不带 owner 的监听器不会自动清理**：切场景后遗留在链上的监听器会被跳过（若 owner 为空则无法判断存活）→ 请成对注销，或在切场景时 `ClearAllListeners()`。
- **取消最多延迟一帧生效**（见 3.2 说明）；超时精度同样取决于帧率。
- **`Task` 续体默认在同线程同步执行**（这是刻意的：保证 `await` 之后仍在主线程，可以安全调 Unity API，也与旧行为一致）。若改成异步续体（`TaskCreationOptions.RunContinuationsAsynchronously`），必须先确认调用方不依赖"续体在主线程同步执行"。
- 未提供：定时器 / 延迟调用 / 协程池、监听器优先级与执行顺序保证（按注册顺序执行，但 `owner` 自动清理会改变顺序）。

## 6. 与框架的关系

- **启动链**：`UPGameRoot.Init()` 用 `await PublicMono.Instance.RunCoroutine(debugerInit.Init())` 驱动日志系统初始化；`SceneMgr` 用 `PublicMono.Instance.StartCoroutine(...)` 跑场景加载协程。
- **帧驱动**：`AudioMgr` 等纯 C# 管理器通过 `AddUpdateListener` 拿到 `Update`（`AudioMgr` 在 `Dispose` 时注销，见 `Runtime/Manager/AudioMgr/README.md`）。
- **异常隔离是框架级保障**：正因为这里逐个 `try/catch`，各模块自己的 `Update` 里即使漏了异常保护，也不会连累其它模块。

## 7. 维护提示

- 三个通道的派发都走 `ListenerList.Invoke()`：**不要在派发过程中改 `entries` 的长度**（新增会被"本帧不生效"挡住；删除走 `removed` 标记 + 派发结束后压缩）—— 修改这块前先读懂 `Invoke/Remove/Clear/Compact` 四个方法。
- `FinishJob` 的顺序不能变：先置 `finished`（防重入）→ 移除出表 → 释放取消注册 → `StopCoroutine` → 最后才 `Set` 结果（`Set` 可能同步内联执行调用方的续体，续体里若再操作协程表必须看到一致状态）。
- `Awake` 之前不保证字段初始化器执行过：所有托管字段都通过 `EnsureInitialized()`（在 `OnAwake` 和每个注册入口调用）兜底，新增字段时请沿用这个习惯。
- 改动后建议用工程外的真实编译校验（`get_errors` 在 Unity 工程里可能是假阴性）：从 `Library/EditorInstance.json` 的 `app_contents_path` 取 `Managed` / `MonoBleedingEdge\lib\mono\4.7.1-api` 作参考程序集，用 `dotnet <sdk>\Roslyn\bincore\csc.dll` 编译 `Runtime/` 下全部源码。注意**不要**把 `NetStandard\ref\2.1.0\netstandard.dll` 加进响应文件（会让 corlib 解析崩掉，出现上千个 CS0518）。
