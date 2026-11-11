using System;
using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// UI 缩放适配：把设计分辨率按缩放系数换算成 CanvasScaler 的参考分辨率。
    /// <para>scale = 1 保持设计分辨率；0.8 时 UI 更小、1.3 时 UI 更大（参考分辨率按反比缩小）。
    /// 与 <see cref="UISafeArea"/> 搭配可覆盖“0.8–1.3 缩放 + 刘海/21:9”场景。</para>
    /// <para>只改 <see cref="CanvasScaler.referenceResolution"/> 与屏幕缩放模式，不动 match 模式与像素密度。</para>
    /// </summary>
    [RequireComponent(typeof(CanvasScaler))]
    [AddComponentMenu("UPandaGF/UI/Canvas Scale")]
    public sealed class UICanvasScale : MonoBehaviour
    {
        public const float MinScale = 0.5f;
        public const float MaxScale = 2f;
        private static readonly Vector2 FallbackResolution = new Vector2(1920f, 1080f);

        [SerializeField] private CanvasScaler scaler;
        [SerializeField, Range(MinScale, MaxScale)] private float scale = 1f;

        private Vector2 baseResolution;

        /// <summary>缩放系数；赋值后立即重算参考分辨率。</summary>
        public float Scale
        {
            get { return scale; }
            set
            {
                float clamped = ClampScale(value);
                if (Mathf.Approximately(scale, clamped)) return;
                scale = clamped;
                Apply();
            }
        }

        /// <summary>设计分辨率（scale = 1 时的参考分辨率）。</summary>
        public Vector2 ReferenceResolution
        {
            get { return baseResolution.sqrMagnitude > 0f ? baseResolution : FallbackResolution; }
            set
            {
                baseResolution = value.sqrMagnitude > 0f ? value : FallbackResolution;
                Apply();
            }
        }

        public static float ClampScale(float value)
        {
            return Mathf.Clamp(value, MinScale, MaxScale);
        }

        private void Awake()
        {
            if (scaler == null) scaler = GetComponent<CanvasScaler>();
            if (baseResolution.sqrMagnitude <= 0f) baseResolution = ResolveBaseResolution();
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

        /// <summary>立即按当前缩放重算参考分辨率；可在 CanvasScaler 配置变化后手动调用。</summary>
        public void Apply()
        {
            if (scaler == null) scaler = GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                Debug.LogError("[UICanvasScale] 所在节点缺少 CanvasScaler，无法应用 UI 缩放。");
                return;
            }

            if (baseResolution.sqrMagnitude <= 0f) baseResolution = ResolveBaseResolution();
            if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            float effective = ClampScale(scale);
            scaler.referenceResolution = new Vector2(
                Mathf.Max(1f, baseResolution.x / effective),
                Mathf.Max(1f, baseResolution.y / effective));
        }

        /// <summary>把缩放绑定到 Observable（例如设置项改变后自动生效）；记得 Dispose。</summary>
        public IDisposable Bind(FloatObservable source)
        {
            return Bind((IReadOnlyObservable<float>)source);
        }

        public IDisposable Bind(IReadOnlyObservable<float> source)
        {
            if (source == null) return null;
            return source.Subscribe(value => Scale = value, true);
        }

        private Vector2 ResolveBaseResolution()
        {
            if (scaler != null && scaler.referenceResolution.sqrMagnitude > 0f)
                return scaler.referenceResolution;
            return FallbackResolution;
        }
    }
}
