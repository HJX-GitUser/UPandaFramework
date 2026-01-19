using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.Events;
using System.Threading.Tasks;

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



    public class GFLoadedEvent : EventArgBase
    {

    }

    [AddComponentMenu("UPandaGF/GameRoot")]
    public class UPGameRoot : EagerMonoSingletonBase<UPGameRoot>
    {
        #region Component
        private DebugerInit debugerInit;//日志系统
        private ABUpdataMgr abUpdataMgr;//资源更新组件
        private AssetsLoader sourcesLoadMgr;//资源加载组件
        private BinaryDataMgrInit binaryDataMgr;//数据管理
        private UIManager UIManager;//UI
        #endregion

        #region Config
        //[Header("资源加载方式")]
        public AssetLoaddingMethod method;
        public bool enableAssetUpdate;//启动资源更新
        //[Header("资源相对路径")]
        public string LoadAssetPath = "AssetBundles/StandaloneWindows/";
        //[Header("主包名")]
        public string MainName = "StandaloneWindows";
        public ABLoadPath MainPackageLoadPath = ABLoadPath.StreamingAssetsPath;//主包加载方式
        /// <summary>
        /// 资源远端更新配置
        /// </summary>
        public ABUpdataMgrArg assetUpdataConfig;

        /// <summary>
        /// 远程加载地址
        /// </summary>
        public string reomoteURL = "http://127.0.0.1:8090/";

        public bool EnableDebugModel = false;
        #endregion
        public Reporter reporter;

        private void Reset()
        {
            SetComponent();
        }
        protected override void OnAwake()
        {
            SetComponent();
            StartCoroutine(Init());
        }

        private IEnumerator Init()
        {
            yield return StartCoroutine(debugerInit.Init());
            binaryDataMgr.Init();

            ABLoadMgr abLoadMgr = ABLoadMgr.Instance;
            abLoadMgr.remoteURL = reomoteURL;
            if (method == AssetLoaddingMethod.Assetbundles)
            {
                abUpdataMgr.Init(assetUpdataConfig, LoadAssetPath);
                if (enableAssetUpdate)
                {
                    yield return StartCoroutine(abUpdataMgr.StartUpadateAssets());
                }
                Task abLoadMgrInitTask = abLoadMgr.Init(LoadAssetPath, MainName, MainPackageLoadPath);
                while (!abLoadMgrInitTask.IsCompleted)
                {
                    yield return null;
                }
            }
            Task sourcesLoadMgrTask = sourcesLoadMgr.Init(method, abLoadMgr);
            while (!sourcesLoadMgrTask.IsCompleted)
            {
                yield return null;
            }
            OnInited();
        }


        private void SetComponent()
        {
            if (debugerInit == null) debugerInit = InitComponent<DebugerInit>();
            if (abUpdataMgr == null) abUpdataMgr = InitComponent<ABUpdataMgr>();
            if (sourcesLoadMgr == null) sourcesLoadMgr = InitComponent<AssetsLoader>();
            if (binaryDataMgr == null) binaryDataMgr = InitComponent<BinaryDataMgrInit>();
            if(UIManager == null) UIManager = InitComponent<UIManager>();
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

        private void OnInited()
        {
            //PLoger.Log_red($"GameRoot Initialization completed!");
            //PLoger.Log_green($"GameRoot Initialization completed!");
            //PLoger.Log_blue($"GameRoot Initialization completed!");
            //PLoger.Log_yellow($"GameRoot Initialization completed!");
            //PLoger.Log_cyan($"GameRoot Initialization completed!");
            //PLoger.LogFormat("<color=yellow>{0}</color>", "GameRoot Initialization completed!");
            PLogger.Log_white($"GameRoot Initialization completed!");
            EventCenter.Instance.EventTrigger(new GFLoadedEvent());
        }

        /// <summary>
        /// 获取资源加载接口
        /// </summary>
        /// <returns></returns>
        public IAssetsLoader GetAssetsLoader()
        {
            return sourcesLoadMgr;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            PLogger.LogWarning("UPGameRoot Destory!!!");
        }
    }
}




