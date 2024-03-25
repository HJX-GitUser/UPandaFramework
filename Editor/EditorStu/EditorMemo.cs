using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public enum E_TestType
{
    One = 1,
    Two = 2,
    Three = 4,
    One_and_Two = 1 | 2,
}

public class EditorMemo : EditorWindow
{
    void Start()
    {
        #region 知识回顾 以及 准备工作
        //1.知识回顾
        //GUILayout 是一个GUI自动布局的公共类
        //它其中的方法和GUI基本一模一样，都是用来绘制、响应各种UI控件的
        //只不过它在GUI的基础上加入了自动布局功能
        //我们无需过多的去关心UI控件的位置和大小

        //GUILayoutOption 布局选项
        //控件的固定宽高
        //GUILayout.Width(300);
        //GUILayout.Height(200);
        //允许控件的最小宽高
        //GUILayout.MinWidth(50);
        //GUILayout.MinHeight(50);
        //允许控件的最大宽高
        //GUILayout.MaxWidth(100);
        //GUILayout.MaxHeight(100);
        //允许或禁止水平拓展
        //GUILayout.ExpandWidth(true);//允许
        //GUILayout.ExpandHeight(false);//禁止
        //GUILayout.ExpandHeight(true);//允许
        //GUILayout.ExpandHeight(false);//禁止

        //2.准备工作
        //创建一个编辑器窗口 
        #endregion

        #region 知识点 EditorGUI是什么？
        //EditorGUI 类似 GUI
        //是一个主要用于绘制编辑器拓展 UI 的工具类
        //它提供了一些 GUI 中没有的API
        //主要是 编辑器功能中会用到的一些 特殊控件

        //而EditorGUILayout 类似于 GUILayout
        //是一个带有自动布局功能的 EditorGUI 绘制工具类

        //我们经常会将 EditorGUI 和 GUI 混合使用 来制作一些编辑器拓展功能
        //但是由于更多时候我们会用到自动布局功能
        //因此我们接下来着重讲解 EditorGUILayout 中的功能
        //EditorGUI和它的区别仅仅是需要自己设置位置而已

        //详细内容：https://docs.unity.cn/cn/2022.3/ScriptReference/EditorGUILayout.html
        #endregion
    }

    [MenuItem("Tools/EditorGUI控件备忘录")]
    private static void OpenLesson3()
    {
        EditorMemo win = EditorWindow.GetWindow<EditorMemo>("EditorGUI控件备忘录");
        //win.titleContent = new GUIContent("EditorGUI知识讲解窗口");
        win.Show();
    }
    int layer;
    string tag;
    Color color;

    E_TestType type;
    E_TestType type2;

    string[] strs = { "选择123", "选择234", "选择345" };
    int[] ints = { 123, 234, 345 };
    int num = 0;

    GameObject obj;

    int i;
    int i2;
    float f;
    double d;
    long l;

    string str;
    Vector2 vec2;
    Vector3 vec3;
    Vector4 vec4;

    Rect rect;
    Bounds bounds;
    BoundsInt boundsInt;

    bool isHide;
    bool isHideGroup;

    bool isTog;
    bool isTogLeft;

    bool isTogGroup;

    float fSlider;
    int iSlider;

    float leftV;
    float rightV;

    AnimationCurve curve = new AnimationCurve();

    Vector2 vec2Pos;

    private void OnGUI()
    {
        //窗口中的控件相关绘制 逻辑处理相关的内容
        //EditorGUI相关的控件 同样还是需要在OnGUI当中进行实现 才能被显示出来
        #region Lesson4 文本控件、层级标签控件、颜色获取控件
        //文本控件
        EditorGUILayout.LabelField("编辑器路径：Editor/EditorStu/EditorMemo");
        EditorGUILayout.LabelField("文本标题", "测试内容");
        //层级标签控件
        layer = EditorGUILayout.LayerField("层级选择", layer);
        tag = EditorGUILayout.TagField("标签选择", tag);
        //颜色获取控件
        color = EditorGUILayout.ColorField(new GUIContent("自定义颜色获取"), 
                                            color, true, false, false);
        #endregion

        #region Lesson5 枚举选择、整数选择、按下就触发的按钮

        isHide = EditorGUILayout.Foldout(isHide, "折叠控件", false);

        if(isHide)
        {
            //枚举选择
            type = (E_TestType)EditorGUILayout.EnumPopup("枚举选择", type);

            type2 = (E_TestType)EditorGUILayout.EnumFlagsField("枚举多选", type2);

            //整数选择控件
            //返回值 其实是整数数组当中的某一个值
            num = EditorGUILayout.IntPopup("整数单选框", num, strs, ints);
            EditorGUILayout.LabelField(num.ToString());

            //按下就响应的按钮
            if (EditorGUILayout.DropdownButton(new GUIContent("按钮上文字"), FocusType.Passive))
                Debug.Log("按下就响应");
        }
        #endregion

        #region Lesson7 折叠、折叠组控件
        isHideGroup = EditorGUILayout.BeginFoldoutHeaderGroup(isHideGroup, "折叠组控件");
        if (isHideGroup)
        {

            vec2Pos = EditorGUILayout.BeginScrollView(vec2Pos); //开启滚动视图
            #region Lesson6 对象关联、各类型输入
            //对象关联
            obj = EditorGUILayout.ObjectField("关联资源对象", obj, typeof(GameObject), false) as GameObject;
            //各类型输入
            i = EditorGUILayout.IntField("Int输入框", i);
            EditorGUILayout.LabelField(i.ToString());
            l = EditorGUILayout.LongField("long输入框", l);
            f = EditorGUILayout.FloatField("Float 输入：", f);
            d = EditorGUILayout.DoubleField("double 输入：", d);

            str = EditorGUILayout.TextField("Text输入：", str);
            vec2 = EditorGUILayout.Vector2Field("Vec2输入： ", vec2);
            vec3 = EditorGUILayout.Vector3Field("Vec3输入： ", vec3);
            vec4 = EditorGUILayout.Vector4Field("Vec4输入： ", vec4);

            rect = EditorGUILayout.RectField("rect输入： ", rect);
            bounds = EditorGUILayout.BoundsField("Bounds输入： ", bounds);
            boundsInt = EditorGUILayout.BoundsIntField("Bounds输入： ", boundsInt);

            //注意：EditorGUILayout中还有一些Delayed开头的输入控件
            //     他们和普通输入控件最主要的区别是：在用户按 Enter 键或将焦点从字段移开之前，返回值不会更改
            i2 = EditorGUILayout.DelayedIntField("Int输入框", i2);
            EditorGUILayout.LabelField(i2.ToString());
            #endregion
            

            #region Lesson8 开关、开关组控件
            //开关组控件
            isTogGroup = EditorGUILayout.BeginToggleGroup("开关组控件", isTogGroup);
            //开关控件
            isTog = EditorGUILayout.Toggle("开关控件", isTog);
            isTogLeft = EditorGUILayout.ToggleLeft("左侧开关", isTogLeft);
            EditorGUILayout.EndToggleGroup();
            #endregion

            #region Lesson9 滑动条、双块滑动条控件
            //滑动条
            fSlider = EditorGUILayout.Slider("滑动条", fSlider, 0, 10);
            iSlider = EditorGUILayout.IntSlider("整形滑动条", iSlider, 0, 10);
            //双块滑动条
            EditorGUILayout.MinMaxSlider("双块滑动条", ref leftV, ref rightV, 0, 10);
            EditorGUILayout.LabelField(leftV.ToString());
            EditorGUILayout.LabelField(rightV.ToString());
            #endregion

            #region Lesson10 帮助框、间隔控件
            //帮助框控件
            EditorGUILayout.HelpBox("一般提示", MessageType.None);
            EditorGUILayout.Space(10);
            EditorGUILayout.HelpBox("感叹号提示", MessageType.Info);
            EditorGUILayout.Space(50);
            EditorGUILayout.HelpBox("警告符号提示", MessageType.Warning);
            EditorGUILayout.Space(100);
            EditorGUILayout.HelpBox("错误符号提示", MessageType.Error);
            //间隔控件
            #endregion

            EditorGUILayout.EndScrollView(); //结束滚动视图
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        #endregion

        #region Lesson11 动画曲线控件和布局API
        //动画曲线控件
        curve = EditorGUILayout.CurveField("曲线控件", curve);
        //布局API
        EditorGUILayout.BeginHorizontal(); //开始水平布局
        EditorGUILayout.LabelField("123123");
        EditorGUILayout.LabelField("123123");
        EditorGUILayout.LabelField("123123");
        EditorGUILayout.EndHorizontal();//结束水平布局

        EditorGUILayout.BeginVertical();//开始垂直布局
        EditorGUILayout.LabelField("123123");
        EditorGUILayout.LabelField("123123");
        EditorGUILayout.LabelField("123123");
        EditorGUILayout.EndVertical();//结束垂直布局

        //vec2Pos = EditorGUILayout.BeginScrollView(vec2Pos); //开启滚动视图
        ////一大堆控件
        //EditorGUILayout.EndScrollView(); //结束滚动视图
        #endregion
    }
}
