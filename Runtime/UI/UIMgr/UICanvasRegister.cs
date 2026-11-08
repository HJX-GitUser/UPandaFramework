using UnityEngine;

namespace UPandaGF
{
    /// <summary>把场景 Canvas 注册给 UI 服务。由根节点注入 IUIService（不再访问静态单例）。</summary>
    public class UICanvasRegister : MonoBehaviour, IUIServiceConsumer
    {
        public Canvas canvas;
        private IUIService ui;
        private bool applied;

        private void Reset()
        {
            canvas = GetComponent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("canvas 组件不存在！！！");
                return;
            }
            RectTransform canvasRect = canvas.GetComponent<RectTransform>();
            RectTransform Bot = (transform.Find("Bot") == null) ? new GameObject("Bot", typeof(RectTransform)).GetComponent<RectTransform>() : transform.Find("Bot") as RectTransform;
            RectTransform Mid = (transform.Find("Mid") == null) ? new GameObject("Mid", typeof(RectTransform)).GetComponent<RectTransform>() : transform.Find("Mid") as RectTransform;
            RectTransform Top = (transform.Find("Top") == null) ? new GameObject("Top", typeof(RectTransform)).GetComponent<RectTransform>() : transform.Find("Top") as RectTransform;
            RectTransform System = (transform.Find("System") == null) ? new GameObject("System", typeof(RectTransform)).GetComponent<RectTransform>() : transform.Find("System") as RectTransform;
            SetRect(Bot, canvasRect);
            SetRect(Mid, canvasRect);
            SetRect(Top, canvasRect);
            SetRect(System, canvasRect);
        }

        private void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (canvas == null)
                Debug.LogError("canvas 组件不存在！！！");
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

        /// <summary>只应用一次：重复 ResetCanvas 会把刚注册的 Canvas 再 SetActive(false)。</summary>
        private void Apply()
        {
            if (applied || ui == null || canvas == null) return;
            applied = true;
            ui.ResetCanvas(canvas);
        }

        private void SetRect(RectTransform rect, RectTransform parent)
        {
            rect.SetParent(parent);
            rect.localPosition = Vector3.zero;
            rect.localScale = Vector3.one;
            rect.gameObject.layer = LayerMask.NameToLayer("UI");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.zero;
        }
    }
}
