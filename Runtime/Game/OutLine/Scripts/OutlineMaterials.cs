using UnityEngine;
using UnityEngine.Rendering;

namespace UPandaGF.Gameplay
{
    /// <summary>
    /// 描边材质缓存。
    /// Built-in：五种显示模式各一份共享的两 Pass 材质（模板 + 描边）。
    /// URP：URP 的 DrawRenderers 对每个 Renderer 只执行一个匹配 Pass，两 Pass 材质只会画出
    ///      不可见的模板 Pass，因此在 URP 下改为「模板代理 + 描边代理」两个 Renderer，
    ///      分别使用单 Pass 的 OutlineMask / OutlineFill 材质，按渲染队列 3100/3101 排序。
    /// 深度比较写在材质里（渲染状态无法用 MaterialPropertyBlock 覆盖），
    /// 颜色、宽度与宽度单位是普通 uniform，按对象用 MaterialPropertyBlock 覆盖。
    /// HDRP 需要单独的 Shader，按名称查找，找不到时停用描边并给出一次错误日志。
    /// </summary>
    internal static class OutlineMaterials
    {
        internal const string ShaderName = OutLine.ShaderName;
        internal const string HDRPShaderName = OutLine.HDRPShaderName;

        private const string TemplatePath = OutLine.MaterialResourcePath;
        private const string MaskTemplatePath = "Materials/OutlineMask";
        private const string FillTemplatePath = "Materials/OutlineFill";
        private const string HDRPPipelineTypeName = "HighDefinition";
        private const string URPPipelineTypeName = "Universal";

        private static readonly int MaskTestId = Shader.PropertyToID("_MaskZTest");
        private static readonly int FillTestId = Shader.PropertyToID("_FillZTest");
        private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly Material[] Cache = new Material[5];
        private static readonly Material[] MaskCache = new Material[5];
        private static readonly Material[] FillCache = new Material[5];

        private static bool reported;

        /// <summary>当前是否 URP（只判断渲染管线类型名，避免包体硬依赖 URP 程序集）。</summary>
        internal static bool IsURP
        {
            get
            {
                RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
                return pipeline != null && pipeline.GetType().FullName.Contains(URPPipelineTypeName);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            // 关闭域重载时静态字段会跨 Play 保留，这里清掉旧材质避免泄漏。
            Clear(Cache);
            Clear(MaskCache);
            Clear(FillCache);
            reported = false;
        }

        private static void Clear(Material[] cache)
        {
            for (int i = 0; i < cache.Length; i++)
            {
                if (cache[i] != null)
                {
                    Object.Destroy(cache[i]);
                    cache[i] = null;
                }
            }
        }

        /// <summary>Built-in / HDRP 使用的两 Pass 材质。</summary>
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

        /// <summary>URP 的模板写入材质（单 Pass）。Built-in 下退化为两 Pass 材质。</summary>
        internal static Material GetMask(OutLine.描边模式 mode)
        {
            if (!IsURP)
            {
                return Get(mode);
            }
            int index = IndexOf(mode);
            if (MaskCache[index] != null)
            {
                return MaskCache[index];
            }
            Material material = CreateSinglePass(MaskTemplatePath, "OutLine Mask " + mode, MaskTestId, MaskTest(mode));
            MaskCache[index] = material;
            return material;
        }

        /// <summary>URP 的描边材质（单 Pass）。Built-in 下返回 null。</summary>
        internal static Material GetFill(OutLine.描边模式 mode)
        {
            if (!IsURP)
            {
                return null;
            }
            int index = IndexOf(mode);
            if (FillCache[index] != null)
            {
                return FillCache[index];
            }
            Material material = CreateSinglePass(FillTemplatePath, "OutLine Fill " + mode, FillTestId, FillTest(mode));
            FillCache[index] = material;
            return material;
        }

        private static Material CreateSinglePass(string path, string name, int testId, CompareFunction test)
        {
            Material template = Resources.Load<Material>(path);
            if (template == null || template.shader == null)
            {
                Report("OutLine 缺少 URP 单 Pass 模板材质：Resources/" + path + "。");
                return null;
            }
            var material = new Material(template)
            {
                name = name,
                hideFlags = HideFlags.DontSave
            };
            if (material.HasProperty(testId))
            {
                material.SetFloat(testId, (float)test);
            }
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
