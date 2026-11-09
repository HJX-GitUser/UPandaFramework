using UnityEngine;
using UnityEngine.Rendering;

namespace UPandaGF.Gameplay
{
    /// <summary>
    /// 描边材质缓存：五种显示模式各一份共享材质。
    /// 深度比较写在材质里（渲染状态无法用 MaterialPropertyBlock 覆盖），
    /// 颜色、宽度与宽度单位是普通 uniform，按对象用 MaterialPropertyBlock 覆盖，
    /// 因此不需要给每个对象实例化材质。
    /// Built-in 与 URP 共用同一个 Shader 资产（由 SubShader 的 RenderPipeline 标签自动选择）；
    /// HDRP 需要单独的 Shader，按名称查找，找不到时停用描边并给出一次错误日志。
    /// </summary>
    internal static class OutlineMaterials
    {
        internal const string ShaderName = OutLine.ShaderName;
        internal const string HDRPShaderName = OutLine.HDRPShaderName;

        private const string TemplatePath = OutLine.MaterialResourcePath;
        private const string HDRPPipelineTypeName = "HighDefinition";

        private static readonly int MaskTestId = Shader.PropertyToID("_MaskZTest");
        private static readonly int FillTestId = Shader.PropertyToID("_FillZTest");
        private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly Material[] Cache = new Material[5];

        private static bool reported;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            // 关闭域重载时静态字段会跨 Play 保留，这里清掉旧材质避免泄漏。
            for (int i = 0; i < Cache.Length; i++)
            {
                if (Cache[i] != null)
                {
                    Object.Destroy(Cache[i]);
                    Cache[i] = null;
                }
            }
            reported = false;
        }

        internal static Material Get(OutLine.描边模式 mode)
        {
            int index = IndexOf(mode);
            Material cached = Cache[index];
            if (cached != null)
            {
                return cached;
            }

            Material template = Resources.Load<Material>(TemplatePath);
            if (template == null)
            {
                Report("OutLine 找不到描边模板材质：Resources/" + TemplatePath +
                    " 缺失。可执行菜单 UPandaGF/Runtime/OutLine/描边材质修复。");
                return null;
            }
            if (template.shader == null)
            {
                Report("OutLine 模板材质的 Shader 引用已丢失。可执行菜单 UPandaGF/Runtime/OutLine/描边材质修复。");
                return null;
            }

            Shader shader = ResolveShader(template);
            if (shader == null)
            {
                return null;
            }

            Material material = template.shader == shader
                ? new Material(template)
                : CreateFromShader(shader, template);
            material.name = "OutLine " + mode;
            material.hideFlags = HideFlags.DontSave;
            material.SetFloat(MaskTestId, (float)MaskTest(mode));
            material.SetFloat(FillTestId, (float)FillTest(mode));
            Cache[index] = material;
            return material;
        }

        /// <summary>模板材质本体，编辑器工具检查/修复时使用。</summary>
        internal static Material LoadTemplate()
        {
            return Resources.Load<Material>(TemplatePath);
        }

        /// <summary>
        /// Built-in / URP 直接用模板材质上的 Shader（URP 由 SubShader 标签选中）；
        /// HDRP 需要单独的 Shader 资产，找不到时返回 null 并提示启用模板。
        /// </summary>
        private static Shader ResolveShader(Material template)
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            string pipelineType = pipeline != null ? pipeline.GetType().FullName : null;
            if (pipelineType == null || !pipelineType.Contains(HDRPPipelineTypeName))
            {
                return template.shader;
            }

            Shader found = Shader.Find(HDRPShaderName);
            if (found == null)
            {
                Report("当前工程使用 HDRP，但找不到 Shader \"" + HDRPShaderName +
                    "\"。请把 Runtime/Game/OutLine/Shaders~/HDRP/OutlineHDRP.shader 复制到 Shaders/ 目录后重试。");
                return null;
            }
            return found;
        }

        private static Material CreateFromShader(Shader shader, Material template)
        {
            var material = new Material(shader);
            if (template.HasProperty(ColorId))
            {
                material.SetColor(ColorId, template.GetColor(ColorId));
            }
            if (template.HasProperty(WidthId))
            {
                material.SetFloat(WidthId, template.GetFloat(WidthId));
            }
            return material;
        }

        private static void Report(string message)
        {
            if (reported)
            {
                return;
            }
            reported = true;
            Debug.LogError(message);
        }

        private static int IndexOf(OutLine.描边模式 mode)
        {
            int index = (int)mode;
            return index >= 0 && index < Cache.Length ? index : 0;
        }

        /// <summary>模板遮罩 Pass 的深度比较：只有「描边与剪影」「仅剪影」限制为可见部分。</summary>
        internal static CompareFunction MaskTest(OutLine.描边模式 mode)
        {
            return mode == OutLine.描边模式.描边与剪影 || mode == OutLine.描边模式.仅剪影
                ? CompareFunction.LessEqual
                : CompareFunction.Always;
        }

        /// <summary>轮廓 Pass 的深度比较：决定轮廓在遮挡物前面还是后面显示。</summary>
        internal static CompareFunction FillTest(OutLine.描边模式 mode)
        {
            switch (mode)
            {
                case OutLine.描边模式.可见描边:
                    return CompareFunction.LessEqual;
                case OutLine.描边模式.遮挡描边:
                case OutLine.描边模式.仅剪影:
                    return CompareFunction.Greater;
                default:
                    return CompareFunction.Always;
            }
        }
    }
}
