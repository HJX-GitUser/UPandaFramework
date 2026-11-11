using System;
using System.Collections.Generic;
using System.Reflection;
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

        /// <summary>替换场景注册的 Canvas（保留 Background/Main/Popup/Guide/Toast 五层约定）。</summary>
        void ResetCanvas(Canvas canvas);

        /// <summary>替换场景注册的 UI 相机。</summary>
        void ResetCamera(Camera camera);

        /// <summary>设置 UI 缩放（0.5–2，1 为设计分辨率）；Canvas 需要 CanvasScaler。</summary>
        void SetUiScale(float scale);

        /// <summary>当前 UI 缩放。</summary>
        float UiScale { get; }

        /// <summary>打开面板并返回可关闭/查询状态的句柄；失败或取消返回 null。</summary>
        Task<UIPanelHandle<T>> OpenAsync<T>(object panelArg = null) where T : BasePanel;

        /// <summary>在指定作用域中打开面板并返回句柄。</summary>
        Task<UIPanelHandle<T>> OpenInScopeAsync<T>(ILifetimeScope scope, object panelArg = null) where T : BasePanel;

        /// <summary>按 Type 打开面板；热更新代码优先使用，避免 AOT 泛型实例化。</summary>
        Task<UIPanelHandle> OpenAsync(Type panelType, object panelArg = null);

        /// <summary>按 Type 在指定作用域打开面板。</summary>
        Task<UIPanelHandle> OpenInScopeAsync(Type panelType, ILifetimeScope scope, object panelArg = null);

        /// <summary>按 UILoadInfo.loadPath 打开已注册/可扫描到的面板。</summary>
        Task<UIPanelHandle> OpenAsync(string loadPath, object panelArg = null);

        /// <summary>关闭句柄对应的面板；面板已关闭或句柄过期返回 false。</summary>
        Task<bool> CloseAsync(UIPanelHandle handle);

        /// <summary>按 Type 关闭面板；热更新代码优先使用。</summary>
        void ClosePanel(Type panelType);

        /// <summary>按 Type 查询已打开面板。</summary>
        BasePanel GetPanel(Type panelType);

        /// <summary>注册单个热更新面板类型。</summary>
        bool RegisterPanel(Type panelType);

        /// <summary>扫描并注册热更新程序集内的面板类型，返回成功数量。</summary>
        int RegisterPanels(Assembly assembly);

        /// <summary>热更新卸载/重载前关闭面板并清空类型缓存。</summary>
        void ResetForHotUpdate();

        /// <summary>关闭指定层栈顶面板。</summary>
        bool CloseTop(E_UI_Layer layer);

        /// <summary>关闭全部面板，或关闭指定层的全部面板；返回实际关闭数量。</summary>
        int CloseAll(E_UI_Layer? layer = null);

        /// <summary>处理返回键：关闭顶层可关闭面板；顶层禁止返回时返回 false。</summary>
        bool HandleBack();

        /// <summary>以 Toast 层打开一个面板，并在 duration 秒后自动关闭。</summary>
        Task<UIPanelHandle<T>> ShowToastAsync<T>(object panelArg = null, float duration = 1.5f) where T : BasePanel;

        /// <summary>按 Type 打开 Toast；热更新代码优先使用。</summary>
        Task<UIPanelHandle> ShowToastAsync(Type panelType, object panelArg = null, float duration = 1.5f);

        /// <summary>当前打开面板的快照，供诊断/业务查询。</summary>
        IReadOnlyList<UIPanelHandle> GetOpenPanels(E_UI_Layer? layer = null);

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
