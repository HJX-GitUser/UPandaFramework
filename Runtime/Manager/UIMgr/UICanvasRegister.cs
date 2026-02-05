using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace UPandaGF
{
    public class UICanvasRegister : MonoBehaviour
    {
        public Canvas canvas;
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
        void Awake()
        {
            canvas = GetComponent<Canvas>();
            if (canvas == null)
                Debug.LogError("canvas 组件不存在！！！");
        }

        private void Start()
        {
            UIManager.Instance.ResetCanvas(canvas);
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

