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

/// <summary>
/// AssetBundle分类窗口
/// </summary>
public class AssetBundleClassificationWindow : EditorWindow
{
    //[MenuItem("UPandaGF/Tools/AssetBundeCheck")]
    public static void ShowWindow()
    {
        AssetBundleClassificationWindow window = GetWindow<AssetBundleClassificationWindow>();
        window.titleContent = new GUIContent("打包窗口");
        window.minSize = new Vector2(500, 400);
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
    private float dependenciesColumnWidth = 150f;
    private float lastClickTime = 0;
    private float doubleClickTime = 0.3f;
    private int sortColumn = 0; // 0: 名称, 1: 大小, 2: 依赖数
    private bool sortAscending = true;

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
    }

    //private void OnDestroy()
    //{
    //}

    private void OnGUI()
    {
        DrawToolbar();
        DrawHeaders();
        DrawAssetBundleList();

        if (showDetails && selectedBundle != null)
        {
            DrawDetailsPanel();
        }
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
        if (GUILayout.Button("构建 AssetBundle", EditorStyles.toolbarButton, GUILayout.Width(120)))
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

        // 大小列
        if (DrawSortableHeader("大小", 1, sizeColumnWidth))
        {
            SortAssetBundles(1);
        }

        // 依赖列
        if (DrawSortableHeader("依赖数量", 2, dependenciesColumnWidth))
        {
            SortAssetBundles(2);
        }

        // 路径列
        GUILayout.Label("加载方式", EditorStyles.toolbarButton, GUILayout.MinWidth(200));

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

    #region 资源关联数据
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
        Debug.Log("资源关联数据已保存至：" + SAVE_PATH + assetRefName + assetRefextension);

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
                Debug.Log($"{item.packageName}:{item.loodPath}");
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
            GUI.backgroundColor = new Color(0.9f, 0.9f, 0.9f, 0.5f);
        }

        GUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUI.backgroundColor = originalColor;

        // 选中状态
        bool isSelected = selectedBundle == bundleInfo;
        if (isSelected)
        {
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f, 0.3f);
        }

        // 名称
        if (GUILayout.Button(bundleInfo.name, GetRowStyle(),
            GUILayout.Width(nameColumnWidth), GUILayout.MinWidth(nameColumnWidth)))
        {
            HandleBundleClick(bundleInfo);
        }

        // 大小
        GUILayout.Label(FormatFileSize(bundleInfo.size), GetRowStyle(),
            GUILayout.Width(sizeColumnWidth), GUILayout.MinWidth(sizeColumnWidth));

        // 依赖数量
        GUILayout.Label(bundleInfo.dependencies.Count.ToString(), GetRowStyle(),
            GUILayout.Width(dependenciesColumnWidth), GUILayout.MinWidth(dependenciesColumnWidth));

        // 路径
        //GUILayout.Label(bundleInfo.loadPath.ToString(), GetRowStyle(), GUILayout.MinWidth(200));
        bundleInfo.loadPath = (ABLoadPath)EditorGUILayout.EnumPopup(bundleInfo.loadPath, GUILayout.MinWidth(200));
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
            Object obj = AssetDatabase.LoadAssetAtPath<Object>(bundleInfo.path);
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

    private void DrawDetailsPanel()
    {
        GUILayout.Space(5);
        EditorGUILayout.LabelField("详细信息", EditorStyles.boldLabel);

        GUILayout.BeginVertical(EditorStyles.helpBox);

        // 基本信息
        EditorGUILayout.LabelField("名称:", selectedBundle.name);
        EditorGUILayout.LabelField("大小:", FormatFileSize(selectedBundle.size));
        EditorGUILayout.LabelField("路径:", selectedBundle.path);
        EditorGUILayout.LabelField("加载方式:", selectedBundle.loadPath.ToString());

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
                    Object obj = AssetDatabase.LoadAssetAtPath<Object>(asset);
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
        // 这里需要根据你的构建输出路径进行调整
        string[] possiblePaths =
        {
            Path.Combine(Application.streamingAssetsPath, bundleName),
            Path.Combine(Application.dataPath, "../AssetBundles", bundleName),
            Path.Combine(System.Environment.CurrentDirectory, "AssetBundles", bundleName)
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
}
