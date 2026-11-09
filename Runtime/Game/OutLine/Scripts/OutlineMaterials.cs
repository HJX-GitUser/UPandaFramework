using UnityEngine;
using UnityEngine.Rendering;

namespace UPandaGF.Gameplay
{
    /// <summary>
    /// 描边材质缓存：五种显示模式各一份共享材质。
    /// 深度比较写在材质里（渲染状态无法用 MaterialPropertyBlock 覆盖），
    /// 颜色与宽度是普通 uniform，按对象用 MaterialPropertyBlock 覆盖，
    /// 因此不需要给每个对象实例化材质。
    /// </summary>
    internal static class OutlineMaterials
    {
        internal const string ShaderName = OutLine.ShaderName;

        private const string TemplatePath = OutLine.MaterialResourcePath;

        private static readonly int MaskTestId = Shader.PropertyToID("_MaskZTest");
        private static readonly int FillTestId = Shader.PropertyToID("_FillZTest");
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
            if (template == null || template.shader == null || template.shader.name != ShaderName)
            {
                if (!reported)
                {
                    reported = true;
                    Debug.LogError("OutLine 找不到描边材质：Resources/" + TemplatePath +
                        " 缺失或 Shader 引用异常（应为 " + ShaderName + "）。可执行菜单 UPandaGF/Runtime/OutLine/描边材质修复。");
                }
                return null;
            }

            Material material = new Material(template)
            {
                name = "OutLine " + mode,
                hideFlags = HideFlags.DontSave
            };
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
