using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UPandaGF;

namespace UPandaGF.Tests
{
    /// <summary>
    /// 0.8.2 UIFocusInputBinder：把 IInputService 的 UI 动作接到 UIFocusNavigator。
    /// </summary>
    public sealed class UIFocusInputBinderTests
    {
        private sealed class FakeInput : IInputService
        {
            public readonly HashSet<string> DownActions = new HashSet<string>();
            public readonly HashSet<string> HeldActions = new HashSet<string>();
            public readonly Dictionary<string, float> Axes = new Dictionary<string, float>();
            public event Action BindingsChanged;
            public float GetAxis(string action, string context = InputContexts.Gameplay) => Axes.TryGetValue(action, out var value) ? value : 0f;
            public bool GetButton(string action, string context = InputContexts.Gameplay) => HeldActions.Contains(action);
            public bool GetButtonDown(string action, string context = InputContexts.Gameplay) => DownActions.Contains(action);
            public bool GetButtonUp(string action, string context = InputContexts.Gameplay) => false;
            public void Bind(InputBinding binding) { BindingsChanged?.Invoke(); }
            public bool Unbind(string action, string context = InputContexts.Gameplay) => false;
            public IReadOnlyList<InputBinding> GetBindings() => Array.Empty<InputBinding>();
            public InputRebindResult TryRebind(InputBinding binding, bool allowReservedKey = false) => InputRebindResult.Success;
            public bool TryCaptureKey(out KeyCode key) { key = KeyCode.None; return false; }
            public bool ResetToDefaults() => false;
            public IReadOnlyList<InputBinding> GetDefaultBindings() => Array.Empty<InputBinding>();
            public InputBinding[] ExportOverrides() => Array.Empty<InputBinding>();
            public void ApplyOverrides(IEnumerable<InputBinding> overrides) { }
            public IDisposable PushContext(string context, bool blockLower = true) => new DisposableAction(() => { });
        }

        private sealed class FakeServices : IGameServices
        {
            public FakeServices(IInputService input) { Input = input; }
            public IAssetService Assets => null;
            public IAudioService Audio => null;
            public ISaveService Saves => null;
            public ISceneService Scenes => null;
            public IInputService Input { get; }
            public ISettingsService Settings => null;
            public ILocalizationService Localization => null;
            public IPauseService Pause => null;
            public IGameClock Clock => null;
            public ITimerService Timer => null;
            public IConfigService Config => null;
            public IGameFlowService Flow => null;
            public IAppLifecycle AppLifecycle => null;
            public IHttpService Http => null;
            public IPoolService Pools => null;
            public ILogService Log => null;
        }

        [UnityTest]
        public IEnumerator ConfirmSubmitsFocusedButton()
        {
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            var root = new GameObject("FocusBinderRoot", typeof(RectTransform));
            var navigator = root.AddComponent<UIFocusNavigator>();
            var buttonObject = new GameObject("Button", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(root.transform, false);
            var button = buttonObject.GetComponent<Button>();
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);
            navigator.SetDefault(button, focusNow: true);
            var binder = root.AddComponent<UIFocusInputBinder>();
            var input = new FakeInput();
            binder.Configure(new FakeServices(input));
            yield return null;

            input.DownActions.Add("Confirm");
            yield return null;
            input.DownActions.Clear();

            Assert.AreEqual(1, clicks, "Confirm 应触发焦点项的 Submit/onClick");
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(eventSystemObject);
        }
    }
}
