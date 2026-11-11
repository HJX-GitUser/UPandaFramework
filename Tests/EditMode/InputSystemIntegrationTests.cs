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
#else
            Assert.AreEqual("UnityLegacyInputBackend", backend.GetType().Name);
#endif
        }
    }
}
