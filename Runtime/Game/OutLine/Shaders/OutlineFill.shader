// URP 单 Pass 描边材质：沿法线外扩，模板测试 NotEqual 把本体挖掉。
// 与 OutlineMask 成对使用，渲染队列 3101 保证在模板 Pass 之后绘制。
Shader "UPandaGF/OutLine/OutlineFill"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _FillZTest("Fill ZTest", Float) = 8
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width", Range(0, 200)) = 4
        [ToggleUI] _WidthMode("Width In Pixels", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+101"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZTest [_FillZTest]
            ZWrite Off
            ColorMask RGB
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref 1
                Comp NotEqual
            }

            CGPROGRAM
            #pragma vertex OutlineFillVertex
            #pragma fragment OutlineFillFragment
            #pragma multi_compile_instancing
            #include "OutlineShared.cginc"
            ENDCG
        }
    }

    Fallback Off
}
