using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;

namespace UPandaGF.ResourcePathCopyTool
{
    public class ResourcePathCopy
    {
        private const string ResourcesFolderName = "Resources/";
        private const string ResourcesFolderPath = "Assets/Resources/";

        [MenuItem("Assets/Copy Resource Path", false, 20)]
        private static void CopyResourcePath()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
                return;

            List<string> paths = new List<string>();

            foreach (var obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);

                if (string.IsNullOrEmpty(path))
                    continue;

                // 获取处理后的路径
                string resourcePath = GetResourcePath(path);

                if (!string.IsNullOrEmpty(resourcePath))
                {
                    paths.Add(resourcePath);
                }
            }

            if (paths.Count > 0)
            {
                string combinedPath = string.Join("\n", paths);
                EditorGUIUtility.systemCopyBuffer = combinedPath;

                Debug.Log($"已复制 {paths.Count} 个资源路径到剪贴板:\n{combinedPath}");

                // 显示提示
                EditorUtility.DisplayDialog("复制成功",
                    $"已复制 {paths.Count} 个资源路径到剪贴板", "确定");
            }
        }

        [MenuItem("Assets/Copy Resource Path", true, 20)]
        private static bool ValidateCopyResourcePath()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
                return false;

            // 检查至少有一个资源在Resources目录下
            foreach (var obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && IsInResourcesFolder(path))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断路径是否在Resources目录下
        /// </summary>
        public static bool IsInResourcesFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            // 检查路径是否包含Resources目录
            int resourcesIndex = assetPath.IndexOf(ResourcesFolderPath);
            if (resourcesIndex >= 0)
                return true;

            // 也检查其他Resources目录（可能存在多个）
            int resourcesIndex2 = assetPath.IndexOf(ResourcesFolderName);
            return resourcesIndex2 >= 0;
        }

        /// <summary>
        /// 获取Resource相对路径（不包含后缀）
        /// </summary>
        public static string GetResourcePath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            // 找到Resources/在路径中的位置
            int resourcesIndex = assetPath.IndexOf(ResourcesFolderName);
            if (resourcesIndex < 0)
            {
                Debug.LogWarning($"资源不在Resources目录下: {assetPath}");
                return null;
            }

            // 获取Resources/后面的路径
            int startIndex = resourcesIndex + ResourcesFolderName.Length;
            if (startIndex >= assetPath.Length)
                return null;

            string relativePath = assetPath.Substring(startIndex);

            // 去除文件后缀
            int dotIndex = relativePath.LastIndexOf('.');
            if (dotIndex > 0)
            {
                relativePath = relativePath.Substring(0, dotIndex);
            }

            return relativePath;
        }

        //[MenuItem("Assets/Copy Resource Path with Subfolder", false, 21)]
        private static void CopyResourcePathWithSubfolders()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
                return;

            Dictionary<string, List<string>> groupedPaths = new Dictionary<string, List<string>>();

            foreach (var obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);

                if (string.IsNullOrEmpty(path))
                    continue;

                // 获取Resources目录
                string resourcesFolder = GetResourcesFolder(path);
                if (string.IsNullOrEmpty(resourcesFolder))
                    continue;

                // 获取处理后的路径
                string resourcePath = GetResourcePath(path);

                if (!string.IsNullOrEmpty(resourcePath))
                {
                    if (!groupedPaths.ContainsKey(resourcesFolder))
                    {
                        groupedPaths[resourcesFolder] = new List<string>();
                    }
                    groupedPaths[resourcesFolder].Add(resourcePath);
                }
            }

            if (groupedPaths.Count > 0)
            {
                List<string> outputLines = new List<string>();
                foreach (var kvp in groupedPaths)
                {
                    outputLines.Add($"// Resources Folder: {kvp.Key}");
                    foreach (var path in kvp.Value)
                    {
                        outputLines.Add(path);
                    }
                    outputLines.Add(""); // 空行分隔
                }

                string result = string.Join("\n", outputLines).Trim();
                EditorGUIUtility.systemCopyBuffer = result;

                Debug.Log($"已复制 {groupedPaths.Values.Sum(list => list.Count)} 个资源路径（按文件夹分组）");
            }
        }

        /// <summary>
        /// 获取Resources文件夹路径
        /// </summary>
        private static string GetResourcesFolder(string assetPath)
        {
            int resourcesIndex = assetPath.IndexOf(ResourcesFolderName);
            if (resourcesIndex < 0)
                return null;

            // 返回完整的Resources文件夹路径
            return assetPath.Substring(0, resourcesIndex + ResourcesFolderName.Length - 1);
        }
    }

    /// <summary>
    /// 扩展编辑器窗口，在Project视图中显示资源路径
    /// </summary>
    public class ResourcePathDisplay : EditorWindow
    {
        private Vector2 scrollPosition;
        private static List<string> recentCopiedPaths = new List<string>();
        private const int MaxRecentPaths = 10;

        [MenuItem("UPandaGF/Tools/Resource/Resource Path Helper", false, 100)]
        public static void ShowWindow()
        {
            var window = GetWindow<ResourcePathDisplay>("Resource Path Helper");
            window.minSize = new Vector2(400, 300);
        }

        private static void CopyResourcePathAdvanced()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
                return;

            List<string> allPaths = new List<string>();
            List<string> csharpPaths = new List<string>();
            List<string> resourcePaths = new List<string>();

            foreach (var obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);

                if (string.IsNullOrEmpty(path))
                    continue;

                // 原始路径
                allPaths.Add(path);

                // Resources相对路径
                string resourcePath = ResourcePathCopy.GetResourcePath(path);
                if (!string.IsNullOrEmpty(resourcePath))
                {
                    resourcePaths.Add(resourcePath);
                }

                // C#代码格式路径
                string csharpPath = GetCSharpPath(resourcePath);
                if (!string.IsNullOrEmpty(csharpPath))
                {
                    csharpPaths.Add(csharpPath);
                }
            }

            // 创建多格式输出
            string output = "";

            if (allPaths.Count > 0)
            {
                output += "=== 原始路径 ===\n" + string.Join("\n", allPaths) + "\n\n";
            }

            if (resourcePaths.Count > 0)
            {
                output += "=== Resources相对路径 ===\n" + string.Join("\n", resourcePaths) + "\n\n";

                // 添加到最近复制记录
                recentCopiedPaths.InsertRange(0, resourcePaths);
                if (recentCopiedPaths.Count > MaxRecentPaths)
                {
                    recentCopiedPaths = recentCopiedPaths.Take(MaxRecentPaths).ToList();
                }
            }

            if (csharpPaths.Count > 0)
            {
                output += "=== C#代码格式 ===\n" + string.Join("\n", csharpPaths) + "\n\n";
            }

            if (!string.IsNullOrEmpty(output))
            {
                EditorGUIUtility.systemCopyBuffer = output.Trim();
                Debug.Log($"已复制 {selectedObjects.Length} 个资源的多种格式路径");

                // 显示提示窗口
                ShowCopyResultWindow(selectedObjects.Length, resourcePaths);
            }
        }

        /// <summary>
        /// 获取C#代码格式的路径
        /// </summary>
        private static string GetCSharpPath(string resourcePath)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return null;

            // 将路径转换为有效的C#标识符
            string[] parts = resourcePath.Split('/');
            List<string> validParts = new List<string>();

            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                // 移除非法字符，只保留字母、数字、下划线
                string validPart = Regex.Replace(part, @"[^a-zA-Z0-9_]", "");
                if (!string.IsNullOrEmpty(validPart))
                {
                    // 确保不以数字开头
                    if (char.IsDigit(validPart[0]))
                    {
                        validPart = "_" + validPart;
                    }
                    validParts.Add(validPart);
                }
            }

            if (validParts.Count == 0)
                return $"\"{resourcePath}\"";

            // 转换为常量形式
            string constantName = string.Join("_", validParts).ToUpper();
            return $"public const string {constantName} = \"{resourcePath}\";";
        }

        /// <summary>
        /// 显示复制结果窗口
        /// </summary>
        private static void ShowCopyResultWindow(int count, List<string> paths)
        {
            if (EditorUtility.DisplayDialog("复制成功",
                $"已复制 {count} 个资源路径\n\n" +
                $"示例: {paths.FirstOrDefault() ?? "N/A"}\n\n" +
                "是否打开路径助手查看详情？",
                "查看", "关闭"))
            {
                ShowWindow();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            // 标题
            EditorGUILayout.LabelField("Resource Path Helper", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("右键点击Resources目录下的资源，选择菜单复制路径", MessageType.Info);

            EditorGUILayout.Space(20);

            // 快速操作按钮
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("复制选中资源路径", GUILayout.Height(30)))
            {
                CopyResourcePathAdvanced();
            }
            if (GUILayout.Button("刷新选中资源", GUILayout.Height(30)))
            {
                RefreshSelectedResources();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(20);

            // 最近复制的路径
            EditorGUILayout.LabelField("最近复制的路径", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));

            if (recentCopiedPaths.Count == 0)
            {
                EditorGUILayout.HelpBox("暂无记录\n请右键点击Resources目录下的资源并选择复制", MessageType.Warning);
            }
            else
            {
                for (int i = 0; i < recentCopiedPaths.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();

                    // 序号
                    EditorGUILayout.LabelField($"{i + 1}.", GUILayout.Width(30));

                    // 路径
                    EditorGUILayout.SelectableLabel(recentCopiedPaths[i],
                        EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                    // 复制按钮
                    if (GUILayout.Button("复制", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.systemCopyBuffer = recentCopiedPaths[i];
                        Debug.Log($"已复制: {recentCopiedPaths[i]}");
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            // 清空按钮
            if (recentCopiedPaths.Count > 0 && GUILayout.Button("清空记录", GUILayout.Height(25)))
            {
                recentCopiedPaths.Clear();
            }
        }

        /// <summary>
        /// 刷新并显示选中的资源
        /// </summary>
        private void RefreshSelectedResources()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                Debug.Log("请先在Project窗口中选择资源");
                return;
            }

            List<string> resources = new List<string>();
            foreach (var obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && ResourcePathCopy.IsInResourcesFolder(path))
                {
                    string resourcePath = ResourcePathCopy.GetResourcePath(path);
                    if (!string.IsNullOrEmpty(resourcePath))
                    {
                        resources.Add($"{obj.name}: {resourcePath}");
                    }
                }
            }

            if (resources.Count > 0)
            {
                string result = "选中的Resources资源:\n" + string.Join("\n", resources);
                EditorGUIUtility.systemCopyBuffer = result;
                Debug.Log(result);
            }
        }

        //[InitializeOnLoadMethod]
        //private static void Initialize()
        //{
        //    // 注册Project窗口的右键菜单
        //    EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
        //}

        ///// <summary>
        ///// 在Project窗口的项目项上绘制GUI
        ///// </summary>
        //private static void OnProjectWindowItemGUI(string guid, Rect selectionRect)
        //{
        //    // 只在右键点击时显示
        //    if (Event.current.type == EventType.MouseDown && Event.current.button == 1 &&
        //        selectionRect.Contains(Event.current.mousePosition))
        //    {
        //        string path = AssetDatabase.GUIDToAssetPath(guid);
        //        if (!string.IsNullOrEmpty(path) && ResourcePathCopy.IsInResourcesFolder(path))
        //        {
        //            // 创建菜单
        //            GenericMenu menu = new GenericMenu();

        //            menu.AddItem(new GUIContent("Copy Resource Path"), false, () =>
        //            {
        //                AssetDatabase.Refresh();
        //                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
        //                if (obj != null)
        //                {
        //                    Selection.activeObject = obj;
        //                    CopyResourcePathAdvanced();
        //                }
        //            });

        //            menu.ShowAsContext();
        //            Event.current.Use();
        //        }
        //    }
        //}
    }

    /// <summary>
    /// 自定义Project窗口的右键菜单扩展
    /// </summary>
    public class ProjectWindowExtension
    {
        [MenuItem("Assets/Copy Full Path", false, 19)]
        private static void CopyFullPath()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
                return;

            List<string> paths = new List<string>();

            foreach (var obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    // 转换为绝对路径
                    string fullPath = Path.GetFullPath(path);
                    paths.Add(fullPath);
                }
            }

            if (paths.Count > 0)
            {
                string result = string.Join("\n", paths);
                EditorGUIUtility.systemCopyBuffer = result;
                Debug.Log($"已复制完整路径:\n{result}");
            }
        }

        [MenuItem("Assets/Copy GUID", false, 20)]
        private static void CopyGUID()
        {
            var selectedObjects = Selection.objects;
            if (selectedObjects == null || selectedObjects.Length == 0)
                return;

            List<string> guids = new List<string>();

            foreach (var obj in selectedObjects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path))
                {
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    if (!string.IsNullOrEmpty(guid))
                    {
                        guids.Add($"{Path.GetFileName(path)}: {guid}");
                    }
                }
            }

            if (guids.Count > 0)
            {
                string result = string.Join("\n", guids);
                EditorGUIUtility.systemCopyBuffer = result;
                Debug.Log($"已复制GUID:\n{result}");
            }
        }
    }
}