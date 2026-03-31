Shader "Custom/LightWall"
{
    Properties
    {
        _MainColor ("MainColor", Color) = (0, 1, 1, 1)
        _GradientPower ("Intensity", Range(0.1, 5)) = 2
        _PulseSpeed ("PulseSpeed", Range(0.1, 5)) = 1
        _PulseStrength ("PulseIntensity", Range(0, 0.5)) = 0.1
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue" = "Transparent" 
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        
        LOD 100
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off//关闭剔除，启用双面渲染
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float height : TEXCOORD0;  // 存模型空间高度
                float pulse : TEXCOORD1;   // 存脉冲值
            };
            
            fixed4 _MainColor;
            float _GradientPower;
            float _PulseSpeed;
            float _PulseStrength;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                
                // 获取模型空间中的高度
                float worldPosY = mul(unity_ObjectToWorld, v.vertex).y;
                float minY = unity_ObjectToWorld._m13;  // 对象的世界坐标Y
                o.height = saturate(worldPosY - minY);//归一化到0-1范围
                
                // 计算脉冲
                float time = _Time.y * _PulseSpeed;
                o.pulse = sin(time) * _PulseStrength;
                
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                float gradient = 1.0 - pow(i.height, _GradientPower); //基础渐变,从底部完全不透明到顶部完全透明
                float pulsedGradient = 1.0 - pow(i.height - i.pulse, _GradientPower); //应用脉冲  在高度上叠加一个随时间变化的偏移
                // 将脉冲渐变和原始渐变混合，实现平滑过渡
                float finalAlpha = lerp(gradient, pulsedGradient, 0.5);
                
                finalAlpha = saturate(finalAlpha);//0-1
                
                fixed4 col = _MainColor;
                col.a = finalAlpha;
                
                return col;
            }
            ENDCG
        }
    }
    
    FallBack "Transparent/VertexLit"
}