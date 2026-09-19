//
//  QuickOutlineMaterialRepair.cs
//  路径：Assets/Scripts/upanda-framework/Editor/QuickOutlineEditor/
//
// =============================================================================
//  一、这个工具解决什么问题
// =============================================================================
//  QuickOutline 描边方案依赖两个「放在 Resources 下」的材质：
//
//      Materials/OutlineMask     → Shader "Custom/Outline Mask"
//      Materials/OutlineFill     → Shader "Custom/Outline Fill"
//
//  （路径与 OutDrawline.cs 里的 Resources.Load<Material>(@"Materials/...") 完全一致）
//
//  跨 Unity 版本升级、重装/重新导入包（Samples 目录被整体重建）等操作会导致 .mat 里
//  保存的 Shader GUID 引用断掉 —— 表现就是 Inspector 里 Shader 显示 Missing、
//  模型/描边变成玫红色。本工具会：
//
//    1. 按运行时同样的方式（Resources.Load）取出这两个材质
//    2. 检查它的 Shader 引用是否已丢失
//       （null / 名称为空 / Hidden/InternalErrorShader 都算丢失）
//    3. 丢失就按 Shader 声明名在工程里找回对应 .shader，重新挂上并保存材质资产
//    4. 在 Console 输出逐条报告，顺带提示构建期风险
//
// =============================================================================
//  二、使用方式
// =============================================================================
//  菜单栏：UPandaGF/Tools/QuictOutLine材质修复
//
//  Shader 的查找顺序（两级兜底）：
//    ① Shader.Find("Custom/Outline Mask")      —— 编辑器下可搜到工程内全部 Shader
//    ② 遍历 AssetDatabase 里所有 Shader 资源，按声明名精确匹配
//
//  注：本工具只修「引用」不修「编译错误」。若 Shader 本身编译失败（例如所用 API 在新
//      版本已被移除、或工程切到了 URP/HDRP），重挂后依然是玫红色，需按 Console 的
//      Shader 报错修 shader 源码 —— 这种情况下报告里会给出明确提示。
// =============================================================================

using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// QuickOutline 描边材质「Shader 引用丢失」检查 / 修复工具
    /// </summary>
    public static class QuickOutlineMaterialRepair
    {
        // ---------------- 配置：与运行时脚本、Shader 声明保持一致 ----------------

        private const string MenuPath = "UPandaGF/Runtime/QuickOutline/QuictOutLine材质修复";

        // 与 OutDrawline.cs 中 Resources.Load<Material>(@"Materials/...") 的路径一致
        private const string MaskResourcesPath = "Materials/OutlineMask";
        private const string FillResourcesPath = "Materials/OutlineFill";

        // 与 .shader 文件里 Shader "..." 的声明名一致
        private const string MaskShaderName = "Custom/Outline Mask";
        private const string FillShaderName = "Custom/Outline Fill";

        // Shader 引用丢失时 Unity 使用的内置占位 Shader（渲染出来就是玫红色）
        private const string InternalErrorShaderName = "Hidden/InternalErrorShader";

        // 单个材质的处理结果
        private enum RepairResult
        {
            Healthy,    // 引用完好
            Repaired,   // 已重新挂载并保存
            Failed      // 材质或 Shader 资源缺失，无法修复
        }

        // =====================================================================
        //  入口
        // =====================================================================

        [MenuItem(MenuPath, false, 100)]
        public static void RepairOutlineMaterials()
        {
            AssetDatabase.Refresh();

            // 先确保两个 Shader 资源本身找得到（找不到就没法修）
            Shader maskShader = FindShader(MaskShaderName);
            Shader fillShader = FindShader(FillShaderName);

            int repairedCount = 0;
            int healthyCount = 0;
            int failedCount = 0;

            StringBuilder report = new StringBuilder();
            report.AppendLine("===== QuickOutline 描边材质修复 =====");

            Count(ProcessMaterial(MaskResourcesPath, MaskShaderName, maskShader, report),
                  ref repairedCount, ref healthyCount, ref failedCount);

            Count(ProcessMaterial(FillResourcesPath, FillShaderName, fillShader, report),
                  ref repairedCount, ref healthyCount, ref failedCount);

            // 修完统一落盘（材质是资产，必须 SaveAssets 才会写进 .mat 文件）
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.AppendLine("===== 完成：修复 " + repairedCount + " 个 / 正常 " + healthyCount +
                              " 个 / 失败 " + failedCount + " 个 =====");

            if (repairedCount > 0)
            {
                report.AppendLine("（已重新挂载 Shader 并保存 .mat，可直接在 Inspector 里确认，建议随后提交版本控制）");
            }

            Debug.Log(report.ToString());

            // 批处理（-batchmode）下不允许弹窗，只保留 Console 日志
            if (Application.isBatchMode) return;

            if (failedCount > 0)
            {
                EditorUtility.DisplayDialog("QuickOutline 材质修复",
                    "完成，但有 " + failedCount + " 个失败，详情见 Console。", "知道了");
            }
            else if (repairedCount > 0)
            {
                EditorUtility.DisplayDialog("QuickOutline 材质修复",
                    "已修复 " + repairedCount + " 个材质，" + healthyCount + " 个本来就正常。", "好");
            }
            else
            {
                EditorUtility.DisplayDialog("QuickOutline 材质修复",
                    "检查完毕：" + healthyCount + " 个材质的 Shader 引用都正常，无需修复。", "好");
            }
        }

        // =====================================================================
        //  单个材质的「检查 → 修复 → 报告」
        // =====================================================================

        private static RepairResult ProcessMaterial(string resourcesPath, string shaderName, Shader shader,
                                                    StringBuilder report)
        {
            string label = resourcesPath + "（期望 Shader：" + shaderName + "）";

            // ---------- 步骤 1：取材质（与运行时同样用 Resources.Load） ----------
            Material mat = FindMaterialAsset(resourcesPath, out string assetPath);

            if (mat == null)
            {
                report.AppendLine("[失败] " + label);
                report.AppendLine("        在 Resources 下找不到该材质。请确认 .mat 文件存在，且位于某个名为 Resources 的目录下" +
                                  "（相对路径需为 " + resourcesPath + ".mat），例如：" +
                                  "Assets/.../QuickOutline/Samples/Resources/" + resourcesPath + ".mat");
                return RepairResult.Failed;
            }

            // ---------- 步骤 2：Shader 资源本身在不在 ----------
            if (shader == null)
            {
                report.AppendLine("[失败] " + label);
                report.AppendLine("        工程里找不到 Shader \"" + shaderName + "\"，无法修复（材质：" + assetPath + "）。");
                report.AppendLine("        请确认对应 .shader 文件存在并编译通过（Assets/.../QuickOutline/Samples/Shaders/）。");
                return RepairResult.Failed;
            }

            // ---------- 步骤 3：引用完好、且指向的就是约定的 Shader → 跳过 ----------
            bool shaderMissing = IsShaderMissing(mat);
            bool shaderMismatched = !shaderMissing && mat.shader.name != shaderName;

            if (!shaderMissing && !shaderMismatched)
            {
                report.AppendLine("[正常] " + label + " → 引用完好（" + assetPath + "）");
                return RepairResult.Healthy;
            }

            // ---------- 步骤 4：引用断了 / 指错 → 重挂 ----------
            string originalName = (mat.shader == null || string.IsNullOrEmpty(mat.shader.name))
                ? "<null>"
                : mat.shader.name;

            mat.shader = shader;
            EditorUtility.SetDirty(mat);   // 标脏；SaveAssets 由入口统一调用

            report.AppendLine("[已修复] " + label);
            if (shaderMissing)
            {
                report.AppendLine("        Shader 引用已丢失（原值 " + originalName + "）→ 重新挂上 \"" +
                                  shader.name + "\"（" + assetPath + "）");
            }
            else
            {
                report.AppendLine("        原本指向 \"" + originalName + "\"，与 QuickOutline 的约定不符 → 已改回 \"" +
                                  shader.name + "\"（" + assetPath + "）");
            }

            AppendBuildHint(shader, report);
            EditorGUIUtility.PingObject(mat);

            return RepairResult.Repaired;
        }

        /// <summary>
        /// 构建期风险提示（不影响编辑器内表现，但会决定打包后是否变玫红）
        /// </summary>
        private static void AppendBuildHint(Shader shader, StringBuilder report)
        {
            if (!shader.isSupported)
            {
                report.AppendLine("        [!] " + shader.name + " 在当前平台不受支持或编译失败，" +
                                  "材质大概率仍是玫红色 —— 请看 Console 的 Shader 报错（典型原因：" +
                                  "用了新版本才有的 API，或工程已切到 URP/HDRP）。");
                return;
            }

            string shaderPath = AssetDatabase.GetAssetPath(shader).Replace('\\', '/');
            if (!string.IsNullOrEmpty(shaderPath) && shaderPath.IndexOf("/Resources/", StringComparison.Ordinal) < 0)
            {
                report.AppendLine("        [!] 提示：" + shader.name + " 不在 Resources 目录下（" + shaderPath + "）。");
                report.AppendLine("            它目前只靠这个 Resources 材质「顺带」进包；一旦该材质被删，" +
                                  "运行时的 Shader.Find 在打包后会返回 null。" +
                                  "建议把 Shader 也放进 Resources，或加入 Project Settings → Graphics → Always Included Shaders。");
            }
        }

        // =====================================================================
        //  资源查找
        // =====================================================================

        /// <summary>
        /// 判断材质的 Shader 引用是否已丢失
        /// （引用断掉时 Unity 会给出 null、空名、或内置的 Hidden/InternalErrorShader）
        /// </summary>
        private static bool IsShaderMissing(Material mat)
        {
            Shader shader = mat.shader;

            if (shader == null) return true;
            if (string.IsNullOrEmpty(shader.name)) return true;

            return shader.name == InternalErrorShaderName;
        }

        /// <summary>
        /// 取 Resources 下的材质：优先用运行时同样的 Resources.Load，
        /// 失败再按「任意 Resources 目录 + 相同相对路径」在 AssetDatabase 里兜底检索
        /// </summary>
        private static Material FindMaterialAsset(string resourcesPath, out string assetPath)
        {
            Material mat = Resources.Load<Material>(resourcesPath);
            if (mat != null)
            {
                assetPath = AssetDatabase.GetAssetPath(mat).Replace('\\', '/');
                return mat;
            }

            // 兜底：文件名过滤 + 路径后缀匹配（相对路径必须位于某个 Resources 目录之下）
            string fileName = resourcesPath.Substring(resourcesPath.LastIndexOf('/') + 1);
            string wantSuffix = "/Resources/" + resourcesPath + ".mat";

            foreach (string guid in AssetDatabase.FindAssets("t:Material " + fileName))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path.EndsWith(wantSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    Material found = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (found != null)
                    {
                        assetPath = path;
                        return found;
                    }
                }
            }

            assetPath = null;
            return null;
        }

        /// <summary>
        /// 找回 Shader：先 Shader.Find（编辑器下能搜到工程内所有 Shader），
        /// 再遍历 AssetDatabase 按声明名精确匹配
        /// </summary>
        private static Shader FindShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null) return shader;

            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Shader candidate = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (candidate != null && candidate.name == shaderName)
                {
                    return candidate;
                }
            }

            return null;
        }

        // =====================================================================
        //  工具方法
        // =====================================================================

        private static void Count(RepairResult result, ref int repaired, ref int healthy, ref int failed)
        {
            switch (result)
            {
                case RepairResult.Repaired: repaired++; break;
                case RepairResult.Healthy: healthy++; break;
                default: failed++; break;
            }
        }
    }
}
