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
        /// 场景加载。实际由 UPGameRoot 装配的 ISceneService 执行（串行队列 / 进度 / 取消 / 作用域归还），
        /// 不要直接调用 SceneManager 绕过串行队列与场景作用域。
        /// </summary>
        /// <param name="path">场景路径（AB 模式为清单里登记的完整路径，如 Assets/Scenes/GameScene.unity）</param>
        /// <param name="assetLoadComplete">场景资源就绪回调（进入 Loading 阶段前触发一次）</param>
        /// <param name="sceneLoadComplete">场景加载完成回调（仅成功时触发）</param>
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

