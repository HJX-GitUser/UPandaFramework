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
            return JsonUtility.FromJson<UPGameRootConfig>(json);
        }
    }
}
