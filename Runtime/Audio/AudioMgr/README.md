# 音频管理器

`AudioMgr` 实现 `IAudioService`，按需创建跨场景的 `AudioRoot`。背景音乐使用两路 AudioSource 交叉淡变，音效分为独占音效和可叠加音效。默认音源为 2D，淡变与叠加音效回收由注入的 `PublicMono` 驱动。`AudioMgr` 由 `GameServices` 创建并持有，不再有静态单例；依赖（`IAssetService`、帧驱动、`ILogService`）全部从构造函数注入。 [源码](AudioMgr.cs) · [服务契约](../../Contracts/Services.cs) · [帧驱动](../../Core/PublicMono/README.md)

## 通过服务播放

在框架就绪后使用注入的 `services.Audio`，从 Unity 主线程调用。Resources 路径省略扩展名，AB 模式使用清单登记的带扩展名全路径。

```csharp
services.Audio.ChangeBKValue(0.7f);
services.Audio.PlayBKMusic("Audio/Menu", fadeDuration: 0.5f);
services.Audio.PlayUniqueSound("Audio/Confirm");
```

| 类型 | 入口 | 行为 |
| --- | --- | --- |
| 背景音乐 | `PlayBKMusic(string, method, fadeDuration, restartIfSame)`、`StopBKMusic(fadeDuration)`、`ChangeBKValue(v)` | 相同路径默认不重播。切换加载失败时保持当前 BGM。淡变时间小于 0 使用默认值，等于 0 立即完成。 |
| 独占音效 | `PlayUniqueSound(string, method)`、`StopAllSound()`、`ChangeSoundValue(value)` | 新音效加载成功后替换旧音效。默认音量范围为 0 到 1。 |
| 叠加音效 | `AudioMgr.LoadSoundAndPlay(path, isLoop, method, callBack)` | 每次成功播放使用独立音源。回调在失败或取消时收到空值。循环音效通过回调保存句柄，业务结束时调用 `StopSound(source)`。 |

需要按音源暂停、播放已有 AudioClip，或使用 `PauseBKMusic/ResumeBKMusic`、`PauseUniqueSound/StopUniqueSound` 时使用具体类：`var audio = (AudioMgr)services.Audio;`。传入 AudioClip 的重载由调用方保有该 Clip；按路径播放由管理器取得和归还资源。 [具体 API](AudioMgr.cs)

## 循环音效与资源回收

```csharp
var audio = (AudioMgr)services.Audio;
AudioSource loopSource = null;
audio.LoadSoundAndPlay(
    "Audio/Engine",
    isLoop: true,
    callBack: source => loopSource = source);

// 在回调交付后，且本次播放结束时执行。
if (loopSource != null)
    audio.StopSound(loopSource);
```

循环音效的异步回调可能晚于请求方的生命周期。回调需要判断业务对象是否仍有效；若业务已结束，应立即停止收到的音源。`StopAllSound()` 同时停止独占和叠加音效并取消相关等待，不影响 BGM。叠加音效自然结束后自动回收。独占音效自然结束仍持有租约，直到停止、替换或管理器释放。

按路径加载的音效或 BGM 会在对应音源停止、替换、回收或管理器释放时归还租约，取消后的迟到结果也会归还。业务不要再用底层加载器重复卸载同一份持有。显式 `PauseBKMusic` 会暂停淡变推进；正常淡变使用 unscaled 时间。`GameTimeService` 的全局音频暂停使用 `AudioListener.pause`，该状态与 BGM 自身暂停分开。 [资源持有与取消](AudioMgr.cs) · [全局暂停](../../Core/Services/InputAndTime.cs)

## 关闭和验证

根节点关闭时由 `GameServices` 调用 `AudioMgr.Dispose()`：解绑帧更新、清空音源、归还引用并终止等待。自建实例使用 `new AudioMgr(IAssetService, PublicMono, ILogService)` 创建，并在创建者关闭时 Dispose；不要重复释放容器持有的实例。需要重建根节点时，先等待旧根的 `ShutdownAsync()`，避免旧关闭流程在新根装配后释放同一实例。测试可用 `new AudioMgr(assetService)` 注入替身。 [根的关闭流程](../../Composition/GameServices.cs) · [音频生命周期回归](../../../Tests/PlayMode/LifecycleRegressionTests.cs)
