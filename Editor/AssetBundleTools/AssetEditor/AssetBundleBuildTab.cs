using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UPandaGF.AssetBundleBrowser.AssetBundleDataSource;
using UPandaGF;
using UPandaGF.GFEditor;
using System;

namespace UPandaGF.AssetBundleBrowser
{
    [System.Serializable]
    public class AssetBundleBuildTab
    {
        const string k_BuildPrefPrefix = "ABBBuild:";
        private string outPutPath = "AssetBundles/";
        private string m_streamingPath = "Assets/StreamingAssets";

        [SerializeField]
        private bool m_AdvancedSettings;


        [SerializeField]
        private Vector2 m_ScrollPosition;

        class ToggleData
        {
            internal ToggleData(bool s,
                string title,
                string tooltip,
                List<string> onToggles,
                BuildAssetBundleOptions opt = BuildAssetBundleOptions.None)
            {
                if (onToggles.Contains(title))
                    state = true;
                else
                    state = s;
                content = new GUIContent(title, tooltip);
                option = opt;
            }
            //internal string prefsKey
            //{ get { return k_BuildPrefPrefix + content.text; } }
            internal bool state;
            internal GUIContent content;
            internal BuildAssetBundleOptions option;
        }

        private AssetBundleInspectTab m_InspectTab;
        private AssetBundleBrowserMain m_Main;

        private AssetBundleClassificationWindow assetBundleClassification;

        [SerializeField]
        private BuildTabData m_UserData;
        [NonSerialized] private bool m_CacheLoadFailed;
        public BuildTabData M_UserData => m_UserData;

        List<ToggleData> m_ToggleData;
        ToggleData m_ForceRebuild;
        ToggleData m_CopyToStreaming;
        GUIContent m_TargetContent;
        GUIContent m_CompressionContent;
        public enum CompressOptions
        {
            Uncompressed = 0,
            StandardCompression,
            ChunkBasedCompression,
        }
        GUIContent[] m_CompressionOptions =
        {
            new GUIContent("No Compression"),
            new GUIContent("Standard Compression (LZMA)"),
            new GUIContent("Chunk Based Compression (LZ4)")
        };
        int[] m_CompressionValues = { 0, 1, 2 };


        internal AssetBundleBuildTab()
        {
            m_AdvancedSettings = false;
            m_UserData = new BuildTabData();

            m_UserData.m_OnToggles = new List<string>();
            m_UserData.m_UseDefaultPath = true;
        }

        internal void OnDisable()
        {
            if (m_CacheLoadFailed) return;
            var dataPath = System.IO.Path.GetFullPath(".");
            dataPath = dataPath.Replace("\\", "/");
            dataPath += "/Library/AssetBundleBrowserBuild.dat";

            try { VersionedDataSerializer.WriteFile(dataPath, VersionedDataSerializer.Serialize(m_UserData, "ab-build-settings")); }
            catch (Exception e) { Debug.LogWarning("AB 构建配置保存失败，保留原文件：" + e.Message); }
        }

        internal void OnEnable(EditorWindow parent)
        {
            m_CacheLoadFailed = false;
            m_Main = (parent as AssetBundleBrowserMain);
            m_InspectTab = m_Main.m_InspectTab;
            //LoadData...
            var dataPath = System.IO.Path.GetFullPath(".");
            dataPath = dataPath.Replace("\\", "/");
            dataPath += "/Library/AssetBundleBrowserBuild.dat";
            //Debug.LogWarning("AssetBundleBrowser:" + dataPath);

            if (File.Exists(dataPath))
            {
                try
                {
                    var data = VersionedDataSerializer.LoadFile<BuildTabData>(dataPath, "ab-build-settings");
                    if (data != null) m_UserData = data;
                }
                catch (Exception e) { m_CacheLoadFailed = true; Debug.LogWarning("AB 构建配置读取失败，使用默认值并保留原文件：" + e.Message); }
            }
            if (m_UserData.m_OnToggles == null) m_UserData.m_OnToggles = new List<string>();

            m_ToggleData = new List<ToggleData>();
            m_ToggleData.Add(new ToggleData(
                false,
                "排除类型信息[Exclude Type Information]",
                "不要在资源包中包含类型信息（不要编写类型树）[Do not include type information within the asset bundle (don't write type tree).]",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.DisableWriteTypeTree));
            m_ToggleData.Add(new ToggleData(
                false,
                "强制重建[Force Rebuild]",
                "强制重新构建资源包[Force rebuild the asset bundles]",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.ForceRebuildAssetBundle));
            m_ToggleData.Add(new ToggleData(
                false,
                "忽略类型树更改[Ignore Type Tree Changes]",
                "在进行增量构建检查时，忽略类型树的变化[Ignore the type tree changes when doing the incremental build check.]",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.IgnoreTypeTreeChanges));
            m_ToggleData.Add(new ToggleData(
                false,
                "追加哈希[Append Hash]",
                "将哈希值附加到assetBundle名称上[Append the hash to the assetBundle name.]",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.AppendHashToAssetBundleName));
            m_ToggleData.Add(new ToggleData(
                false,
                "严格模式[Strict Mode]",
                "如果在构建过程中报告了任何错误，则不允许构建成功。[Do not allow the build to succeed if any errors are reporting during it.]",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.StrictMode));
            m_ToggleData.Add(new ToggleData(
                false,
                "试运行构建[Dry Run Build]",
                "进行一次模拟构建[Do a dry run build.]",
                m_UserData.m_OnToggles,
                BuildAssetBundleOptions.DryRunBuild));


            m_ForceRebuild = new ToggleData(
                false,
                "清理文件夹[Clear Folders]",
                "如果您选择将构建内容复制到StreamingAssets/AssetBundles，则此操作将清除构建目录以及该目录中所有内容。[Will wipe out all contents of build directory as well as StreamingAssets/AssetBundles if you are choosing to copy build there.]",
                m_UserData.m_OnToggles);
            m_CopyToStreaming = new ToggleData(
                false,
                "[Copy to StreamingAssets]",
                "[After build completes, will copy all build content to <StreamingAssets>/<OutputPath> (keeping the folder structure) for use in stand-alone player.]",
                m_UserData.m_OnToggles);

            m_TargetContent = new GUIContent("Build Target", "Choose target platform to build for.");
            m_CompressionContent = new GUIContent("压缩[Compression]", "Choose no compress, standard (LZMA), or chunk based (LZ4)");

            if (m_UserData.m_UseDefaultPath)
            {
                ResetPathToDefault();
            }
            if (assetBundleClassification == null) assetBundleClassification = new AssetBundleClassificationWindow();
            RepaintAssetBundleClassification();
        }

        public void RepaintAssetBundleClassification()
        {
            assetBundleClassification.ShowWindow(m_Main);
        }

        internal void OnGUI()
        {
            m_ScrollPosition = EditorGUILayout.BeginScrollView(m_ScrollPosition);
            bool newState = false;
            var centeredStyle = new GUIStyle(GUI.skin.GetStyle("Label"));
            centeredStyle.alignment = TextAnchor.UpperCenter;
            GUILayout.Label(new GUIContent("Example build setup"), centeredStyle);
            //basic options
            EditorGUILayout.Space();
            GUILayout.BeginVertical();

            // build target
            using (new EditorGUI.DisabledScope(!AssetBundleModel.Model.DataSource.CanSpecifyBuildTarget))
            {
                ValidBuildTarget tgt = (ValidBuildTarget)EditorGUILayout.EnumPopup(m_TargetContent, m_UserData.m_BuildTarget);
                if (tgt != m_UserData.m_BuildTarget)
                {
                    m_UserData.m_BuildTarget = tgt;
                    if (m_UserData.m_UseDefaultPath)
                    {
                        m_UserData.m_OutputPath = outPutPath;
                        m_UserData.m_OutputPath += m_UserData.m_BuildTarget.ToString();
                        //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
                    }
                }
            }


            ////output path
            using (new EditorGUI.DisabledScope(!AssetBundleModel.Model.DataSource.CanSpecifyBuildOutputDirectory))
            {
                EditorGUILayout.Space();
                GUILayout.BeginHorizontal();
                var newPath = EditorGUILayout.TextField("Output Path", m_UserData.m_OutputPath);
                if (!System.String.IsNullOrEmpty(newPath) && newPath != m_UserData.m_OutputPath)
                {
                    m_UserData.m_UseDefaultPath = false;
                    m_UserData.m_OutputPath = newPath;
                    //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
                }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Browse", GUILayout.MaxWidth(75f)))
                    BrowseForFolder();
                if (GUILayout.Button("Reset", GUILayout.MaxWidth(75f)))
                    ResetPathToDefault();
                //if (string.IsNullOrEmpty(m_OutputPath))
                //    m_OutputPath = EditorUserBuildSettings.GetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath");
                GUILayout.EndHorizontal();
                EditorGUILayout.Space();

                newState = GUILayout.Toggle(
                    m_ForceRebuild.state,
                    m_ForceRebuild.content);
                if (newState != m_ForceRebuild.state)
                {
                    if (newState)
                        m_UserData.m_OnToggles.Add(m_ForceRebuild.content.text);
                    else
                        m_UserData.m_OnToggles.Remove(m_ForceRebuild.content.text);
                    m_ForceRebuild.state = newState;
                }
                newState = GUILayout.Toggle(
                    m_CopyToStreaming.state,
                    m_CopyToStreaming.content);
                if (newState != m_CopyToStreaming.state)
                {
                    if (newState)
                        m_UserData.m_OnToggles.Add(m_CopyToStreaming.content.text);
                    else
                        m_UserData.m_OnToggles.Remove(m_CopyToStreaming.content.text);
                    m_CopyToStreaming.state = newState;
                }
            }

            // advanced options
            using (new EditorGUI.DisabledScope(!AssetBundleModel.Model.DataSource.CanSpecifyBuildOptions))
            {
                EditorGUILayout.Space();
                m_AdvancedSettings = EditorGUILayout.Foldout(m_AdvancedSettings, "高级设置[Advanced Settings]");
                if (m_AdvancedSettings)
                {
                    var indent = EditorGUI.indentLevel;
                    EditorGUI.indentLevel = 1;
                    CompressOptions cmp = (CompressOptions)EditorGUILayout.IntPopup(
                        m_CompressionContent,
                        (int)m_UserData.m_Compression,
                        m_CompressionOptions,
                        m_CompressionValues);

                    if (cmp != m_UserData.m_Compression)
                    {
                        m_UserData.m_Compression = cmp;
                    }
                    foreach (var tog in m_ToggleData)
                    {
                        newState = EditorGUILayout.ToggleLeft(
                            tog.content,
                            tog.state);
                        if (newState != tog.state)
                        {

                            if (newState)
                                m_UserData.m_OnToggles.Add(tog.content.text);
                            else
                                m_UserData.m_OnToggles.Remove(tog.content.text);
                            tog.state = newState;
                        }
                    }
                    EditorGUILayout.Space();
                    EditorGUI.indentLevel = indent;
                }
            }

            // build.
            EditorGUILayout.Space();
            //if (GUILayout.Button("Build Window"))
            //{
            //    //AssetBundleClassificationWindow.ShowWindow();
            //    //EditorApplication.delayCall += ExecuteBuild;
            //}

            assetBundleClassification.OnGUI();

            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }
        public void ExecuteBuild()
        {
            // 32 位 Windows 的产物目录与运行时默认的 StandaloneWindows64 不一致，先给出明确提示。
            if (m_UserData.m_BuildTarget == ValidBuildTarget.StandaloneWindows)
            {
                Debug.LogWarning("[AssetBundle] 当前选择的是 StandaloneWindows（32 位），产物目录为 AssetBundles/StandaloneWindows；"
                    + "框架运行时 Windows 默认查找 StandaloneWindows64。除明确要出 32 位包外，请改选 StandaloneWindows64。");
            }

            // 1. 记录开始时间（Ticks为100纳秒单位）
            long startTicks = System.DateTime.UtcNow.Ticks;
            string streamingTarget = null;
            bool streamingTargetCleared = false;

            if (AssetBundleModel.Model.DataSource.CanSpecifyBuildOutputDirectory)
            {
                if (string.IsNullOrEmpty(m_UserData.m_OutputPath))
                    BrowseForFolder();

                if (string.IsNullOrEmpty(m_UserData.m_OutputPath)) //in case they hit "cancel" on the open browser
                {
                    UnityEngine.Debug.LogError("AssetBundle Build: No valid output path for build.");
                    return;
                }

                // 必须在 BrowseForFolder 之后解析：否则空输出目录会算成 StreamingAssets 根，误清整目录。
                streamingTarget = GetStreamingTargetPath();
                if (m_ForceRebuild.state)
                {
                    bool deletedOutput = true, deletedStreaming = true;
                    // 清空输出目录：统一走破坏性操作护栏（拒绝工程根/Assets 根，确认目标）。
                    if (!EditorDestructiveGuard.ConfirmDelete(
                        "清空 AB 输出目录", m_UserData.m_OutputPath,
                        "重新构建前将清空 AB 输出目录下的全部内容。"))
                        deletedOutput = false;

                    // 只删本次的拷贝目标（<StreamingAssets>/<OutputPath>），绝不删整个 StreamingAssets。
                    if (m_CopyToStreaming.state && Directory.Exists(streamingTarget))
                        deletedStreaming = EditorDestructiveGuard.ConfirmDelete(
                            "清空 StreamingAssets 拷贝目标", streamingTarget,
                            "重新构建前将清空 StreamingAssets 下的 AB 拷贝目标（不会删除整个 StreamingAssets）。",
                            m_UserData.m_OutputPath);

                    try
                    {
                        if (deletedOutput && Directory.Exists(m_UserData.m_OutputPath))
                            Directory.Delete(m_UserData.m_OutputPath, true);

                        if (deletedStreaming && m_CopyToStreaming.state && Directory.Exists(streamingTarget))
                        {
                            Directory.Delete(streamingTarget, true);
                            streamingTargetCleared = true;
                        }
                    }
                    catch (System.Exception e)
                    {
                        UnityEngine.Debug.LogException(e);
                    }
                }
                if (!Directory.Exists(m_UserData.m_OutputPath))
                    Directory.CreateDirectory(m_UserData.m_OutputPath);
            }

            BuildAssetBundleOptions opt = GetOpt();

            ABBuildInfo buildInfo = new ABBuildInfo();

            buildInfo.outputDirectory = m_UserData.m_OutputPath;
            buildInfo.options = opt;
            buildInfo.buildTarget = (BuildTarget)m_UserData.m_BuildTarget;
            buildInfo.onBuild = (assetBundleName) =>
            {
                if (m_InspectTab == null)
                    return;
                m_InspectTab.AddBundleFolder(buildInfo.outputDirectory);
                m_InspectTab.RefreshBundles();
            };

            AssetBundleModel.Model.DataSource.BuildAssetBundles(buildInfo);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            if (m_CopyToStreaming.state)
            {
                if (string.IsNullOrEmpty(streamingTarget)) streamingTarget = GetStreamingTargetPath();
                int copiedFiles = 0;
                try { copiedFiles = Directory.GetFiles(m_UserData.m_OutputPath, "*", SearchOption.AllDirectories).Length; }
                catch (System.Exception) { }

                // force rebuild 已经确认过清空拷贝目标时不再重复询问。
                if (streamingTargetCleared || EditorDestructiveGuard.ConfirmOverwriteDirectory(
                    "复制 AB 到 StreamingAssets", streamingTarget, copiedFiles,
                    $"将把本次构建的 {copiedFiles} 个文件复制到 StreamingAssets，同名文件会被覆盖。"))
                {
                    DirectoryCopy(m_UserData.m_OutputPath, streamingTarget);
                }
                else
                {
                    UnityEngine.Debug.LogWarning("已跳过复制到 StreamingAssets（用户取消）；AB 仍保留在输出目录。");
                }
            }

            // 2. 计算耗时并输出（转换为毫秒）
            long endTicks = System.DateTime.UtcNow.Ticks;
            double durationMs = (endTicks - startTicks) / 10000.0; // 1 Tick = 100纳秒 → 1毫秒 = 10000 Ticks 1毫秒等于1,000,000纳秒
            UnityEngine.Debug.Log($"打包执行耗时：{(durationMs/1000):F2} 秒");
        }

        public BuildAssetBundleOptions GetOpt()
        {
            BuildAssetBundleOptions opt = BuildAssetBundleOptions.None;
            if (AssetBundleModel.Model.DataSource.CanSpecifyBuildOptions)
            {
                if (m_UserData.m_Compression == CompressOptions.Uncompressed)
                    opt |= BuildAssetBundleOptions.UncompressedAssetBundle;
                else if (m_UserData.m_Compression == CompressOptions.ChunkBasedCompression)
                    opt |= BuildAssetBundleOptions.ChunkBasedCompression;
                foreach (ToggleData tog in m_ToggleData)
                {
                    if (tog.state)
                        opt |= tog.option;
                }
            }
            UnityEngine.Debug.Log($"BuildAssetBundleOptions:{opt.ToString()}");
            return opt;
        }


        /// <summary>
        /// AB 包拷贝到 StreamingAssets 的目标目录：保留输出路径层级（如 Assets/StreamingAssets/AssetBundles/StandaloneWindows），
        /// 否则运行时按 LoadAssetPath 找不到包
        /// </summary>
        private string GetStreamingTargetPath()
        {
            string relative = (m_UserData.m_OutputPath ?? string.Empty).Replace("\\", "/");
            relative = relative.Trim(new char[] { System.IO.Path.AltDirectorySeparatorChar });
            string projectRoot = System.IO.Path.GetFullPath(".").Replace("\\", "/").TrimEnd(new char[] { System.IO.Path.AltDirectorySeparatorChar });

            if (relative.StartsWith(projectRoot + "/"))
                relative = relative.Substring(projectRoot.Length + 1);

            // 工程外的绝对路径：只保留最后一级目录名，避免拷到 StreamingAssets 之外
            if (System.IO.Path.IsPathRooted(relative))
            {
                int index = relative.LastIndexOf(System.IO.Path.AltDirectorySeparatorChar);
                relative = index >= 0 ? relative.Substring(index + 1) : relative;
            }

            if (relative.StartsWith("Assets/"))
                return relative;

            return m_streamingPath.TrimEnd(new char[] { System.IO.Path.AltDirectorySeparatorChar }) + "/" + relative;
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName)
        {
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            foreach (string folderPath in Directory.GetDirectories(sourceDirName, "*", SearchOption.AllDirectories))
            {
                if (!Directory.Exists(folderPath.Replace(sourceDirName, destDirName)))
                    Directory.CreateDirectory(folderPath.Replace(sourceDirName, destDirName));
            }

            foreach (string filePath in Directory.GetFiles(sourceDirName, "*.*", SearchOption.AllDirectories))
            {
                var fileDirName = Path.GetDirectoryName(filePath).Replace("\\", "/");
                var fileName = Path.GetFileName(filePath);
                string newFilePath = Path.Combine(fileDirName.Replace(sourceDirName, destDirName), fileName);

                File.Copy(filePath, newFilePath, true);
            }
        }

        private void BrowseForFolder()
        {
            m_UserData.m_UseDefaultPath = false;
            var newPath = EditorUtility.OpenFolderPanel("Bundle Folder", m_UserData.m_OutputPath, string.Empty);
            if (!string.IsNullOrEmpty(newPath))
            {
                var gamePath = System.IO.Path.GetFullPath(".");
                gamePath = gamePath.Replace("\\", "/");
                if (newPath.StartsWith(gamePath) && newPath.Length > gamePath.Length)
                    newPath = newPath.Remove(0, gamePath.Length + 1);
                m_UserData.m_OutputPath = newPath;
                //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
            }
        }
        private void ResetPathToDefault()
        {
            m_UserData.m_UseDefaultPath = true;
            m_UserData.m_OutputPath = outPutPath;
            m_UserData.m_OutputPath += m_UserData.m_BuildTarget.ToString();
            //EditorUserBuildSettings.SetPlatformSettings(EditorUserBuildSettings.activeBuildTarget.ToString(), "AssetBundleOutputPath", m_OutputPath);
        }

        //Note: this is the provided BuildTarget enum with some entries removed as they are invalid in the dropdown
        public enum ValidBuildTarget
        {
            //NoTarget = -2,        --doesn't make sense
            //iPhone = -1,          --deprecated
            //BB10 = -1,            --deprecated
            //MetroPlayer = -1,     --deprecated
            StandaloneOSXUniversal = 2,
            StandaloneOSXIntel = 4,
            StandaloneWindows = 5,
            WebPlayer = 6,
            WebPlayerStreamed = 7,
            iOS = 9,
            PS3 = 10,
            XBOX360 = 11,
            Android = 13,
            StandaloneLinux = 17,
            StandaloneWindows64 = 19,
            WebGL = 20,
            WSAPlayer = 21,
            StandaloneLinux64 = 24,
            StandaloneLinuxUniversal = 25,
            WP8Player = 26,
            StandaloneOSXIntel64 = 27,
            BlackBerry = 28,
            Tizen = 29,
            PSP2 = 30,
            PS4 = 31,
            PSM = 32,
            XboxOne = 33,
            SamsungTV = 34,
            N3DS = 35,
            WiiU = 36,
            tvOS = 37,
            Switch = 38
        }

        [System.Serializable]
        public class BuildTabData
        {
            public List<string> m_OnToggles;
            public ValidBuildTarget m_BuildTarget = ValidBuildTarget.StandaloneWindows64;
            public CompressOptions m_Compression = CompressOptions.StandardCompression;
            public string m_OutputPath = string.Empty;
            public bool m_UseDefaultPath = true;
        }
    }

}
