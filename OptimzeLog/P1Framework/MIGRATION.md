# P1 使用与迁移

## 业务接收服务

场景中的对象实现 IGameServicesConsumer，框架就绪后或场景加载完成时由根节点注入。动态实例由工厂显式注入。

```csharp
public sealed class PlayerAudio : UnityEngine.MonoBehaviour, UPandaGF.IGameServicesConsumer
{
    private UPandaGF.IAudioService audio;
    public void Configure(UPandaGF.IGameServices services) => audio = services.Audio;
    public void OnHit() => audio?.PlayUniqueSound("Audio/Hit");
}
```

只在装配入口等待 `await root.WaitUntilReadyAsync()`，然后取得 `root.Services`。可把某个窄接口传给纯 C# 业务对象进行测试。

## 场景资源、订阅、对象池

```csharp
using UPandaGF;
using UnityEngine;
using UnityEngine.SceneManagement;

var services = root.Services;
var scene = await services.Scenes.LoadAsync("Assets/Scenes/Arena.unity");
var scope = services.Scenes.GetScope(scene);
var prefab = await services.AcquireInScopeAsync<GameObject>(scope, "Actors/Enemy");
var pool = services.CreatePool(scope);
var enemy = await pool.GetObjAsync("Actors/Enemy");
scope.Subscribe(eventBus, eventListener); // EventBus，监听者实现 IEventListener<T>
// 或 scope.Subscribe(eventCenter, callback)，EventCenter 采用 UnityAction<T>。
// 延时、加载等异步操作必须传入 scope.Token。
await services.Scenes.UnloadAsync(scene);
// scope 已取消和释放；池的空闲与借出对象均销毁，事件退订。
```

如果自己 Instantiate(prefab.Asset)，需要把实例生命周期与凭据一起交给作用域，例如 Own(new DisposableAction(() => Object.Destroy(instance)))。
不要让一个场景的对象继续持有另一个已退出作用域的凭据；跨场景常驻对象应使用应用级持有者。
关闭时等待 Unity 的 Destroy 生效后，实例持有的资源凭据才会最终归还。

## UI 与菜单

```csharp
var scope = root.Services.Scenes.GetScope(gameObject.scene);
var panel = await UIManager.Instance.ShowPanelInScopeAsync<MyPanel>(scope, viewModel);
// 作用域退出自动关闭；同路径面板同时只能属于一个作用域。

var pause = root.Services.Pause.Pause("Menu");
var input = root.Services.Input.PushContext("UI");
// 菜单关闭：
input.Dispose();
pause.Dispose();
```

可使用 PauseMenuController 替代手动保存句柄，动态创建的控制器先调用 Configure(root.Services)。
作用域已取消的待打开面板返回 null；迟到加载归还资源，不会关闭新作用域后来重开的同路径面板。
应用级界面继续调用 ShowPanelAsync，不绑定场景作用域。

## 输入映射

业务使用 `services.Input.GetAxis("MoveVertical")`、`GetButtonDown("Interact")` 等动作名。
在 inputBindings 配置动作、context、axis 或正负按键；使用 `InputService.Bind` 可以显式重绑定，不修改冻结启动配置。
车辆与任务示例控制器已经迁移。其他业务的直接 Input 调用保留兼容；它们不会自动获得上下文隔离，按实际业务逐步迁移。

## 存档

```csharp
root.Services.Saves.Save("slot1", saveData);
var restored = root.Services.Saves.Load<MySaveData>("slot1");
```

新适配器默认使用 persistentDataPath/Saves，数据沿用现有版本化序列化。槽名不允许路径跳转。原 BinaryDataMgr/PlayerPrefs 接口仍保留，不自动迁移已有业务存档目录。

## 程序集与配置兼容

自有 asmdef 使用新契约时增加 UPandaGF.Contracts 引用；使用任务、回放、车辆等增加 UPandaGF.Extensions；使用示例增加 UPandaGF.Samples。
Core Runtime 本身不引用 Extensions/Samples。Editor 工具可以引用它们；剥离业务发布框架时应一并去掉相应 Editor 工具。

以下文件移动时保留 .meta GUID：

| 原位置 | 新位置 |
| --- | --- |
| Runtime/Manager/AssetsMgr/AssetLease.cs | Runtime/Contracts/AssetLease.cs |
| Runtime/Manager/GameRoot/PublicEnum.cs | Runtime/Contracts/PublicEnum.cs |
| Runtime/Manager/GameRoot/GameLaunchExample.cs | Samples/Startup/GameLaunchExample.cs |
| Runtime/Manager/PublicMono/HowUseMonoMgr.cs | Samples/Coroutines/HowUseMonoMgr.cs |
| Runtime/Manager/ObjPool/TestPoolMgr.cs | Samples/Pooling/TestPoolMgr.cs |
| Runtime/Game/StateMachine/StateMachineExample.cs | Samples/StateMachine/StateMachineExample.cs |
| Runtime/Game/InteractiveTaskScoringSystem/TaskInteractive/TaskTriggerExample.cs | Samples/Tasks/TaskTriggerExample.cs |

老 JSON 无需立刻补全新字段，默认 schemaVersion=1、bundleTarget=StandaloneWindows64、默认动作映射。其他目标平台应显式修改 bundleTarget 和对应包路径。
运行中修改 Config 副本不会生效；更改启动配置后重新创建根节点。音量、输入绑定和时间缩放通过对应服务接口调整。
旧原始资源加载继续显式管理包；新资源加载使用可释放凭据。不要把两个释放协议混为一谈。
