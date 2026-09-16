using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UPandaGF;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using System;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using UPandaGF.GFEditor;

namespace AssetBundleBrowser
{
    /// <summary>
    /// AssetBundle分类窗口
    /// </summary>
    internal class AssetBundleClassificationWindow
    {
        public AssetBundleBrowserMain _mainBrower;
        [SerializeField]
        private bool assetSettings;
        private string configName = "AssetBundleBuildConfig";
        private AssetBundleClassificationWindowConfig config;
        /// <summary>
        /// key是包名
        /// </summary>
        public Dictionary<string, ABLoadPath> sourcesDic;
        public void ShowWindow(AssetBundleBrowserMain arg)
        {
            _mainBrower = arg;
            OnEnable();
        }
        private ABSourcesRelated sourcesRelated;


        private AssetBundleInfo mainBundleInfo;
        private List<AssetBundleInfo> assetBundleInfos = new List<AssetBundleInfo>();
        private Vector2 scrollPosition;
        private string searchFilter = "";
        private bool showDetails = false;
        private AssetBundleInfo selectedBundle = null;

        // 列宽
        private float nameColumnWidth = 200f;
        private float sizeColumnWidth = 100f;
        private float dependenciesColumnWidth = 60f;
        private float pathColumnWidth = 200f;
        private float lastClickTime = 0;
        private float doubleClickTime = 0.3f;
        private int sortColumn = 0; // 0: 名称, 1: 大小, 2: 依赖数
        private bool sortAscending = true;

        // 添加构建状态变量
        private bool isBuildingSingle = false;
        private string currentBuildingBundle = "";

        // AES 配置一致性检测缓存
        private UPandaGF.UPGameRoot aesSyncGameRoot = null;
        private bool aesSyncSearched = false;

        /// <summary>
        /// Bundle显示列表
        /// </summary>
        private IEnumerable<AssetBundleInfo> ABargs;

        [Serializable]
        private class AssetBundleInfo
        {
            public string name;
            public long size;
            public ABLoadPath loadPath;
            public string md5;
            public List<string> assets = new List<string>();
            public List<string> dependencies = new List<string>();
            public string path;
            /// <summary>
            /// 是否已构建（未构建的包没有真实的大小/MD5，不会写入清单）
            /// </summary>
            public bool isBuilt = false;
            public bool isVariant = false;
            public string variant = "";
        }

        private async void OnEnable()
        {
            //Debug.Log("AssetBundleClassificationWindow OnEnable");
            GetData();
            await GetABSourcesRelated();
            RefreshAssetBundleList();
        }
        private void GetData()
        {
            config = UPandaGFConfig.LoadJsonConfig<AssetBundleClassificationWindowConfig>(configName);
            //获取主包信息
            string m_OutputPath = _mainBrower.m_BuildTabData.m_OutputPath;
            // Unity 用"输出目录名"作为主包（manifest bundle）的文件名：默认路径 AssetBundles/<BuildTarget> 下恰好等于 BuildTarget 名，
            // 但用户 Browse 改成别的目录后主包名就是那个目录名，因此这里不能硬编码 BuildTarget
            string bundleName = GetLastPathSegment(m_OutputPath);
            if (mainBundleInfo == null) mainBundleInfo = new AssetBundleInfo();
            mainBundleInfo.name = bundleName;
            string fullPath = GetBuildPathForBundle(bundleName);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                Debug.Log($"主包未构建：{Path.Combine(m_OutputPath, bundleName)}");
                mainBundleInfo.isBuilt = false;
                mainBundleInfo.size = 0;
                mainBundleInfo.md5 = string.Empty;
                mainBundleInfo.path = "未构建";
            }
            else
            {
                FileInfo fileInfo = new FileInfo(fullPath);
                mainBundleInfo.isBuilt = true;
                mainBundleInfo.size = fileInfo.Length;
                mainBundleInfo.md5 = GetMD5(fullPath);
                mainBundleInfo.path = fullPath;
            }
            mainBundleInfo.loadPath = config.mainBundleLoadPath;
        }

        public void SaveData()
        {
            UPandaGFConfig.SaveJsonConfig(config, configName);
        }

        /// <summary>
        /// 取场景中的 UPGameRoot（用于校验 AES 配置是否与打包端一致）
        /// </summary>
        private static UPandaGF.UPGameRoot FindSceneGameRoot()
        {
            UPandaGF.UPGameRoot[] roots = Resources.FindObjectsOfTypeAll<UPandaGF.UPGameRoot>();
            foreach (UPandaGF.UPGameRoot root in roots)
            {
                if (root == null) continue;
                if (!root.gameObject.scene.IsValid()) continue;   // 过滤工程资源
                if (EditorUtility.IsPersistent(root)) continue;   // 过滤预制体资源
                return root;
            }
            return null;
        }

        /// <summary>
        /// AES 配置有两份（本窗口的 JSON 与场景中 UPGameRoot 的序列化字段），这里做一致性提醒 + 一键同步
        /// </summary>
        private void DrawAESConfigSync()
        {
            if (!aesSyncSearched)
            {
                aesSyncGameRoot = FindSceneGameRoot();
                aesSyncSearched = true;
            }

            GUILayout.BeginHorizontal();
            if (aesSyncGameRoot == null)
            {
                EditorGUILayout.HelpBox("场景中未找到 UPGameRoot。运行时的解密配置来自它，本窗口的 AES 设置不会自动生效。", MessageType.Info);
                if (GUILayout.Button("重新检测", GUILayout.Width(80))) aesSyncSearched = false;
            }
            else
            {
                AssetBundleClassificationWindowConfig sceneConfig = aesSyncGameRoot.Config != null
                    ? aesSyncGameRoot.Config.AssetAESConfig
                    : null;
                bool same = sceneConfig != null
                    && sceneConfig.enable == config.enable
                    && string.Equals(sceneConfig.AESKEY, config.AESKEY)
                    && string.Equals(sceneConfig.AESIV, config.AESIV);

                if (same)
                {
                    EditorGUILayout.HelpBox("场景中 UPGameRoot 的 AES 配置与本窗口一致。", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("场景中 UPGameRoot 的 AES 配置与本窗口不一致：运行时解密会失败并中止 AB 模式初始化，请同步后再打包。", MessageType.Warning);
                }

                if (GUILayout.Button("同步到场景中的 UPGameRoot", GUILayout.Width(220)))
                {
                    Undo.RecordObject(aesSyncGameRoot, "Sync AssetBundle AES Config");
                    if (aesSyncGameRoot.Config.AssetAESConfig == null)
                        aesSyncGameRoot.Config.AssetAESConfig = new AssetBundleClassificationWindowConfig();
                    aesSyncGameRoot.Config.AssetAESConfig.enable = config.enable;
                    aesSyncGameRoot.Config.AssetAESConfig.AESKEY = config.AESKEY;
                    aesSyncGameRoot.Config.AssetAESConfig.AESIV = config.AESIV;
                    EditorUtility.SetDirty(aesSyncGameRoot);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(aesSyncGameRoot.gameObject.scene);
                    Debug.Log("AES 配置已同步到场景中的 UPGameRoot（记得保存场景）");
                }
                if (GUILayout.Button("重新检测", GUILayout.Width(80)))
                {
                    aesSyncSearched = false;
                }
            }
            GUILayout.EndHorizontal();
        }

        public void OnGUI()
        {
            // 构建中提示：BuildPipeline 是同步阻塞调用，拿不到真实进度，这里只做状态提示 + 防重入
            if (isBuildingSingle)
            {
                EditorGUILayout.HelpBox($"正在构建：{currentBuildingBundle}\n构建期间编辑器会阻塞，请等待完成。", MessageType.Info);
            }
            assetSettings = EditorGUILayout.Foldout(assetSettings, "加密设置");
            if (assetSettings)
            {
                EditorGUILayout.LabelField("存储位置", assetRefSavePath);
                EditorGUILayout.LabelField("文件名", assetRefName);
                EditorGUILayout.LabelField("文件后缀", assetRefextension);
                GUILayout.Space(1);
                EditorGUILayout.LabelField("AES配置:");
                config.enable = EditorGUILayout.BeginToggleGroup("使用加密", config.enable);
                config.AESKEY = EditorGUILayout.TextField("Key", config.AESKEY);
                config.AESIV = EditorGUILayout.TextField("IV", config.AESIV);
                EditorGUILayout.EndToggleGroup();
                GUILayout.Space(5);
                DrawAESConfigSync();
            }
            DrawMainBundlAsset();
            DrawToolbar();
            if (ABargs == null || ABargs.Count() == 0)
            {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("AssetBundle is null!!!", EditorStyles.boldLabel);
            }
            else
            {
                DrawHeaders();
                DrawAssetBundleList();
                if (showDetails && selectedBundle != null)
                {
                    DrawDetailsPanel();
                }
            }

            // 统一写回"加载配置"：不依赖每一行都被绘制（被搜索过滤掉的行也能保留用户的选择）
            SyncLoadPathToSourcesDic();
        }

        /// <summary>
        /// 把列表当前的"加载配置"同步到 sourcesDic，作为下次 RefreshAssetBundleList 的初值
        /// </summary>
        private void SyncLoadPathToSourcesDic()
        {
            if (sourcesDic == null) return;
            foreach (AssetBundleInfo bundleInfo in assetBundleInfos)
            {
                sourcesDic[bundleInfo.name] = bundleInfo.loadPath;
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

            //if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
            //{
            //    RefreshAssetBundleList();
            //}
            GUILayout.Space(10);
            string newSearch = GUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));
            if (newSearch != searchFilter)
            {
                searchFilter = newSearch;
            }
            if (GUILayout.Button("搜索", EditorStyles.toolbarButton, GUILayout.Width(40)))
            {
                if (GetFilteredAssetBundles().Count() == 0)
                {
                    searchFilter = "";
                }
                RefreshAssetBundleList();
            }
            //GUILayout.Label("搜索:", GUILayout.Width(40));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("更新配置", EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                GenerateAssetBundleInfo();
            }

            //if (GUILayout.Button("构建 AssetBundle",  EditorStyles.toolbarButton, GUILayout.Width(120)))
            if (ColorButton("Build All", Color.green, EditorStyles.toolbarButton, GUILayout.Width(120)))
            {
                EditorApplication.delayCall += ExecuteBuild;
            }
            GUILayout.EndHorizontal();
        }

        private void ExecuteBuild()
        {
            _mainBrower.m_BuildTab.ExecuteBuild();
            // 刷新列表
            RefreshAssetBundleList();
            GenerateAssetBundleInfo();
        }

        bool ColorButton(string text, Color color, GUIStyle style = null, params GUILayoutOption[] options)
        {
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = color;

            var buttonStyle = style ?? EditorStyles.miniButton;
            bool result = GUILayout.Button(text, buttonStyle, options);

            GUI.backgroundColor = oldColor;
            return result;
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
            if (GUILayout.Button(new GUIContent("加载配置"), EditorStyles.toolbarButton,
                GUILayout.Width(pathColumnWidth), GUILayout.MinWidth(pathColumnWidth)))
            {
                EditorUtility.DisplayDialog("说明：",
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
        /// 资源清单（assetData.assetref）的完整本地路径；上传页签等复用，避免路径重复硬编码
        /// </summary>
        internal static string AssetDataFullPath => Application.streamingAssetsPath + assetRefSavePath + assetRefName + assetRefextension;

        /// <summary>
        /// 遍历项目目录，生成AB资源关联数据
        /// </summary>
        public void GenerateAssetBundleInfo()
        {
            // 获取所有的资源路径
            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths();

            ABSourcesRelated aBSourcesRef = new ABSourcesRelated();
            //主包的数据：
            aBSourcesRef.mainBundleInfo = new AssetBundleLoadInfo()
            {
                bundleName = mainBundleInfo.name,
                size = mainBundleInfo.size,
                md5 = mainBundleInfo.md5,
                loadPath = mainBundleInfo.loadPath
            };
            //AssetBundle数据（只登记已构建的包）
            List<string> unbuiltBundles = new List<string>();
            List<AssetBundleLoadInfo> abLoadInfo = GetAssetBundleInfo(unbuiltBundles);
            foreach (AssetBundleLoadInfo item in abLoadInfo)
            {
                aBSourcesRef.bundleInfo.Add(item.bundleName, item);
            }

            if (unbuiltBundles.Count > 0)
            {
                Debug.LogWarning($"有 {unbuiltBundles.Count} 个 AssetBundle 尚未构建，本次不会写入清单（请先打包后再点「更新配置」）。若这些包之前打包过，控制台可能同时提示「已被移除」，属预期：\n{FormatUnbuiltBundles(unbuiltBundles)}");
            }

            // 主包未构建时清单没有任何可用信息，直接不写，避免用空清单覆盖上一次可用的清单
            if (!mainBundleInfo.isBuilt)
            {
                Debug.LogError($"主包未构建：{mainBundleInfo.path}\n本次不写入 {assetRefName + assetRefextension}，以免覆盖上一次可用的清单。请先执行 Build All 或右键单个包构建。");
                SaveData();
                return;
            }
            //资源加载数据
            foreach (string assetPath in allAssetPaths)
            {
                // 排除非资源文件
                if (assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Assets/Plugins") && !assetPath.EndsWith(".cs"))
                {
                    // 获取该资源的 AssetBundle 名字
                    string assetBundleName = AssetDatabase.GetImplicitAssetBundleName(assetPath);

                    // 如果资源没有被分配到 AssetBundle，则跳过
                    if (string.IsNullOrEmpty(assetBundleName)) continue;
                    string variantName = AssetDatabase.GetImplicitAssetBundleVariantName(assetPath);
                    bool variantNameisNull = string.IsNullOrEmpty(variantName);
                    //Debug.Log($"资源包名字:{assetBundleName}\n变体：{variantName},isNull:{variantNameisNull}");
                    if (!variantNameisNull)
                    {
                        assetBundleName += $".{variantName}";
                    }
                    // 资源所属的包未构建（没写进 bundleInfo）时跳过，
                    // 否则运行时 GetABLoadPath 会因为查不到包信息而抛 KeyNotFoundException
                    if (!aBSourcesRef.bundleInfo.ContainsKey(assetBundleName))
                        continue;
                    // 获取资源的名字
                    string assetName = Path.GetFileNameWithoutExtension(assetPath);
                    AssetRelatedArg assetInfo = new AssetRelatedArg(assetBundleName, assetName);
                    aBSourcesRef.sourcesDic.Add(assetPath, assetInfo);
                }
            }
            CheckDifferences(aBSourcesRef);
            sourcesRelated = aBSourcesRef;

            Save(aBSourcesRef);
            SaveData();
            Debug.Log($"项目共有 {sourcesRelated.sourcesDic.Count} 个资源，{sourcesRelated.bundleInfo.Count} 个已构建的包"
                + (unbuiltBundles.Count > 0 ? $"，跳过 {unbuiltBundles.Count} 个未构建的包" : string.Empty));
            AssetDatabase.Refresh();
        }

        public void Save(object obj)
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
                //string AESKEY = "111a222aaabbbccc";
                //string AESIV = "111b222aaabbbccc";
                if (config.enable)
                    bytes = AESEncryption.AESEncrypt(bytes, config.AESKEY, config.AESIV);

                File.WriteAllBytes(SAVE_PATH + assetRefName + assetRefextension, bytes);
                ms.Close();
            }
            Debug.Log("资源数据已保存至：" + SAVE_PATH + assetRefName + assetRefextension);
        }

        /// <summary>
        /// 检查不同
        /// </summary>
        /// <param name="arg"></param>
        private void CheckDifferences(ABSourcesRelated arg)
        {
            if (sourcesRelated == null) return;
            foreach (AssetBundleLoadInfo item in arg.bundleInfo.Values)
            {
                if (sourcesRelated.bundleInfo.ContainsKey(item.bundleName))
                {
                    AssetBundleLoadInfo old = sourcesRelated.bundleInfo[item.bundleName];
                    if (!old.md5.Equals(item.md5))
                    {
                        Debug.Log($"{item.bundleName}已更改!\nold:{old.md5}\nnew:{item.md5}");
                    }
                }
                else
                {
                    Debug.Log($"新增：{item.bundleName}\nmd5:{item.md5}");
                }
            }

            foreach (AssetBundleLoadInfo item in sourcesRelated.bundleInfo.Values)
            {
                if (!arg.bundleInfo.ContainsKey(item.bundleName))
                {
                    Debug.Log($"{item.bundleName}已被移除！！！");
                }
            }

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
                try
                {
                    sourcesRelated = LoadABSourcesRelated(b);
                }
                catch (Exception)
                {
                    sourcesRelated = new ABSourcesRelated();
                }
            }
            else
            {
                sourcesRelated = new ABSourcesRelated();
            }
            sourcesDic = new Dictionary<string, ABLoadPath>();
            foreach (var item in sourcesRelated.bundleInfo.Values)
            {
                if (!sourcesDic.ContainsKey(item.bundleName))
                {
                    sourcesDic.Add(item.bundleName, item.loadPath);
                    //Debug.Log($"{item.bundleName}:{item.loadPath}");
                }
            }
        }

        private ABSourcesRelated LoadABSourcesRelated(byte[] bytes)
        {
            ABSourcesRelated obj = null;
            if (config.enable)
                bytes = AESEncryption.AESDecrypt(bytes, config.AESKEY, config.AESIV);
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
            foreach (AssetBundleInfo bundleInfo in ABargs)
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
        private void DrawMainBundlAsset()
        {
            AssetBundleInfo bundleInfo = mainBundleInfo;
            // Color originalColor = GUI.backgroundColor;
            GUILayout.BeginHorizontal(EditorStyles.helpBox);
            //GUI.backgroundColor = originalColor;

            GUILayout.Label("主包设置：", GetRowStyle(),
                GUILayout.Width(sizeColumnWidth), GUILayout.MinWidth(sizeColumnWidth));

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
                    //HandleBundleClick(bundleInfo);

                }
                else if (Event.current.button == 1)
                {
                    //Debug.Log("选中");
                    //selectedBundle = bundleInfo;
                    //showDetails = true;

                    // 显示右键菜单
                    //ShowNameContextMenu(bundleInfo);
                }
            }
            // 大小
            GUILayout.Label(FormatFileSize(bundleInfo.size), GetRowStyle(),
                GUILayout.Width(sizeColumnWidth), GUILayout.MinWidth(sizeColumnWidth));

            // 路径
            //GUILayout.Label(bundleInfo.loadPath.ToString(), GetRowStyle(), GUILayout.MinWidth(200));
            float lableW = pathColumnWidth - 40;
            lableW = Mathf.Clamp(lableW, 20, pathColumnWidth - 40);
            config.mainBundleLoadPath = (ABLoadPath)EditorGUILayout.EnumPopup(config.mainBundleLoadPath, GUILayout.Width(lableW), GUILayout.MinWidth(lableW));
            bundleInfo.loadPath = config.mainBundleLoadPath;
            //sourcesDic[bundleInfo.name] = bundleInfo.loadPath;
            GUILayout.FlexibleSpace();

            GUILayout.EndHorizontal();

            //GUI.backgroundColor = originalColor;
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
                    //selectedBundle = bundleInfo;
                    //showDetails = true;

                    // 显示右键菜单
                    ShowNameContextMenu(bundleInfo);
                }
            }
            // 大小
            GUILayout.Label(FormatFileSize(bundleInfo.size), GetRowStyle(),
                GUILayout.Width(sizeColumnWidth), GUILayout.MinWidth(sizeColumnWidth));

            // 依赖数量
            GUILayout.Label(bundleInfo.dependencies.Count.ToString(), GetRowStyle(),
                GUILayout.Width(dependenciesColumnWidth), GUILayout.MinWidth(dependenciesColumnWidth));

            // 路径
            //GUILayout.Label(bundleInfo.loadPath.ToString(), GetRowStyle(), GUILayout.MinWidth(200));
            float lableW = pathColumnWidth - 40;
            lableW = Mathf.Clamp(lableW, 20, pathColumnWidth - 40);
            bundleInfo.loadPath = (ABLoadPath)EditorGUILayout.EnumPopup(bundleInfo.loadPath, GUILayout.Width(lableW), GUILayout.MinWidth(lableW));
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
                // 双击
                selectedBundle = bundleInfo;
                showDetails = true;
            }
            else
            {
                // 单击
                selectedBundle = bundleInfo;
                if (showDetails) showDetails = false;
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
            EditorGUILayout.LabelField("MD5:", selectedBundle.isBuilt ? selectedBundle.md5 : "未构建");

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
                            //Repaint();
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
                    info.isBuilt = true;
                    info.size = fileInfo.Length;
                    info.path = buildPath;
                    info.md5 = GetMD5(fileInfo.FullName);
                }
                else
                {
                    // 未构建：不能写"占位 MD5"，否则热更的 MD5 比对会全部失真；生成清单时会跳过这类包
                    info.isBuilt = false;
                    info.size = 0;//EstimateBundleSize(assetPaths);
                    info.path = "未构建";
                    info.md5 = string.Empty;
                }

                // 检查是否是变体（与 Unity 规则一致：先取最后一个 '/' 之后的短名，再按短名中最后一个 '.' 分割变体）
                string shortName = bundleName;
                int lastSlash = shortName.LastIndexOf('/');
                if (lastSlash >= 0) shortName = shortName.Substring(lastSlash + 1);
                int variantIndex = shortName.LastIndexOf('.');
                if (variantIndex > 0)
                {
                    info.isVariant = true;
                    info.variant = shortName.Substring(variantIndex + 1);
                }

                assetBundleInfos.Add(info);
            }

            // 初始排序
            SortAssetBundles(0);
            showDetails = false;
            ABargs = GetFilteredAssetBundles();
        }

        /// <summary>
        /// 获取资源包信息
        /// </summary>
        /// <returns></returns>
        private List<AssetBundleLoadInfo> GetAssetBundleInfo(List<string> unbuiltBundles)
        {
            List<AssetBundleLoadInfo> abInfo = new List<AssetBundleLoadInfo>();
            foreach (var bundle in assetBundleInfos)
            {
                // 未构建的包不写进清单：它的大小/MD5 没有真实值
                if (!bundle.isBuilt)
                {
                    if (unbuiltBundles != null) unbuiltBundles.Add(bundle.name);
                    continue;
                }

                AssetBundleLoadInfo info = new AssetBundleLoadInfo
                {
                    bundleName = bundle.name,
                    loadPath = bundle.loadPath,
                    size = bundle.size,
                    md5 = bundle.md5
                };
                abInfo.Add(info);
            }
            return abInfo;
        }

        /// <summary>
        /// 未构建包的提示文本（最多列举 10 个）
        /// </summary>
        private static string FormatUnbuiltBundles(List<string> unbuiltBundles)
        {
            int count = Mathf.Min(unbuiltBundles.Count, 10);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                sb.Append("  - ").Append(unbuiltBundles[i]).Append('\n');
            }
            if (unbuiltBundles.Count > count)
                sb.Append("  ...（其余 ").Append(unbuiltBundles.Count - count).Append(" 个省略）\n");
            return sb.ToString();
        }

        /// <summary>
        /// 得到文件的MD5码
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns></returns>
        private string GetMD5(string filePath)
        {
            using (FileStream file = new FileStream(filePath, FileMode.Open))
            {
                //声明一个MD5对象 用于生成MD5码
                MD5 md5 = new MD5CryptoServiceProvider();
                //利用API 得到数据的MD5码 16个字节 数组
                byte[] md5Info = md5.ComputeHash(file);
                //关闭文件流
                file.Close();
                //把16个字节转换为 16进制 拼接成字符串 为了减小md5码的长度
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < md5Info.Length; i++)
                {
                    sb.Append(md5Info[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 取路径最后一级名称（主包名 = 输出目录名，BuildPipeline 用输出目录名命名 manifest bundle）
        /// </summary>
        private static string GetLastPathSegment(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            char[] separators = new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            string trimmed = path.TrimEnd(separators);
            int index = trimmed.LastIndexOfAny(separators);
            return index >= 0 ? trimmed.Substring(index + 1) : trimmed;
        }

        /// <summary>
        /// 查找包文件：输出路径可能是工程内相对路径，或工程内/外的绝对路径
        /// </summary>
        private string GetBuildPathForBundle(string bundleName)
        {
            if (_mainBrower == null || string.IsNullOrEmpty(bundleName)) return string.Empty;

            string m_OutputPath = _mainBrower.m_BuildTabData.m_OutputPath;
            if (string.IsNullOrEmpty(m_OutputPath)) return string.Empty;

            string[] possiblePaths;
            if (Path.IsPathRooted(m_OutputPath))
            {
                possiblePaths = new string[] { Path.Combine(m_OutputPath, bundleName) };
            }
            else
            {
                possiblePaths = new string[]
                {
                    Path.Combine(System.Environment.CurrentDirectory, m_OutputPath, bundleName),  // 工程根
                    Path.Combine(Application.dataPath, m_OutputPath, bundleName),                // 输出目录位于 Assets 下
                    Path.Combine(Application.streamingAssetsPath, m_OutputPath, bundleName),    // 已拷到 StreamingAssets
                };
            }

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            return string.Empty;
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
                if (bytes == 0)
                {
                    return "未构建";
                }
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

            // 构建选中的AssetBundle及其依赖
            menu.AddItem(new GUIContent("Build/Build This Bundle + Dependencies"), false, () =>
            {
                BuildAssetBundleWithDependencies(bundleInfo.name);
            });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("详细信息"), false, () =>
            {
                selectedBundle = bundleInfo;
                showDetails = true;
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
            if (bundleInfo.isBuilt)
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
            if (bundleInfo.isBuilt)
            {
                menu.AddItem(new GUIContent("复制MD5码"), false, () =>
                {
                    EditorGUIUtility.systemCopyBuffer = bundleInfo.md5;
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("复制MD5码(未构建)"));
            }
            //复制加载路径枚举
            menu.AddItem(new GUIContent($"复制加载路径:{bundleInfo.loadPath}"), false, () =>
            {
                EditorGUIUtility.systemCopyBuffer = $"ABLoadPath.{bundleInfo.loadPath}";
            });

            menu.ShowAsContext();
        }


        // 构建单个AssetBundle
        private void BuildSingleAssetBundle(string bundleName)
        {
            // 1. 记录开始时间（Ticks为100纳秒单位）
            long startTicks = System.DateTime.UtcNow.Ticks;

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

                Debug.Log($"开始构建单个AssetBundle: {bundleName}");
                // 获取构建配置
                AssetBundleBuildTab buildTab = _mainBrower.m_BuildTab;
                if (buildTab == null)
                {
                    Debug.LogError("无法获取AssetBundleBrowser的BuildTab");
                    return;
                }

                // 获取构建参数
                BuildTarget buildTarget = (BuildTarget)buildTab.M_UserData.m_BuildTarget;
                BuildAssetBundleOptions buildOptions = buildTab.GetOpt();

                // 创建临时构建目标目录
                string outputPath = _mainBrower.m_BuildTabData.m_OutputPath;
                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = "AssetBundles/" + buildTarget;
                }

                // 确保输出目录存在
                Directory.CreateDirectory(outputPath);

                // 获取要构建的AssetBundle的所有资源路径
                string[] assetPaths = AssetDatabase.GetAssetPathsFromAssetBundle(bundleName);
                if (assetPaths.Length == 0)
                {
                    Debug.LogWarning($"没有找到属于AssetBundle '{bundleName}' 的资源");
                    return;
                }

                // 创建AssetBundle构建配置
                var builds = new List<AssetBundleBuild>();
                var build = new AssetBundleBuild
                {
                    assetBundleName = bundleName,
                    // 添加主资源
                    assetNames = assetPaths
                };
                builds.Add(build);

                Debug.Log($"构建AssetBundle: {bundleName}, 包含资源数: {assetPaths.Length}");
                Debug.Log($"输出路径: {outputPath}");

                // 执行构建（BuildPipeline 是同步阻塞调用，期间无法刷新界面）
                var result = BuildPipeline.BuildAssetBundles(outputPath, builds.ToArray(), buildOptions, buildTarget);

                if (result == null)
                {
                    Debug.LogError($"构建AssetBundle '{bundleName}' 失败");
                    return;
                }

                Debug.Log($"AssetBundle '{bundleName}' 构建完成!");
                Debug.Log($"文件大小: {new FileInfo(Path.Combine(outputPath, bundleName)).Length} bytes");

                // 刷新列表
                RefreshAssetBundleList();
                // 保存资源关联数据
                GenerateAssetBundleInfo();
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
            }

            // 2. 计算耗时并输出（转换为毫秒）
            long endTicks = System.DateTime.UtcNow.Ticks;
            double durationMs = (endTicks - startTicks) / 10000.0; // 1 Tick = 100纳秒 → 1毫秒 = 10000 Ticks 1毫秒等于1,000,000纳秒
            UnityEngine.Debug.Log($"打包执行耗时：{(durationMs / 1000):F2} 秒");
        }

        // 构建AssetBundle及其依赖
        private void BuildAssetBundleWithDependencies(string bundleName)
        {
            // 1. 记录开始时间（Ticks为100纳秒单位）
            long startTicks = System.DateTime.UtcNow.Ticks;

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

                Debug.Log($"开始构建AssetBundle及其依赖: {bundleName}");

                //获取构建配置
                AssetBundleBuildTab buildTab = _mainBrower.m_BuildTab;
                if (buildTab == null)
                {
                    Debug.LogError("无法获取AssetBundleBrowser的BuildTab");
                    return;
                }

                //获取要构建的AssetBundle及其依赖
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

                //获取构建参数
                BuildTarget buildTarget = (BuildTarget)buildTab.M_UserData.m_BuildTarget;
                BuildAssetBundleOptions buildOptions = buildTab.GetOpt();

                //创建临时构建目标目录
                string outputPath = _mainBrower.m_BuildTabData.m_OutputPath;
                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = "AssetBundles/" + buildTarget;
                }

                //确保输出目录存在
                Directory.CreateDirectory(outputPath);

                //创建AssetBundle构建配置
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

                Debug.Log($"构建 {builds.Count} 个AssetBundle");
                Debug.Log($"输出路径: {outputPath}");

                //执行构建（同步阻塞调用，期间无法刷新界面）
                var result = BuildPipeline.BuildAssetBundles(outputPath, builds.ToArray(), buildOptions, buildTarget);

                if (result == null)
                {
                    Debug.LogError("构建AssetBundle失败");
                    return;
                }

                Debug.Log($"AssetBundle '{bundleName}' 及其依赖构建完成!");
                //刷新列表
                RefreshAssetBundleList();
                //保存资源关联数据
                GenerateAssetBundleInfo();
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
            }
            // 2. 计算耗时并输出（转换为毫秒）
            long endTicks = System.DateTime.UtcNow.Ticks;
            double durationMs = (endTicks - startTicks) / 10000.0; // 1 Tick = 100纳秒 → 1毫秒 = 10000 Ticks 1毫秒等于1,000,000纳秒
            UnityEngine.Debug.Log($"打包执行耗时：{(durationMs / 1000):F2} 秒");
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
}
