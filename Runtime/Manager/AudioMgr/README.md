# AudioMgr — 音频管理器（BGM / 音效）

`Runtime/Manager/AudioMgr/AudioMgr.cs`：`UPandaGF` 命名空间下的纯 C# 懒加载单例（`LazySingletonBase<AudioMgr>`），负责背景音乐与音效的加载、播放与回收。

## 1. 文件组成

| 文件 | 说明 |
|---|---|
| `AudioMgr.cs` | 全部实现（含两个私有内部类型 `PlayingClip` / `SoundHandle`） |

依赖：`ResourcesLoader`（Resources 模式）、`IAssetsLoader`（AssetBundle 模式）、`PublicMono`（帧更新驱动）、`PLogger`（日志）。

## 2. 核心概念

**① 双路 BGM 交叉淡入淡出（cross fade）**

只有一路 `AudioSource` 时，给 `clip` 赋值会立刻打断当前播放，只能硬切；交叉淡变要求"新的淡入"和"旧的淡出"同时存在，所以 BGM 固定占两路：

- `bgmCurrent`：正在发声（或正在淡出）的那一路
- `bgmIncoming`：正在淡入的那一路（没有交叉淡变时为 `null`）
- 淡变进度用 `Time.unscaledDeltaTime` 推进（暂停游戏不冻结音乐）；`PauseBKMusic()` 期间淡变进度一起冻结
- 淡变进行到一半又切歌：会**立刻收掉"正在淡出的那一路"**，最多两路同时响，不会三路齐鸣
- 淡变结束后旧路会 `Stop()` + `clip = null` + 归还资源引用

**② 唯一音效 vs 叠加音效**

| | 组件 | 行为 |
|---|---|---|
| 唯一音效 | `uniqueSound` | 同一时刻只响一个；**新音效加载完成后**才顶掉上一个（加载失败不打断当前正在响的音效） |
| 叠加音效 | `soundList` 里的多个 `AudioSource` | `LoadSoundAndPlay`，可同时播放多个；播完自动回收（销毁组件 + 归还资源引用） |

**③ 资源引用计数**

`ResourcesLoader` 按"路径 + 类型"缓存并计数。本类每成功加载一次音频，就记一条 `PlayingClip { path, needRelease }`，在**切歌 / 回收 / 停止 / 释放**时调用 `UnLoadAsset<AudioClip>(path, isDel: true)` 归还一次计数；否则 `AudioClip` 会被缓存永久占用（旧实现的引用计数只增不减）。

释放顺序固定为：**先 `Stop()` + `clip = null` 解除引用 → 再归还计数**（否则 `Resources.UnloadAsset` 时资源仍被 `AudioSource` 引用）。

AssetBundle 模式按包整体卸载（`IAssetsLoader.UnLoadAB / ClearAB`），没有单资源接口，因此这类加载**不做**单资源释放。

**④ 请求序号（防竞态）**

`bgmRequestId` / `uniqueRequestId` 每次请求自增，异步回调先比对序号，不等则直接返回（并把已加上的引用计数还回去）。这样"连续快速切歌"不会被先到达的旧回调覆盖。

## 3. 常驻节点结构

```
AudioRoot (DontDestroyOnLoad)
  ├── BGM_A   ← AudioSource（交叉淡入淡出的一路）
  ├── BGM_B   ← AudioSource（另一路）
  └── SFX     ← 唯一音效 + 所有叠加音效都挂在这个节点上
```

节点在**首次调用对外 API 时**惰性创建（`EnsureSetup()`），并 `DontDestroyOnLoad`：切场景不销毁，避免 `soundList` / `bgmSources` 里的引用变成"假 null"，进而在 `Update` 里每帧抛异常。所有 `AudioSource` 都显式设置 `playOnAwake = false`、`spatialBlend = 0`（2D）。

## 4. API 参考

### 4.1 背景音乐

| 方法 / 属性 | 说明 |
|---|---|
| `PlayBKMusic(string path, AssetLoadMethod method = Resources, float fadeDuration = -1, bool restartIfSame = false)` | 异步加载后交叉淡入淡出；`fadeDuration < 0` 用默认值（默认 1 秒），`0` = 立即切换；加载失败保持当前 BGM 不变；同一路径且未在淡变时默认不重复加载 |
| `PlayBKMusic(AudioClip clip, float fadeDuration = -1, bool restartIfSame = false)` | 直接播放已加载好的 clip（资源由外部管理，不归还计数） |
| `StopBKMusic(float fadeDuration = -1)` | 淡出停止（默认 1 秒），`0` = 立即停止；会作废还在加载中的请求 |
| `PauseBKMusic()` / `ResumeBKMusic()` | 暂停 / 恢复（暂停期间淡变进度冻结） |
| `ChangeBKValue(float v)` | 设置 BGM 音量（`Clamp01`）；淡变中会在淡变过程中生效 |
| `IsBgmPlaying` / `IsBgmPaused` / `CurrentBgmPath` / `BgmVolume` / `DefaultBgmFadeDuration` | 查询 / 配置 |

### 4.2 音效

| 方法 / 属性 | 说明 |
|---|---|
| `PlayUniqueSound(string path, AssetLoadMethod method = Resources)` | 唯一音效：加载完成后顶掉上一个 |
| `PlayUniqueSound(AudioClip clip)` | 唯一音效（直接给 clip） |
| `PauseUniqueSound(bool isPause = true)` / `StopUniqueSound()` | 暂停 / 停止（停止会归还资源引用） |
| `LoadSoundAndPlay(string path, bool isLoop = false, AssetLoadMethod method = Resources, UnityAction<AudioSource> callBack = null)` | 叠加音效；播完自动回收；回调参数是音效组件，**失败时以 `null` 回调** |
| `StopSound(AudioSource source)` | 停止并回收指定音效（已播完/外部销毁的传进来也安全） |
| `PauseSound(AudioSource source, bool isPause = true)` | 暂停 / 恢复；**暂停中的音效不会被自动回收** |
| `ChangeSoundValue(float v)` | 设置音效音量，同时作用于唯一音效与所有正在播放的音效 |
| `StopAllSound()` | 停止全部音效（不含 BGM） |
| `PlayingSoundCount` / `SoundVolume` | 查询 |

## 5. 使用示例

```csharp
// 交叉淡入淡出切歌（1.5 秒）
AudioMgr.Instance.PlayBKMusic("Audio/Bgm_Main", fadeDuration: 1.5f);

// 暂停 / 恢复 / 淡出停止
AudioMgr.Instance.PauseBKMusic();
AudioMgr.Instance.ResumeBKMusic();
AudioMgr.Instance.StopBKMusic(0.5f);

// 唯一音效（点击音，同时只响一个）
AudioMgr.Instance.PlayUniqueSound("Audio/Click");

// 叠加音效（爆炸声可以叠着响），播完自动回收
AudioMgr.Instance.LoadSoundAndPlay("Audio/Explosion", isLoop: false, callBack: source =>
{
    if (source == null) PLogger.LogError("爆炸音效加载失败");
});

// AssetBundle 模式（需要场景里已有 UPGameRoot）
AudioMgr.Instance.PlayBKMusic("Assets/ArtAssets/Audio/Bgm_Main.wav", AssetLoadMethod.AssetBundle);
```

## 6. 注意事项与已知限制

- **加载失败会以 `null` 回调**：`ResourcesLoader` 在资源缺失 / 已被卸载时会 `Invoke(null)`，本类已判空并打错误日志，不会再把"空 clip"赋给播放组件（旧实现会因此静默打断正在播的音频）。
- **循环音效不会被回收**：`LoadSoundAndPlay(isLoop: true)` 需要调用方自己 `StopSound(source)`。
- **外部自行 `Pause()` 音效会被回收**：自动回收只认"没在播且不是本类暂停的"，所以请用 `PauseSound()` 而不是直接操作 `AudioSource`。
- **AssetBundle 模式不做单资源释放**（按包卸载）；`UPGameRoot` 缺失时会打错误日志并让回调收到 `null`。
- **音量不持久化**：跨启动保存请调用方自行写配置 / `EditorPrefs`。
- 未提供：`AudioMixer` 分组、3D 位置音效（`PlayClipAtPoint`）、自定义淡变曲线、播放速度 / 音高。
- **不要用 `new AudioMgr()`**：`LazySingletonBase<T>` 带 `new()` 约束，构造函数必须是 public，但外部实例化会得到"游离实例"并重复注册帧更新；统一用 `AudioMgr.Instance`。

## 7. 与框架的关系

- **更新驱动**：帧更新通过 `PublicMono.Instance.AddUpdateListener(Update)` 注册，注册时机是"首次使用"（不在构造函数里，因为 `PublicMono.Instance` 在退出播放时返回 `null`）。`Update` 内部整体 `try/catch` —— `PublicMono` 的 `updateEvent` 是多播委托，任一监听器抛异常会让排在它后面的所有模块这一帧的 Update 全被跳过。
- **释放**：实现 `IDisposable`，`LazySingletonBase.Release()` 会调用 `Dispose()` 解绑更新、停掉音频、归还引用计数并销毁 `AudioRoot`。
- **Resources 模式不依赖 `UPGameRoot`**；只有 AssetBundle 模式才会惰性去取 `UPGameRoot.GetAssetsLoader()`。
- **日志**：常规错误走 `PLogger`（与框架其它模块一致）；`Update` 内的"框架级异常"用 `Debug.LogException` —— `PLogger` 带 `[Conditional("OPEN_PLOG")]`，未定义宏时会被静默丢弃。

## 8. 维护提示

- 新增播放接口时，**务必**沿用"请求序号 + 加载失败判空 + 归还引用计数"这三件套。
- 归还引用计数前一定先 `clip = null`，否则 `Resources.UnloadAsset` 会卸载仍被引用着的资源。
- 修改 `EnsureSetup()` 的节点结构时，注意它同时承担"节点被外部销毁后重建"的职责（重建前会先归还旧记录的资源引用）。
- 改动后建议用工程外的真实编译校验（Unity 生成的 `.csproj` 未必包含新文件，`get_errors` 可能是假阴性）：用 `Library/EditorInstance.json` 里的 `app_contents_path` 找到 `Managed` / `MonoBleedingEdge\lib\mono\4.7.1-api` 作参考程序集，跑 `dotnet <sdk>\Roslyn\bincore\csc.dll` 编译 `Runtime/` 下的全部源码。
