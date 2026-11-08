namespace UPandaGF
{
    /// <summary>启动/模块诊断状态（Core 定义，Composition 与诊断面板共用）。</summary>
    public enum FrameworkModuleState { Pending, Running, Ready, Failed, RolledBack }

    /// <summary>单个启动模块的诊断条目。</summary>
    public readonly struct FrameworkModuleDiagnostic
    {
        public readonly string Name;
        public readonly FrameworkModuleState State;
        public readonly long Milliseconds;
        public readonly string Detail;
        public FrameworkModuleDiagnostic(string name, FrameworkModuleState state, long milliseconds = -1, string detail = null)
        { Name = name; State = state; Milliseconds = milliseconds; Detail = detail; }
    }

    /// <summary>ResourcesLoader 的诊断计数。</summary>
    public readonly struct ResourceLoaderDiagnostics
    {
        public readonly int CachedEntries;
        public readonly int LoadingEntries;
        public readonly int TotalReferences;
        public readonly int ZeroReferenceEntries;
        public ResourceLoaderDiagnostics(int cachedEntries, int loadingEntries, int totalReferences, int zeroReferenceEntries)
        { CachedEntries = cachedEntries; LoadingEntries = loadingEntries; TotalReferences = totalReferences; ZeroReferenceEntries = zeroReferenceEntries; }
    }

    /// <summary>ABLoadMgr 的诊断计数。</summary>
    public readonly struct AssetBundleDiagnostics
    {
        public readonly int LoadedBundles;
        public readonly int PendingBundles;
        public readonly int PendingAssets;
        public readonly int RetiringBundles;
        /// <summary>租约 pin 总数（包含旧接口占用的 pin）。</summary>
        public readonly int LeasePins;
        /// <summary>仍被旧接口（GetAssetBundle / LoadResAsync）占用、需要 ReleaseLegacyBundles 才释放的包数。</summary>
        public readonly int LegacyPins;
        public AssetBundleDiagnostics(int loadedBundles, int pendingBundles, int pendingAssets, int retiringBundles, int leasePins, int legacyPins = 0)
        { LoadedBundles = loadedBundles; PendingBundles = pendingBundles; PendingAssets = pendingAssets; RetiringBundles = retiringBundles; LeasePins = leasePins; LegacyPins = legacyPins; }
    }
}
