/* ==========================================================================================
 * AssetsLoader —— 资源加载门面（Editor 直读 / AssetBundle 两种模式）
 *
 * 【角色】
 *   实现 IAssetsLoader（旧接口，供老代码用）与 ISceneAssetProvider（供新的 SceneService 用）。
 *   职责是把“资源路径”翻译成“某个 AB 包里的某个资源”，并负责清单与主包的初始化。
 *
 * 【两种模式】
 *   Editor：直接用 AssetDatabase（EditorSourcesMgr）读工程资源，不需要 AB、不需要清单。
 *   AssetBundle：读 StreamingAssets/Data/assetData.assetref（清单）→ 初始化 ABLoadMgr（主包 + 依赖）
 *                → 之后每次取资源都通过清单查 bundleName / sourceName / loadPath。
 *
 * 【清单（assetref）里的三张表】
 *   sourcesDic：资源路径（工程内完整路径）→ { bundleName, sourceName }
 *   bundleInfo：bundleName → { loadPath（StreamingAssets / PersistentData / Remote）, size, md5 }
 *   mainBundleInfo：主包信息（AssetBundleManifest 就在这个包里，加载依赖关系全靠它）
 *
 * 【epoch（initializationGeneration）】
 *   Init 是异步的，期间可能被 Shutdown 或再次 Init。每次 Init 取一个自增 epoch，
 *   每个 await 之后都比对：不一致（或对象已销毁）就放弃后续装配，
 *   避免“旧的 Init 把新状态覆盖掉”或“对象已销毁还在用”。
 *
 * 【场景加载只有一条路】
 *   LoadSceneAsync 不直接调 SceneManager，而是转交 ISceneService（见 Services/SceneService.cs）：
 *   由它负责串行化、AB 准备、进度上报与场景资源作用域。
 *   随包（Build Settings）场景也走同一条路：清单里查不到、但 Unity 能直接加载时，
 *   PrepareSceneAsync 返回 null（无需准备 AB），由 UnitySceneBackend 直接加载。
 * ========================================================================================== */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

namespace UPandaGF
{
    /// <summary>
    /// 场景路径的归属判定（决定 <see cref="SceneService"/> 加载前是否需要准备 AB）。
    /// </summary>
    public enum ScenePathKind
    {
        /// <summary>AB 清单里有登记：加载前需要准备场景所在包。</summary>
        Manifest,
        /// <summary>清单里没有、但 Unity 能直接加载：随包场景（Build Settings），无需准备。</summary>
        Builtin,
        /// <summary>清单里有多个同名 .unity：必须改用完整路径。</summary>
        Ambiguous,
        /// <summary>既不在清单、Unity 也加载不了：路径写错或资源未打包。</summary>
        Missing,
    }

    /// <summary>
    /// 资源加载封装。
    /// <para>对外两条路径：旧接口（LoadAsync / LoadSceneAsync / LoadAssemblyAsync）与服务层（AcquireAsync / PrepareSceneAsync）。</para>
    /// <para>初始化由 UPGameRoot 在启动链里调用 <see cref="Init"/>；运行时不可重复初始化（除非先 Shutdown）。</para>
    /// </summary>
    public class AssetsLoader : MonoBehaviour, IAssetsLoader, ISceneAssetProvider
    {
        /// <summary>初始化代际号：每次 Init/Shutdown/ClearAB 自增；异步步骤靠它判断“自己是否已过期”。</summary>
        private int initializationGeneration;
        /// <summary>Init 是否跑完（Editor 模式在进入时就置 true；AB 模式要等主包加载成功）。</summary>
        private bool initialized;
        /// <summary>
        /// 资源服务是否可用：Init 跑完，并且“该模式所需的底层”已就绪。
        /// <para>按模式所需的<b>条件正向</b>写：Editor 只需要 initialized；AssetBundle 还要求主包已加载。</para>
        /// <para>写成“等于 Editor”而不是“不等于 AssetBundle”，是为了将来新增加载模式时，
        /// 不会因为“没命中 AssetBundle”而被误判成“不需要 AB”。</para>
        /// </summary>
        public bool IsInitialized => initialized && (method == AssetLoaddingMethod.Editor
            || method == AssetLoaddingMethod.NoAssetBundle
            || (abLoadMgr != null && abLoadMgr.IsInitialized));
        /// <summary>当前加载模式（Editor / AssetBundle）。</summary>
        private AssetLoaddingMethod method;
        /// <summary>AB 加载管理器（仅 AB 模式有效）。单例，Init 时才拿。</summary>
        private ABLoadMgr abLoadMgr;
        /// <summary>供框架内部/诊断直接拿 AB 管理器（不对外暴露）。</summary>
        public ABLoadMgr BundleLoader => abLoadMgr;
        /// <summary>Editor 模式下的 AssetDatabase 读取器。</summary>
        private EditorSourcesMgr editorSourcesMgr;
        /// <summary>场景服务；由 UPGameRoot 的 Services 启动步骤注入。</summary>
        private ISceneService sceneService;
        /// <summary>由 UPGameRoot 的 Services 启动步骤注入；场景加载只走 ISceneService 这一条路径。</summary>
        public void ConfigureScenes(ISceneService service) { sceneService = service; }
        /// <summary>当前生效的资源清单（assetref 反序列化结果）；Editor 模式为 null。</summary>
        private ABSourcesRelated sourceRef;

        /// <summary>清单在 StreamingAssets 下的目录（注意前后都有 '/'）。</summary>
        private string assetRefSavePath = "/Data/";
        /// <summary>清单文件名（无后缀）。</summary>
        private string assetRefName = "assetData";
        /// <summary>清单文件后缀。</summary>
        private string assetRefextension = ".assetref";

        [HideInInspector]
        /// <summary>清单的 AES 配置（由 UPGameRoot 注入）；未启用时按明文解析。</summary>
        public AssetBundleClassificationWindowConfig AssetAESConfig;

        /// <summary>
        /// 初始化加载器。
        /// </summary>
        /// <param name="arg0">加载模式。</param>
        /// <param name="remoteURL">远程 AB 根地址（仅 AB 模式且 loadPath=Remote 时需要）。</param>
        /// <param name="LoadAssetPath">AB 相对目录（如 AssetBundles/Android/），用于拼 StreamingAssets/Persistent/Remote 路径。</param>
        /// <param name="sourceRefArgBytes">已热更好的清单（非 null 时直接用它，不再读随包数据）。</param>
        /// <remarks>
        /// 流程：Editor 模式直接返回；AB 模式先拿清单（参数优先，否则读 StreamingAssets）→ 校验主包信息
        /// → 初始化 ABLoadMgr（加载主包与 Manifest）→ 把清单交给 ABLoadMgr 供后续依赖查询。
        /// 每一步 await 后都比对 epoch，防止“初始化中途被 Shutdown/重入”。
        /// </remarks>
        public async Task Init(AssetLoaddingMethod arg0, string remoteURL, string LoadAssetPath, ABSourcesRelated sourceRefArgBytes)
        {
            int epoch = ++initializationGeneration;
            PLogger.Log("AssetsLoader Init");
            initialized = false;   // 先置 false：装配过程中 IsInitialized 必须为假
            method = arg0;
            sourceRef = null;
            editorSourcesMgr = EditorSourcesMgr.Instance;
            if (method == AssetLoaddingMethod.Editor)
            {
#if !UNITY_EDITOR
                throw new System.InvalidOperationException("Player 中不能使用 Editor 资源加载模式。");
#else
                // Editor 模式：不需要 AB，也不需要清单
                initialized = true;
                return;
#endif
            }
            if (method == AssetLoaddingMethod.NoAssetBundle)
            {
                // NoAssetBundle 模式：跳过 AB 模块初始化，资源由 ResourcesLoader 直接读取。
                initialized = true;
                PLogger.Log("AssetsLoader Inited (Resources)");
                return;
            }
            if (sourceRefArgBytes != null)
            {
                // 热更流程已经下载并解析好的清单：直接用，避免重复读随包文件
                sourceRef = sourceRefArgBytes;
            }
            else
            {
                // 随包清单：StreamingAssets/Data/assetData.assetref（Android 下走 UnityWebRequest）
                string assetRefpath = assetRefSavePath + assetRefName + assetRefextension;
                byte[] b = await StreamingAssetsLoader.LoadBinaryDataAsync(assetRefpath);
                // 等待期间可能被 Shutdown 或又发起了一次 Init：过期就直接放弃
                if (epoch != initializationGeneration || this == null) return;
                if (b != null) sourceRef = LoadABSourcesRelated(b);
            }


            if (method == AssetLoaddingMethod.AssetBundle)
            {
                if (sourceRef == null)
                {
                    PLogger.LogError("资源关联数据 assetData.assetref 缺失或反序列化失败，无法以 AssetBundle 方式加载资源");
                    return;
                }
                if (sourceRef.mainBundleInfo == null)
                {
                    PLogger.LogError("资源关联数据缺少主包信息 mainBundleInfo");
                    return;
                }
                abLoadMgr = ABLoadMgr.Instance;
                abLoadMgr.remoteURL = remoteURL;
                PLogger.Log($"主包：{sourceRef.mainBundleInfo.bundleName}，加载方式：{sourceRef.mainBundleInfo.loadPath}");
                // 加载主包 + Manifest；失败时 ABLoadMgr 内部会打根因日志
                await abLoadMgr.Init(LoadAssetPath, sourceRef.mainBundleInfo.bundleName, sourceRef.mainBundleInfo.loadPath);
                if (epoch != initializationGeneration || this == null || !abLoadMgr.IsInitialized) return;
                abLoadMgr.SetABSourcesRelated(sourceRef);
            }
            initialized = true;
            PLogger.Log("AssetsLoader Inited !!!");
        }

        /// <summary>
        /// 服务层入口：取一份资源持有权（AssetLease）。用完必须 Dispose，否则引用计数/AB 不会释放。
        /// <para>Editor 模式返回“无 owner 的租约”（AssetDatabase 管理资源，不需要释放，但保留显式生命周期语义）。</para>
        /// <para>AB 模式：查清单拿到 bundleName/sourceName/loadPath，再交给 ABLoadMgr。资源未登记返回 null。</para>
        /// </summary>
        public async Task<AssetLease<T>> AcquireAsync<T>(string path) where T : UnityEngine.Object
        {
            if (!IsInitialized) throw new System.InvalidOperationException("资源服务尚未就绪。");
            if (method == AssetLoaddingMethod.Editor)
            {
                var asset = editorSourcesMgr.Load<T>(path);
                // AssetDatabase 自己管理工程资源，不需要释放；仍返回租约是为了统一调用方的生命周期写法（owner 为 null）
                return asset != null ? new AssetLease<T>(asset, null) : null;
            }
            if (method == AssetLoaddingMethod.NoAssetBundle)
                return await ResourcesLoader.Instance.AcquireAsync<T>(path);
            // 清单里查不到（未打包/路径拼错）时 TryGetAssetArg 已打过错误日志，这里返回 null
            AssetRelatedArg arg;
            if (!TryGetAssetArg(path, out arg)) return null;
            return await abLoadMgr.AcquireAsync<T>(arg.bundleName, arg.sourceName, sourceRef.GetABLoadPath(arg));
        }
        /// <summary>
        /// 判断场景路径属于 AB 清单还是随包场景（<paramref name="canStreamedLevelBeLoaded"/> 固定用 <see cref="Application.CanStreamedLevelBeLoaded"/>）。
        /// </summary>
        /// <param name="resolvedPath">Manifest 时为清单里的完整路径，其余情况为 null。</param>
        public static ScenePathKind ClassifyScenePath(string path, ICollection<string> manifestKeys, out string resolvedPath)
        {
            return ClassifyScenePath(path, manifestKeys, Application.CanStreamedLevelBeLoaded, out resolvedPath);
        }
        /// <summary>
        /// 判断场景路径属于 AB 清单还是随包场景。
        /// <para><b>清单优先</b>：清单里登记的 .unity（完整路径或唯一短名）一律按 AB 处理，
        /// 即使 Unity 此时也能直接加载 —— 反过来判断会跳过资源准备，进场景时资源缺失。</para>
        /// <para>清单里没有时，才用 <paramref name="canStreamedLevelBeLoaded"/> 确认是不是随包场景。</para>
        /// </summary>
        /// <param name="resolvedPath">Manifest 时为清单里的完整路径，其余情况为 null。</param>
        public static ScenePathKind ClassifyScenePath(string path, ICollection<string> manifestKeys, Func<string, bool> canStreamedLevelBeLoaded, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrEmpty(path) || manifestKeys == null) return ScenePathKind.Missing;
            if (manifestKeys.Contains(path)) { resolvedPath = path; return ScenePathKind.Manifest; }
            // 允许传短名（如 "GameScene"）：在清单里找唯一同名的 .unity，重名必须报错而不是随便挑一个
            string match = null;
            int matches = 0;
            foreach (string key in manifestKeys)
            {
                if (!key.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileNameWithoutExtension(key) != path) continue;
                match = key; matches++;
            }
            if (matches > 1) return ScenePathKind.Ambiguous;
            if (matches == 1) { resolvedPath = match; return ScenePathKind.Manifest; }
            if (canStreamedLevelBeLoaded != null && canStreamedLevelBeLoaded(path)) return ScenePathKind.Builtin;
            return ScenePathKind.Missing;
        }
        /// <summary>
        /// 为即将加载的场景准备资源（ISceneAssetProvider 的实现）。
        /// <para>返回值是“场景资源包的持有权”，由 SceneService 挂到该场景的 LifetimeScope 上，场景卸载时自动释放。</para>
        /// <para>返回 null 表示不需要预加载（Editor 直读资源；或场景随包打进 Build Settings、不在 AB 清单里）。</para>
        /// <para>失败时抛异常（SceneService 会把本次加载置为 Failed），不返回 null 混淆“无需准备”。
        /// 取消时 token 抛出，且不会留下已加载的包（WaitForLeaseAsync 会处理迟到资源）。</para>
        /// </summary>
        public async Task<IDisposable> PrepareSceneAsync(string path, System.Threading.CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!IsInitialized) throw new System.InvalidOperationException("资源服务尚未就绪。");
            // Editor 模式无需准备 AB；返回 null 表示“不需要预加载”
            // （SceneService 装配时本来就不会传 provider，这里再兜一层，防止被直接调用/被测试替换后端时漏判）
            if (method == AssetLoaddingMethod.Editor) return null;
            if (method == AssetLoaddingMethod.NoAssetBundle) return null;
            AssetRelatedArg arg;
            // 场景归属：清单里登记 → 先加载 AB；清单外但 Unity 能直接加载 → 随包场景，无需准备。
            // 顺序必须是“先清单、后随包”：AB 场景在包加载前 CanStreamedLevelBeLoaded 同样是 false，反着判断会漏准备。
            string resolved;
            switch (ClassifyScenePath(path, sourceRef.sourcesDic.Keys, out resolved))
            {
                // 清单与 Build Settings 都登记时按 AB 加载（清单优先）：提示一次，避免“改了随包场景却不生效”的困惑
                case ScenePathKind.Manifest:
                    if (Application.CanStreamedLevelBeLoaded(resolved))
                        PLogger.LogWarning("场景同时在 AB 清单与 Build Settings 中，按 AB 加载：" + resolved);
                    path = resolved; break;
                case ScenePathKind.Builtin: return null;
                default: throw new ArgumentException("场景名称未登记或存在重名，请使用完整路径：" + path);
            }
            if (!TryGetAssetArg(path, out arg)) throw new System.ArgumentException("场景资源未登记：" + path);
            // 只加载场景包（不加载包内资源）；等待可取消，迟到资源会自动归还
            var lease = await AssetService.WaitForLeaseAsync(abLoadMgr.AcquireBundleAsync(arg.bundleName, sourceRef.GetABLoadPath(arg)), token);
            if (lease == null) throw new System.IO.IOException("场景资源包加载失败：" + path);
            return lease;
        }

        /// <summary>旧接口：异步加载单个资源（不涉及“持有权”，由 AB/Resources 的缓存自己管）。资源未登记返回 null。</summary>
        public async Task<T> LoadAsync<T>(string path) where T : UnityEngine.Object
        {
            T asset = null;
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    asset = editorSourcesMgr.Load<T>(path);
                    break;
                case AssetLoaddingMethod.AssetBundle:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                        break;
                    asset = await abLoadMgr.LoadResAsync<T>(arg.bundleName, arg.sourceName, sourceRef.GetABLoadPath(arg));
                    break;
                case AssetLoaddingMethod.NoAssetBundle:
                    AssetLease<T> resourcesLease = await ResourcesLoader.Instance.AcquireAsync<T>(path);
                    asset = resourcesLease != null ? resourcesLease.Asset : null;
                    resourcesLease?.Dispose();
                    break;
            }
            return asset;
        }

        /// <summary>旧接口：按 Type 加载（适用于泛型不便使用的场合，如反射/编辑器工具）。</summary>
        public async Task<UnityEngine.Object> LoadAsync(string path, System.Type type)
        {
            UnityEngine.Object asset = null;
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    asset = editorSourcesMgr.Load(path, type);
                    break;
                case AssetLoaddingMethod.AssetBundle:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                        break;
                    asset = await abLoadMgr.LoadResAsync(arg.bundleName, arg.sourceName, type, sourceRef.GetABLoadPath(arg));
                    break;
                case AssetLoaddingMethod.NoAssetBundle:
                    asset = await LoadResourcesObjectAsync(path, type);
                    break;
            }
            return asset;
        }

        /// <summary>旧接口（回调式）：加载完成后回调；失败/未登记时回调 null（保证回调一定会被调一次）。</summary>
        public void LoadAsync<T>(string path, UnityAction<T> callback) where T : UnityEngine.Object
        {
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    callback?.Invoke(editorSourcesMgr.Load<T>(path));
                    break;
                case AssetLoaddingMethod.AssetBundle:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                    {
                        callback?.Invoke(null);
                        return;
                    }
                    abLoadMgr.LoadResAsync(arg.bundleName, arg.sourceName, sourceRef.GetABLoadPath(arg), callback);
                    break;
                case AssetLoaddingMethod.NoAssetBundle:
                    ResourcesLoader.Instance.LoadAsync<T>(path, asset =>
                    {
                        try { callback?.Invoke(asset); }
                        finally { ResourcesLoader.Instance.UnLoadAsset<T>(path); }
                    });
                    break;
            }
        }

        /// <summary>旧接口（回调式，带 Type）：同上。</summary>
        public void LoadAsync(string path, System.Type type, UnityAction<UnityEngine.Object> callback)
        {
            switch (method)
            {
                case AssetLoaddingMethod.Editor:
                    callback?.Invoke(editorSourcesMgr.Load(path, type));
                    break;
                case AssetLoaddingMethod.AssetBundle:
                    AssetRelatedArg arg;
                    if (!TryGetAssetArg(path, out arg))
                    {
                        callback?.Invoke(null);
                        return;
                    }
                    abLoadMgr.LoadResAsync(arg.bundleName, arg.sourceName, type, sourceRef.GetABLoadPath(arg), callback);
                    break;
                case AssetLoaddingMethod.NoAssetBundle:
                    ResourcesLoader.Instance.LoadAsync(path, type, asset =>
                    {
                        try { callback?.Invoke(asset); }
                        finally { ResourcesLoader.Instance.UnLoadAsset(path, type); }
                    });
                    break;
            }
        }


        /// <summary>旧接口：加载场景（Single 模式），等同于下面重载的默认参数形式。</summary>
        public void LoadSceneAsync(string path, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null)
        {
            LoadSceneAsync(path, LoadSceneMode.Single, assetLoadComplete, sceneLoadComplete);
        }
        /// <summary>
        /// 旧接口：加载场景。实际工作全部交给 ISceneService（串行化 / AB 准备 / 进度 / 作用域）。
        /// <para>回调语义：assetLoadComplete 在“开始真正 Loading”时触发一次（资源已就绪）；
        /// sceneLoadComplete 在场景加载完成后触发。</para>
        /// <para>本方法不抛异常（异常在内部记录）；取消时两个回调都不会被调。</para>
        /// </summary>
        public void LoadSceneAsync(string path, LoadSceneMode loadSceneMode, UnityAction assetLoadComplete = null, UnityAction sceneLoadComplete = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError($"场景路径为空：{path}");
                return;
            }
            if (sceneService == null)
            {
                // 该状态只出现在 Services 启动步骤之前、或 Shutdown 之后；此时资源系统本身也不可用。
                PLogger.LogError($"场景服务尚未装配，无法加载场景：{path}。请先等待 UPGameRoot 就绪，并使用 Services.Scenes。");
                return;
            }
            // 不 await：保留旧的 fire-and-forget 语义；异常在 LoadManagedSceneAsync 内部处理
            _ = LoadManagedSceneAsync(path, loadSceneMode, assetLoadComplete, sceneLoadComplete);
        }

        /// <summary>逐个调用回调节点，单个监听抛异常不影响其余回调（也不会把场景加载流程带崩）。</summary>
        private static void InvokeSceneCallback(UnityAction callback)
        {
            if (callback == null) return;
            foreach (UnityAction action in callback.GetInvocationList())
                try { action(); } catch (Exception e) { Debug.LogException(e); }
        }
        /// <summary>
        /// 真正驱动场景加载：监听进度，第一次进入 Loading 阶段时通知 prepared；完成后通知 completed。
        /// <para>取消静默（不算错误）；其它异常记日志。都不向外抛。</para>
        /// </summary>
        private async Task LoadManagedSceneAsync(string path, LoadSceneMode mode, UnityAction prepared, UnityAction completed)
        {
            bool notified = false;
            try
            {
                await sceneService.LoadAsync(path, mode, progress: progress => {
                    if (!notified && progress.Phase == SceneTransitionPhase.Loading)
                    { notified = true; InvokeSceneCallback(prepared); }
                });
                InvokeSceneCallback(completed);
            }
            catch (System.OperationCanceledException) { }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>
        /// 旧接口（回调式）：加载热更程序集（HybridCLR 的 dll 以 TextAsset 形式打进 AB）。
        /// <para>Player：走 AB 读出 dll 字节 → Assembly.Load；Editor：不加载，直接从已加载程序集里按名字找。</para>
        /// <para>失败/路径为空都会回调 null（不会不回调）。</para>
        /// </summary>
        public void LoadAssemblyAsync(string path, UnityAction<Assembly> callback)
        {
            Assembly hotUpdateAss = null;
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError("程序集路径为空");
                callback?.Invoke(null);
                return;
            }
#if !UNITY_EDITOR
            LoadAsync<TextAsset>(path, (dllAsset) =>
            {
                if (dllAsset == null)
                {
                    PLogger.LogError("程序集文件加载失败：" + path);
                    callback?.Invoke(null);
                    return;
                }
                try
                {
                    hotUpdateAss = Assembly.Load(dllAsset.bytes);
                }
                catch (System.Exception e)
                {
                    PLogger.LogError($"加载程序集 {path} 失败：{e}");
                }
                callback?.Invoke(hotUpdateAss);
            });
#else
            // Editor下无需加载，直接查找获得HotUpdate程序集
            string AssemblyName = Path.GetFileName(path).Split('.')[0];
            try
            {
                hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == AssemblyName);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"{AssemblyName}\n{e}");
            }
            callback?.Invoke(hotUpdateAss);
#endif
        }

        /// <summary>旧接口（Task 式）：同 <see cref="LoadAssemblyAsync(string, UnityAction{Assembly})"/>，失败返回 null。</summary>
        public async Task<Assembly> LoadAssemblyAsync(string path)
        {
            Assembly hotUpdateAss = null;
            if (string.IsNullOrEmpty(path))
            {
                PLogger.LogError("程序集路径为空");
                return null;
            }
#if !UNITY_EDITOR
            TextAsset dllAsset = await LoadAsync<TextAsset>(path);
            if (dllAsset == null)
            {
                PLogger.LogError("程序集文件加载失败：" + path);
                return null;
            }
            try
            {
                hotUpdateAss = Assembly.Load(dllAsset.bytes);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"加载程序集 {path} 失败：{e}");
            }
#else
            // Editor下无需加载，直接查找获得HotUpdate程序集
            string AssemblyName = Path.GetFileName(path).Split('.')[0];
            try
            {
                hotUpdateAss = System.AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == AssemblyName);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"{AssemblyName}\n{e}");
            }
#endif
            return hotUpdateAss;
        }

        /// <summary>旧接口：卸载指定 AB。假模式（Editor）下总是返回 true。</summary>
        public bool UnLoadAB(string abName)
        {
            bool isUnload = false;
            if (method == AssetLoaddingMethod.AssetBundle)
            {
                isUnload = abLoadMgr.UnLoadAB(abName);
            }
            else
            {
                isUnload = true;
            }
            return isUnload;
        }

        /// <summary>
        /// 关闭：代际号 +1（让进行中的 Init 作废）、清空清单/AB/场景服务引用。
        /// <para>对象销毁时（OnDestroy）自动调用；重复调用安全。</para>
        /// </summary>
        public void Shutdown()
        {
            initializationGeneration++;
            initialized = false;
            sceneService = null;
            if (abLoadMgr != null) abLoadMgr.ClearAB();
            abLoadMgr = null;
            sourceRef = null;
        }
        private void OnDestroy() { Shutdown(); }

        /// <summary>
        /// 清 AB 缓存，但保留清单与场景服务（用于“重进游戏但不重载清单”的场合）。
        /// <para>AB 模式下会把 initialized 置 false，之后必须重新 Init 才能再用。</para>
        /// </summary>
        public void ClearAB()
        {
            initializationGeneration++;
            if (method == AssetLoaddingMethod.AssetBundle) initialized = false;
            if (method == AssetLoaddingMethod.AssetBundle)
            {
                if (abLoadMgr != null) abLoadMgr.ClearAB();
            }
        }

        private static Task<UnityEngine.Object> LoadResourcesObjectAsync(string path, System.Type type)
        {
            var completion = new TaskCompletionSource<UnityEngine.Object>();
            ResourcesLoader.Instance.LoadAsync(path, type, asset =>
            {
                completion.TrySetResult(asset);
                ResourcesLoader.Instance.UnLoadAsset(path, type);
            });
            return completion.Task;
        }

        /// <summary>
        /// 根据资源路径查询资源加载参数；资源不存在时记录错误并返回 false。
        /// <para>这是一道完整校验：已就绪 + 路径非空 + sourcesDic 命中 + bundleInfo 里有该包，
        /// 任一项不满足都当作“资源不存在”（并把路径打进日志，便于看出是拼写错还是没打进包）。</para>
        /// </summary>
        private bool TryGetAssetArg(string path, out AssetRelatedArg arg)
        {
            arg = null;
            if (IsInitialized && !string.IsNullOrEmpty(path) && sourceRef != null
                && sourceRef.sourcesDic != null && sourceRef.sourcesDic.TryGetValue(path, out arg)
                && arg != null && sourceRef.bundleInfo != null && sourceRef.bundleInfo.ContainsKey(arg.bundleName))
                return true;
            PLogger.LogError("该资源不存在：" + path);
            return false;
        }

        /// <summary>
        /// 反序列化资源清单（assetref）。
        /// <para>按 AssetAESConfig 决定是否先 AES 解密；空数据/格式错都返回 null 并记错误日志。
        /// 注意：“打包端加密”与“运行端开启 AES”必须成对，否则清单会解不开。</para>
        /// </summary>
        public ABSourcesRelated LoadABSourcesRelated(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                PLogger.LogError("资源关联数据为空，无法反序列化");
                return null;
            }
            try
            {
                return AssetReferenceSerializer.Decode(bytes, AssetAESConfig);
            }
            catch (System.Exception e)
            {
                PLogger.LogError($"资源关联数据反序列化失败：{e}");
                return null;
            }
        }

    }
}

