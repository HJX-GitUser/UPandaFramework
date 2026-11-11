using UnityEngine;

namespace UPandaGF
{
    /// <summary>把场景相机注册给 UI 服务。由根节点注入 IUIService（不再访问静态单例）。</summary>
    public class UICameraRegister : MonoBehaviour, IUIServiceConsumer
    {
        public Camera _camera;
        private IUIService ui;
        private bool applied;

        private void Reset()
        {
            _camera = GetComponent<Camera>();
            if (GetComponent<Camera>() == null)
                Debug.LogError("Camera 组件不存在！！！");
        }

        private void Awake()
        {
            // Inspector 允许注册外部相机；只有未赋值时才取自身组件，避免覆盖手工配置。
            if (_camera == null) _camera = GetComponent<Camera>();
            if (_camera == null)
                Debug.LogError("Camera 组件不存在！！！");
        }

        public void ConfigureUi(IUIService service)
        {
            ui = service;
            Apply();
        }

        private void Start()
        {
            Apply();
        }

        /// <summary>只应用一次：重复 ResetCamera 会把刚注册的相机再 SetActive(false)。</summary>
        private void Apply()
        {
            if (applied || ui == null || _camera == null) return;
            applied = true;
            ui.ResetCamera(_camera);
        }
    }
}
