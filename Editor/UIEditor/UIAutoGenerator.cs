using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;
using System.Reflection;

namespace UPandaGF
{
    /// <summary>
    /// UGUI界面脚本自动生成器
    /// </summary>
    public class UIAutoGenerator : EditorWindow
    {
        [MenuItem("UPandaGF/Tools/UI/自动生成UI脚本(测试开发中)")]
        public static void ShowWindow()
        {
            GetWindow<UIAutoGenerator>("UI脚本生成器");
        }

        [MenuItem("GameObject/UPandaGF/UI/生成UI控制脚本", false, 49)]
        private static void GenerateUIScriptForSelection()
        {
            if (Selection.activeGameObject != null)
            {
                var generator = CreateInstance<UIAutoGenerator>();
                generator.GenerateScript(Selection.activeGameObject);
                DestroyImmediate(generator);
            }
        }

        private string scriptName = "UIWindow";
        private string namespaceName = "Game.UI";
        private bool generateAutoBindCode = true;
        private bool inheritMonoBehaviour = true;
        private string baseClassName = "MonoBehaviour";
        private Vector2 scrollPos;

        private static readonly List<string> supportedTypes = new List<string>
        {
            "Canvas",
            "CanvasGroup",
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
            "InputFieldTMP",
            "Dropdown",
            "ToggleGroup",
            "ScrollView"
        };

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("UI脚本生成器", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            EditorGUILayout.HelpBox("选择UI根节点后，点击生成按钮自动生成UI控制脚本", MessageType.Info);

            EditorGUILayout.Space(10);
            scriptName = EditorGUILayout.TextField("脚本名称", scriptName);
            namespaceName = EditorGUILayout.TextField("命名空间", namespaceName);
            baseClassName = EditorGUILayout.TextField("基类名称", baseClassName);

            generateAutoBindCode = EditorGUILayout.Toggle("生成自动绑定代码", generateAutoBindCode);
            inheritMonoBehaviour = EditorGUILayout.Toggle("继承MonoBehaviour", inheritMonoBehaviour);

            EditorGUILayout.Space(20);

            if (GUILayout.Button("生成UI脚本", GUILayout.Height(40)))
            {
                if (Selection.activeGameObject != null)
                {
                    GenerateScript(Selection.activeGameObject);
                }
                else
                {
                    EditorUtility.DisplayDialog("提示", "请先选择一个UI GameObject", "确定");
                }
            }

            EditorGUILayout.EndScrollView();
        }

        public void GenerateScript(GameObject uiRoot)
        {
            if (uiRoot == null)
            {
                Debug.LogError("UI根节点为空");
                return;
            }

            // 收集所有UI组件
            Dictionary<string, List<ComponentInfo>> components = CollectComponents(uiRoot);

            // 生成脚本内容
            string scriptContent = GenerateScriptContent(uiRoot.name, components);

            // 保存脚本文件
            string path = SaveScriptFile(scriptName, scriptContent);

            if (!string.IsNullOrEmpty(path))
            {
                // 自动添加组件到GameObject
                AddComponentToGameObject(uiRoot, scriptName, path);

                // 编译后自动绑定
                EditorApplication.update += () => AutoBindAfterCompile(uiRoot, path);

                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("成功", $"UI脚本已生成: {path}", "确定");
            }
        }

        private Dictionary<string, List<ComponentInfo>> CollectComponents(GameObject root)
        {
            Dictionary<string, List<ComponentInfo>> components = new Dictionary<string, List<ComponentInfo>>();

            // 遍历所有子物体
            Transform[] allChildren = root.GetComponentsInChildren<Transform>(true);

            foreach (Transform child in allChildren)
            {
                if (child == root.transform) continue;

                // 检查支持的组件类型
                Component[] allComponents = child.GetComponents<Component>();

                foreach (Component component in allComponents)
                {
                    if (component == null) continue;

                    string typeName = component.GetType().Name;

                    if (IsSupportedType(typeName))
                    {
                        if (!components.ContainsKey(typeName))
                        {
                            components[typeName] = new List<ComponentInfo>();
                        }

                        string fieldName = GenerateFieldName(child.name, typeName);

                        components[typeName].Add(new ComponentInfo
                        {
                            fieldName = fieldName,
                            gameObjectName = child.name,
                            path = GetRelativePath(child, root.transform),
                            component = component
                        });
                    }
                }

                // 如果GameObject没有特殊组件，也记录为GameObject类型
                if (allComponents.Length <= 1) // 只有Transform组件
                {
                    if (!components.ContainsKey("GameObject"))
                    {
                        components["GameObject"] = new List<ComponentInfo>();
                    }

                    string fieldName = GenerateFieldName(child.name, "GameObject");

                    components["GameObject"].Add(new ComponentInfo
                    {
                        fieldName = fieldName,
                        gameObjectName = child.name,
                        path = GetRelativePath(child, root.transform),
                        component = child.transform
                    });
                }
            }

            return components;
        }

        private string GenerateScriptContent(string uiName, Dictionary<string, List<ComponentInfo>> components)
        {
            StringBuilder sb = new StringBuilder();

            // 添加using语句
            sb.AppendLine("using UnityEngine;");
            sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using TMPro;");
            sb.AppendLine("using System.Collections;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine();

            // 添加命名空间
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine($"namespace {namespaceName}");
                sb.AppendLine("{");
            }

            // 类定义
            string inheritance = inheritMonoBehaviour ? $" : {baseClassName}" : "";
            sb.AppendLine($"    /// <summary>");
            sb.AppendLine($"    /// {uiName} UI控制脚本");
            sb.AppendLine($"    /// 自动生成于: {System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}");
            sb.AppendLine($"    /// </summary>");
            sb.AppendLine($"    public class {scriptName}{inheritance}");
            sb.AppendLine("    {");

            // 生成字段
            sb.AppendLine("        #region UI Components");
            foreach (var kvp in components)
            {
                string typeName = kvp.Key;
                foreach (var compInfo in kvp.Value)
                {
                    sb.AppendLine($"        [SerializeField] private {typeName} {compInfo.fieldName};");
                }
            }
            sb.AppendLine("        #endregion");
            sb.AppendLine();

            // 生成属性
            sb.AppendLine("        #region UI Properties");
            foreach (var kvp in components)
            {
                string typeName = kvp.Key;
                foreach (var compInfo in kvp.Value)
                {
                    string propertyName = char.ToUpper(compInfo.fieldName[0]) + compInfo.fieldName.Substring(1);
                    sb.AppendLine($"        public {typeName} {propertyName} => {compInfo.fieldName};");
                }
            }
            sb.AppendLine("        #endregion");
            sb.AppendLine();

            // 生成初始化方法
            sb.AppendLine("        #region Initialization");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 自动绑定UI组件");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private void AutoBindComponents()");
            sb.AppendLine("        {");
            sb.AppendLine("            if (transform == null) return;");
            sb.AppendLine();

            foreach (var kvp in components)
            {
                foreach (var compInfo in kvp.Value)
                {
                    string type = kvp.Key;
                    sb.AppendLine($"            // {compInfo.gameObjectName}");
                    sb.AppendLine($"            {compInfo.fieldName} = transform.Find(\"{compInfo.path}\")");

                    if (type != "GameObject" && type != "Transform")
                    {
                        sb.AppendLine($"                .GetComponent<{type}>();");
                    }
                    else
                    {
                        sb.AppendLine($"                .gameObject;");
                    }

                    sb.AppendLine($"            if ({compInfo.fieldName} == null)");
                    sb.AppendLine($"                Debug.LogError($\"找不到组件: {compInfo.gameObjectName} ({type})\");");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 初始化UI");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        public virtual void Initialize()");
            sb.AppendLine("        {");
            if (generateAutoBindCode)
            {
                sb.AppendLine("            AutoBindComponents();");
            }
            sb.AppendLine("            SetupEventListeners();");
            sb.AppendLine("        }");
            sb.AppendLine("        #endregion");
            sb.AppendLine();

            // 生成事件监听设置
            sb.AppendLine("        #region Event Listeners");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 设置事件监听");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private void SetupEventListeners()");
            sb.AppendLine("        {");

            foreach (var kvp in components)
            {
                if (kvp.Key == "Button")
                {
                    foreach (var compInfo in kvp.Value)
                    {
                        string methodName = $"On{char.ToUpper(compInfo.fieldName[0])}{compInfo.fieldName.Substring(1)}Click";
                        sb.AppendLine($"            if ({compInfo.fieldName} != null)");
                        sb.AppendLine($"                {compInfo.fieldName}.onClick.AddListener({methodName});");
                    }
                }
                else if (kvp.Key == "Toggle")
                {
                    foreach (var compInfo in kvp.Value)
                    {
                        string methodName = $"On{char.ToUpper(compInfo.fieldName[0])}{compInfo.fieldName.Substring(1)}ValueChanged";
                        sb.AppendLine($"            if ({compInfo.fieldName} != null)");
                        sb.AppendLine($"                {compInfo.fieldName}.onValueChanged.AddListener((value) => {methodName}(value));");
                    }
                }
                else if (kvp.Key == "Slider")
                {
                    foreach (var compInfo in kvp.Value)
                    {
                        string methodName = $"On{char.ToUpper(compInfo.fieldName[0])}{compInfo.fieldName.Substring(1)}ValueChanged";
                        sb.AppendLine($"            if ({compInfo.fieldName} != null)");
                        sb.AppendLine($"                {compInfo.fieldName}.onValueChanged.AddListener({methodName});");
                    }
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine();

            // 生成事件方法模板
            sb.AppendLine("        #region Event Methods");

            foreach (var kvp in components)
            {
                if (kvp.Key == "Button")
                {
                    foreach (var compInfo in kvp.Value)
                    {
                        string methodName = $"On{char.ToUpper(compInfo.fieldName[0])}{compInfo.fieldName.Substring(1)}Click";
                        sb.AppendLine($"        /// <summary>");
                        sb.AppendLine($"        /// {compInfo.gameObjectName} 点击事件");
                        sb.AppendLine($"        /// </summary>");
                        sb.AppendLine($"        private void {methodName}()");
                        sb.AppendLine($"        {{");
                        sb.AppendLine($"            // TODO: 实现点击逻辑");
                        sb.AppendLine($"            Debug.Log($\"{compInfo.fieldName} 被点击\");");
                        sb.AppendLine($"        }}");
                        sb.AppendLine();
                    }
                }
                else if (kvp.Key == "Toggle")
                {
                    foreach (var compInfo in kvp.Value)
                    {
                        string methodName = $"On{char.ToUpper(compInfo.fieldName[0])}{compInfo.fieldName.Substring(1)}ValueChanged";
                        sb.AppendLine($"        /// <summary>");
                        sb.AppendLine($"        /// {compInfo.gameObjectName} 值改变事件");
                        sb.AppendLine($"        /// </summary>");
                        sb.AppendLine($"        private void {methodName}(bool value)");
                        sb.AppendLine($"        {{");
                        sb.AppendLine($"            // TODO: 实现值改变逻辑");
                        sb.AppendLine($"            Debug.Log($\"{compInfo.fieldName} 值改变: {{value}}\");");
                        sb.AppendLine($"        }}");
                        sb.AppendLine();
                    }
                }
            }

            sb.AppendLine("        #endregion");
            sb.AppendLine("        ");

            // 生成清理方法
            sb.AppendLine("        #region Cleanup");
            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 清理事件监听");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private void OnDestroy()");
            sb.AppendLine("        {");
            sb.AppendLine("            RemoveEventListeners();");
            sb.AppendLine("        }");
            sb.AppendLine();

            sb.AppendLine("        /// <summary>");
            sb.AppendLine("        /// 移除事件监听");
            sb.AppendLine("        /// </summary>");
            sb.AppendLine("        private void RemoveEventListeners()");
            sb.AppendLine("        {");

            foreach (var kvp in components)
            {
                if (kvp.Key == "Button")
                {
                    foreach (var compInfo in kvp.Value)
                    {
                        string methodName = $"On{char.ToUpper(compInfo.fieldName[0])}{compInfo.fieldName.Substring(1)}Click";
                        sb.AppendLine($"            if ({compInfo.fieldName} != null)");
                        sb.AppendLine($"                {compInfo.fieldName}.onClick.RemoveListener({methodName});");
                    }
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("        #endregion");

            sb.AppendLine("    }");

            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private string SaveScriptFile(string fileName, string content)
        {
            string path = EditorUtility.SaveFilePanel("保存UI脚本", "Assets/Scripts/UI", fileName, "cs");

            if (!string.IsNullOrEmpty(path))
            {
                // 确保路径在Assets目录下
                if (!path.StartsWith(Application.dataPath))
                {
                    Debug.LogError("请将脚本保存在Assets目录内");
                    return null;
                }

                // 创建目录
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, content, Encoding.UTF8);
                return path;
            }

            return null;
        }

        private void AddComponentToGameObject(GameObject target, string componentName, string scriptPath)
        {
            // 脚本编译后会自动添加组件
            // 这里只需要确保脚本存在
        }

        private void AutoBindAfterCompile(GameObject uiRoot, string scriptPath)
        {
            if (EditorApplication.isCompiling) return;

            EditorApplication.update -= () => AutoBindAfterCompile(uiRoot, scriptPath);

            // 等待一帧确保编译完成
            EditorApplication.delayCall += () =>
            {
                if (uiRoot == null) return;

                // 获取脚本类型
                string relativePath = "Assets" + scriptPath.Replace(Application.dataPath, "");
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(relativePath);

                if (script != null)
                {
                    System.Type scriptType = script.GetClass();

                    if (scriptType != null)
                    {
                        // 添加或获取组件
                        Component component = uiRoot.GetComponent(scriptType);
                        if (component == null)
                        {
                            component = uiRoot.AddComponent(scriptType);
                        }

                        // 如果启用了自动绑定，调用初始化方法
                        if (generateAutoBindCode)
                        {
                            // 通过反射调用Initialize方法
                            MethodInfo initMethod = scriptType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
                            if (initMethod != null)
                            {
                                initMethod.Invoke(component, null);
                            }
                        }

                        EditorUtility.SetDirty(uiRoot);
                        Selection.activeObject = uiRoot;
                    }
                }
            };
        }

        private bool IsSupportedType(string typeName)
        {
            return supportedTypes.Contains(typeName);
        }

        private string GenerateFieldName(string gameObjectName, string componentType)
        {
            // 清理名称，移除特殊字符
            string cleanName = Regex.Replace(gameObjectName, @"[^a-zA-Z0-9_]", "");

            // 如果名称以数字开头，添加前缀
            if (char.IsDigit(cleanName[0]))
            {
                cleanName = "_" + cleanName;
            }

            // 转换为驼峰命名
            cleanName = char.ToLower(cleanName[0]) + cleanName.Substring(1);

            // 添加类型后缀避免重复
            string suffix = componentType;
            if (componentType.EndsWith("TMP"))
                suffix = "TMP";
            else if (componentType.Length > 0)
                suffix = componentType.Substring(0, 1).ToUpper();

            return $"{cleanName}{suffix}";
        }

        private string GetRelativePath(Transform child, Transform root)
        {
            List<string> path = new List<string>();
            Transform current = child;

            while (current != root && current != null)
            {
                path.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", path);
        }

        private class ComponentInfo
        {
            public string fieldName;
            public string gameObjectName;
            public string path;
            public Component component;
        }
    }

    /// <summary>
    /// 自定义属性，用于标记自动绑定的UI元素
    /// </summary>
    public class AutoBindAttribute : PropertyAttribute
    {
        public string Path { get; private set; }
        public System.Type ComponentType { get; private set; }

        public AutoBindAttribute(string path, System.Type componentType = null)
        {
            Path = path;
            ComponentType = componentType;
        }
    }
}