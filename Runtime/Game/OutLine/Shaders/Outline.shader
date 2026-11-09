// =============================================================================
//  Outline.shader —— 描边高亮（Built-in 管线）
//
//  【原理】一个材质两个 Pass，先后绘制，共用同一份几何：
//    ① Stencil Pass：ColorMask 0，把「模型本体」覆盖到的像素写进模板缓冲（Ref = 1）。
//    ② Outline Pass：顶点沿法线向屏幕外扩一圈，Stencil { Comp NotEqual }，
//       只在本体之外（模板 != 1）着色 —— 本体把内圈挖掉，留下的就是轮廓。
//
//  【可调状态】两个 Pass 的深度比较由材质属性 _MaskZTest / _FillZTest 控制，
//  运行时脚本为五种模式各准备一份共享材质：
//    全部描边   Mask=Always    Fill=Always
//    可见描边   Mask=Always    Fill=LessEqual
//    遮挡描边   Mask=Always    Fill=Greater
//    描边与剪影 Mask=LessEqual Fill=Always
//    仅剪影     Mask=LessEqual Fill=Greater（宽度 0）
//
//  【URP】URP 的 DrawRenderers 对每个 Renderer 只执行一个匹配 Pass，两 Pass 材质
//  只会画出不可见的模板 Pass。因此 URP 不使用本文件，而是由运行时创建
//  「模板代理 + 描边代理」两个 Renderer，分别使用单 Pass 的
//  OutlineMask.shader 与 OutlineFill.shader（渲染队列 3100 / 3101）。
//  HDRP 需要单独的 Shader（Shaders~/HDRP/OutlineHDRP.shader）。
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
