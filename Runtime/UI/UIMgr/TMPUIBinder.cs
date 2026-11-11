using System;
using TMPro;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// TextMeshPro 专用绑定。与 UIBinder 分开，避免不使用 TMP 的程序集
    /// 仅因引用 UIBinder 就被迫引用 Unity.TextMeshPro。
    /// </summary>
    public static class TMPUIBinder
    {
        public static IDisposable BindText(TMP_Text target, StringObservable source,
            Func<string, string> format = null)
        {
            return BindTextCore(target, source, format);
        }

        public static IDisposable BindText(TMP_Text target, IReadOnlyObservable<string> source,
            Func<string, string> format = null)
        {
            return BindTextCore(target, source, format);
        }

        public static IDisposable BindDropdown(TMP_Dropdown target, IntObservable source)
        {
            if (target == null || source == null) return null;

            target.SetValueWithoutNotify(source.Value);
            UnityAction<int> onUIChanged = value =>
            {
                if (source.Value != value) source.Value = value;
            };
            target.onValueChanged.AddListener(onUIChanged);

            CompositeDisposable binding = new CompositeDisposable();
            binding.Add(source.Subscribe(value => target.SetValueWithoutNotify(value)));
            binding.Add(new DisposableAction(() => target.onValueChanged.RemoveListener(onUIChanged)));
            return binding;
        }

        private static IDisposable BindTextCore(TMP_Text target, IReadOnlyObservable<string> source,
            Func<string, string> format)
        {
            if (target == null || source == null) return null;
            return source.Subscribe(value => target.text = format != null ? format(value) : value, true);
        }
    }
}
