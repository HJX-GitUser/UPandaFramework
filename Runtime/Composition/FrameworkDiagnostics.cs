using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>主线程诊断快照；数值表示框架缓存与请求状态，不代表 Unity 原生资源内存。</summary>
    public sealed class FrameworkDiagnosticsSnapshot
    {
        public string FrameworkState { get; internal set; } = "NotCreated";
        public string CurrentStep { get; internal set; }
        public string InitializationError { get; internal set; }
        public long TotalStartupMilliseconds { get; internal set; }
        public readonly List<FrameworkModuleDiagnostic> Modules = new List<FrameworkModuleDiagnostic>();
        public readonly List<StartupStepMetric> StartupSteps = new List<StartupStepMetric>();
        public ResourceLoaderDiagnostics Resources { get; internal set; }
        public AssetBundleDiagnostics AssetBundles { get; internal set; }
        public int SceneScopes { get; internal set; }
        public int PendingSceneRequests { get; internal set; }
        public int OpenPanels { get; internal set; }
        public int PendingPanels { get; internal set; }
    }

    public static class FrameworkDiagnostics
    {
        public static FrameworkDiagnosticsSnapshot Capture(UPGameRoot root)
        {
            var snapshot = new FrameworkDiagnosticsSnapshot();
            if (root == null) return snapshot;
            snapshot.FrameworkState = root.State.ToString();
            snapshot.CurrentStep = root.CurrentStartupStep;
            snapshot.InitializationError = root.InitializationError?.Message;
            if (root.Lifecycle != null) snapshot.Modules.AddRange(root.Lifecycle.GetModuleDiagnostics());
            if (root.StartupMetrics != null)
            {
                snapshot.StartupSteps.AddRange(root.StartupMetrics);
                foreach (var step in root.StartupMetrics) snapshot.TotalStartupMilliseconds += step.Milliseconds;
            }

            var services = root.RuntimeServices;
            if (services != null)
            {
                if (services.ResourceLoader != null) snapshot.Resources = services.ResourceLoader.GetDiagnostics();
                snapshot.SceneScopes = services.SceneService.DiagnosticScopeCount;
                snapshot.PendingSceneRequests = services.SceneService.DiagnosticPendingRequestCount;
                snapshot.Modules.Add(new FrameworkModuleDiagnostic("SceneService",
                    services.SceneService.HasPendingRequests ? FrameworkModuleState.Running : FrameworkModuleState.Ready,
                    detail: $"作用域 {snapshot.SceneScopes} / 请求 {snapshot.PendingSceneRequests}"));
                snapshot.Modules.Add(new FrameworkModuleDiagnostic("GameFlow",
                    services.FlowController.Status == GameFlowStatus.Failed ? FrameworkModuleState.Failed : FrameworkModuleState.Ready,
                    detail: services.FlowController.Status + " / " + (services.FlowController.CurrentStateId ?? "未进入")));
                snapshot.Modules.Add(new FrameworkModuleDiagnostic("AppLifecycle", FrameworkModuleState.Ready,
                    detail: (services.AppLifecycleService.IsPaused ? "后台" : "前台") + " / " + (services.AppLifecycleService.IsFocused ? "有焦点" : "无焦点")));
            }

            var bundleLoader = root.AssetLoader != null ? root.AssetLoader.BundleLoader : null;
            if (bundleLoader != null) snapshot.AssetBundles = bundleLoader.GetDiagnostics();
            var ui = root.RootUIManager;
            if (ui != null)
            {
                snapshot.OpenPanels = ui.DiagnosticOpenPanelCount;
                snapshot.PendingPanels = ui.DiagnosticPendingPanelCount;
            }
            return snapshot;
        }
    }
}
