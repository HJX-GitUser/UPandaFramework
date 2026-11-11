using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>单个面板的诊断快照。</summary>
    public sealed class UIPanelDiagnostic
    {
        public string Key;
        public string PanelType;
        public E_UI_Layer Layer;
        public UIPanelState State;
        public int Order;
        public bool Modal;
        public bool Paused;
        public bool Pooled;
        public double LoadMilliseconds;
        public double OpenMilliseconds;
        public int Generation;
    }

    /// <summary>按路径统计的空闲面板数量。</summary>
    public sealed class UIPoolEntryDiagnostic
    {
        public string Key;
        public int Count;
    }

    /// <summary>UIManager 的主线程诊断快照。</summary>
    public sealed class UIDiagnosticsSnapshot
    {
        public int OpenPanels;
        public int PendingPanels;
        public int PooledPanels;
        public int PoolHits;
        public int PoolMisses;
        public int InstancesCreated;
        public int InstancesReturned;
        public int InstancesDestroyed;
        public readonly List<UIPanelDiagnostic> Panels = new List<UIPanelDiagnostic>();
        public readonly List<UIPoolEntryDiagnostic> Pool = new List<UIPoolEntryDiagnostic>();
    }
}
