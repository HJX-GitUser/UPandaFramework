//==================================================================================================
//  水面着色器 —— Unity 内置渲染管线（Built-in RP）/ Surface Shader 实现
//--------------------------------------------------------------------------------------------------
//  功能一览：
//    1. 动态波浪      ：世界空间 XZ 驱动，三重正弦波叠加（主波 + 45° 副波 + 垂直细波）做顶点偏移
//    2. 法线扰动      ：两张法线贴图以不同平铺 + 不同速度滚动，whiteout 混合
//    3. 反射          ：默认取 unity_SpecCube0（反射探针 / 天空盒），也可指定自定义 Cubemap，支持 mip 模糊
//    4. 折射          ：GrabPass 抓屏 + 法线扭曲 UV，强度可调（含背景被水体吸收的深/浅水渐变）
//    5. 菲涅尔        ：Schlick 近似，视角越平反射越强
//    6. 颜色          ：浅水色 × 背景 → 深水色，由「水深（相机深度纹理）」驱动
//    7. 泡沫          ：岸边（深度）+ 波峰/陡坡（波形 & 法线倾斜）+ 噪声图案 × 遮罩
//
//  使用步骤：
//    a) 新建材质并指定本 Shader；两张法线贴图请在导入设置里把 Texture Type 设为 Normal map
//    b) 水面物体建议用「细分过的大平面」（默认 Plane 只有 10×10 格，波浪会显得很生硬），
//       需要竖面/水下的场合把 SubShader 里的 Cull Back 改为 Cull Off
//    c) 场景中放一个 Reflection Probe（或让天空盒生效）才能有可信的反射
//    d) 若深浅水/岸边泡沫不生效：说明相机的深度纹理没生成，代码里加
//          camera.depthTextureMode |= DepthTextureMode.Depth;
//
//  渲染设置：Queue = Transparent(3000)、ZWrite Off、alpha 混合；ForwardBase 逐像素主光 + ForwardAdd 逐像素附加光
//  性能提示（移动端）：
//    · GrabPass 是全屏拷贝，是最大的开销；不需要折射时把 _RefractionStrength 设为 0，
//      并且直接删掉 GrabPass { "_WaterGrabTexture" } 与 surf 里的折射采样即可
//    · 材质面板上的 _DEPTH_FADE / _FOAM_MASK / _CUSTOM_CUBE 三个开关是 shader_feature，
//      关掉后对应分支代码不会进入变体，可显著减少采样与指令数
//    · 关掉 _DEPTH_FADE 后仍可用「离相机越远越深」的近似，只是没有岸边泡沫和真实水深
//==================================================================================================

Shader "Custom/Water Surface (Built-in)"
{
    Properties
    {
        // ============================ 颜色 / 水深 ============================
        _ShallowColor       ("浅水颜色（与背景相乘做水体吸收）", Color) = (0.45, 0.80, 0.86, 1)
        _DeepColor          ("深水颜色",                        Color) = (0.02, 0.09, 0.16, 1)
        _Alpha              ("整体透明度 Alpha",                Range(0, 1))  = 0.9
        _DepthFadeDistance  ("深浅水过渡距离（米）",            Range(0.05, 50)) = 6

        // ============================== 波浪 ==============================
        _WaveAmplitude      ("波高 Amplitude（米）",       Range(0, 2))  = 0.12
        _WaveLength         ("波长 Wavelength（米）",      Range(0.2, 60)) = 8
        _WaveSpeed          ("波速 Speed（米/秒）",        Range(0, 10)) = 1.2
        _WaveDirection      ("波浪方向 Direction (XY)",    Vector) = (1, 0.35, 0, 0)

        // ============================== 法线 ==============================
        [Normal] [NoScaleOffset] _NormalMap1 ("法线贴图 A", 2D) = "bump" {}
        [Normal] [NoScaleOffset] _NormalMap2 ("法线贴图 B", 2D) = "bump" {}
        _NormalScale        ("法线强度",              Range(0, 2))     = 0.85
        _NormalTiling       ("法线平铺（次/米）",     Range(0.001, 1)) = 0.05
        _NormalTiling2      ("法线贴图 B 平铺倍数",   Range(0.1, 8))   = 1.7
        _NormalSpeed1       ("法线贴图 A 滚动速度 XY", Vector) = (0.02, 0.03, 0, 0)
        _NormalSpeed2       ("法线贴图 B 滚动速度 XY", Vector) = (0.03, -0.018, 0, 0)

        // ============================ 反射 / 折射 ============================
        _ReflectionIntensity("反射强度",             Range(0, 2))   = 0.75
        _ReflectionBlur     ("反射模糊（粗糙度）",   Range(0, 1))   = 0.15
        [NoScaleOffset] _ReflectionCube ("自定义反射 Cubemap", Cube) = "_Skybox" {}
        [Toggle(_CUSTOM_CUBE)] _UseCustomCube ("使用自定义 Cubemap（关闭=用反射探针）", Float) = 0
        _RefractionStrength ("折射强度",             Range(0, 0.2)) = 0.02
        [Toggle(_DEPTH_FADE)] _UseDepthFade ("使用相机深度纹理（深浅水 / 岸边泡沫）", Float) = 1

        // ========================== 菲涅尔 / 高光 ==========================
        _FresnelPower       ("菲涅尔指数",           Range(0.1, 10)) = 4
        _FresnelBias        ("菲涅尔基础反射 F0",    Range(0, 0.5))  = 0.03
        _SpecTint           ("高光颜色",             Color) = (1, 1, 1, 1)
        _SpecPower          ("高光锐度",             Range(1, 512)) = 128
        _SpecIntensity      ("高光强度",             Range(0, 3))   = 0.6

        // ============================== 泡沫 ==============================
        [NoScaleOffset] _FoamMap  ("泡沫贴图 (R)", 2D) = "white" {}
        [NoScaleOffset] _FoamMask ("泡沫遮罩 (R)", 2D) = "white" {}
        [Toggle(_FOAM_MASK)] _UseFoamMask ("使用泡沫遮罩", Float) = 1
        _FoamColor          ("泡沫颜色",             Color) = (0.95, 0.98, 1, 1)
        _FoamIntensity      ("泡沫强度",             Range(0, 3))     = 1
        _FoamThreshold      ("泡沫阈值",             Range(0, 0.95))  = 0.45
        _FoamTiling         ("泡沫平铺（次/米）",    Range(0.001, 1)) = 0.08
        _FoamSpeed          ("泡沫滚动速度 XY",      Vector) = (0.015, 0.012, 0, 0)
        _FoamShoreWidth     ("岸边泡沫宽度（米）",   Range(0.01, 10)) = 1.0
        _FoamCrestStart     ("波峰泡沫起始 (0~1)",   Range(0, 1))     = 0.65
    }

    SubShader
    {
        Tags
        {
            "RenderType"       = "Transparent"
            "Queue"            = "Transparent"      // 3000：在不透明物体之后渲染，GrabPass 才能抓到背景
            "IgnoreProjector"  = "True"
            "PreviewType"      = "Plane"
        }

        LOD 200
        ZWrite Off                                  // 透明水面不写深度
        Cull Off                                   // 需要从水下看到水面时改成 Cull Off

        //------------------------------------------------------------------
        // 抓屏：把当前帧缓冲拷贝到 _WaterGrabTexture，供折射使用
        // 用「带名字」的写法，一帧只会抓一次（多个水面共享同一张），比 GrabPass {} 省很多
        //------------------------------------------------------------------
        GrabPass { "_WaterGrabTexture" }

        CGPROGRAM
        // surf = 表面函数，WaterLight = 自定义光照模型，vert = 顶点修改函数
        // alpha:fade = 传统 alpha 混合；fullforwardshadows = 支持点光/聚光灯阴影
        #pragma surface surf WaterLight vertex:vert alpha:fade fullforwardshadows
        #pragma target 3.0

        // 三个功能开关（shader_feature：未被任何材质开启的分支会被剥离，不占运行时开销）
        #pragma shader_feature_local _CUSTOM_CUBE
        #pragma shader_feature_local _DEPTH_FADE
        #pragma shader_feature_local _FOAM_MASK

        #include "UnityCG.cginc"

        //============================== 属性 ==============================
        fixed4 _ShallowColor;
        fixed4 _DeepColor;
        half   _Alpha;
        float  _DepthFadeDistance;

        half   _WaveAmplitude;
        half   _WaveLength;
        half   _WaveSpeed;
        float4 _WaveDirection;

        sampler2D _NormalMap1;
        sampler2D _NormalMap2;
        half   _NormalScale;
        half   _NormalTiling;
        half   _NormalTiling2;
        float4 _NormalSpeed1;
        float4 _NormalSpeed2;

        half   _ReflectionIntensity;
        half   _ReflectionBlur;
        samplerCUBE _ReflectionCube;
        half   _RefractionStrength;

        half   _FresnelPower;
        half   _FresnelBias;
        fixed4 _SpecTint;
        half   _SpecPower;
        half   _SpecIntensity;

        sampler2D _FoamMap;
        sampler2D _FoamMask;
        fixed4 _FoamColor;
        half   _FoamIntensity;
        half   _FoamThreshold;
        half   _FoamTiling;
        float4 _FoamSpeed;
        half   _FoamShoreWidth;
        half   _FoamCrestStart;

        sampler2D _WaterGrabTexture;    // GrabPass 的结果
        sampler2D _CameraDepthTexture;  // 相机深度纹理（不透明物体的深度）

        #define WATER_TWO_PI 6.28318530718

        // ComputeGrabScreenPos 内部的 y 轴缩放常量（必须与 UnityCG.cginc 保持一致）
        #ifdef UNITY_UV_STARTS_AT_TOP
            #define WATER_GRAB_Y_SCALE (-1.0)
        #else
            #define WATER_GRAB_Y_SCALE ( 1.0)
        #endif

        //--------------------------------------------------------------
        // 由「抓屏坐标」换算出「屏幕坐标」
        // 移动端的插值器很宝贵（目标 3.0 只有 10 个），grabPos 与 screenPos 只在 y 缩放上不同：
        //     grab.y   = pos.y*0.5 * WATER_GRAB_Y_SCALE + w
        //     screen.y = pos.y*0.5 * _ProjectionParams.x  + w    （x 分量两者完全一致）
        // 所以只插值一份 grabPos，采样 _CameraDepthTexture 时再换算，省下 1 个插值器。
        // （_ProjectionParams.x 为 -1 表示当前是渲染到 RenderTexture，例如开了后处理时）
        //--------------------------------------------------------------
        float4 MakeScreenPos (float4 grabPos)
        {
            float2 clipHalf = grabPos.xy - grabPos.w;                       // = pos.xy * 0.5
            clipHalf.y *= _ProjectionParams.x * WATER_GRAB_Y_SCALE;         // 换算成屏幕坐标的 y
            return float4(float2(clipHalf.x, clipHalf.y) + grabPos.w, grabPos.zw);
        }

        //============================== 输入结构 ==============================
        // 说明：worldNormal（INTERNAL_DATA）这类「自动成员」由 Surface Shader 生成器填充；
        //       其余成员必须自己在顶点函数里赋值。
        //       屏幕坐标 screenPos 这里故意不声明 —— 它由 grabPos 现算（见 MakeScreenPos），
        //       因为移动端插值器有限，少一个是一个。
        struct Input
        {
            float4 grabPos;                   // 自定义：抓屏投影坐标（折射 & 深度纹理共用）
            float3 wPos;                      // 自定义：顶点位移后的世界坐标
            float3 worldNormal; INTERNAL_DATA // 自动：世界法线（配合 WorldNormalVector 做逐像素法线变换）
            half   crest;                     // 自定义：归一化波峰高度 0(波谷)~1(波峰)
        };

        //============================== 波形函数 ==============================
        // 三重正弦波叠加：主方向波 + 45° 副波 + 垂直细波，返回世界空间垂直位移（米）
        // crest：把所有波形归一化到 0~1，供「波峰泡沫」判断使用
        float WaveHeight (float2 p, out half crest)
        {
            float amp   = _WaveAmplitude;
            float len   = max(_WaveLength, 0.05);
            float speed = _WaveSpeed;

            // 主方向（世界 XZ 平面），非法输入时退化为 +X
            float2 d1 = _WaveDirection.xy;
            d1 = (dot(d1, d1) > 1e-6) ? normalize(d1) : float2(1.0, 0.0);
            // 派生方向：旋转 45° 与旋转 90°，省掉两个「方向」参数
            float2 d2 = float2(d1.x * 0.70710678 - d1.y * 0.70710678,
                               d1.x * 0.70710678 + d1.y * 0.70710678);
            float2 d3 = float2(-d1.y, d1.x);

            float t  = _Time.y * speed;
            float k1 = WATER_TWO_PI / len;      // 波数 k = 2π/λ
            float k2 = k1 * 1.73;               // 副波：波长 / 1.73
            float k3 = k1 * 3.11;               // 细波：波长 / 3.11

            // 相位 = k·(d·p) - ω·t，ω = k·speed ⇒ speed 即相速度（米/秒）
            float h = sin(dot(p, d1) * k1 - t * k1)          * amp
                    + sin(dot(p, d2) * k2 - t * k2 * 1.15)   * amp * 0.50
                    + sin(dot(p, d3) * k3 - t * k3 * 1.35)   * amp * 0.25;

            float maxAmp = amp * 1.75;
            crest = (maxAmp > 1e-5) ? saturate(h / maxAmp * 0.5 + 0.5) : 0.5;
            return h;
        }

        //============================== 顶点函数 ==============================
        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);

            // 物体空间 → 世界空间 → 加波浪高度 → 再转回物体空间（对平移/旋转/缩放都成立）
            float3 wPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            half   crest;
            wPos.y += WaveHeight(wPos.xz, crest);
            v.vertex = mul(unity_WorldToObject, float4(wPos, 1.0));

            o.wPos  = wPos;
            o.crest = crest;
            // 折射用的抓屏坐标必须自己算：surface 的 screenPos 不能用于 GrabPass
            o.grabPos = ComputeGrabScreenPos(UnityObjectToClipPos(v.vertex));
        }

        //============================== 表面函数 ==============================
        void surf (Input IN, inout SurfaceOutput o)
        {
            float  t = _Time.y;
            float3 V = normalize(_WorldSpaceCameraPos - IN.wPos);   // 世界空间视线方向（指向相机）

            //--------------------------------------------------------------
            // 1. 双法线贴图滚动混合
            //    用「世界空间 XZ」当 UV：平铺与水面物体的尺寸/UV 展开无关，多块水面也能无缝拼接
            //--------------------------------------------------------------
            float2 uvBase = IN.wPos.xz * _NormalTiling;

            half3 n1 = UnpackNormal(tex2D(_NormalMap1, uvBase + _NormalSpeed1.xy * t));
            half3 n2 = UnpackNormal(tex2D(_NormalMap2, uvBase * _NormalTiling2 + _NormalSpeed2.xy * t));

            // whiteout 混合：两层法线叠加且不会像 xy 直接相加那样过曝失真
            half3 nT = normalize(half3(n1.xy + n2.xy, n1.z * n2.z));
            nT.xy *= _NormalScale;
            nT = normalize(nT);

            o.Normal = nT;                                  // 切空间法线（光照模型用）
            float3 N = WorldNormalVector(IN, o.Normal);     // 世界空间法线（含扰动）

            // 法线「倾斜度」：波面越陡越接近 1，用来在波浪破碎/陡坡处生成泡沫
            half slope = saturate((1.0 - nT.z) * 2.0);

            //--------------------------------------------------------------
            // 2. 菲涅尔（Schlick 近似）：边缘/远处反射更强
            //--------------------------------------------------------------
            half NdotV   = saturate(dot(N, V));
            half fresnel = _FresnelBias + (1.0 - _FresnelBias) * pow(1.0 - NdotV, _FresnelPower);
            fresnel = saturate(fresnel);

            //--------------------------------------------------------------
            // 3. 水深 → 吸收程度（0 = 极浅，1 = 深水）
            //--------------------------------------------------------------
            float depthDiff = 0.0;
            half  absorb;

            #ifdef _DEPTH_FADE
                // 本像素水面视深：ComputeGrabScreenPos 的 w 分量经透视校正插值后恰好等于线性视深
                float surfaceZ = IN.grabPos.w;
                // 背景（不透明物体）视深（水面自身是透明的，不写深度，所以不会污染深度纹理）
                float sceneZ = LinearEyeDepth(
                    SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(MakeScreenPos(IN.grabPos))));
                depthDiff = max(sceneZ - surfaceZ, 0.0);
                absorb    = saturate(depthDiff / max(_DepthFadeDistance, 0.001));
            #else
                // 退化方案：没有深度纹理时用「离相机越远越深」近似（浅水/岸边泡沫会失效）
                absorb = saturate(distance(_WorldSpaceCameraPos, IN.wPos) / max(_DepthFadeDistance, 0.001));
            #endif

            //--------------------------------------------------------------
            // 4. 折射：GrabPass 抓屏 + 法线扭曲 UV
            //--------------------------------------------------------------
            float2 refractOffset = nT.xy * _RefractionStrength;
            refractOffset.x *= _ScreenParams.y / _ScreenParams.x;   // 让屏幕空间偏移在横纵方向上等长

            float4 grabUV = IN.grabPos;
            grabUV.xy += refractOffset * grabUV.w;                  // 乘 w 抵消 tex2Dproj 的透视除法

            half3 bg = tex2Dproj(_WaterGrabTexture, grabUV).rgb;

            // 水体颜色：浅水 = 背景被浅水色染色（水体吸收），深水 = 完全被深水色取代
            half3 waterBody = lerp(bg * _ShallowColor.rgb, _DeepColor.rgb, absorb);

            //--------------------------------------------------------------
            // 5. 反射：反射探针（unity_SpecCube0）或自定义 Cubemap
            //    lod 由 _ReflectionBlur 控制 → 模糊反射（粗糙水面）
            //--------------------------------------------------------------
            float3 R   = reflect(-V, N);
            half   lod = _ReflectionBlur * 7.0;     // 0~7 级 mip

            #ifdef _CUSTOM_CUBE
                half3 refl = texCUBElod(_ReflectionCube, float4(R, lod)).rgb;
            #else
                half3 refl = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, R, lod),
                                       unity_SpecCube0_HDR).rgb;
            #endif

            //--------------------------------------------------------------
            // 6. 泡沫：岸边（深度）+ 波峰/陡坡（波形 & 法线）+ 噪声图案 × 遮罩
            //--------------------------------------------------------------
            half foamNoise = tex2D(_FoamMap, IN.wPos.xz * _FoamTiling + _FoamSpeed.xy * t).r;

            #ifdef _FOAM_MASK
                half foamMask = tex2D(_FoamMask, IN.wPos.xz * _FoamTiling).r;
            #else
                half foamMask = 1.0;
            #endif

            half shoreFoam = 0.0;
            #ifdef _DEPTH_FADE
                shoreFoam  = 1.0 - saturate(depthDiff / max(_FoamShoreWidth, 0.001));
                shoreFoam *= shoreFoam;     // 平方 → 收窄成沿岸的一条带
            #endif

            // 波峰 / 陡坡处破碎的白沫
            half crestFoam = saturate((IN.crest - _FoamCrestStart) / max(0.001, 1.0 - _FoamCrestStart));
            crestFoam = max(crestFoam, slope * 0.5);

            half foamSrc = saturate(max(shoreFoam, crestFoam) * foamNoise * foamMask * _FoamIntensity);
            half foam    = smoothstep(_FoamThreshold, min(0.999, _FoamThreshold + 0.35), foamSrc);

            //--------------------------------------------------------------
            // 7. 合成
            //--------------------------------------------------------------
            half  reflWeight = saturate(fresnel * _ReflectionIntensity);
            half3 color      = lerp(waterBody, refl, reflWeight);
            color            = lerp(color, _FoamColor.rgb, foam);

            // 水面本身不做漫反射：背景光照已经包含在折射结果里，走 Self-Illumination 通道输出，
            // 直接输出到 Emission 可避免「折射背景再被光照乘一遍」的重复受光。
            o.Albedo   = 0;
            o.Emission = color;             // 折射 + 反射 + 泡沫
            o.Gloss    = _SpecIntensity;    // 交给下面的自定义光照模型当高光强度
            o.Specular = 0;
            // 泡沫处不透明；视角越平（反射越强）也越不透明
            o.Alpha    = saturate(lerp(_Alpha, 1.0, foam) + fresnel * 0.4);
        }

        //============================== 自定义光照模型 ==============================
        // SurfaceOutput 的 Albedo 恒为 0，这里只算「太阳/灯光的镜面反光」，其余交给 Emission
        inline half4 LightingWaterLight (SurfaceOutput s, half3 lightDir, half3 viewDir, half atten)
        {
            half3 h    = normalize(lightDir + viewDir);
            half  nh   = saturate(dot(s.Normal, h));
            half  spec = pow(nh, _SpecPower) * s.Gloss;
            // Albedo 为 0，保留漫反射项便于二次开发（改成非 0 即可让水面参与漫反射）
            half  diff = saturate(dot(s.Normal, lightDir)) * s.Albedo;

            half4 c;
            c.rgb = _LightColor0.rgb * _SpecTint.rgb * (spec + diff) * atten;   // 阴影里反光消失
            c.a   = s.Alpha;

            #ifdef UNITY_PASS_FORWARDADD
                // ForwardAdd 是加色混合，若再叠加 alpha 会把目标 alpha 推过 1，污染后续透明物体
                c.a = 0.0;
            #endif
            return c;
        }
        ENDCG
    }

    // 不提供后备着色器：①不生成 ShadowCaster 通道 → 水面不投射阴影（需求里的「关闭阴影投射」）
    //                 ②若需要水面投影，改成 FallBack "Diffuse" 即可
    FallBack Off
}
