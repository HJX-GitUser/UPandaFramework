# 隧道暗角资源

[TunnelingVignette.cs](TunnelingVignette.cs) 当前是占位组件，Start 与 Update 为空。它不创建暗角、读取运动速度或注册任何 Volume 和 Renderer Feature。暗角素材单独放在 [Shader 资源库](../../Shader/ShaderLibrary/Library/BuildIn/Tunneling%20Vignette/)。

## 使用素材

目录包含手写 [Shader](../../Shader/ShaderLibrary/Library/BuildIn/Tunneling%20Vignette/TunnelingVignette.shader)、[旧版 Universal Unlit Shader Graph](../../Shader/ShaderLibrary/Library/BuildIn/Tunneling%20Vignette/TunnelingVignetteSG.shadergraph)、半球模型、材质和预制体。手写版本是半球网格上的透明覆盖效果，使用 UV 和 Alpha 计算开口及过渡。材质属性包括 `_ApertureSize`、`_FeatheringEffect`、`_VignetteColor` 和 `_VignetteColorBlend`。

原预制体的 Mesh、材质以及材质的 Shader GUID 与仓库的对应资源不一致。试用时可用当前 FBX、材质和 Shader 建立新的半球对象，放在目标相机下，按视野与裁剪面调整尺寸，再核对开口、边缘过渡及透明层顺序。旧 Shader Graph 需要在目标 Unity 和管线中检查导入与生成结果。运行时动态收缩视野仍需业务驱动材质参数。 [视觉资源说明](../../Shader/README.md)
