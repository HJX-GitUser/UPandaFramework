# SceneMgr 场景切换

同步 LoadScene 和全部旧 LoadSceneAsyn 重载保留；新增拼写完整的 Task 入口：

```csharp
await SceneMgr.Instance.LoadSceneAsync(
    "Level1",
    LoadSceneMode.Additive,
    cancellationToken,
    timeoutSeconds: 30f);
```

Task 入口通过 PublicMono.RunCoroutine 等待，成功完成、失败抛异常、宿主停用/销毁或令牌取消进入取消态，超时抛 TimeoutException。名称为空或场景不在 Build Settings / 已加载 AB 中时明确失败，不再依赖协程末尾回调结束轮询。

旧回调入口只在成功时调用 Callback，逐个隔离多播回调异常；取消静默结束，失败由 PublicMono 记录一次。需要观测失败与取消时使用 Task 入口。原同步接口仍同步传播 Unity 或业务回调异常。

SceneMgr_SceneAsynLoadProgress 保留原格式。监听器异常由 EventCenter 隔离，不影响场景任务；取消后停止本次进度通知。不同加载任务的取消互不关联。

**取消与超时只终止等待和进度通知。Unity 已提交的原生场景加载不能撤销，仍可能完成，Single 模式仍可能切换场景。** 业务应据此安排加载流程，不把取消视为回滚或自动卸载。新入口默认不限时，与旧行为一致。

真实 Additive 场景成功、失败、回调异常及宿主销毁由 `Tests/PlayMode/CoordinationRegressionTests.cs` 验证。测试夹具临时生成唯一场景目录并恢复原 Build Settings；不改业务场景。详情见 `OptimzeLog/CoordinationAudit/README.md`。
