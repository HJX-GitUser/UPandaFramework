using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UPandaGF.GFEditor
{
    public enum UIPanelIssueSeverity
    {
        Error,
        Warning
    }

    /// <summary>单条面板配置问题；CI/菜单共用。</summary>
    public sealed class UIPanelIssue
    {
        public UIPanelIssueSeverity Severity;
        public Type PanelType;
        public string LoadPath;
        public string Message;

        public string PanelName => PanelType != null ? PanelType.FullName : "(unknown)";

        public override string ToString()
        {
            return "[" + Severity + "] " + PanelName + " (" + (LoadPath ?? "-") + "): " + Message;
        }
    }

    /// <summary>面板校验结果：只有 Warning 时仍可通过。</summary>
    public sealed class UIPanelValidationReport
    {
        public readonly List<UIPanelIssue> Issues = new List<UIPanelIssue>();

        public int ErrorCount => Issues.Count(issue => issue.Severity == UIPanelIssueSeverity.Error);
        public int WarningCount => Issues.Count(issue => issue.Severity == UIPanelIssueSeverity.Warning);
        public bool Passed => ErrorCount == 0;

        public void Error(Type panelType, string path, string message)
        {
            Issues.Add(new UIPanelIssue
            {
                Severity = UIPanelIssueSeverity.Error,
                PanelType = panelType,
                LoadPath = path,
                Message = message
            });
        }

        public void Warning(Type panelType, string path, string message)
        {
            Issues.Add(new UIPanelIssue
            {
                Severity = UIPanelIssueSeverity.Warning,
                PanelType = panelType,
                LoadPath = path,
                Message = message
            });
        }
    }

    /// <summary>
    /// 面板配置校验器（UI-050）：
    /// <para>扫描运行时程序集里所有具体 <see cref="BasePanel"/> 子类，校验 UILoadInfo、路径唯一性、
    /// 层级合法性与 Resources 预制体（根节点必须是 RectTransform），并提示重名交互控件。</para>
    /// <para>菜单入口：UPandaGF/UI/校验面板配置；CI 入口：<see cref="ValidateForCI"/>（0 通过，1 失败）。
    /// 默认跳过测试/示例/Editor 程序集；宿主 Assembly-CSharp 中的业务面板会被纳入。</para>
    /// </summary>
    public static class UIPanelValidator
    {
        private static readonly AssetLoadMethod[] KnownLoadMethods =
        {
            AssetLoadMethod.Resources,
            AssetLoadMethod.AssetBundle
        };

        [MenuItem("UPandaGF/UI/校验面板配置")]
        public static void ValidateFromMenu()
        {
            UIPanelValidationReport report = ValidateAll(true);
            string summary = $"面板校验：{report.ErrorCount} 个错误，{report.WarningCount} 个警告。";
            if (report.Passed) Debug.Log("[UIPanelValidator] " + summary);
            else Debug.LogError("[UIPanelValidator] " + summary);
            EditorUtility.DisplayDialog("UI 面板校验", summary + "\n\n详见 Console。", "确定");
        }

        /// <summary>CI 入口：输出结果并以 0/1 退出 Unity 批处理。</summary>
        public static void ValidateForCI()
        {
            UIPanelValidationReport report = ValidateAll(true);
            Debug.Log($"[UIPanelValidator] 完成：{report.Issues.Count} 项，{report.ErrorCount} 个错误，{report.WarningCount} 个警告。");
            EditorApplication.Exit(report.Passed ? 0 : 1);
        }

        /// <summary>扫描所有运行时面板；log 为 true 时逐条写入 Console。</summary>
        public static UIPanelValidationReport ValidateAll(bool log = true)
        {
            return ValidateTypes(CollectPanelTypes(), path => Resources.Load<GameObject>(path), log);
        }

        /// <summary>收集可扫描程序集里的具体 BasePanel 子类。</summary>
        public static List<Type> CollectPanelTypes()
        {
            List<Type> result = new List<Type>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (!ShouldScanAssembly(assembly)) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types != null ? e.Types.Where(t => t != null).ToArray() : Type.EmptyTypes;
                }
                catch (Exception)
                {
                    continue;
                }

                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];
                    if (type == null || type.IsAbstract || !type.IsSubclassOf(typeof(BasePanel))) continue;
                    result.Add(type);
                }
            }
            result.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return result;
        }

        /// <summary>
        /// 校验给定类型集合；prefabResolver 为 null 时使用 Resources.Load。
        /// 测试可注入自定义解析器，避免依赖真实 Resources 资源。
        /// </summary>
        public static UIPanelValidationReport ValidateTypes(IEnumerable<Type> panelTypes,
            Func<string, GameObject> prefabResolver = null, bool log = true)
        {
            UIPanelValidationReport report = new UIPanelValidationReport();
            if (panelTypes == null) return report;
            prefabResolver = prefabResolver ?? (path => Resources.Load<GameObject>(path));

            List<Type> types = panelTypes.Where(type => type != null).Distinct().ToList();
            Dictionary<string, List<Type>> paths = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            bool warnedAssetBundle = false;

            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                if (!typeof(BasePanel).IsAssignableFrom(type))
                {
                    report.Error(type, null, "类型不是 BasePanel 子类。");
                    continue;
                }
                if (type.IsAbstract)
                {
                    report.Warning(type, null, "抽象面板不会被 UIManager 打开，已跳过。");
                    continue;
                }

                UILoadInfoAttribute info = type.GetCustomAttribute<UILoadInfoAttribute>(false);
                if (info == null)
                {
                    report.Error(type, null, "缺少 [UILoadInfo] 特性，UIManager 无法打开该面板。");
                    continue;
                }

                string path = info.loadPath;
                if (string.IsNullOrWhiteSpace(path))
                {
                    report.Error(type, path, "loadPath 不能为空。");
                }
                else
                {
                    if (!paths.TryGetValue(path, out List<Type> bucket))
                    {
                        bucket = new List<Type>();
                        paths.Add(path, bucket);
                    }
                    bucket.Add(type);

                    if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        report.Error(type, path, "loadPath 不能带 .prefab 扩展名。");
                    if (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Assets\\", StringComparison.Ordinal))
                        report.Error(type, path, "loadPath 必须是资源相对路径（如 UI/SettingsPanel），不是 Assets/ 工程路径。");
                    if (path.StartsWith("/") || path.StartsWith("\\"))
                        report.Error(type, path, "loadPath 不能以斜杠开头。");
                }

                if (!Enum.IsDefined(typeof(E_UI_Layer), info.ui_Layer))
                    report.Error(type, path, "ui_Layer 不是合法层级。");
                if (!Enum.IsDefined(typeof(UIPanelPlacement), info.placement))
                    report.Error(type, path, "placement 不是合法布局策略。");
                if (Array.IndexOf(KnownLoadMethods, info.loadMethod) < 0)
                    report.Error(type, path, "loadMethod 不是受支持的加载方式。");

                if (string.IsNullOrWhiteSpace(path)) continue;
                if (info.loadMethod == AssetLoadMethod.Resources)
                {
                    GameObject prefab = prefabResolver(path);
                    if (prefab == null)
                    {
                        report.Error(type, path, "Resources 中找不到预制体（路径需相对 Resources 目录）。");
                        continue;
                    }
                    if (prefab.transform as RectTransform == null)
                        report.Error(type, path, "预制体根节点必须是 RectTransform（UI 面板）。");
                    else
                        CheckDuplicateInteractiveNames(report, type, path, prefab);
                }
                else if (info.loadMethod == AssetLoadMethod.AssetBundle && !warnedAssetBundle)
                {
                    warnedAssetBundle = true;
                    report.Warning(type, path, "AssetBundle 预制体存在性由构建清单校验，本次只检查特性/路径/层级。");
                }
            }

            foreach (KeyValuePair<string, List<Type>> pair in paths)
            {
                if (pair.Value.Count <= 1) continue;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    report.Error(pair.Value[i], pair.Key,
                        "loadPath 重复：" + pair.Value.Count + " 个面板共用该路径（" +
                        string.Join(", ", pair.Value.Select(t => t.Name)) + "）。");
                }
            }

            if (log)
            {
                for (int i = 0; i < report.Issues.Count; i++)
                {
                    UIPanelIssue issue = report.Issues[i];
                    if (issue.Severity == UIPanelIssueSeverity.Error) Debug.LogError("[UIPanelValidator] " + issue);
                    else Debug.LogWarning("[UIPanelValidator] " + issue);
                }
            }
            return report;
        }

        private static void CheckDuplicateInteractiveNames(UIPanelValidationReport report, Type panelType,
            string path, GameObject prefab)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            UIBehaviour[] behaviours = prefab.GetComponentsInChildren<UIBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                UIBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (!(behaviour is Button) && !(behaviour is Toggle) && !(behaviour is Slider) &&
                    !(behaviour is Dropdown) && !(behaviour is InputField) && !(behaviour is Scrollbar))
                    continue;

                string name = behaviour.gameObject.name;
                counts.TryGetValue(name, out int count);
                counts[name] = count + 1;
            }

            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value > 1)
                {
                    report.Warning(panelType, path,
                        "预制体存在重名交互控件 “" + pair.Key + "”（" + pair.Value +
                        " 个）；GetControl/TryGetControl 只会返回第一个，建议改名。");
                }
            }
        }

        private static bool ShouldScanAssembly(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic) return false;
            string name = assembly.GetName().Name;
            if (string.IsNullOrEmpty(name)) return false;
            if (name.StartsWith("UPandaGF.Tests", StringComparison.Ordinal)) return false;
            if (name.StartsWith("UPandaGF.Samples", StringComparison.Ordinal)) return false;
            if (name == "UPandaGF.Editor" || name == "Assembly-CSharp-Editor") return false;
            if (name.StartsWith("Unity", StringComparison.Ordinal)) return false;
            if (name == "mscorlib" || name == "netstandard" || name == "System" ||
                name.StartsWith("System.", StringComparison.Ordinal)) return false;
            return true;
        }
    }
}
