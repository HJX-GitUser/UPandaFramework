// URP 单 Pass 模板写入材质：ColorMask 0，把本体的可见像素写进模板缓冲。
// URP 的 DrawRenderers 对每个 Renderer 只执行一个匹配 Pass，因此模板与描边必须是
// 两个 Renderer（见 OutlineMask/OutlineFill 与 OutLine 运行时的双代理实现）。
Shader "UPandaGF/OutLine/OutlineMask"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _MaskZTest("Mask ZTest", Float) = 8
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width", Range(0, 200)) = 4
        [ToggleUI] _WidthMode("Width In Pixels", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "Mask"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZTest [_MaskZTest]
            ZWrite Off
            ColorMask 0

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            CGPROGRAM
            #pragma vertex OutlineStencilVertex
            #pragma fragment OutlineStencilFragment
            #pragma multi_compile_instancing
            #include "OutlineShared.cginc"
            ENDCG
        }
    }

    Fallback Off
}
