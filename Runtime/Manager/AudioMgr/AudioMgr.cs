// =============================================================================
//  AudioMgr.cs —— 音频管理器（背景音乐 + 音效）
// -----------------------------------------------------------------------------
//  职责：
//      · 背景音乐：双路 AudioSource 交叉淡入淡出切换，支持暂停 / 恢复 / 停止 / 调音量
//      · 音效：唯一音效（同时只响一个）+ 叠加音效（播完自动回收）
//      · 资源：Resources / AssetBundle 两种加载方式；Resources 模式下会把
//              ResourcesLoader 的引用计数还回去，避免 AudioClip 常驻内存
//
//  为什么是两路 AudioSource？
//      交叉淡入淡出要求"新的淡入"与"旧的淡出"同时存在；只有一路 AudioSource 时
//      给 clip 赋值会立刻打断当前播放，只能硬切。
//
//  常驻结构（切场景不销毁，避免引用变成"假 null"）：
//      AudioRoot (DontDestroyOnLoad)
//        ├── BGM_A / BGM_B     ← 交叉淡入淡出的两路
//        └── SFX               ← 唯一音效 + 所有叠加音效都挂在它上面
//
//  生命周期 / 线程注意：
//      · 纯 C# 单例（LazySingletonBase）。帧更新由 PublicMono 驱动，
//        注册时机是"首次使用"而不是构造函数 —— PublicMono.Instance 在退出播放时返回 null。
//      · Update 内部整体 try/catch：PublicMono 的 updateEvent 是多播委托，
//        任一监听器抛异常会让排在它后面所有模块这一帧的 Update 全被跳过。
//      · 本类实现 IDisposable，LazySingletonBase.Release() 会调 Dispose() 解绑更新。
//
//  使用示例：
//      AudioMgr.Instance.PlayBKMusic("Audio/Bgm_Main", fadeDuration: 1.5f);   // 交叉淡入 1.5 秒
//      AudioMgr.Instance.PlayUniqueSound("Audio/Click");                     // 唯一音效
//      AudioMgr.Instance.LoadSoundAndPlay("Audio/Explosion", isLoop: false); // 叠加音效（可叠加）
//      AudioMgr.Instance.StopBKMusic(0.5f);                                   // 0.5 秒淡出停止
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// 音频管理器：BGM 支持交叉淡入淡出；音效支持 Resources / AssetBundle 加载并自动回收。
    /// </summary>
    public class AudioMgr : LazySingletonBase<AudioMgr>, IDisposable
    {
        #region 常量

        /// <summary>BGM 通道数：固定 2 路用于交叉淡入淡出</summary>
        private const int BgmSourceCount = 2;

        private const string AudioRootName = "AudioRoot";
        private const string BgmNodeName = "BGM";
        private const string SoundNodeName = "SFX";

        #endregion

        #region 字段

        // ---------- 常驻节点与播放组件 ----------
        private GameObject audioRoot;                   // 常驻根节点（DontDestroyOnLoad）
        private GameObject soundObj;                    // 音效容器
        private AudioSource[] bgmSources;               // 两路 BGM
        private PlayingClip[] bgmClips;                 // 每路 BGM 当前持有的资源记录（与 bgmSources 一一对应）
        private AudioSource uniqueSound;                // 唯一音效的播放组件
        private readonly PlayingClip uniqueClip = new PlayingClip();
        private readonly List<SoundHandle> soundList = new List<SoundHandle>();   // 叠加音效

        // ---------- BGM 状态 ----------
        private AudioSource bgmCurrent;                 // 正在发声（或正在淡出）的那一路
        private AudioSource bgmIncoming;                // 正在淡入的那一路（没有交叉淡变时为 null）
        private float bgmValue = 1f;                    // BGM 音量上限（0~1）
        private float bgmDefaultFade = 1f;              // 默认淡入淡出时长（秒）
        private float bgmActiveFade;                    // 本次淡变的时长
        private float bgmFadeElapsed;                   // 本次淡变已经过去的时间
        private float bgmFadeFromVolume;                // 淡出方开始淡出时的音量
        private bool bgmIsFading;
        private bool bgmPaused;
        private int bgmRequestId;                       // 请求序号：用于丢弃过期的异步加载回调

        // ---------- 音效状态 ----------
        private float soundValue = 1f;                  // 音效音量（0~1）
        private int uniqueRequestId;                    // 唯一音效的请求序号

        // ---------- 其它 ----------
        private IAssetsLoader assetsLoader;             // 惰性获取（仅 AssetBundle 模式使用）
        private bool updateRegistered;                  // 是否已注册到 PublicMono

        #endregion

        #region 查询属性

        /// <summary>BGM 音量上限（0~1）</summary>
        public float BgmVolume { get { return bgmValue; } }

        /// <summary>音效音量（0~1）</summary>
        public float SoundVolume { get { return soundValue; } }

        /// <summary>BGM 是否正在播放（暂停时为 false）</summary>
        public bool IsBgmPlaying { get { return bgmCurrent != null && bgmCurrent.isPlaying; } }

        /// <summary>BGM 是否处于暂停状态</summary>
        public bool IsBgmPaused { get { return bgmPaused; } }

        /// <summary>最近一次请求播放的 BGM 路径（用 AudioClip 直接播放时为 null）</summary>
        public string CurrentBgmPath { get; private set; }

        /// <summary>当前正在播放的叠加音效数量</summary>
        public int PlayingSoundCount { get { return soundList.Count; } }

        /// <summary>默认淡入淡出时长（秒）</summary>
        public float DefaultBgmFadeDuration
        {
            get { return bgmDefaultFade; }
            set { bgmDefaultFade = Mathf.Max(0f, value); }
        }

        #endregion

        #region 生命周期

        /// <summary>
        /// 按需创建常驻节点与播放组件、注册帧更新。
        /// 所有对外 API 的第一行都会调用它，因此不存在"构造函数里碰 Unity API"的问题。
        /// </summary>
        private void EnsureSetup()
        {
            EnsureUpdateRegistered();

            if (audioRoot != null) return;

            // 节点被外部销毁过（例如手动 Destroy）：先把旧记录的资源引用还回去，再重建
            ReleaseAllBgmClips();
            ClearAllSounds();
            bgmCurrent = null;
            bgmIncoming = null;
            bgmIsFading = false;

            audioRoot = new GameObject(AudioRootName);
            if (Application.isPlaying)
            {
                // 切场景不销毁：否则 soundList / bgmSources 里的引用会变成"假 null"，
                // 每帧访问它们会抛异常（并连带跳过 PublicMono 其它监听者的 Update）
                UnityEngine.Object.DontDestroyOnLoad(audioRoot);
            }

            bgmSources = new AudioSource[BgmSourceCount];
            bgmClips = new PlayingClip[BgmSourceCount];
            for (int i = 0; i < BgmSourceCount; i++)
            {
                string nodeName = string.Format("{0}_{1}", BgmNodeName, (char)('A' + i));
                GameObject node = new GameObject(nodeName);
                node.transform.SetParent(audioRoot.transform);
                bgmSources[i] = CreateSource(node);
                bgmClips[i] = new PlayingClip();
            }

            GameObject soundNode = new GameObject(SoundNodeName);
            soundNode.transform.SetParent(audioRoot.transform);
            soundObj = soundNode;
            uniqueSound = CreateSource(soundObj);
        }

        /// <summary>统一的 AudioSource 创建：2D 播放、不自动播放</summary>
        private static AudioSource CreateSource(GameObject host)
        {
            AudioSource source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;   // 显式关闭（AddComponent 默认是 true，只是因为没 clip 才没出声）
            source.spatialBlend = 0f;     // 0 = 2D
            return source;
        }

        /// <summary>注册帧更新（惰性，避免在构造函数里触碰 Unity API）</summary>
        private void EnsureUpdateRegistered()
        {
            if (updateRegistered) return;

            PublicMono mono = PublicMono.Instance;   // 退出播放时为 null
            if (mono == null) return;

            mono.AddUpdateListener(Update);
            updateRegistered = true;
        }

        /// <summary>帧更新：推进 BGM 淡变 + 回收播放完的音效</summary>
        private void Update()
        {
            // 整体兜异常：PublicMono 的 updateEvent 是多播委托，这里抛出去会中断其它模块的更新
            try
            {
                UpdateBgmFade();
                RecycleFinishedSounds();
            }
            catch (Exception e)
            {
                // 用原始 Debug 而非 PLogger：PLogger 带 [Conditional("OPEN_PLOG")]，
                // 未定义宏时这条"框架级异常"会被静默丢掉
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 释放：解绑帧更新、停掉全部音频、归还资源引用、销毁常驻节点。
        /// 由 <see cref="LazySingletonBase{T}.Release"/> 调用。
        /// </summary>
        public void Dispose()
        {
            if (updateRegistered)
            {
                PublicMono mono = PublicMono.Instance;
                if (mono != null) mono.RemoveUpdateListener(Update);
                updateRegistered = false;
            }

            bgmRequestId++;      // 让还在加载中的回调作废
            uniqueRequestId++;

            bgmIsFading = false;
            bgmPaused = false;
            bgmCurrent = null;
            bgmIncoming = null;
            CurrentBgmPath = null;

            ReleaseAllBgmClips();
            ClearAllSounds();

            if (uniqueSound != null)
            {
                uniqueSound.Stop();
                uniqueSound.clip = null;
            }
            uniqueClip.Release();

            if (audioRoot != null)
            {
                UnityEngine.Object.Destroy(audioRoot);
                audioRoot = null;
                soundObj = null;
                uniqueSound = null;
                bgmSources = null;
                bgmClips = null;
            }
        }

        #endregion

        #region 背景音乐

        /// <summary>
        /// 播放背景音乐（异步加载完成后交叉淡入淡出切换；加载失败时保持当前 BGM 不变）。
        /// </summary>
        /// <param name="AcPath">资源路径；AssetBundle 模式用编辑器下的路径（带后缀）</param>
        /// <param name="method">加载方式</param>
        /// <param name="fadeDuration">交叉淡变时长（秒）：小于 0 用默认值（默认 1 秒），0 = 立即切换</param>
        /// <param name="restartIfSame">与当前正在播的是同一路径时，是否重新从头播放（默认 false：什么都不做）</param>
        public void PlayBKMusic(string AcPath, AssetLoadMethod method = AssetLoadMethod.Resources,
                                float fadeDuration = -1f, bool restartIfSame = false)
        {
            EnsureSetup();

            if (string.IsNullOrEmpty(AcPath))
            {
                PLogger.LogError("[AudioMgr] PlayBKMusic 收到空路径");
                return;
            }

            // 同一首且没有正在进行的淡变：默认不重复加载（避免白白 AddRefCount、也避免从头重播）
            if (!restartIfSame && !bgmIsFading && bgmCurrent != null && CurrentBgmPath == AcPath)
            {
                if (!bgmCurrent.isPlaying && !bgmPaused) bgmCurrent.Play();
                return;
            }

            int requestId = ++bgmRequestId;      // 让更早的请求作废
            RequestClip(AcPath, method, (clip, needRelease) =>
            {
                if (requestId != bgmRequestId || audioRoot == null)
                {
                    ReleaseClipRef(AcPath, needRelease);   // 过期请求：把自己加上的引用计数还回去
                    return;
                }

                if (clip == null)
                {
                    PLogger.LogError($"[AudioMgr] 背景音乐加载失败，当前 BGM 保持不变：{AcPath}");
                    ReleaseClipRef(AcPath, needRelease);
                    return;
                }

                SwitchBgm(clip, AcPath, needRelease, fadeDuration);
            });
        }

        /// <summary>
        /// 直接播放一个已加载好的 AudioClip（资源由外部管理，本类不归还引用计数）。
        /// </summary>
        public void PlayBKMusic(AudioClip clip, float fadeDuration = -1f, bool restartIfSame = false)
        {
            EnsureSetup();

            if (clip == null)
            {
                PLogger.LogError("[AudioMgr] PlayBKMusic 收到 null clip");
                return;
            }

            if (!restartIfSame && !bgmIsFading && bgmCurrent != null && bgmCurrent.clip == clip)
            {
                if (!bgmCurrent.isPlaying && !bgmPaused) bgmCurrent.Play();
                return;
            }

            SwitchBgm(clip, null, false, fadeDuration);
        }

        /// <summary>
        /// 停止背景音乐（默认 1 秒淡出；传 0 立即停止）。
        /// </summary>
        /// <param name="fadeDuration">淡出时长（秒），小于 0 用默认值</param>
        public void StopBKMusic(float fadeDuration = -1f)
        {
            bgmRequestId++;      // 丢弃还在加载中的请求
            bgmPaused = false;

            // 交叉淡变进行到一半时停止：以"正在淡入的那路"为当前，另一路直接收掉
            AudioSource audible = bgmIncoming != null ? bgmIncoming : bgmCurrent;
            if (bgmCurrent != null && bgmCurrent != audible) StopAndClearBgmSource(bgmCurrent);
            bgmIncoming = null;
            bgmCurrent = audible;
            CurrentBgmPath = null;

            if (bgmCurrent == null)
            {
                bgmIsFading = false;
                return;
            }

            float duration = fadeDuration < 0f ? bgmDefaultFade : Mathf.Max(0f, fadeDuration);
            bgmFadeFromVolume = bgmCurrent.volume;
            bgmActiveFade = duration;
            bgmFadeElapsed = 0f;
            bgmIsFading = true;

            if (duration <= 0f) CompleteBgmFade();
        }

        /// <summary>暂停背景音乐（暂停期间淡变进度也一起冻结）</summary>
        public void PauseBKMusic()
        {
            if (bgmPaused) return;
            bgmPaused = true;
            SetBgmSourcesPaused(true);
        }

        /// <summary>恢复背景音乐</summary>
        public void ResumeBKMusic()
        {
            if (!bgmPaused) return;
            bgmPaused = false;
            SetBgmSourcesPaused(false);
        }

        /// <summary>改变背景音乐音量（0~1）。正在淡变时会在淡变过程中生效。</summary>
        public void ChangeBKValue(float v)
        {
            bgmValue = Mathf.Clamp01(v);

            // 淡变中由 UpdateBgmFade 每帧按新上限重算，这里不要再直接赋值，否则会跳一下
            if (!bgmIsFading && bgmCurrent != null) bgmCurrent.volume = bgmValue;
        }

        /// <summary>真正切换 BGM：把 clip 放到"空闲的那一路"，两路交叉淡入淡出</summary>
        private void SwitchBgm(AudioClip clip, string path, bool needRelease, float fadeDuration)
        {
            float duration = fadeDuration < 0f ? bgmDefaultFade : Mathf.Max(0f, fadeDuration);

            AudioSource audible = bgmIncoming != null ? bgmIncoming : bgmCurrent;   // 现在真正听得见的那一路
            AudioSource target = audible == bgmSources[0] ? bgmSources[1] : bgmSources[0];

            // 目标路可能正是上一轮淡出的那路（上一轮还没结束就又切歌）：先彻底收掉，避免三路同时响
            if (target != audible) StopAndClearBgmSource(target);

            bgmCurrent = audible;                                  // 淡出方
            bgmIncoming = target;                                  // 淡入方
            bgmFadeFromVolume = audible != null ? audible.volume : 0f;

            target.clip = clip;
            target.loop = true;
            target.volume = duration > 0f ? 0f : bgmValue;
            target.Play();
            SetBgmSourceClip(target, path, needRelease);

            bgmPaused = false;                  // 明确要求播放 → 解除暂停
            CurrentBgmPath = path;
            bgmActiveFade = duration;
            bgmFadeElapsed = 0f;
            bgmIsFading = true;

            if (duration <= 0f) CompleteBgmFade();
        }

        /// <summary>推进淡变进度（暂停时冻结）</summary>
        private void UpdateBgmFade()
        {
            if (!bgmIsFading || bgmPaused || bgmSources == null) return;

            if (bgmActiveFade <= 0f)
            {
                CompleteBgmFade();
                return;
            }

            bgmFadeElapsed += Time.unscaledDeltaTime;      // 用非缩放时间：暂停游戏不该冻结音乐
            float t = Mathf.Clamp01(bgmFadeElapsed / bgmActiveFade);

            if (bgmIncoming != null) bgmIncoming.volume = bgmValue * t;
            if (bgmCurrent != null && bgmCurrent != bgmIncoming) bgmCurrent.volume = bgmFadeFromVolume * (1f - t);

            if (t >= 1f) CompleteBgmFade();
        }

        /// <summary>淡变结束：收掉旧的那一路，把淡入方提为当前</summary>
        private void CompleteBgmFade()
        {
            bgmIsFading = false;

            if (bgmCurrent != null && bgmCurrent != bgmIncoming)
                StopAndClearBgmSource(bgmCurrent);      // 停播 + 解除 clip + 归还引用计数

            bgmCurrent = bgmIncoming;
            bgmIncoming = null;

            if (bgmCurrent != null) bgmCurrent.volume = bgmValue;
        }

        /// <summary>停掉并清空一路 BGM，同时归还它占用的资源引用</summary>
        private void StopAndClearBgmSource(AudioSource source)
        {
            if (source == null) return;

            source.Stop();
            source.clip = null;      // 先解除引用，引用计数归零时 UnloadAsset 才安全

            int index = IndexOfBgmSource(source);
            if (index >= 0 && bgmClips[index] != null) bgmClips[index].Release();
        }

        private void SetBgmSourcesPaused(bool pause)
        {
            if (bgmSources == null) return;

            for (int i = 0; i < bgmSources.Length; i++)
            {
                AudioSource source = bgmSources[i];
                if (source == null) continue;

                if (pause) source.Pause();
                else source.UnPause();
            }
        }

        private int IndexOfBgmSource(AudioSource source)
        {
            if (bgmSources == null || source == null) return -1;

            for (int i = 0; i < bgmSources.Length; i++)
            {
                if (bgmSources[i] == source) return i;
            }
            return -1;
        }

        private void SetBgmSourceClip(AudioSource source, string path, bool needRelease)
        {
            int index = IndexOfBgmSource(source);
            if (index >= 0 && bgmClips[index] != null) bgmClips[index].Set(path, needRelease);
        }

        private void ReleaseAllBgmClips()
        {
            if (bgmClips == null) return;

            for (int i = 0; i < bgmClips.Length; i++)
            {
                if (bgmClips[i] != null) bgmClips[i].Release();
            }
        }

        #endregion

        #region 唯一音效

        /// <summary>
        /// 播放唯一音效（同一时刻只有一个；新音效加载完成后才会顶掉上一个，
        /// 因此加载失败时不会把正在响的音效打断）。
        /// </summary>
        public void PlayUniqueSound(string AcPath, AssetLoadMethod method = AssetLoadMethod.Resources)
        {
            EnsureSetup();

            if (string.IsNullOrEmpty(AcPath))
            {
                PLogger.LogError("[AudioMgr] PlayUniqueSound 收到空路径");
                return;
            }

            int requestId = ++uniqueRequestId;
            RequestClip(AcPath, method, (clip, needRelease) =>
            {
                if (requestId != uniqueRequestId || audioRoot == null)
                {
                    ReleaseClipRef(AcPath, needRelease);
                    return;
                }

                if (clip == null)
                {
                    PLogger.LogError($"[AudioMgr] 唯一音效加载失败，保持上一个音效：{AcPath}");
                    ReleaseClipRef(AcPath, needRelease);
                    return;
                }

                // 顺序很重要：先停播并解除 clip 引用，再归还引用计数，
                // 否则 UnloadAsset 时 clip 还被 AudioSource 引用着
                StopUniqueSource();
                uniqueClip.Release();
                PlayOnSource(uniqueSound, clip, false);
                uniqueClip.Set(AcPath, needRelease);
            });
        }

        /// <summary>播放唯一音效（直接给 AudioClip，资源由外部管理）</summary>
        public void PlayUniqueSound(AudioClip clip)
        {
            EnsureSetup();

            if (clip == null)
            {
                PLogger.LogError("[AudioMgr] PlayUniqueSound 收到 null clip");
                return;
            }

            StopUniqueSource();
            uniqueClip.Release();       // 同样：先解除 clip 引用再归还引用计数
            PlayOnSource(uniqueSound, clip, false);
        }

        /// <summary>暂停 / 恢复唯一音效</summary>
        public void PauseUniqueSound(bool isPause = true)
        {
            if (uniqueSound == null) return;

            if (isPause) uniqueSound.Pause();
            else uniqueSound.UnPause();
        }

        /// <summary>停止唯一音效（并归还资源引用）</summary>
        public void StopUniqueSound()
        {
            uniqueRequestId++;      // 丢弃还在加载中的请求

            StopUniqueSource();
            uniqueClip.Release();
        }

        /// <summary>停播唯一音效并解除它持有的 clip 引用（释放顺序：先解除引用，再归还引用计数）</summary>
        private void StopUniqueSource()
        {
            if (uniqueSound == null) return;

            uniqueSound.Stop();
            uniqueSound.clip = null;
        }

        #endregion

        #region 叠加音效

        /// <summary>
        /// 加载并播放一个音效；同一个时刻可以有多个音效同时播放（与"唯一音效"相对）。
        /// 播放结束后会自动回收（销毁 AudioSource 并归还资源引用）。
        /// </summary>
        /// <param name="AcPath">资源路径</param>
        /// <param name="isLoop">是否循环（循环音效不会被自动回收，需要自己调 StopSound）</param>
        /// <param name="method">加载方式</param>
        /// <param name="callBack">加载回调，参数是音效组件；失败时以 null 回调，调用方需判空</param>
        public void LoadSoundAndPlay(string AcPath, bool isLoop = false,
                                     AssetLoadMethod method = AssetLoadMethod.Resources,
                                     UnityAction<AudioSource> callBack = null)
        {
            EnsureSetup();

            if (string.IsNullOrEmpty(AcPath))
            {
                PLogger.LogError("[AudioMgr] LoadSoundAndPlay 收到空路径");
                if (callBack != null) callBack(null);
                return;
            }

            RequestClip(AcPath, method, (clip, needRelease) =>
            {
                // 加载失败，或本类已经被 Dispose：明确以 null 回调，不给调用方"马上会被回收的空壳"
                if (clip == null || audioRoot == null)
                {
                    if (clip == null) PLogger.LogError($"[AudioMgr] 音效加载失败：{AcPath}");
                    ReleaseClipRef(AcPath, needRelease);
                    if (callBack != null) callBack(null);
                    return;
                }

                AudioSource source = CreateSource(soundObj);
                PlayOnSource(source, clip, isLoop);

                SoundHandle handle = new SoundHandle(source);
                handle.clip.Set(AcPath, needRelease);
                soundList.Add(handle);

                if (callBack != null) callBack(source);
            });
        }

        /// <summary>改变音效音量（0~1），同时作用于唯一音效与所有正在播放的音效</summary>
        public void ChangeSoundValue(float value)
        {
            soundValue = Mathf.Clamp01(value);

            for (int i = 0; i < soundList.Count; i++)
            {
                AudioSource source = soundList[i].source;
                if (source != null) source.volume = soundValue;
            }

            if (uniqueSound != null) uniqueSound.volume = soundValue;   // 修复：以前漏了唯一音效
        }

        /// <summary>
        /// 停止某个音效并回收它（<see cref="LoadSoundAndPlay"/> 回调里拿到的 source）。
        /// 传进来的 source 不在本类管理范围内时，只停止、不销毁。
        /// </summary>
        public void StopSound(AudioSource source)
        {
            if (source == null) return;

            for (int i = soundList.Count - 1; i >= 0; i--)
            {
                if (soundList[i].source == source)
                {
                    DestroySoundHandle(soundList[i]);
                    soundList.RemoveAt(i);
                    return;
                }
            }

            source.Stop();
        }

        /// <summary>
        /// 暂停 / 恢复某个音效。被暂停的音效不会被自动回收，
        /// 恢复后用 <see cref="StopSound"/> 结束它。
        /// </summary>
        public void PauseSound(AudioSource source, bool isPause = true)
        {
            if (source == null) return;

            for (int i = 0; i < soundList.Count; i++)
            {
                if (soundList[i].source == source) soundList[i].pausedByUser = isPause;
            }

            if (isPause) source.Pause();
            else source.UnPause();
        }

        /// <summary>停止所有音效（含唯一音效）；背景音乐不受影响</summary>
        public void StopAllSound()
        {
            ClearAllSounds();

            uniqueRequestId++;
            StopUniqueSource();
            uniqueClip.Release();
        }

        /// <summary>回收播放完的音效</summary>
        private void RecycleFinishedSounds()
        {
            for (int i = soundList.Count - 1; i >= 0; i--)
            {
                SoundHandle handle = soundList[i];
                if (handle == null)
                {
                    soundList.RemoveAt(i);
                    continue;
                }

                // source == null 说明组件被外部销毁了（旧实现直接访问 isPlaying 会每帧抛异常），
                // 这里当成"已结束"处理，顺便把资源引用还回去
                bool finished = handle.source == null || (!handle.source.isPlaying && !handle.pausedByUser);
                if (finished)
                {
                    DestroySoundHandle(handle);
                    soundList.RemoveAt(i);
                }
            }
        }

        private void DestroySoundHandle(SoundHandle handle)
        {
            if (handle == null) return;

            if (handle.source != null)
            {
                handle.source.Stop();
                handle.source.clip = null;      // 先解除引用，再归还引用计数
                UnityEngine.Object.Destroy(handle.source);
            }

            handle.clip.Release();
        }

        private void ClearAllSounds()
        {
            for (int i = soundList.Count - 1; i >= 0; i--)
            {
                DestroySoundHandle(soundList[i]);
            }
            soundList.Clear();
        }

        private void PlayOnSource(AudioSource source, AudioClip clip, bool loop)
        {
            if (source == null) return;

            source.Stop();
            source.clip = clip;
            source.loop = loop;
            source.volume = soundValue;
            source.Play();
        }

        #endregion

        #region 内部：加载与资源引用

        /// <summary>
        /// 按加载方式异步加载 AudioClip。
        /// 回调第二个参数表示"这次加载是否需要在用完后归还 ResourcesLoader 的引用计数"
        /// （AssetBundle 模式按包整体卸载，不做单资源释放，所以传 false）。
        /// 加载失败时会以 clip = null 回调。
        /// </summary>
        private void RequestClip(string path, AssetLoadMethod method, UnityAction<AudioClip, bool> callback)
        {
            switch (method)
            {
                case AssetLoadMethod.Resources:
                    ResourcesLoader loader = ResourcesLoader.Instance;
                    if (loader == null)
                    {
                        PLogger.LogError($"[AudioMgr] ResourcesLoader 不可用（可能正在退出播放）：{path}");
                        callback(null, false);
                        return;
                    }
                    loader.LoadAsync<AudioClip>(path, clip => callback(clip, true));
                    break;

                case AssetLoadMethod.AssetBundle:
                    IAssetsLoader bundleLoader = GetAssetsLoader();
                    if (bundleLoader == null)
                    {
                        callback(null, false);      // 内部已经打过错误日志
                        return;
                    }
                    bundleLoader.LoadAsync<AudioClip>(path, clip => callback(clip, false));
                    break;

                default:
                    PLogger.LogError($"[AudioMgr] 不支持的加载方式：{method}（path:{path}）");
                    callback(null, false);
                    break;
            }
        }

        /// <summary>
        /// AssetBundle 资源加载器（惰性获取）。
        /// 注意：UPGameRoot 是饿汉单例，缺失时会自动创建实例，
        /// 因此只有真正用到 AssetBundle 模式时才会去取它。
        /// </summary>
        private IAssetsLoader GetAssetsLoader()
        {
            if (assetsLoader != null) return assetsLoader;

            UPGameRoot root = UPGameRoot.Instance;
            assetsLoader = root != null ? root.GetAssetsLoader() : null;
            if (assetsLoader == null)
            {
                PLogger.LogError("[AudioMgr] 资源加载器不可用（UPGameRoot 未初始化或资源系统尚未就绪），AssetBundle 模式无法加载音频");
            }
            return assetsLoader;
        }

        /// <summary>归还一次 ResourcesLoader 的引用计数（AB 资源由 AB 管理器整体卸载，不在这里处理）</summary>
        private static void ReleaseClipRef(string path, bool needRelease)
        {
            if (!needRelease || string.IsNullOrEmpty(path)) return;

            ResourcesLoader loader = ResourcesLoader.Instance;
            if (loader != null) loader.UnLoadAsset<AudioClip>(path, true);
        }

        #endregion

        #region 内部类型

        /// <summary>
        /// "某一路播放源当前占用的音频资源"记录。
        /// 用于在该路被替换 / 回收时归还 ResourcesLoader 的引用计数 —— 否则
        /// AudioClip 会被 resDic 永久缓存（引用计数只增不减）。
        /// </summary>
        private sealed class PlayingClip
        {
            private string path;
            private bool needRelease;

            public void Set(string newPath, bool newNeedRelease)
            {
                path = newPath;
                needRelease = newNeedRelease;
            }

            /// <summary>归还引用计数：isDel = true 表示引用归零时真正从缓存里移除</summary>
            public void Release()
            {
                if (!needRelease || string.IsNullOrEmpty(path)) return;

                ReleaseClipRef(path, true);
                path = null;
                needRelease = false;
            }
        }

        /// <summary>一个正在播放的叠加音效</summary>
        private sealed class SoundHandle
        {
            public readonly AudioSource source;
            public readonly PlayingClip clip = new PlayingClip();
            public bool pausedByUser;      // 用户手动暂停过：不自动回收

            public SoundHandle(AudioSource source)
            {
                this.source = source;
            }
        }

        #endregion
    }
}
