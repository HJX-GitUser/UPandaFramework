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
        string firstScenePath = "Assets/Scenes/firstScene.unity";
        private void OnEnable()
        {
            component = (UPGameRoot)target;
            firstScenePath = EditorPrefs.GetString("UPGameRootEditor_firstScenePath", firstScenePath);
        }
        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            EditorGUILayout.LabelField("UPGameRoot", EditorStyles.boldLabel);
            EditorGUILayout.Space();
            component.method = (AssetLoaddingMethod)EditorGUILayout.EnumPopup("资源加载方式", component.method);
            switch (component.method)
            {
                case AssetLoaddingMethod.Editor:
                    AssetEditorGUI();
                    break;
                case AssetLoaddingMethod.Assetbundles:
                    AssetbundlesEditorGUI();
                    break;
            }
            EditorGUILayout.Space(10);
            firstScenePath = EditorGUILayout.TextField("加载场景", firstScenePath);
            if (component.firstScenePath != firstScenePath)
            {
                Debug.Log(firstScenePath);
                component.firstScenePath = firstScenePath;
                EditorPrefs.SetString("UPGameRootEditor_firstScenePath", firstScenePath);
            }
        }

        private void AssetEditorGUI()
        {

        }

        private void AssetbundlesEditorGUI()
        {
            component.aBRootPath = (ABRootPath)EditorGUILayout.EnumPopup("根路径", component.aBRootPath);
            if (component.aBRootPath == ABRootPath.persistentDataPath)
            {
                component.enableChecekOnStart = EditorGUILayout.ToggleLeft("启动时检查资源更新", component.enableChecekOnStart);
                EditorGUILayout.Space();
            }
            component.LoadAssetPath = EditorGUILayout.TextField("资源路径", component.LoadAssetPath);
            component.MainName = EditorGUILayout.TextField("主包名", component.MainName);
        }
    }
}