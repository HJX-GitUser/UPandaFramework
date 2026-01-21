using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    public interface IAssetsLoader
    {
        /// <summary>
        /// 加载AB包资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <returns></returns>
        Task<T> LoadAsync<T>(string path) where T : UnityEngine.Object;
        void LoadAsync<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object;
        Task<UnityEngine.Object> LoadAsync(string path, System.Type type);
        void LoadAsync(string path, System.Type type, UnityAction<UnityEngine.Object> callback);

        /// <summary>
        /// 场景加载
        /// </summary>
        /// <param name="path"></param>
        /// <param name="assetLoadComplete">场景资源加载结束回调</param>
        /// <param name="sceneLoadComplete">场景加载结束回调</param>
        void LoadSceneAsync(string path, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null);
        void LoadSceneAsync(string path, LoadSceneMode loadSceneMode, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null);

        /// <summary>
        /// 加载程序集 （HybridCLR）
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        void LoadAssemblyAsync(string path, UnityAction<Assembly> callback);
        Task<Assembly> LoadAssemblyAsync(string path);

        /// <summary>
        /// 资源卸载
        /// </summary>
        /// <param name="abName">ab包的包名</param>
        /// <returns></returns>
        bool UnLoadAB(string abName);

        /// <summary>
        /// 清空AB资源
        /// </summary>
        /// <returns></returns>
        void ClearAB();
    }


}

