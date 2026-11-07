using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        /// 把 AB 工具窗口的 AES 配置同步到组件的 config.AssetAESConfig 上。
        /// <para>只在实际有差异时才写 + SetDirty：避免白脏场景，也避免把面板上刚改的值静默冲掉；
        /// 这份值会在保存场景时随面板配置写入 GameRootConfig.json（打包运行时以该 Json 为准）。</para>
        /// </summary>
        private void SyncAesConfigFromJson()
        {
            AssetBundleClassificationWindowConfig fromWindow =
                UPandaGFConfig.LoadJsonConfig<AssetBundleClassificationWindowConfig>("AssetBundleBuildConfig");
            if (fromWindow == null) return;

            UPGameRootConfig rootConfig = component.Config;
            if (rootConfig == null) return;

            AssetBundleClassificationWindowConfig current = rootConfig.AssetAESConfig;
            if (current != null
                && current.enable == fromWindow.enable
                && current.AESKEY == fromWindow.AESKEY
                && current.AESIV == fromWindow.AESIV
                && current.mainBundleLoadPath == fromWindow.mainBundleLoadPath)
            {
                return;
            }

            rootConfig.AssetAESConfig = fromWindow;
            EditorUtility.SetDirty(component);   // 否则这次改动可能不会写进场景
        }

        public override void OnInspectorGUI()
        {
            //base.OnInspectorGUI();
            serializedObject.Update();
            EditorGUILayout.LabelField("UPGameRoot", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 用 SerializedProperty.enumValueIndex 来选分支：SerializedProperty 改的是"待应用的值"，
            // 直接读 component.Config.method 会慢一帧（切换后要再点一下才会显示对应区域）
            SerializedProperty methodProperty = ShowArg("config.method", "资源加载方式");
            AssetLoaddingMethod currentMethod = methodProperty != null
                ? (AssetLoaddingMethod)methodProperty.enumValueIndex
                : component.Config.method;

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
            DrawConfigFileBar();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>配置文件的路径提示与 读取 / 定位 按钮（写盘已自动化，不再提供保存按钮）</summary>
        private void DrawConfigFileBar()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("配置文件（打包运行时优先读取）", EditorStyles.boldLabel);

            bool exists = File.Exists(UPGameRootConfigFile.AbsolutePath);
            EditorGUILayout.LabelField($"{UPGameRootConfigFile.AssetPath}　{(exists ? "（已存在）" : "（尚未生成）")}", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(!exists))
            {
                if (GUILayout.Button("从 Json 读取")) LoadConfigFromJson();
            }
            if (GUILayout.Button("定位文件", GUILayout.Width(80f)))
            {
                if (exists) EditorUtility.RevealInFinder(UPGameRootConfigFile.AbsolutePath);
                else Debug.LogWarning($"[UPGameRootEditor] 配置文件尚未生成：{UPGameRootConfigFile.AssetPath}");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox("编辑器 Play 以面板为准（不读 Json）；面板改动在保存场景（Ctrl+S）时自动写入 Json，"
                                    + "构建前也会再写一次并按 Player 规则校验。打包运行时以这个 Json 为准。", MessageType.None);
        }

        /// <summary>从 Json 读回配置覆盖面板上的值（编辑器里把打包配置拉回面板的唯一入口）</summary>
        private void LoadConfigFromJson()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[UPGameRootEditor] 运行中配置已冻结，请退出 Play 后再从 Json 读取。");
                return;
            }
            try
            {
                string path = UPGameRootConfigFile.AbsolutePath;
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[UPGameRootEditor] 配置文件不存在：{UPGameRootConfigFile.AssetPath}，保存场景或构建时会自动生成");
                    return;
                }

                UPGameRootConfig loaded = UPGameRootConfigFile.FromJson(File.ReadAllText(path, Encoding.UTF8));
                if (loaded == null)
                {
                    Debug.LogError($"[UPGameRootEditor] 配置解析失败：{UPGameRootConfigFile.AssetPath}");
                    return;
                }

                Undo.RecordObject(component, "读取 GameRoot 配置");
                component.SetConfig(loaded);
                EditorUtility.SetDirty(component);
                serializedObject.Update();      // 让面板立刻显示新值
                Debug.Log($"[UPGameRootEditor] 配置已从 Json 读取：{UPGameRootConfigFile.AssetPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[UPGameRootEditor] 读取配置失败：{e}");
            }
        }

        private void AssetbundlesEditorGUI()
        {
            AssetBundleClassificationWindowConfig aesConfig = component.Config.AssetAESConfig;
            if (aesConfig != null && aesConfig.enable)
            {
                EditorGUILayout.HelpBox($"AES加密配置：\nkey:{aesConfig.AESKEY}\niv:{aesConfig.AESIV}", MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("清单 AES 加密：未启用（默认）", EditorStyles.miniLabel);
            }

            ShowArg("config.enableAssetUpdate", "启动资源更新");
            EditorGUILayout.Space();
            ShowArg("config.bundleTarget", "AB 目标平台（须与构建平台一致）");
            if (GUILayout.Button("跟随当前平台（bundleTarget + 相对路径）")) AlignToActiveBuildTarget();
            EditorGUILayout.Space();
            ShowArg("config.remoteURL", "远程加载URL");
            ShowArg("config.LoadAssetPath", "相对路径");
            ShowArg("config.downloadBatchTimeout", "下载等待超时（秒）");
            ShowArg("config.hotUpdate", "热更磁盘管理", true);
            ShowArg("config.AssetAESConfig", "清单 AES 配置", true);

            if (GUILayout.Button("重置相对路径"))
            {
                component.Config.LoadAssetPath = "AssetBundles/StandaloneWindows/";
                EditorUtility.SetDirty(component);
            }
        }

        /// <summary>
        /// 把 bundleTarget / LoadAssetPath 对齐到当前激活平台。
        /// <para>这两项必须与实际构建平台一致，否则构建时被 FrameworkConfigBuildValidator 拦下，
        /// 且 Player 启动的 Configuration 步骤也会因同一项校验失败而进不了 GFLoadedEvent。</para>
        /// </summary>
        private void AlignToActiveBuildTarget()
        {
            SerializedProperty bundleTargetProperty = serializedObject.FindProperty("config.bundleTarget");
            SerializedProperty loadPathProperty = serializedObject.FindProperty("config.LoadAssetPath");
            if (bundleTargetProperty == null || loadPathProperty == null)
            {
                Debug.LogError("[UPGameRootEditor] 找不到 config.bundleTarget / config.LoadAssetPath 字段，请同步更新本工具");
                return;
            }

            string bundleTarget = UPandaGF.GFEditor.FrameworkBuildPipeline.BundleTargetName(EditorUserBuildSettings.activeBuildTarget);
            bundleTargetProperty.stringValue = bundleTarget;
            // 只接管默认的 "AssetBundles/<目标>/" 形态，自定义输出目录不覆盖
            if (string.IsNullOrEmpty(loadPathProperty.stringValue)
                || loadPathProperty.stringValue.StartsWith("AssetBundles/", StringComparison.Ordinal))
            {
                loadPathProperty.stringValue = "AssetBundles/" + bundleTarget + "/";
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
            Debug.Log($"[UPGameRootEditor] 已对齐到当前平台 {bundleTarget}"
                + $"（相对路径 {loadPathProperty.stringValue}）；保存场景时会自动写回 {UPGameRootConfigFile.AssetPath}。");
        }

        /// <summary>
        /// 画一个序列化字段。
        /// <para>⚠ 字段名是字符串：UPGameRoot 里的字段改名 / 删除后 FindProperty 会返回 null，
        /// 而把 null 丢给 EditorGUILayout.PropertyField 会在 Unity 内部抛 NullReferenceException
        /// （Inspector 直接报错、面板画不出来）。这里统一兜住，并给出能直接定位的提示。</para>
        /// </summary>
        /// <returns>找到的 SerializedProperty；没找到返回 null</returns>
        private SerializedProperty ShowArg(string argName, string inspectName, bool includeChildren = false)
        {
            SerializedProperty arg = serializedObject.FindProperty(argName);
            if (arg == null)
            {
                if (reportedMissingFields.Add(argName))
                {
                    Debug.LogError($"[UPGameRootEditor] 找不到序列化字段「{argName}」：UPGameRoot / UPGameRootConfig 里的字段可能被改名或删掉了，请同步更新本工具");
                }
                EditorGUILayout.HelpBox($"字段「{argName}」不存在（已改名 / 删除？），请检查 UPGameRootEditor", MessageType.Error);
                return null;
            }

            EditorGUILayout.PropertyField(arg, new GUIContent(inspectName), includeChildren);
            return arg;
        }

        /// <summary>按 EnableDebugModel 开关同步场景里的 Reporter（创建 / 销毁）</summary>
        private void SyncReporter()
        {
#if OPEN_PLOG
            if (EditorApplication.isPlaying) return;   // 运行时不改场景

            ShowArg("config.EnableDebugModel", "启动日志窗口");
            if (component.Config.EnableDebugModel)
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
            if (component.Config.EnableDebugModel) DestroyReporterIfExists();
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
