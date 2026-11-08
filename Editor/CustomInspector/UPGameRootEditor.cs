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
            SyncLogConsole();
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
                component.Config.LoadAssetPath = "AssetBundles/StandaloneWindows64/";
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

        /// <summary>按 EnableDebugModel 开关同步场景里的日志控制台（创建 / 销毁）。</summary>
        private void SyncLogConsole()
        {
#if OPEN_PLOG
            if (EditorApplication.isPlaying) return;   // 运行时不改场景

            ShowArg("config.EnableDebugModel", "启动日志窗口");
            if (component.Config.EnableDebugModel)
            {
                if (component.logConsole == null)
                {
                    DebugerInit debugerInit = component.GetComponentInChildren<DebugerInit>(true);
                    if (debugerInit == null)
                    {
                        EditorGUILayout.HelpBox("找不到 DebugerInit 子物体，无法创建日志控制台", MessageType.Warning);
                        return;
                    }

                    CreateLogConsole(debugerInit.transform);
                    component.logConsole = component.GetComponentInChildren<UPandaLogConsole>(true);
                    EditorUtility.SetDirty(component);
                }
                EditorGUILayout.HelpBox("启动该选项后，运行时点击左上角 FPS 按钮可打开日志控制台；正式包建议关闭。", MessageType.Info);
            }
            else
            {
                DestroyLogConsoleIfExists();
            }
#else
            if (component.Config.EnableDebugModel) DestroyLogConsoleIfExists();
#endif
        }

        /// <summary>
        /// 删除场景里的日志控制台。
        /// <para>延后到本次 GUI 事件结束再销毁，并走 Undo，避免在 OnInspectorGUI 绘制期间直接销毁对象。</para>
        /// </summary>
        private void DestroyLogConsoleIfExists()
        {
            UPandaLogConsole console = component.logConsole;
            if (console == null) return;

            component.logConsole = null;
            EditorUtility.SetDirty(component);

            EditorApplication.delayCall += () =>
            {
                if (console != null)
                    Undo.DestroyObjectImmediate(console.gameObject);
            };
        }

        public void CreateLogConsole(Transform parent)
        {
            if (parent.gameObject.GetComponentInChildren<UPandaLogConsole>() != null)
            {
                Debug.LogWarning("日志控制台已创建！");
                return;
            }

            GameObject consoleObject = new GameObject("LogConsole");
            consoleObject.transform.SetParent(parent, false);
            var console = consoleObject.AddComponent<UPandaLogConsole>();
            LogConsoleIconLoader.Assign(console);
            Undo.RegisterCreatedObjectUndo(consoleObject, "Create Log Console");

            MonoScript script = MonoScript.FromMonoBehaviour(console);
            if (MonoImporter.GetExecutionOrder(script) != -12000)
                MonoImporter.SetExecutionOrder(script, -12000);
        }
    }
}
