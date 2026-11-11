using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UPandaGF.GFEditor;

namespace UPandaGF
{
    /// <summary>生成脚本时识别到的一个 UI 控件。</summary>
    public sealed class UIAutoControl
    {
        public string TypeName;
        public string FieldName;
        public string GameObjectName;
        public UIAutoControlEvent Event;
    }

    public enum UIAutoControlEvent
    {
        None,
        Click,
        BoolChanged,
        FloatChanged,
        IntChanged,
        StringEndEdit
    }

    /// <summary>UI 面板脚本生成设置；与 EditorWindow 解耦，便于测试模板。</summary>
    public sealed class UIAutoGeneratorSettings
    {
        public string ScriptName = "UIWindow";
        public string NamespaceName = "Game.UI";
        public E_UI_Layer Layer = E_UI_Layer.Popup;
        public AssetLoadMethod LoadMethod = AssetLoadMethod.Resources;
        public string LoadPath = "UI/UIWindow";
        public UIPanelPlacement Placement = UIPanelPlacement.FullScreen;
        public bool Modal;
        public bool PauseGame;
        public bool CloseOnBack = true;
        public bool Pool;
        public int Order;
        public bool GenerateBindCode = true;
        public bool GenerateEventStubs = true;
    }

    /// <summary>
    /// 面板脚本生成器（UI-051 重写）。
    /// <para>生成 partial <see cref="BasePanel"/> 子类：UILoadInfo、控件字段、OnAwake 控件缓存、
    /// OnOpen/OnClose 模板与可选事件桩；使用 <c>TryGetControl</c> 按名称取控件，不依赖层级路径。</para>
    /// <para>不再使用 EditorApplication.update / 自动反射初始化，生成后不会残留编辑器回调；
    /// 组件由业务挂到预制体根节点，或由 UIManager 在打开时按 Type 添加。</para>
    /// </summary>
    public class UIAutoGenerator : EditorWindow
    {
        private const string MenuWindow = "UPandaGF/UI/面板脚本生成器";
        private const string MenuSelection = "GameObject/UPandaGF/UI/生成面板脚本";

        private static readonly HashSet<string> SupportedTypes = new HashSet<string>
        {
            "Image",
            "RawImage",
            "Text",
            "TextMeshProUGUI",
            "Button",
            "Toggle",
            "Slider",
            "Scrollbar",
            "ScrollRect",
            "InputField",
            "TMP_InputField",
            "Dropdown",
            "TMP_Dropdown",
            "ToggleGroup"
        };

        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while", "value", "var"
        };

        private UIAutoGeneratorSettings settings = new UIAutoGeneratorSettings();
        private Vector2 scrollPos;
        private string detectedSummary = "尚未检测";

        [MenuItem(MenuWindow)]
        public static void ShowWindow()
        {
            GetWindow<UIAutoGenerator>("UI 面板脚本生成器");
        }

        [MenuItem(MenuSelection, false, 49)]
        private static void GenerateForSelection()
        {
            if (Selection.activeGameObject == null)
            {
                EditorUtility.DisplayDialog("生成面板脚本", "请先选择一个 UI 根节点。", "确定");
                return;
            }

            UIAutoGenerator generator = CreateInstance<UIAutoGenerator>();
            try { generator.GenerateScript(Selection.activeGameObject); }
            finally { DestroyImmediate(generator); }
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("UI 面板脚本生成器", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("选择 UI 根节点后生成 partial BasePanel 脚本；" +
                                    "控件用 TryGetControl 缓存，事件登记到 OpenEvents，关闭时自动释放。",
                MessageType.Info);

            EditorGUILayout.Space(8);
            settings.ScriptName = EditorGUILayout.TextField("类名", settings.ScriptName);
            settings.NamespaceName = EditorGUILayout.TextField("命名空间", settings.NamespaceName);
            settings.Layer = (E_UI_Layer)EditorGUILayout.EnumPopup("UI 层级", settings.Layer);
            settings.LoadMethod = (AssetLoadMethod)EditorGUILayout.EnumPopup("加载方式", settings.LoadMethod);
            settings.LoadPath = EditorGUILayout.TextField("资源路径", settings.LoadPath);
            settings.Placement = (UIPanelPlacement)EditorGUILayout.EnumPopup("布局策略", settings.Placement);
            settings.Modal = EditorGUILayout.Toggle("模态", settings.Modal);
            settings.PauseGame = EditorGUILayout.Toggle("打开时暂停", settings.PauseGame);
            settings.CloseOnBack = EditorGUILayout.Toggle("可返回关闭", settings.CloseOnBack);
            settings.Pool = EditorGUILayout.Toggle("关闭后入池", settings.Pool);
            settings.Order = EditorGUILayout.IntField("同层排序", settings.Order);
            settings.GenerateBindCode = EditorGUILayout.Toggle("生成控件缓存", settings.GenerateBindCode);
            settings.GenerateEventStubs = EditorGUILayout.Toggle("生成事件桩", settings.GenerateEventStubs);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("扫描选中节点", GUILayout.Height(24)) && Selection.activeGameObject != null)
            {
                List<UIAutoControl> controls = CollectControls(Selection.activeGameObject);
                detectedSummary = controls.Count + " 个控件：" +
                                  string.Join(", ", controls.Select(c => c.GameObjectName + "(" + c.TypeName + ")").ToArray());
                if (string.IsNullOrEmpty(settings.LoadPath) || settings.LoadPath == "UI/UIWindow")
                    settings.LoadPath = DeriveLoadPath(Selection.activeGameObject);
            }
            EditorGUILayout.LabelField("检测结果", detectedSummary);

            EditorGUILayout.Space(8);
            if (GUILayout.Button("生成面板脚本", GUILayout.Height(36)))
            {
                if (Selection.activeGameObject == null)
                    EditorUtility.DisplayDialog("生成面板脚本", "请先选择一个 UI 根节点。", "确定");
                else
                    GenerateScript(Selection.activeGameObject);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>生成并保存脚本；保存成功返回 Assets 相对路径，取消/失败返回 null。</summary>
        public string GenerateScript(GameObject uiRoot)
        {
            if (uiRoot == null)
            {
                Debug.LogError("[UIAutoGenerator] UI 根节点为空。");
                return null;
            }
            if (!IsValidIdentifier(settings.ScriptName))
            {
                EditorUtility.DisplayDialog("生成面板脚本", "类名必须是合法 C# 标识符。", "确定");
                return null;
            }
            if (!string.IsNullOrEmpty(settings.NamespaceName) && !IsValidNamespace(settings.NamespaceName))
            {
                EditorUtility.DisplayDialog("生成面板脚本", "命名空间格式不合法。", "确定");
                return null;
            }

            if (string.IsNullOrEmpty(settings.LoadPath)) settings.LoadPath = DeriveLoadPath(uiRoot);
            List<UIAutoControl> controls = CollectControls(uiRoot);
            string content = BuildScriptContent(settings, controls);
            string path = SaveScriptFile(settings.ScriptName, content);
            if (string.IsNullOrEmpty(path)) return null;

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            EditorUtility.DisplayDialog("生成面板脚本",
                "脚本已生成：\n" + path +
                "\n\n下一步：\n1. 把组件挂到预制体根节点（或让 UIManager 按 Type 添加）。" +
                "\n2. 确认 [UILoadInfo] 的资源路径与预制体位置一致。" +
                "\n3. 运行 UPandaGF/UI/校验面板配置。", "确定");
            return path;
        }

        /// <summary>
        /// 纯模板方法（供测试/工具复用）：根据设置与控件列表生成脚本内容。
        /// 不读写文件、不依赖当前选择。
        /// </summary>
        public static string BuildScriptContent(UIAutoGeneratorSettings settings, IReadOnlyList<UIAutoControl> controls)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            controls = controls ?? Array.Empty<UIAutoControl>();
            string className = settings.ScriptName;
            bool hasNamespace = !string.IsNullOrEmpty(settings.NamespaceName);
            string indent = hasNamespace ? "    " : "";
            string inner = hasNamespace ? indent + "    " : "    ";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("// 由 UPandaGF/UI/面板脚本生成器生成于 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "。");
            sb.AppendLine("// 建议只修改标记为 TODO 的部分；重新生成会覆盖本文件。");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using TMPro;");
            sb.AppendLine("using UPandaGF;");
            sb.AppendLine();
            if (hasNamespace)
            {
                sb.AppendLine("namespace " + settings.NamespaceName);
                sb.AppendLine("{");
            }

            sb.AppendLine(inner + "/// <summary>");
            sb.AppendLine(inner + "/// " + className + " 面板。");
            sb.AppendLine(inner + "/// </summary>");
            sb.AppendLine(inner + "[" + BuildLoadInfo(settings) + "]");
            sb.AppendLine(inner + "public partial class " + className + " : BasePanel");
            sb.AppendLine(inner + "{");
            string body = inner + "    ";

            if (controls.Count > 0)
            {
                sb.AppendLine(body + "#region 控件引用");
                for (int i = 0; i < controls.Count; i++)
                {
                    UIAutoControl control = controls[i];
                    sb.AppendLine(body + "[SerializeField] private " + control.TypeName + " " + control.FieldName + ";");
                }
                sb.AppendLine(body + "#endregion");
                sb.AppendLine();
            }

            if (settings.GenerateBindCode)
            {
                sb.AppendLine(body + "protected override void OnAwake()");
                sb.AppendLine(body + "{");
                sb.AppendLine(body + "    AutoBindComponents();");
                sb.AppendLine(body + "}");
                sb.AppendLine();
            }

            sb.AppendLine(body + "public override void OnOpen(object panelArg = null)");
            sb.AppendLine(body + "{");
            sb.AppendLine(body + "    // TODO: 根据 panelArg 刷新显示；OpenEvents 在 OnClose 时自动释放。");
            if (settings.GenerateEventStubs)
            {
                for (int i = 0; i < controls.Count; i++)
                {
                    string listener = BuildListener(controls[i]);
                    if (!string.IsNullOrEmpty(listener)) sb.AppendLine(body + "    " + listener);
                }
            }
            sb.AppendLine(body + "}");
            sb.AppendLine();

            sb.AppendLine(body + "public override void OnClose()");
            sb.AppendLine(body + "{");
            sb.AppendLine(body + "    // TODO: 停止本面板的计时器/试听等；事件与绑定由框架释放。");
            sb.AppendLine(body + "}");
            sb.AppendLine();

            if (settings.GenerateBindCode)
            {
                sb.AppendLine(body + "/// <summary>从 BasePanel 的控件缓存取引用；不依赖层级路径。</summary>");
                sb.AppendLine(body + "private void AutoBindComponents()");
                sb.AppendLine(body + "{");
                if (controls.Count == 0)
                {
                    sb.AppendLine(body + "    // 未发现可缓存的 UI 控件。");
                }
                else
                {
                    for (int i = 0; i < controls.Count; i++)
                    {
                        UIAutoControl control = controls[i];
                        sb.AppendLine(body + "    TryGetControl(\"" + control.GameObjectName + "\", out " + control.FieldName + ");");
                        sb.AppendLine(body + "    if (" + control.FieldName + " == null)");
                        sb.AppendLine(body + "        Debug.LogError(\"[" + className + "] 找不到控件：" +
                                      Escape(control.GameObjectName) + " (" + control.TypeName + ")\");");
                    }
                }
                sb.AppendLine(body + "}");
                sb.AppendLine();
            }

            if (settings.GenerateEventStubs)
            {
                for (int i = 0; i < controls.Count; i++)
                {
                    UIAutoControl control = controls[i];
                    if (control.Event == UIAutoControlEvent.None) continue;
                    string signature = BuildHandlerSignature(control);
                    if (string.IsNullOrEmpty(signature)) continue;
                    sb.AppendLine(body + "private void " + signature);
                    sb.AppendLine(body + "{");
                    sb.AppendLine(body + "    // TODO: 处理 " + Escape(control.GameObjectName) + " 的交互。");
                    sb.AppendLine(body + "}");
                    sb.AppendLine();
                }
            }

            sb.AppendLine(inner + "}");
            if (hasNamespace) sb.AppendLine("}");
            return sb.ToString();
        }

        /// <summary>从选中节点收集可绑定控件；跳过嵌套的其它 BasePanel 子树。</summary>
        public static List<UIAutoControl> CollectControls(GameObject root)
        {
            List<UIAutoControl> result = new List<UIAutoControl>();
            if (root == null) return result;
            HashSet<string> usedFields = new HashSet<string>();
            HashSet<string> seenControls = new HashSet<string>();

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform child = all[i];
                if (child == null || child == root.transform) continue;
                BasePanel owner = child.GetComponentInParent<BasePanel>();
                if (owner != null && owner.gameObject != root) continue;

                Component[] components = child.GetComponents<Component>();
                for (int j = 0; j < components.Length; j++)
                {
                    Component component = components[j];
                    if (component == null) continue;
                    string typeName = component.GetType().Name;
                    if (!SupportedTypes.Contains(typeName)) continue;
                    // TryGetControl 只按名称+类型取第一个；同名同类型的重复控件无法区分，跳过以免生成永远绑定到同一个控件的字段。
                    if (!seenControls.Add(child.name + "|" + typeName)) continue;

                    UIAutoControl control = new UIAutoControl
                    {
                        TypeName = typeName,
                        GameObjectName = child.name,
                        FieldName = BuildFieldName(child.name, typeName, usedFields),
                        Event = ResolveEvent(typeName)
                    };
                    result.Add(control);
                }
            }
            return result;
        }

        private static string BuildLoadInfo(UIAutoGeneratorSettings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("UILoadInfo(E_UI_Layer.").Append(settings.Layer)
              .Append(", AssetLoadMethod.").Append(settings.LoadMethod)
              .Append(", \"").Append(Escape(settings.LoadPath)).Append("\"");
            if (settings.Placement != UIPanelPlacement.FullScreen)
                sb.Append(", placement = UIPanelPlacement.").Append(settings.Placement);
            if (settings.Modal) sb.Append(", modal = true");
            if (settings.PauseGame) sb.Append(", pauseGame = true");
            if (!settings.CloseOnBack) sb.Append(", closeOnBack = false");
            if (settings.Pool) sb.Append(", pool = true");
            if (settings.Order != 0) sb.Append(", order = ").Append(settings.Order);
            sb.Append(")");
            return sb.ToString();
        }

        private static string BuildListener(UIAutoControl control)
        {
            string handler = BuildHandlerName(control);
            if (string.IsNullOrEmpty(handler)) return null;
            return "OpenEvents.Listen(" + control.FieldName + "." + GetUnityEventName(control.Event) +
                   ", " + handler + ");";
        }

        private static string BuildHandlerName(UIAutoControl control)
        {
            if (control.Event == UIAutoControlEvent.None) return null;
            string property = char.ToUpperInvariant(control.FieldName[0]) + control.FieldName.Substring(1);
            switch (control.Event)
            {
                case UIAutoControlEvent.Click: return "On" + property + "Click";
                case UIAutoControlEvent.StringEndEdit: return "On" + property + "EndEdit";
                default: return "On" + property + "ValueChanged";
            }
        }

        private static string BuildHandlerSignature(UIAutoControl control)
        {
            string name = BuildHandlerName(control);
            if (string.IsNullOrEmpty(name)) return null;
            switch (control.Event)
            {
                case UIAutoControlEvent.Click: return name + "()";
                case UIAutoControlEvent.BoolChanged: return name + "(bool value)";
                case UIAutoControlEvent.FloatChanged: return name + "(float value)";
                case UIAutoControlEvent.IntChanged: return name + "(int value)";
                case UIAutoControlEvent.StringEndEdit: return name + "(string value)";
                default: return null;
            }
        }

        private static string GetUnityEventName(UIAutoControlEvent controlEvent)
        {
            switch (controlEvent)
            {
                case UIAutoControlEvent.Click: return "onClick";
                case UIAutoControlEvent.StringEndEdit: return "onEndEdit";
                default: return "onValueChanged";
            }
        }

        private static UIAutoControlEvent ResolveEvent(string typeName)
        {
            switch (typeName)
            {
                case "Button": return UIAutoControlEvent.Click;
                case "Toggle": return UIAutoControlEvent.BoolChanged;
                case "Slider":
                case "Scrollbar": return UIAutoControlEvent.FloatChanged;
                case "Dropdown":
                case "TMP_Dropdown": return UIAutoControlEvent.IntChanged;
                case "InputField":
                case "TMP_InputField": return UIAutoControlEvent.StringEndEdit;
                default: return UIAutoControlEvent.None;
            }
        }

        private static string BuildFieldName(string gameObjectName, string typeName, HashSet<string> used)
        {
            string clean = Regex.Replace(gameObjectName ?? string.Empty, "[^A-Za-z0-9_]", string.Empty);
            if (string.IsNullOrEmpty(clean)) clean = "ui";
            if (char.IsDigit(clean[0])) clean = "_" + clean;
            clean = char.ToLowerInvariant(clean[0]) + clean.Substring(1);
            if (Keywords.Contains(clean)) clean += "Field";

            if (used.Add(clean)) return clean;
            string suffix = typeName.StartsWith("TMP_", StringComparison.Ordinal)
                ? "TMP" : typeName;
            string candidate = clean + suffix;
            int index = 2;
            while (!used.Add(candidate)) candidate = clean + suffix + index++;
            return candidate;
        }

        private static string DeriveLoadPath(GameObject root)
        {
            string assetPath = AssetDatabase.GetAssetPath(root);
            if (!string.IsNullOrEmpty(assetPath))
            {
                int index = assetPath.IndexOf("/Resources/", StringComparison.Ordinal);
                if (index >= 0)
                {
                    string relative = assetPath.Substring(index + "/Resources/".Length);
                    return Path.ChangeExtension(relative, null).Replace('\\', '/');
                }
            }
            return "UI/" + (root != null ? root.name : "UIWindow");
        }

        private static string SaveScriptFile(string fileName, string content)
        {
            string path = EditorUtility.SaveFilePanel("保存面板脚本", "Assets/Scripts/UI", fileName, "cs");
            if (string.IsNullOrEmpty(path)) return null;
            if (!EditorDestructiveGuard.IsInsideAssets(path))
            {
                Debug.LogError("[UIAutoGenerator] 请将脚本保存在 Assets 目录内。");
                return null;
            }
            if (!EditorDestructiveGuard.ConfirmOverwrite("覆盖 UI 脚本", path)) return null;

            EditorAssetPathUtility.EnsureAbsoluteDirectoryForFile(path);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return "Assets" + path.Substring(Application.dataPath.Length);
        }

        private static bool IsValidIdentifier(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$") &&
                   !Keywords.Contains(value);
        }

        private static bool IsValidNamespace(string value)
        {
            string[] parts = value.Split('.');
            for (int i = 0; i < parts.Length; i++)
                if (!IsValidIdentifier(parts[i])) return false;
            return true;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    /// <summary>标记字段由生成器自动绑定；保留给旧脚本/自定义 Inspector 使用。</summary>
    public class AutoBindAttribute : PropertyAttribute
    {
        public string Path { get; private set; }
        public Type ComponentType { get; private set; }

        public AutoBindAttribute(string path, Type componentType = null)
        {
            Path = path;
            ComponentType = componentType;
        }
    }
}
