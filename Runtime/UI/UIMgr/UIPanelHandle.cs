using System;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>面板对外可见的生命周期状态。</summary>
    public enum UIPanelState
    {
        Loading,
        Opened,
        Closing,
        Closed,
        Failed
    }

    /// <summary>
    /// 面板句柄。句柄只记录路径/类型/代次，真实状态从 UIManager 查询；
    /// 因此面板关闭后旧句柄不会误指向同路径重新打开的新面板。
    /// </summary>
    public class UIPanelHandle
    {
        internal readonly UIManager owner;
        internal readonly string path;
        internal readonly Type panelType;
        internal readonly E_UI_Layer layer;
        internal readonly int generation;

        internal UIPanelHandle(UIManager owner, string path, Type panelType, E_UI_Layer layer, int generation)
        {
            this.owner = owner;
            this.path = path;
            this.panelType = panelType;
            this.layer = layer;
            this.generation = generation;
        }

        public string Key => path;
        public Type PanelType => panelType;
        public E_UI_Layer Layer => layer;
        public UIPanelState State => owner != null ? owner.GetPanelState(path, generation) : UIPanelState.Closed;
        public BasePanel Panel => owner != null ? owner.GetPanelForHandle(path, generation) : null;
        public bool IsOpen => State == UIPanelState.Opened;
        public Task<bool> CloseAsync() => owner != null ? owner.CloseAsync(this) : Task.FromResult(false);

        public override string ToString()
        {
            return (PanelType != null ? PanelType.Name : "Panel") + "[" + path + "] " + State;
        }
    }

    /// <summary>强类型面板句柄；Panel 在关闭后为 null。</summary>
    public sealed class UIPanelHandle<T> : UIPanelHandle where T : BasePanel
    {
        internal UIPanelHandle(UIManager owner, string path, Type panelType, E_UI_Layer layer, int generation)
            : base(owner, path, panelType, layer, generation) { }

        public new T Panel => base.Panel as T;
    }
}
