Shader "UPandaGF/OutLine/OutlineFill"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0
        _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)
        _OutlineWidth("Outline Width", Range(0, 200)) = 2
        [HideInInspector] _UseSmoothNormals("Use Smooth Normals", Float) = 0
    }

    SubShader
    {
        // TEXCOORD3 可存储对象空间下的平滑法线。
        Tags { "Queue" = "Transparent+110" "RenderType" = "Transparent" "DisableBatching" = "True" }

        Pass
        {
            Name "Fill"
            Cull Off
            ZTest [_ZTest]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            Stencil
            {
                Ref 1
                Comp NotEqual
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float3 smoothNormal : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _OutlineColor;
            float _OutlineWidth;
            float _UseSmoothNormals;

            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position = UnityObjectToClipPos(input.vertex);

                float3 normal = _UseSmoothNormals > 0.5 && any(input.smoothNormal)
                    ? input.smoothNormal : input.normal;
                float3 viewNormal = mul((float3x3)UNITY_MATRIX_IT_MV, normal);
                viewNormal *= rsqrt(max(dot(viewNormal, viewNormal), 1e-12));

                // 透视相机按视深补偿外扩宽度。
                // 正交相机按视野高度补偿外扩宽度。
                float scale = unity_OrthoParams.w > 0.5
                    ? unity_OrthoParams.y : -UnityObjectToViewPos(input.vertex).z;
                float3 offset = viewNormal * (scale * _OutlineWidth / 1000.0);

                // 单独投影外扩偏移，保证零宽度填充与遮罩重合。
                output.position += mul(UNITY_MATRIX_P, float4(offset, 0.0));
                return output;
            }

            fixed4 frag() : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
}
