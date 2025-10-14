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


    public class UPGameRoot : BaseMonoSingleton<UPGameRoot>
    {
        #region Component
        private DebugerInit debugerInit;
        private PublicMono monoMgr;
        private ABUpdataMgr abUpdataMgr;
        private ABLoadMgr abLoadMgr;
        private SourcesLoad sourcesLoadMgr;
        private BinaryDataMgrInit binaryDataMgr;
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
       

        //初始加载场景
        public string firstScenePath = "Assets/Scenes/InitScene.unity";

        #endregion

        private void Reset()
        {
            SetComponent();
        }
        protected override void Initialize()
        {
            SetComponent();
#if !UNITY_EDITOR
            if (method == AssetLoaddingMethod.Editor)
            {
                method = AssetLoaddingMethod.Assetbundles;
                aBRootPath = ABRootPath.streamingAssetsPath;
            }
#endif
            StartCoroutine(Init());
        }

        private IEnumerator Init()
        {
            yield return StartCoroutine(debugerInit.Init());
            abUpdataMgr.Init(assetUpdataConfig, LoadAssetPath);
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
            abLoadMgr.MainName = MainName;
            abLoadMgr.Init();
            sourcesLoadMgr.Init(method, abLoadMgr);
            OnSourceInited();
        }

        private void SetComponent()
        {
            monoMgr = InitComponent<PublicMono>();
            debugerInit = InitComponent<DebugerInit>();
            abUpdataMgr = InitComponent<ABUpdataMgr>();
            abLoadMgr = InitComponent<ABLoadMgr>();
            sourcesLoadMgr = InitComponent<SourcesLoad>();
            binaryDataMgr = InitComponent<BinaryDataMgrInit>();
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
            PLoger.Log($"场景加载： {firstScenePath}");
            sourcesLoadMgr.LoadSceneAsync(firstScenePath);
        }
    }
}


