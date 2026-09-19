# 水面 Shader（`Custom/Water Surface (Built-in)`）

> 一句话：**Unity 内置渲染管线（Built-in RP）下的 Surface Shader 水面**。
> 动态波浪 + 双法线扰动 + 反射 + 折射 + 菲涅尔 + 深浅水吸收 + 泡沫，一个材质球调完。
>
> ⚠️ 用的是 Surface Shader，**只支持内置管线**；换成 URP / HDRP 会失效。

---

## 一、文件

| 文件 | 说明 |
| --- | --- |
| `Water_Surface_Builtin.shader` | Shader 本体，已逐段中文注释 |
| `Water_Surface_Builtin.mat` | 配套材质（关键词已设为 `_DEPTH_FADE` + `_FOAM_MASK`），可直接复制使用 |
| `WaterBump.jpeg` | 法线贴图（导入类型已是 **Normal map**，材质的两个法线槽都指向它） |

---

## 二、快速使用

1. **放水面物体**
   场景里放一个平面，`y ≈ 0`。
   ⚠️ 建议用**细分过**的平面（Unity 自带 Plane 只有 10×10 格），格子太粗波浪会显得很生硬。

2. **建材质**
   新建 Material → Shader 选 `Custom/Water Surface (Built-in)`；或直接复制现成的 `Water_Surface_Builtin.mat`。

3. **填法线贴图**
   两张法线贴图分别拖进 `_NormalMap1` / `_NormalMap2`，导入设置里 Texture Type 必须是 **Normal map**。
   当前材质两个槽用的是同一张图（`WaterBump.jpeg`），换成两张不同的水面法线会更自然。

4. **让反射有东西可反射**
   场景里放一个 **Reflection Probe**，或让天空盒生效（默认取 `unity_SpecCube0`，也就是反射探针 / 天空盒）。
   想用手工指定的 Cubemap：勾上 `_UseCustomCube` 并把 Cube 拖进 `_ReflectionCube`。

5. **让「深浅水 + 岸边泡沫」生效（关键一步）**
   相机需要有深度纹理，挂一个脚本执行：

   ```csharp
   void Start() { GetComponent<Camera>().depthTextureMode |= DepthTextureMode.Depth; }
   ```

   没设置的话，水深相关的效果不会出现（`_UseDepthFade` 会退化成「离相机越远越深」的近似）。

6. **调参**
   按第三节的表格调。当前材质是一组**比较平缓的水面**参数（`_WaveAmplitude = 0.04`、`_WaveLength = 15`），可以直接当起点。

---

## 三、常用参数

材质面板按功能分组，最常动的就是这些：

| 分组 | 参数 | 作用 |
| --- | --- | --- |
| **颜色 / 水深** | `_ShallowColor` | 浅水色，与背景**相乘**，等于「水体的颜色吸收」 |
| | `_DeepColor` | 深水色（水足够深时完全取代背景） |
| | `_DepthFadeDistance` | 从浅到深的过渡距离（米），越大越"清浅" |
| | `_Alpha` | 整体透明度 |
| **波浪** | `_WaveAmplitude` | 波高（米），**感觉太晃就调它** |
| | `_WaveLength` | 波长（米），越大越平缓开阔 |
| | `_WaveSpeed` | 相速度（米/秒） |
| | `_WaveDirection` | 主波方向（XY），副波/细波由它自动派生（45°、90°） |
| **法线** | `_NormalMap1/2` | 两张水面法线，以不同速度滚动做 whiteout 混合 |
| | `_NormalScale` | 法线强度，太大水面会像"塑料皱褶" |
| | `_NormalTiling` | 平铺（次/米）。用**世界 XZ** 当 UV，所以与物体尺寸无关，多块水面能无缝拼 |
| | `_NormalTiling2` / `_NormalSpeed1/2` | 第二张法线的平铺倍数与两张各自的滚动速度 |
| **反射 / 折射** | `_ReflectionIntensity` | 反射强度（配合菲涅尔） |
| | `_ReflectionBlur` | 反射模糊，0 = 镜面，1 = 很粗糙 |
| | `_RefractionStrength` | 折射扭曲强度，**设 0 = 关闭折射（同时省掉抓屏开销）** |
| **菲涅尔 / 高光** | `_FresnelPower` / `_FresnelBias` | 视角越平反射越强；`_FresnelBias` 是垂直看时的基础反射率 |
| | `_SpecTint` / `_SpecPower` / `_SpecIntensity` | 太阳高光的颜色、锐度、强度 |
| **泡沫** | `_FoamMap` / `_FoamMask` | 泡沫图案 / 遮罩（都只用 R 通道，留空即用默认白色） |
| | `_FoamShoreWidth` | 岸边泡沫带宽（米） |
| | `_FoamCrestStart` | 波峰泡沫起始高度（0~1），越小泡沫越多 |
| | `_FoamThreshold` / `_FoamIntensity` | 泡沫的显影阈值与强度 |
| | `_FoamTiling` / `_FoamSpeed` | 泡沫图案的平铺与滚动 |

---

## 四、三个开关（性能相关）

这三个都是 `shader_feature` 关键字，**关掉后对应代码分支会被剥离**，不占运行时开销：

| 开关（面板名） | 默认 | 打开时 | 关掉后 |
| --- | --- | --- | --- |
| `_UseDepthFade` | 开 | 采样相机深度纹理 → 真实水深、岸边泡沫 | 省一次深度采样；深浅水退化为「离相机越远越深」，**岸边泡沫失效** |
| `_UseFoamMask` | 开 | 泡沫图案再乘一张遮罩 | 省一次采样；泡沫均匀铺满 |
| `_UseCustomCube` | 关 | 使用 `_ReflectionCube` 指定的 Cubemap | 使用反射探针 / 天空盒（`unity_SpecCube0`） |

---

## 五、注意事项

* **渲染设置**：`Queue = Transparent(3000)`、`ZWrite Off`、`Cull Back`。
  如果需要**从水下看水面**，把 SubShader 里的 `Cull Back` 改成 `Cull Off`。
* **GrabPass 是最大的开销**：它会把当前帧缓冲整屏拷贝一份（用带名字的写法，一帧只抓一次，多块水面共享）。
  不需要折射时：`_RefractionStrength = 0`，并删掉 `GrabPass { "_WaterGrabTexture" }` 与 surf 里的折射采样。
* **水面不投射阴影**：`FallBack Off` → 不生成 ShadowCaster 通道。确实需要水面投影就改成 `FallBack "Diffuse"`。
* **不要额外增加插值器**：`#pragma target 3.0` 只有 10 个插值器，所以 `Input` 里刻意没有 `screenPos`——深度采样用的屏幕坐标由 `MakeScreenPos()` 从 `grabPos` 现算。要加新数据前先算清插值器余量。
* **水面自身不算漫反射**：`o.Albedo = 0`，颜色全部走 `Emission`（背景光照已经包含在抓屏结果里，再乘一遍光照就重复受光了）。自定义光照模型只出镜面反光。
* 水面需要**不透明物体先画完**才能抓到正确的背景，这也是它放在 `Transparent` 队列的原因。

---

## 六、常见问题

| 现象 | 排查 |
| --- | --- |
| 水面一片纯色 / 看不到折射 | 场景里没有已经画好的不透明背景；或 `_RefractionStrength = 0` |
| 反射是灰的 / 没有反射 | 场景没有反射探针，天空盒也没生效 |
| 深浅水、岸边泡沫不生效 | 相机深度纹理没开（见第二节第 5 步）；或 `_UseDepthFade` 被关 |
| 波浪像"折纸"，很生硬 | 平面细分不够；或 `_NormalScale` / `_WaveAmplitude` 太大 |
| 水面像镜面塑料 | 降 `_ReflectionIntensity`、升 `_ReflectionBlur`、降 `_NormalScale` |
| 岸边白沫太多/太少 | `_FoamShoreWidth`（带宽）、`_FoamThreshold`（阈值）、`_FoamIntensity` |
| 水面太透明 | 升 `_Alpha`；泡沫处和不透明物体一样不透明是正常的（泡沫处 alpha 会被拉到 1） |
| 移动端掉帧 | 关掉 `_UseFoamMask`、把 `_RefractionStrength` 设 0 并删 GrabPass、避免整屏水面 |
