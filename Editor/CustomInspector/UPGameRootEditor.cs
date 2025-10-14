using AssetBundleBrowser;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.VisualScripting.FullSerializer;
using UnityEditor;
using UnityEngine;

namespace UPandaGF
{
    [CustomEditor(typeof(UPGameRoot))]
    public class UPGameRootEditor : Editor
    {
        UPGameRoot component;
        private void OnEnable()
        {
            component = (UPGameRoot)target;
        }
        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUILayout.LabelField("UPGameRoot", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            ShowArg("method", "资源加载方式");
            switch (component.method)
            {
                case AssetLoaddingMethod.Editor:
                    //AssetEditorGUI();
                    EditorGUILayout.Space();
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    AssetbundlesEditorGUI();
                    break;
            }
            EditorGUILayout.Space(10);
            ShowArg("firstScenePath", "加载场景路径");
            serializedObject.ApplyModifiedProperties();
        }

        private void AssetEditorGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("把资源添加到AssetBundle后，需要更新资源关联数据才能加载", MessageType.Info);
            if (GUILayout.Button("更新资源关联数据"))
            {
                AssetBundleBuildTab.GenerateAssetBundleInfo();
            }
            EditorGUILayout.Space();
        }

        private void AssetbundlesEditorGUI()
        {
            ShowArg("aBRootPath", "根路径");
            if (component.aBRootPath == ABRootPath.persistentDataPath)
            {
                ShowArg("assetUpdataConfig", "资源更新配置");
                EditorGUILayout.Space();
            }
            ShowArg("LoadAssetPath", "资源路径");
            ShowArg("MainName", "主包名");
        }


        private void ShowArg(string argName,string inspectName)
        {
            SerializedProperty arg = serializedObject.FindProperty(argName);
            EditorGUILayout.PropertyField(arg, new GUIContent(inspectName));
        }
    }
}