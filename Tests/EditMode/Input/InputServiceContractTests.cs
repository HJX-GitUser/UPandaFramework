using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// 0.8.2 契约回归：IInputService 的 Bind/Unbind/GetBindings/GetButtonUp
    /// 与上下文/暂停语义。使用 FakeBackend，不依赖真实设备。
    /// </summary>
    public sealed class InputServiceContractTests
    {
        private sealed class FakeBackend : IInputBackend
        {
            public HashSet<KeyCode> HeldKeys = new HashSet<KeyCode>();
            public HashSet<KeyCode> DownKeys = new HashSet<KeyCode>();
            public HashSet<KeyCode> UpKeys = new HashSet<KeyCode>();
            public float AxisValue;
            public KeyCode CaptureKey = KeyCode.None;

            public float Axis(string name) => AxisValue;
            public bool Held(KeyCode key) => HeldKeys.Contains(key);
            public bool Down(KeyCode key) => DownKeys.Contains(key);
            public bool Up(KeyCode key) => UpKeys.Contains(key);
            public bool TryCaptureKey(out KeyCode key)
            {
                key = CaptureKey;
                return CaptureKey != KeyCode.None;
            }
        }

        private sealed class FakePause : IPauseService
        {
            public bool IsPaused { get; set; }
            public IDisposable Pause(string reason = "Gameplay")
            {
                IsPaused = true;
                return new DisposableAction(() => IsPaused = false);
            }
        }

        private static InputBinding Bind(string action, KeyCode positive, string context = InputContexts.Gameplay)
            => new InputBinding { action = action, context = context, positive = positive };

        [Test]
        public void DefaultBindingsAreRegisteredWithExpectedShape()
        {
            using (var service = new InputService(new FakeBackend(), null))
            {
                var bindings = service.GetBindings();
                Assert.AreEqual(14, bindings.Count);
                Assert.IsTrue(bindings.Any(x => x.action == "MoveVertical" && x.axis == "Vertical"));
                Assert.IsTrue(bindings.Any(x => x.action == "Confirm" && x.context == InputContexts.UI));
                Assert.IsTrue(bindings.Any(x => x.action == "Cancel" && x.positive == KeyCode.Escape));
                Assert.IsTrue(bindings.Any(x => x.action == "NavigateVertical" && x.context == InputContexts.UI));
                Assert.AreEqual(14, service.GetDefaultBindings().Count);
            }
        }

        [Test]
        public void BindCopiesSnapshotAndOverridesSameAction()
        {
            var backend = new FakeBackend();
            using (var service = new InputService(backend, null, new InputBinding[0]))
            {
                var binding = Bind("Custom", KeyCode.Space);
                service.Bind(binding);
                binding.positive = KeyCode.None; // 外部修改不应影响内部

                backend.HeldKeys.Add(KeyCode.Space);
                Assert.IsTrue(service.GetButton("Custom"));

                service.Bind(Bind("Custom", KeyCode.E));
                Assert.IsFalse(service.GetButton("Custom"), "Space 已被 E 覆盖");
                backend.HeldKeys.Add(KeyCode.E);
                Assert.IsTrue(service.GetButton("Custom"));
                Assert.AreEqual(1, service.GetBindings().Count);
            }
        }

        [Test]
        public void GetButtonUpUsesBackendUp()
        {
            var backend = new FakeBackend();
            using (var service = new InputService(backend, null, new[] { Bind("Jump", KeyCode.Space) }))
            {
                Assert.IsFalse(service.GetButtonUp("Jump"));
                backend.UpKeys.Add(KeyCode.Space);
                Assert.IsTrue(service.GetButtonUp("Jump"));
                Assert.IsFalse(service.GetButtonDown("Jump"));
            }
        }

        [Test]
        public void UnbindRemovesAndReportsMissing()
        {
            using (var service = new InputService(new FakeBackend(), null, new[] { Bind("Custom", KeyCode.Space) }))
            {
                Assert.IsTrue(service.Unbind("Custom"));
                Assert.IsFalse(service.Unbind("Custom"));
                Assert.AreEqual(0, service.GetBindings().Count);
                Assert.IsFalse(service.GetButton("Custom"));
            }
        }

        [Test]
        public void GetBindingsReturnsSnapshotNotInternalState()
        {
            using (var service = new InputService(new FakeBackend(), null, new[] { Bind("Custom", KeyCode.Space) }))
            {
                var first = service.GetBindings();
                first[0].positive = KeyCode.E;
                var second = service.GetBindings();
                Assert.AreEqual(KeyCode.Space, second[0].positive);
            }
        }

        [Test]
        public void UiContextBlocksGameplayAndReleaseRestores()
        {
            var backend = new FakeBackend();
            backend.DownKeys.Add(KeyCode.Return);
            backend.HeldKeys.Add(KeyCode.Space);
            using (var service = new InputService(backend, null))
            {
                Assert.IsTrue(service.GetButtonDown("Confirm", InputContexts.UI));

                var ui = service.PushContext(InputContexts.UI);
                Assert.IsFalse(service.GetButton("Jump"), "UI 压栈后 Gameplay 应被阻断");
                Assert.IsTrue(service.GetButtonDown("Confirm", InputContexts.UI), "UI 动作仍应可用");

                ui.Dispose();
                Assert.IsTrue(service.GetButton("Jump"), "释放 UI 后 Gameplay 恢复");
            }
        }

        [Test]
        public void PauseBlocksOnlyGameplayContext()
        {
            var backend = new FakeBackend();
            backend.HeldKeys.Add(KeyCode.Space);
            backend.DownKeys.Add(KeyCode.Return);
            var pause = new FakePause { IsPaused = true };
            using (var service = new InputService(backend, pause))
            {
                Assert.IsFalse(service.GetButton("Jump"));
                Assert.IsTrue(service.GetButtonDown("Confirm", InputContexts.UI));
            }
        }

        [Test]
        public void BindRejectsInvalidArguments()
        {
            using (var service = new InputService(new FakeBackend(), null, new InputBinding[0]))
            {
                Assert.Throws<ArgumentException>(() => service.Bind(null));
                Assert.Throws<ArgumentException>(() => service.Bind(new InputBinding { action = "", positive = KeyCode.Space }));
                Assert.Throws<ArgumentException>(() => service.Bind(new InputBinding { action = "A", context = " " }));
                Assert.Throws<ArgumentNullException>(() => new InputService(null, null));
            }
        }

        [Test]
        public void DisposeSemanticsAreExplicit()
        {
            var service = new InputService(new FakeBackend(), null, new[] { Bind("Custom", KeyCode.Space) });
            service.Dispose();
            service.Dispose(); // 幂等

            Assert.AreEqual(0, service.GetAxis("Custom"));
            Assert.IsFalse(service.GetButton("Custom"));
            Assert.IsFalse(service.GetButtonDown("Custom"));
            Assert.IsFalse(service.GetButtonUp("Custom"));
            Assert.AreEqual(0, service.GetBindings().Count);
            Assert.Throws<ObjectDisposedException>(() => service.Bind(Bind("A", KeyCode.A)));
            Assert.Throws<ObjectDisposedException>(() => service.Unbind("Custom"));
            Assert.Throws<ObjectDisposedException>(() => service.PushContext(InputContexts.UI));
        }

        [Test]
        public void ContextConstantsMatchLegacyValues()
        {
            Assert.AreEqual("Gameplay", InputContexts.Gameplay);
            Assert.AreEqual("UI", InputContexts.UI);
            Assert.AreEqual("Rebind", InputContexts.Rebind);
            Assert.AreEqual("Cutscene", InputContexts.Cutscene);
        }
    }

    /// <summary>
    /// 输入配置校验：New 后端下不支持的轴/按键必须 fail-fast；
    /// 与后端无关的规则（axis 与按键互斥、空数组）在所有工程生效。
    /// </summary>
    public sealed class InputConfigValidationTests
    {
        private static List<string> Validate(UPGameRootConfig config) => UPGameRootConfigFile.Validate(config, true);

        [Test]
        public void DefaultConfigPassesOnCurrentBackend()
        {
            var errors = Validate(new UPGameRootConfig());
            CollectionAssert.IsEmpty(errors);
        }

        [Test]
        public void AxisAndKeyBindingsAreMutuallyExclusive()
        {
            var config = new UPGameRootConfig();
            config.inputBindings[0].axis = "Vertical";
            config.inputBindings[0].positive = KeyCode.Space;
            var errors = Validate(config);
            Assert.IsTrue(errors.Exists(e => e.Contains("二选一")), string.Join("\n", errors));
        }

        [Test]
        public void EmptyBindingArrayIsRejected()
        {
            var config = new UPGameRootConfig { inputBindings = new InputBinding[0] };
            var errors = Validate(config);
            Assert.IsTrue(errors.Exists(e => e.Contains("空数组")), string.Join("\n", errors));
        }

#if ENABLE_INPUT_SYSTEM
        [Test]
        public void NewBackendRejectsUnsupportedAxis()
        {
            var config = new UPGameRootConfig();
            config.inputBindings[0].axis = "CustomAxis";
            config.inputBindings[0].positive = KeyCode.None;
            var errors = Validate(config);
            Assert.IsTrue(errors.Exists(e => e.Contains("不支持的轴名")), string.Join("\n", errors));
        }

        [Test]
        public void NewBackendRejectsUnsupportedKey()
        {
            var config = new UPGameRootConfig();
            config.inputBindings[2].positive = KeyCode.F13; // Handbrake
            var errors = Validate(config);
            Assert.IsTrue(errors.Exists(e => e.Contains("不支持的按键")), string.Join("\n", errors));
        }
#endif
    }
}
