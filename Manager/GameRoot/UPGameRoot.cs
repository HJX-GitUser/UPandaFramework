using System;
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
        //[Header("资源加载方式")]
        public AssetLoaddingMethod method;
        //[Header("资源加载根路径")]
        public ABRootPath aBRootPath;
        //[Header("资源路径")]
        public string LoadAssetPath = "AB/PC/";
        //[Header("主包名")]
        public string MainName = "PC";

        //[Header("启动时检查资源更新")]
        public bool enableChecekOnStart = false;

        private DebugerInit debugerInit;
        private MonoMgr monoMgr;
        private ABUpdataMgr abUpdataMgr;
        private ABLoadMgr abLoadMgr;
        private SourcesLoad sourcesLoadMgr;
        private EditorSourcesMgr editorSourcesMgr;

        private BinaryDataMgrInit binaryDataMgr;
        /// <summary>
        /// 资源数据存储的位置
        /// </summary>
        public string assetRefSavePath = "/Data/";
        /// <summary>
        /// 资源名
        /// </summary>
        public string assetRefName = "assetData";

        /// <summary>
        /// 资源数据存储文件后缀
        /// </summary>
        public string assetRefextension = ".assetref";

        //运行场景
        public string firstScenePath;

        private void Reset()
        {
            SetComponent();
        }
        protected override void Initialize()
        {
            SetComponent();
#if !UNITY_EDITOR
            method = AssetLoaddingMethod.Assetbundles;
#endif
            if (method == AssetLoaddingMethod.Editor)
            {
                enableChecekOnStart = false;
                aBRootPath = ABRootPath.streamingAssetsPath;
            }
            abUpdataMgr.localFilePath = LoadAssetPath;
            abUpdataMgr.enableChecekOnStart = enableChecekOnStart;
            switch (aBRootPath)
            {
                case ABRootPath.streamingAssetsPath:
                    abLoadMgr.PathUrl = Application.streamingAssetsPath + "/" + LoadAssetPath;
                    break;
                case ABRootPath.persistentDataPath:
                    abLoadMgr.PathUrl = Application.persistentDataPath + "/" + LoadAssetPath;
                    break;
            }
            abLoadMgr.MainName = MainName;
            debugerInit.Init(() =>
            {
                abUpdataMgr.Init();
                abUpdataMgr.updataCompleteEvent += OnSourceInited;
                abLoadMgr.Init();
                string assetRefpath = Application.streamingAssetsPath + assetRefSavePath + assetRefName + assetRefextension;
                ABSourcesRelated arg = LoadABSourcesRelated(assetRefpath);
                sourcesLoadMgr.Init(method, arg, abLoadMgr, editorSourcesMgr);
                abUpdataMgr.StartUpadateAssets();
            });
        }

        private void SetComponent()
        {
            debugerInit = InitComponent<DebugerInit>();
            monoMgr = InitComponent<MonoMgr>();
            abUpdataMgr = InitComponent<ABUpdataMgr>();
            abLoadMgr = InitComponent<ABLoadMgr>();
            sourcesLoadMgr = InitComponent<SourcesLoad>();
            editorSourcesMgr = InitComponent<EditorSourcesMgr>();
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



        private ABSourcesRelated LoadABSourcesRelated(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return null;
            }
            ABSourcesRelated obj = null;


            byte[] bytes = File.ReadAllBytes(filePath);
            using (MemoryStream ms = new MemoryStream(bytes))
            {
                BinaryFormatter bf = new BinaryFormatter();
                obj = bf.Deserialize(ms) as ABSourcesRelated;
                ms.Close();
            }
            return obj;
        }

        private void OnSourceInited(bool state)
        {
            sourcesLoadMgr.LoadSceneAsync(firstScenePath);
        }
    }
}


