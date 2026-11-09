// =============================================================================
//  OutlineShared.cginc —— 描边两个 Pass 的共用实现（Built-in / URP 通用）
//
//  只依赖 UnityCG.cginc 里的宏与内建矩阵，不引用任何 SRP 头文件，因此：
//    · Built-in 管线直接用；
//    · URP 通过 SubShader 的 RenderPipeline 标签选中对应分支后也能编译运行；
//    · 不安装 URP/HDRP 的工程导入本文件也不会报错。
//
//  宽度有两种单位（由材质属性 _WidthMode 切换）：
//    0 = 屏幕相对厚度：透视按视深补偿、正交按视野高度补偿，远近观感一致；
//    1 = 像素单位：直接按设备像素定宽，分辨率变化时厚度不变，适合 HUD / DPI 敏感场景。
// =============================================================================

#ifndef UPANDA_OUTLINE_SHARED_INCLUDED
#define UPANDA_OUTLINE_SHARED_INCLUDED

#include "UnityCG.cginc"

float4 _OutlineColor;
float _OutlineWidth;
float _WidthMode;

struct OutlineVertexInput
{
    float4 position : POSITION;
    float3 normal : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct OutlineVertexOutput
{
    float4 position : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

// Pass 1：只做对象空间到裁剪空间的变换，模板写入由固定管线状态完成。
OutlineVertexOutput OutlineStencilVertex(OutlineVertexInput input)
{
    OutlineVertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    output.position = UnityObjectToClipPos(input.position);
    return output;
}

fixed4 OutlineStencilFragment(OutlineVertexOutput input) : SV_Target
{
    return fixed4(0.0, 0.0, 0.0, 0.0);
}

// Pass 2：沿法线外扩后着色，模板测试（NotEqual）负责把本体挖掉。
OutlineVertexOutput OutlineFillVertex(OutlineVertexInput input)
{
    OutlineVertexOutput output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4 clip = UnityObjectToClipPos(input.position);
    // 视图空间法线：逆转置矩阵保证非等比缩放下仍垂直于表面。
    float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, input.normal));
    float4 projectedNormal = mul(UNITY_MATRIX_P, float4(viewNormal, 0.0));

    if (_WidthMode > 0.5)
    {
        // 像素单位：把投影后的法线换算成像素方向，归一化后按固定像素长度外扩。
        float2 halfScreen = _ScreenParams.xy * 0.5;
        float2 pixelDirection = projectedNormal.xy * halfScreen;
        float lengthSquared = dot(pixelDirection, pixelDirection);
        float2 pixelStep = lengthSquared > 1e-12
            ? pixelDirection * rsqrt(lengthSquared) * _OutlineWidth
            : float2(0.0, 0.0);
        clip.xy += pixelStep / halfScreen * clip.w;
    }
    else
    {
        // 屏幕相对厚度：透视按视深补偿，正交按视野高度补偿。
        float depth = -UnityObjectToViewPos(input.position).z;
        float distanceScale = unity_OrthoParams.w > 0.5 ? unity_OrthoParams.y : depth;
        clip += mul(UNITY_MATRIX_P, float4(viewNormal * (distanceScale * _OutlineWidth * 0.001), 0.0));
    }

    output.position = clip;
    return output;
}

fixed4 OutlineFillFragment(OutlineVertexOutput input) : SV_Target
{
    return _OutlineColor;
}

#endif
