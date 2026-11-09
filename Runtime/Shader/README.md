# Shader 学习资料与视觉资源

本目录分为教学笔记和可导入的资源。教学部分包含 ShaderLab、常用数学与九个效果案例。资源部分提供手写 Shader、材质、模型和少量组件。`UPandaGF.Visuals` 没有对框架服务程序集的引用，`CloudsVolume` 是其中的运行组件。移动资源时应一起保留 meta，现有素材的引用状态见各项说明。 [Shader 基础](UnityShader学习文档/文档/Shader基础.md) [教学案例](UnityShader学习文档/案例/) [程序集](UPandaGF.Visuals.asmdef)

## 可导入资源

| 内容 | 实现与使用条件 |
| --- | --- |
| [水面](UnityShader学习文档/案例/9.水效果/README.md) | Built-in Surface Shader，使用 GrabPass 和可选深度纹理。当前材质的 Shader、法线需要重新绑定 |
| [切片体积云](ShaderLibrary/Library/BuildIn/体积云/README.md) | Built-in Surface Shader 与 C# 绘制组件。现成场景与材质保留了旧引用，按说明重绑后使用 |
| [隧道暗角](ShaderLibrary/Library/BuildIn/Tunneling%20Vignette/) | 半球模型、透明叠加 Shader 与旧版 Universal Unlit Shader Graph。当前 prefab 及材质的旧引用需重新绑定 |
| [置顶文字](ShaderLibrary/Library/BuildIn/UI/AlwaysOnTopText.shader) | UGUI Shader，以 `ZTest Always` 绕过深度遮挡，仍受透明队列和 Canvas 排序影响 |
| [OutLine](../Game/OutLine/README.md) | 单材质双 Pass 描边：先写模板缓冲，再沿法线外扩并靠模板挖出轮廓；颜色、宽度与宽度单位用 MaterialPropertyBlock 逐对象覆盖；带 URP 分支，HDRP 为可选模板 |

## 管线与验收

水面和体积云依赖 Built-in 的 Surface Shader 生成流程，无法直接作为 URP 或 HDRP 的材质运行。暗角手写 Shader 和置顶文字采用传统 ShaderLab/CG。旧 Shader Graph 也需要在目标 Unity 中确认升级后仍能生成正确的 Pass。即使素材导入成功，仍需在发布使用的相机、渲染管线和 Player 中核对渲染效果。

运行案例前先看 Inspector 中的脚本、Shader、材质和纹理引用，优先处理 Missing Script 和粉色材质，再评估画面与帧耗时。框架历史跨平台探针未覆盖这些视觉案例。 [跨平台验证边界](../../OptimzeLog/CrossPlatform/README.md)
