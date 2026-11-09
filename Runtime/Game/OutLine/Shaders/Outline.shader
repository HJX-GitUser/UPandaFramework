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
//  【宽度】_OutlineWidth 是屏幕相对厚度：透视相机按视深补偿，正交相机按视野高度补偿，
//  因此同一宽度在远近、缩放变化下看起来一样粗。换算比例 0.001 与 Inspector 的 0~200 对应。
// =============================================================================

Shader "UPandaGF/OutLine/Outline"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _MaskZTest("Mask ZTest", Float) = 8
        [Enum(UnityEngine.Rendering.CompareFunction)] _FillZTest("Fill ZTest", Float) = 8
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width", Range(0, 200)) = 4
    }

    SubShader
    {
        Tags
        {
            // 透明队列：深度缓冲已由场景中的不透明物体写好，遮挡关系才正确。
            "Queue" = "Transparent+100"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            // 关闭动态合批：合批会把顶点烘焙到世界空间，外扩方向需要对象空间法线。
            "DisableBatching" = "True"
        }

        // ---------------------------------------------------------------------
        //  Pass 1：只写模板缓冲
        // ---------------------------------------------------------------------
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
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct VertexInput
            {
                float4 position : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertexOutput
            {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            VertexOutput Vert(VertexInput input)
            {
                VertexOutput output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.position);
                return output;
            }

            fixed4 Frag(VertexOutput input) : SV_Target
            {
                return fixed4(0.0, 0.0, 0.0, 0.0);
            }
            ENDCG
        }

        // ---------------------------------------------------------------------
        //  Pass 2：沿法线外扩并着色
        // ---------------------------------------------------------------------
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
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct VertexInput
            {
                float4 position : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertexOutput
            {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _OutlineColor;
            float _OutlineWidth;

            VertexOutput Vert(VertexInput input)
            {
                VertexOutput output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 clip = UnityObjectToClipPos(input.position);

                // 视图空间法线：逆转置矩阵保证非等比缩放下依然垂直于表面。
                float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, input.normal));

                // 透视按视深补偿，正交按视野高度补偿，使屏幕上的厚度与距离无关。
                float depth = -UnityObjectToViewPos(input.position).z;
                float distanceScale = unity_OrthoParams.w > 0.5 ? unity_OrthoParams.y : depth;

                // 外扩量换算到裁剪空间：法线方向乘上比例后再投影，避免逐轴缩放失真。
                float3 viewOffset = viewNormal * (distanceScale * _OutlineWidth * 0.001);
                clip += mul(UNITY_MATRIX_P, float4(viewOffset, 0.0));
                output.position = clip;
                return output;
            }

            fixed4 Frag(VertexOutput input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
}
