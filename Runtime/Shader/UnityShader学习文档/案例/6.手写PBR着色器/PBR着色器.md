# PBR_Manual 着色器

一个**手写的 PBR（Physically Based Rendering，基于物理的渲染）着色器**，采用 Cook-Torrance 微表面模型。用于学习 PBR 原理，或作为自定义 PBR 效果的起点（对标 Unity 内置 Standard 着色器的手写实现）。

## Shader
```hlsl
// ============================================================
// 手写 PBR（基于物理的渲染）着色器
// ------------------------------------------------------------
// 着色模型：Cook-Torrance 微表面模型
//   · D（法线分布）：GGX / Trowbridge-Reitz
//   · F（Fresnel）：Schlick 近似
//   · G（几何遮蔽）：Smith（Schlick-GGX）
//
// 光照组成：
//   · 直接光 = 漫反射（Lambert）+ 镜面高光（Cook-Torrance），含阴影衰减
//   · 间接光 = 环境光/光照探针（ShadeSH9）+ 反射探针（按粗糙度采样 mip）
//
// 支持特性：法线贴图、金属度/光滑度贴图、AO 贴图、雾效、阴影接收与投射
// 用途：学习 / 自定义 PBR 效果（对标 Unity Standard 着色器的手写实现）
// ============================================================
Shader "Custom/PBR_Manual"
{
    Properties
    {
        // 反照率贴图：物体基础颜色（RGB）
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        // 法线贴图：切线空间法线，表现表面凹凸细节
        _BumpMap ("Normal Map", 2D) = "bump" {}
        // 金属度/光滑度贴图：R 通道 = 金属度，A 通道 = 光滑度
        _MetallicGlossMap ("Metallic (R) Smoothness (A)", 2D) = "white" {}
        // 金属度标量：0 = 非金属（电介质），1 = 金属（无贴图时使用）
        _Metallic ("Metallic", Range(0,1)) = 0.0
        // 光滑度标量：0 = 粗糙，1 = 镜面（无贴图时使用）
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        // 环境光遮蔽贴图：R 通道，减弱间接光在角落/缝隙处的亮度
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        // 法线贴图开关：勾选后启用 _USE_NORMAL_MAP 关键字
        [Toggle(_USE_NORMAL_MAP)] _UseNormalMap ("Use Normal Map", Float) = 0
        // 金属度/光滑度贴图开关：勾选后启用 _USE_METALLIC_MAP 关键字
        [Toggle(_USE_METALLIC_MAP)] _UseMetallicMap ("Use Metallic Map", Float) = 0
    }
    SubShader
    {
        // 标记为不透明，用于渲染分类与替换
        Tags { "RenderType"="Opaque" }
        LOD 300

        Pass
        {
            Name "FORWARD"
            // 前向渲染主 Pass：处理主方向光 + 阴影 + 光照探针
            Tags { "LightMode"="ForwardBase" }

            CGPROGRAM
            #pragma vertex vert      // 顶点着色器入口函数名
            #pragma fragment frag    // 片元着色器入口函数名
            // 前向渲染基础变体集：生成 DIRECTIONAL / LIGHTPROBE_SH / 阴影 / 顶点光 / 光照贴图等变体
            #pragma multi_compile_fwdbase
            // 雾效变体集：生成 FOG_LINEAR / FOG_EXP / FOG_EXP2 变体
            #pragma multi_compile_fog
            // 材质关键字：仅当材质勾选对应 Toggle 时才编译该分支
            #pragma shader_feature _USE_NORMAL_MAP
            #pragma shader_feature _USE_METALLIC_MAP
            #include "UnityCG.cginc"                  // 常用工具函数（矩阵、光照、雾、辅助函数）
            #include "AutoLight.cginc"                // 阴影相关宏（SHADOW_COORDS / TRANSFER_SHADOW 等）
            #include "UnityGlobalIllumination.cginc"  // GI 相关结构体与函数（本文件保留备用）

            // ---- 顶点着色器输入：来自网格的数据 ----
            struct appdata
            {
                float4 vertex : POSITION;  // 模型空间顶点坐标
                float3 normal : NORMAL;    // 模型空间法线
                float4 tangent : TANGENT;  // 模型空间切线（w 分量存手性 ±1）
                float2 uv : TEXCOORD0;     // 第一套 UV
            };

            // ---- 顶点着色器输出 → 片元着色器输入 ----
            struct v2f
            {
                float2 uv : TEXCOORD0;           // 传递 UV
                float4 pos : SV_POSITION;        // 裁剪空间坐标（顶点着色器必须输出）
                float3 worldPos : TEXCOORD1;     // 世界坐标
                float3 worldNormal : TEXCOORD2;  // 世界法线
                float3 worldTangent : TEXCOORD3; // 世界切线
                float3 worldBinormal : TEXCOORD4;// 世界副切线（bitangent）
                SHADOW_COORDS(5)                 // 声明阴影坐标插值器（占用 TEXCOORD5）
                UNITY_FOG_COORDS(6)              // 声明雾效坐标插值器（占用 TEXCOORD6）
            };

            // ---- 与 Properties 对应的材质变量声明 ----
            sampler2D _MainTex;
            float4 _MainTex_ST;    // 主贴图的 Tiling/Offset，由 TRANSFORM_TEX 使用
            sampler2D _BumpMap;
            sampler2D _MetallicGlossMap;
            sampler2D _OcclusionMap;
            float _Metallic;
            float _Smoothness;

            v2f vert (appdata v)
            {
                v2f o;
                // 模型空间 → 裁剪空间（等价于 mul(UNITY_MATRIX_MVP, v.vertex)）
                o.pos = UnityObjectToClipPos(v.vertex);
                // 模型空间 → 世界空间（unity_ObjectToWorld 为模型矩阵）
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                // 法线转到世界空间：内部用逆转置矩阵，正确处理非均匀缩放
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                // 切线方向转到世界空间（不含平移）
                o.worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
                // 副切线 = 法线 × 切线 × 手性；叉积方向由 v.tangent.w 校正
                o.worldBinormal = cross(o.worldNormal, o.worldTangent) * v.tangent.w;
                // UV 乘以贴图的 Tiling/Offset（_MainTex_ST）
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // 计算并传递阴影坐标（与 SHADOW_COORDS 配套）
                TRANSFER_SHADOW(o);
                // 计算并传递雾效坐标（与 UNITY_FOG_COORDS 配套）
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            // ============================================================
            // PBR 核心函数（Cook-Torrance 的三个组成项）
            // ============================================================

            // 法线分布函数 D（NDF）：GGX / Trowbridge-Reitz
            // 描述微表面法线朝向的分布，roughness 越大高光越扩散
            // NdotH = dot(N, H)，H 为半角向量
            float D_GGX(float NdotH, float roughness)
            {
                float a = roughness * roughness;              // α = roughness²（Disney 重映射）
                float a2 = a * a;
                float denom = NdotH * NdotH * (a2 - 1.0) + 1.0;
                return a2 / (UNITY_PI * denom * denom);       // UNITY_PI 为圆周率 π 常量
            }

            // Fresnel 项 F：Schlick 近似
            // 描述反射率随视角的变化：掠射角（视线与表面近乎平行）时反射最强
            // HdotV = dot(H, V)，F0 为垂直入射反射率
            float3 F_Schlick(float HdotV, float3 F0)
            {
                // pow(x, 5.0)：x 的 5 次幂，Schlick 近似的固定指数
                return F0 + (1.0 - F0) * pow(1.0 - HdotV, 5.0);
            }

            // 几何遮蔽项 G（单方向）：Schlick-GGX
            // 描述微表面之间相互遮挡造成的光线损失
            // k = (r+1)²/8 用于直接光照（IBL 间接光照应使用 k = r²/2）
            float G_SchlickGGX(float NdotV, float roughness)
            {
                float r = roughness + 1.0;
                float k = (r * r) / 8.0;
                return NdotV / (NdotV * (1.0 - k) + k);
            }

            // 几何遮蔽项 G：Smith（入射与出射两个方向的乘积）
            float G_Smith(float NdotL, float NdotV, float roughness)
            {
                return G_SchlickGGX(NdotL, roughness) * G_SchlickGGX(NdotV, roughness);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 采样反照率贴图
                fixed4 albedo = tex2D(_MainTex, i.uv);

                // ---- 法线 ----
                float3 N;
                #if defined(_USE_NORMAL_MAP)
                    // UnpackNormal：把 DXT5nm 压缩的法线从 [0,1] 解码回 [-1,1]
                    float3 normalTS = UnpackNormal(tex2D(_BumpMap, i.uv));
                    // 构造切线→世界矩阵（三列分别为切线/副切线/法线）
                    float3x3 tangentToWorld = float3x3(i.worldTangent, i.worldBinormal, i.worldNormal);
                    // 把切线空间法线转到世界空间
                    N = normalize(mul(normalTS, tangentToWorld));
                #else
                    // 无贴图时直接使用插值后的世界法线
                    N = normalize(i.worldNormal);
                #endif

                // ---- 金属度与粗糙度 ----
                float metallic = _Metallic;
                float smoothness = _Smoothness;
                #if defined(_USE_METALLIC_MAP)
                    fixed4 metalGloss = tex2D(_MetallicGlossMap, i.uv);
                    metallic = metalGloss.r;   // R 通道 = 金属度
                    smoothness = metalGloss.a; // A 通道 = 光滑度
                #endif
                float roughness = 1.0 - smoothness;  // 粗糙度 = 1 - 光滑度
                roughness = max(roughness, 0.001);   // 下限防 0（纯镜面会导致高光走样）

                // 采样环境光遮蔽（AO）
                float ao = tex2D(_OcclusionMap, i.uv).r;

                // F0（垂直入射反射率）与漫反射颜色
                // 电介质固定约 4% 反射率；金属则用 Albedo 颜色作为反射率
                float3 F0 = lerp(0.04, albedo.rgb, metallic);
                // 金属无漫反射，因此乘以 (1 - metallic)
                float3 diffuseColor = albedo.rgb * (1.0 - metallic);

                // ---- 视角与光照方向 ----
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos); // 视线方向（相机→片元）
                float3 L = normalize(_WorldSpaceLightPos0.xyz);          // 主光源方向（方向光）
                float3 H = normalize(L + V);                             // 半角向量（Blinn-Phong 式）

                float NdotL = saturate(dot(N, L));   // 法线·光线（saturate 截断到 0~1）
                float NdotV = saturate(dot(N, V));   // 法线·视线
                float NdotH = saturate(dot(N, H));   // 法线·半角
                float HdotV = saturate(dot(H, V));   // 半角·视线（即 VdotH）

                // ---- 直接光照（Cook-Torrance） ----
                float3 directLight = 0;
                if (NdotL > 0)  // 只有面朝光源时才计算
                {
                    float D = D_GGX(NdotH, roughness);           // 法线分布
                    float3 F = F_Schlick(HdotV, F0);             // Fresnel
                    float G = G_Smith(NdotL, NdotV, roughness);  // 几何遮蔽

                    // 镜面项：Cook-Torrance BRDF，分母防止除零
                    float3 specular = (D * F * G) / max(4.0 * NdotL * NdotV, 0.0001);
                    // 漫反射项：Lambert（除以 π 保证能量守恒）
                    float3 diffuse = diffuseColor / UNITY_PI;

                    // 能量守恒：被镜面反射掉的光不再参与漫反射
                    float3 kS = F;                             // 镜面系数 = Fresnel
                    float3 kD = (1.0 - kS) * (1.0 - metallic); // 漫反射系数

                    // UNITY_LIGHT_ATTENUATION：计算光照 + 阴影衰减（让物体接收阴影）
                    UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
                    directLight = (kD * diffuse + specular) * NdotL * _LightColor0.rgb * atten;
                }

                // ---- 间接光照 ----
                // ShadeSH9：采样环境光 + 光照探针（球谐函数），返回环境色（已在活动色彩空间）
                half3 ambient = ShadeSH9(half4(N, 1));

                // 间接漫反射 = 环境色 × Albedo × AO
                float3 indirectDiffuse = ambient * albedo.rgb * ao;

                // 间接镜面反射：按粗糙度选择反射探针 mip（粗糙表面反射更模糊），并应用 Fresnel
                float3 reflDir = reflect(-V, N);                    // 反射方向（reflect 为反射向量函数）
                float mip = roughness * UNITY_SPECCUBE_LOD_STEPS;   // 粗糙度 → mip 级（UNITY_SPECCUBE_LOD_STEPS = 6）
                // UNITY_SAMPLE_TEXCUBE_LOD：按指定 mip 级采样反射探针 cubemap
                // DecodeHDR：解码 HDR cubemap 颜色（unity_SpecCube0_HDR 为解码参数）
                half3 envSpec = DecodeHDR(UNITY_SAMPLE_TEXCUBE_LOD(unity_SpecCube0, reflDir, mip), unity_SpecCube0_HDR);
                float3 fresnel = F_Schlick(NdotV, F0);              // Fresnel 权重（掠射角反射增强）
                float3 indirectSpecular = envSpec * fresnel;

                // 最终颜色 = 直接光 + 间接漫反射 + 间接镜面
                float3 finalColor = directLight + indirectDiffuse + indirectSpecular;

                // 应用雾效（UNITY_APPLY_FOG 根据距离把颜色向雾色混合）
                UNITY_APPLY_FOG(i.fogCoord, finalColor);
                return fixed4(finalColor, 1.0);
            }
            ENDCG
        }


        // 阴影投射 Pass：复用内置 VertexLit 的 ShadowCaster，让物体能投射阴影
        UsePass "VertexLit/SHADOWCASTER"
    }
    // 当不支持本 Shader 时回退到 Diffuse
    FallBack "Diffuse"
}
```

## 功能特性

- **完整 Cook-Torrance BRDF**：GGX 法线分布 + Schlick Fresnel + Smith 几何遮蔽
- **金属度/光滑度工作流**：支持标量或贴图（`_MetallicGlossMap` 的 R=金属度、A=光滑度）
- **法线贴图**：支持切线空间法线，表现表面凹凸细节
- **环境光遮蔽（AO）**：减弱间接光在角落/缝隙处的亮度
- **阴影**：既能投射阴影（复用内置 ShadowCaster），也能接收阴影（含阴影衰减）
- **间接光照**：环境光 + 光照探针（球谐 `ShadeSH9`）+ 反射探针（按粗糙度采样 mip）
- **雾效**：支持线性/指数雾
- **关键字开关**：法线贴图、金属度贴图可开关，减少无谓采样与变体

## 使用步骤

1. 在 Project 面板右键 → **Create → Material**；
2. 在材质 Inspector 顶部的 Shader 下拉框中选择 **Custom → PBR_Manual**；
3. 给材质指定 Albedo 贴图（必须），并按需指定法线/金属度/AO 贴图；
4. 把材质拖到场景中的模型上即可。

## 参数说明

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `_MainTex` | 2D | white | 反照率贴图（基础颜色，RGB） |
| `_BumpMap` | 2D | bump | 法线贴图（切线空间） |
| `_MetallicGlossMap` | 2D | white | 金属度(R) + 光滑度(A) 贴图 |
| `_Metallic` | Range(0,1) | 0.0 | 金属度（无贴图时使用）；0=电介质，1=金属 |
| `_Smoothness` | Range(0,1) | 0.5 | 光滑度（无贴图时使用）；0=粗糙，1=镜面 |
| `_OcclusionMap` | 2D | white | 环境光遮蔽贴图（R 通道） |
| `_UseNormalMap` | Toggle | 关 | 启用法线贴图 |
| `_UseMetallicMap` | Toggle | 关 | 启用金属度/光滑度贴图 |

## PBR 模型简介

着色器使用经典的 **Cook-Torrance** 微表面 BRDF：

$$
f_{specular} = \frac{D \cdot F \cdot G}{4 \, (N \cdot L)(N \cdot V)}
$$

- **D（法线分布函数，NDF）**：GGX / Trowbridge-Reitz，决定高光的扩散程度；
- **F（Fresnel）**：Schlick 近似，决定反射率随视角的变化（掠射角反射最强）；
- **G（几何遮蔽）**：Smith（Schlick-GGX），决定微表面相互遮挡造成的光线损失。

漫反射项为 Lambert 模型（$diffuseColor / \pi$），并通过 Fresnel 做能量守恒：被镜面反射掉的光不再参与漫反射。

## 光照组成

```
最终颜色 = 直接光 + 间接漫反射 + 间接镜面反射
```

| 项 | 来源 | 说明 |
|----|------|------|
| 直接光 | 主方向光（ForwardBase） | Cook-Torrance 镜面 + Lambert 漫反射，乘以阴影衰减 |
| 间接漫反射 | 环境光 + 光照探针 | `ShadeSH9` 球谐采样 × Albedo × AO |
| 间接镜面 | 反射探针 | 按粗糙度采样 cubemap mip，乘 Fresnel 权重 |

## 渲染设置

- `LightMode = ForwardBase`：前向渲染主 Pass，处理主方向光；
- `multi_compile_fwdbase`：生成阴影/光照贴图/光照探针/顶点光等变体；
- `multi_compile_fog`：生成雾效变体；
- 阴影投射复用内置 `VertexLit/SHADOWCASTER` Pass。

## 已知局限

- **仅支持单个方向光**：没有 `ForwardAdd` Pass，不支持多光源、点光源、聚光灯；
- **不支持烘焙光照贴图**：缺少 `LIGHTMAP_ON` 相关采样；
- **间接镜面为简化实现**：未做 box projection、多探针混合、DFG 预积分能量补偿；
- **假设主光为方向光**：`_WorldSpaceLightPos0.xyz` 按方向光处理。

## 扩展方向

- 添加 `ForwardAdd` Pass 支持多光源；
- 添加 `LIGHTMAP_ON` 变体支持烘焙光照贴图；
- 间接镜面使用 `UNITY_BRDF_GI` 或 DFG LUT 做更精确的能量守恒；
- 增加 `_PBR 参数` 的贴图组合（如粗糙度贴图单独控制）。
