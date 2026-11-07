# Built-in 水面

`Custom/Water Surface (Built-in)` 使用顶点波浪、双滚动法线、反射探针或手动 Cubemap、背景折射、深浅水与泡沫。它是 Built-in Surface Shader，依赖 GrabPass，不能直接用于 URP/HDRP。 [Shader](Water_Surface_Builtin.shader)

## 配置场景

1. 在 Built-in 场景中放置有足够细分的平面。顶点不足时，大波浪会呈折线。
2. 新建材质并选择 `Custom/Water Surface (Built-in)`，或复制随附材质。当前材质记录的 Shader 与两处法线 GUID 与文件的 meta 不一致，使用前重新指定 Shader 和贴图。法线贴图导入类型选 Normal map。
3. 场景中配置天空盒或 Reflection Probe。也可启用 `_UseCustomCube` 并填写 `_ReflectionCube`。
4. 使用深浅水和岸边泡沫时启用材质的 `_UseDepthFade`，并使渲染水面的相机生成深度纹理。深度分支开启时会直接采样深度，缺少有效纹理时没有自动可靠回退。

```csharp
GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth;
```

[配套材质](Water_Surface_Builtin.mat) [法线贴图](WaterBump.jpeg)

## 常用参数

| 调整内容 | 参数 |
| --- | --- |
| 浅水吸收、深水色和透明度 | `_ShallowColor`、`_DeepColor`、`_DepthFadeDistance`、`_Alpha` |
| 波浪形状 | `_WaveAmplitude`、`_WaveLength`、`_WaveSpeed`、`_WaveDirection` |
| 世界 XZ 平铺的两套法线 | `_NormalMap1`、`_NormalMap2`、`_NormalScale`、`_NormalTiling`、`_NormalTiling2`、`_NormalSpeed1`、`_NormalSpeed2` |
| 反射、折射和高光 | `_ReflectionIntensity`、`_ReflectionBlur`、`_RefractionStrength`、`_FresnelPower`、`_FresnelBias`、`_SpecTint`、`_SpecPower`、`_SpecIntensity` |
| 岸边和波峰泡沫 | `_FoamMap`、`_FoamMask`、`_FoamColor`、`_FoamShoreWidth`、`_FoamCrestStart`、`_FoamThreshold`、`_FoamIntensity` |

`_UseDepthFade` 对应 `_DEPTH_FADE`，关闭后改用相机距离近似吸收，真实岸边泡沫失效。`_UseFoamMask` 控制额外遮罩采样，`_UseCustomCube` 切换手工 Cubemap 和 Unity 反射探针。

## 绘制和性能

水面在 Transparent 队列绘制，默认 ZWrite Off、Cull Back。GrabPass 抓取已经绘制的背景，`_RefractionStrength = 0` 仅关闭 UV 偏移，仍会执行抓屏和背景采样。确实需要移除这部分开销时，要使用不含 GrabPass 的实现。初次验收可分别检查有无深度纹理、岸边泡沫、天空反射及视角贴近水面时的表现。 [Shader 实现](Water_Surface_Builtin.shader) [视觉目录](../../../README.md)
