using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEngine;

namespace UPandaGF
{
    [CustomEditor(typeof(UPGameRoot))]
    public class UPGameRootEditor : Editor
    {
        UPGameRoot component;
        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            if (component == null) component = target as UPGameRoot;
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
            if (GUILayout.Button("创建资源数据"))
            {
                GenerateAssetBundleInfo();
            }
            EditorGUILayout.HelpBox("AB包改动后需要创建资源数据，否者资源加载可能异常！", MessageType.Info);
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

        /// <summary>
        /// 遍历项目目录，生成AB包数据
        /// </summary>
        private void GenerateAssetBundleInfo()
        {
            // 获取所有的资源路径
            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
            ABSourcesRef aBSourcesRef = new ABSourcesRef();
            foreach (string assetPath in allAssetPaths)
            {
                // 排除非资源文件
                if (assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Assets/Plugins") && !assetPath.EndsWith(".cs"))
                {
                    // 获取该资源的 AssetBundle 名字
                    string assetBundleName = AssetDatabase.GetImplicitAssetBundleName(assetPath);

                    // 如果资源没有被分配到 AssetBundle，则跳过
                    if (string.IsNullOrEmpty(assetBundleName)) continue;

                    // 获取资源的名字
                    string assetName = Path.GetFileNameWithoutExtension(assetPath);

                    // 创建一个 AssetInfo 对象，并添加到列表中
                    ABArg assetInfo = new ABArg(assetBundleName, assetName);
                    aBSourcesRef.sourcesDic.Add(assetPath, assetInfo);

                }
            }

            Save(aBSourcesRef, component.assetRefName);
            AssetDatabase.Refresh();
        }

        public void Save(object obj, string fileName)
        {
            string SAVE_PATH = Application.streamingAssetsPath + component.assetRefSavePath;
            //先判断路径文件夹有没有
            if (!Directory.Exists(SAVE_PATH))
            {
                Directory.CreateDirectory(SAVE_PATH);
            }

            //可以对数据再做些操作，比如进行加密
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryFormatter bf = new BinaryFormatter();
                bf.Serialize(ms, obj);

                byte[] bytes = ms.GetBuffer();
                //ToDo:..在这里可以做一些加密的工作
                File.WriteAllBytes(SAVE_PATH + fileName + component.assetRefextension, bytes);
                ms.Close();
            }
            Debug.Log("数据已保存至：" + SAVE_PATH + fileName + component.assetRefextension);

        }
    }
}