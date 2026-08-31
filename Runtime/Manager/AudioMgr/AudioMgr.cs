using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    public class AudioMgr : LazySingletonBase<AudioMgr>
    {
        //唯一的背景音乐组件
        private AudioSource bkMusic = null;
        //唯一的音效播放组件
        private AudioSource uniqueSound = null;

        //音乐大小
        private float bkValue = 1;

        //音效依附对象
        private GameObject soundObj = null;
        //音效列表
        private List<AudioSource> soundList = new List<AudioSource>();
        //音效大小
        private float soundValue = 1;

        private IAssetsLoader assetsLoader;
        public AudioMgr()
        {
            PublicMono.Instance.AddUpdateListener(Update);
        }
        private void Update()
        {
            for (int i = soundList.Count - 1; i >= 0; i--)
            {
                if (!soundList[i].isPlaying)
                {
                    GameObject.Destroy(soundList[i]);
                    soundList.RemoveAt(i);
                }
            }
        }
        /// <summary>
        /// 播放背景音乐
        /// </summary>
        /// <param name="AcPath">音乐路径</param>
        public void PlayBKMusic(string AcPath)
        {
            if (bkMusic == null)
            {
                GameObject obj = new GameObject("BKMusic");
                bkMusic = obj.AddComponent<AudioSource>();
            }
            //异步加载背景音乐 加载完成后 播放
            ResourcesLoader.Instance.LoadAsync<AudioClip>(AcPath, (clip) =>
            {
                bkMusic.clip = clip;
                bkMusic.loop = true;
                bkMusic.volume = bkValue;
                bkMusic.Play();
            });
        }

        public void PlayBKMusic(AudioClip clip)
        {
            if (bkMusic == null)
            {
                GameObject obj = new GameObject("BKMusic");
                bkMusic = obj.AddComponent<AudioSource>();
            }
            bkMusic.clip = clip;
            bkMusic.loop = true;
            bkMusic.volume = bkValue;
            bkMusic.Play();
        }

        /// <summary>
        /// 暂停背景音乐
        /// </summary>
        public void PauseBKMusic()
        {
            if (bkMusic == null) { return; }
            bkMusic.Pause();
        }
        /// <summary>
        /// 停止背景音乐
        /// </summary>
        public void StopBKMusic()
        {
            if (bkMusic == null) { return; }
            bkMusic.Stop();
        }
        /// <summary>
        /// 改变背景音乐 音量大小
        /// </summary>
        /// <param name="v">音量大小</param>
        public void ChangeBKValue(float v)
        {
            bkValue = v;
            if (bkMusic == null) { return; }
            bkMusic.volume = bkValue;
        }

        /// <summary>
        /// 播放唯一音效（播放前关闭上一个音频）
        /// </summary>
        /// <param name="AcPath"></param>
        public void PlayUniqueSound(string AcPath, AssetLoadMethod method = AssetLoadMethod.Resources)
        {
            if (soundObj == null)
            {
                soundObj = new GameObject("Sound");
            }
            if (uniqueSound == null) uniqueSound = soundObj.AddComponent<AudioSource>();
            LoadSound(AcPath, method, (clip) =>
            {
                uniqueSound.Stop();
                uniqueSound.clip = clip;
                uniqueSound.loop = false;
                uniqueSound.volume = soundValue;
                uniqueSound.Play();
            });
        }
        /// <summary>
        /// 播放唯一音效（播放前关闭上一个音频）
        /// </summary>
        /// <param name="AcPath"></param>
        public void PlayUniqueSound(AudioClip clip)
        {
            if (soundObj == null)
            {
                soundObj = new GameObject("Sound");
            }
            if (uniqueSound == null) uniqueSound = soundObj.AddComponent<AudioSource>();
            uniqueSound.Stop();
            uniqueSound.clip = clip;
            uniqueSound.loop = false;
            uniqueSound.volume = soundValue;
            uniqueSound.Play();
        }

        /// <summary>
        /// 暂停唯一音效
        /// </summary>
        public void PauseUniqueSound(bool isPause = true)
        {
            if (uniqueSound == null) { return; }
            if (isPause)
                uniqueSound.Pause();
            else
                uniqueSound.Play();
        }

        /// <summary>
        /// 停止唯一音效
        /// </summary>
        public void StopUniqueSound()
        {
            if (uniqueSound == null) { return; }
            uniqueSound.Stop();
        }

        /// <summary>
        /// 加载并播放音效
        /// </summary>
        /// <param name="AcPath">资源路径；使用AssetBundle时，路径用编辑器下的路径(带后缀)</param>
        /// <param name="isLoop">是否循环</param>
        /// <param name="method">资源加载方式</param>
        /// <param name="callBack">加载结束的回调，参数是音效组件</param>
        public void LoadSoundAndPlay(string AcPath, bool isLoop = false, AssetLoadMethod method = AssetLoadMethod.Resources, UnityAction<AudioSource> callBack = null)
        {
            if (soundObj == null)
            {
                soundObj = new GameObject("Sound");
            }
            LoadSound(AcPath, method, (clip) =>
            {
                AudioSource source = soundObj.AddComponent<AudioSource>();
                source.clip = clip;
                source.loop = isLoop;
                source.volume = soundValue;
                source.Play();
                soundList.Add(source);
                callBack?.Invoke(source);
            });
        }

        private void LoadSound(string path, AssetLoadMethod method, UnityAction<AudioClip> callback)
        {
            switch (method)
            {
                case AssetLoadMethod.Resources:
                    ResourcesLoader.Instance.LoadAsync(path, callback);
                    break;
                case AssetLoadMethod.AssetBundle:
                    assetsLoader.LoadAsync(path, callback);
                    break;
            }
        }

        /// <summary>
        /// 改变音效声音大小
        /// </summary>
        /// <param name="value"></param>
        public void ChangeSoundValue(float value)
        {
            soundValue = value;
            for (int i = 0; i < soundList.Count; ++i)
                soundList[i].volume = value;
        }
        /// <summary>
        /// 停止音效
        /// </summary>
        /// <param name="source"></param>
        public void StopSound(AudioSource source)
        {
            if (soundList.Contains(source))
            {
                soundList.Remove(source);
                source.Stop();
                GameObject.Destroy(source);
            }
        }
    }
}

