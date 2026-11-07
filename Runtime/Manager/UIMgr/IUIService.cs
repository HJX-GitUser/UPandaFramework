using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// UI 服务契约（Runtime 程序集，而非 Contracts）。
    /// <para>为什么不在 Contracts：面板基类 <see cref="BasePanel"/> 属于 Runtime，
    /// 把泛型面板 API 放进 Contracts 会造成 Contracts → Runtime 的循环引用。
    /// 需要 UI 能力的场景组件实现 <see cref="IUIServiceConsumer"/>，由 UPGameRoot 统一注入。</para>
    /// </summary>
    public interface IUIService
    {
        void Init();
        void Shutdown();

        /// <summary>替换场景注册的 Canvas（保留 Bot/Mid/Top/System 四层约定）。</summary>
        void ResetCanvas(Canvas canvas);

        /// <summary>替换场景注册的 UI 相机。</summary>
        void ResetCamera(Camera camera);

        Task<T> ShowPanelAsync<T>(object panelArg = null) where T : BasePanel;
        Task<T> ShowPanelInScopeAsync<T>(ILifetimeScope scope, object panelArg = null) where T : BasePanel;
        void ShowPanel<T>(object panelArg = null, UnityAction<T> callBack = null) where T : BasePanel;
        void ClosePanel<T>() where T : BasePanel;
        T GetPanel<T>() where T : BasePanel;
    }

    /// <summary>需要 UI 服务的场景组件实现此接口；注入时机与 IGameServicesConsumer 相同。</summary>
    public interface IUIServiceConsumer
    {
        void ConfigureUi(IUIService ui);
    }
}
