using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    [CustomEditor(typeof(UPGameRoot))]
    public class UPGameRootEditor : UnityEditor.Editor
    {
        UPGameRoot component;

        /// <summary>已经报过错的字段名（避免 OnGUI 每帧刷错误日志）</summary>
        private static readonly HashSet<string> reportedMissingFields = new HashSet<string>();

        private void OnEnable()
        {
            component = (UPGameRoot)target;
            SyncAesConfigFromJson();
        }

        /// <summary>
        /// 把 AB 工具窗口的配置同步到组件的清单 AES 开关上。
        /// <para>只在实际有差异时才写 + SetDirty：旧实现在每次 OnEnable 都无条件覆盖，
        /// 既会白脏场景，也会把场景里手改的值静默冲掉；配置文件损坏（FromJson 返回 null）时也不动。 </para>
        /// </summary>
        private void SyncAesConfigFromJson()
        {
            AssetBundleClassificationWindowConfig config =
                UPandaGFConfig.LoadJsonConfig<AssetBundleClassificationWindowConfig>("AssetBundleBuildConfig");
            if (config == null) return;

            AssetBundleClassificationWindowConfig current = component.AssetAESConfig;
            if (current != null
                && current.enable == config.enable
                && current.AESKEY == config.AESKEY
                && current.AESIV == config.AESIV
                && current.mainBundleLoadPath == config.mainBundleLoadPath)
            {
                return;
            }

            component.AssetAESConfig = config;
            EditorUtility.SetDirty(component);   // 否则这次改动可能不会写进场景
        }

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUILayout.LabelField("UPGameRoot", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 用 SerializedProperty.enumValueIndex 来选分支：SerializedProperty 改的是"待应用的值"，
            // 直接读 component.method 会慢一帧（切换后要再点一下才会显示对应区域）
            SerializedProperty methodProperty = ShowArg("method", "资源加载方式");
            AssetLoaddingMethod currentMethod = methodProperty != null
                ? (AssetLoaddingMethod)methodProperty.enumValueIndex
                : component.method;

            switch (currentMethod)
            {
                case AssetLoaddingMethod.Editor:
                    EditorGUILayout.Space();
                    break;

                case AssetLoaddingMethod.Assetbundles:
                    AssetbundlesEditorGUI();
                    break;
            }

            EditorGUILayout.Space(10);
            SyncReporter();
            serializedObject.ApplyModifiedProperties();
        }

        private void AssetbundlesEditorGUI()
        {
            AssetBundleClassificationWindowConfig aesConfig = component.AssetAESConfig;
            if (aesConfig != null && aesConfig.enable)
            {
                EditorGUILayout.HelpBox($"AES加密配置：\nkey:{aesConfig.AESKEY}\niv:{aesConfig.AESIV}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("清单 AES 加密：未启用（默认）", EditorStyles.miniLabel);
            }

            ShowArg("enableAssetUpdate", "启动资源更新");
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            ShowArg("remoteURL", "远程加载URL");
            ShowArg("LoadAssetPath", "相对路径");
            ShowArg("downloadBatchTimeout", "下载等待超时（秒）");
            if (GUILayout.Button("重置"))
            {
                component.LoadAssetPath = "AssetBundles/StandaloneWindows/";
                EditorUtility.SetDirty(component);
            }
        }

        /// <summary>
        /// 画一个序列化字段。
        /// <para>⚠ 字段名是字符串：UPGameRoot 里的字段改名 / 删除后 FindProperty 会返回 null，
        /// 而把 null 丢给 EditorGUILayout.PropertyField 会在 Unity 内部抛 NullReferenceException
        /// （Inspector 直接报错、面板画不出来）。这里统一兜住，并给出能直接定位的提示。</para>
        /// </summary>
        /// <returns>找到的 SerializedProperty；没找到返回 null</returns>
        private SerializedProperty ShowArg(string argName, string inspectName)
        {
            SerializedProperty arg = serializedObject.FindProperty(argName);
            if (arg == null)
            {
                if (reportedMissingFields.Add(argName))
                {
                    Debug.LogError($"[UPGameRootEditor] 找不到序列化字段「{argName}」：UPGameRoot 里的字段可能被改名或删掉了，请同步更新本工具");
                }
                EditorGUILayout.HelpBox($"字段「{argName}」不存在（已改名 / 删除？），请检查 UPGameRootEditor", MessageType.Error);
                return null;
            }

            EditorGUILayout.PropertyField(arg, new GUIContent(inspectName));
            return arg;
        }

        /// <summary>按 EnableDebugModel 开关同步场景里的 Reporter（创建 / 销毁）</summary>
        private void SyncReporter()
        {
#if OPEN_PLOG
            if (EditorApplication.isPlaying) return;   // 运行时不改场景

            ShowArg("EnableDebugModel", "启动日志窗口");
            if (component.EnableDebugModel)
            {
                if (component.reporter == null)
                {
                    DebugerInit debugerInit = component.GetComponentInChildren<DebugerInit>(true);
                    if (debugerInit == null)
                    {
                        EditorGUILayout.HelpBox("找不到 DebugerInit 子物体，无法创建 Reporter", MessageType.Warning);
                        return;
                    }

                    CreateReporter(debugerInit.transform);
                    component.reporter = component.GetComponentInChildren<Reporter>(true);
                    EditorUtility.SetDirty(component);
                }
                EditorGUILayout.HelpBox("启动该选项，在运行时可以点击左上角按钮启动日志面板，用于打包项目时调试，正式包取消勾选", MessageType.Info);
            }
            else
            {
                DestroyReporterIfExists();
            }
#else
            if (component.EnableDebugModel) DestroyReporterIfExists();
#endif
        }

        /// <summary>
        /// 删除场景里的 Reporter。
        /// <para>不用 DestroyImmediate：在 OnInspectorGUI 绘制期间直接销毁对象容易和正在绘制的界面打架，
        /// 延后到本次 GUI 事件处理完再销毁，并走 Undo 以便反悔。</para>
        /// </summary>
        private void DestroyReporterIfExists()
        {
            Reporter reporter = component.reporter;
            if (reporter == null) return;

            component.reporter = null;
            EditorUtility.SetDirty(component);

            EditorApplication.delayCall += () =>
            {
                if (reporter != null)
                    Undo.DestroyObjectImmediate(reporter.gameObject);
            };
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
