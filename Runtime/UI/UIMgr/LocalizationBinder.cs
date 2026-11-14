using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 代码方式绑定本地化文本：立即赋值，并在语言变化时刷新；返回 IDisposable，建议加入 BasePanel.OpenEvents。
    /// </summary>
    public static class LocalizationBinder
    {
        public static IDisposable Bind(ILocalizationService localization, Text target, string key, params object[] args)
        {
            if (localization == null || target == null || string.IsNullOrEmpty(key)) return null;
            Apply(localization, target, key, args);
            Action<LocalizationChange> listener = change => Apply(localization, target, key, args);
            localization.LocaleChanged += listener;
            return new DisposableAction(() => localization.LocaleChanged -= listener);
        }

        public static IDisposable Bind(ILocalizationService localization, TMP_Text target, string key, params object[] args)
        {
            if (localization == null || target == null || string.IsNullOrEmpty(key)) return null;
            Apply(localization, target, key, args);
            Action<LocalizationChange> listener = change => Apply(localization, target, key, args);
            localization.LocaleChanged += listener;
            return new DisposableAction(() => localization.LocaleChanged -= listener);
        }

        private static void Apply(ILocalizationService localization, Text target, string key, object[] args)
        {
            target.text = args != null && args.Length > 0
                ? localization.Format(key, args)
                : localization.Get(key);
        }

        private static void Apply(ILocalizationService localization, TMP_Text target, string key, object[] args)
        {
            target.text = args != null && args.Length > 0
                ? localization.Format(key, args)
                : localization.Get(key);
        }
    }
}
