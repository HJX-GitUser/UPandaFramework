using UnityEngine;
using UnityEditor;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public class JsonEditorWindow : EditorWindow
{
    private JToken _root;
    private string _filePath = "";
    private Vector2 _scrollPosition;
    private Dictionary<string, bool> _foldoutStates = new Dictionary<string, bool>();

    [MenuItem("Tools/JSON Editor")]
    static void Init()
    {
        JsonEditorWindow window = GetWindow<JsonEditorWindow>();
        window.titleContent = new GUIContent("JSON Editor");
        window.Show();
    }

    private void OnEnable()
    {
        if (_root == null)
        {
            NewJson();
        }
    }

    private void NewJson()
    {
        _root = new JObject();
        _filePath = "";
        _foldoutStates.Clear();
    }

    private void OnGUI()
    {
        DrawToolbar();

        if (_root == null)
        {
            EditorGUILayout.LabelField("No JSON loaded.");
            return;
        }

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        DrawJToken(_root, "root", true);
        EditorGUILayout.EndScrollView();
    }

    private void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("New", EditorStyles.toolbarButton))
        {
            NewJson();
        }
        if (GUILayout.Button("Open", EditorStyles.toolbarButton))
        {
            OpenJson();
        }
        if (GUILayout.Button("Save", EditorStyles.toolbarButton))
        {
            SaveJson();
        }
        if (GUILayout.Button("Save As", EditorStyles.toolbarButton))
        {
            SaveJsonAs();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void OpenJson()
    {
        string path = EditorUtility.OpenFilePanel("Open JSON file", Application.dataPath, "json");
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                string content = File.ReadAllText(path);
                _root = JToken.Parse(content);
                _filePath = path;
                _foldoutStates.Clear();
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", "Failed to parse JSON: " + e.Message, "OK");
            }
        }
    }

    private void SaveJson()
    {
        if (string.IsNullOrEmpty(_filePath))
        {
            SaveJsonAs();
        }
        else
        {
            SaveJsonToPath(_filePath);
        }
    }

    private void SaveJsonAs()
    {
        string path = EditorUtility.SaveFilePanel("Save JSON file", Application.dataPath, "data.json", "json");
        if (!string.IsNullOrEmpty(path))
        {
            SaveJsonToPath(path);
            _filePath = path;
        }
    }

    private void SaveJsonToPath(string path)
    {
        try
        {
            string content = _root.ToString(Formatting.Indented);
            File.WriteAllText(path, content);
            AssetDatabase.Refresh();
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Error", "Failed to save JSON: " + e.Message, "OK");
        }
    }

    // 统一入口：根据类型分发绘制，并处理折叠标题
    private void DrawJToken(JToken token, string path, bool showHeader = false)
    {
        if (token == null) return;

        switch (token.Type)
        {
            case JTokenType.Object:
                DrawJObject((JObject)token, path, showHeader);
                break;
            case JTokenType.Array:
                DrawJArray((JArray)token, path, showHeader);
                break;
            default:
                DrawJValue((JValue)token, path);
                break;
        }
    }

    private void DrawJObject(JObject obj, string path, bool showHeader)
    {
        if (showHeader)
        {
            // 显示可折叠的标题行
            if (!_foldoutStates.ContainsKey(path))
                _foldoutStates[path] = true;

            EditorGUILayout.BeginHorizontal();
            _foldoutStates[path] = EditorGUILayout.Foldout(_foldoutStates[path], path, true);
            // 根对象不允许删除，所以不显示删除按钮
            EditorGUILayout.EndHorizontal();

            if (!_foldoutStates[path]) return;
        }

        EditorGUI.indentLevel++;

        // 绘制所有属性
        var properties = obj.Properties().ToList();
        for (int i = 0; i < properties.Count; i++)
        {
            var prop = properties[i];
            string propPath = path + "." + prop.Name;

            EditorGUILayout.BeginHorizontal();

            // 折叠箭头（如果值是对象或数组）
            bool isContainer = prop.Value.Type == JTokenType.Object || prop.Value.Type == JTokenType.Array;
            if (isContainer)
            {
                if (!_foldoutStates.ContainsKey(propPath))
                    _foldoutStates[propPath] = true;
                _foldoutStates[propPath] = EditorGUILayout.Foldout(_foldoutStates[propPath], "", true);
            }
            else
            {
                GUILayout.Space(16); // 缩进对齐
            }

            // 可编辑的属性名
            string newName = EditorGUILayout.TextField(prop.Name);
            if (newName != prop.Name)
            {
                RenameProperty(obj, prop.Name, newName, path);
            }

            // 删除按钮
            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                obj.Remove(prop.Name);
                GUIUtility.ExitGUI(); // 避免继续绘制已删除的项
            }

            EditorGUILayout.EndHorizontal();

            // 如果折叠打开，绘制属性值
            if (isContainer && _foldoutStates[propPath])
            {
                EditorGUI.indentLevel++;
                DrawJToken(prop.Value, propPath, false); // 内嵌值不显示自己的标题
                EditorGUI.indentLevel--;
            }
            else if (!isContainer)
            {
                // 基本类型直接绘制在下一行
                EditorGUI.indentLevel++;
                DrawJToken(prop.Value, propPath, false);
                EditorGUI.indentLevel--;
            }
        }

        // 添加新属性的按钮
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 15);
        if (GUILayout.Button("Add Property", GUILayout.Width(100)))
        {
            ShowAddPropertyMenu(obj, path);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    private void DrawJArray(JArray array, string path, bool showHeader)
    {
        if (showHeader)
        {
            if (!_foldoutStates.ContainsKey(path))
                _foldoutStates[path] = true;

            EditorGUILayout.BeginHorizontal();
            _foldoutStates[path] = EditorGUILayout.Foldout(_foldoutStates[path], path, true);
            EditorGUILayout.EndHorizontal();

            if (!_foldoutStates[path]) return;
        }

        EditorGUI.indentLevel++;

        for (int i = 0; i < array.Count; i++)
        {
            int index = i;
            string elemPath = path + "[" + index + "]";
            JToken elem = array[index];

            EditorGUILayout.BeginHorizontal();

            bool isContainer = elem.Type == JTokenType.Object || elem.Type == JTokenType.Array;
            if (isContainer)
            {
                if (!_foldoutStates.ContainsKey(elemPath))
                    _foldoutStates[elemPath] = true;
                _foldoutStates[elemPath] = EditorGUILayout.Foldout(_foldoutStates[elemPath], "", true);
            }
            else
            {
                GUILayout.Space(16);
            }

            EditorGUILayout.LabelField("[" + index + "]", GUILayout.Width(30));

            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                array.RemoveAt(index);
                GUIUtility.ExitGUI();
            }

            EditorGUILayout.EndHorizontal();

            if (isContainer && _foldoutStates[elemPath])
            {
                EditorGUI.indentLevel++;
                DrawJToken(elem, elemPath, false);
                EditorGUI.indentLevel--;
            }
            else if (!isContainer)
            {
                EditorGUI.indentLevel++;
                DrawJToken(elem, elemPath, false);
                EditorGUI.indentLevel--;
            }
        }

        // 添加元素按钮
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(EditorGUI.indentLevel * 15);
        if (GUILayout.Button("Add Element", GUILayout.Width(100)))
        {
            ShowAddElementMenu(array, path);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
    }

    private void DrawJValue(JValue value, string path)
    {
        // 基本类型直接绘制可编辑控件
        switch (value.Type)
        {
            case JTokenType.String:
                string str = value.Value<string>();
                string newStr = EditorGUILayout.TextField(str);
                if (newStr != str)
                    value.Value = newStr;
                break;

            case JTokenType.Integer:
                long intVal = value.Value<long>();
                long newInt = EditorGUILayout.LongField(intVal);
                if (newInt != intVal)
                    value.Value = newInt;
                break;

            case JTokenType.Float:
                float floatVal = value.Value<float>();
                float newFloat = EditorGUILayout.FloatField(floatVal);
                if (newFloat != floatVal)
                    value.Value = newFloat;
                break;

            case JTokenType.Boolean:
                bool boolVal = value.Value<bool>();
                bool newBool = EditorGUILayout.Toggle(boolVal);
                if (newBool != boolVal)
                    value.Value = newBool;
                break;

            default:
                // 其他类型作为字符串处理
                string fallback = value.ToString();
                string newFallback = EditorGUILayout.TextField(fallback);
                if (newFallback != fallback)
                {
                    try
                    {
                        // 尝试解析为原始类型，否则保留字符串
                        value.Value = newFallback;
                    }
                    catch { }
                }
                break;
        }
    }

    // 重命名对象属性
    private void RenameProperty(JObject parent, string oldName, string newName, string parentPath)
    {
        if (oldName == newName) return;
        if (parent[newName] != null)
        {
            EditorUtility.DisplayDialog("Error", "Property with name '" + newName + "' already exists.", "OK");
            return;
        }

        JToken value = parent[oldName];
        parent.Remove(oldName);
        parent[newName] = value;

        // 更新折叠状态
        string oldPath = parentPath + "." + oldName;
        string newPath = parentPath + "." + newName;
        if (_foldoutStates.TryGetValue(oldPath, out bool state))
        {
            _foldoutStates[newPath] = state;
            _foldoutStates.Remove(oldPath);
        }
    }

    // 显示添加属性菜单
    private void ShowAddPropertyMenu(JObject obj, string parentPath)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("String"), false, () => AddProperty(obj, parentPath, JTokenType.String));
        menu.AddItem(new GUIContent("Number (Integer)"), false, () => AddProperty(obj, parentPath, JTokenType.Integer));
        menu.AddItem(new GUIContent("Number (Float)"), false, () => AddProperty(obj, parentPath, JTokenType.Float));
        menu.AddItem(new GUIContent("Boolean"), false, () => AddProperty(obj, parentPath, JTokenType.Boolean));
        menu.AddItem(new GUIContent("Object"), false, () => AddProperty(obj, parentPath, JTokenType.Object));
        menu.AddItem(new GUIContent("Array"), false, () => AddProperty(obj, parentPath, JTokenType.Array));
        menu.ShowAsContext();
    }

    private void AddProperty(JObject obj, string parentPath, JTokenType type)
    {
        string baseName = "newProperty";
        string name = baseName;
        int counter = 1;
        while (obj[name] != null)
        {
            name = baseName + counter;
            counter++;
        }

        JToken value;
        switch (type)
        {
            case JTokenType.String: value = ""; break;
            case JTokenType.Integer: value = 0; break;
            case JTokenType.Float: value = 0.0; break;
            case JTokenType.Boolean: value = false; break;
            case JTokenType.Object: value = new JObject(); break;
            case JTokenType.Array: value = new JArray(); break;
            default: value = ""; break;
        }

        obj[name] = value;
    }

    // 显示添加数组元素菜单
    private void ShowAddElementMenu(JArray array, string parentPath)
    {
        GenericMenu menu = new GenericMenu();
        menu.AddItem(new GUIContent("String"), false, () => AddElement(array, parentPath, JTokenType.String));
        menu.AddItem(new GUIContent("Number (Integer)"), false, () => AddElement(array, parentPath, JTokenType.Integer));
        menu.AddItem(new GUIContent("Number (Float)"), false, () => AddElement(array, parentPath, JTokenType.Float));
        menu.AddItem(new GUIContent("Boolean"), false, () => AddElement(array, parentPath, JTokenType.Boolean));
        menu.AddItem(new GUIContent("Object"), false, () => AddElement(array, parentPath, JTokenType.Object));
        menu.AddItem(new GUIContent("Array"), false, () => AddElement(array, parentPath, JTokenType.Array));
        menu.ShowAsContext();
    }

    private void AddElement(JArray array, string parentPath, JTokenType type)
    {
        JToken value;
        switch (type)
        {
            case JTokenType.String: value = ""; break;
            case JTokenType.Integer: value = 0; break;
            case JTokenType.Float: value = 0.0; break;
            case JTokenType.Boolean: value = false; break;
            case JTokenType.Object: value = new JObject(); break;
            case JTokenType.Array: value = new JArray(); break;
            default: value = ""; break;
        }
        array.Add(value);
    }
}