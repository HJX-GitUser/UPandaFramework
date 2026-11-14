using TMPro;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// TMP 文本本地化组件。P0 只替换文本，不自动换字体；宿主需给 TMP 字体配置 zh/en fallback。
    /// </summary>
    [AddComponentMenu("UPandaGF/Localization/Localized TMP Text")]
    public sealed class LocalizedTMPText : MonoBehaviour, IGameServicesConsumer
    {
        [SerializeField] private TMP_Text target;
        [SerializeField] private string key;
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
            if (target == null) target = GetComponent<TMP_Text>();
            if (target == null || string.IsNullOrEmpty(key)) return;

            target.text = localization != null
                ? (formatArgs != null && formatArgs.Length > 0
                    ? localization.Format(key, formatArgs)
                    : localization.Get(key))
                : key;
        }
    }
}
