// =============================================================================
//  Custom/Clouds —— 切片式体积云（Layered / Slice-based Volumetric Clouds）
// -----------------------------------------------------------------------------
//  本文件由 Amplify Shader Editor (ASE) 生成后人工加注。
//  完整原理、使用方式与调参说明见同目录 README.md。
//
//  一句话原理：外部脚本 CloudsVolume.cs 每帧把同一个平面 Mesh 沿 Y 轴堆叠 N 份
//  （Graphics.DrawMeshInstanced），形成一叠"薄片"；每一片都用本 Shader 以半透明
//  方式绘制，叠加在一起就在视觉上近似出一团体积云。
//
//  本 Shader 由四部分组成：
//    1. vertexDataFunc()                  → 顶点阶段：传递视空间深度（供距离淡出用）
//    2. surf()                            → 表面阶段：计算云的颜色（散射 + 环境光 + 雾色）
//    3. LightingStandardCustomLighting()  → 自定义光照：计算云的 alpha（= 云密度）
//    4. Pass "ShadowCaster"                → 阴影投射：用抖动遮罩做半透明裁剪
//
//  注意：颜色直接写进 Emission（自发光风格），所以 Tags 里 IsEmissive=true、
//         ForceNoShadowCasting=true，云不会被当作普通 PBR 材质走光照。
// =============================================================================
Shader "Custom/Clouds" {
    Properties {
        // -------------------------------- 通用 --------------------------------
        _ViewDistance ("View Distance", Float) = 5000                       // 可见距离：视距超过它的云片淡出为全透明
        _DirectionSpeed ("Direction / Speed", Vector) = (20, 5, 10, -5)     // 两层噪声的滚动方向与速度：(第1层 xy, 第2层 zw)

        // ---------------------------- 噪声 1（大尺度层） ----------------------------
        [Header(Main)][Space(10)]_Noise01 ("Noise 01", 2D) = "white" { }    // 第 1 层噪声图（灰度噪声图即可，只取 R 通道）
        _Tiling01 ("Tiling", Float) = 0.02                                  // 第 1 层缩放（内部会 /10000，换算成很大尺度的 UV）
        _Noise01Power ("Power", Range(0, 1)) = 0.75                         // 第 1 层权重（越大，大尺度云团越明显）

        // ----------------------------- 噪声 2（细节层） -----------------------------
        [Space(30)]_Noise02 ("Noise 02", 2D) = "white" { }                  // 第 2 层噪声图（提供细节与"翻滚"感）
        _Tiling02 ("Tiling", Float) = 0.03                                  // 第 2 层缩放
        _Noise02Power ("Power", Range(0, 1)) = 0.3                          // 两层噪声的混合系数（对 max 混合结果做 lerp）

        // ------------------------------- 云形态 -------------------------------
        [Space(10)][Header(Clouds)][Space(10)]_ScatteringColor ("Color", Color) = (1, 1, 1, 1) // 散射色：按垂直密度剖面叠加到云体上
        _Coverage ("Coverage", Range(0, 1)) = 0.3                           // 云量：同时影响垂直衰减指数与密度阈值，越大云越厚越实
        _Softness ("Softness", Range(0, 1)) = 0.25                          // 柔和度：密度曲线的 pow 指数，越大边缘过渡越柔
        [Toggle(_USEFOG_ON)] _UseFog ("Use Fog", Float) = 1                 // 开关：勾选后云色随视距在"雾色 ↔ 云色"之间插值

        // ------------------------------- 散射 -------------------------------
        [Space(10)][Header(Scattering)][Space(10)]_CloudsColor ("Color", Color) = (0.1843137, 0.3568628, 0.4627451, 1) // 远处 / 雾中的云体基础色
        [HideInInspector]_cloudsPosition ("cloudsPosition", Float) = 1      // 云层中心的世界 Y（由 CloudsVolume.cs 每帧写入 transform.position.y，勿手改）
        _ScatteringPower ("Absorption", Range(0, 50)) = 10                  // 吸收/衰减指数：越大则只有最厚的中心区域才出现明亮散射
        [HideInInspector]_cloudsHeight ("cloudsHeight", Float) = 1          // 云层厚度（由 CloudsVolume.cs 写入 volumeSize，勿手改）
        [HideInInspector] __dirty ("", Int) = 1                             // ASE 内部占位参数，无实际作用
    }

    SubShader {
        // 透明队列（Transparent+0，排在普通透明物之后）；IsEmissive 表示颜色按自发光处理；ForceNoShadowCasting 由 ASE 默认写入
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+0" "IgnoreProjector" = "True" "ForceNoShadowCasting" = "True" "IsEmissive" = "true" }
        Cull Off                // 双面渲染：云片从任意角度看都可见
        CGINCLUDE
        // ---- 公共 include：表面着色器所需的标准光照 / 变量 / 工具库 ----
        #include "UnityPBSLighting.cginc"
        #include "UnityShaderVariables.cginc"
        #include "UnityCG.cginc"
        #include "Lighting.cginc"
        #pragma target 3.0
        #pragma shader_feature_local _USEFOG_ON         // 由 [Toggle(_USEFOG_ON)] 生成的本地关键字开关

        // 自定义 Input：本 Shader 只需要「世界坐标」和「视空间深度」两项数据
        struct Input {
            float3 worldPos;        // 世界坐标：用于噪声采样、视线方向、云层高度计算
            float eyeDepth;         // 视空间深度：由 vertexDataFunc 写入，用于距离淡出
        };

        // 自定义表面输出结构：在标准 SurfaceOutput 之外额外携带两份数据——
        //   SurfInput：把 Input 从 surf() 原样传给自定义光照函数
        //   GIData   ：GI 输入（由下方 _GI 回调塞入），使 surf() 内可访问 _LightColor0 / unity_AmbientSky 等
        struct SurfaceOutputCustomLightingCustom {
            half3 Albedo;
            half3 Normal;
            half3 Emission;
            half Metallic;
            half Smoothness;
            half Occlusion;
            half Alpha;
            Input SurfInput;
            UnityGIInput GIData;
        };

        // ------------- 属性对应的 uniform（与 Properties 一一对应，顺序由 ASE 生成） -------------
        uniform float _cloudsPosition;      // 云层中心 Y（C# 写入）
        uniform float _cloudsHeight;        // 云层厚度 H（C# 写入）
        uniform float _Coverage;            // 云量
        uniform float _ScatteringPower;     // 吸收指数
        uniform float4 _ScatteringColor;    // 散射色
        uniform float4 _CloudsColor;        // 云体基础色
        uniform float _ViewDistance;        // 可见距离
        uniform sampler2D _Noise02;         // 细节层噪声
        uniform float4 _DirectionSpeed;     // (第1层 xy, 第2层 zw) 滚动方向/速度
        uniform float _Tiling02;            // 细节层缩放
        uniform sampler2D _Noise01;         // 大尺度层噪声
        uniform float _Tiling01;            // 大尺度层缩放
        uniform float _Noise01Power;        // 第 1 层权重
        uniform float _Noise02Power;        // 两层混合系数
        uniform float _Softness;            // 柔和度（密度曲线指数）

        // =====================================================================
        // 【函数】vertexDataFunc —— 顶点函数：仅向 Input 写入视空间深度
        // 作用：计算顶点在视空间中的深度（相机到顶点的正向距离），存入 Input.eyeDepth；
        //       光照函数再用它做距离淡出（DistanceFade）。不做任何顶点变形。
        // 参数：v = 顶点数据（appdata_full，此处只读 v.vertex）；o = 输出结构 Input
        // 调用者：主表面着色器（#pragma vertex:vertexDataFunc）与 ShadowCaster Pass 复用
        // =====================================================================
        void vertexDataFunc(inout appdata_full v, out Input o) {
            UNITY_INITIALIZE_OUTPUT(Input, o);                      // 先把所有字段清零
            o.eyeDepth = -UnityObjectToViewPos(v.vertex.xyz).z;     // 视空间 Z 取反 = 正向深度（正值）
        }

        // =====================================================================
        // 【函数】LightingStandardCustomLighting —— 自定义光照函数（本 Shader 的"密度计算器"）
        // 作用：颜色已由 surf() 写进 Emission，所以这里 c.rgb 恒为 0，真正被使用的只有
        //       c.a（alpha）——它代表云的不透明度，交给 alpha:fade 做混合。
        //       密度 = 噪声 × 垂直剖面，再乘以距离淡出。
        // 参数：s = 表面输出（含 surf() 填好的 SurfInput）；viewDir / gi 在本函数内未使用
        // 返回：half4，其中只有 .a 有效
        // =====================================================================
        inline half4 LightingStandardCustomLighting(inout SurfaceOutputCustomLightingCustom s, half3 viewDir, UnityGI gi) {
            UnityGIInput data = s.GIData;
            Input i = s.SurfInput;                                  // 取出顶点阶段传来的 worldPos / eyeDepth
            half4 c = 0;

            // ---------- ① 距离淡出：视距越远越透明（避免远处云片出现硬边） ----------
            // (eyeDepth - 近裁面) / _ViewDistance  →  0(近) ~ 1(远)
            float cameraDepthFade66 = ((i.eyeDepth - _ProjectionParams.y - 0.0) / _ViewDistance);
            // 翻转成权重：1 = 近处完整参与混合，0 = 超出 _ViewDistance 后完全透明
            float DistanceFade124 = (1.0 - cameraDepthFade66);
            // ---------- ② 两层流动噪声：在【世界 XZ 平面】上随时间滚动 ----------
            // appendResult161 = _DirectionSpeed.zw → 第 2 层（细节层）的滚动方向与速度
            float2 appendResult161 = (float2(_DirectionSpeed.z, _DirectionSpeed.w));
            float3 ase_worldPos = i.worldPos;
            // appendResult4 = 像素的世界 XZ 坐标。所有云片共用同一套 XZ 噪声，因此叠加后像"一整团"云
            float2 appendResult4 = (float2(ase_worldPos.x, ase_worldPos.z));
            // panner159 = 世界 XZ + 时间 × 速度 → 随时间平移的采样坐标
            float2 panner159 = (_Time.y * appendResult161 + appendResult4);
            // 采样第 2 层噪声并取 R 通道（灰度图）；Tiling/10000 是把面板上的 0.02~0.05 换算成极大尺度的 UV
            float3 temp_cast_1 = (tex2D(_Noise02, (panner159 * (_Tiling02 / 10000.0))).r).xxx;   // ASE 生成的冗余临时变量，未使用
            float3 temp_cast_2 = (tex2D(_Noise02, (panner159 * (_Tiling02 / 10000.0))).r).xxx;
            float3 linearToGamma92 = LinearToGammaSpace(temp_cast_2);  // 线性→Gamma：让噪声在屏幕上的对比更"自然"
            // appendResult160 = _DirectionSpeed.xy → 第 1 层（大尺度层），此处方向/速度要取负号
            float2 appendResult160 = (float2(_DirectionSpeed.x, _DirectionSpeed.y));
            float2 panner157 = (_Time.y * appendResult160 + appendResult4);
            float3 temp_cast_3 = (tex2D(_Noise01, (panner157 * (_Tiling01 / 10000.0))).r).xxx;   // 同上，冗余临时变量
            float3 temp_cast_4 = (tex2D(_Noise01, (panner157 * (_Tiling01 / 10000.0))).r).xxx;
            float3 linearToGamma91 = LinearToGammaSpace(temp_cast_4);

            // ---------- ③ 两层噪声混合：lerp(目标, max(源, 目标), Power) ≈ 变亮/叠加混合 ----------
            float3 blendOpSrc155 = linearToGamma92;                     // 源：细节层
            float3 blendOpDest155 = (linearToGamma91 * _Noise01Power);  // 目标：大尺度层 × 权重
            // _Noise02Power 决定在"纯大尺度层"与"两层取较亮者"之间插值
            float3 lerpBlendMode155 = lerp(blendOpDest155, max(blendOpSrc155, blendOpDest155), _Noise02Power);
            float3 Noise121 = (saturate(lerpBlendMode155));             // 最终噪声值（0~1）

            // ---------- ④ 垂直密度剖面：离云层中心越远密度越小（近似球/水滴的天然剖面） ----------
            float Coverage211 = _Coverage;
            // d = |云层中心Y - 像素Y| / 云层厚度 → 0(中心) ~ 1+(边缘)
            // 指数用 (1 - Coverage)：云量越大，剖面越"扁平"，云整体越厚实
            float CloudHeight130 = (1.0 - pow(saturate((abs((_cloudsPosition - ase_worldPos.y)) / _cloudsHeight)), (1.0 - Coverage211)));

            // ---------- ⑤ 密度阈值：把 Coverage 映射为截断阈值 m = 1 - Coverage/0.98 ----------
            float3 temp_cast_5 = ((1.0 - (0.0 + (Coverage211 - 0.0) * (1.0 - 0.0) / (0.98 - 0.0)))).xxx;
            float3 temp_cast_6 = (_Softness).xxx;                       // pow 指数 = Softness

            // ---------- ⑥ 云覆盖率：先 remap 把 [m,1] 拉满到 [0,1]，再用 pow(Softness) 调边缘软硬 ----------
            float3 CloudCoverage134 = pow(saturate((float3(0, 0, 0) + ((Noise121 * CloudHeight130) - temp_cast_5) * (float3(1, 0, 0) - float3(0, 0, 0)) / (float3(1, 0, 0) - temp_cast_5))), temp_cast_6);

            // ---------- ⑦ 输出：颜色交给 surf()，这里只输出 alpha ----------
            c.rgb = 0;                                                  // 颜色不在这里计算
            c.a = saturate((DistanceFade124 * CloudCoverage134)).x;     // alpha = 距离淡出 × 云覆盖率
            return c;
        }

        // =====================================================================
        // 【函数】LightingStandardCustomLighting_GI —— GI 数据转发回调
        // 作用：Unity 在调用自定义光照函数之前会先调它，把完整的 UnityGIInput
        //       （光照贴图坐标、环境光探针、阴影坐标等）挂到 s.GIData 上，
        //       使 surf() 里能取到 _LightColor0、unity_AmbientSky 等光照信息。
        // 参数：s = 输出（写入 GIData）；data = 输入 GI 数据；gi = 此处不改写
        // =====================================================================
        inline void LightingStandardCustomLighting_GI(inout SurfaceOutputCustomLightingCustom s, UnityGIInput data, inout UnityGI gi) {
            s.GIData = data;
        }

        // =====================================================================
        // 【函数】surf —— 表面函数：计算云的颜色并写入 Emission
        // 作用：最终颜色由三部分相加：
        //       ① Scattering119             —— 正向散射（逆光看云边缘发亮）+ 环境光贡献，按“厚度”加权
        //       ② _ScatteringColor * 厚度    —— 云体自身的散射质感
        //       ③ lerp(雾色, _CloudsColor)   —— 云的基础色，随距离从“云色”渐变到“雾色”
        //       注意：不透明度（Alpha）不在这里输出，它由 LightingStandardCustomLighting 决定。
        // 参数：i = Input（worldPos / eyeDepth）；o = 自定义表面输出（写 Emission）
        // =====================================================================
        void surf(Input i, inout SurfaceOutputCustomLightingCustom o) {
            o.SurfInput = i;                                        // 透传 Input，供自定义光照函数使用
            // 光照贴图兼容分支（ASE 生成）：启用 LightMap 时拿不到 _LightColor0，置 0
            #if defined(LIGHTMAP_ON) && (UNITY_VERSION < 560 || (defined(LIGHTMAP_SHADOW_MIXING) && !defined(SHADOWS_SHADOWMASK) && defined(SHADOWS_SCREEN)))//aselc
                float4 ase_lightColor = 0;
            #else //aselc
                float4 ase_lightColor = _LightColor0;               // 主平行光（太阳）的颜色
            #endif //aselc
            float3 ase_worldPos = i.worldPos;
            float3 ase_worldViewDir = Unity_SafeNormalize(UnityWorldSpaceViewDir(ase_worldPos));  // 世界空间视线方向（像素→相机）
            #if defined(LIGHTMAP_ON) && UNITY_VERSION < 560 //aseld
                float3 ase_worldlightDir = 0;
            #else //aseld
                float3 ase_worldlightDir = normalize(UnityWorldSpaceLightDir(ase_worldPos));      // 世界空间光线方向（像素→光源）
            #endif //aseld

            // ---------- ① 正向散射：视线越"迎着"光线，散射越强（云逆光时的镶边亮部） ----------
            float dotResult109 = dot(ase_worldViewDir, -ase_worldlightDir);     // 视线与逆光方向的夹角余弦
            float temp_output_114_0 = pow(saturate(dotResult109), 5.0);         // pow(cos,5)：廉价的前向散射相位函数（Henyey-Greenstein 近似）

            // ---------- ② 复用垂直密度剖面，作为"厚度"权重（与光照函数中的算法一致） ----------
            float Coverage211 = _Coverage;
            float CloudHeight130 = (1.0 - pow(saturate((abs((_cloudsPosition - ase_worldPos.y)) / _cloudsHeight)), (1.0 - Coverage211)));
            // pow(厚度, _ScatteringPower)：吸收越强，只有最厚的中心区域才有明亮散射
            float temp_output_197_0 = pow((1 * CloudHeight130), _ScatteringPower);

            // ---------- ③ 散射项：太阳前向散射 + 天空环境光散射，整体再乘厚度 ----------
            float4 Scattering119 = (((ase_lightColor * temp_output_114_0) + (temp_output_114_0 * unity_AmbientSky * ase_lightColor)) * temp_output_197_0);
            float Lerp223 = temp_output_197_0;                          // 厚度权重（ASE 起的名字叫 Lerp，实际不是插值系数）

            // ---------- ④ 距离淡出与雾色插值 ----------
            float cameraDepthFade66 = ((i.eyeDepth - _ProjectionParams.y - 0.0) / _ViewDistance);
            float DistanceFade124 = (1.0 - cameraDepthFade66);
            #ifdef _USEFOG_ON
                float staticSwitch192 = saturate((DistanceFade124 / 1.0));  // 勾选 Use Fog：远处趋向雾色
            #else
                float staticSwitch192 = 1.0;                                // 不勾选：始终使用 _CloudsColor
            #endif
            float4 lerpResult181 = lerp(unity_FogColor, _CloudsColor, staticSwitch192);   // 云体基础色（雾色 ↔ 云色）

            // ---------- ⑤ 合成最终颜色 → 写入 Emission（当自发光输出，不再走 PBR） ----------
            float4 CloudLighting137 = (Scattering119 + ((_ScatteringColor * Lerp223) + lerpResult181));
            o.Emission = CloudLighting137.rgb;
        }

        ENDCG
        CGPROGRAM
        // 表面着色器声明：
        //   surf StandardCustomLighting → 使用本文件的自定义光照函数
        //   alpha:fade                   → 传统透明混合（SrcAlpha OneMinusSrcAlpha）
        //   keepalpha                    → 保留写入的 alpha 通道（不被雾/其他处理覆盖成 1）
        //   fullforwardshadows           → 前向渲染下处理所有阴影类型
        //   nofog                        → 关闭 Unity 自动雾效（雾已在 surf 里手工处理）
        //   vertex:vertexDataFunc        → 使用自定义顶点函数
        #pragma surface surf StandardCustomLighting alpha:fade keepalpha fullforwardshadows nofog vertex:vertexDataFunc

        ENDCG
        // =====================================================================
        // Pass: ShadowCaster —— 阴影投射通道（由 ASE 自动生成）
        // 作用：即使主材质写了 ForceNoShadowCasting，ASE 仍会生成该 Pass。它先用
        //       surf + 光照函数算出该像素的 alpha（即云密度），再用 _DitherMaskLOD
        //       这个 3D 抖动遮罩做 clip，使“半透明的云”投出带噪点的柔和阴影，
        //       而不是一块实心黑斑。
        // =====================================================================
        Pass {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_shadowcaster
            #pragma multi_compile UNITY_PASS_SHADOWCASTER
            #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
            #include "HLSLSupport.cginc"
            #if (SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN)
                #define CAN_SKIP_VPOS     // 这些平台支持 SV_Position，可省掉 VPOS 参数
            #endif
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
            #include "UnityPBSLighting.cginc"
            sampler3D _DitherMaskLOD;    // Unity 内置的抖动遮罩（Bayer 风格阈值表），用于模拟半透明阴影

            // 阴影投射用的顶点→片元结构：
            //   customPack1 携带顶点阶段算出的 eyeDepth
            //   worldPos    世界坐标（片元里重建 Input 时用）
            struct v2f {
                V2F_SHADOW_CASTER;
                float1 customPack1 : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            // =====================================================================
            // 【函数】vert —— 阴影投射通道的顶点函数
            // 作用：复用主通道的 vertexDataFunc 拿到 eyeDepth，再按法线偏移算出阴影投射位置。
            // 参数：v = 顶点数据；返回：v2f（含阴影投射坐标 + 自定义数据）
            // =====================================================================
            v2f vert(appdata_full v) {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                Input customInputData;
                vertexDataFunc(v, customInputData);                     // 复用主通道的顶点逻辑
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                half3 worldNormal = UnityObjectToWorldNormal(v.normal);
                o.customPack1.x = customInputData.eyeDepth;
                o.worldPos = worldPos;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)                 // 按法线偏移写入阴影投射坐标
                return o;
            }
            // =====================================================================
            // 【函数】frag —— 阴影投射通道的片元函数
            // 作用：先重建 surf 所需的 Input，用 surf + 光照函数算出云密度 alpha，
            //       再用 3D 抖动遮罩按 alpha 做概率性裁剪（clip），得到“半透明”的阴影。
            // 参数：IN = 顶点阶段输出；vpos（部分平台）= 屏幕像素坐标；返回：阴影图颜色值
            // =====================================================================
            half4 frag(v2f IN
            #if !defined(CAN_SKIP_VPOS)
                , UNITY_VPOS_TYPE vpos : VPOS
            #endif
            ) : SV_Target {
                UNITY_SETUP_INSTANCE_ID(IN);
                Input surfIN;
                UNITY_INITIALIZE_OUTPUT(Input, surfIN);
                surfIN.eyeDepth = IN.customPack1.x;                     // 从顶点阶段拿回 eyeDepth
                float3 worldPos = IN.worldPos;
                half3 worldViewDir = normalize(UnityWorldSpaceViewDir(worldPos));
                surfIN.worldPos = worldPos;
                SurfaceOutputCustomLightingCustom o;
                UNITY_INITIALIZE_OUTPUT(SurfaceOutputCustomLightingCustom, o)
                surf(surfIN, o);                                        // 算颜色（此处只用得上 alpha）
                UnityGI gi;
                UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
                o.Alpha = LightingStandardCustomLighting(o, worldViewDir, gi).a;   // 拿回云密度作为 alpha
                #if defined(CAN_SKIP_VPOS)
                    float2 vpos = IN.pos;
                #endif
                // 用屏幕坐标 + alpha 查抖动遮罩：alpha 越小，被裁掉的像素比例越高
                half alphaRef = tex3D(_DitherMaskLOD, float3(vpos.xy * 0.25, o.Alpha * 0.9375)).a;
                clip(alphaRef - 0.01);                                  // 抖动阈值裁剪 → 模拟半透明阴影
                SHADOW_CASTER_FRAGMENT(IN)
            }
            ENDCG
        }
    }
    Fallback "Diffuse"
}