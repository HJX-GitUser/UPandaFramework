// =============================================================================
//  OutlineMaterialRepairTool.cs —— 描边模板材质检查 / 修复
//
//  运行时只依赖一份模板材质：Resources/Materials/Outline.mat，它引用
//  Shader "UPandaGF/OutLine/Outline"。跨 Unity 版本升级、重装包或整体重建
//  Samples 目录时，.mat 里保存的 Shader GUID 可能断开，表现为 Inspector 里
//  Shader 显示 Missing、模型描边变玫红色。本工具会：
//    1. 按运行时同样的路径（Resources.Load）取出模板材质；
//    2. 检查 Shader 引用是否丢失（null / 名称为空 / Hidden/InternalErrorShader）；
//    3. 丢失就按 Shader 声明名在工程里找回并重新挂上，保存材质资产；
//    4. 顺带校验 Resources 路径是否仍能被运行时加载。
//
//  菜单：UPandaGF/Runtime/OutLine/描边材质修复
// =============================================================================

using System;
using System.Text;
using UnityEditor;
using UnityEngine;
using UPandaGF.Gameplay;

namespace UPandaGF.GFEditor
{
    /// <summary>描边模板材质的 Shader 引用检查与修复工具。</summary>
    public static class OutlineMaterialRepairTool
    {
        private const string MenuPath = "UPandaGF/Runtime/OutLine/描边材质修复";
        private const string InternalErrorShader = "Hidden/InternalErrorShader";
        private const string MaterialPathSuffix = "/OutLine/Resources/Materials/Outline.mat";

        [MenuItem(MenuPath, false, 100)]
        public static void Repair()
        {
            AssetDatabase.Refresh();

            var report = new StringBuilder();
            report.AppendLine("===== 描边材质修复 =====");

            Shader shader = FindShader(OutLine.ShaderName);
            if (shader == null)
            {
                report.AppendLine("找不到 Shader \"" + OutLine.ShaderName + "\"，请确认 Runtime/Game/OutLine/Shaders/Outline.shader 已导入。");
                Finish(report, false);
                return;
            }

            Material material = FindTemplateMaterial(out string materialPath);
            if (material == null)
            {
                report.AppendLine("找不到模板材质 " + OutLine.MaterialResourcePath +
                    "（应位于某个 Resources 目录下），请确认包的 Resources 目录完整。");
                Finish(report, false);
                return;
            }

            report.AppendLine("模板材质：" + materialPath);

            bool repaired = false;
            if (material.shader == null || material.shader.name != OutLine.ShaderName)
            {
                string previous = material.shader != null ? material.shader.name : "<丢失>";
                if (previous == InternalErrorShader)
                {
                    previous = "<丢失>";
                }
                material.shader = shader;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
                repaired = true;
                report.AppendLine("已修复 Shader 引用：" + previous + " → " + OutLine.ShaderName);
            }
            else
            {
                report.AppendLine("Shader 引用正常：" + OutLine.ShaderName);
            }

            Material runtimeMaterial = Resources.Load<Material>(OutLine.MaterialResourcePath);
            if (runtimeMaterial == null)
            {
                report.AppendLine("警告：运行时 Resources.Load(\"" + OutLine.MaterialResourcePath +
                    "\") 取不到材质，请确认它仍在 Resources 目录内。");
            }
            else if (runtimeMaterial != material)
            {
                report.AppendLine("警告：Resources.Load 取到的材质与该路径不是同一份资产，可能存在多份同名文件。");
            }
            else
            {
                report.AppendLine("运行时 Resources 加载正常。");
            }

            Finish(report, repaired);
        }

        private static void Finish(StringBuilder report, bool repaired)
        {
            report.AppendLine("===== 完成 =====");
            Debug.Log(report.ToString());

            // 批处理（-batchmode）下不弹窗，只保留 Console 日志。
            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog("描边材质修复",
                    repaired ? "已重新挂载 Shader 并保存材质，建议提交版本控制。" : "描边材质无需修复，详情见 Console。",
                    "确定");
            }
        }

        private static Material FindTemplateMaterial(out string assetPath)
        {
            assetPath = null;
            foreach (string guid in AssetDatabase.FindAssets("Outline t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!path.EndsWith(MaterialPathSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                assetPath = path;
                return AssetDatabase.LoadAssetAtPath<Material>(path);
            }

            Material fallback = Resources.Load<Material>(OutLine.MaterialResourcePath);
            if (fallback != null)
            {
                assetPath = AssetDatabase.GetAssetPath(fallback);
            }
            return fallback;
        }

        private static Shader FindShader(string shaderName)
        {
            Shader found = Shader.Find(shaderName);
            if (found != null)
            {
                return found;
            }

            // Shader.Find 在部分导入状态下可能找不到，退回按资产逐个比对声明名。
            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                Shader candidate = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null && candidate.name == shaderName)
                {
                    return candidate;
                }
            }
            return null;
        }
    }
}
