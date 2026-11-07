using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Text;
namespace UPandaGF.GFEditor
{
    public class DLLCopyConfigWindow : EditorWindow
    {
        [System.Serializable]
        public class CopyRule
        {
            public string sourcePath;
            public string targetPath;
            public bool renameToBytes = true;
            public bool includeSubfolders = true;
            public string filePattern = "*.dll";
        }

        [System.Serializable]
        public class CopyConfig
        {
            public List<CopyRule> rules = new List<CopyRule>();
            public bool clearTargetBeforeCopy = true;
        }

        private CopyConfig config = new CopyConfig();
        private Vector2 scrollPos;
        private string fileName = "DLLCopyConfig";

        [MenuItem("UPandaGF/Tools/HybridCLR DLL Copy Tool")]
        public static void ShowWindow()
        {
            GetWindow<DLLCopyConfigWindow>("DLL Copy Config");
        }

        private void OnEnable()
        {
            config = UPandaGFConfig.LoadJsonConfig<CopyConfig>(fileName);
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("DLL复制配置", EditorStyles.boldLabel);

            EditorGUILayout.Space(5);
            config.clearTargetBeforeCopy = EditorGUILayout.Toggle("复制前清空目标目录", config.clearTargetBeforeCopy);

            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField("复制规则", EditorStyles.boldLabel);

            // 规则列表
            for (int i = 0; i < config.rules.Count; i++)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.LabelField($"规则 {i + 1}", EditorStyles.boldLabel);

                CopyRule rule = config.rules[i];

                // 源路径
                EditorGUILayout.BeginHorizontal();
                rule.sourcePath = EditorGUILayout.TextField("源路径", rule.sourcePath);
                if (GUILayout.Button("浏览", GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFolderPanel("选择源目录", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        rule.sourcePath = path;
                    }
                }
                EditorGUILayout.EndHorizontal();

                // 目标路径
                EditorGUILayout.BeginHorizontal();
                rule.targetPath = EditorGUILayout.TextField("目标路径", rule.targetPath);
                if (GUILayout.Button("浏览", GUILayout.Width(60)))
                {
                    string path = EditorUtility.OpenFolderPanel("选择目标目录", Application.dataPath, "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        rule.targetPath = path;
                    }
                }
                EditorGUILayout.EndHorizontal();

                // 其他选项
                rule.renameToBytes = EditorGUILayout.Toggle("重命名为.bytes", rule.renameToBytes);
                rule.includeSubfolders = EditorGUILayout.Toggle("包含子文件夹", rule.includeSubfolders);
                rule.filePattern = EditorGUILayout.TextField("文件匹配模式", rule.filePattern);

                // 删除按钮
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("删除规则", GUILayout.Width(100)))
                {
                    config.rules.RemoveAt(i);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            // 添加规则按钮
            if (GUILayout.Button("添加新规则"))
            {
                config.rules.Add(new CopyRule());
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(20);

            // 操作按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("保存配置", GUILayout.Height(30)))
            {
                UPandaGFConfig.SaveJsonConfig(config, fileName);
            }

            if (GUILayout.Button("加载配置", GUILayout.Height(30)))
            {
                config = UPandaGFConfig.LoadJsonConfig<CopyConfig>(fileName);
            }

            if (GUILayout.Button("执行复制", GUILayout.Height(40)))
            {
                ExecuteCopy();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 快速操作
            if (GUILayout.Button("快速设置 HyBridCLR 路径"))
            {
                SetupHybridCLRPaths();
            }
        }

        private void ExecuteCopy()
        {
            int totalSuccess = 0;
            int totalFiles = 0;

            foreach (var rule in config.rules)
            {
                if (string.IsNullOrEmpty(rule.sourcePath) || !Directory.Exists(rule.sourcePath))
                {
                    Debug.LogWarning($"源路径不存在: {rule.sourcePath}");
                    continue;
                }

                if (string.IsNullOrEmpty(rule.targetPath))
                {
                    Debug.LogWarning($"目标路径为空");
                    continue;
                }

                if (EditorDestructiveGuard.PathsOverlap(rule.sourcePath, rule.targetPath))
                {
                    Debug.LogError($"源目录与目标目录不能相同或互相包含，已跳过规则。源：{rule.sourcePath} 目标：{rule.targetPath}");
                    continue;
                }

                // 先枚举源文件：源为空/路径写错时绝不能先清空目标。
                SearchOption searchOption = rule.includeSubfolders ?
                    SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                string[] files = Directory.GetFiles(rule.sourcePath, rule.filePattern, searchOption);
                if (files.Length == 0)
                {
                    Debug.LogWarning($"源目录没有匹配 {rule.filePattern} 的文件，已跳过（未清空目标）：{rule.sourcePath}");
                    continue;
                }

                // 确保目标目录存在
                if (!Directory.Exists(rule.targetPath))
                {
                    Directory.CreateDirectory(rule.targetPath);
                }

                // 清理目标目录：确认 + 只清顶层（与原行为保持一致）
                if (config.clearTargetBeforeCopy)
                {
                    if (!EditorDestructiveGuard.ConfirmDelete(
                        "清空 DLL 目标目录",
                        rule.targetPath,
                        $"将删除目标目录顶层的全部文件，然后复制 {files.Length} 个文件。",
                        rule.sourcePath))
                        continue;

                    string[] existingFiles = Directory.GetFiles(rule.targetPath, "*.*");
                    foreach (string file in existingFiles)
                    {
                        File.Delete(file);
                    }
                }

                int success = 0;
                foreach (string file in files)
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        string targetFile;

                        if (rule.renameToBytes)
                        {
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(file);
                            targetFile = Path.Combine(rule.targetPath, nameWithoutExt + ".dll.bytes");
                        }
                        else
                        {
                            targetFile = Path.Combine(rule.targetPath, fileName);
                        }

                        File.Copy(file, targetFile, true);
                        Debug.Log($"已复制: {fileName} -> {targetFile}");
                        success++;
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"复制失败 {file}: {e.Message}");
                    }
                }

                totalSuccess += success;
                totalFiles += files.Length;
                Debug.Log($"规则完成: {success}/{files.Length} 个文件");
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("完成",
                $"所有规则执行完成！\n总共成功: {totalSuccess}/{totalFiles} 个文件",
                "确定");
        }

        private void SetupHybridCLRPaths()
        {
            // 添加HyBridCLR默认路径
            CopyRule rule = new CopyRule
            {
                sourcePath = Path.Combine(Application.dataPath, "HybridCLRData", "HotUpdateDlls", "StandaloneWindows64"),
                targetPath = Path.Combine(Application.dataPath, "StreamingAssets", "HotUpdate"),
                renameToBytes = true,
                includeSubfolders = false,
                filePattern = "*.dll"
            };

            config.rules.Add(rule);
            UPandaGFConfig.SaveJsonConfig(config, fileName);
        }


    }
}
