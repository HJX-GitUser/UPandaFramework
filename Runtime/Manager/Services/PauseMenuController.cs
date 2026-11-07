/* ==========================================================================================
 * PauseMenuController —— 可复用的暂停菜单行为
 *
 * 【怎么用】
 *   把本脚本挂在场景里的任意常驻物体（或 UI 根）上，把菜单根节点拖到 menuRoot。
 *   它会在 Update 里监听：Gameplay 上下文下的 "Pause" 打开菜单，UI 上下文下的 "Cancel" 关闭。
 *
 * 【它做了三件事（成对）】
 *   1) 向 IPauseService 申请一份 "Menu" 暂停凭据（与后台暂停嵌套，互不影响）；
 *   2) 向输入服务压入 "UI" 上下文（屏蔽 Gameplay，菜单按钮才能正常响应）；
 *   3) 显示 menuRoot。
 *   关闭时按相反顺序释放。
 *
 * 【为什么 OnDisable 里也要 Close】物体被禁用/场景卸载时，Update 不会再跑；
 *   如果此时菜单还开着，暂停凭据就永远不释放 —— 表现为“回到游戏后 timeScale 一直是 0”。
 * ========================================================================================== */

using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>可复用暂停菜单行为；在场景中挂载并绑定现有 menuRoot，支持嵌套暂停。</summary>
    public sealed class PauseMenuController : MonoBehaviour, IGameServicesConsumer
    {
        /// <summary>菜单根节点（打开时 SetActive(true)，关闭时 false）。</summary>
        [SerializeField] private GameObject menuRoot;
        /// <summary>由框架注入的服务集合；未注入时 Update 不做任何事。</summary>
        private IGameServices services;
        /// <summary>暂停凭据 / UI 上下文凭据。非 null 即表示菜单开着。</summary>
        private IDisposable pause, context;
        public bool IsOpen => pause != null;
        /// <summary>注入服务。先关闭菜单（如果开着），避免换服务后旧凭据泄漏。</summary>
        public void Configure(IGameServices value) { Close(); services = value; }
        /// <summary>打开菜单：申请暂停 + 压入 UI 上下文 + 显示面板。重复调用安全。</summary>
        public void Open()
        {
            if (services == null || IsOpen) return;
            pause = services.Pause.Pause("Menu");
            // 压入 UI 上下文：Gameplay 输入被屏蔽，菜单导航键仍可用
            context = services.Input.PushContext("UI");
            if (menuRoot != null) menuRoot.SetActive(true);
        }
        /// <summary>
        /// 关闭菜单。
        /// <para>先清空字段再 Dispose：Dispose 过程可能触发其它回调，避免重入时重复释放。</para>
        /// </summary>
        public void Close()
        {
            var oldContext = context; var oldPause = pause; context = pause = null;
            oldContext?.Dispose(); oldPause?.Dispose();
            if (menuRoot != null) menuRoot.SetActive(false);
        }
        private void Update()
        {
            if (services == null) return;
            // 开着时用 UI 上下文的 Cancel 关闭；关着时用 Gameplay 的 Pause 打开
            if (IsOpen) { if (services.Input.GetButtonDown("Cancel", "UI")) Close(); }
            else if (services.Input.GetButtonDown("Pause")) Open();
        }
        /// <summary>禁用/销毁时兜底关闭，保证暂停凭据一定被释放。</summary>
        private void OnDisable() { Close(); }
    }
}
