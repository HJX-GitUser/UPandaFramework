using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// uGUI Text 本地化组件：填 key，语言切换时自动刷新；由 <see cref="IGameServicesConsumer"/> 注入服务。
    /// <para>也可在代码里调用 <see cref="Bind(ILocalizationService)"/>。字体为空/内置时使用
    /// <see cref="UnityBuiltinFont.GetDefault"/>，避免中文缺字形。</para>
    /// </summary>
    [AddComponentMenu("UPandaGF/Localization/Localized Text")]
    public sealed class LocalizedText : MonoBehaviour, IGameServicesConsumer
    {
        [SerializeField] private Text target;
        [SerializeField] private string key;
        [Tooltip("可选格式化参数；对应 string.Format 的 {0}、{1}…")]
        [SerializeField] private string[] formatArgs;
        [SerializeField] private bool autoRefreshOnLocale = true;

        private ILocalizationService localization;

        public string Key
        {
            get { return key; }
            set { key = value; Refresh(); }
        }

        public void Configure(IGameServices services)
        {
            Bind(services != null ? services.Localization : null);
        }

        public void Bind(ILocalizationService value)
        {
            Unsubscribe();
            localization = value;
            Subscribe();
            Refresh();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        private void OnDisable() { Unsubscribe(); }

        private void OnDestroy() { Unsubscribe(); }

        private void Subscribe()
        {
            if (!autoRefreshOnLocale || localization == null) return;
            localization.LocaleChanged -= OnLocaleChanged;
            localization.LocaleChanged += OnLocaleChanged;
        }

        private void Unsubscribe()
        {
            if (localization == null) return;
            localization.LocaleChanged -= OnLocaleChanged;
        }

        private void OnLocaleChanged(LocalizationChange change) { Refresh(); }

        public void Refresh()
        {
            if (target == null) target = GetComponent<Text>();
            if (target == null || string.IsNullOrEmpty(key)) return;

            target.text = localization != null
                ? (formatArgs != null && formatArgs.Length > 0
                    ? localization.Format(key, formatArgs)
                    : localization.Get(key))
                : key;

            if (target.font == null || UnityBuiltinFont.IsBuiltin(target.font))
                target.font = UnityBuiltinFont.GetDefault();
        }
    }
}
