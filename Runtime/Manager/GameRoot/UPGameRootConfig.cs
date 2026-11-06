// =============================================================================
//  UPGameRootConfig.cs —— UPGameRoot 的配置对象（Inspector 面板与 Json 共用一个对象）
// -----------------------------------------------------------------------------
//  真源与优先级：
//      ① 启动时 UPGameRoot.Init() 的第一步会读 StreamingAssets/Data/GameRootConfig.json，
//         读到就用它覆盖面板上的值（读不到 / 解析失败才退回面板 / 代码里的默认值）；
//      ② Inspector 面板改完点「保存到 Json」写回文件；
//      ③ 手改 Json 同样生效（下次启动读入）。
//
//  为什么放 StreamingAssets：随包发布、不走 AB 就能被读到（Android 上也能用
//  StreamingAssetsLoader 的 UnityWebRequest 分支读）。
//
//  ⚠ 改字段名注意：JsonUtility 按字段名序列化，改名会让旧 Json 里的对应项失效（回落默认值）。
//     需要兼容旧文件就自己写迁移，或保留旧字段。
// =============================================================================

using System;
using System.IO;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// UPGameRoot 的配置（Inspector 面板与 GameRootConfig.json 共用这一个对象）。
    /// <para>字段默认值 = 旧版 UPGameRoot 上散字段的默认值，升级后行为与之前一致。</para>
    /// </summary>
    [Serializable]
    public class UPGameRootConfig
    {
        public int schemaVersion = 1;
        [Tooltip("AB 构建目标，与 Unity BuildTarget 名称一致；Player 启动和构建前校验")]
        public string bundleTarget = "StandaloneWindows64";
        public InputBinding[] inputBindings = InputBinding.Defaults();
        [Tooltip("资源加载方式：Editor=编辑器直读资源；Assetbundles=走 AssetBundle")]
        public AssetLoaddingMethod method = AssetLoaddingMethod.Editor;

        [Tooltip("是否启动资源热更（从 remoteURL 下载清单并做增量更新）")]
        public bool enableAssetUpdate = false;

        [Tooltip("资源相对路径；同时决定远端 URL 与 persistentDataPath 下的子目录")]
        public string LoadAssetPath = "AssetBundles/StandaloneWindows/";

        [Tooltip("远端（热更）根地址")]
        public string remoteURL = "http://127.0.0.1:80/";

        [Tooltip("等待一批下载完成的超时（秒）；<= 0 表示不超时")]
        public float downloadBatchTimeout = 300f;

        public HotUpdateSettings hotUpdate = new HotUpdateSettings();

        [Tooltip("启用运行时日志窗口（Reporter），正式包建议关闭")]
        public bool EnableDebugModel = false;

        [Tooltip("资源清单 AES 配置：必须与打包端一致，随包清单是明文时保持关闭")]
        public AssetBundleClassificationWindowConfig AssetAESConfig = new AssetBundleClassificationWindowConfig { enable = false };
    }

    /// <summary>
    /// 配置文件（StreamingAssets/Data/GameRootConfig.json）的路径与序列化。
    /// 路径规则集中在这里，运行时与编辑器共用，避免两边写死不一致。
    /// </summary>
    public static class UPGameRootConfigFile
    {
        /// <summary>StreamingAssets 下的子目录（与日志配置 LogConfig.json 同目录）</summary>
        public const string SubFolder = "Data";

        /// <summary>文件名（不含扩展名）</summary>
        public const string FileName = "GameRootConfig";

        /// <summary>工程内路径（提示 / 文档用）</summary>
        public static string AssetPath { get { return "Assets/StreamingAssets/" + SubFolder + "/" + FileName + ".json"; } }

        /// <summary>绝对路径（编辑器读写；桌面平台也能直接 File 读写）</summary>
        public static string AbsolutePath { get { return Path.Combine(Application.streamingAssetsPath, SubFolder, FileName + ".json"); } }

        /// <summary>相对 StreamingAssets 的路径（给 StreamingAssetsLoader 用，必须以 '/' 开头）</summary>
        public static string RelativePath { get { return "/" + SubFolder + "/" + FileName + ".json"; } }

        /// <summary>配置 → Json 文本（带缩进，方便手改与版本管理）</summary>
        public static UPGameRootConfig Snapshot(UPGameRootConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            return FromJson(ToJson(config));
        }
        public static UPGameRootConfig SnapshotAndValidate(UPGameRootConfig config, bool isEditor, string expectedBundleTarget = null)
        {
            var snapshot = Snapshot(config);
            var errors = Validate(snapshot, isEditor, expectedBundleTarget);
            if (errors.Count > 0) throw new InvalidDataException("框架配置无效：\n" + string.Join("\n", errors));
            return snapshot;
        }
        public static System.Collections.Generic.List<string> Validate(UPGameRootConfig value, bool isEditor, string expectedBundleTarget = null)
        {
            var errors = new System.Collections.Generic.List<string>();
            if (value == null) { errors.Add("配置为空"); return errors; }
            if (value.schemaVersion != 1) errors.Add("不支持的 schemaVersion：" + value.schemaVersion);
            if (!Enum.IsDefined(typeof(AssetLoaddingMethod), value.method)) errors.Add("资源加载模式无效");
            if (!isEditor && value.method == AssetLoaddingMethod.Editor) errors.Add("Player 禁止使用 Editor 资源加载模式");
            if (float.IsNaN(value.downloadBatchTimeout) || float.IsInfinity(value.downloadBatchTimeout) || value.downloadBatchTimeout < 0) errors.Add("下载超时必须为非负有限值");
            if (value.method == AssetLoaddingMethod.Assetbundles)
            {
                if (string.IsNullOrWhiteSpace(value.LoadAssetPath) || Path.IsPathRooted(value.LoadAssetPath)) errors.Add("LoadAssetPath 必须为非空相对路径");
                else try { AssetLocation.Combine("root", value.LoadAssetPath); } catch (ArgumentException) { errors.Add("LoadAssetPath 包含非法路径段"); }
                if (!Uri.TryCreate(value.remoteURL, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) errors.Add("remoteURL 必须为 HTTP/HTTPS 地址");
                if (!string.IsNullOrEmpty(expectedBundleTarget) && value.bundleTarget != expectedBundleTarget) errors.Add("bundleTarget 与目标平台不匹配：" + expectedBundleTarget);
            }
            if (value.hotUpdate == null) errors.Add("hotUpdate 配置为空");
            else if (value.hotUpdate.minimumFreeSpaceMB < 0 || value.hotUpdate.maximumStagingMB < 0 || value.hotUpdate.maximumManagedCacheMB < 0
                || value.hotUpdate.retainedManifestCount < 1 || value.hotUpdate.retainedManifestCount > 10) errors.Add("热更容量或清单保留数量无效");
            if (value.AssetAESConfig == null) errors.Add("AES 配置为空");
            else if (value.AssetAESConfig.enable)
            {
                int key = System.Text.Encoding.UTF8.GetByteCount(value.AssetAESConfig.AESKEY ?? "");
                if ((key != 16 && key != 24 && key != 32) || System.Text.Encoding.UTF8.GetByteCount(value.AssetAESConfig.AESIV ?? "") != 16) errors.Add("AES Key/IV 字节长度无效");
            }
            var actions = new System.Collections.Generic.HashSet<string>();
            if (value.inputBindings == null) errors.Add("输入映射为空");
            else foreach (var binding in value.inputBindings)
            {
                if (binding == null || string.IsNullOrWhiteSpace(binding.action) || string.IsNullOrWhiteSpace(binding.context)) { errors.Add("输入动作或上下文为空"); continue; }
                if (!actions.Add(binding.context + "/" + binding.action)) errors.Add("输入动作重复：" + binding.context + "/" + binding.action);
                if (!Enum.IsDefined(typeof(KeyCode), binding.positive) || !Enum.IsDefined(typeof(KeyCode), binding.negative)) errors.Add("输入按键无效：" + binding.action);
                if (string.IsNullOrEmpty(binding.axis) && binding.positive == KeyCode.None && binding.negative == KeyCode.None) errors.Add("输入动作未绑定：" + binding.action);
            }
            return errors;
        }
        public static string ToJson(UPGameRootConfig config)
        {
            return JsonUtility.ToJson(config ?? new UPGameRootConfig(), true);
        }

        /// <summary>
        /// Json 文本 → 配置；文本为空返回 null。
        /// <para>Json 非法时 <see cref="JsonUtility.FromJson{T}"/> 会抛异常，调用方自行 catch 并打日志。</para>
        /// </summary>
        public static UPGameRootConfig FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var result = new UPGameRootConfig();
            JsonUtility.FromJsonOverwrite(json, result);
            if (result.hotUpdate == null) result.hotUpdate = new HotUpdateSettings();
            return result;
        }
    }
}
