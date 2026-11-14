using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 生成系统设置面板预制体。面板布局在运行时构建，预制体只需要根节点（RectTransform + CanvasGroup + 组件），
    /// 这样新增设置项不需要重新生成预制体。
    /// <para>菜单：UPandaGF/UI/生成设置面板预制体；CI/批处理可用 -executeMethod 调用本类。</para>
    /// </summary>
    public static class SettingsPanelGenerator
    {
        public const string PrefabPath = "Assets/upanda-framework/Runtime/Resources/UI/SettingsPanel.prefab";

        [MenuItem("UPandaGF/UI/生成设置面板预制体")]
        public static void Generate()
        {
            EnsureDirectory("Assets/upanda-framework/Runtime/Resources/UI");

            var root = new GameObject("SettingsPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(SettingsPanel));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(960f, 640f);
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SettingsPanelGenerator] 已生成：" + PrefabPath);
        }

        private static void EnsureDirectory(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            string name = path.Substring(path.LastIndexOf('/') + 1);
            EnsureDirectory(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
