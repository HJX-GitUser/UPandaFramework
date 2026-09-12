using System;
using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// 编辑器环境 资源加载
    /// </summary>
    public class EditorSourcesMgr : LazySingletonBase<EditorSourcesMgr>
    {
        public T Load<T>(string path) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<T>(path);
#else
            PLogger.LogError("无法加载资源，Editor环境下才能使用此方法");
            return null;
#endif
        }

        public UnityEngine.Object Load(string path, Type type)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath(path, type);
#else
            PLogger.LogError("无法加载资源，Editor环境下才能使用此方法");
            return null;
#endif
        }

    }
}

