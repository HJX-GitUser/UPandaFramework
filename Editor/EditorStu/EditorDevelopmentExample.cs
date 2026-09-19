// =============================================================================
//  EditorDevelopmentExample.cs —— 编辑器"开发流程"案例合集
// -----------------------------------------------------------------------------
//  打开方式：菜单 UPandaGF/Tools/编辑器开发案例（快捷键 Ctrl+Shift+E）
//
//  和 EditorGUIExample.cs 的分工：
//      EditorGUIExample.cs          —— 控制"长什么样"（控件速查）
//      本文件                        —— 控制"什么时候做、能做哪些事"（编辑器开发流程）
//
//  【案例目录】
//      1. 菜单 MenuItem（5 种写法 + validate + 快捷键 + priority）
//      2. 窗口 EditorWindow（生命周期 / 单例 / ShowUtility / ShowPopup）
//      3. 序列化与撤销（SerializedObject / Undo / SetDirty）
//      4. 资产操作（ScriptableObject 创建 / AssetDatabase / 进度条）
//      5. 交互（Selection / Ping / OpenAsset / DragAndDrop / 剪贴板 / JSON）
//      6. 持久化（EditorPrefs）
//      7. 回调（EditorApplication.update / delayCall / 层级窗口 / 场景视图 Handles）
//      8. 弹窗与通知（DisplayDialog / ShowNotification / RevealInFinder）
//      9. 环境信息（版本 / 平台 / 各种路径）
//
//  【本文件底部还有 4 个"不在窗口里"的案例】，都带了注释说明：
//      · Assets 右键菜单 + validate 函数（打印选中资源信息）
//      · GameObject 菜单 + Undo（创建带配置的空物体）
//      · [InitializeOnLoadMethod]（编辑器加载 / 重编译后自动执行一次）
//      · AssetModificationProcessor（保存资产前的钩子）
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 编辑器开发案例窗口：把"写一个编辑器工具"会遇到的能力挨个演示一遍，并且都能点。
    /// </summary>
    public class EditorDevelopmentExample : EditorWindow
    {
        /// <summary>菜单路径；"%#e" 是快捷键写法：% = Ctrl/Cmd，# = Shift，& = Alt。</summary>
        private const string MenuPath = "UPandaGF/Tools/编辑器开发案例 %#e";

        #region 打开窗口

        [MenuItem(MenuPath, priority = 101)]
        private static void OpenWindow()
        {
            EditorDevelopmentExample window = GetWindow<EditorDevelopmentExample>();
            window.titleContent = new GUIContent("编辑器开发案例", "编辑器开发常见能力的可点击案例");
            window.minSize = new Vector2(470f, 400f);
        }

        #endregion

        #region 字段

        private Vector2 scroll;

        // 各章节的展开状态（只是界面状态，不加 [SerializeField] 也行）
        private bool openMenu = true;
        private bool openWindow = true;
        private bool openSerialize = true;
        private bool openAsset = true;
        private bool openInteract = true;
        private bool openPrefs = true;
        private bool openCallback = true;
        private bool openDialog = true;
        private bool openInfo = true;

        // 3 序列化与撤销
        private DemoConfigSO configAsset;
        private SerializedObject configSerialized;
        private UnityEngine.Object configSerializedTarget;

        // 5 交互
        private readonly List<UnityEngine.Object> droppedObjects = new List<UnityEngine.Object>();
        private Vector2 droppedScroll;

        // 6 持久化（EditorPrefs 的读写演示）
        private const string PrefsIntKey = "UPandaGF.EditorExample.PrefsInt";
        private int prefsInt = 1;

        // 7 回调
        private const string LogOnLoadKey = "UPandaGF.EditorExample.LogOnLoad";
        private const string LogWillSaveKey = "UPandaGF.EditorExample.LogWillSave";
        private bool trackEditorTime;
        private double lastRefreshTime;
        private bool colorHierarchyNames;
        private bool drawSceneHandles = true;

        #endregion

        #region 生命周期：订阅 / 取消订阅回调

        private void OnEnable()
        {
            prefsInt = EditorPrefs.GetInt(PrefsIntKey, 1);   // 打开窗口时读一次

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            // 一定要在 OnDisable 里取消订阅：否则窗口关掉后回调还在跑，而且会一直引用已销毁的窗口
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.hierarchyWindowItemOnGUI -= OnHierarchyItemGUI;
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            DrawMenuSection();
            DrawWindowSection();
            DrawSerializeSection();
            DrawAssetSection();
            DrawInteractSection();
            DrawPrefsSection();
            DrawCallbackSection();
            DrawDialogSection();
            DrawInfoSection();

            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region 1 菜单 MenuItem

        private void DrawMenuSection()
        {
            openMenu = EditorGUILayout.BeginFoldoutHeaderGroup(openMenu, "1. 菜单 MenuItem（5 种写法）");
            if (openMenu)
            {
                EditorGUILayout.HelpBox(
                    "菜单就是给静态方法贴一个 [MenuItem] 特性，方法必须是 static（可用 private）。\n\n"
                    + "① 顶部菜单栏（本窗口就是这么加的）\n"
                    + "     [MenuItem(\"UPandaGF/Tools/编辑器开发案例\")]\n"
                    + "   写 \"A/B/C\" 会自动生成多级子菜单。\n\n"
                    + "② 带快捷键：把 \"%#e\" 拼在路径后面\n"
                    + "     % = Ctrl/Cmd　# = Shift　& = Alt　_字母 = 单个字母键\n\n"
                    + "③ Project 窗口右键菜单：路径以 Assets/ 开头（本文件底部有完整例子）\n"
                    + "     [MenuItem(\"Assets/UPandaGF/打印选中资源信息\")]\n"
                    + "     [MenuItem(\"Assets/UPandaGF/打印选中资源信息\", true)]   // 第二个参数 true = 这是 validate 函数，返回 bool\n\n"
                    + "④ Hierarchy 窗口右键菜单：路径以 GameObject/ 开头\n\n"
                    + "⑤ 组件 / 资产面板里的右键菜单：给方法贴 [ContextMenu(\"菜单名\")]（见 EditorExampleAssets.cs）\n\n"
                    + "priority（优先级）：只影响同级菜单项的排序，数字小的在上面；两项相差大于 10 会自动加一条分隔线。",
                    MessageType.Info, true);

                if (GUILayout.Button("执行一次「Assets 右键菜单」里那个方法", GUILayout.Width(280f)))
                    PrintSelectionInfo();

                if (GUILayout.Button("执行一次「GameObject 菜单」里那个方法（会创建一个空物体）", GUILayout.Width(380f)))
                    EditorExampleGameObjectMenu.CreateConfiguredObject();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region 2 窗口 EditorWindow

        private void DrawWindowSection()
        {
            openWindow = EditorGUILayout.BeginFoldoutHeaderGroup(openWindow, "2. 窗口 EditorWindow（生命周期 / 打开方式）");
            if (openWindow)
            {
                EditorGUILayout.HelpBox(
                    "打开窗口：\n"
                    + "     EditorWindow.GetWindow<T>()            取一个已存在的窗口，没有就新建（并自动显示），所以不用再 Show()\n"
                    + "     GetWindow<T>(utility: true)            打开成「浮动小窗」而不是停靠页签\n"
                    + "     window.ShowUtility()                   等价于上面的浮动小窗\n"
                    + "     window.ShowPopup()                     弹窗式（失焦自动关闭）\n"
                    + "     EditorWindow.HasOpenInstances<T>()     判断某类窗口是否已经开着\n\n"
                    + "生命周期消息（本窗口都打了日志，看 Console 就知道谁先谁后）：\n"
                    + "     OnEnable            创建 / 域重载后（订阅回调、读 EditorPrefs 放这里）\n"
                    + "     OnGUI               每帧绘制（可以有多次：Layout / Repaint 各一次）\n"
                    + "     OnFocus / OnLostFocus\n"
                    + "     OnSelectionChange / OnHierarchyChange / OnProjectChange\n"
                    + "     Update              每秒约 100 次（窗口开着时才收到）\n"
                    + "     OnDisable / OnDestroy  关闭前（取消订阅回调放这里）\n\n"
                    + "注意：EditorWindow 没有 Start() 这类 MonoBehaviour 的消息，\n"
                    + "      里面写的初始化代码永远不会执行 —— 要初始化就写 OnEnable()。",
                    MessageType.Info, true);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("打开子窗口（ShowUtility：浮动小窗）", GUILayout.Width(260f)))
                    GetWindow<EditorExampleMiniWindow>().ShowUtility();
                if (GUILayout.Button("打开子窗口（ShowPopup：弹窗）", GUILayout.Width(240f)))
                    GetWindow<EditorExampleMiniWindow>().ShowPopup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField($"子窗口（EditorExampleMiniWindow）当前是否打开：{EditorWindow.HasOpenInstances<EditorExampleMiniWindow>()}");
                EditorGUILayout.LabelField($"本窗口位置 / 大小：{position}");
                EditorGUILayout.LabelField($"刷新模式：{(focusedWindow == this ? "有焦点（会持续重绘）" : "没焦点")}");

                if (GUILayout.Button("Repaint()：手动要求重绘一次", GUILayout.Width(260f)))
                    Repaint();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region 3 序列化与撤销

        private void DrawSerializeSection()
        {
            openSerialize = EditorGUILayout.BeginFoldoutHeaderGroup(openSerialize, "3. 序列化与撤销（SerializedObject / Undo）");
            if (openSerialize)
            {
                EditorGUILayout.HelpBox(
                    "改编辑器目标对象的数据，只有两条路：\n"
                    + "① 推荐：SerializedObject + SerializedProperty\n"
                    + "     so.Update() → FindProperty(\"字段名\") 改值 → so.ApplyModifiedProperties()\n"
                    + "     好处：自动支持多选、自动进 Undo、Prefab 覆盖、属性名改名跟踪。\n"
                    + "② 老写法：直接改字段 + Undo.RecordObject(obj, \"操作名\") + EditorUtility.SetDirty(obj)\n"
                    + "     改了但没 SetDirty，Unity 不知道要存盘；没 RecordObject，Ctrl+Z 撤销不了。\n\n"
                    + "SerializedObject 不是可序列化类型，不能期望它跨域重载还存在 —— 用之前判空重建。",
                    MessageType.Info, true);

                configAsset = EditorGUILayout.ObjectField("示例配置资产", configAsset, typeof(DemoConfigSO), false) as DemoConfigSO;

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("创建 / 读取示例资产", GUILayout.Width(200f)))
                    configAsset = DemoConfigSO.CreateOrLoadExample();
                EditorGUILayout.EndHorizontal();

                SerializedObject serialized = GetConfigSerialized();

                if (serialized == null)
                {
                    EditorGUILayout.HelpBox("先点上面的按钮创建一个示例资产，下面的按钮才有目标。", MessageType.None);
                }
                else
                {
                    serialized.Update();
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("count +1（SerializedProperty 写法，可 Ctrl+Z）", GUILayout.Width(300f)))
                    {
                        SerializedProperty countProperty = serialized.FindProperty("count");
                        if (countProperty != null)
                            countProperty.intValue++;
                        serialized.ApplyModifiedProperties();      // 写回 + 自动记录 Undo
                    }
                    if (GUILayout.Button("speed +1（老写法，也可 Ctrl+Z）", GUILayout.Width(240f)))
                    {
                        Undo.RecordObject(configAsset, "示例：speed +1");   // 先记录再改
                        configAsset.speed += 1f;
                        EditorUtility.SetDirty(configAsset);                 // 告诉 Unity 数据变了
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Undo.PerformUndo()", GUILayout.Width(180f)))
                        Undo.PerformUndo();
                    if (GUILayout.Button("Undo.PerformRedo()", GUILayout.Width(180f)))
                        Undo.PerformRedo();
                    if (GUILayout.Button("AssetDatabase.SaveAssets()", GUILayout.Width(220f)))
                        AssetDatabase.SaveAssets();
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField("    当前值：", EditorStyles.miniLabel);
                    EditorGUILayout.LabelField($"    count = {configAsset.count}　speed = {configAsset.speed}　range = {configAsset.demoRange}");
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>拿到（必要时重建）示例资产的 SerializedObject。</summary>
        private SerializedObject GetConfigSerialized()
        {
            if (configAsset == null)
            {
                configSerialized = null;
                configSerializedTarget = null;
                return null;
            }

            if (configSerialized == null || configSerializedTarget != configAsset)
            {
                configSerialized = new SerializedObject(configAsset);
                configSerializedTarget = configAsset;
            }
            return configSerialized;
        }

        #endregion

        #region 4 资产操作

        private void DrawAssetSection()
        {
            openAsset = EditorGUILayout.BeginFoldoutHeaderGroup(openAsset, "4. 资产操作（ScriptableObject / AssetDatabase / 进度条）");
            if (openAsset)
            {
                EditorGUILayout.HelpBox(
                    "创建一个配置资产的标准流程：\n"
                    + "     目录不存在 → 先建目录（AssetDatabase.CreateFolder 不会自动建父目录）\n"
                    + "     asset = ScriptableObject.CreateInstance<T>()\n"
                    + "     AssetDatabase.CreateAsset(asset, \"Assets/xxx/Config.asset\")\n"
                    + "     AssetDatabase.SaveAssets()\n"
                    + "读取：AssetDatabase.LoadAssetAtPath<T>(path)（返回 null 就是不存在）",
                    MessageType.Info, true);

                EditorGUILayout.LabelField("示例资产路径（可选中复制）：", EditorStyles.miniLabel);
                EditorGUILayout.SelectableLabel(DemoConfigSO.AssetPath, EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.LabelField($"GUID：{AssetDatabase.AssetPathToGUID(DemoConfigSO.AssetPath)}");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("创建 / 读取", GUILayout.Width(140f)))
                    configAsset = DemoConfigSO.CreateOrLoadExample();
                if (GUILayout.Button("选中它", GUILayout.Width(100f)))
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<DemoConfigSO>(DemoConfigSO.AssetPath);
                if (GUILayout.Button("AssetDatabase.Refresh()", GUILayout.Width(200f)))
                    AssetDatabase.Refresh();
                if (GUILayout.Button("统计框架里的 ScriptableObject", GUILayout.Width(220f)))
                {
                    string[] guids = AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/Scripts/upanda-framework" });
                    Debug.Log($"[编辑器案例] upanda-framework 下有 {guids.Length} 个 ScriptableObject");
                }
                EditorGUILayout.EndHorizontal();

                if (GUILayout.Button("模拟耗时任务（可取消的进度条，约 1 秒）", GUILayout.Width(320f)))
                {
                    for (int i = 0; i < 50; i++)
                    {
                        bool canceled = EditorUtility.DisplayCancelableProgressBar(
                            "示例任务", $"正在处理第 {i + 1}/50 步", (i + 1) / 50f);
                        if (canceled)
                        {
                            Debug.LogWarning("[编辑器案例] 用户取消了任务");
                            break;
                        }
                        System.Threading.Thread.Sleep(10);   // 假装在干活
                    }
                    EditorUtility.ClearProgressBar();        // 一定要清掉，否则进度条会一直挂在编辑器上
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region 5 交互

        private void DrawInteractSection()
        {
            openInteract = EditorGUILayout.BeginFoldoutHeaderGroup(openInteract, "5. 交互（Selection / 拖拽 / 剪贴板 / JSON）");
            if (openInteract)
            {
                // 字段可能被域重载清成 null，用之前判空最保险
                if (droppedObjects == null)
                    droppedScroll = Vector2.zero;

                GameObject activeGameObject = Selection.activeGameObject;
                EditorGUILayout.LabelField($"Selection.activeObject：{(Selection.activeObject == null ? "（未选中）" : Selection.activeObject.name)}");
                EditorGUILayout.LabelField($"Selection.activeGameObject：{(activeGameObject == null ? "（未选中物体）" : activeGameObject.name)}");
                EditorGUILayout.LabelField($"选中数量：{Selection.objects.Length}　其中资产：{Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets).Length}");
                EditorGUILayout.LabelField($"选中路径：{AssetDatabase.GetAssetPath(Selection.activeObject)}");

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(Selection.activeObject == null))
                {
                    if (GUILayout.Button("Ping（在 Project 里高亮）", GUILayout.Width(180f)))
                        EditorGUIUtility.PingObject(Selection.activeObject);
                    if (GUILayout.Button("OpenAsset（打开它）", GUILayout.Width(160f)))
                        AssetDatabase.OpenAsset(Selection.activeObject);
                    if (GUILayout.Button("转成 JSON 并复制到剪贴板", GUILayout.Width(200f)))
                        GUIUtility.systemCopyBuffer = EditorJsonUtility.ToJson(Selection.activeObject, true);
                }
                EditorGUILayout.EndHorizontal();

                // ---- 拖拽接收区：DragAndDrop 必须配合 Event.current 用 ----
                EditorGUILayout.LabelField("把 Project 里的资源（或 Hierarchy 里的物体）拖到下面的框里：", EditorStyles.miniLabel);
                Rect dropArea = GUILayoutUtility.GetRect(0f, 44f, GUILayout.ExpandWidth(true));
                GUI.Box(dropArea, "拖到这里（DragAndDrop 目标区）");

                Event current = Event.current;
                switch (current.type)
                {
                    case EventType.DragUpdated:
                    case EventType.DragPerform:
                        if (dropArea.Contains(current.mousePosition))
                        {
                            // visualMode 决定光标显示成什么样子（Copy / Move / None...）
                            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                            if (current.type == EventType.DragPerform)
                            {
                                DragAndDrop.AcceptDrag();
                                droppedObjects.Clear();
                                foreach (UnityEngine.Object dropped in DragAndDrop.objectReferences)
                                    droppedObjects.Add(dropped);
                                Debug.Log($"[编辑器案例] 拖入了 {droppedObjects.Count} 个对象");
                            }
                            current.Use();   // 消费掉事件，防止别的控件也跟着响应
                        }
                        break;

                    case EventType.DragExited:
                        DragAndDrop.visualMode = DragAndDropVisualMode.None;
                        Repaint();
                        break;
                }

                EditorGUILayout.LabelField($"已接收 {droppedObjects.Count} 个对象：", EditorStyles.miniLabel);
                droppedScroll = EditorGUILayout.BeginScrollView(droppedScroll, GUILayout.Height(60f));
                foreach (UnityEngine.Object dropped in droppedObjects)
                    EditorGUILayout.ObjectField(dropped, typeof(UnityEngine.Object), false);
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region 6 持久化 EditorPrefs

        private void DrawPrefsSection()
        {
            openPrefs = EditorGUILayout.BeginFoldoutHeaderGroup(openPrefs, "6. 持久化（EditorPrefs）");
            if (openPrefs)
            {
                EditorGUILayout.HelpBox(
                    "EditorPrefs 存的是「编辑器级」的小设置（键值对，跟在注册表 / plist 里），适合记工具自己的开关。\n"
                    + "     EditorPrefs.SetInt / GetInt / HasKey / DeleteKey\n"
                    + "     EditorPrefs.SetString / SetFloat / SetBool ...（bool 其实是用 int 存的）\n"
                    + "注意：它不跟着项目走（换电脑就没了），需要跟项目走就写 ScriptableObject 或 json 文件。\n"
                    + "慎用 EditorPrefs.DeleteAll() —— 会把整个 Unity 的偏好设置（包括窗口布局）都清掉。",
                    MessageType.Info, true);

                EditorGUI.BeginChangeCheck();
                prefsInt = EditorGUILayout.IntField("示例 Int（改完自动存）", prefsInt);
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetInt(PrefsIntKey, prefsInt);

                EditorGUILayout.LabelField($"键：{PrefsIntKey}　存在：{EditorPrefs.HasKey(PrefsIntKey)}　存的值：{EditorPrefs.GetInt(PrefsIntKey, -1)}");

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("重新读取", GUILayout.Width(120f)))
                    prefsInt = EditorPrefs.GetInt(PrefsIntKey, 1);
                if (GUILayout.Button("删除这个键", GUILayout.Width(140f)))
                    EditorPrefs.DeleteKey(PrefsIntKey);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(4f);

                // 下面两个开关存进 EditorPrefs，交给本文件底部的 InitializeOnLoadMethod / AssetModificationProcessor 读
                DrawPrefsToggle("编辑器加载 / 重编译后打一行日志（看 Console，然后 Ctrl+R 重载试试）", LogOnLoadKey);
                DrawPrefsToggle("保存资产时打一行日志（Ctrl+S 试试）", LogWillSaveKey);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawPrefsToggle(string label, string key)
        {
            bool value = EditorPrefs.GetBool(key, false);
            bool newValue = EditorGUILayout.Toggle(label, value);
            if (newValue != value)
                EditorPrefs.SetBool(key, newValue);
        }

        #endregion

        #region 7 回调

        private void DrawCallbackSection()
        {
            openCallback = EditorGUILayout.BeginFoldoutHeaderGroup(openCallback, "7. 回调（EditorApplication / 场景视图 Handles）");
            if (openCallback)
            {
                EditorGUILayout.HelpBox(
                    "常用回调（都要在 OnEnable 订阅、OnDisable 取消订阅）：\n"
                    + "     EditorApplication.update                      ≈ 每秒 100 次，做轮询 / 定时刷新\n"
                    + "     EditorApplication.delayCall                   当前帧 UI 处理完后执行一次（不用协程也能「延后一帧」）\n"
                    + "     EditorApplication.hierarchyWindowItemOnGUI     往 Hierarchy 里画东西\n"
                    + "     SceneView.duringSceneGui                       往场景视图里画东西（配合 Handles）\n"
                    + "     EditorApplication.playModeStateChanged         进出播放模式时做事\n\n"
                    + "改场景里的对象要用 Undo.RecordObject + SetDirty，不然改了不存盘、也撤销不了。",
                    MessageType.Info, true);

                trackEditorTime = EditorGUILayout.Toggle("开启 update 定时刷新（显示运行时长）", trackEditorTime);
                if (trackEditorTime)
                    EditorGUILayout.LabelField($"编辑器已运行：{EditorApplication.timeSinceStartup:F1} 秒　正在编译：{EditorApplication.isCompiling}　正在播放：{EditorApplication.isPlaying}");

                if (GUILayout.Button("EditorApplication.delayCall：延后一帧执行一次", GUILayout.Width(320f)))
                {
                    EditorApplication.delayCall += () => Debug.Log("[编辑器案例] delayCall：在当前帧处理完之后执行了一次");
                }

                colorHierarchyNames = EditorGUILayout.Toggle("在 Hierarchy 每一行右侧画一个绿点", colorHierarchyNames);
                drawSceneHandles = EditorGUILayout.Toggle("在场景视图里给选中物体画手柄（可拖动，可撤销）", drawSceneHandles);
                if (GUILayout.Button("SceneView.RepaintAll()：立刻刷新场景视图", GUILayout.Width(320f)))
                    SceneView.RepaintAll();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>EditorApplication.update：约每秒 100 次，这里只用来做"降频刷新"（避免每帧 Repaint 拖慢编辑器）。</summary>
        private void OnEditorUpdate()
        {
            if (!trackEditorTime)
                return;

            if (EditorApplication.timeSinceStartup - lastRefreshTime > 0.2)
            {
                lastRefreshTime = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        /// <summary>EditorApplication.hierarchyWindowItemOnGUI：往 Hierarchy 每行里画东西。</summary>
        private void OnHierarchyItemGUI(int instanceID, Rect selectionRect)
        {
            if (!colorHierarchyNames)
                return;

            Rect dot = new Rect(selectionRect.xMax - 14f, selectionRect.y + 4f, 6f, 6f);
            EditorGUI.DrawRect(dot, new Color(0.25f, 0.9f, 0.4f, 0.9f));
        }

        /// <summary>SceneView.duringSceneGui：往场景视图里画手柄（Handles）。</summary>
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!drawSceneHandles)
                return;

            Transform target = Selection.activeTransform;
            if (target == null)
            {
                Handles.Label(Vector3.zero, "场景手柄案例：先在 Hierarchy 里选中一个物体");
                return;
            }

            Handles.color = Color.cyan;
            Handles.Label(target.position + Vector3.up * 0.5f, $"选中：{target.name}");

            // BeginChangeCheck/EndChangeCheck 包住手柄：只有真的拖动了才写回
            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(target.position, target.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(target, "移动物体（场景手柄案例）");
                target.position = newPosition;
                EditorUtility.SetDirty(target);
            }
        }

        #endregion

        #region 8 弹窗与通知

        private void DrawDialogSection()
        {
            openDialog = EditorGUILayout.BeginFoldoutHeaderGroup(openDialog, "8. 弹窗与通知");
            if (openDialog)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("DisplayDialog（两个按钮）", GUILayout.Width(200f)))
                {
                    bool ok = EditorUtility.DisplayDialog("示例弹窗", "这是一条消息，点确定返回 true。", "确定", "取消");
                    Debug.Log($"[编辑器案例] DisplayDialog 返回：{ok}");
                }
                if (GUILayout.Button("DisplayDialogComplex（三个按钮）", GUILayout.Width(230f)))
                {
                    int choice = EditorUtility.DisplayDialogComplex("示例弹窗", "选一个：", "保存", "取消", "不保存");
                    Debug.Log($"[编辑器案例] DisplayDialogComplex 返回：{choice}");
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("窗口内通知（ShowNotification）", GUILayout.Width(230f)))
                    ShowNotification(new GUIContent("这是一条窗口内通知，几秒后自动消失"));
                if (GUILayout.Button("打开 Project 窗口（FocusProjectWindow）", GUILayout.Width(260f)))
                    EditorUtility.FocusProjectWindow();
                if (GUILayout.Button("在资源管理器里打开项目路径", GUILayout.Width(230f)))
                    EditorUtility.RevealInFinder(Application.dataPath);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("复制一段文本到系统剪贴板", GUILayout.Width(230f)))
                    GUIUtility.systemCopyBuffer = "由 UPandaGF 编辑器案例复制的文本";
                if (GUILayout.Button("读剪贴板（看 Console）", GUILayout.Width(200f)))
                    Debug.Log($"[编辑器案例] 剪贴板内容：{GUIUtility.systemCopyBuffer}");
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        #endregion

        #region 9 环境信息

        private void DrawInfoSection()
        {
            openInfo = EditorGUILayout.BeginFoldoutHeaderGroup(openInfo, "9. 环境信息（版本 / 平台 / 路径）");
            if (openInfo)
            {
                EditorGUILayout.LabelField($"Unity 版本：{Application.unityVersion}　平台：{Application.platform}");
                EditorGUILayout.LabelField($"编辑器状态：isPlaying={EditorApplication.isPlaying}　isPaused={EditorApplication.isPaused}　isCompiling={EditorApplication.isCompiling}");
                EditorGUILayout.LabelField($"皮肤：{(EditorGUIUtility.isProSkin ? "专业版（深色）" : "浅色")}　界面缩放：{EditorGUIUtility.pixelsPerPoint}");

                DrawPathField("Application.dataPath", Application.dataPath);
                DrawPathField("Application.persistentDataPath", Application.persistentDataPath);
                DrawPathField("Application.streamingAssetsPath", Application.streamingAssetsPath);
                DrawPathField("Application.temporaryCachePath", Application.temporaryCachePath);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private static void DrawPathField(string label, string path)
        {
            EditorGUILayout.LabelField(label, EditorStyles.miniLabel);
            EditorGUILayout.SelectableLabel(path, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        #endregion

        #region Assets 右键菜单 + validate（不在窗口里，但可以点窗口按钮调用）

        /// <summary>
        /// Assets 右键菜单。下面那个同路径 + true 的方法是它的 validate 函数：
        /// 返回 false 时菜单项会变灰（这里要求 Project 里至少选中一个东西）。
        /// </summary>
        [MenuItem("Assets/UPandaGF/打印选中资源信息", priority = 1000)]
        private static void PrintSelectionInfo()
        {
            UnityEngine.Object[] selected = Selection.objects;
            if (selected.Length == 0)
            {
                Debug.Log("[编辑器案例] 没有选中任何资源");
                return;
            }

            foreach (UnityEngine.Object item in selected)
            {
                string path = AssetDatabase.GetAssetPath(item);
                Debug.Log($"[编辑器案例] {item.name}\n    类型：{item.GetType().Name}\n"
                          + $"    路径：{path}\n    GUID：{AssetDatabase.AssetPathToGUID(path)}");
            }
        }

        [MenuItem("Assets/UPandaGF/打印选中资源信息", true)]
        private static bool ValidatePrintSelectionInfo()
        {
            return Selection.objects.Length > 0;
        }

        #endregion
    }

    #region GameObject 菜单 + Undo（不在窗口里）

    /// <summary>
    /// Hierarchy 右键菜单 + Undo：菜单里做"创建/删除物体"这类操作，一定要用 Undo.RegisterXXX，
    /// 否则用户 Ctrl+Z 撤不回来。
    /// </summary>
    internal static class EditorExampleGameObjectMenu
    {
        /// <summary>菜单项必须 static；逻辑用 internal 放开，方便窗口里的按钮也能调用同一套代码。</summary>
        [MenuItem("GameObject/UPandaGF/创建带配置的空物体", false, 10)]
        internal static void CreateConfiguredObject()
        {
            GameObject created = new GameObject("ConfiguredObject");

            // 关键：把"创建"注册进 Undo 栈，Ctrl+Z 才能删掉它
            Undo.RegisterCreatedObjectUndo(created, "创建带配置的空物体");

            Selection.activeGameObject = created;
            EditorGUIUtility.PingObject(created);
            Debug.Log("[编辑器案例] 已创建 ConfiguredObject（可 Ctrl+Z 撤销）");
        }
    }

    #endregion

    #region [InitializeOnLoadMethod]（不在窗口里）

    /// <summary>
    /// 编辑器加载 / 脚本重编译后自动执行一次。
    /// 适合做自检、注册全局回调；不要在里面做重活，否则每次改代码都会卡一下。
    /// </summary>
    internal static class EditorExampleAutoRun
    {
        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            if (EditorPrefs.GetBool("UPandaGF.EditorExample.LogOnLoad", false))
                Debug.Log("[编辑器案例] [InitializeOnLoadMethod] 执行了一次（编辑器加载 / 脚本重编译后）");
        }
    }

    #endregion

    #region AssetModificationProcessor（不在窗口里）

    /// <summary>
    /// 资产保存前的钩子：Unity 保存资产前会调用同名的静态方法 OnWillSaveAssets。
    /// 返回的字符串数组 = "允许保存"的路径列表；想在保存前拦截某个文件，把它从这个数组里去掉即可。
    /// </summary>
    internal class EditorExampleAssetProcessor : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            if (EditorPrefs.GetBool("UPandaGF.EditorExample.LogWillSave", false))
                Debug.Log($"[编辑器案例] 即将保存 {paths.Length} 个资产：{string.Join("、", paths)}");

            return paths;   // 原样返回 = 全部放行
        }
    }

    #endregion

    #region 子窗口（演示 ShowUtility / ShowPopup / 生命周期）

    /// <summary>
    /// 一个最小的窗口，专门用来演示"窗口打开方式"和生命周期消息的顺序。
    /// 没有菜单项 —— 从编辑器开发案例窗口里打开它。
    /// </summary>
    public class EditorExampleMiniWindow : EditorWindow
    {
        private int repaintCount;

        private void OnEnable()
        {
            Debug.Log("[子窗口] OnEnable：创建 / 域重载后");
        }

        private void OnFocus()
        {
            Debug.Log("[子窗口] OnFocus：拿到焦点");
        }

        private void OnLostFocus()
        {
            Debug.Log("[子窗口] OnLostFocus：失去焦点");
        }

        private void OnDestroy()
        {
            Debug.Log("[子窗口] OnDestroy：被关闭");
        }

        private void OnGUI()
        {
            repaintCount++;

            EditorGUILayout.LabelField("这是一个最小窗口（EditorExampleMiniWindow）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"OnGUI 执行次数：{repaintCount}");
            EditorGUILayout.LabelField($"位置 / 大小：{position}");
            EditorGUILayout.HelpBox(
                "看 Console 里的 [子窗口] 日志，就能知道打开 / 聚焦 / 关闭分别触发了哪个消息。\n"
                + "用「编辑器开发案例」窗口里的 ShowUtility / ShowPopup 按钮打开它，可以看到两种不同的打开方式。",
                MessageType.None);
        }
    }

    #endregion
}
