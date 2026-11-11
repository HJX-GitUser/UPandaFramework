using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UPandaGF;
using UPandaGF.GFEditor;

namespace UPandaGF.Tests.EditMode
{
    /// <summary>UI-050：面板校验器规则。</summary>
    public class UIPanelValidatorTests
    {
        [UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/TestValid")]
        private sealed class ValidPanel : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        private sealed class MissingAttributePanel : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        [UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/TestDuplicate")]
        private sealed class DuplicateA : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        [UILoadInfo(E_UI_Layer.Popup, AssetLoadMethod.Resources, "UI/TestDuplicate")]
        private sealed class DuplicateB : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        [UILoadInfo(E_UI_Layer.Popup, AssetLoadMethod.Resources, "UI/TestMissing")]
        private sealed class MissingPrefabPanel : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        [UILoadInfo(E_UI_Layer.Popup, AssetLoadMethod.Resources, "UI/TestNoRect")]
        private sealed class NoRectPanel : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        [UILoadInfo((E_UI_Layer)99, AssetLoadMethod.Resources, "UI/TestBadLayer")]
        private sealed class BadLayerPanel : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        [UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, "UI/Test.prefab")]
        private sealed class ExtensionPathPanel : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private GameObject CreatePrefab(string name, bool rect = true)
        {
            GameObject go = rect
                ? new GameObject(name, typeof(RectTransform))
                : new GameObject(name);
            objects.Add(go);
            return go;
        }

        private bool HasError(UIPanelValidationReport report, string text)
        {
            return report.Issues.Any(issue =>
                issue.Severity == UIPanelIssueSeverity.Error &&
                issue.Message.IndexOf(text, StringComparison.Ordinal) >= 0);
        }

        [Test]
        public void ValidPanelPasses()
        {
            GameObject prefab = CreatePrefab("Valid");
            UIPanelValidationReport report = UIPanelValidator.ValidateTypes(
                new[] { typeof(ValidPanel) }, path => prefab, false);

            Assert.IsTrue(report.Passed);
            Assert.AreEqual(0, report.Issues.Count);
        }

        [Test]
        public void MissingAttributeIsError()
        {
            UIPanelValidationReport report = UIPanelValidator.ValidateTypes(
                new[] { typeof(MissingAttributePanel) }, path => null, false);

            Assert.IsFalse(report.Passed);
            Assert.IsTrue(HasError(report, "缺少 [UILoadInfo]"));
        }

        [Test]
        public void DuplicatePathIsErrorForEveryPanel()
        {
            GameObject prefab = CreatePrefab("Duplicate");
            UIPanelValidationReport report = UIPanelValidator.ValidateTypes(
                new[] { typeof(DuplicateA), typeof(DuplicateB) }, path => prefab, false);

            Assert.IsFalse(report.Passed);
            Assert.AreEqual(2, report.Issues.Count(issue => issue.Message.Contains("loadPath 重复")));
        }

        [Test]
        public void MissingResourcesPrefabIsError()
        {
            UIPanelValidationReport report = UIPanelValidator.ValidateTypes(
                new[] { typeof(MissingPrefabPanel) }, path => null, false);

            Assert.IsFalse(report.Passed);
            Assert.IsTrue(HasError(report, "找不到预制体"));
        }

        [Test]
        public void NonRectTransformRootIsError()
        {
            GameObject prefab = CreatePrefab("NoRect", false);
            UIPanelValidationReport report = UIPanelValidator.ValidateTypes(
                new[] { typeof(NoRectPanel) }, path => prefab, false);

            Assert.IsFalse(report.Passed);
            Assert.IsTrue(HasError(report, "RectTransform"));
        }

        [Test]
        public void InvalidLayerAndExtensionAreErrors()
        {
            GameObject prefab = CreatePrefab("Bad");
            UIPanelValidationReport report = UIPanelValidator.ValidateTypes(
                new[] { typeof(BadLayerPanel), typeof(ExtensionPathPanel) }, path => prefab, false);

            Assert.IsTrue(HasError(report, "ui_Layer"));
            Assert.IsTrue(HasError(report, ".prefab"));
        }

        [Test]
        public void DuplicateInteractiveNameWarnsButPasses()
        {
            GameObject prefab = CreatePrefab("DupNames");
            CreateButton(prefab.transform, "Button");
            CreateButton(prefab.transform, "Button");

            UIPanelValidationReport report = UIPanelValidator.ValidateTypes(
                new[] { typeof(ValidPanel) }, path => prefab, false);

            Assert.IsTrue(report.Passed);
            Assert.AreEqual(1, report.WarningCount);
            Assert.IsTrue(report.Issues[0].Message.Contains("重名"));
        }

        [Test]
        public void PackagePanelsPassValidation()
        {
            UIPanelValidationReport report = UIPanelValidator.ValidateAll(false);
            string details = string.Join("\n", report.Issues.Select(issue => issue.ToString()).ToArray());
            Assert.IsTrue(report.Passed, details);
        }

        [Test]
        public void CollectPanelTypesFindsRuntimePanelAndSkipsTests()
        {
            List<Type> types = UIPanelValidator.CollectPanelTypes();
            CollectionAssert.Contains(types, typeof(SimpleLoadUI));
            CollectionAssert.DoesNotContain(types, typeof(ValidPanel));
        }

        private static Button CreateButton(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Button));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Button>();
        }
    }

    /// <summary>UI-051：生成脚本模板与控件收集。</summary>
    public class UIAutoGeneratorTests
    {
        private sealed class NestedTestPanel : BasePanel
        {
            public override void OnOpen(object panelArg = null) { }
            public override void OnClose() { }
        }

        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private GameObject CreateRoot()
        {
            GameObject root = new GameObject("DemoPanel", typeof(RectTransform));
            objects.Add(root);
            return root;
        }

        private static UIAutoGeneratorSettings CreateSettings()
        {
            return new UIAutoGeneratorSettings
            {
                ScriptName = "DemoPanel",
                NamespaceName = "Game.UI",
                Layer = E_UI_Layer.Main,
                LoadMethod = AssetLoadMethod.Resources,
                LoadPath = "UI/Demo",
                Modal = true,
                Pool = true,
                Order = 3
            };
        }

        [Test]
        public void TemplateImplementsBasePanelContractAndLoadInfo()
        {
            List<UIAutoControl> controls = new List<UIAutoControl>
            {
                new UIAutoControl
                {
                    TypeName = "Button", FieldName = "btnClose", GameObjectName = "BtnClose",
                    Event = UIAutoControlEvent.Click
                },
                new UIAutoControl
                {
                    TypeName = "TMP_InputField", FieldName = "inpName", GameObjectName = "InpName",
                    Event = UIAutoControlEvent.StringEndEdit
                }
            };

            string code = UIAutoGenerator.BuildScriptContent(CreateSettings(), controls);

            StringAssert.Contains("public partial class DemoPanel : BasePanel", code);
            StringAssert.Contains("[UILoadInfo(E_UI_Layer.Main, AssetLoadMethod.Resources, \"UI/Demo\"", code);
            StringAssert.Contains("modal = true", code);
            StringAssert.Contains("pool = true", code);
            StringAssert.Contains("order = 3", code);
            StringAssert.Contains("protected override void OnAwake()", code);
            StringAssert.Contains("public override void OnOpen(object panelArg = null)", code);
            StringAssert.Contains("public override void OnClose()", code);
            StringAssert.Contains("TryGetControl(\"BtnClose\", out btnClose);", code);
            StringAssert.Contains("TMP_InputField inpName;", code);
            StringAssert.Contains("OpenEvents.Listen(btnClose.onClick, OnBtnCloseClick);", code);
            StringAssert.Contains("private void OnInpNameEndEdit(string value)", code);
            StringAssert.DoesNotContain("InputFieldTMP", code);
            StringAssert.DoesNotContain("ScrollView", code);
            StringAssert.DoesNotContain("EditorApplication.update", code);
            StringAssert.DoesNotContain("private void OnDestroy()", code);
        }

        [Test]
        public void TemplateCanDisableBindAndEventStubs()
        {
            UIAutoGeneratorSettings settings = CreateSettings();
            settings.GenerateBindCode = false;
            settings.GenerateEventStubs = false;
            string code = UIAutoGenerator.BuildScriptContent(settings, new List<UIAutoControl>
            {
                new UIAutoControl
                {
                    TypeName = "Button", FieldName = "btnOk", GameObjectName = "BtnOk",
                    Event = UIAutoControlEvent.Click
                }
            });

            StringAssert.DoesNotContain("AutoBindComponents", code);
            StringAssert.DoesNotContain("OpenEvents.Listen", code);
            StringAssert.Contains("public override void OnOpen(object panelArg = null)", code);
            StringAssert.Contains("public override void OnClose()", code);
        }

        [Test]
        public void CollectControlsSkipsNestedPanelSubtree()
        {
            GameObject root = CreateRoot();
            CreateButton(root.transform, "BtnRoot");

            GameObject nested = new GameObject("NestedPanel", typeof(RectTransform));
            nested.transform.SetParent(root.transform, false);
            nested.AddComponent<NestedTestPanel>();
            CreateButton(nested.transform, "BtnNested");

            List<UIAutoControl> controls = UIAutoGenerator.CollectControls(root);
            Assert.AreEqual(1, controls.Count);
            Assert.AreEqual("BtnRoot", controls[0].GameObjectName);
        }

        [Test]
        public void FieldNamesAreUniqueAndValidIdentifiers()
        {
            GameObject root = CreateRoot();
            CreateButton(root.transform, "Button A");
            CreateButton(root.transform, "Button-A");

            List<UIAutoControl> controls = UIAutoGenerator.CollectControls(root);
            Assert.AreEqual(2, controls.Count);
            Assert.AreNotEqual(controls[0].FieldName, controls[1].FieldName);
            foreach (UIAutoControl control in controls)
            {
                StringAssert.IsMatch("^[A-Za-z_][A-Za-z0-9_]*$", control.FieldName);
                Assert.AreNotEqual("class", control.FieldName);
            }
        }

        [Test]
        public void DuplicateSameNameAndTypeIsSkipped()
        {
            GameObject root = CreateRoot();
            CreateButton(root.transform, "Button");
            CreateButton(root.transform, "Button");

            List<UIAutoControl> controls = UIAutoGenerator.CollectControls(root);
            Assert.AreEqual(1, controls.Count, "同名同类型只能按名称取到第一个，后续重复项不应生成字段");
        }

        private static Button CreateButton(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Button));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Button>();
        }
    }
}
