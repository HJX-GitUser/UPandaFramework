using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

namespace UPandaGF
{
    /// <summary>
    /// AB包加载管理器
    /// </summary>
    public class ABLoadMgr : LazyMonoSingletonBase<ABLoadMgr>
    {
        /// <summary>
        /// 是否从StreamingAssets路径下加载
        /// </summary>
        // [HideInInspector]
        // public bool IsLoadedOnStreammingAssets = true;
        //主包
        private AssetBundle mainAB = null;
        //主包依赖获取配置文件
        private AssetBundleManifest manifest = null;

        //存储 AB包对象(AB包不能够重复加载 否则会报错)
        private Dictionary<string, AssetBundle> _loadedBundles = new Dictionary<string, AssetBundle>();
        /// <summary>
        /// 记录每个包是用哪种模式加载的。缓存键只有包名，不带加载路径，
        /// 同一会话里切换模式时会直接命中旧包 —— 表现为"新模式加载不到资源"，实际是复用了旧包。
        /// </summary>
        private readonly Dictionary<string, ABLoadPath> _loadedBundlePaths = new Dictionary<string, ABLoadPath>();
        /// <summary>
        /// 最近一次 AB 加载失败的根因（模式 / 路径 / HTTP 状态 / 原始错误）。
        /// 写在这里是为了让 Init 的失败汇总能带出真正的原因，而不用去翻前面几行日志。
        /// </summary>
        private string _lastLoadFailReason;
        private HashSet<string> _loadingBundles = new HashSet<string>();

        /// <summary>
        /// 获取AB包加载路径（相对路径）
        /// </summary>
        [HideInInspector]
        public string PathUrl;

        /// <summary>
        /// 主包名 根据平台不同 包名不同
        /// </summary>
        [HideInInspector]
        public string MainName;

        /// <summary>
        /// 主包加载路径
        /// </summary>
        public ABLoadPath MainPackageLoadPath = ABLoadPath.StreamingAssetsPath;

        /// <summary>
        /// 远程加载地址
        /// </summary>
        public string remoteURL = "http://127.0.0.1:8090/";

        private EventCenter eventCenter => EventCenter.Instance;

        private ABSourcesRelated sourceRef;

        #region 初始化
        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="pathUrl">相对路径</param>
        /// <param name="mainName">主包名</param>
        /// <param name="mainLoadPath">主包加载路径</param>
        /// <returns></returns>
        public async Task Init(string pathUrl, string mainName, ABLoadPath mainLoadPath)
        {
            PathUrl = pathUrl;
            MainName = mainName;
            MainPackageLoadPath = mainLoadPath;
            try
            {
                // 加载主包 和 配置文件
                // 加载所有包是 通过它才能得到依赖信息
                PLogger.Log($"AB包加载模式：{MainPackageLoadPath}，主包路径：{GetFullPath(MainPackageLoadPath, MainName)}");
                mainAB = await LoadAssetBundle(MainName, MainPackageLoadPath);
                if (mainAB == null)
                {
                    PLogger.LogError($"主包加载失败：{MainName}（模式 {MainPackageLoadPath}，路径 {GetFullPath(MainPackageLoadPath, MainName)}）"
                                     + $"\n根因：{(_lastLoadFailReason ?? "无详细记录（可能命中缓存中的空包）")}"
                                     + "\n对照：PersistentDataPath 模式要求文件已经被热更下载到 persistentDataPath；"
                                     + "RemotePath 模式要求 remoteURL 可访问（Android 还需允许明文 HTTP）");
                    return;
                }
                manifest = mainAB.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                if (manifest == null)
                {
                    PLogger.LogError($"主包 {MainName} 中没有 AssetBundleManifest：确认加载的是 AB 清单主包（不是普通资源包）");
                    return;
                }
                Debug.Log($"AB包加载路径：{PathUrl}\n资源包主包：{MainName}");
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"AssetBundleManifest 主包({MainName}:{mainLoadPath})加载失败:\n{e}");
            }
        }

        public void SetABSourcesRelated(ABSourcesRelated arg)
        {
            sourceRef = arg;
        }
        #endregion

        #region 加载AssetBundle
        /// <summary>
        /// 取得 AB 包的完整读取路径。
        /// <para>三种模式必须用各自的拼接规则，不能共用一个 Path.Combine：</para>
        /// <list type="bullet">
        /// <item>RemotePath 是 URL —— Path.Combine 在 base 没有尾斜杠时会插入平台分隔符 '\\'，
        /// 而 PathUrl 若以 '/' 开头又会被当成"根路径"，把 remoteURL 整段丢掉（Windows 上 IsPathRooted("/x") == true）。</item>
        /// <item>PersistentDataPath 是文件路径 —— 同理，PathUrl 以 '/' 开头会丢掉 Application.persistentDataPath，
        /// 变成 "/AssetBundles/Android/xxx" 这种落到盘符根的路径。</item>
        /// </list>
        /// </summary>
        /// <param name="loadPath">加载模式</param>
        /// <param name="abName">包名（文件名）</param>
        private string GetFullPath(ABLoadPath loadPath, string abName)
        {
            switch (loadPath)
            {
                case ABLoadPath.StreamingAssetsPath:
                    return Path.Combine(StreamingAssetsLoader.CombinePath(PathUrl), abName);

                case ABLoadPath.PersistentDataPath:
                    return CombineLocalPath(Application.persistentDataPath, PathUrl, abName);

                case ABLoadPath.RemotePath:
                    if (string.IsNullOrEmpty(remoteURL))
                    {
                        PLogger.LogError("[ABLoadMgr] RemotePath 模式要求 remoteURL 非空（检查 UPGameRoot 配置里的 remoteURL）");
                        return string.Empty;
                    }
                    return CombineUrl(remoteURL, PathUrl, abName);

                default:
                    return Path.Combine(StreamingAssetsLoader.CombinePath(PathUrl), abName);
            }
        }

        /// <summary>
        /// 拼接 URL。不要用 Path.Combine：它是文件系统 API，不适用于 URL。
        /// </summary>
        public static string CombineUrl(params string[] segments)
        {
            string url = string.Empty;
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment))
                    continue;

                url = url.Length == 0 ? segment : url.TrimEnd('/') + "/" + segment.TrimStart('/');
            }
            return url;
        }

        /// <summary>
        /// 拼接本地文件路径。会自动去掉后续段的前导 '/'、'\\'，
        /// 避免 Path.Combine 把以分隔符开头的段当成绝对路径而丢弃前面的 basePath。
        /// </summary>
        public static string CombineLocalPath(params string[] segments)
        {
            string path = string.Empty;
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment))
                    continue;

                path = path.Length == 0
                    ? segment
                    : Path.Combine(path, segment.TrimStart('/', '\\'));
            }
            return path;
        }

        /// <summary>
        /// http 请求失败时给出方向性提示。
        /// <para>先判定错误文本：只有真的像明文策略拦载才提 manifest，否则会拿 "404" 去说"明文被拦"。
        /// （Android 9+ / targetSdk>=28）</para>
        /// </summary>
        private static void LogRemoteFailureHint(string fullPath, string error)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(error))
                return;
            if (!fullPath.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase))
                return;

            string lower = error.ToLowerInvariant();
            if (lower.Contains("insecure") || lower.Contains("cleartext") || lower.Contains("not allowed") || lower.Contains("non-secure"))
            {
                // 注意顺序：Unity 自己的开关在所有平台（含编辑器）都会拦，且早于 Android 平台策略——先查它。
                PLogger.LogError($"[ABLoadMgr] 「{error}」= 明文 HTTP 被拦。按顺序处理：\n"
                                 + "  ① Unity 自己的开关（编辑器也会拦，最容易被漏查）：Player Settings → Other Settings → Configuration → "
                                 + "\"Allow downloads over HTTP\" 设为 [Always allowed]（PlayerSettings.insecureHttpOption：NotAllowed=0 / DevelopmentOnly=1 / AlwaysAllowed=2）；\n"
                                 + "  ② Android 平台策略（targetSdk≥28）：AndroidManifest.xml 的 <application> 上加 android:usesCleartextTraffic=\"true\"，改完必须重新打包；\n"
                                 + "  ③ 根治：服务端改成 https。");
                return;
            }

            PLogger.LogError("[ABLoadMgr] http 请求失败但不是明文策略问题，按这个顺序查："
                             + "① 设备能不能访问到该地址（拿设备浏览器直接打开这个 URL 试，PC 能打开不代表设备能）；"
                             + "② 服务器上文件是否真的存在（目录层级、大小写）；"
                             + "③ 是否命中旧的 AssetBundle 缓存（同一 URL 不传 hash 时会复用缓存，换包后必须带 hash 或清缓存）。");
        }
        /// <summary>
        /// 加载AssetBundle
        /// </summary>
        /// <param name="abName">包名</param>
        /// <param name="loodPath">加载方式</param>
        /// <param name="progressEvent">加载进度</param>
        /// <returns></returns>

        private async Task<AssetBundle> LoadAssetBundle(string abName, ABLoadPath loadPath)
        {
            string fullPath = GetFullPath(loadPath, abName);

            // ✅ 已加载
            if (_loadedBundles.TryGetValue(abName, out var loadedAb))
            {
                // 缓存只有包名这一层键：切换加载模式时会复用旧模式加载出来的包
                if (_loadedBundlePaths.TryGetValue(abName, out ABLoadPath cachedLoadPath) && cachedLoadPath != loadPath)
                {
                    PLogger.LogWarning($"[ABLoadMgr] 包 {abName} 已按 {cachedLoadPath} 模式加载过，本次请求的是 {loadPath} 模式，"
                                       + "将复用已加载的包。要真正切换模式请先 ClearAB() 或重启应用。");
                }
                return loadedAb;
            }

            // ✅ 防并发
            while (_loadingBundles.Contains(abName))
                await Task.Yield();

            if (_loadedBundles.TryGetValue(abName, out loadedAb))
                return loadedAb;

            _loadingBundles.Add(abName);

            AssetBundle ab = null;
            ABLoadProgressEvent progressEvent = new ABLoadProgressEvent(abName, loadPath, 0, false);
            try
            {
                switch (loadPath)
                {
                    case ABLoadPath.RemotePath:
                    case ABLoadPath.StreamingAssetsPath:
                        using (var request = UnityWebRequestAssetBundle.GetAssetBundle(fullPath))
                        {
                            request.SendWebRequest();

                            while (!request.isDone)
                            {
                                progressEvent.progress = request.downloadProgress;
                                eventCenter?.EventTrigger(progressEvent);
                                await Task.Yield();
                            }

#if UNITY_2020_1_OR_NEWER
                    if (request.result != UnityWebRequest.Result.Success)
#else
                            if (request.isNetworkError || request.isHttpError)
#endif
                            {
                                _lastLoadFailReason = $"模式 {loadPath}，URL {fullPath}，HTTP {request.responseCode}，结果 {request.result}，错误：{request.error}";
                                Debug.LogError($"AssetBundle加载失败\n模式：{loadPath}\nURL：{fullPath}\nHTTP：{request.responseCode} 结果：{request.result}\n错误：{request.error}");
                                LogRemoteFailureHint(fullPath, request.error);
                                return null;
                            }

                            ab = DownloadHandlerAssetBundle.GetContent(request);
                        }
                        break;

                    case ABLoadPath.PersistentDataPath:
                        var createRequest = AssetBundle.LoadFromFileAsync(fullPath);
                        if (createRequest == null)
                        {
                            _lastLoadFailReason = $"模式 {loadPath}，路径 {fullPath}，异常：AssetBundle.LoadFromFileAsync 未创建请求";
                            Debug.LogError($"AssetBundle加载失败：模式 {loadPath}，路径 {fullPath}（LoadFromFileAsync 未创建请求）");
                            return null;
                        }

                        while (!createRequest.isDone)
                        {
                            progressEvent.progress = createRequest.progress;
                            eventCenter?.EventTrigger(progressEvent);
                            await Task.Yield();
                        }

                        ab = createRequest.assetBundle;
                        break;
                }

                if (ab == null)
                {
                    _lastLoadFailReason = loadPath == ABLoadPath.PersistentDataPath
                        ? $"模式 {loadPath}，路径 {fullPath}，异常：该文件读不出有效 AB（文件不存在 / 不是 AB / 内容不完整）"
                        : $"模式 {loadPath}，路径 {fullPath}，异常：请求成功但 DownloadHandlerAssetBundle.GetContent 返回 null（内容不是有效 AB，或命中了损坏的缓存）";
                    Debug.LogError($"AssetBundle加载失败：模式 {loadPath}，路径 {fullPath}");
                    if (loadPath == ABLoadPath.PersistentDataPath)
                    {
                        PLogger.LogError("[ABLoadMgr] PersistentDataPath 模式要求该文件已经存在于 persistentDataPath："
                                         + "先确认热更下载真的成功过（enableAssetUpdate 打开、下载日志无报错），否则这个目录是空的。");
                    }
                    return null;
                }

                _loadedBundles[abName] = ab;
                _loadedBundlePaths[abName] = loadPath;

                progressEvent.progress = 1;
                progressEvent.isDown = true;
                eventCenter?.EventTrigger(progressEvent);

                return ab;
            }
            catch (System.Exception e)
            {
                _lastLoadFailReason = $"模式 {loadPath}，路径 {fullPath}，异常 {e.GetType().Name}: {e.Message}";
                Debug.LogError($"AssetBundle加载失败 特殊异常 '{abName}'\n{e}");
                // SendWebRequest() 对 http:// 会直接抛异常（不走 request.error），这里同样给出方向
                LogRemoteFailureHint(fullPath, e.Message);
                return null;
            }
            finally
            {
                _loadingBundles.Remove(abName);
            }
        }

        private IEnumerator LoadAssetBundle(string abName, ABLoadPath loodPath, UnityAction<AssetBundle> callback)
        {
            // 已成功加载过：直接回调，避免重复加载（重复 Add 会抛 ArgumentException）
            if (_loadedBundles.TryGetValue(abName, out AssetBundle cached) && cached != null)
            {
                if (_loadedBundlePaths.TryGetValue(abName, out ABLoadPath cachedLoadPath) && cachedLoadPath != loodPath)
                {
                    PLogger.LogWarning($"[ABLoadMgr] 包 {abName} 已按 {cachedLoadPath} 模式加载过，本次请求的是 {loodPath} 模式，"
                                       + "将复用已加载的包。要真正切换模式请先 ClearAB() 或重启应用。");
                }
                callback(cached);
                yield break;
            }

            // string fullPath = Path.Combine(PathUrl, abName);
            string fullPath = GetFullPath(loodPath, abName);
            AssetBundle ab = null;
            ABLoadProgressEvent aBLoadProgessEvent = new ABLoadProgressEvent(abName, loodPath, 0, false);
            switch (loodPath)
            {
                case ABLoadPath.RemotePath:
                case ABLoadPath.StreamingAssetsPath:
                    // 对于StreamingAssets路径（特别是WebGL和Android），使用UnityWebRequest
                    using (UnityWebRequest webRequest = UnityWebRequestAssetBundle.GetAssetBundle(fullPath))
                    {
                        webRequest.SendWebRequest();
                        while (!webRequest.isDone)
                        {
                            aBLoadProgessEvent.progress = webRequest.downloadProgress;
                            // PLogger.Log_yellow($"{aBLoadProgessEvent.abName}:下载进度：{aBLoadProgessEvent.progress}");
                            eventCenter.EventTrigger(aBLoadProgessEvent);
                            yield return null;
                        }
                        //检查请求结果
#if UNITY_2020_1_OR_NEWER
                        if (webRequest.result != UnityWebRequest.Result.Success)
#else
                        if (webRequest.isNetworkError || webRequest.isHttpError)
#endif
                        {
                            Debug.LogError($"AssetBundle加载失败\n模式：{loodPath}\nURL：{fullPath}\nHTTP：{webRequest.responseCode} 结果：{webRequest.result}\n错误：{webRequest.error}");
                            LogRemoteFailureHint(fullPath, webRequest.error);
                            callback.Invoke(null);
                            yield break;
                        }
                        ab = DownloadHandlerAssetBundle.GetContent(webRequest);
                        if (ab == null)
                        {
                            Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                            callback.Invoke(null);
                            yield break;
                        }
                    }
                    break;
                case ABLoadPath.PersistentDataPath:
                    // 对于本地文件路径，使用LoadFromFileAsync
                    AssetBundleCreateRequest createRequest = AssetBundle.LoadFromFileAsync(fullPath);
                    if (createRequest == null)
                    {
                        Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                        callback.Invoke(null);
                        yield break;
                    }
                    while (!createRequest.isDone)
                    {
                        aBLoadProgessEvent.progress = createRequest.progress;
                        //PLogger.Log_yellow($"{aBLoadProgessEvent.abName}:加载进度：{aBLoadProgessEvent.progress}");
                        eventCenter.EventTrigger(aBLoadProgessEvent);
                        yield return null;
                    }
                    ab = createRequest.assetBundle;
                    if (ab == null)
                    {
                        Debug.LogError($"AssetBundle加载失败\n{fullPath}");
                        callback.Invoke(null);
                        yield break;
                    }
                    break;
            }
            aBLoadProgessEvent.progress = 1;
            aBLoadProgessEvent.isDown = true;
            //PLogger.Log_yellow($"{aBLoadProgessEvent.abName}：{aBLoadProgessEvent.progress}");
            _loadedBundles[abName] = ab;
            _loadedBundlePaths[abName] = loodPath;
            eventCenter.EventTrigger(aBLoadProgessEvent);
            callback(ab);
        }
        #endregion

        #region 通过AssetBundle 获取资源
        /// <summary>
        /// 通过AssetBundle 获取资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="bundle"></param>
        /// <param name="assetName"></param>
        /// <returns></returns>
        private async Task<T> LoadAsset<T>(AssetBundle bundle, string assetName) where T : UnityEngine.Object
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync<T>(assetName);
            while (!abq.isDone)
            {
                await Task.Yield();
            }
            return abq.asset as T;
        }

        private async Task<UnityEngine.Object> LoadAsset(AssetBundle bundle, string assetName, System.Type type)
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync(assetName, type);
            while (!abq.isDone)
            {
                await Task.Yield();
            }
            return abq.asset;
        }

        private IEnumerator LoadAsset<T>(AssetBundle bundle, string assetName, UnityAction<T> callback) where T : UnityEngine.Object
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync<T>(assetName);
            yield return abq;
            callback(abq.asset as T);
        }

        private IEnumerator LoadAsset(AssetBundle bundle, string assetName, System.Type type, UnityAction<UnityEngine.Object> callback)
        {
            AssetBundleRequest abq = bundle.LoadAssetAsync(assetName, type);
            yield return abq;
            callback(abq.asset);
        }
        #endregion

        #region 加载指定包以及依赖包
        /// <summary>
        /// 加载指定包以及依赖包
        /// </summary>
        /// <param name="abName"></param>
        private async Task<AssetBundle> LoadDependenciesAndAimBundle(string abName, ABLoadPath loodPath)
        {
            if (mainAB == null || manifest == null)
            {
                Debug.LogError("主包未加载");
                return null;
            };
            //获取依赖包：加载失败不写缓存，否则 null 会被当成"已加载"，后续永远不再重试
            string[] strs = manifest.GetAllDependencies(abName);
            for (int i = 0; i < strs.Length; i++)
            {
                if (_loadedBundles.ContainsKey(strs[i]))
                    continue;

                AssetBundle dependency = await LoadAssetBundle(strs[i], loodPath);
                if (dependency == null)
                {
                    Debug.LogError($"依赖包加载失败，中止加载：{abName} -> {strs[i]}");
                    return null;
                }
                _loadedBundles[strs[i]] = dependency;
            }

            //加载目标包
            if (!_loadedBundles.ContainsKey(abName))
            {
                AssetBundle target = await LoadAssetBundle(abName, loodPath);
                if (target == null)
                {
                    Debug.LogError($"AssetBundle加载失败：{abName}");
                    return null;
                }
                _loadedBundles[abName] = target;
            }

            return _loadedBundles[abName];
        }
        private IEnumerator LoadDependenciesAndAimBundle(string abName, ABLoadPath loodPath, UnityAction<AssetBundle> callback)
        {
            if (mainAB == null || manifest == null)
            {
                Debug.LogError("主包未加载");
                callback(null);
                yield break;
            };
            //获取依赖包：加载失败不写缓存，避免 null 被当成"已加载"
            string[] strs = manifest.GetAllDependencies(abName);
            for (int i = 0; i < strs.Length; i++)
            {
                if (_loadedBundles.ContainsKey(strs[i]))
                    continue;

                string dependencyName = strs[i];
                AssetBundle dependency = null;
                yield return StartCoroutine(LoadAssetBundle(dependencyName, loodPath, (arg) => dependency = arg));

                if (dependency == null)
                {
                    Debug.LogError($"依赖包加载失败，中止加载：{abName} -> {dependencyName}");
                    callback(null);
                    yield break;
                }
                _loadedBundles[dependencyName] = dependency;
            }

            //加载目标包
            if (!_loadedBundles.ContainsKey(abName))
            {
                AssetBundle target = null;
                yield return StartCoroutine(LoadAssetBundle(abName, loodPath, (arg) => target = arg));

                if (target == null)
                {
                    Debug.LogError($"AssetBundle加载失败：{abName}");
                    callback(null);
                    yield break;
                }
                _loadedBundles[abName] = target;
            }

            callback(_loadedBundles[abName]);
        }

        public async Task<AssetBundle> GetAssetBundle(string abName, ABLoadPath loodPath)
        {
            return await LoadDependenciesAndAimBundle(abName, loodPath);
        }

        public void GetAssetBundle(string abName, ABLoadPath loodPath, UnityAction<AssetBundle> callback)
        {
            Debug.Log($"加载资源{abName}：{loodPath}");
            StartCoroutine(LoadDependenciesAndAimBundle(abName, loodPath, (arg) =>
             {
                 callback(arg);
             }));
        }
        #endregion

        #region 异步加载Assets资源
        /// <summary>
        /// 异步加载资源
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="abName">AB包名</param>
        /// <param name="resName">资源名</param>
        public async Task<T> LoadResAsync<T>(string abName, string resName, ABLoadPath loodPath) where T : UnityEngine.Object
        {
            AssetBundle bundle = await LoadDependenciesAndAimBundle(abName, loodPath);
            if (bundle == null)
            {
                Debug.LogError($"加载资源失败，AssetBundle未就绪：{abName}/{resName}");
                return null;
            }
            return await LoadAsset<T>(bundle, resName);
        }

        public async Task<Object> LoadResAsync(string abName, string resName, System.Type type, ABLoadPath loodPath)
        {
            AssetBundle bundle = await LoadDependenciesAndAimBundle(abName, loodPath);
            if (bundle == null)
            {
                Debug.LogError($"加载资源失败，AssetBundle未就绪：{abName}/{resName}");
                return null;
            }
            return await LoadAsset(bundle, resName, type);
        }

        public void LoadResAsync<T>(string abName, string resName, ABLoadPath loodPath, UnityAction<T> callBack) where T : Object
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, loodPath, callBack));
        }
        private IEnumerator ReallyLoadResAsync<T>(string abName, string resName, ABLoadPath loodPath, UnityAction<T> callBack) where T : Object
        {
            AssetBundle bundle = null;
            yield return StartCoroutine(LoadDependenciesAndAimBundle(abName, loodPath, (arg) => bundle = arg));
            if (bundle == null)
            {
                Debug.LogError($"加载资源失败，AssetBundle未就绪：{abName}/{resName}");
                callBack(null);
                yield break;
            }
            StartCoroutine(LoadAsset(bundle, resName, callBack));
        }

        public void LoadResAsync(string abName, string resName, System.Type type, ABLoadPath loodPath, UnityAction<Object> callBack)
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, type, loodPath, callBack));
        }
        public void LoadResAsync(string abName, string resName, ABLoadPath loodPath, UnityAction<Object> callBack)
        {
            StartCoroutine(ReallyLoadResAsync(abName, resName, typeof(Object), loodPath, callBack));
        }
        private IEnumerator ReallyLoadResAsync(string abName, string resName, System.Type type, ABLoadPath loodPath, UnityAction<Object> callBack)
        {
            AssetBundle bundle = null;
            yield return StartCoroutine(LoadDependenciesAndAimBundle(abName, loodPath, (arg) => bundle = arg));
            if (bundle == null)
            {
                Debug.LogError($"加载资源失败，AssetBundle未就绪：{abName}/{resName}");
                callBack(null);
                yield break;
            }
            StartCoroutine(LoadAsset(bundle, resName, type, callBack));
        }
        #endregion

        #region 卸载AB包的方法
        /// <summary>
        /// 卸载AB包
        /// </summary>
        /// <param name="name"></param>
        public bool UnLoadAB(string name)
        {
            if (_loadedBundles.ContainsKey(name))
            {
                if (_loadedBundles[name] == null)
                {
                    Debug.Log("该资源正处于异步加载中，无法卸载！");
                    return false;
                }
                _loadedBundles[name].Unload(false);
                _loadedBundles.Remove(name);
                _loadedBundlePaths.Remove(name);
            }
            return true;
        }
        /// <summary>
        /// 清空AB包的方法
        /// </summary>
        public void ClearAB()
        {
            StopAllCoroutines();
            AssetBundle.UnloadAllAssetBundles(false);
            _loadedBundles.Clear();
            _loadedBundlePaths.Clear();
            _loadingBundles.Clear();
            //卸载主包
            mainAB = null;
            // manifest 是从主包加载出来的资产，必须一并清掉；否则依赖查询仍会引用已卸载的包
            manifest = null;
        }
        #endregion

        /// <summary>
        /// 根据字节数得到大小
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

    }
    /// <summary>
    /// AB包加载进度事件
    /// </summary>
    public class ABLoadProgressEvent : EventArgBase
    {
        public string abName;
        public ABLoadPath loadPath;
        public float progress;
        public bool isDown;

        public ABLoadProgressEvent(string abName, ABLoadPath loadPath, float progress, bool isDown)
        {
            this.abName = abName;
            this.loadPath = loadPath;
            this.progress = progress;
            this.isDown = isDown;
        }
    }
}

