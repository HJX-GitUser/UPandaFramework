using NUnit.Framework;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// 输入后端选择：工程启用新 Input System（Active Input Handling 含 New）时应使用
    /// <see cref="InputSystemBackend"/>，否则回退 <see cref="UnityLegacyInputBackend"/>。
    /// 该用例在两种工程设置下都能跑，用来防止条件编译分支失效。
    /// </summary>
    public sealed class InputSystemIntegrationTests
    {
        [Test]
        public void FactorySelectsBackendMatchingProjectSettings()
        {
            IInputBackend backend = InputBackendFactory.Create();
            Assert.IsNotNull(backend);
#if ENABLE_INPUT_SYSTEM
            Assert.AreEqual("InputSystemBackend", backend.GetType().Name);
            Assert.IsTrue(InputBackendFactory.UsesInputSystemBackend);
            Assert.IsTrue(InputBackendFactory.SupportsAxis("Vertical"));
            Assert.IsFalse(InputBackendFactory.SupportsAxis("CustomAxis"));
            Assert.IsTrue(InputBackendFactory.SupportsKey(UnityEngine.KeyCode.Alpha1));
            Assert.IsFalse(InputBackendFactory.SupportsKey(UnityEngine.KeyCode.F13));
#else
            Assert.AreEqual("UnityLegacyInputBackend", backend.GetType().Name);
            Assert.IsFalse(InputBackendFactory.UsesInputSystemBackend);
            // 旧后端把合法性交给引擎/InputManager，编译期一律报告“支持”。
            Assert.IsTrue(InputBackendFactory.SupportsAxis("CustomAxis"));
            Assert.IsTrue(InputBackendFactory.SupportsKey(UnityEngine.KeyCode.F13));
#endif
        }
    }
}
