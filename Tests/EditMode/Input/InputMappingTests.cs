#if ENABLE_INPUT_SYSTEM
using System;
using NUnit.Framework;
using UnityEngine;
using UPandaGF;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>
    /// 0.8.2 映射回归：防止 InputSystemBackend 的 KeyCode→Key 映射再次错位
    /// （0.8.1 的 Alpha1–Alpha9 → LeftShift…ContextMenu 缺陷）。
    /// <para>不直接引用 Unity.InputSystem 类型：通过反射读取 Key 枚举的期望值，
    /// 这样同一测试程序集在 Old-only 工程也能编译（整段被 #if 排除）。</para>
    /// </summary>
    public sealed class InputMappingTests
    {
        private static Type keyType;

        private static int ExpectedKey(string name)
        {
            if (keyType == null)
            {
                keyType = Type.GetType("UnityEngine.InputSystem.Key, Unity.InputSystem");
                if (keyType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        keyType = assembly.GetType("UnityEngine.InputSystem.Key");
                        if (keyType != null) break;
                    }
                }
            }
            Assert.IsNotNull(keyType, "未找到 UnityEngine.InputSystem.Key：请确认工程启用了新 Input System");
            return (int)Enum.Parse(keyType, name);
        }

        [TestCase(KeyCode.A, "A")]
        [TestCase(KeyCode.M, "M")]
        [TestCase(KeyCode.Z, "Z")]
        [TestCase(KeyCode.F1, "F1")]
        [TestCase(KeyCode.F12, "F12")]
        [TestCase(KeyCode.Space, "Space")]
        [TestCase(KeyCode.Return, "Enter")]
        [TestCase(KeyCode.KeypadEnter, "NumpadEnter")]
        [TestCase(KeyCode.Escape, "Escape")]
        [TestCase(KeyCode.Tab, "Tab")]
        [TestCase(KeyCode.Backspace, "Backspace")]
        [TestCase(KeyCode.Delete, "Delete")]
        [TestCase(KeyCode.LeftShift, "LeftShift")]
        [TestCase(KeyCode.RightShift, "RightShift")]
        [TestCase(KeyCode.LeftControl, "LeftCtrl")]
        [TestCase(KeyCode.RightControl, "RightCtrl")]
        [TestCase(KeyCode.LeftAlt, "LeftAlt")]
        [TestCase(KeyCode.RightAlt, "RightAlt")]
        [TestCase(KeyCode.AltGr, "AltGr")]
        [TestCase(KeyCode.LeftMeta, "LeftMeta")]
        [TestCase(KeyCode.RightMeta, "RightMeta")]
        [TestCase(KeyCode.LeftWindows, "LeftWindows")]
        [TestCase(KeyCode.RightWindows, "RightWindows")]
        [TestCase(KeyCode.LeftCommand, "LeftCommand")]
        [TestCase(KeyCode.RightCommand, "RightCommand")]
        [TestCase(KeyCode.UpArrow, "UpArrow")]
        [TestCase(KeyCode.DownArrow, "DownArrow")]
        [TestCase(KeyCode.LeftArrow, "LeftArrow")]
        [TestCase(KeyCode.RightArrow, "RightArrow")]
        [TestCase(KeyCode.Home, "Home")]
        [TestCase(KeyCode.End, "End")]
        [TestCase(KeyCode.PageUp, "PageUp")]
        [TestCase(KeyCode.PageDown, "PageDown")]
        [TestCase(KeyCode.Insert, "Insert")]
        [TestCase(KeyCode.Print, "PrintScreen")]
        [TestCase(KeyCode.CapsLock, "CapsLock")]
        [TestCase(KeyCode.Numlock, "NumLock")]
        [TestCase(KeyCode.ScrollLock, "ScrollLock")]
        [TestCase(KeyCode.Pause, "Pause")]
        [TestCase(KeyCode.Menu, "ContextMenu")]
        [TestCase(KeyCode.Keypad0, "Numpad0")]
        [TestCase(KeyCode.Keypad5, "Numpad5")]
        [TestCase(KeyCode.Keypad9, "Numpad9")]
        [TestCase(KeyCode.KeypadDivide, "NumpadDivide")]
        [TestCase(KeyCode.KeypadMultiply, "NumpadMultiply")]
        [TestCase(KeyCode.KeypadPlus, "NumpadPlus")]
        [TestCase(KeyCode.KeypadMinus, "NumpadMinus")]
        [TestCase(KeyCode.KeypadPeriod, "NumpadPeriod")]
        [TestCase(KeyCode.KeypadEquals, "NumpadEquals")]
        [TestCase(KeyCode.BackQuote, "Backquote")]
        [TestCase(KeyCode.Quote, "Quote")]
        [TestCase(KeyCode.Semicolon, "Semicolon")]
        [TestCase(KeyCode.Comma, "Comma")]
        [TestCase(KeyCode.Period, "Period")]
        [TestCase(KeyCode.Slash, "Slash")]
        [TestCase(KeyCode.Backslash, "Backslash")]
        [TestCase(KeyCode.LeftBracket, "LeftBracket")]
        [TestCase(KeyCode.RightBracket, "RightBracket")]
        [TestCase(KeyCode.Minus, "Minus")]
        [TestCase(KeyCode.Equals, "Equals")]
        public void ToKeyMapsKeyCodeToExpectedInputSystemKey(KeyCode key, string expectedName)
        {
            int expected = ExpectedKey(expectedName);
            Assert.AreEqual(expected, InputKeyMapping.MapToKeyValue(key),
                key + " 应映射到 Key." + expectedName);
        }

        [Test]
        public void DigitsMapToDigitKeysAndNotModifiers()
        {
            // 0.8.1 P0 回归：Key 枚举数字块顺序是 Digit1..Digit9,Digit0。
            Assert.AreEqual(ExpectedKey("Digit0"), InputKeyMapping.MapToKeyValue(KeyCode.Alpha0));
            for (int digit = 1; digit <= 9; digit++)
            {
                var keyCode = (KeyCode)((int)KeyCode.Alpha0 + digit);
                Assert.AreEqual(ExpectedKey("Digit" + digit), InputKeyMapping.MapToKeyValue(keyCode),
                    "Alpha" + digit + " 必须映射到 Key.Digit" + digit);
            }
            Assert.AreNotEqual(ExpectedKey("LeftShift"), InputKeyMapping.MapToKeyValue(KeyCode.Alpha1));
            Assert.AreNotEqual(ExpectedKey("RightShift"), InputKeyMapping.MapToKeyValue(KeyCode.Alpha2));
            Assert.AreNotEqual(ExpectedKey("LeftAlt"), InputKeyMapping.MapToKeyValue(KeyCode.Alpha3));
        }

        [TestCase(KeyCode.F13)]
        [TestCase(KeyCode.Clear)]
        [TestCase(KeyCode.Help)]
        [TestCase(KeyCode.Break)]
        [TestCase(KeyCode.SysReq)]
        [TestCase(KeyCode.Tilde)]
        public void UnsupportedKeysReportNotSupported(KeyCode key)
        {
            Assert.IsFalse(InputKeyMapping.IsSupported(key), key + " 当前后端不应视为支持");
            Assert.AreEqual(0, InputKeyMapping.MapToKeyValue(key));
        }

        [Test]
        public void MouseButtonsAreSupportedUpToMouse4()
        {
            Assert.IsTrue(InputKeyMapping.IsSupported(KeyCode.Mouse0));
            Assert.IsTrue(InputKeyMapping.IsSupported(KeyCode.Mouse1));
            Assert.IsTrue(InputKeyMapping.IsSupported(KeyCode.Mouse2));
            Assert.IsTrue(InputKeyMapping.IsSupported(KeyCode.Mouse3));
            Assert.IsTrue(InputKeyMapping.IsSupported(KeyCode.Mouse4));
            Assert.IsFalse(InputKeyMapping.IsSupported(KeyCode.Mouse5));
            Assert.IsFalse(InputKeyMapping.IsSupported(KeyCode.Mouse6));
        }

        [Test]
        public void JoystickButtonsAreSupportedForFirstSixteen()
        {
            for (int i = 0; i <= 15; i++)
            {
                var key = (KeyCode)((int)KeyCode.JoystickButton0 + i);
                Assert.IsTrue(InputKeyMapping.IsSupported(key), "JoystickButton" + i + " 应支持");
            }
            Assert.IsFalse(InputKeyMapping.IsSupported(KeyCode.JoystickButton16));
            Assert.IsFalse(InputKeyMapping.IsSupported(KeyCode.JoystickButton19));
            Assert.IsFalse(InputKeyMapping.IsSupported(KeyCode.Joystick1Button0));
        }

        [Test]
        public void BackendFactoryReportsCapabilities()
        {
            Assert.IsTrue(InputBackendFactory.UsesInputSystemBackend);
            Assert.IsTrue(InputBackendFactory.SupportsAxis("Vertical"));
            Assert.IsTrue(InputBackendFactory.SupportsAxis("Horizontal"));
            Assert.IsTrue(InputBackendFactory.SupportsAxis("Mouse X"));
            Assert.IsTrue(InputBackendFactory.SupportsAxis("Mouse Y"));
            Assert.IsTrue(InputBackendFactory.SupportsAxis("Mouse ScrollWheel"));
            Assert.IsFalse(InputBackendFactory.SupportsAxis("Custom"));
            Assert.IsFalse(InputBackendFactory.SupportsAxis(null));
            Assert.IsTrue(InputBackendFactory.SupportsKey(KeyCode.Alpha1));
            Assert.IsFalse(InputBackendFactory.SupportsKey(KeyCode.F13));
        }
    }
}
#endif
