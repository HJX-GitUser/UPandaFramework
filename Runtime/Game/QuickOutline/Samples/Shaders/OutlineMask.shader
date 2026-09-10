//
//  OutlineMask.shader
//  QuickOutline
//
//  Created by Chris Nolet on 2/21/18.
//  Copyright © 2018 Chris Nolet. All rights reserved.
//
// =============================================================================
//  一、职责：描边方案的第 ① 步 —— 把「物体本体」覆盖到的像素在模板缓冲里标记为 1
// =============================================================================
//  本文件与 OutlineFill.shader 成对工作（两个材质由 OutDrawline.cs 追加到同一个
//  Renderer 的材质列表末尾），完整流程：
//
//    ① 本文件（Queue = Transparent+100）：先画
//         ColorMask 0                      不输出任何颜色（屏幕上看不到这一次绘制）
//         ZWrite Off                       不写深度，避免污染后续物体的深度测试
//         Stencil { Ref 1, Pass Replace }  通过深度测试的像素 → 模板值写为 1
//       ⇒ 模板缓冲中「本体」= 1，其余像素 = 0
//
//    ② OutlineFill（Queue = Transparent+110）：后画
//         顶点沿"视图空间法线"向外扩出一圈，并且
//         Stencil { Ref 1, Comp NotEqual } 只在本体之外（模板 != 1）的像素着色
//       ⇒ 外扩轮廓被本体"挖空"，剩下环绕物体的一圈 —— 这就是描边
//
//  即：本 Pass 不产生任何可见像素，它的全部意义就是"把本体区域涂进模板缓冲"，
//      供后一个 Pass 做"挖洞"用。
//
// =============================================================================
//  二、为什么这个 Pass 里没有 CGPROGRAM / 顶点片元程序？
// =============================================================================
//  这是刻意写的"只有渲染状态、不做着色"的 Pass：它唯一目的是借光栅化把模板值写进屏幕
//  像素，不需要计算任何颜色。Unity 会给这种没有携带着色器程序的 Pass 使用内置默认程序
//  完成顶点变换（模型空间 → 裁剪空间），因此几何照常被光栅化并参与深度 / 模板测试；
//  再配合 ColorMask 0，这次绘制不会向颜色缓冲写入任何内容。
//  ⇒ 所以看到 Pass 里"空空如也"是正常的，不是漏写。
//     （如果遇到个别图形 API 的兼容问题时，也可以显式补一段极简的 vert/frag：
//       顶点用 UnityObjectToClipPos 变换、片元返回 fixed4(0,0,0,0)，效果等价。）
//
// =============================================================================
//  三、渲染状态逐条说明
// =============================================================================
//  Tags
//    Queue = "Transparent+100"   透明队列偏移 100：确保排在本体材质之后、OutlineFill(+110) 之前
//    RenderType = "Transparent"  归类为透明物体（相机裁剪 / 替换 Shader 会读取它）
//    注意：这里没有声明 DisableBatching —— 本 Pass 不依赖对象空间的自定义数据，
//          动态合批不会影响模板写入结果（OutlineFill 才必须关掉合批）
//
//  Pass 内
//    Cull Off        关闭背面剔除：正反面都参与光栅化，保证模板覆盖完整，
//                    不会因为"从里往外看"时本体被自己的背面剔除而漏写模板
//    ZTest [_ZTest]  深度测试方式由材质属性 _ZTest 控制，取值由 OutDrawline.cs 按模式设置：
//                      OutlineAll / OutlineVisible / OutlineHidden → Always（不看遮挡，全部写模板）
//                      OutlineAndSilhouette / SilhouetteOnly        → LessEqual（只有通过深度测试的像素才写模板）
//                    这个差别就是"被其它物体挡住的区域算不算本体"的开关，
//                    也是五种描边模式效果的真正来源
//    ZWrite Off      不写深度：避免把本体"钉"进深度缓冲，影响后续透明物体的排序与叠加
//    ColorMask 0     屏蔽全部颜色通道：只写模板不写颜色，因此这个 Pass 完全不可见
//    Stencil { Ref 1, Pass Replace }
//                      Ref 1        模板参考值
//                      Pass Replace 深度测试与模板测试都通过的像素 → 把模板值替换为 Ref(=1)
//                    未声明的 Fail / ZFail 动作默认是 Keep（不修改），本例也不需要
//
//  四、属性
//    [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest
//      —— [Enum(...)] 是材质面板绘制器，让这个 float 在 Inspector 里显示为枚举下拉框；
//         0 = Disabled（不做深度测试，效果等价于 Always）。运行时由脚本按模式改写。
//
//  五、内置函数
//    本文件不含任何着色器代码，因此不涉及内置函数；
//    UnityObjectToViewPos / UnityViewToClipPos / UNITY_MATRIX_IT_MV 等说明见 OutlineFill.shader
// =============================================================================

Shader "Custom/Outline Mask" {
  Properties {
    // [Enum(...)]：把 float 在 Inspector 里显示成 CompareFunction 下拉框
    // 0 = Disabled（不做深度测试，等价 Always）；由 OutDrawline.cs 按描边模式改写
    [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 0
  }

  SubShader {
    Tags {
      "Queue" = "Transparent+100"     // 排在本体之后、OutlineFill(+110) 之前
      "RenderType" = "Transparent"    // 归类为透明物体
    }

    Pass {
      Name "Mask"
      Cull Off                        // 双面光栅化，保证模板覆盖完整
      ZTest [_ZTest]                  // 深度测试方式由脚本按模式设置（决定"哪些像素算本体"）
      ZWrite Off                      // 不写深度，避免污染深度缓冲
      ColorMask 0                     // 屏蔽所有颜色通道：只写模板，不产生可见像素

      // 模板写入：深度与模板测试均通过的像素，模板值替换为 1
      // 本 Pass 没有 CGPROGRAM：只做渲染状态 + 光栅化，顶点变换由 Unity 内置默认程序完成
      Stencil {
        Ref 1
        Pass Replace
      }
    }
  }
}
