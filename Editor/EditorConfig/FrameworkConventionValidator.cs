using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 编码规范静态检查：把"新业务不要绕过服务层"落成可执行的规则。
    /// 只报告不修改；CI 可调用 Validate() 拿违例数做门禁。
    /// </summary>
    public static class FrameworkConventionValidator
    {
        private sealed class Rule
        {
            public string Id;
            public Regex Pattern;
            public string Message;
            public string[] AllowFiles;
        }
        private static readonly Rule[] Rules =
        {
            new Rule { Id = "NO_DIRECT_INPUT", Pattern = new Regex(@"\bInput\s*\.\s*(Get|mouse|touch|anyKey)", RegexOptions.Compiled),
                Message = "请使用 IInputService，不要在业务里直接读 UnityEngine.Input。", AllowFiles = new[] { "InputAndTime.cs" } },
            new Rule { Id = "NO_RESOURCES_LOAD", Pattern = new Regex(@"\bResources\s*\.\s*Load", RegexOptions.Compiled),
                Message = "请使用 IAssetService / ResourcesLoader，不要直接 Resources.Load。", AllowFiles = new[] { "ResourcesLoader.cs" } },
            new Rule { Id = "NO_FIND_OBJECT", Pattern = new Regex(@"\bFindObjectOfType\s*\(|\bFindObjectsOfType\s*\(", RegexOptions.Compiled),
                Message = "FindObjectOfType 已过时且有隐式查找成本，请显式注入或用 FindFirstObjectByType。", AllowFiles = new string[0] },
        };

        [MenuItem("UPandaGF/框架/校验编码规范")]
        public static void ValidateFromMenu()
        {
            int violations = Validate();
            if (violations == 0) Debug.Log("[FrameworkConvention] 未发现规范违例。");
            else Debug.LogWarning($"[FrameworkConvention] 共 {violations} 处规范违例，详见上方日志。");
        }

        /// <summary>扫描 Runtime 程序集目录，返回违例数量。</summary>
        public static int Validate(bool log = true)
        {
            string runtimeRoot = FindRuntimeRoot();
            if (runtimeRoot == null)
            {
                Debug.LogWarning("[FrameworkConvention] 未找到 UPandaGF.Runtime.asmdef，跳过检查。");
                return 0;
            }
            int violations = 0;
            foreach (string file in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                string text = File.ReadAllText(file);
                string[] lines = text.Split('\n');
                string fileName = Path.GetFileName(file);
                // UnityEditor 只允许出现在 #if UNITY_EDITOR 保护内；粗检：文件里完全没有保护块即视为违规
                if (text.Contains("UnityEditor") && !text.Contains("#if UNITY_EDITOR"))
                {
                    violations++;
                    if (log) Debug.LogError($"[NO_EDITOR_LEAK] {file}: Runtime 代码引用 UnityEditor 必须用 #if UNITY_EDITOR 包住。");
                }
                for (int i = 0; i < lines.Length; i++)
                {
                    foreach (var rule in Rules)
                    {
                        if (Array.IndexOf(rule.AllowFiles, fileName) >= 0) continue;
                        if (!rule.Pattern.IsMatch(lines[i])) continue;
                        violations++;
                        if (log) Debug.LogWarning($"[{rule.Id}] {file}:{i + 1} {rule.Message}");
                    }
                }
            }
            return violations;
        }

        private static string FindRuntimeRoot()
        {
            foreach (string guid in AssetDatabase.FindAssets("UPandaGF.Runtime t:AssemblyDefinitionAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("UPandaGF.Runtime.asmdef", StringComparison.OrdinalIgnoreCase))
                    return Path.GetDirectoryName(path);
            }
            return null;
        }
    }
}
