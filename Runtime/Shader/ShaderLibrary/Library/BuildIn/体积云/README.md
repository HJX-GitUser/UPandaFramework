# 体积云 `Custom/Clouds` — 原理与使用说明

> 本目录实现了一套 **切片式（Slice-based / Layered）伪体积云**，运行在 **内置渲染管线（Built-in Render Pipeline）** 下。
> 本文说明它的实现原理、使用步骤、每个参数的物理含义，以及已知的坑与优化点。

---

## 一、文件与依赖

| 路径 | 作用 |
| --- | --- |
| `Clouds.shader` | 云片着色器（已逐函数加注） |
| `CloudsVolume.cs` | 云体积控制器：每帧把同一个平面 Mesh 沿 Y 轴堆叠 N 份并绘制 |
| `Custom_Clouds.mat` | 已配好的材质（**已勾选 GPU Instancing**、已开启 `_USEFOG_ON`） |
| `.../UnityShader学习文档/案例/8.体积云/NoiseClouds01.png`<br>`.../NoiseClouds02.png` | 两张噪声贴图（材质当前引用的是这两个） |
| `.../UnityShader学习文档/案例/8.体积云/体积云.md` | 原始学习笔记（本文是其配套的技术补充说明） |
| `ShaderScene.unity` | 工程内的实际用例：`Cloud` 空物体的子物体上挂了 `CloudsVolume`（200 片 / 厚度 1000） |

---

## 二、总体思路：为什么"一叠平面"看起来像体积云

真实的体积云要在三维空间里做光线步进（Ray Marching）逐点积分密度，开销很大。本方案用的是经典近似：

> **把体积离散成一叠水平的平面切片**，每个切片采样同一张噪声图、用同一个密度函数，但**所处的世界高度不同**，于是每片的密度值都不同；再以半透明方式逐层叠加，人眼就会把它理解成一团有厚度、有内部明暗层次的云。

因此整套实现被拆成两边：

* **C# 侧（`CloudsVolume.cs`）负责"体积"**：决定云有多厚、有多少层、每层在世界空间哪个高度。
* **Shader 侧（`Clouds.shader`）负责"云的形态与外观"**：给定一个世界坐标，算出这里"有多云"（密度 → alpha）、"是什么颜色"（散射 → Emission）。

```mermaid
flowchart LR
    A["CloudsVolume.Update()"] --> B["沿 Y 轴生成 N 个 TRS 矩阵<br/>从云顶均匀排到云底"]
    B --> C["Graphics.DrawMeshInstanced<br/>一次提交 N 个切片"]
    C --> D["vertexDataFunc()<br/>计算 eyeDepth"]
    D --> E["surf()<br/>算噪声 + 散射 → 写入 Emission"]
    E --> F["LightingStandardCustomLighting()<br/>算云密度 → 写入 alpha"]
    F --> G["alpha:fade 半透明混合<br/>N 层叠加 = 体积感"]
```

### 着色器内部的四个部分

| 部分 | 职责 |
| --- | --- |
| `vertexDataFunc()` | 顶点阶段：计算视空间深度 `eyeDepth`，供距离淡出使用 |
| `surf()` | 表面阶段：算云的颜色（正向散射 + 环境光 + 雾色），写入 `Emission` |
| `LightingStandardCustomLighting()` | 自定义"光照"函数：算云的不透明度（= 密度），写入 `alpha` |
| `LightingStandardCustomLighting_GI()` | GI 转发回调：把光照/GI 数据挂到表面输出上，供 `surf()` 使用 |
| `Pass "ShadowCaster"` | 阴影投射：用抖动遮罩（dither）裁出"半透明"阴影 |

> ⚠️ 注意一个反直觉的设计：**颜色和透明度是在两个不同函数里算的**。
> `surf()` 只给颜色（Emission），而 `LightingStandardCustomLighting()` 里 `c.rgb` 恒为 0，只返回 `c.a` 作为透明度。
> 这是因为 `alpha:fade` 表面着色器最终取的是光照函数返回的 alpha。

---

## 三、原理细节

### 3.1 云层几何：由 C# 决定的 4 个量

`CloudsVolume.cs` 每帧做两件事：把云的参数写进材质，然后画 N 个切片。

```text
_cloudsPosition ← transform.position.y      // 云层中心高度 C
_cloudsHeight   ← volumeSize                // 云层总厚度  H

volumeOffset    = H / N / 2
startPosition   = position + up * (volumeOffset * N / 2)     // 从最高处开始
第 i 片的高度   = startPosition - up * volumeOffset * i       // 逐片向下
每片变换        = TRS(高度, transform.rotation, transform.localScale)
```

要点：

* 切片是**沿世界 Y 轴分布的**，所以云的"厚度"是垂直厚度。
* 每片的平面方向由 `transform.rotation` 决定。工程用例是 `rotation = (-90, 0, 0)`：把 Quad 的局部 XY 平面转成世界 XZ 平面，即**水平朝上**。
* 每片的大小由 `transform.localScale` 决定。工程用例是 `(10000, 10000, 100)`，即云团宽 10000 单位、厚度由 `volumeSize` 控制（localScale.z 不影响水平切片的大小）。
* **所有切片共用同一套世界 XZ 噪声**（见 3.2），这是叠加后能"连成一整团"而不是"一堆各画各的薄片"的关键。

工程用例：`N = 200`、`H = 1000`、中心 `y = 500` → 切片从 `y ≈ 750` 均匀排到 `y ≈ 252`，间距 `2.5`。

### 3.2 噪声：两层滚动叠加

在**世界 XZ 平面**上采样，不随切片高度变化：

```text
worldXZ   = (worldPos.x, worldPos.z)

// 大尺度层（Noise01）
uv1       = _Time.y * _DirectionSpeed.xy + worldXZ
n1        = texture(_Noise01, uv1 * (_Tiling01 / 10000)).r      // 只取 R 通道
n1        = LinearToGammaSpace(n1)                              // 线性 → Gamma，提升屏幕观感对比

// 细节层（Noise02）
uv2       = _Time.y * _DirectionSpeed.zw + worldXZ
n2        = LinearToGammaSpace( texture(_Noise02, uv2 * (_Tiling02 / 10000)).r )

// 混合：lerp(目标, max(源, 目标), Power)，≈ "变亮/叠加"混合模式
noise     = saturate( lerp(n1 * _Noise01Power, max(n2, n1 * _Noise01Power), _Noise02Power) )
```

* 把面板上的 `Tiling` 再除以 **10000**，是为了配合"云团尺寸上万单位"的尺度，让输入值保持在 `0.01 ~ 3` 这种人类友好范围。
* `_DirectionSpeed` 的 xy 给大尺度层、zw 给细节层，两层以不同方向/速度滚动 → 产生"云在缓慢翻滚"的感觉。
* `_Noise01Power` 给大尺度层加权，`_Noise02Power` 决定"纯大尺度层"与"两层取较亮者"之间的混合比例。

### 3.3 垂直密度剖面：让云"中间厚、上下薄"

即使噪声相同，如果每片都一样亮，叠加出来就是一个均匀的方块。所以需要一个随高度衰减的剖面：

```text
d        = saturate( |_cloudsPosition - worldPos.y| / _cloudsHeight )   // 0=云层中心, 1=云层上下边缘
profile  = 1 - pow(d, 1 - _Coverage)
```

* `d` 只与"离云层中心多远"有关，因此剖面是**关于云层中心对称**的（上下都衰减），中心 `profile = 1`，边缘趋近 `0`。
* 指数取 `1 - _Coverage`：云量越大，指数越趋近 0，`pow(d, 很小)` 迅速逼近 1 → 剖面被"压平" → 云看起来更厚实、垂直方向延伸更满。

### 3.4 云密度（= 不透明度 alpha）

```text
m          = 1 - _Coverage / 0.98
coverage   = pow( saturate( (noise * profile - m) / (1 - m) ), _Softness )
alpha      = saturate( distanceFade * coverage )
```

* `noise * profile` 是"这块地方有多少云料"。
* 减去阈值 `m` 再除以 `(1 - m)`，是一次 **remap**：把 `[m, 1]` 重新拉伸到 `[0, 1]`，`m` 以下被截断为 0。`_Coverage` 越大 → `m` 越小 → 更多区域通过截断 → 云更多、更实。
* 最后 `pow(..., _Softness)`：`_Softness` 是 Range(0,1)，指数小于 1 会**整体抬亮**，即边缘过渡更柔、中间更实。
* `distanceFade` 做远处淡出（见 3.6）。

### 3.5 着色：颜色从哪来

`surf()` 里最终颜色由三项相加：

```text
// ① 正向散射（逆光时云边缘发亮）
scatterPhase = pow( saturate( dot(viewDir, -lightDir) ), 5 )      // 廉价前向散射相位函数
thickness    = pow( profile, _ScatteringPower )                   // 越厚越亮，_ScatteringPower 相当于吸收系数
scattering   = (lightColor * scatterPhase + scatterPhase * unity_AmbientSky * lightColor) * thickness

// ② 云体自身的散射色
body         = _ScatteringColor * thickness

// ③ 云的基础色，随距离在"雾色 ↔ 云色"之间插值
base         = lerp( unity_FogColor, _CloudsColor, fogWeight )    // fogWeight 由 _UseFog 开关决定

Emission     = scattering + body + base
```

* `pow(dot(V, -L), 5)` 是 **Henyey-Greenstein 相位函数的廉价替身**：视线越"迎着"光线（逆光看云），该项越大 → 云边缘出现明亮镶边。
* 亮度由**主平行光**（`_LightColor0`）驱动：**场景里没有 Directional Light 时，云会明显变暗**。旋转平行光可以看到云的整体明暗随光向变化，这正是这个效果的主要"光照感"来源。
* 颜色最终走 `Emission` 输出（`IsEmissive = true`），所以云**不会**被当作普通 PBR 表面接受阴影/金属度等处理。

### 3.6 距离淡出与雾

```text
eyeDepth     = -UnityObjectToViewPos(v.vertex).z              // 顶点阶段写入，视空间正向深度
depthRatio   = (eyeDepth - 近裁面) / _ViewDistance
distanceFade = 1 - depthRatio                                 // 1=近处, 0=超过 _ViewDistance 后全透明
```

* 顶点在相机背后或超出 `_ViewDistance` 时 alpha 归零，避免"远处云片出现硬边"以及浪费 overdraw。
* 同一个 `distanceFade` 还充当雾权重的开关变量（`_USEFOG_ON`）：
  * 勾选 `Use Fog` → 远处云色趋向 `unity_FogColor`；
  * 不勾选 → 恒为 1，始终使用 `_CloudsColor`。

### 3.7 ShadowCaster 通道

由 ASE 自动生成。它的作用不是"让云投影"，而是**让云投出"半透明"的柔和阴影**：

1. 顶点函数复用 `vertexDataFunc` 拿 `eyeDepth`，按法线偏移算出阴影投射位置；
2. 片元函数重建 `Input`，用 `surf()` + `LightingStandardCustomLighting()` 算出该像素的云密度 alpha；
3. 用屏幕坐标 + alpha 去查 `_DitherMaskLOD`（Unity 内置 3D 抖动遮罩），做 `clip()` 的概率性裁剪。

于是 alpha 小的稀疏区域会被"按比例打孔"裁掉，阴影呈现噪点状的半透明感，而不是一块实心黑斑。

---

## 四、使用步骤

### 4.1 准备噪声贴图

需要两张灰度噪声图（本项目即 `NoiseClouds01.png` / `NoiseClouds02.png`）：

* 只用了 **R 通道**，所以灰度图即可；
* 建议设为 **Repeat** 循环模式、关闭 sRGB 之外的额外压缩（可保持默认）；
* 建议 **关闭 Generate Mip Maps** 或至少保持 mip 可用——由于采样尺度极大（tiling 会除以 10000），mip 有助于减少远处闪烁。

### 4.2 创建材质

1. 新建 Material，Shader 选 `Custom/Clouds`；
2. 指定 `_Noise01`、`_Noise02`；
3. **务必勾选 `Enable GPU Instancing`**（C# 用的是 `DrawMeshInstanced`，不勾选会失效/报错告警）；
4. 按第五节表格调参（工程现成参考：`Assets/ArtAssets/Shader/Custom_Clouds.mat`）。

### 4.3 搭建场景

1. 新建空物体（如 `Cloud`）作为容器；
2. 在其下新建子物体，调整 Transform：
   * `Position.y` = 云层中心高度（工程用例 `500`）；
   * `Rotation = (-90, 0, 0)`（把平面转成水平朝上）；
   * `Scale = (10000, 10000, 100)`（云团水平尺寸）；
3. 给该子物体挂 `CloudsVolume` 脚本，设置：
   * `volumeSamples`：切片数量（工程用例 `200`，越小越快）；
   * `volumeSize`：云层总厚度（工程用例 `1000`）；
   * `Coludmesh`：**使用 Unity 内置 `Quad` 网格**（`GameObject > 3D Object > Quad` 的网格，或直接拖 Quad 的 Mesh）；
   * `cloudsMaterial`：4.2 创建的材质；
4. 场景里需要有 **Directional Light**（决定云的光照颜色），并把相机视角拉远一些便于观察。

### 4.4 必须满足的条件（Checklist）

- [ ] 材质的 Shader 是 `Custom/Clouds`，且 **勾选了 `Enable GPU Instancing`**
- [ ] 场景内有 **平行光**（否则云会偏暗）
- [ ] 相机的 **Culling Mask 包含 `Default`(Layer 0)** —— 脚本用 `Graphics.DrawMesh(..., layer: 0)`，云片都在第 0 层
- [ ] 相机的远裁面（Far Clip）要足够大，能容纳 `_ViewDistance` 与云团尺寸（工程用例为万级单位）
- [ ] 项目使用 **内置渲染管线**；若切到 URP/HDRP，本 Shader（表面着色器）会失效变粉
- [ ] `Clouds.shader` 含中文注释，**请以 UTF-8 保存**（勿存成 GBK/ANSI）

---

## 五、参数说明

### 通用

| 参数 | 类型 | 默认 | 含义 |
| --- | --- | --- | --- |
| `_ViewDistance` | Float | 5000 | 可见距离。超过该视距的云片 alpha 归零（同时充当雾权重） |
| `_DirectionSpeed` | Vector | (20, 5, 10, -5) | `xy` = 大尺度层滚动方向/速度；`zw` = 细节层滚动方向/速度 |

### 噪声

| 参数 | 类型 | 默认 | 含义 |
| --- | --- | --- | --- |
| `_Noise01` | 2D | white | 大尺度层噪声（决定云团大轮廓） |
| `_Tiling01` | Float | 0.02 | 大尺度层缩放（内部 ×1/10000）。**越大图案越小越碎** |
| `_Noise01Power` | Range(0,1) | 0.75 | 大尺度层权重，越高云越"成团" |
| `_Noise02` | 2D | white | 细节层噪声（决定边缘细节与翻滚感） |
| `_Tiling02` | Float | 0.03 | 细节层缩放（内部 ×1/10000） |
| `_Noise02Power` | Range(0,1) | 0.3 | 两层混合系数；越大越偏向"取两层较亮者" |

> 实际使用提示：`Tiling` 会被除以 10000，所以当云团尺寸是上万单位时，**需要把 Tiling 提高到 1~5 量级**才能看到明显图案。工程材质用的就是 `_Tiling01 = 3`。

### 云形态

| 参数 | 类型 | 默认 | 含义 |
| --- | --- | --- | --- |
| `_Coverage` | Range(0,1) | 0.3 | 云量。同时影响垂直剖面指数与密度阈值：**越大云越厚越实** |
| `_Softness` | Range(0,1) | 0.25 | 柔和度（密度曲线 pow 指数）。越小过渡越软、越"雾"；越大越硬、边界越清晰 |
| `_ScatteringColor` | Color | 白 | 云体自身散射色（按厚度叠加） |
| `_CloudsColor` | Color | 深蓝 | 云/雾的基础色（远处趋向它） |
| `_ScatteringPower` | Range(0,50) | 10 | 吸收系数。**越大只有最厚的中心才亮**，云越"厚重不透光" |
| `_UseFog` | Toggle | 1 | 是否让远处云色趋向场景雾色（`unity_FogColor`） |

### 隐藏参数（由脚本驱动，勿手改）

| 参数 | 说明 |
| --- | --- |
| `_cloudsPosition` | 云层中心世界 Y，`CloudsVolume.Update()` 每帧写入 `transform.position.y` |
| `_cloudsHeight` | 云层厚度，每帧写入 `volumeSize` |
| `__dirty` | ASE 生成的占位参数，无实际作用 |

---

## 六、调参配方

| 想要的效果 | 怎么调 |
| --- | --- |
| 云更厚实、更"实" | ↑ `_Coverage`（0.4~0.6），↑ `_ScatteringPower` |
| 云更稀薄、更透光 | ↓ `_Coverage`（0.15~0.25），↓ `_ScatteringPower`（4~8） |
| 边缘更柔和、更朦胧 | ↓ `_Softness`（0.1~0.2） |
| 边缘更锐利、更"块状" | ↑ `_Softness`（0.5~0.8） |
| 云团更碎、细节更多 | ↑ `_Tiling01` / `_Tiling02`，↑ `_Noise02Power` |
| 云更"一整片"、轮廓更大 | ↓ `_Tiling01` / `_Tiling02`，↑ `_Noise01Power` |
| 云移动更快 | ↑ `_DirectionSpeed` 各分量（注意两层方向不同才有翻滚感） |
| 云整体更亮/更暗 | 调整场景平行光的 `Intensity` / `Color`（主因），或调 `_ScatteringColor`、`_CloudsColor` |
| 逆光镶边更强 | ↑ `_ScatteringColor` 亮度；相位指数 `5` 写死在 shader 里，需要更强可改 shader |
| 远处云不淡出 | ↑ `_ViewDistance`（同时要保证相机远裁面足够大） |
| 云与场景雾融合 | 勾选 `Use Fog`，并确保 `Lighting > Environment > Fog` 已开启 |

---

## 七、性能与已知问题

### 7.1 性能画像

* **几何很便宜**：Quad 只有 2 个三角面。工程用例 200 片 ≈ 400 面。
* **主要开销是 overdraw**：200 层半透明平面全屏叠加，GPU 填充率消耗大。屏幕越大、`volumeSamples` 越多越明显。
* **Draw Call**：`DrawMeshInstanced` 一次提交一批（单次上限 **1023** 个实例），所以 draw call 不成问题；但脚本里那段逐片 `Graphics.DrawMesh` 会额外产生 **N 个 draw call**（见下条）。

优化建议：优先降低 `volumeSamples`（视觉上 40~80 已能接受）、配合 `_ViewDistance` 距离淡出、减小云团水平尺寸，避免全屏覆盖。

### 7.2 ⚠️ 已知缺陷：每片被重复绘制两次

`CloudsVolume.Update()` 里同时用了两种画法：

```csharp
for (int i = 0; i < volumeSamples; i++) {
    ...
    matrices[i] = matrix;
    Graphics.DrawMesh(Coludmesh, matrix, cloudsMaterial, 0);   // ← 逐片画了 N 次
}
Graphics.DrawMeshInstanced(Coludmesh, 0, cloudsMaterial, matrices, volumeSamples);  // ← 又批量画了 N 次
```

结果是**每帧每个切片被绘制两次**：等效透明度/密度翻倍、draw call 翻倍。修复方式二选一：

* 保留批量：**删掉循环里的 `Graphics.DrawMesh(...)`**（推荐，性能最好）；或
* 保留逐片：删掉 `Graphics.DrawMeshInstanced(...)`（draw call 更多，但每片可被独立排序）。

> 注意：修改这段代码会改变画面观感（密度会减半），修完后需要重新调 `_Coverage` / `_Softness`。

### 7.3 其它注意点

| 问题 | 说明 |
| --- | --- |
| **每帧 GC 分配** | `matrices = new Matrix4x4[volumeSamples];` 每帧新建数组 → 每帧一次堆分配。建议把数组缓存在字段里，尺寸变化时再重建 |
| **Layer 固定为 0** | `Graphics.DrawMesh(..., 0)` 与 `DrawMeshInstanced(..., 0)` 都画在第 0 层，相机 Culling Mask 必须含 `Default` |
| **切片内部无排序** | `DrawMeshInstanced` 是一个 draw call，实例之间没有逐片排序；alpha 混合不是可交换运算，某些视角可能有层次顺序异常 |
| **必须先勾 GPU Instancing** | 否则 `DrawMeshInstanced` 无法正确批处理 |
| **表面着色器限制** | 只支持内置渲染管线。切到 URP/HDRP 需重写（本工程 `GraphicsSettings.m_CustomRenderPipeline = 0`，确认为内置管线） |
| **`Cull Off`** | 双面渲染，从云团内部看也可见，但也意味着 overdraw 不会因背面剔除而减少 |
| **强依赖平行光** | 颜色由 `_LightColor0` 驱动，无平行光时云会偏暗 |

---

## 八、排错

| 现象 | 排查方向 |
| --- | --- |
| 完全看不到云 | 相机 Culling Mask 是否含 `Default`；相机远裁面是否太小；`Cloud` 物体的 Y 是否在视锥内；`volumeSize` / `volumeSamples` 是否为 0 |
| 云是粉红色 | Shader 编译失败（用了 URP/HDRP 或改坏了 shader），看 Console 的 shader error |
| 云像一块均匀的平板，没有细节 | `_Tiling01/_Tiling02` 太小（别忘了要 ÷10000），把 Tiling 调到 1~5 |
| 云太暗 | 场景缺平行光；或 `_ScatteringPower` 过大、`_Coverage` 过小 |
| 没有"半透明"感，边缘像硬切 | `_Softness` 过大；或 `_Coverage` 过大导致密度饱和 |
| 画面有闪烁/层次错乱 | 见 7.2、7.3（重复绘制 + 切片内部无排序） |
| 性能掉帧 | 降低 `volumeSamples`，检查相机是否让整屏都是透明层叠；并先修掉 7.2 的重复绘制 |

---

## 九、相关文档

* 原始学习笔记：`Assets/Scripts/upanda-framework/Runtime/Shader/UnityShader学习文档/案例/8.体积云/体积云.md`
* 着色器内注释：`Clouds.shader`（每个函数与关键公式都有中文说明）
* 控制器源码：`CloudsVolume.cs`
