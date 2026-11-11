using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UPandaGF;

namespace UPandaGF.Tests
{
    public class UIEventScopeTests
    {
        private struct TestMessage { public int value; }

        private sealed class Listener : IEventListener<TestMessage>
        {
            public int Count;
            public void OnEvent(TestMessage message) { Count++; }
        }

        private sealed class BindingPanel : BasePanel
        {
            public int Clicks;
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
            protected override void Button_OnClick(string btnName) { Clicks++; }
        }

        [Test]
        public void UnityEventIsRemovedWhenScopeDisposed()
        {
            UnityEvent evt = new UnityEvent();
            int count = 0;
            UIEventScope scope = new UIEventScope();
            scope.Listen(evt, () => count++);

            evt.Invoke();
            scope.Dispose();
            evt.Invoke();

            Assert.AreEqual(1, count);
            Assert.IsTrue(scope.IsDisposed);
        }

        [Test]
        public void EventBusSubscriptionIsRemovedWhenScopeDisposed()
        {
            EventBus bus = new EventBus();
            Listener listener = new Listener();
            UIEventScope scope = new UIEventScope();
            scope.Subscribe(bus, listener);

            bus.Dispatch(new TestMessage { value = 1 });
            scope.Dispose();
            bus.Dispatch(new TestMessage { value = 2 });

            Assert.AreEqual(1, listener.Count);
            Assert.AreEqual(0, bus.GetSubscriptionCount<TestMessage>());
        }

        [Test]
        public void BasePanelBindingIsReleasedWithEventScope()
        {
            GameObject root = new GameObject("BindingPanel", typeof(RectTransform));
            GameObject buttonObject = new GameObject("Btn", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(root.transform, false);
            try
            {
                BindingPanel panel = root.AddComponent<BindingPanel>();
                Button button = buttonObject.GetComponent<Button>();

                button.onClick.Invoke();
                Assert.AreEqual(1, panel.Clicks);

                panel.DisposeEventScopes();
                button.onClick.Invoke();
                Assert.AreEqual(1, panel.Clicks);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
