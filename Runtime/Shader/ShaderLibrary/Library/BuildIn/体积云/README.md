# 切片式体积云

`Custom/Clouds` 是 Built-in 的半透明 Surface Shader。`CloudsVolume` 把同一网格沿世界 Y 轴分层提交，Shader 按世界位置采样两层滚动噪声，并用高度、视距和覆盖率计算密度。各层叠加形成厚度和明暗，采用的是切片近似。组件编入 `UPandaGF.Visuals`，通过 `ExecuteInEditMode` 在编辑模式也会执行。 [云 Shader](Clouds.shader) [绘制组件](CloudsVolume.cs)

## 配置

1. 在 Built-in 场景里创建一个云层对象，挂 `CloudsVolume`，分配一个平面网格给 `Coludmesh`。设置对象旋转，使平面沿世界 XZ 展开。
2. 创建使用 `Custom/Clouds` 的材质并启用 GPU Instancing。分别绑定两张噪声贴图，填写 `cloudsMaterial`。每组云层使用独立材质，组件每次 Update 会写入中心高度和厚度。
3. 使用正数 `volumeSamples` 和 `volumeSize`，先从较少切片调起。相邻间隔为 `volumeSize / volumeSamples / 2`，世界 Y 上的位置由对象中心往上下铺开。
4. 调整 `_Coverage`、`_Softness`、`_ViewDistance` 和 `_DirectionSpeed` 控制外观。可用 `_Tiling01`、`_Tiling02` 和两层权重分开调整大块轮廓与细节。

[噪声一](../../../../UnityShader学习文档/案例/8.体积云/NoiseClouds01.png) [噪声二](../../../../UnityShader学习文档/案例/8.体积云/NoiseClouds02.png) [体积云学习笔记](../../../../UnityShader学习文档/案例/8.体积云/体积云.md)

## 示例与性能边界

随附场景和材质保留了旧脚本、Shader、材质、纹理引用，和当前资源的 meta 不匹配。可将场景当作对象布局参照，使用当前类型和资源重新绑定。缺材质时 Update 会直接报错，场景需保证渲染平台支持 GPU Instancing，实例数不可超过 Unity 单次提交限制。

当前 Update 每次新建矩阵数组，对各切片调用一次 `Graphics.DrawMesh`，之后还提交整批 `Graphics.DrawMeshInstanced`。这会重复提交切片。半透明层数、覆盖屏幕的面积和阴影都影响开销，建议在目标平台通过 Frame Debugger 核对绘制批次，再测实际负载。该 Shader 依赖 Built-in 光照与 Surface Shader 生成流程，迁移到 SRP 需要独立实现。 [原场景](ShaderScene.unity) [配套材质](Custom_Clouds.mat)
