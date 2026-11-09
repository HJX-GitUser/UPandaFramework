// =============================================================================
//  OutlineHDRP.shader —— HDRP 分支模板（可选启用）
//
//  ⚠ 本文件放在以 "~" 结尾的目录里，Unity 不会导入它，因此不会影响 Built-in / URP 工程。
//     使用 HDRP 的项目需要：
//       1. 把本文件复制到 Runtime/Game/OutLine/Shaders/ 下（或工程内任意 Shader 目录）；
//       2. 确认工程已安装 HDRP 包，且 include 路径与所用 HDRP 版本一致；
//       3. 在 Play Mode 下验收五种模式、遮挡与模板缓冲效果。
//
//  运行时通过 Shader.Find("UPandaGF/OutLine/Outline (HDRP)") 查找本 Shader：
//  找到就用它创建描边材质，找不到会打一条错误日志并停用描边渲染。
//  本仓库的验证工程只覆盖 Built-in，模板未在 HDRP 下编译验证。
//
//  与 Outline.shader 的差异：
//    · 使用 HLSLPROGRAM + SRP Core/HDRP 头文件，Pass 打 "LightMode"="Forward"；
//    · 模板缓冲与深度比较沿用同一套固定管线状态；
//    · 正交相机的宽度补偿未实现（HDRP 模板只按透视视深补偿）。
// =============================================================================

Shader "UPandaGF/OutLine/Outline (HDRP)"
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
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Stencil"
            Tags { "LightMode" = "Forward" }
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

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertStencil
            #pragma fragment FragStencil

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            struct AttributesStencil
            {
                float4 positionOS : POSITION;
            };

            struct VaryingsStencil
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsStencil VertStencil(AttributesStencil input)
            {
                VaryingsStencil output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            float4 FragStencil(VaryingsStencil input) : SV_Target
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "Forward" }
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

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex VertOutline
            #pragma fragment FragOutline

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            float4 _OutlineColor;
            float _OutlineWidth;
            float _WidthMode;

            struct AttributesOutline
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct VaryingsOutline
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsOutline VertOutline(AttributesOutline input)
            {
                VaryingsOutline output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float3 viewPosition = TransformWorldToView(positionWS);
                float3 viewNormal = TransformWorldToViewDir(normalWS, true);
                float4 positionCS = TransformWorldToHClip(positionWS);
                float4 projectedNormal = mul(UNITY_MATRIX_P, float4(viewNormal, 0.0));

                if (_WidthMode > 0.5)
                {
                    float2 halfScreen = _ScreenSize.xy * 0.5;
                    float2 pixelDirection = projectedNormal.xy * halfScreen;
                    float lengthSquared = dot(pixelDirection, pixelDirection);
                    float2 pixelStep = lengthSquared > 1e-12
                        ? pixelDirection * rsqrt(lengthSquared) * _OutlineWidth
                        : float2(0.0, 0.0);
                    positionCS.xy += pixelStep / halfScreen * positionCS.w;
                }
                else
                {
                    float depth = -viewPosition.z;
                    positionCS += mul(UNITY_MATRIX_P, float4(viewNormal * (depth * _OutlineWidth * 0.001), 0.0));
                }

                output.positionCS = positionCS;
                return output;
            }

            float4 FragOutline(VaryingsOutline input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
