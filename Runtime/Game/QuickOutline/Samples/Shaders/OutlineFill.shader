//
//  OutlineFill.shader
//  QuickOutline
//
//  Created by Chris Nolet on 2/21/18.
//  Copyright © 2018 Chris Nolet. All rights reserved.
//
// =============================================================================
//  一、描边方案总览（本文件是其中的「填充」Pass，需配合 OutlineMask.shader）
// =============================================================================
//  两个 Shader 会被同时挂到同一个 Renderer 的材质列表末尾（由 OutDrawline.cs
//  在 OnEnable 里 append，并通过 CombineSubmeshes 追加一个重复子网格，让这两个
//  材质有几何可以绘制），依靠「模板缓冲 Stencil」把物体本体区域"抠掉"，只留下外圈：
//
//    ① OutlineMask（Queue = Transparent+100）先画：
//         ColorMask 0                       —— 只写模板，不输出任何颜色
//         Stencil { Ref 1, Pass Replace }    —— 物体本体覆盖到的像素，模板值写为 1
//       结果：模板缓冲中「本体」= 1，其余像素 = 0
//
//    ② OutlineFill（本文件，Queue = Transparent+110）后画：
//         先把顶点沿「视图空间法线」向外扩出一圈，再
//         Stencil { Ref 1, Comp NotEqual }   —— 只在模板 != 1（即本体之外）的像素着色
//       结果：外扩出的轮廓被本体"挖空"，只剩环绕物体的一圈 —— 这就是描边
//
//  外扩量随视距等比放大（见 vert 中乘以 -viewPosition.z），刚好抵消透视除法，
//  因此描边在屏幕上的像素宽度与物体远近无关。
//
//  各种描边模式（OutlineAll / OutlineVisible / OutlineHidden /
//  OutlineAndSilhouette / SilhouetteOnly）不在本文件里做分支，而是由
//  OutDrawline.cs 动态改 _ZTest（SilhouetteOnly 时还会把 _OutlineWidth 设为 0）
//  来切换，所以本 Shader 只有这一条固定管线。
//
// =============================================================================
//  二、本文件用到的内置函数 / 宏（逐个说明）
// =============================================================================
//  any(v)                       任一分量非 0 就返回 true。
//                               用于判断 TEXCOORD3 里的自定义平滑法线是否有效
//  normalize(v)                 向量单位化（v / length(v)），消除缩放造成的长度变化
//  mul(a, b)                    矩阵 × 矩阵、矩阵 × 向量（注意不是逐分量相乘）
//  UnityObjectToViewPos(pos)    模型空间顶点 → 视图空间
//                               等价于 mul(UNITY_MATRIX_MV, float4(pos, 1))
//  UnityViewToClipPos(pos)      视图空间顶点 → 裁剪空间
//                               等价于 mul(UNITY_MATRIX_P, float4(pos, 1))；
//                               之后的透视除法由 GPU 自动完成
//  UNITY_MATRIX_IT_MV           (M·V) 的逆转置矩阵。
//                               法线不能直接用 M·V 变换（非等比缩放下会不垂直于表面），
//                               必须用逆转置；这里只取 3x3 部分（法线是方向，不要平移）
//  UNITY_VERTEX_INPUT_INSTANCE_ID        顶点输入里的 GPU 实例 ID 字段
//  UNITY_SETUP_INSTANCE_ID(input)        顶点着色器开头取出实例 ID
//  UNITY_VERTEX_OUTPUT_STEREO            单通道立体渲染（VR）的输出字段
//  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output)  初始化上面的字段；
//                                        非 XR 平台以上三个宏都会展开为空，无运行时开销
//  fixed / fixed4 / float3 / float4      HLSL 数据类型：fixed 为低精度（颜色够用），
//                                        float 为全精度（坐标/法线用）
//  tex2D / saturate / lerp / dot         本 Shader 未使用（不需要采样贴图，也不做光照明暗）
// =============================================================================

Shader "Custom/Outline Fill" {
  Properties {
    // [Enum(...)] 是材质面板绘制器：把 float 在 Inspector 里显示成 CompareFunction 下拉框。
    // 0 = Disabled（不做深度测试，效果等价于 Always）。该值由 OutDrawline.cs 按描边模式改写：
    //   OutlineAll=Always(8) / OutlineVisible=LessEqual(4) / OutlineHidden=Greater(5) ...
    [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0

    _OutlineColor("Outline Color", Color) = (1, 1, 1, 1)   // 描边颜色：RGB 为颜色，A 为透明度
    _OutlineWidth("Outline Width", Range(0, 10)) = 2       // 描边宽度（0~10 的美术单位，见 vert 中的 /1000）
  }

  SubShader {
    Tags {
      "Queue" = "Transparent+110"     // 透明队列 +110：确保排在 OutlineMask(+100) 之后绘制
      "RenderType" = "Transparent"    // 归类为透明物体（供相机裁剪、替换 Shader 识别）
      "DisableBatching" = "True"      // 禁用动态合批：合批会在 CPU 端把顶点烘焙到世界空间并把
                                      // 对象矩阵置为单位矩阵，此时 TEXCOORD3 中"对象空间"的
                                      // 平滑法线会错位，导致描边断裂/闪烁
    }

    Pass {
      Name "Fill"
      Cull Off                        // 关闭背面剔除：正反面都绘制，避免只看到"半个描边"
      ZTest [_ZTest]                  // 深度测试方式由材质属性控制（用于切换描边模式）
      ZWrite Off                      // 不写深度：透明物体的常规做法，避免遮挡后面物体的深度
      Blend SrcAlpha OneMinusSrcAlpha // 标准 Alpha 混合：out = src*srcA + dst*(1-srcA)，支持半透明描边
      ColorMask RGB                   // 只写 RGB、不写 A 通道，避免破坏帧缓冲中已有的透明度信息

      // 模板测试：Ref=1，Comp=NotEqual ⇒ 只在"模板值不等于 1"的像素上通过（即物体本体之外）
      // 未显式声明的 Pass/Fail/ZFail 动作默认都是 Keep，所以本 Pass 只读模板、不修改它
      Stencil {
        Ref 1
        Comp NotEqual
      }

      CGPROGRAM
      #include "UnityCG.cginc"          // 引入 Unity 内置库：矩阵宏与 UnityObjectToViewPos 等函数

      #pragma vertex vert             // 指定顶点着色器入口函数名
      #pragma fragment frag           // 指定片元着色器入口函数名

      // 顶点输入结构：字段由网格数据自动填充
      struct appdata {
        float4 vertex : POSITION;         // 模型空间顶点坐标（必须带齐 4 个分量才能参与 4x4 变换）
        float3 normal : NORMAL;           // 模型空间法线（硬边模型在同位置会有多条不同法线）
        float3 smoothNormal : TEXCOORD3;  // UV3 通道承载的"平滑法线"：由 OutDrawline.cs 预计算并写入
                                          // （同一位置的顶点取法线平均值，避免硬边处描边裂开）
        UNITY_VERTEX_INPUT_INSTANCE_ID    // GPU 实例化 ID（多平台/实例化兼容所需）
      };

      // 顶点输出 → 片元输入
      struct v2f {
        float4 position : SV_POSITION;    // 裁剪空间坐标（系统语义，光栅化前由 GPU 自动做透视除法）
        fixed4 color : COLOR;             // 描边颜色，直接透传给片元着色器
        UNITY_VERTEX_OUTPUT_STEREO        // 立体渲染（VR）输出字段
      };

      uniform fixed4 _OutlineColor;       // 与 Properties 中的 _OutlineColor 对应（uniform：由材质传入）
      uniform float _OutlineWidth;        // 与 Properties 中的 _OutlineWidth 对应

      v2f vert(appdata input) {
        v2f output;

        UNITY_SETUP_INSTANCE_ID(input);                // 取出实例 ID（未启用实例化时展开为空）
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output); // 初始化 VR 立体输出（非 XR 平台展开为空）

        // 优先使用脚本写入 UV3 的平滑法线；SkinnedMeshRenderer 的 UV3 被脚本清零，
        // 此时 any() 为 false，回退到网格自带的硬边法线
        float3 normal = any(input.smoothNormal) ? input.smoothNormal : input.normal;

        // 顶点：模型空间 → 视图空间（相机空间；右手系，相机朝 -Z 方向看）
        float3 viewPosition = UnityObjectToViewPos(input.vertex);

        // 法线：模型空间 → 视图空间。
        // 用 UNITY_MATRIX_IT_MV 的 3x3 部分（逆转置），保证存在非等比缩放时法线仍垂直于表面；
        // normalize 消除变换带来的长度变化，使后面的外扩量只与 _OutlineWidth 有关
        float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, normal));

        // 核心：在视图空间沿法线外扩，再投影到裁剪空间
        //   -viewPosition.z        视图空间中相机前方的点 z 为负值，取负即"到相机的距离"
        //   乘以该距离             外扩量随距离等比增大，抵消透视除法的影响，
        //                          使描边在 NDC/屏幕上的宽度恒定（远近一样粗）
        //   _OutlineWidth / 1000   把 Inspector 的 0~10 换算为合适的世界空间尺度
        output.position = UnityViewToClipPos(viewPosition + viewNormal * -viewPosition.z * _OutlineWidth / 1000.0);
        output.color = _OutlineColor;

        return output;
      }

      fixed4 frag(v2f input) : SV_Target {
        return input.color;             // 纯色输出：颜色由顶点阶段透传，混合与模板测试由渲染状态完成
      }
      ENDCG
    }
  }
}
