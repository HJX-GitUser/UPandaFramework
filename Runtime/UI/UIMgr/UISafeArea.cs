using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 把 RectTransform 锚定到 Screen.safeArea，适配刘海/圆角/横竖屏。
    /// 放在需要安全区适配的 UI 根节点；每帧仅在 safeArea 变化时重算。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("UPandaGF/UI/Safe Area")]
    public sealed class UISafeArea : MonoBehaviour
    {
        private RectTransform rect;
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        private void Awake()
        {
            rect = transform as RectTransform;
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        private void Update()
        {
            Rect safe = Screen.safeArea;
            Vector2Int screen = new Vector2Int(Screen.width, Screen.height);
            if (safe != lastSafeArea || screen != lastScreen) Apply();
        }

        /// <summary>立即应用当前安全区；可在分辨率/方向变化后手动调用。</summary>
        public void Apply()
        {
            if (rect == null) rect = transform as RectTransform;
            if (rect == null) return;

            int width = Screen.width;
            int height = Screen.height;
            if (width <= 0 || height <= 0) return;

            Rect safe = Screen.safeArea;
            lastSafeArea = safe;
            lastScreen = new Vector2Int(width, height);

            Vector2 min = safe.position;
            Vector2 max = safe.position + safe.size;
            min.x /= width;
            max.x /= width;
            min.y /= height;
            max.y /= height;

            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
