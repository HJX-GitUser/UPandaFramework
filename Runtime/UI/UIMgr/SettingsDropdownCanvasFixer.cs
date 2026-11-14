using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 把 overlay 里的 Dropdown List Canvas 对齐到根 Canvas 的相机/平面距离。
    /// 嵌套 Canvas 在 Screen Space Camera 模式下可能拿不到 worldCamera：Scene 视图可见、Game 视图不可见。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SettingsDropdownCanvasFixer : MonoBehaviour
    {
        private void Update()
        {
            Canvas root = GetComponentInParent<Canvas>();
            if (root == null) return;
            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);
            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null || !canvas.gameObject.activeInHierarchy) continue;
                if (canvas.worldCamera != root.worldCamera) canvas.worldCamera = root.worldCamera;
                if (!Mathf.Approximately(canvas.planeDistance, root.planeDistance))
                    canvas.planeDistance = root.planeDistance;
                if (canvas.sortingLayerID != root.sortingLayerID) canvas.sortingLayerID = root.sortingLayerID;
                if (canvas.gameObject.layer != root.gameObject.layer) canvas.gameObject.layer = root.gameObject.layer;
            }
        }
    }
}
