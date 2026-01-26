using AssetBundleBrowser;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF
{
    [CustomEditor(typeof(UPGameRoot))]
    public class UPGameRootEditor : Editor
    {
        AssetBundleBrowserMain assetBundleBrowserMain;
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
#if OPEN_PLOG
            if (!EditorApplication.isPlaying)
            {
                ShowArg("EnableDebugModel", "启动日志窗口");
                if (component.EnableDebugModel)
                {
                    if (component.reporter == null)
                    {
                        UnityEngine.Transform arg = component.GetComponentInChildren<DebugerInit>().transform;
                        CreateReporter(arg);
                        component.reporter = component.GetComponentInChildren<Reporter>();
                    }
                }
                else
                {
                    if (component.reporter != null)
                    {
                        DestroyImmediate(component.reporter.gameObject);
                        component.reporter = null;
                    }
                }
            }
#endif
            serializedObject.ApplyModifiedProperties();
        }

        private void AssetEditorGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("把资源添加到AssetBundle后，需要更新资源关联数据才能加载", MessageType.Info);
            if (GUILayout.Button("更新资源关联数据"))
            {
                //AssetBundleBuildTab.GenerateAssetBundleInfo();
            }
            EditorGUILayout.Space();
        }

        private void AssetbundlesEditorGUI()
        {
            ShowArg("enableAssetUpdate", "启动资源更新");
            if (component.enableAssetUpdate)
                ShowArg("assetUpdataConfig", "资源更新配置");
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            ShowArg("reomoteURL", "远程加载URL");
            ShowArg("LoadAssetPath", "资源路径");
            ShowArg("MainName", "主包名");
            ShowArg("MainPackageLoadPath", "主包加载路径");
            if (GUILayout.Button("重置"))
            {
                assetBundleBrowserMain = AssetBundleBrowserMain.instance;
                if (assetBundleBrowserMain != null)
                {
                    component.reomoteURL = "http://127.0.0.1:8090/";
                    string m_OutputPath = assetBundleBrowserMain.m_BuildTabData.m_OutputPath;
                    string result = "AssetBundles/";
                    int startIndex = m_OutputPath.IndexOf("AssetBundles/");
                    if (startIndex != -1)
                    {
                        result = m_OutputPath.Substring(startIndex);
                    }
                    component.LoadAssetPath = result;
                    component.MainName = result.Split('/')[1];
                    component.MainPackageLoadPath = ABLoadPath.StreamingAssetsPath; 
                }
            }
        }


        private void ShowArg(string argName, string inspectName)
        {
            SerializedProperty arg = serializedObject.FindProperty(argName);
            EditorGUILayout.PropertyField(arg, new GUIContent(inspectName));
        }


        public void CreateReporter(UnityEngine.Transform obj)
        {
            if (obj.gameObject.GetComponentInChildren<Reporter>() != null)
            {
                Debug.LogWarning("Reporter已创建！");
                return;
            };
            const int ReporterExecOrder = -12000;
            GameObject reporterObj = new GameObject();
            reporterObj.transform.SetParent(obj);
            reporterObj.name = "Reporter";
            Reporter reporter = reporterObj.AddComponent<Reporter>();
            reporterObj.AddComponent<ReporterMessageReceiver>();
            //reporterObj.AddComponent<TestReporter>();

            // Register root object for undo.
            Undo.RegisterCreatedObjectUndo(reporterObj, "Create Reporter Object");

            MonoScript reporterScript = MonoScript.FromMonoBehaviour(reporter);
            string reporterPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(reporterScript));

            if (MonoImporter.GetExecutionOrder(reporterScript) != ReporterExecOrder)
            {
                MonoImporter.SetExecutionOrder(reporterScript, ReporterExecOrder);
                //Debug.Log("Fixing exec order for " + reporterScript.name);
            }

            reporter.images = new Images();
            reporter.images.clearImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/clear.png"), typeof(Texture2D));
            reporter.images.collapseImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/collapse.png"), typeof(Texture2D));
            reporter.images.clearOnNewSceneImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/clearOnSceneLoaded.png"), typeof(Texture2D));
            reporter.images.showTimeImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/timer_1.png"), typeof(Texture2D));
            reporter.images.showSceneImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/UnityIcon.png"), typeof(Texture2D));
            reporter.images.userImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/user.png"), typeof(Texture2D));
            reporter.images.showMemoryImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/memory.png"), typeof(Texture2D));
            reporter.images.softwareImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/software.png"), typeof(Texture2D));
            reporter.images.dateImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/date.png"), typeof(Texture2D));
            reporter.images.showFpsImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/fps.png"), typeof(Texture2D));
            //reporter.images.graphImage           = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/chart.png"), typeof(Texture2D));
            reporter.images.infoImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/info.png"), typeof(Texture2D));
            reporter.images.saveLogsImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/Save.png"), typeof(Texture2D));
            reporter.images.searchImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/search.png"), typeof(Texture2D));
            reporter.images.copyImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/copy.png"), typeof(Texture2D));
            reporter.images.closeImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/close.png"), typeof(Texture2D));
            reporter.images.buildFromImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/buildFrom.png"), typeof(Texture2D));
            reporter.images.systemInfoImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/ComputerIcon.png"), typeof(Texture2D));
            reporter.images.graphicsInfoImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/graphicCard.png"), typeof(Texture2D));
            reporter.images.backImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/back.png"), typeof(Texture2D));
            reporter.images.logImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/log_icon.png"), typeof(Texture2D));
            reporter.images.warningImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/warning_icon.png"), typeof(Texture2D));
            reporter.images.errorImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/error_icon.png"), typeof(Texture2D));
            reporter.images.barImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/bar.png"), typeof(Texture2D));
            reporter.images.button_activeImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/button_active.png"), typeof(Texture2D));
            reporter.images.even_logImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/even_log.png"), typeof(Texture2D));
            reporter.images.odd_logImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/odd_log.png"), typeof(Texture2D));
            reporter.images.selectedImage = (Texture2D)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/selected.png"), typeof(Texture2D));

            reporter.images.reporterScrollerSkin = (GUISkin)AssetDatabase.LoadAssetAtPath(Path.Combine(reporterPath, "Images/reporterScrollerSkin.guiskin"), typeof(GUISkin));
        }
    }
}