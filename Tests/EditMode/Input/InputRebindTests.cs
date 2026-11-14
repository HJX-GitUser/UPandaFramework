using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// 0.8.2 运行时改键基础设施测试：冲突/保留键、捕获、重置、覆盖层导出/应用与 JSON 持久化。
    /// </summary>
    public sealed class InputRebindTests
    {
        private sealed class FakeBackend : IInputBackend
        {
            public HashSet<KeyCode> HeldKeys = new HashSet<KeyCode>();
            public KeyCode CaptureKey;
            public float Axis(string name) => 0f;
            public bool Held(KeyCode key) => HeldKeys.Contains(key);
            public bool Down(KeyCode key) => false;
            public bool Up(KeyCode key) => false;
            public bool TryCaptureKey(out KeyCode key)
            {
                key = CaptureKey;
                return CaptureKey != KeyCode.None;
            }
        }

        private static InputBinding Binding(string action, KeyCode positive, string context = InputContexts.Gameplay)
            => new InputBinding { action = action, context = context, positive = positive };

        [Test]
        public void TryRebindSucceedsAndRaisesEvent()
        {
            var backend = new FakeBackend();
            using (var service = new InputService(backend, null))
            {
                int events = 0;
                service.BindingsChanged += () => events++;
                var result = service.TryRebind(Binding("Jump", KeyCode.Q));
                Assert.AreEqual(InputRebindResult.Success, result);
                Assert.AreEqual(1, events);
                Assert.IsTrue(service.GetBindings().Any(x => x.action == "Jump" && x.positive == KeyCode.Q));
            }
        }

        [Test]
        public void TryRebindRejectsConflictInsideSameContext()
        {
            using (var service = new InputService(new FakeBackend(), null))
            {
                // Handbrake/Jump 默认都是 Space，新动作再绑 Space 必须冲突。
                var result = service.TryRebind(Binding("Custom", KeyCode.Space));
                Assert.AreEqual(InputRebindResult.Conflict, result);
                Assert.IsFalse(service.GetBindings().Any(x => x.action == "Custom"));
            }
        }

        [Test]
        public void TryRebindAllowsSameKeyAcrossContexts()
        {
            using (var service = new InputService(new FakeBackend(), null))
            {
                var result = service.TryRebind(Binding("Custom", KeyCode.Space, InputContexts.UI));
                Assert.AreEqual(InputRebindResult.Success, result);
            }
        }

        [Test]
        public void TryRebindRejectsReservedKeyUnlessAllowed()
        {
            using (var service = new InputService(new FakeBackend(), null))
            {
                Assert.AreEqual(InputRebindResult.ReservedKey, service.TryRebind(Binding("Custom", KeyCode.F1)));
                Assert.AreEqual(InputRebindResult.Success, service.TryRebind(Binding("Custom", KeyCode.F1), allowReservedKey: true));
            }
        }

        [Test]
        public void TryRebindRejectsInvalidBindings()
        {
            using (var service = new InputService(new FakeBackend(), null))
            {
                Assert.AreEqual(InputRebindResult.InvalidBinding, service.TryRebind(null));
                Assert.AreEqual(InputRebindResult.InvalidBinding, service.TryRebind(new InputBinding { action = "", positive = KeyCode.A }));
                Assert.AreEqual(InputRebindResult.InvalidBinding, service.TryRebind(new InputBinding { action = "A", axis = "Vertical", positive = KeyCode.A }));
                Assert.AreEqual(InputRebindResult.InvalidBinding, service.TryRebind(new InputBinding { action = "A" }));
                Assert.AreEqual(InputRebindResult.InvalidBinding, service.TryRebind(new InputBinding { action = "A", positive = KeyCode.A, negative = KeyCode.A }));
            }
        }

        [Test]
        public void UnbindRaisesEventAndResetRestoresDefaults()
        {
            using (var service = new InputService(new FakeBackend(), null))
            {
                Assert.IsTrue(service.TryRebind(Binding("Jump", KeyCode.Q)) == InputRebindResult.Success);
                Assert.IsTrue(service.Unbind("Jump"));
                Assert.IsFalse(service.GetBindings().Any(x => x.action == "Jump"));

                int events = 0;
                service.BindingsChanged += () => events++;
                Assert.IsTrue(service.ResetToDefaults());
                Assert.AreEqual(1, events);
                Assert.IsTrue(service.GetBindings().Any(x => x.action == "Jump" && x.positive == KeyCode.Space));
                Assert.AreEqual(service.GetDefaultBindings().Count, service.GetBindings().Count);
            }
        }

        [Test]
        public void ExportOverridesContainsOnlyChangedBindingsAndApplies()
        {
            var changed = new InputBinding { action = "Jump", context = InputContexts.Gameplay, positive = KeyCode.Q };
            using (var source = new InputService(new FakeBackend(), null))
            {
                Assert.AreEqual(InputRebindResult.Success, source.TryRebind(changed));
                var overrides = source.ExportOverrides();
                Assert.AreEqual(1, overrides.Length);
                Assert.AreEqual("Jump", overrides[0].action);
                Assert.AreEqual(KeyCode.Q, overrides[0].positive);

                var backend = new FakeBackend();
                using (var target = new InputService(backend, null))
                {
                    target.ApplyOverrides(overrides);
                    backend.HeldKeys.Add(KeyCode.Q);
                    Assert.IsTrue(target.GetButton("Jump"));
                    backend.HeldKeys.Clear();
                    backend.HeldKeys.Add(KeyCode.Space);
                    Assert.IsFalse(target.GetButton("Jump"), "Q 覆盖后 Space 不再触发 Jump");
                }
            }
        }

        [Test]
        public void CaptureUsesBackend()
        {
            var backend = new FakeBackend();
            using (var service = new InputService(backend, null))
            {
                Assert.IsFalse(service.TryCaptureKey(out _));
                backend.CaptureKey = KeyCode.Q;
                Assert.IsTrue(service.TryCaptureKey(out var key));
                Assert.AreEqual(KeyCode.Q, key);
            }
        }

        [Test]
        public void OverlayJsonRoundTrips()
        {
            var overlay = new InputBindingOverlay
            {
                bindings = new[] { new InputBinding { action = "Jump", positive = KeyCode.Q } }
            };
            string json = InputBindingOverlay.ToJson(overlay);
            var restored = InputBindingOverlay.FromJson(json);
            Assert.IsNotNull(restored);
            Assert.AreEqual(1, restored.bindings.Length);
            Assert.AreEqual("Jump", restored.bindings[0].action);
            Assert.AreEqual(KeyCode.Q, restored.bindings[0].positive);
            Assert.IsNull(InputBindingOverlay.FromJson(null));
        }

        [Test]
        public void OverlayStoreRoundTripsAndFallsBackToBackup()
        {
            string directory = Path.Combine(Application.temporaryCachePath, "input-overlay-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "input-bindings.json");
            try
            {
                var first = new InputBindingOverlay
                {
                    bindings = new[] { new InputBinding { action = "Jump", positive = KeyCode.Q } }
                };
                Assert.IsTrue(InputBindingOverlayStore.Save(path, first, out var error), error);

                var second = new InputBindingOverlay
                {
                    bindings = new[] { new InputBinding { action = "Jump", positive = KeyCode.E } }
                };
                Assert.IsTrue(InputBindingOverlayStore.Save(path, second, out error), error);

                var loaded = InputBindingOverlayStore.Load(path, out error);
                Assert.IsNotNull(loaded, error);
                Assert.AreEqual(KeyCode.E, loaded.bindings[0].positive);

                // 破坏主文件：应回退到上一次成功写入的 .bak。
                File.WriteAllText(path, "not-a-json");
                var fallback = InputBindingOverlayStore.Load(path, out error);
                Assert.IsNotNull(fallback, error);
                Assert.AreEqual(KeyCode.Q, fallback.bindings[0].positive);
                StringAssert.Contains("回退", error);
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        }
    }
}
