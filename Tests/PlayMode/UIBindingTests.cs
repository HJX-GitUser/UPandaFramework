using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UPandaGF;

namespace UPandaGF.Tests
{
    public class ObservableTests
    {
        [Test]
        public void ValueChangePushesOnlyWhenChanged()
        {
            BoolObservable source = new BoolObservable(false);
            int calls = 0;
            IDisposable subscription = source.Subscribe(value => calls++);

            source.Value = false;
            source.Value = true;
            source.Value = true;

            Assert.AreEqual(1, calls);
            subscription.Dispose();
            source.Value = false;
            Assert.AreEqual(1, calls);
        }

        [Test]
        public void SubscribeCanInvokeCurrentValue()
        {
            StringObservable source = new StringObservable("current");
            string received = null;
            IDisposable subscription = source.Subscribe(value => received = value, true);

            Assert.AreEqual("current", received);
            subscription.Dispose();
        }

        [Test]
        public void DisposedObservableStopsNotifying()
        {
            FloatObservable source = new FloatObservable(1f);
            int calls = 0;
            source.Subscribe(value => calls++);
            source.Dispose();
            source.Value = 2f;
            Assert.AreEqual(0, calls);
        }

        [Test]
        public void CompositeDisposableDisposesInReverseOrderAndIsIdempotent()
        {
            int order = 0;
            int first = 0;
            int second = 0;
            CompositeDisposable bag = new CompositeDisposable();
            bag.Add(new DisposableAction(() => first = ++order));
            bag.Add(new DisposableAction(() => second = ++order));

            bag.Dispose();
            bag.Dispose();

            Assert.AreEqual(1, second);
            Assert.AreEqual(2, first);
        }
    }

    public class UIBinderTests
    {
        private GameObject Create(string name, params Type[] components)
        {
            return new GameObject(name, components);
        }

        [Test]
        public void TextBindingUpdatesAndDisposes()
        {
            GameObject go = Create("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            try
            {
                Text text = go.GetComponent<Text>();
                StringObservable source = new StringObservable("a");
                IDisposable binding = UIBinder.BindText(text, source);

                Assert.AreEqual("a", text.text);
                source.Value = "b";
                Assert.AreEqual("b", text.text);

                binding.Dispose();
                source.Value = "c";
                Assert.AreEqual("b", text.text);
                source.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ToggleBindingIsTwoWayAndDisposes()
        {
            GameObject go = Create("Toggle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Toggle));
            try
            {
                Toggle toggle = go.GetComponent<Toggle>();
                BoolObservable source = new BoolObservable(false);
                IDisposable binding = UIBinder.BindToggle(toggle, source);

                source.Value = true;
                Assert.IsTrue(toggle.isOn);
                toggle.isOn = false;
                Assert.IsFalse(source.Value);

                binding.Dispose();
                source.Value = true;
                Assert.IsFalse(toggle.isOn);
                source.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SliderBindingIsTwoWay()
        {
            GameObject go = Create("Slider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Slider));
            try
            {
                Slider slider = go.GetComponent<Slider>();
                slider.minValue = 0f;
                slider.maxValue = 10f;
                FloatObservable source = new FloatObservable(2f);
                IDisposable binding = UIBinder.BindSlider(slider, source);

                Assert.AreEqual(2f, slider.value, 0.001f);
                source.Value = 7f;
                Assert.AreEqual(7f, slider.value, 0.001f);
                slider.value = 4f;
                Assert.AreEqual(4f, source.Value, 0.001f);
                binding.Dispose();
                source.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void InteractableAndActiveBindingsFollowSource()
        {
            GameObject buttonObject = Create("Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Button));
            GameObject panel = Create("Panel", typeof(RectTransform));
            try
            {
                Button button = buttonObject.GetComponent<Button>();
                BoolObservable interactable = new BoolObservable(true);
                BoolObservable active = new BoolObservable(true);
                IDisposable a = UIBinder.BindInteractable(button, interactable);
                IDisposable b = UIBinder.BindActive(panel, active);

                interactable.Value = false;
                active.Value = false;
                Assert.IsFalse(button.interactable);
                Assert.IsFalse(panel.activeSelf);

                a.Dispose();
                b.Dispose();
                interactable.Dispose();
                active.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(buttonObject);
                UnityEngine.Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void SafeAreaAppliesScreenSafeArea()
        {
            GameObject canvasObject = Create("Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject safeObject = Create("Safe", typeof(RectTransform));
            try
            {
                safeObject.transform.SetParent(canvasObject.transform, false);
                UISafeArea safeArea = safeObject.AddComponent<UISafeArea>();
                safeArea.Apply();

                Rect safe = Screen.safeArea;
                RectTransform rect = safeObject.GetComponent<RectTransform>();
                Assert.AreEqual(safe.xMin / Screen.width, rect.anchorMin.x, 0.001f);
                Assert.AreEqual(safe.yMin / Screen.height, rect.anchorMin.y, 0.001f);
                Assert.AreEqual(safe.xMax / Screen.width, rect.anchorMax.x, 0.001f);
                Assert.AreEqual(safe.yMax / Screen.height, rect.anchorMax.y, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }
    }
}
