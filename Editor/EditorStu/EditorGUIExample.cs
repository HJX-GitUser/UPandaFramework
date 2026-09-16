// =============================================================================
//  EditorGUIExample.cs —— 编辑器控件速查表（把每个常用控件都摆出来看效果）
// -----------------------------------------------------------------------------
//  打开方式：菜单 UPandaGF/Tools/EditorGUI控件备忘录
//
//  【阅读顺序】
//      第 0 节   概念：GUI / GUILayout / EditorGUI / EditorGUILayout 的关系
//      第 1 节   文本与标签
//      第 2 节   数值输入（含 Delayed 延迟输入、三种滑条）
//      第 3 节   开关与开关组（含禁用、变更检测）
//      第 4 节   向量 / 矩形 / 边界
//      第 5 节   颜色、渐变、曲线
//      第 6 节   枚举、下拉、层级与标签（含真正的弹出菜单 GenericMenu）
//      第 7 节   资源对象（ObjectField / Selection / Ping / OpenAsset）
//      第 8 节   布局与分组（Foldout / ScrollView / 水平垂直 / GUILayoutOption / 缩进 / 间隔）
//      第 9 节   样式 GUIStyle / EditorStyles
//      第 10 节  进度条、帮助框、标题栏
//      第 11 节  SerializedProperty —— 自定义 Inspector 的核心（必看）
//      第 12 节  事件与快捷键
//
//  【三条铁律】
//      1. Begin / End 必须成对。推荐 using + XXXScope 写法，中途 return 或抛异常也不会漏掉 End。
//      2. 编辑器 UI 每帧重绘：状态一律放字段里；想让它在脚本重编译、重启编辑器后还保留，
//         必须加 [SerializeField]（static / readonly / private 未标记的都不会被序列化）。
//      3. 控件的返回值一定要接住：xxx = EditorGUILayout.XxxField(...)，
//         只写 EditorGUILayout.XxxField(...) 而不赋值，用户改了也不会生效。
//
//  配套文件（同目录）：
//      EditorDevelopmentExample.cs —— 编辑器"开发流程"类案例（菜单/资产/回调/拖拽/EditorPrefs...）
//      EditorExampleAssets.cs      —— 案例用的示例资产（ScriptableObject + PropertyDrawer + CustomEditor）
// =============================================================================

using System;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 演示用枚举：配合 EnumPopup（单选）/ EnumFlagsField（多选）使用。
    /// <para>注意 [Flags] 特性：只有标了它，EnumFlagsField / MaskField 的"多选"语义才成立。</para>
    /// <para>组合值（OneAndTwo）不会作为独立选项出现在多选下拉里，而是显示成"One + Two"。</para>
    /// </summary>
    [Flags]
    public enum E_TestType
    {
        None = 0,
        One = 1,
        Two = 2,
        Three = 4,
        OneAndTwo = One | Two,
    }

    /// <summary>
    /// 编辑器控件速查窗口：左边是控件名，右边是能真实操作的控件，点一点就知道它长什么样、返回值是什么。
    /// </summary>
    public class EditorGUIExample : EditorWindow
    {
        /// <summary>菜单路径（抽成常量，方便和字面量式的 [MenuItem] 对齐）。</summary>
        private const string MenuPath = "UPandaGF/Tools/EditorGUI控件备忘录";

        #region 打开窗口

        [MenuItem(MenuPath, priority = 100)]
        private static void OpenWindow()
        {
            EditorGUIExample window = GetWindow<EditorGUIExample>();
            // GetWindow 已经把窗口显示出来了，不需要再调 Show()。
            // 想改标题用 titleContent（能带 tooltip），而不是给 GetWindow 传字符串。
            window.titleContent = new GUIContent("编辑器控件备忘录", "EditorGUI / EditorGUILayout 控件速查");
            window.minSize = new Vector2(430f, 320f);
        }

        #endregion

        #region 章节定义

        /// <summary>章节编号。用它去访问 sectionOpen / sectionTitles / sectionKeywords。</summary>
        private enum Section
        {
            Readme = 0,
            Text,
            Number,
            Boolean,
            Vector,
            ColorAndCurve,
            Choice,
            Object,
            Layout,
            Style,
            Progress,
            Property,
            Event,
            Count,
        }

        /// <summary>章节标题（显示在窗口里）。</summary>
        private static readonly string[] sectionTitles =
        {
            "0. 先读这里：GUI / EditorGUI / EditorGUILayout 的关系",
            "1. 文本与标签",
            "2. 数值输入（含延迟输入、滑条）",
            "3. 开关与开关组（含禁用、变更检测）",
            "4. 向量 / 矩形 / 边界",
            "5. 颜色、渐变与曲线",
            "6. 枚举、下拉与选择（含真正的弹出菜单）",
            "7. 资源对象（ObjectField / Selection / Ping）",
            "8. 布局与分组（Foldout / ScrollView / GUILayoutOption）",
            "9. 样式 GUIStyle / EditorStyles",
            "10. 进度条、帮助框、标题栏",
            "11. SerializedProperty（自定义 Inspector 的核心）",
            "12. 事件与快捷键",
        };

        /// <summary>
        /// 每个章节的关键词（只用在中文/英文搜索里，不显示）。
        /// 顶部搜索框会同时匹配"标题"和"关键词"，所以搜 "Slider" 能找到"2. 数值输入"这一节。
        /// </summary>
        private static readonly string[] sectionKeywords =
        {
            "说明 概念 GUI GUILayout EditorGUI EditorGUILayout 文档 区别",
            "LabelField 文本 SelectableLabel 复制 TextField TextArea 多行 PasswordField 密码 DelayedTextField LinkButton 链接 图标 tooltip",
            "IntField LongField FloatField DoubleField Delayed 延迟 Slider IntSlider MinMaxSlider 滑条 数值 输入框",
            "Toggle ToggleLeft 开关 ToggleGroup 开关组 DisabledScope 禁用 BeginChangeCheck EndChangeCheck 变更检测",
            "Vector2 Vector3 Vector4 Vector2Int Vector3Int Rect RectInt Bounds BoundsInt 向量 矩形 边界",
            "ColorField GradientField CurveField 颜色 渐变 曲线 取色器 Alpha HDR",
            "EnumPopup EnumFlagsField Flags Popup IntPopup MaskField DropdownButton GenericMenu LayerField LayerMaskField TagField 枚举 下拉 层级 标签 遮罩",
            "ObjectField 资源 引用 拖拽 Selection Ping PingObject OpenAsset 选择",
            "Foldout BeginFoldoutHeaderGroup ScrollView Horizontal Vertical Scope Space Indent 缩进 GUILayoutOption Width Height Expand 间隔 分割线 布局",
            "GUIStyle GUISkin EditorStyles 样式 字体 字号 颜色 miniButton toolbarButton",
            "ProgressBar 进度 HelpBox 帮助框 InspectorTitlebar 标题栏 提示",
            "SerializedProperty SerializedObject PropertyField FindProperty ApplyModifiedProperties 属性 自定义 Inspector",
            "Event KeyCode 快捷键 mousePosition Repaint 事件",
        };

        #endregion

        #region 演示状态（加 [SerializeField] 才能在重编译后保留）

        // 界面本身的状态
        [SerializeField] private bool[] sectionOpen;    // 每一节是否展开
        [SerializeField] private string filter = string.Empty;
        [SerializeField] private Vector2 scroll;        // 外层滚动位置
        [SerializeField] private Vector2 innerScroll;   // 第 8 节内嵌小滚动区的位置

        // 1 文本
        [SerializeField] private string textValue = "可以编辑的文本";
        [SerializeField] private string delayedTextValue = "回车 / 失去焦点后才生效";
        [SerializeField] private string textAreaValue = "多行文本第一行\n多行文本第二行";
        [SerializeField] private string passwordValue = "123456";

        // 2 数值
        [SerializeField] private int intValue = 10;
        [SerializeField] private int delayedIntValue = 10;
        [SerializeField] private long longValue = 100L;
        [SerializeField] private float floatValue = 1.5f;
        [SerializeField] private float delayedFloatValue = 1.5f;
        [SerializeField] private double doubleValue = 3.14d;
        [SerializeField] private float sliderValue = 3f;
        [SerializeField] private int intSliderValue = 3;
        [SerializeField] private float minSliderValue = 2f;
        [SerializeField] private float maxSliderValue = 8f;

        // 3 开关
        [SerializeField] private bool toggleValue;
        [SerializeField] private bool toggleLeftValue;
        [SerializeField] private bool toggleGroupValue;
        [SerializeField] private bool toggleGroupChildValue;
        [SerializeField] private bool disableInput;
        [SerializeField] private int changeCheckValue = 5;
        [SerializeField] private int changeCheckCount;

        // 4 向量 / 矩形 / 边界
        [SerializeField] private Vector2 vector2Value = new Vector2(1f, 2f);
        [SerializeField] private Vector3 vector3Value = new Vector3(1f, 2f, 3f);
        [SerializeField] private Vector4 vector4Value = new Vector4(1f, 2f, 3f, 4f);
        [SerializeField] private Vector2Int vector2IntValue = new Vector2Int(1, 2);
        [SerializeField] private Vector3Int vector3IntValue = new Vector3Int(1, 2, 3);
        [SerializeField] private Rect rectValue = new Rect(0f, 0f, 100f, 50f);
        [SerializeField] private RectInt rectIntValue = new RectInt(0, 0, 100, 50);
        [SerializeField] private Bounds boundsValue = new Bounds(Vector3.zero, Vector3.one);
        [SerializeField] private BoundsInt boundsIntValue = new BoundsInt(Vector3Int.zero, Vector3Int.one);

        // 5 颜色 / 渐变 / 曲线
        [SerializeField] private Color colorValue = Color.white;
        [SerializeField] private Color colorWithAlpha = new Color(1f, 0.5f, 0f, 0.5f);
        [SerializeField] private Gradient gradientValue = new Gradient();
        [SerializeField] private AnimationCurve curveValue = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve curveRanged = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        // 6 选择类
        [SerializeField] private E_TestType enumValue = E_TestType.One;
        [SerializeField] private E_TestType enumFlagsValue = E_TestType.One | E_TestType.Two;
        [SerializeField] private int popupIndex;
        [SerializeField] private int intPopupValue = 123;
        [SerializeField] private int maskValue = 1;
        [SerializeField] private int layerValue;
        [SerializeField] private LayerMask layerMaskValue = ~0;
        [SerializeField] private string tagValue = "Untagged";
        [SerializeField] private bool menuCheckValue = true;

        // 7 资源对象
        [SerializeField] private GameObject gameObjectValue;
        [SerializeField] private UnityEngine.Object sceneObjectValue;
        [SerializeField] private DemoConfigSO configAsset;

        // 8 布局
        [SerializeField] private bool foldoutValue = true;
        [SerializeField] private bool headerFoldoutValue = true;
        [SerializeField] private float fadeValue = 1f;

        // 10 进度 / 标题栏
        [SerializeField] private float progressValue = 0.35f;
        [SerializeField] private bool titlebarFoldout = true;

        // 11 SerializedProperty
        [SerializeField] private Vector2 propertyScroll;

        // 12 事件
        [SerializeField] private string lastEventInfo = "（在窗口里点一下鼠标、按一下键看看）";

        /// <summary>下拉框用的选项文字与对应整数（IntPopup 就是靠它把"索引"和"值"分开）。</summary>
        private static readonly string[] options = { "选项 123", "选项 234", "选项 345" };
        private static readonly int[] optionValues = { 123, 234, 345 };

        #endregion

        #region 自定义样式（GUIStyle 不能每帧 new，缓存成字段）

        private GUIStyle customLabelStyle;

        /// <summary>橙色 16 号粗体：基于内置样式改，而不是从零 new 一个。</summary>
        private GUIStyle CustomLabelStyle
        {
            get
            {
                if (customLabelStyle == null)
                {
                    customLabelStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
                    customLabelStyle.normal.textColor = new Color(1f, 0.6f, 0.1f);
                }
                return customLabelStyle;
            }
        }

        #endregion

        #region 窗口生命周期

        private void OnEnable()
        {
            // 数组字段可能是 null（第一次打开）或长度对不上（后来加了新章节），都重建一次
            if (sectionOpen == null || sectionOpen.Length != (int)Section.Count)
            {
                sectionOpen = new bool[(int)Section.Count];
                SetAllSections(true);   // 第一次打开默认全部展开
            }
        }

        #endregion

        #region OnGUI —— 窗口内容

        private void OnGUI()
        {
            DrawToolbar();

            // 整个窗口只有一个滚动视图；第 8 节里还会演示"固定高度的小滚动区"
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawReadmeSection();
            DrawTextSection();
            DrawNumberSection();
            DrawBooleanSection();
            DrawVectorSection();
            DrawColorSection();
            DrawChoiceSection();
            DrawObjectSection();
            DrawLayoutSection();
            DrawStyleSection();
            DrawProgressSection();
            DrawPropertySection();
            DrawEventSection();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>顶部工具条：搜索 + 全部展开 / 折叠。</summary>
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            filter = GUILayout.TextField(filter, EditorStyles.toolbarSearchField, GUILayout.Width(180f));
            if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                filter = string.Empty;

            if (GUILayout.Button("全部展开", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                SetAllSections(true);
            if (GUILayout.Button("全部折叠", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                SetAllSections(false);

            GUILayout.FlexibleSpace();
            GUILayout.Label($"命中 {CountMatchedSections()}/{sectionTitles.Length} 节", EditorStyles.miniLabel, GUILayout.Width(90f));
            GUILayout.Label($"Unity {Application.unityVersion}", EditorStyles.miniLabel, GUILayout.Width(110f));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);
        }

        #endregion

        #region 0 概念说明

        private void DrawReadmeSection()
        {
            using (SectionScope section = BeginSection(Section.Readme))
            {
                if (!section.IsOpen)
                    return;

                EditorGUILayout.HelpBox(
                    "一、谁是谁\n" +
                    "    GUI / GUILayout              ：运行时 UI（做游戏界面用）。\n" +
                    "    EditorGUI / EditorGUILayout  ：编辑器 UI（做 Inspector、窗口、工具用）。\n\n" +
                    "    规律：EditorGUI 的方法几乎都要你传一个 Rect（位置自己算）；\n" +
                    "          EditorGUILayout 的方法只要传值，位置由它自动排（带 Layout = 自动布局）。\n" +
                    "          GUILayout / EditorGUILayout 的关系也是这样。\n\n" +
                    "二、布局选项 GUILayoutOption（第 8 节有可点的实例）\n" +
                    "    GUILayout.Width(300) / Height(200)    —— 固定宽高\n" +
                    "    GUILayout.MinWidth(50) / MinHeight(50) —— 允许的最小宽高\n" +
                    "    GUILayout.MaxWidth(100) / MaxHeight(100) —— 允许的最大宽高\n" +
                    "    GUILayout.ExpandWidth(true) / ExpandHeight(false) —— 是否允许被撑满\n\n" +
                    "三、Begin / End 必须成对，推荐 using 写法（XXXScope），中途 return 也不会漏 End。\n\n" +
                    "四、编辑器 UI 每帧重绘，所以状态都放字段里；要持久化就加 [SerializeField]。\n\n"
                    + "官方文档：https://docs.unity.cn/cn/2021.3/ScriptReference/EditorGUILayout.html",
                    MessageType.Info, true);
            }
        }

        #endregion

        #region 1 文本与标签

        private void DrawTextSection()
        {
            using (SectionScope section = BeginSection(Section.Text))
            {
                if (!section.IsOpen)
                    return;

                // LabelField：只读文本。一个 string 是"一行文字"，两个 string 是"标题 + 内容"
                EditorGUILayout.LabelField("编辑器路径：Editor/EditorStu/EditorGUIExample.cs");
                EditorGUILayout.LabelField("文本标题", "测试内容");

                // GUIContent = 文字 + 图标 + 鼠标悬停提示（tooltip），所有控件都能用它当标签
                EditorGUILayout.LabelField(new GUIContent(
                    "带图标的文本（鼠标停上来有提示）",
                    EditorGUIUtility.IconContent("console.infoicon").image,
                    "这句话就是 tooltip"));

                // SelectableLabel：可以选中、复制的只读文本，显示路径 / ID 很好用
                EditorGUILayout.SelectableLabel(
                    "SelectableLabel：这段文字可以被选中、复制",
                    EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));

                // 可编辑文本系列
                textValue = EditorGUILayout.TextField("TextField 输入：", textValue);
                delayedTextValue = EditorGUILayout.DelayedTextField("DelayedTextField（回车/失焦才生效）：", delayedTextValue);

                EditorGUILayout.LabelField("TextArea 多行输入：");
                textAreaValue = EditorGUILayout.TextArea(textAreaValue, GUILayout.Height(50f));

                passwordValue = EditorGUILayout.PasswordField("PasswordField（内容变成圆点）：", passwordValue);

                if (EditorGUILayout.LinkButton("LinkButton（蓝色链接样式的按钮）"))
                    Debug.Log("[EditorGUIExample] 点击了 LinkButton");
            }
        }

        #endregion

        #region 2 数值输入

        private void DrawNumberSection()
        {
            using (SectionScope section = BeginSection(Section.Number))
            {
                if (!section.IsOpen)
                    return;

                intValue = EditorGUILayout.IntField("IntField 整数输入框：", intValue);
                EditorGUILayout.LabelField($"    ↑ Int 当前值：{intValue}");

                longValue = EditorGUILayout.LongField("LongField 长整数输入框：", longValue);
                floatValue = EditorGUILayout.FloatField("FloatField 浮点输入框：", floatValue);
                doubleValue = EditorGUILayout.DoubleField("DoubleField 双精度输入框：", doubleValue);

                // Delayed 系列：用户按回车 或 焦点离开控件 之前，返回值不会改变
                delayedIntValue = EditorGUILayout.DelayedIntField("DelayedIntField（回车才生效）：", delayedIntValue);
                EditorGUILayout.LabelField($"    ↑ DelayedInt 当前值：{delayedIntValue}（编辑中不会变）");
                delayedFloatValue = EditorGUILayout.DelayedFloatField("DelayedFloatField（回车才生效）：", delayedFloatValue);

                // 三种滑条
                sliderValue = EditorGUILayout.Slider("Slider 浮点滑条：", sliderValue, 0f, 10f);
                intSliderValue = EditorGUILayout.IntSlider("IntSlider 整数滑条：", intSliderValue, 0, 10);
                EditorGUILayout.MinMaxSlider("MinMaxSlider 双块滑条：", ref minSliderValue, ref maxSliderValue, 0f, 10f);
                EditorGUILayout.LabelField($"    ↑ 双块滑条的左/右值：{minSliderValue:F2} ~ {maxSliderValue:F2}");
            }
        }

        #endregion

        #region 3 开关与开关组

        private void DrawBooleanSection()
        {
            using (SectionScope section = BeginSection(Section.Boolean))
            {
                if (!section.IsOpen)
                    return;

                toggleValue = EditorGUILayout.Toggle("Toggle 开关控件：", toggleValue);
                toggleLeftValue = EditorGUILayout.ToggleLeft("ToggleLeft（文字在左，整行都能点）", toggleLeftValue);

                // ToggleGroup：返回值是"组的开关"；组关掉后组内控件会变灰（值不变，只是不能编辑）
                toggleGroupValue = EditorGUILayout.BeginToggleGroup("ToggleGroup 开关组（关掉后组内变灰）", toggleGroupValue);
                toggleGroupChildValue = EditorGUILayout.Toggle("组内开关：", toggleGroupChildValue);
                EditorGUILayout.LabelField($"    组内开关的值：{toggleGroupChildValue}");
                EditorGUILayout.EndToggleGroup();

                // DisabledScope：临时把一段 UI 变灰（不改变真实值），常用于"缺少前置条件时不可编辑"
                disableInput = EditorGUILayout.Toggle("禁用下面这段输入：", disableInput);
                using (new EditorGUI.DisabledScope(disableInput))
                {
                    textValue = EditorGUILayout.TextField("被禁用的输入框：", textValue);
                    if (GUILayout.Button("被禁用的按钮（点了没反应）"))
                        Debug.Log("[EditorGUIExample] 这行永远不会执行");
                }

                // BeginChangeCheck / EndChangeCheck：判断"这一段里有没有控件被改过"
                EditorGUI.BeginChangeCheck();
                changeCheckValue = EditorGUILayout.IntSlider("拖我试试（值改成 5 以外）：", changeCheckValue, 0, 10);
                if (EditorGUI.EndChangeCheck())
                {
                    changeCheckCount++;
                    Debug.Log($"[EditorGUIExample] 检测到改动，累计 {changeCheckCount} 次");
                }
                EditorGUILayout.LabelField($"    累计改动次数：{changeCheckCount}");
            }
        }

        #endregion

        #region 4 向量 / 矩形 / 边界

        private void DrawVectorSection()
        {
            using (SectionScope section = BeginSection(Section.Vector))
            {
                if (!section.IsOpen)
                    return;

                vector2Value = EditorGUILayout.Vector2Field("Vector2Field 二维向量：", vector2Value);
                vector3Value = EditorGUILayout.Vector3Field("Vector3Field 三维向量：", vector3Value);
                vector4Value = EditorGUILayout.Vector4Field("Vector4Field 四维向量：", vector4Value);
                vector2IntValue = EditorGUILayout.Vector2IntField("Vector2IntField 二维整数：", vector2IntValue);
                vector3IntValue = EditorGUILayout.Vector3IntField("Vector3IntField 三维整数：", vector3IntValue);

                rectValue = EditorGUILayout.RectField("RectField 矩形：", rectValue);
                rectIntValue = EditorGUILayout.RectIntField("RectIntField 整数矩形：", rectIntValue);

                boundsValue = EditorGUILayout.BoundsField("BoundsField 包围盒：", boundsValue);
                boundsIntValue = EditorGUILayout.BoundsIntField("BoundsIntField 整数包围盒：", boundsIntValue);
            }
        }

        #endregion

        #region 5 颜色、渐变与曲线

        private void DrawColorSection()
        {
            using (SectionScope section = BeginSection(Section.ColorAndCurve))
            {
                if (!section.IsOpen)
                    return;

                colorValue = EditorGUILayout.ColorField("ColorField 颜色（默认）：", colorValue);

                // 完整参数：label, value, showEyedropper(取色器), showAlpha(是否显示透明度), hdr(是否允许 HDR 值)
                colorWithAlpha = EditorGUILayout.ColorField(
                    new GUIContent("ColorField 颜色（取色器 + 透明度，不用 HDR）"),
                    colorWithAlpha, true, true, false);

                gradientValue = EditorGUILayout.GradientField("GradientField 渐变：", gradientValue);
                curveValue = EditorGUILayout.CurveField("CurveField 曲线：", curveValue);

                // 带颜色和取值范围限制的曲线（注意 Rect 是"显示范围"，不是裁剪数据）
                curveRanged = EditorGUILayout.CurveField(
                    "CurveField 曲线（绿色 + 限制显示范围）",
                    curveRanged, Color.green, new Rect(0f, 0f, 1f, 1f));
            }
        }

        #endregion

        #region 6 枚举、下拉与选择

        private void DrawChoiceSection()
        {
            using (SectionScope section = BeginSection(Section.Choice))
            {
                if (!section.IsOpen)
                    return;

                // EnumPopup：普通单选下拉
                enumValue = (E_TestType)EditorGUILayout.EnumPopup("EnumPopup 枚举单选：", enumValue);

                // EnumFlagsField：多选下拉（枚举类型必须标了 [Flags] 语义才对）
                enumFlagsValue = (E_TestType)EditorGUILayout.EnumFlagsField("EnumFlagsField 枚举多选：", enumFlagsValue);
                EditorGUILayout.LabelField($"    多选结果：{enumFlagsValue}（数值 {(int)enumFlagsValue}）");

                // Popup：返回"选了第几项"（索引）
                popupIndex = EditorGUILayout.Popup("Popup 索引下拉（返回索引）：", popupIndex, options);
                EditorGUILayout.LabelField($"    当前索引 {popupIndex} → 文字：{options[Mathf.Clamp(popupIndex, 0, options.Length - 1)]}");

                // IntPopup：返回"具体的整数"，索引和值可以不对应
                intPopupValue = EditorGUILayout.IntPopup("IntPopup 整数下拉（返回值）：", intPopupValue, options, optionValues);
                EditorGUILayout.LabelField($"    当前值：{intPopupValue}");

                // MaskField：位掩码下拉，等价于"勾选若干个位"，结果是一个 int（和 EnumFlagsField 是一对）
                maskValue = EditorGUILayout.MaskField("MaskField 位掩码：", maskValue, options);
                EditorGUILayout.LabelField(
                    $"    当前值：{maskValue}　二进制：{Convert.ToString(maskValue, 2).PadLeft(options.Length, '0')}");

                // DropdownButton：只告诉你"被按下"，它自己不会弹菜单，要自己配 GenericMenu
                Rect buttonRect = EditorGUILayout.GetControlRect();
                if (EditorGUI.DropdownButton(buttonRect, new GUIContent("DropdownButton：点我弹出真正的菜单"), FocusType.Keyboard))
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("普通菜单项"), false, () => Debug.Log("[EditorGUIExample] 点了普通菜单项"));
                    menu.AddItem(new GUIContent("带勾选的菜单项"), menuCheckValue, () => menuCheckValue = !menuCheckValue);
                    menu.AddSeparator(string.Empty);
                    menu.AddDisabledItem(new GUIContent("禁用项（点不动）"));
                    menu.DropDown(buttonRect);   // 立即弹出；想弹在鼠标位置就用 menu.ShowAsContext()
                }
                EditorGUILayout.LabelField($"    带勾选菜单项的状态：{menuCheckValue}（弹菜单再点它就会变）");

                // 层级 / 层级遮罩 / 标签
                layerValue = EditorGUILayout.LayerField("LayerField 层级选择：", layerValue);

                // 层级遮罩（LayerMask）没有专门的控件：2021.3 的做法是 MaskField + 32 个层级名
                layerMaskValue = EditorGUILayout.MaskField("MaskField 层级遮罩（LayerMask）：", layerMaskValue.value, GetLayerNames());

                tagValue = EditorGUILayout.TagField("TagField 标签选择：", tagValue);
                EditorGUILayout.LabelField($"    当前层级名：{LayerMask.LayerToName(layerValue)}　当前标签：{tagValue}");
            }
        }

        #endregion

        #region 7 资源对象

        private void DrawObjectSection()
        {
            using (SectionScope section = BeginSection(Section.Object))
            {
                if (!section.IsOpen)
                    return;

                // 最后一个参数 allowSceneObjects：false = 只能拖 Project 里的资源；true = 也能拖场景里的物体
                gameObjectValue = EditorGUILayout.ObjectField(
                    "ObjectField 关联 GameObject（不允许场景物体）", gameObjectValue, typeof(GameObject), false) as GameObject;

                sceneObjectValue = EditorGUILayout.ObjectField(
                    "ObjectField 关联任意对象（允许场景物体）", sceneObjectValue, typeof(UnityEngine.Object), true);

                configAsset = EditorGUILayout.ObjectField(
                    "ObjectField 关联示例配置 DemoConfigSO", configAsset, typeof(DemoConfigSO), false) as DemoConfigSO;

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(configAsset == null))
                {
                    if (GUILayout.Button("选中它（Selection）", GUILayout.Width(160f)))
                        Selection.activeObject = configAsset;
                    if (GUILayout.Button("高亮定位（Ping）", GUILayout.Width(150f)))
                        EditorGUIUtility.PingObject(configAsset);
                    if (GUILayout.Button("打开它（OpenAsset）", GUILayout.Width(150f)))
                        AssetDatabase.OpenAsset(configAsset);
                }
                if (GUILayout.Button("创建 / 读取示例资产", GUILayout.Width(150f)))
                    configAsset = DemoConfigSO.CreateOrLoadExample();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("    提示：下面第 10、11 节会用到这个 DemoConfigSO，建议先点一下「创建 / 读取示例资产」。");
            }
        }

        #endregion

        #region 8 布局与分组

        private void DrawLayoutSection()
        {
            using (SectionScope section = BeginSection(Section.Layout))
            {
                if (!section.IsOpen)
                    return;

                // ---- Foldout：最基础的折叠，bool 自己存 ----
                foldoutValue = EditorGUILayout.Foldout(foldoutValue, "Foldout 折叠控件（点我）", true);
                if (foldoutValue)
                {
                    EditorGUILayout.LabelField("      折叠里的内容");
                    EditorGUILayout.HelpBox("Foldout 和 BeginFoldoutHeaderGroup 的区别：后者带标题栏样式，更适合当「章节」用。", MessageType.None);
                }

                EditorGUILayout.Space(4f);

                // ---- BeginFoldoutHeaderGroup：本窗口每一节就是用它做的 ----
                headerFoldoutValue = EditorGUILayout.BeginFoldoutHeaderGroup(headerFoldoutValue, "BeginFoldoutHeaderGroup 折叠标题栏");
                if (headerFoldoutValue)
                    EditorGUILayout.LabelField("      折叠标题栏里的内容");
                EditorGUILayout.EndFoldoutHeaderGroup();

                EditorGUILayout.Space(4f);

                // ---- ScrollView：Begin/End 版 ----
                EditorGUILayout.LabelField("BeginScrollView 固定高度 60 的小滚动区（里面有 10 行，滚一下试试）：");
                innerScroll = EditorGUILayout.BeginScrollView(innerScroll, GUILayout.Height(60f));
                for (int i = 0; i < 10; i++)
                    EditorGUILayout.LabelField($"      小滚动区第 {i + 1} 行");
                EditorGUILayout.EndScrollView();

                // ---- ScrollViewScope：using 版（推荐），注意把 scrollPosition 存回字段 ----
                using (EditorGUILayout.ScrollViewScope scrollScope = new EditorGUILayout.ScrollViewScope(innerScroll, GUILayout.Height(40f)))
                {
                    innerScroll = scrollScope.scrollPosition;
                    EditorGUILayout.LabelField("      用 using (new EditorGUILayout.ScrollViewScope(...)) 写的小滚动区");
                    EditorGUILayout.LabelField("      它和上面那个共享同一个滚动位置字段，所以会一起滚");
                }

                EditorGUILayout.Space(4f);

                // ---- 水平 / 垂直布局 ----
                EditorGUILayout.LabelField("水平布局（BeginHorizontal / EndHorizontal）：");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("水平布局1", GUILayout.Width(90f));
                EditorGUILayout.LabelField("水平布局2", GUILayout.Width(90f));
                EditorGUILayout.LabelField("水平布局3", GUILayout.Width(90f));
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("垂直布局（BeginVertical / EndVertical）：");
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField("　　垂直布局1");
                EditorGUILayout.LabelField("　　垂直布局2");
                EditorGUILayout.EndVertical();

                // ---- 同样的东西，用 Scope / using 写（推荐）----
                EditorGUILayout.LabelField("上面两种布局的 using 写法（HorizontalScope / VerticalScope）：");
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("HorizontalScope 1", GUILayout.Width(160f));
                    EditorGUILayout.LabelField("HorizontalScope 2");
                }
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField("　　VerticalScope 1");
                    EditorGUILayout.LabelField("　　VerticalScope 2");
                }

                EditorGUILayout.Space(4f);

                // ---- GUILayoutOption 布局选项（就是开头第 0 节文字里列的那些）----
                EditorGUILayout.LabelField("GUILayoutOption 布局选项实例（同一行里各自不同宽度）：");
                EditorGUILayout.BeginHorizontal();
                GUILayout.Button("Width(80)", GUILayout.Width(80f));
                GUILayout.Button("Height(40)", GUILayout.Height(40f));
                GUILayout.Button("MinWidth(140)", GUILayout.MinWidth(140f));
                GUILayout.Button("MaxWidth(60)", GUILayout.MaxWidth(60f));
                GUILayout.Button("ExpandWidth(true)", GUILayout.ExpandWidth(true));
                GUILayout.Button("ExpandWidth(false)", GUILayout.ExpandWidth(false));
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("整行按钮：ExpandWidth(true) 会把它撑满整行", GUILayout.ExpandWidth(true)))
                    Debug.Log("[EditorGUIExample] 点了整行按钮");

                EditorGUILayout.Space(4f);
                fadeValue = EditorGUILayout.Slider("FadeGroup 淡入淡出（拖动看下面高度变化）：", fadeValue, 0f, 1f);
                using (EditorGUILayout.FadeGroupScope fade = new EditorGUILayout.FadeGroupScope(fadeValue))
                {
                    if (fade.visible)
                    {
                        EditorGUILayout.HelpBox("这一整块是 FadeGroupScope：value = 1 时完全显示，0 时高度收缩为 0。", MessageType.None);
                        EditorGUILayout.LabelField("      折叠/展开动画就是靠它做的");
                    }
                }

                EditorGUILayout.Space(4f);

                // ---- 缩进：改的是"全局缩进层级"，用完记得还原 ----
                EditorGUILayout.LabelField("缩进演示（EditorGUI.indentLevel）：");
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("缩进 1 级");
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("缩进 2 级");
                EditorGUI.indentLevel--;
                EditorGUI.indentLevel--;   // 一定要还原，否则后面的控件全被缩进

                EditorGUILayout.Space(4f);

                // ---- 间隔 Space ----
                EditorGUILayout.LabelField("Space 间隔演示（下面四行的间距依次变大）：");
                EditorGUILayout.LabelField("　↑ 上一行");
                EditorGUILayout.Space(10f);
                EditorGUILayout.LabelField("　↑ 上面是 Space(10)（常用值）");
                EditorGUILayout.Space(50f);
                EditorGUILayout.LabelField("　↑ 上面是 Space(50)");
                EditorGUILayout.Space(100f);
                EditorGUILayout.LabelField("　↑ 上面是 Space(100)（太宽了，平时用不到）");

                // ---- 分割线：Unity 没提供控件，一般自己画 ----
                DrawSeparator();
                EditorGUILayout.LabelField("↑ 上面这条线是手绘的分割线（见 DrawSeparator 方法）");
                DrawSeparator();
            }
        }

        #endregion

        #region 9 样式

        private void DrawStyleSection()
        {
            using (SectionScope section = BeginSection(Section.Style))
            {
                if (!section.IsOpen)
                    return;

                EditorGUILayout.LabelField("EditorStyles.boldLabel 粗体", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("EditorStyles.miniLabel 小号浅色字", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("EditorStyles.miniBoldLabel 小号粗体", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("EditorStyles.largeLabel 大号字", EditorStyles.largeLabel);
                EditorGUILayout.LabelField(
                    "EditorStyles.wordWrappedLabel 会自动换行的长文本：" + new string('字', 40) + "（窗口窄了就会折行）",
                    EditorStyles.wordWrappedLabel);

                if (GUILayout.Button("EditorStyles.miniButton：小按钮", EditorStyles.miniButton))
                    Debug.Log("[EditorGUIExample] miniButton");
                if (GUILayout.Button("EditorStyles.toolbarButton：工具条按钮", EditorStyles.toolbarButton))
                    Debug.Log("[EditorGUIExample] toolbarButton");
                if (GUILayout.Button("GUI.skin.button：运行时默认按钮样式", GUI.skin.button))
                    Debug.Log("[EditorGUIExample] GUI.skin.button");

                EditorGUILayout.Space(4f);
                DrawSeparator();

                // 自定义样式：改字号 / 颜色，注意 GUIStyle 要缓存（见 CustomLabelStyle）
                EditorGUILayout.LabelField("下面这行用的是自定义 GUIStyle（橙色 16 号粗体）：", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("自定义 GUIStyle 示例文本", CustomLabelStyle);

                EditorGUILayout.HelpBox(
                    "要改样式就 new GUIStyle(某个内置样式) 再改属性，别从零 new GUIStyle()（会丢掉默认边距和背景）。\n"
                    + "也别在 OnGUI 里每帧 new GUIStyle —— 会一直产生垃圾，缓存成字段（本文件的 CustomLabelStyle）。",
                    MessageType.None);
            }
        }

        #endregion

        #region 10 进度条、帮助框、标题栏

        private void DrawProgressSection()
        {
            using (SectionScope section = BeginSection(Section.Progress))
            {
                if (!section.IsOpen)
                    return;

                // 帮助框的四种图标
                EditorGUILayout.HelpBox("MessageType.None：普通提示（没有图标）", MessageType.None);
                EditorGUILayout.HelpBox("MessageType.Info：感叹号提示", MessageType.Info);
                EditorGUILayout.HelpBox("MessageType.Warning：警告符号提示", MessageType.Warning);
                EditorGUILayout.HelpBox("MessageType.Error：错误符号提示", MessageType.Error);

                EditorGUILayout.Space(6f);

                // 进度条：EditorGUILayout 没有"自动布局版"，要先用 GetControlRect 拿一个矩形，再交给 EditorGUI 画
                progressValue = EditorGUILayout.Slider("拖动改变进度：", progressValue, 0f, 1f);
                Rect progressRect = EditorGUILayout.GetControlRect(false, 20f);
                EditorGUI.ProgressBar(progressRect, progressValue, $"进度 {progressValue:P0}");

                EditorGUILayout.Space(6f);

                // InspectorTitlebar：给一个对象画"带折叠箭头 + 图标 + 名字"的标题栏
                if (configAsset != null)
                {
                    titlebarFoldout = EditorGUILayout.InspectorTitlebar(titlebarFoldout, configAsset);
                    if (titlebarFoldout)
                        EditorGUILayout.LabelField("      标题栏展开后的内容（这里通常画这个对象的属性）");
                }
                else
                {
                    EditorGUILayout.HelpBox("先在「7. 资源对象」里创建 / 指定一个 DemoConfigSO，这里就能看到 InspectorTitlebar。", MessageType.None);
                }
            }
        }

        #endregion

        #region 11 SerializedProperty（自定义 Inspector 的核心）

        /// <summary>缓存 SerializedObject：不能每帧 new（会丢 Undo 状态、还费性能）。</summary>
        private SerializedObject configSerialized;
        private UnityEngine.Object configSerializedTarget;

        private void DrawPropertySection()
        {
            using (SectionScope section = BeginSection(Section.Property))
            {
                if (!section.IsOpen)
                    return;

                if (configAsset == null)
                {
                    EditorGUILayout.HelpBox(
                        "先在第 7 节里创建或指定一个 DemoConfigSO 资产。\n"
                        + "这里会用 SerializedObject + PropertyField 把它的字段画出来 —— 这就是写自定义 Inspector 的标准套路。",
                        MessageType.Info);
                    return;
                }

                // 目标换了就重建（注意 SerializedObject 不是可序列化类型，域重载后会变 null，所以要判空）
                if (configSerialized == null || configSerializedTarget != configAsset)
                {
                    configSerialized = new SerializedObject(configAsset);
                    configSerializedTarget = configAsset;
                }

                EditorGUILayout.LabelField("下面这些控件不是一个个手写的，而是 PropertyField 按属性自动画出来的：", EditorStyles.miniLabel);

                propertyScroll = EditorGUILayout.BeginScrollView(propertyScroll, GUILayout.Height(190f));

                SerializedObject serializedConfig = configSerialized;
                serializedConfig.Update();                              // 1) 从真实对象读取最新值

                EditorGUILayout.PropertyField(serializedConfig.FindProperty("displayName"));
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("count"));
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("speed"));
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("spawnPoint"));
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("color"));
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("demoRange"));     // 这一项有自己的 PropertyDrawer
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("items"), true);   // true = 展开子属性（列表项）
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("note"));
                EditorGUILayout.PropertyField(serializedConfig.FindProperty("flags"));

                serializedConfig.ApplyModifiedProperties();             // 2) 写回真实对象（自动进 Undo）

                EditorGUILayout.EndScrollView();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("AssetDatabase.SaveAssets() 存盘", GUILayout.Width(200f)))
                    AssetDatabase.SaveAssets();
                if (GUILayout.Button("打印所有属性名到 Console", GUILayout.Width(200f)))
                    DumpPropertyNames(configAsset);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox(
                    "要点：\n"
                    + "1) 属性名是字符串（FindProperty(\"count\")），字段改名后这里会拿到 null，所以要判空或改成属性路径常量。\n"
                    + "2) 一定要 Update() → 画 → ApplyModifiedProperties()，否则改了不保存、也进不了 Undo。\n"
                    + "3) 用 PropertyField 画的对象，多选、Prefab 覆盖、Undo 都是自动支持的 —— 这就是推荐它的原因。",
                    MessageType.None);
            }
        }

        /// <summary>把所有属性名打到 Console：写 FindProperty 前先用它确认名字。</summary>
        private static void DumpPropertyNames(UnityEngine.Object target)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty iterator = serialized.GetIterator();
            Debug.Log($"[EditorGUIExample] {target.name} 的属性列表：");
            while (iterator.NextVisible(true))
                Debug.Log($"    {iterator.propertyPath}　（{iterator.propertyType}）");
        }

        #endregion

        #region 12 事件与快捷键

        private void DrawEventSection()
        {
            using (SectionScope section = BeginSection(Section.Event))
            {
                if (!section.IsOpen)
                    return;

                Event current = Event.current;

                EditorGUILayout.LabelField($"当前事件类型：{current.type}");
                EditorGUILayout.LabelField($"鼠标位置（窗口内坐标）：{current.mousePosition}");
                EditorGUILayout.LabelField($"鼠标按键：{current.button}　按住 Shift：{current.shift}　Ctrl：{current.control}");

                if (current.type == EventType.KeyDown)
                {
                    lastEventInfo = $"捕获到按键 {current.keyCode}（在 OnGUI 里用 Event.current 拿到的）";
                    Repaint();   // 需要"持续刷新"（比如显示实时数据）时，自己调 Repaint()
                }
                EditorGUILayout.LabelField($"最近一次记录：{lastEventInfo}");

                EditorGUILayout.HelpBox(
                    "1) Event.current 只有在窗口有焦点时才有鼠标 / 键盘事件；\n"
                    + "2) 想每帧刷新就自己调 Repaint()（也可以订阅 EditorApplication.update，见 EditorDevelopmentExample.cs）；\n"
                    + "3) 全局快捷键不要写在这里，用 [MenuItem] 的后缀：% = Ctrl/Cmd，# = Shift，& = Alt。\n"
                    + "   例：[MenuItem(\"UPandaGF/示例/打开 %#e\")]。",
                    MessageType.None);
            }
        }

        #endregion

        #region 公共小工具

        /// <summary>
        /// 开始一个"折叠标题栏"章节。用 using 包住，Dispose 时自动 EndFoldoutHeaderGroup：
        /// 哪怕中途 return 或抛异常，也不会出现"Begin 了没 End"导致的布局错乱。
        /// </summary>
        private SectionScope BeginSection(Section section)
        {
            int index = (int)section;

            if (!MatchFilter(index))
                return new SectionScope(false, false);   // 被搜索过滤掉了：不画，也就不需要 End

            if (!string.IsNullOrEmpty(filter))
                sectionOpen[index] = true;               // 搜索时自动展开命中的章节

            sectionOpen[index] = EditorGUILayout.BeginFoldoutHeaderGroup(sectionOpen[index], sectionTitles[index]);
            return new SectionScope(true, sectionOpen[index]);
        }

        /// <summary>章节折叠头的 using 包装，见 BeginSection 的注释。</summary>
        private readonly struct SectionScope : IDisposable
        {
            private readonly bool began;

            /// <summary>这一节是不是展开的（只有展开才需要画内容）。</summary>
            public bool IsOpen { get; }

            public SectionScope(bool began, bool isOpen)
            {
                this.began = began;
                IsOpen = isOpen;
            }

            public void Dispose()
            {
                if (began)
                    EditorGUILayout.EndFoldoutHeaderGroup();
            }
        }

        private void SetAllSections(bool open)
        {
            for (int i = 0; i < sectionOpen.Length; i++)
                sectionOpen[i] = open;
        }

        private bool MatchFilter(int index)
        {
            if (string.IsNullOrEmpty(filter))
                return true;

            return sectionTitles[index].IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || sectionKeywords[index].IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private int CountMatchedSections()
        {
            int count = 0;
            for (int i = 0; i < sectionTitles.Length; i++)
            {
                if (MatchFilter(i))
                    count++;
            }
            return count;
        }

        /// <summary>画一条水平分割线（Unity 没提供现成控件，一般这么画）。</summary>
        private static void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
        }

        private static string[] layerNames;

        /// <summary>
        /// 取 32 个层级名（缓存起来，不要每帧新建数组）。
        /// 注意：MaskField 的第 i 位对应"数组第 i 项"，所以必须是 32 项、空层级也要占位，否则勾选会错位。
        /// </summary>
        private static string[] GetLayerNames()
        {
            if (layerNames != null)
                return layerNames;

            layerNames = new string[32];
            for (int i = 0; i < layerNames.Length; i++)
            {
                string name = LayerMask.LayerToName(i);
                layerNames[i] = string.IsNullOrEmpty(name) ? $"层级 {i}（未命名）" : name;
            }
            return layerNames;
        }

        #endregion
    }
}
