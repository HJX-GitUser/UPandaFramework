// =============================================================================
//  Outline.shader —— 描边高亮（原创实现）
//
//  【原理】一个材质两个 Pass，先后绘制，共用同一份几何：
//    ① Stencil Pass：ColorMask 0，把「模型本体」覆盖到的像素写进模板缓冲（Ref = 1）。
//       它不产生任何可见像素，只负责给下一步「挖洞」。
//    ② Outline Pass：顶点沿法线向屏幕外扩一圈，Stencil 设置 Comp NotEqual，
//       只在本体之外（模板 != 1）着色 —— 本体把内圈挖掉，留下的就是轮廓。
//
//  【可调状态】两个 Pass 的深度比较由材质属性 _MaskZTest / _FillZTest 控制。
//  运行时脚本为五种模式各准备一份共享材质，因此这两个值不需要逐对象改写：
//    全部描边   Mask=Always    Fill=Always    轮廓始终可见
//    可见描边   Mask=Always    Fill=LessEqual 轮廓被遮挡物挡住
//    遮挡描边   Mask=Always    Fill=Greater   只画被挡住的那部分轮廓
//    描边与剪影 Mask=LessEqual Fill=Always    可见处画轮廓，遮挡处填剪影
//    仅剪影     Mask=LessEqual Fill=Greater   只填遮挡处的剪影（宽度 0）
//
//  【宽度】_WidthMode = 0 时 _OutlineWidth 是屏幕相对厚度（透视按视深、正交按视野高度
//  补偿，远近与缩放观感一致）；_WidthMode = 1 时按设备像素定宽。
//
//  【渲染管线】本文件带两个 SubShader：
//    · 第一个打 "RenderPipeline" = "UniversalPipeline"，供 URP 选择；
//      它只使用内建宏，作为 SRPDefaultUnlit 透明 Pass 由 URP 的 DrawObjectsPass 绘制；
//    · 第二个不带标签，供 Built-in 管线使用。
//    HDRP 不识别这两种 SubShader，需要单独启用 Shaders~/HDRP/OutlineHDRP.shader。
// =============================================================================

Shader "UPandaGF/OutLine/Outline"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _MaskZTest("Mask ZTest", Float) = 8
        [Enum(UnityEngine.Rendering.CompareFunction)] _FillZTest("Fill ZTest", Float) = 8
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width", Range(0, 200)) = 4
        [ToggleUI] _WidthMode("Width In Pixels", Float) = 0
    }

    // -------------------------------------------------------------------------
    //  URP 分支：由 "RenderPipeline" 标签选中，Pass 以 SRPDefaultUnlit 执行。
    // -------------------------------------------------------------------------
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
            Name "Stencil"
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

        Pass
        {
            Name "Outline"
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

    // -------------------------------------------------------------------------
    //  Built-in 分支：没有 RenderPipeline 标签，Built-in 管线使用这一个。
    // -------------------------------------------------------------------------
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "Stencil"
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

        Pass
        {
            Name "Outline"
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
