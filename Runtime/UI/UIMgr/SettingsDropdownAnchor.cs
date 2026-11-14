using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 下拉模板现在挂在 ScrollRect 外的 overlay 上；点击下拉框时把模板移动到下拉框下方，
    /// uGUI Dropdown 随后克隆模板到 overlay，列表不会再被 viewport 遮罩裁剪。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsDropdownAnchor : MonoBehaviour, IPointerDownHandler
    {
        private Dropdown dropdown;
        private RectTransform template;
        private RectTransform overlay;

        public void Bind(Dropdown value, RectTransform templateRect, RectTransform overlayRect)
        {
            dropdown = value;
            template = templateRect;
            overlay = overlayRect;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            PositionTemplate();
        }

        /// <summary>键盘/手柄打开前也可由面板调用。</summary>
        public void PositionTemplate()
        {
            if (dropdown == null || template == null || overlay == null) return;
            RectTransform dropdownRect = (RectTransform)dropdown.transform;
            var corners = new Vector3[4];
            dropdownRect.GetWorldCorners(corners);

            Vector3 bottomLeft = overlay.InverseTransformPoint(corners[0]);
            template.anchorMin = new Vector2(0.5f, 0.5f);
            template.anchorMax = new Vector2(0.5f, 0.5f);
            template.pivot = new Vector2(0f, 1f);
            template.anchoredPosition = new Vector2(bottomLeft.x, bottomLeft.y - 4f);
            template.sizeDelta = new Vector2(dropdownRect.rect.width, 160f);
        }
    }
}
