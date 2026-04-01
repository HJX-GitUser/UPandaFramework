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

                // 确保目标目录存在
                if (!Directory.Exists(rule.targetPath))
                {
                    Directory.CreateDirectory(rule.targetPath);
                }

                // 清理目标目录
                if (config.clearTargetBeforeCopy)
                {
                    string[] existingFiles = Directory.GetFiles(rule.targetPath, "*.*");
                    foreach (string file in existingFiles)
                    {
                        File.Delete(file);
                    }
                }

                // 获取文件
                SearchOption searchOption = rule.includeSubfolders ?
                    SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                string[] files = Directory.GetFiles(rule.sourcePath, rule.filePattern, searchOption);

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
