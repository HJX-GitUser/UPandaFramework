using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 把 <see cref="IInputService"/> 的 UI 上下文动作映射到 <see cref="UIFocusNavigator"/>：
    /// 方向键/摇杆移动焦点、Confirm 提交、Cancel 返回，并在按住方向时自动重复。
    /// <para>框架只提供绑定组件而非全局输入路由：只有挂在面板/菜单根节点上的实例才会处理输入，
    /// 避免多个面板抢焦点。挂载后由 <see cref="IGameServicesConsumer.Configure"/> 注入输入服务。</para>
    /// <para>默认动作名：NavigateVertical/NavigateHorizontal（UI 上下文轴）、Confirm、Cancel。</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UPandaGF/UI/Focus Input Binder")]
    public sealed class UIFocusInputBinder : MonoBehaviour, IGameServicesConsumer
    {
        [Tooltip("要驱动的焦点导航器；留空则从自身/子节点查找。")]
        [SerializeField] private UIFocusNavigator navigator;
        [Tooltip("方向轴超过该阈值才算一次导航。")]
        [SerializeField] private float axisThreshold = 0.5f;
        [Tooltip("首次按住方向后，开始重复导航前的延迟（秒，unscaled）。")]
        [SerializeField] private float repeatDelay = 0.4f;
        [Tooltip("按住方向重复导航的间隔（秒，unscaled）。")]
        [SerializeField] private float repeatInterval = 0.12f;
        [SerializeField] private string verticalAction = "NavigateVertical";
        [SerializeField] private string horizontalAction = "NavigateHorizontal";
        [SerializeField] private string submitAction = "Confirm";
        [SerializeField] private string cancelAction = "Cancel";

        private IInputService input;
        private Vector2 lastDirection;
        private float nextRepeatTime;

        /// <summary>由 UPGameRoot/GameServices 注入；未注入时 Update 不处理。</summary>
        public void Configure(IGameServices services)
        {
            input = services?.Input;
            if (navigator == null) ResolveNavigator();
        }

        private void Awake() => ResolveNavigator();

        private void Update()
        {
            if (input == null || navigator == null) return;

            if (input.GetButtonDown(submitAction, InputContexts.UI)) navigator.Submit();
            if (input.GetButtonDown(cancelAction, InputContexts.UI)) navigator.Cancel();

            float vertical = input.GetAxis(verticalAction, InputContexts.UI);
            float horizontal = input.GetAxis(horizontalAction, InputContexts.UI);
            var direction = new Vector2(
                Mathf.Abs(horizontal) >= axisThreshold ? Mathf.Sign(horizontal) : 0f,
                Mathf.Abs(vertical) >= axisThreshold ? Mathf.Sign(vertical) : 0f);

            if (direction == Vector2.zero)
            {
                lastDirection = Vector2.zero;
                return;
            }

            bool first = lastDirection == Vector2.zero || direction != lastDirection;
            if (!first && Time.unscaledTime < nextRepeatTime) return;

            navigator.NavigateOrAdjust(direction);
            lastDirection = direction;
            nextRepeatTime = Time.unscaledTime + (first ? repeatDelay : repeatInterval);
        }

        private void ResolveNavigator()
        {
            if (navigator != null) return;
            navigator = GetComponent<UIFocusNavigator>();
            if (navigator == null) navigator = GetComponentInChildren<UIFocusNavigator>(true);
        }
    }
}
