using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UPandaGF;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine.Events;
using System.Threading.Tasks;
using AssetBundleBrowser;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using System;
using UnityEditor.Compilation;
using System.Threading;

/// <summary>
/// AssetBundle分类窗口
/// </summary>
public class AssetBundleClassificationWindow : EditorWindow
{
    public static void ShowWindow()
    {
        AssetBundleClassificationWindow window = GetWindow<AssetBundleClassificationWindow>();
        window.titleContent = new GUIContent("打包窗口");
        window.position = new Rect(100, 100, 550, 600);//设置初始位置和大小
        window.Show();
    }

    private ABSourcesRelated sourcesRelated;
    /// <summary>
    /// key是包名
    /// </summary>
    public Dictionary<string, ABLoadPath> sourcesDic = new Dictionary<string, ABLoadPath>();

    private List<AssetBundleInfo> assetBundleInfos = new List<AssetBundleInfo>();
    private Vector2 scrollPosition;
    private string searchFilter = "";
    private bool showDetails = false;
    private AssetBundleInfo selectedBundle = null;

    // 列宽
    private float nameColumnWidth = 150f;
    private float sizeColumnWidth = 100f;
    private float dependenciesColumnWidth = 100f;
    private float pathColumnWidth = 200f;
    private float lastClickTime = 0;
    private float doubleClickTime = 0.3f;
    private int sortColumn = 0; // 0: 名称, 1: 大小, 2: 依赖数
    private bool sortAscending = true;

    // 添加构建状态变量
    private bool isBuildingSingle = false;
    private string currentBuildingBundle = "";
    private float buildProgress = 0f;

    private class AssetBundleInfo
    {
        public string name;
        public long size;
        public ABLoadPath loadPath;
        public List<string> assets = new List<string>();
        public List<string> dependencies = new List<string>();
        public string path;
        public bool isVariant = false;
        public string variant = "";
    }

    private async void OnEnable()
    {
        await GetABSourcesRelated();
        RefreshAssetBundleList();

        // 监听Editor更新，用于显示构建进度
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        // 更新构建进度显示
        if (isBuildingSingle)
        {
            Repaint();
        }
    }

    //private void OnDestroy()
    //{
    //}

    private void OnGUI()
    {
        // 显示构建进度
        if (isBuildingSingle)
        {
            DrawBuildProgress();
        }

        DrawToolbar();
        DrawHeaders();
        DrawAssetBundleList();

        if (showDetails && selectedBundle != null)
        {
            DrawDetailsPanel();
        }
    }

    // 显示构建进度
    private void DrawBuildProgress()
    {
        Rect progressRect = new Rect(0, 0, position.width, 20);
        EditorGUI.ProgressBar(progressRect, buildProgress, $"正在构建: {currentBuildingBundle}");
    }

    // 创建纯色纹理
    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++)
            pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }


    private void DrawToolbar()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            RefreshAssetBundleList();
        }
        GUILayout.Space(10);
        GUILayout.Label("搜索:", GUILayout.Width(40));
        string newSearch = GUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));
        if (newSearch != searchFilter)
        {
            searchFilter = newSearch;
        }

        GUILayout.FlexibleSpace();
        if (GUILayout.Button("更新配置", EditorStyles.toolbarButton, GUILayout.Width(120)))
        {
            GenerateAssetBundleInfo();
        }

        //if (GUILayout.Button("构建 AssetBundle",  EditorStyles.toolbarButton, GUILayout.Width(120)))
        if (EditorGUIExtensions.ColorButton("Build All", Color.green, EditorStyles.toolbarButton, GUILayout.Width(120)))
        {
            GenerateAssetBundleInfo();
            EditorApplication.delayCall += AssetBundleBrowserMain.instance.m_BuildTab.ExecuteBuild;
        }
        GUILayout.EndHorizontal();
    }

    private void DrawHeaders()
    {
        GUILayout.BeginHorizontal(EditorStyles.toolbar);

        // 名称列
        if (DrawSortableHeader("包名", 0, nameColumnWidth))
        {
            SortAssetBundles(0);
        }
        // 依赖列
        if (DrawSortableHeader("依赖数量", 1, dependenciesColumnWidth))
        {
            SortAssetBundles(1);
        }

        // 大小列
        if (DrawSortableHeader("大小", 2, sizeColumnWidth))
        {
            SortAssetBundles(2);
        }

        // 路径列
        if (GUILayout.Button(new GUIContent("加载配置"), EditorStyles.toolbarButton,
            GUILayout.Width(pathColumnWidth), GUILayout.MinWidth(pathColumnWidth)))
        {
            EditorUtility.DisplayDialog("提示",
                 $"SreamingAssets : 资源从SreamingAssets路径加载.\n\n" +
                 $"PersistentDataPath : 资源从PersistentDataPath路径加载，该资源需要先下载到本地，主要配合热更新使用.\n\n" +
                 $"RemotePath : 资源直接从远程路径加载", 
                 "确定");
        }

        GUILayout.EndHorizontal();
    }

    private bool DrawSortableHeader(string label, int columnIndex, float width)
    {
        GUIContent content = new GUIContent(label);

        if (sortColumn == columnIndex)
        {
            content.text += sortAscending ? " ↑" : " ↓";
        }

        return GUILayout.Button(content, EditorStyles.toolbarButton,
            GUILayout.Width(width), GUILayout.MinWidth(width));
    }

    #region 资源引用数据
    /// <summary>
    /// 资源数据存储的位置
    /// </summary>
    private static string assetRefSavePath = "/Data/";
    /// <summary>
    /// 资源名
    /// </summary>
    private static string assetRefName = "assetData";

    /// <summary>
    /// 资源数据存储文件后缀
    /// </summary>
    private static string assetRefextension = ".assetref";
    /// <summary>
    /// 遍历项目目录，生成AB资源关联数据
    /// </summary>
    public void GenerateAssetBundleInfo()
    {
        // 获取所有的资源路径
        string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();
        ABSourcesRelated aBSourcesRef = new ABSourcesRelated();
        foreach (string assetPath in allAssetPaths)
        {
            // 排除非资源文件
            if (assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Assets/Plugins") && !assetPath.EndsWith(".cs"))
            {
                // 获取该资源的 AssetBundle 名字
                string assetBundleName = AssetDatabase.GetImplicitAssetBundleName(assetPath);

                // 如果资源没有被分配到 AssetBundle，则跳过
                if (string.IsNullOrEmpty(assetBundleName)) continue;

                // 获取资源的名字
                string assetName = Path.GetFileNameWithoutExtension(assetPath);
                // 创建一个 AssetInfo 对象，并添加到列表中
                ABLoadPath arg = sourcesDic[assetBundleName];
                ABRelatedArg assetInfo = new ABRelatedArg(assetBundleName, assetName, arg);
                aBSourcesRef.sourcesDic.Add(assetPath, assetInfo);
            }
        }

        Save(aBSourcesRef);
        AssetDatabase.Refresh();
    }

    public static void Save(object obj)
    {
        string SAVE_PATH = Application.streamingAssetsPath + assetRefSavePath;
        //先判断路径文件夹有没有
        if (!Directory.Exists(SAVE_PATH))
        {
            Directory.CreateDirectory(SAVE_PATH);
        }

        //可以对数据再做些操作，比如进行加密
        using (MemoryStream ms = new MemoryStream())
        {
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(ms, obj);

            byte[] bytes = ms.GetBuffer();
            //ToDo:..在这里可以做一些加密的工作
            string AESKEY = "111a222aaabbbccc";
            string AESIV = "111b222aaabbbccc";
            bytes = AESEncryption.AESEncrypt(bytes, AESKEY, AESIV);

            File.WriteAllBytes(SAVE_PATH + assetRefName + assetRefextension, bytes);
            ms.Close();
        }
        Debug.Log("资源数据已保存至：" + SAVE_PATH + assetRefName + assetRefextension);

    }

    public async Task GetABSourcesRelated()
    {
        //资源关联数据
        string assetRefpath = assetRefSavePath + assetRefName + assetRefextension;
        byte[] b = null;
        if (StreamingAssetsLoader.CheckFile(assetRefpath))
        {
            b = await StreamingAssetsLoader.LoadBinaryDataAsync(assetRefpath);
        }
        else
        {
            Debug.LogWarning($"资源关联数据未创建！\n{assetRefpath}");
        }

        if (b != null)
        {
            sourcesRelated = LoadABSourcesRelated(b);
        }
        else
        {
            sourcesRelated = new ABSourcesRelated();
        }
        foreach (var item in sourcesRelated.sourcesDic.Values)
        {
            if (!sourcesDic.ContainsKey(item.packageName))
            {
                sourcesDic.Add(item.packageName, item.loodPath);
                //Debug.Log($"{item.packageName}:{item.loodPath}");
            }
        }
    }

    private ABSourcesRelated LoadABSourcesRelated(byte[] bytes)
    {
        ABSourcesRelated obj = null;
        string AESKEY = "111a222aaabbbccc";
        string AESIV = "111b222aaabbbccc";
        bytes = AESEncryption.AESDecrypt(bytes, AESKEY, AESIV);
        using (MemoryStream ms = new MemoryStream(bytes))
        {
            BinaryFormatter bf = new BinaryFormatter();
            obj = bf.Deserialize(ms) as ABSourcesRelated;
            ms.Close();
        }
        return obj;
    }
    #endregion


    private void SortAssetBundles(int column)
    {
        if (sortColumn == column)
        {
            sortAscending = !sortAscending;
        }
        else
        {
            sortColumn = column;
            sortAscending = true;
        }

        switch (column)
        {
            case 0: // 按名称排序
                assetBundleInfos.Sort((a, b) =>
                    sortAscending ? a.name.CompareTo(b.name) : b.name.CompareTo(a.name));
                break;
            case 1: // 按大小排序
                assetBundleInfos.Sort((a, b) =>
                    sortAscending ? a.size.CompareTo(b.size) : b.size.CompareTo(a.size));
                break;
            case 2: // 按依赖数排序
                assetBundleInfos.Sort((a, b) =>
                    sortAscending ? a.dependencies.Count.CompareTo(b.dependencies.Count) :
                                  b.dependencies.Count.CompareTo(a.dependencies.Count));
                break;
        }
    }

    private void DrawAssetBundleList()
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        int index = 0;
        foreach (var bundleInfo in GetFilteredAssetBundles())
        {
            DrawAssetBundleRow(bundleInfo, index);
            index++;
        }

        GUILayout.EndScrollView();
    }

    private IEnumerable<AssetBundleInfo> GetFilteredAssetBundles()
    {
        if (string.IsNullOrEmpty(searchFilter))
        {
            return assetBundleInfos;
        }

        return assetBundleInfos.Where(b =>
            b.name.IndexOf(searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            b.assets.Any(a => a.IndexOf(searchFilter, System.StringComparison.OrdinalIgnoreCase) >= 0));
    }

    private void DrawAssetBundleRow(AssetBundleInfo bundleInfo, int index)
    {
        Color originalColor = GUI.backgroundColor;

        // 交替行背景色
        if (index % 2 == 0)
        {
            GUI.backgroundColor = new Color(0, 0, 0, 0.8f);
        }
        // 选中状态
        bool isSelected = selectedBundle == bundleInfo;
        if (isSelected)
        {
            GUI.backgroundColor = new Color(0f, 1f, 1f, 1f);
        }
        GUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUI.backgroundColor = originalColor;

        // 名称
        Rect nameRect = GUILayoutUtility.GetRect(
            new GUIContent(bundleInfo.name),
            GetRowStyle(),
            GUILayout.Width(nameColumnWidth),
            GUILayout.MinWidth(nameColumnWidth)
        );

        if (GUI.Button(nameRect, bundleInfo.name, GetRowStyle()))
        {
            if (Event.current.button == 0) // 左键
            {
                HandleBundleClick(bundleInfo);
            }
            else if (Event.current.button == 1)
            {
                //Debug.Log("选中");
                selectedBundle = bundleInfo;
                showDetails = true;

                // 显示右键菜单
                ShowNameContextMenu(bundleInfo);
            }
        }

        // 依赖数量
        GUILayout.Label(bundleInfo.dependencies.Count.ToString(), GetRowStyle(),
            GUILayout.Width(dependenciesColumnWidth), GUILayout.MinWidth(dependenciesColumnWidth));

        // 大小
        GUILayout.Label(FormatFileSize(bundleInfo.size), GetRowStyle(),
            GUILayout.Width(sizeColumnWidth), GUILayout.MinWidth(sizeColumnWidth));

        // 路径
        //GUILayout.Label(bundleInfo.loadPath.ToString(), GetRowStyle(), GUILayout.MinWidth(200));
        float lableW = pathColumnWidth - 40;
        lableW = Mathf.Clamp(lableW, 20, pathColumnWidth - 40);
        bundleInfo.loadPath = (ABLoadPath)EditorGUILayout.EnumPopup(bundleInfo.loadPath, GUILayout.Width(lableW), GUILayout.MinWidth(lableW));
        sourcesDic[bundleInfo.name] = bundleInfo.loadPath;
        GUILayout.FlexibleSpace();

        GUILayout.EndHorizontal();

        GUI.backgroundColor = originalColor;
    }

    private GUIStyle GetRowStyle()
    {
        var style = new GUIStyle(GUI.skin.label);
        style.alignment = TextAnchor.MiddleLeft;
        style.padding = new RectOffset(5, 5, 2, 2);
        return style;
    }

    private void HandleBundleClick(AssetBundleInfo bundleInfo)
    {
        float currentTime = (float)EditorApplication.timeSinceStartup;

        if (selectedBundle == bundleInfo && (currentTime - lastClickTime) < doubleClickTime)
        {
            // 双击 - 在Project窗口中定位
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(bundleInfo.path);
            if (obj != null)
            {
                EditorUtility.FocusProjectWindow();
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }
        else
        {
            // 单击 - 选中
            selectedBundle = bundleInfo;
            showDetails = true;
        }

        lastClickTime = currentTime;
    }
    private Vector2 detailsscrollPosition;
    private void DrawDetailsPanel()
    {
        GUILayout.Space(5);
        EditorGUILayout.LabelField("详细信息", EditorStyles.boldLabel);
        GUILayout.BeginVertical(EditorStyles.helpBox);
        detailsscrollPosition = GUILayout.BeginScrollView(detailsscrollPosition, GUILayout.Height(150));
        // 基本信息
        EditorGUILayout.LabelField("名称:", selectedBundle.name);
        EditorGUILayout.LabelField("大小:", FormatFileSize(selectedBundle.size));
        EditorGUILayout.LabelField("加载方式:", selectedBundle.loadPath.ToString());
        EditorGUILayout.LabelField("路径:", selectedBundle.path);

        if (selectedBundle.isVariant)
        {
            EditorGUILayout.LabelField("变体:", selectedBundle.variant);
        }

        GUILayout.Space(10);

        // 包含的资源
        EditorGUILayout.LabelField($"包含的资源 ({selectedBundle.assets.Count}):", EditorStyles.boldLabel);
        if (selectedBundle.assets.Count > 0)
        {
            foreach (var asset in selectedBundle.assets)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                EditorGUILayout.LabelField(asset);

                if (GUILayout.Button("定位", GUILayout.Width(40)))
                {
                    UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(asset);
                    if (obj != null)
                    {
                        EditorUtility.FocusProjectWindow();
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                }

                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(10);

        // 依赖
        EditorGUILayout.LabelField($"依赖 ({selectedBundle.dependencies.Count}):", EditorStyles.boldLabel);
        if (selectedBundle.dependencies.Count > 0)
        {
            foreach (var dependency in selectedBundle.dependencies)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                EditorGUILayout.LabelField(dependency);

                if (GUILayout.Button("定位", GUILayout.Width(40)))
                {
                    var dependencyBundle = assetBundleInfos.Find(b => b.name == dependency);
                    if (dependencyBundle != null)
                    {
                        selectedBundle = dependencyBundle;
                        Repaint();
                    }
                }

                GUILayout.EndHorizontal();
            }
        }
        else
        {
            EditorGUILayout.LabelField("无依赖");
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    public void RefreshAssetBundleList()
    {
        assetBundleInfos.Clear();

        // 获取所有设置了AssetBundle标签的资源
        string[] allAssetBundleNames = AssetDatabase.GetAllAssetBundleNames();
        ABLoadPath anLP = ABLoadPath.StreamingAssetsPath;
        foreach (string bundleName in allAssetBundleNames)
        {
            anLP = sourcesDic.ContainsKey(bundleName) ? sourcesDic[bundleName] : ABLoadPath.StreamingAssetsPath;
            AssetBundleInfo info = new AssetBundleInfo
            {
                name = bundleName,
                assets = new List<string>(),
                loadPath = anLP
            };
            // 获取该AssetBundle中的所有资源路径
            string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            info.assets.AddRange(assetPaths);

            // 获取该AssetBundle的依赖
            string[] dependencies = AssetDatabase.GetAssetBundleDependencies(bundleName, true);
            info.dependencies.AddRange(dependencies);

            // 获取文件大小（如果已构建）
            string buildPath = GetBuildPathForBundle(bundleName);
            if (File.Exists(buildPath))
            {
                FileInfo fileInfo = new FileInfo(buildPath);
                info.size = fileInfo.Length;
                info.path = buildPath;
            }
            else
            {
                // 如果没有构建，估计大小
                info.size = EstimateBundleSize(assetPaths);
                info.path = "未构建";
            }

            // 检查是否是变体
            int variantIndex = bundleName.IndexOf('.');
            if (variantIndex > 0)
            {
                info.isVariant = true;
                info.variant = bundleName.Substring(variantIndex + 1);
            }

            assetBundleInfos.Add(info);
        }

        // 初始排序
        SortAssetBundles(0);
    }

    private string GetBuildPathForBundle(string bundleName)
    {
        AssetBundleBrowserMain assetBundleBrowserMain = AssetBundleBrowserMain.instance;
        if (assetBundleBrowserMain == null) return "";

        string m_OutputPath = assetBundleBrowserMain.m_BuildTabData.m_OutputPath;
        if (string.IsNullOrEmpty(m_OutputPath)) return "";

        string result = "AssetBundles/";
        int startIndex = m_OutputPath.IndexOf("AssetBundles/");
        if (startIndex != -1)
        {
            result = m_OutputPath.Substring(startIndex);
        }
        // 这里需要根据你的构建输出路径进行调整
        string[] possiblePaths =
        {
            $"{Application.dataPath}/{result}/{bundleName}",
            $"{Application.streamingAssetsPath}/{result}/{bundleName}",
            $"{System.Environment.CurrentDirectory}/{result}/{bundleName}"
        };

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return "";
    }

    private long EstimateBundleSize(string[] assetPaths)
    {
        long totalSize = 0;
        foreach (string path in assetPaths)
        {
            if (File.Exists(path))
            {
                FileInfo fileInfo = new FileInfo(path);
                totalSize += fileInfo.Length;
            }
        }
        return totalSize;
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        else if (bytes < 1024 * 1024)
        {
            return $"{(bytes / 1024.0):0.0} KB";
        }
        else
        {
            return $"{(bytes / (1024.0 * 1024.0)):0.0} MB";
        }
    }

    //右键菜单方法
    private void ShowNameContextMenu(AssetBundleInfo bundleInfo)
    {
        GenericMenu menu = new GenericMenu();

        // Build菜单项
        menu.AddItem(new GUIContent("Build/Build This Bundle"), false, () =>
        {
            BuildSingleAssetBundle(bundleInfo.name);
        });

        // 2. 构建选中的AssetBundle及其依赖
        menu.AddItem(new GUIContent("Build/Build This Bundle + Dependencies"), false, () =>
        {
            BuildAssetBundleWithDependencies(bundleInfo.name);
        });

        menu.AddSeparator("");

        //定位到文件/在Project中高亮
        if (bundleInfo.assets != null && bundleInfo.assets.Count > 0)
        {
            menu.AddItem(new GUIContent("在Project中高亮"), false, () =>
            {
                if (bundleInfo.assets.Count > 0)
                {
                    // 高亮第一个资源
                    string firstAsset = bundleInfo.assets[0];
                    if (firstAsset != null)
                    {
                        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(firstAsset);
                        if (obj != null)
                        {
                            EditorUtility.FocusProjectWindow();
                            Selection.activeObject = obj;
                            EditorGUIUtility.PingObject(obj);
                        }
                    }
                }
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("在Project中高亮(无资源)"));
        }

        //复制名称
        menu.AddItem(new GUIContent("复制包名"), false, () =>
        {
            EditorGUIUtility.systemCopyBuffer = bundleInfo.name;
        });

        //在资源管理器中显示(如果已构建)
        if (bundleInfo.path != "未构建" && !string.IsNullOrEmpty(bundleInfo.path))
        {
            menu.AddItem(new GUIContent("在资源管理器中显示"), false, () =>
            {
                if (File.Exists(bundleInfo.path))
                {
                    // 在文件管理器中高亮文件
                    EditorUtility.RevealInFinder(bundleInfo.path);
                }
                else
                {
                    Debug.LogWarning($"文件不存在: {bundleInfo.path}");
                }
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("在资源管理器中显示(未构建)"));
        }

        menu.AddSeparator("");

        //复制路径
        menu.AddItem(new GUIContent("复制AssetBundle路径"), false, () =>
        {
            EditorGUIUtility.systemCopyBuffer = bundleInfo.path;
        });

        //复制加载路径枚举
        menu.AddItem(new GUIContent($"复制加载路径:{bundleInfo.loadPath}"), false, () =>
        {
            EditorGUIUtility.systemCopyBuffer = $"ABLoadPath.{bundleInfo.loadPath}";
        });

        menu.ShowAsContext();
    }

    // Build子菜单
    private void ShowBuildSubMenu(AssetBundleInfo bundleInfo)
    {
        GenericMenu subMenu = new GenericMenu();

        // 1. 只构建选中的AssetBundle
        subMenu.AddItem(new GUIContent("Build This Bundle"), false, () =>
        {
            BuildSingleAssetBundle(bundleInfo.name);
        });

        // 2. 构建选中的AssetBundle及其依赖
        subMenu.AddItem(new GUIContent("Build This Bundle + Dependencies"), false, () =>
        {
            BuildAssetBundleWithDependencies(bundleInfo.name);
        });

        // 3. 构建所有AssetBundle
        subMenu.AddItem(new GUIContent("Build All"), false, () =>
        {
            GenerateAssetBundleInfo();
            if (AssetBundleBrowserMain.instance != null)
            {
                EditorApplication.delayCall += AssetBundleBrowserMain.instance.m_BuildTab.ExecuteBuild;
            }
        });

        subMenu.ShowAsContext();
    }

    // 构建单个AssetBundle
    private async void BuildSingleAssetBundle(string bundleName)
    {
        if (isBuildingSingle)
        {
            Debug.LogWarning("正在构建中，请等待完成...");
            return;
        }

        //if (!EditorUtility.DisplayDialog("构建确认",
        //    $"注意：这将只构建指定的AssetBundle:{bundleName}，不包括其依赖。", "确定", "取消"))
        //{
        //    return;
        //}

        try
        {
            isBuildingSingle = true;
            currentBuildingBundle = bundleName;
            buildProgress = 0f;

            Debug.Log($"开始构建单个AssetBundle: {bundleName}");

            // 1. 保存资源关联数据
            GenerateAssetBundleInfo();

            // 2. 获取构建配置
            AssetBundleBuildTab buildTab = AssetBundleBrowserMain.instance?.m_BuildTab;
            if (buildTab == null)
            {
                Debug.LogError("无法获取AssetBundleBrowser的BuildTab");
                return;
            }

            // 3. 获取构建参数
            BuildTarget buildTarget = (BuildTarget)buildTab.M_UserData.m_BuildTarget;
            BuildAssetBundleOptions buildOptions = buildTab.GetOpt();

            // 4. 创建临时构建目标目录
            string outputPath = AssetBundleBrowserMain.instance.m_BuildTabData.m_OutputPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = "AssetBundles/" + buildTarget;
            }

            // 确保输出目录存在
            Directory.CreateDirectory(outputPath);

            // 5. 获取要构建的AssetBundle的所有资源路径
            string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
            if (assetPaths.Length == 0)
            {
                Debug.LogWarning($"没有找到属于AssetBundle '{bundleName}' 的资源");
                return;
            }

            // 6. 创建AssetBundle构建配置
            var builds = new List<AssetBundleBuild>();
            var build = new AssetBundleBuild
            {
                assetBundleName = bundleName,
                // 添加主资源
                assetNames = assetPaths
            };
            builds.Add(build);

            // 7. 开始构建
            buildProgress = 0.2f;
            await Task.Run(() => Thread.Sleep(100)); // 让进度条显示

            Debug.Log($"构建AssetBundle: {bundleName}, 包含资源数: {assetPaths.Length}");
            Debug.Log($"输出路径: {outputPath}");

            // 8. 执行构建
            buildProgress = 0.5f;
            var result = BuildPipeline.BuildAssetBundles(outputPath, builds.ToArray(), buildOptions, buildTarget);

            if (result == null)
            {
                Debug.LogError($"构建AssetBundle '{bundleName}' 失败");
                return;
            }

            // 9. 构建完成
            buildProgress = 1.0f;
            await Task.Run(() => Thread.Sleep(500)); // 显示完成状态

            Debug.Log($"AssetBundle '{bundleName}' 构建完成!");
            Debug.Log($"文件大小: {new FileInfo(Path.Combine(outputPath, bundleName)).Length} bytes");

            // 10. 刷新列表
            RefreshAssetBundleList();
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            Debug.LogError($"构建AssetBundle '{bundleName}' 时发生错误: {e.Message}");
            Debug.LogError(e.StackTrace);
        }
        finally
        {
            isBuildingSingle = false;
            currentBuildingBundle = "";
            buildProgress = 0f;
        }
    }

    // 构建AssetBundle及其依赖
    private async void BuildAssetBundleWithDependencies(string bundleName)
    {
        if (isBuildingSingle)
        {
            Debug.LogWarning("正在构建中，请等待完成...");
            return;
        }
        //if (!EditorUtility.DisplayDialog("构建确认",
        //    $"确定要构建 '{bundleName}' 及其所有依赖吗？", "确定", "取消"))
        //{
        //    return;
        //}
        try
        {
            isBuildingSingle = true;
            currentBuildingBundle = bundleName;
            buildProgress = 0f;

            Debug.Log($"开始构建AssetBundle及其依赖: {bundleName}");

            // 1. 保存资源关联数据
            GenerateAssetBundleInfo();

            // 2. 获取构建配置
            AssetBundleBuildTab buildTab = AssetBundleBrowserMain.instance?.m_BuildTab;
            if (buildTab == null)
            {
                Debug.LogError("无法获取AssetBundleBrowser的BuildTab");
                return;
            }

            // 3. 获取要构建的AssetBundle及其依赖
            var bundlesToBuild = new HashSet<string>();
            CollectBundleDependencies(bundleName, bundlesToBuild);

            if (bundlesToBuild.Count == 0)
            {
                Debug.LogWarning("没有找到要构建的AssetBundle");
                return;
            }

            Debug.Log($"将要构建 {bundlesToBuild.Count} 个AssetBundle:");
            foreach (var bundle in bundlesToBuild)
            {
                Debug.Log($"  - {bundle}");
            }

            // 4. 获取构建参数
            // 3. 获取构建参数
            BuildTarget buildTarget = (BuildTarget)buildTab.M_UserData.m_BuildTarget;
            BuildAssetBundleOptions buildOptions = buildTab.GetOpt();

            // 5. 创建临时构建目标目录
            string outputPath = AssetBundleBrowserMain.instance.m_BuildTabData.m_OutputPath;
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = "AssetBundles/" + buildTarget;
            }

            // 确保输出目录存在
            Directory.CreateDirectory(outputPath);

            // 6. 创建AssetBundle构建配置
            var builds = new List<AssetBundleBuild>();

            foreach (var bundle in bundlesToBuild)
            {
                string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundle);
                if (assetPaths.Length > 0)
                {
                    var build = new AssetBundleBuild
                    {
                        assetBundleName = bundle,
                        assetNames = assetPaths
                    };
                    builds.Add(build);
                }
            }

            if (builds.Count == 0)
            {
                Debug.LogWarning("没有找到有效的AssetBundle进行构建");
                return;
            }

            // 7. 开始构建
            buildProgress = 0.2f;
            await Task.Run(() => Thread.Sleep(100)); // 让进度条显示

            Debug.Log($"构建 {builds.Count} 个AssetBundle");
            Debug.Log($"输出路径: {outputPath}");

            // 8. 执行构建
            buildProgress = 0.5f;
            var result = BuildPipeline.BuildAssetBundles(outputPath, builds.ToArray(), buildOptions, buildTarget);

            if (result == null)
            {
                Debug.LogError("构建AssetBundle失败");
                return;
            }

            // 9. 构建完成
            buildProgress = 1.0f;
            await Task.Run(() => Thread.Sleep(500)); // 显示完成状态

            Debug.Log($"AssetBundle '{bundleName}' 及其依赖构建完成!");

            // 10. 刷新列表
            RefreshAssetBundleList();

            AssetDatabase.Refresh();

        }
        catch (Exception e)
        {
            Debug.LogError($"构建AssetBundle '{bundleName}' 时发生错误: {e.Message}");
            Debug.LogError(e.StackTrace);
        }
        finally
        {
            isBuildingSingle = false;
            currentBuildingBundle = "";
            buildProgress = 0f;
        }
    }

    // 递归收集AssetBundle的依赖
    private void CollectBundleDependencies(string bundleName, HashSet<string> bundles)
    {
        if (bundles.Contains(bundleName))
        {
            return;
        }

        bundles.Add(bundleName);

        // 获取直接依赖
        string[] dependencies = AssetDatabase.GetAssetBundleDependencies(bundleName, true);

        foreach (var dependency in dependencies)
        {
            if (!bundles.Contains(dependency))
            {
                CollectBundleDependencies(dependency, bundles);
            }
        }
    }
}

public static class EditorGUIExtensions
{
    public static bool ColorButton(string text, Color color, GUIStyle style = null, params GUILayoutOption[] options)
    {
        var oldColor = GUI.backgroundColor;
        GUI.backgroundColor = color;

        var buttonStyle = style ?? EditorStyles.miniButton;
        bool result = GUILayout.Button(text, buttonStyle, options);

        GUI.backgroundColor = oldColor;
        return result;
    }
}