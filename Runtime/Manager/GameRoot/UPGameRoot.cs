using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// 资源加载方式
    /// </summary>
    public enum AssetLoaddingMethod
    {
        Editor,//编辑器环境下加载资源
        Assetbundles,//使用AssetBundle加载资源
    }

    /// <summary>
    /// AB包根路径
    /// </summary>
    public enum ABRootPath
    {
        streamingAssetsPath,
        persistentDataPath
    }

    public class GFLoadedEvent : EventArgBase
    {
        public override int EventID => typeof(GFLoadedEvent).GetHashCode();

    }

    [AddComponentMenu("UPandaGF/GameRoot")]
    public class UPGameRoot : EagerMonoSingletonBase<UPGameRoot>
    {
        #region Component
        private DebugerInit debugerInit;//日志系统
        private ABUpdataMgr abUpdataMgr;//资源更新组件
        private SourcesLoad sourcesLoadMgr;//资源加载组件
        private BinaryDataMgrInit binaryDataMgr;//数据管理
        #endregion

        #region Config
        //[Header("资源加载方式")]
        public AssetLoaddingMethod method;
        //[Header("资源加载根路径")]
        public ABRootPath aBRootPath;
        //[Header("资源路径")]
        public string LoadAssetPath = "AssetBundles/StandaloneWindows/";
        //[Header("主包名")]
        public string MainName = "StandaloneWindows";

        /// <summary>
        /// 资源远端更新配置
        /// </summary>
        public ABUpdataMgrArg assetUpdataConfig;

        public bool EnableDebugModel = false;
        public Reporter reporter;


        //初始加载场景
        //public string firstScenePath = "Assets/Scenes/InitScene.unity";

        #endregion

        private void Reset()
        {
            SetComponent();
        }
        protected override void OnAwake()
        {
            SetComponent();
            if (method == AssetLoaddingMethod.Editor)
            {
                aBRootPath = ABRootPath.streamingAssetsPath;
#if !UNITY_EDITOR
                method = AssetLoaddingMethod.Assetbundles;
#endif
            }
            StartCoroutine(Init());
        }

        private IEnumerator Init()
        {
            yield return StartCoroutine(debugerInit.Init());
            binaryDataMgr.Init();
            abUpdataMgr.Init(assetUpdataConfig, LoadAssetPath);
            ABLoadMgr abLoadMgr = ABLoadMgr.Instance;
            abLoadMgr.MainName = MainName;
            switch (aBRootPath)
            {
                case ABRootPath.streamingAssetsPath:
                    abLoadMgr.PathUrl = Application.streamingAssetsPath + "/" + LoadAssetPath;
                    break;
                case ABRootPath.persistentDataPath:
                    abLoadMgr.PathUrl = Application.persistentDataPath + "/" + LoadAssetPath;
                    yield return StartCoroutine(abUpdataMgr.StartUpadateAssets());
                    break;
            }
            sourcesLoadMgr.Init(method, abLoadMgr);
            OnSourceInited();
        }

        private void SetComponent()
        {
            if (debugerInit == null) debugerInit = InitComponent<DebugerInit>();
            if (abUpdataMgr == null) abUpdataMgr = InitComponent<ABUpdataMgr>();
            if (sourcesLoadMgr == null) sourcesLoadMgr = InitComponent<SourcesLoad>();
            if (binaryDataMgr == null) binaryDataMgr = InitComponent<BinaryDataMgrInit>();
        }

        private T InitComponent<T>() where T : Component
        {
            T component = GetComponentInChildren<T>();
            if (component == null)
            {
                GameObject obj = new GameObject(typeof(T).Name);
                obj.transform.parent = transform;
                component = obj.AddComponent<T>();
            }
            return component;
        }

        private void OnSourceInited()
        {
            //PLoger.Log_red($"GameRoot Initialization completed!");
            //PLoger.Log_green($"GameRoot Initialization completed!");
            //PLoger.Log_blue($"GameRoot Initialization completed!");
            //PLoger.Log_yellow($"GameRoot Initialization completed!");
            //PLoger.Log_cyan($"GameRoot Initialization completed!");
            //PLoger.LogFormat("<color=yellow>{0}</color>", "GameRoot Initialization completed!");
            PLoger.Log_white($"GameRoot Initialization completed!");
            EventCenter.Instance.EventTrigger(new GFLoadedEvent());
        }

        /// <summary>
        /// 获取StreamingAssets的路径（适用于所有平台）
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static string GetStreamingAssetsPath()
        {
            string path = "";
#if UNITY_EDITOR
            path = Application.streamingAssetsPath;
#else
            // 判断平台类型，获取对应的路径
            if (Application.platform == RuntimePlatform.Android)
            {
                // Android平台上，StreamingAssets路径是通过jar包访问的
                path = "jar:file://" + Application.dataPath + "!/assets";
            }
            else if (Application.platform == RuntimePlatform.IPhonePlayer)
            {
                // iOS平台，StreamingAssets在应用包的根目录
                path = "file://" + Path.Combine(Application.dataPath, "Raw");
            }
            else
            {
                // 其他平台（Windows, Mac, Linux, WebGL等），可以直接使用Application.streamingAssetsPath
                path = Application.streamingAssetsPath;
            }
#endif
            return path;
        }


        public ISourcesLoad GetSourcesLoadComponent()
        {
            return sourcesLoadMgr;
        }
    }
}




