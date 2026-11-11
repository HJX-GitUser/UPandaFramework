using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// 常用 uGUI/TMP 单向/双向绑定。绑定返回 IDisposable，建议加入 BasePanel.OpenEvents。
    /// 具体 Observable 类型（Bool/Float/Int/String）对热更新 AOT 更友好。
    /// </summary>
    public static class UIBinder
    {
        public static IDisposable AddTo(UIEventScope scope, IDisposable binding)
        {
            scope?.Add(binding);
            return binding;
        }

        public static IDisposable BindText(Text target, StringObservable source,
            Func<string, string> format = null)
        {
            return BindTextCore(target, source, format);
        }

        public static IDisposable BindText(Text target, IReadOnlyObservable<string> source,
            Func<string, string> format = null)
        {
            return BindTextCore(target, source, format);
        }

        public static IDisposable BindToggle(Toggle target, BoolObservable source)
        {
            if (target == null || source == null) return null;

            target.SetIsOnWithoutNotify(source.Value);
            UnityAction<bool> onUIChanged = value =>
            {
                if (source.Value != value) source.Value = value;
            };
            target.onValueChanged.AddListener(onUIChanged);

            CompositeDisposable binding = new CompositeDisposable();
            binding.Add(source.Subscribe(value => target.SetIsOnWithoutNotify(value)));
            binding.Add(new DisposableAction(() => target.onValueChanged.RemoveListener(onUIChanged)));
            return binding;
        }

        public static IDisposable BindSlider(Slider target, FloatObservable source)
        {
            if (target == null || source == null) return null;

            target.SetValueWithoutNotify(source.Value);
            UnityAction<float> onUIChanged = value =>
            {
                if (!Mathf.Approximately(source.Value, value)) source.Value = value;
            };
            target.onValueChanged.AddListener(onUIChanged);

            CompositeDisposable binding = new CompositeDisposable();
            binding.Add(source.Subscribe(value => target.SetValueWithoutNotify(value)));
            binding.Add(new DisposableAction(() => target.onValueChanged.RemoveListener(onUIChanged)));
            return binding;
        }

        public static IDisposable BindDropdown(Dropdown target, IntObservable source)
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

        public static IDisposable BindInteractable(Selectable target, BoolObservable source)
        {
            if (target == null || source == null) return null;
            return source.Subscribe(value => target.interactable = value, true);
        }

        public static IDisposable BindActive(GameObject target, BoolObservable source)
        {
            if (target == null || source == null) return null;
            return source.Subscribe(value => target.SetActive(value), true);
        }

        public static IDisposable BindColor(Graphic target, ColorObservable source)
        {
            if (target == null || source == null) return null;
            return source.Subscribe(value => target.color = value, true);
        }

        public static IDisposable BindButton(Button target, Action callback)
        {
            if (target == null || callback == null) return null;
            UnityAction listener = () => callback();
            target.onClick.AddListener(listener);
            return new DisposableAction(() => target.onClick.RemoveListener(listener));
        }

        private static IDisposable BindTextCore(Text target, IReadOnlyObservable<string> source,
            Func<string, string> format)
        {
            if (target == null || source == null) return null;
            return source.Subscribe(value => target.text = Format(value, format), true);
        }

        private static string Format(string value, Func<string, string> format)
        {
            return format != null ? format(value) : value;
        }
    }
}
