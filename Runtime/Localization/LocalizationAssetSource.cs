using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>本地化文本表读取抽象（便于测试替换；默认实现走 IAssetService）。</summary>
    public interface ILocalizationAssetSource
    {
        Task<string> LoadTextAsync(string path, AssetLoadMethod method, CancellationToken token);
    }

    /// <summary>
    /// 通过框架统一的 <see cref="IAssetService"/> 读取 TextAsset：
    /// 内置表用 Resources；项目表可用 Resources 或 AssetBundle。读取后立即归还租约。
    /// </summary>
    public sealed class LocalizationAssetSource : ILocalizationAssetSource
    {
        private readonly IAssetService assets;
        private readonly ILogService log;

        public LocalizationAssetSource(IAssetService assets, ILogService log = null)
        {
            this.assets = assets ?? throw new ArgumentNullException(nameof(assets));
            this.log = log;
        }

        public async Task<string> LoadTextAsync(string path, AssetLoadMethod method, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            token.ThrowIfCancellationRequested();
            try
            {
                AssetLease<TextAsset> lease = await assets.AcquireAsync<TextAsset>(path, method, token);
                try
                {
                    TextAsset asset = lease != null ? lease.Asset : null;
                    return asset != null ? asset.text : null;
                }
                finally
                {
                    lease?.Dispose();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                if (log != null)
                    log.Warning("[Localization] 表加载失败（" + method + "）：" + path + "：" + exception.Message);
                return null;
            }
        }
    }
}
