using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    public interface ISourcesLoad
    {
        /// <summary>
        /// 加载资源 泛型
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        void LoadAsync<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object;

        /// <summary>
        /// 加载资源
        /// </summary>
        /// <param name="path"></param>
        /// <param name="type"></param>
        /// <param name="callback"></param>
        void LoadAsync(string path, System.Type type, UnityAction<UnityEngine.Object> callback);


        /// <summary>
        /// 场景加载
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        void LoadSceneAsync(string path, UnityAction callback = null);
        void LoadSceneAsync(string path, LoadSceneMode loadSceneMode, UnityAction callback = null);

        /// <summary>
        /// 加载程序集 （HybridCLR）
        /// </summary>
        /// <param name="path"></param>
        /// <param name="callback"></param>
        public void LoadAssemblyAsync(string path, UnityAction<Assembly> callback);

        /// <summary>
        /// 资源卸载
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool UnLoadAB(string path);

        /// <summary>
        /// 清空AB资源
        /// </summary>
        /// <returns></returns>
        public void ClearAB();

    }


}

