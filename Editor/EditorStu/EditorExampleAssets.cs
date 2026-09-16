// =============================================================================
//  EditorExampleAssets.cs —— 案例用的示例资产，以及"怎么画自己的 UI"
// -----------------------------------------------------------------------------
//  本文件 4 个部分：
//      ① DemoRange           一个 [Serializable] 数据类（用来演示自定义 PropertyDrawer）
//      ② DemoConfigSO        ScriptableObject 配置资产（演示资产创建 / 读取 / 自定义 Inspector）
//      ③ DemoRangeDrawer     自定义 PropertyDrawer（把这个类型画成一条双块滑条）
//      ④ DemoConfigSOEditor  自定义 Inspector（演示 SerializedObject 的标准写法）
//
//  它被这两个窗口使用：
//      EditorGUIExample.cs           第 7 / 10 / 11 节
//      EditorDevelopmentExample.cs   第 3 节
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    #region ① 示例数据类型

    /// <summary>
    /// 演示用的普通数据类：只有 [Serializable] 时，Unity 会用"默认方式"画它（把字段逐个列出来）。
    /// 想让它长得不一样，就得写下面的 DemoRangeDrawer。
    /// </summary>
    [Serializable]
    public class DemoRange
    {
        [Tooltip("下限")]
        public float min = 20f;

        [Tooltip("上限")]
        public float max = 80f;

        public override string ToString()
        {
            return $"{min:F1} ~ {max:F1}";
        }
    }

    #endregion

    #region ② 示例配置资产

    /// <summary>
    /// 示例配置资产（ScriptableObject）：编辑器工具常见的"数据存放处"。
    /// 创建 / 读取都用 DemoConfigSO.CreateOrLoadExample()。
    /// </summary>
    public class DemoConfigSO : ScriptableObject
    {
        /// <summary>存放示例资产的目录（相对项目根目录）。</summary>
        public const string AssetFolder = "Assets/Scripts/upanda-framework/Editor/EditorStu/Demo";

        /// <summary>示例资产的完整路径。</summary>
        public const string AssetPath = AssetFolder + "/DemoConfig.asset";

        [Tooltip("显示名称")]
        public string displayName = "示例配置";

        [Range(0, 100)]
        public int count = 10;

        public float speed = 1f;

        public Vector3 spawnPoint = new Vector3(0f, 1f, 0f);

        public Color color = Color.cyan;

        [Tooltip("这个字段会被 DemoRangeDrawer 画成一条双块滑条")]
        public DemoRange demoRange = new DemoRange();

        public List<string> items = new List<string> { "第一项", "第二项" };

        [TextArea(2, 4)]
        public string note = "这是一个用于演示自定义 Inspector 与 PropertyDrawer 的示例资产。";

        public E_TestType flags = E_TestType.One | E_TestType.Two;

        /// <summary>演示 [ContextMenu]：在 Inspector 标题栏上右键就能看到「打印当前配置」。</summary>
        [ContextMenu("打印当前配置")]
        internal void LogConfig()
        {
            Debug.Log($"[DemoConfigSO] {displayName}　count={count}　speed={speed}　range={demoRange}　flags={flags}");
        }

        /// <summary>
        /// 创建（不存在时）或读取示例资产。
        /// <para>注意：AssetDatabase.CreateFolder 不会自动创建父目录，所以这里先用 Directory.CreateDirectory 建好目录再 Refresh。</para>
        /// </summary>
        public static DemoConfigSO CreateOrLoadExample()
        {
            DemoConfigSO asset = AssetDatabase.LoadAssetAtPath<DemoConfigSO>(AssetPath);
            if (asset != null)
                return asset;

            if (!Directory.Exists(AssetFolder))
            {
                Directory.CreateDirectory(AssetFolder);
                AssetDatabase.Refresh();   // 让 Unity 立刻看到这个新目录
            }

            asset = CreateInstance<DemoConfigSO>();
            AssetDatabase.CreateAsset(asset, AssetPath);
            AssetDatabase.SaveAssets();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
            Debug.Log($"[DemoConfigSO] 已创建示例资产：{AssetPath}");
            return asset;
        }
    }

    #endregion

    #region ③ 自定义 PropertyDrawer

    /// <summary>
    /// 自定义属性绘制器：告诉 Unity"DemoRange 这个类型在 Inspector 里怎么画"。
    /// 只要某个字段的类型是 DemoRange（不管在哪个类里），都会用这个绘制器。
    /// </summary>
    [CustomPropertyDrawer(typeof(DemoRange))]
    public class DemoRangeDrawer : PropertyDrawer
    {
        private const float ValueWidth = 70f;

        /// <summary>这块 UI 需要多高。这里只要一行；不重写默认也是一行，显式写出来更直观。</summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty minProperty = property.FindPropertyRelative("min");
            SerializedProperty maxProperty = property.FindPropertyRelative("max");
            if (minProperty == null || maxProperty == null)
            {
                EditorGUI.LabelField(position, label.text, "找不到 min / max 字段");
                return;
            }

            // BeginProperty / EndProperty：让右键菜单、预制体覆盖、多选这些内置能力也作用到这块自定义 UI
            EditorGUI.BeginProperty(position, label, property);

            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, position.height);
            Rect valueRect = new Rect(position.xMax - ValueWidth, position.y, ValueWidth, position.height);
            Rect sliderRect = new Rect(
                position.x + EditorGUIUtility.labelWidth,
                position.y,
                position.width - EditorGUIUtility.labelWidth - ValueWidth - 4f,
                position.height);

            EditorGUI.PrefixLabel(labelRect, label);

            float min = minProperty.floatValue;
            float max = maxProperty.floatValue;
            EditorGUI.MinMaxSlider(sliderRect, ref min, ref max, 0f, 100f);
            minProperty.floatValue = Mathf.Min(min, max);
            maxProperty.floatValue = Mathf.Max(min, max);

            EditorGUI.LabelField(valueRect, $"{minProperty.floatValue:F0} ~ {maxProperty.floatValue:F0}");

            EditorGUI.EndProperty();
        }
    }

    #endregion

    #region ④ 自定义 CustomEditor

    /// <summary>
    /// 自定义 Inspector：DemoConfigSO 选中后在 Inspector 里就长这样。
    /// <para>标准写法：serializedObject.Update() → PropertyField → ApplyModifiedProperties()。</para>
    /// </summary>
    [CustomEditor(typeof(DemoConfigSO))]
    public class DemoConfigSOEditor : Editor
    {
        private bool showAdvanced;
        private bool showPropertyNames;

        public override void OnInspectorGUI()
        {
            DemoConfigSO config = (DemoConfigSO)target;

            EditorGUILayout.HelpBox("这是 DemoConfigSO 的自定义 Inspector（见 EditorExampleAssets.cs）。", MessageType.Info);

            // 1) 推荐写法：用 SerializedObject 画，多选 / Undo / Prefab 覆盖全都自动支持
            serializedObject.Update();

            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("count"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("speed"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spawnPoint"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("color"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("demoRange"));    // 由 DemoRangeDrawer 画
            EditorGUILayout.PropertyField(serializedObject.FindProperty("items"), true);  // true = 展开子属性（列表项）
            EditorGUILayout.PropertyField(serializedObject.FindProperty("note"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("flags"));

            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "更多画法", true);
            if (showAdvanced)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.LabelField("DisabledScope：下面这块是只读展示");
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("count"));
                }

                showPropertyNames = EditorGUILayout.Toggle("列出所有属性名（FindProperty 前用来对名字）", showPropertyNames);
                if (showPropertyNames)
                {
                    SerializedProperty iterator = serializedObject.GetIterator();
                    while (iterator.NextVisible(true))
                        EditorGUILayout.LabelField(iterator.propertyPath, iterator.propertyType.ToString(), EditorStyles.miniLabel);
                }

                EditorGUILayout.LabelField("其它写法：", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("　DrawDefaultInspector()：用默认方式画全部属性", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("　DrawPropertiesExcluding(serializedObject, \"note\")：画除 note 以外的属性", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("　EditorGUILayout.InspectorTitlebar()：画一个带折叠箭头的标题栏", EditorStyles.miniLabel);
            }

            serializedObject.ApplyModifiedProperties();   // 写回（自动记录 Undo）

            EditorGUILayout.Space();

            // 2) 老写法：直接改字段 —— 必须先 Undo.RecordObject，改完还要 SetDirty
            if (GUILayout.Button("老写法：数值清零（Ctrl+Z 可撤销）"))
            {
                Undo.RecordObject(config, "清空 DemoConfigSO");
                config.count = 0;
                config.speed = 0f;
                config.demoRange.min = 0f;
                config.demoRange.max = 0f;
                EditorUtility.SetDirty(config);
                serializedObject.Update();     // 让上面的界面同步刷新
            }

            // 3) 底部工具行
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打印当前配置"))
                config.LogConfig();
            if (GUILayout.Button("AssetDatabase.SaveAssets()"))
                AssetDatabase.SaveAssets();
            if (GUILayout.Button("在 Project 里高亮"))
                EditorGUIUtility.PingObject(config);
            EditorGUILayout.EndHorizontal();
        }
    }

    #endregion
}
