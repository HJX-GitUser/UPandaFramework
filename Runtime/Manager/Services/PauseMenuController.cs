using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>可复用暂停菜单行为；在场景中挂载并绑定现有 menuRoot，支持嵌套暂停。</summary>
    public sealed class PauseMenuController : MonoBehaviour, IGameServicesConsumer
    {
        [SerializeField] private GameObject menuRoot;
        private IGameServices services;
        private IDisposable pause, context;
        public bool IsOpen => pause != null;
        public void Configure(IGameServices value) { Close(); services = value; }
        public void Open()
        {
            if (services == null || IsOpen) return;
            pause = services.Pause.Pause("Menu");
            context = services.Input.PushContext("UI");
            if (menuRoot != null) menuRoot.SetActive(true);
        }
        public void Close()
        {
            var oldContext = context; var oldPause = pause; context = pause = null;
            oldContext?.Dispose(); oldPause?.Dispose();
            if (menuRoot != null) menuRoot.SetActive(false);
        }
        private void Update()
        {
            if (services == null) return;
            if (IsOpen) { if (services.Input.GetButtonDown("Cancel", "UI")) Close(); }
            else if (services.Input.GetButtonDown("Pause")) Open();
        }
        private void OnDisable() { Close(); }
    }
}
