# OutLine 描边高亮

OutLine 为模型添加选中提示、交互高亮和遮挡剪影。当前版本面向 Unity 2022.3 / 2021.3 的 Built-in Render Pipeline。

本模块是原创实现：不改动业务对象的材质与网格，而是在被描边对象下创建一个隐藏的代理 Renderer 来绘制轮廓。

## 快速接入

1. 在模型根对象添加 `OutLine` 组件。组件会收集自身与子级中启用的 `MeshRenderer`、`SkinnedMeshRenderer`（包含未激活的子物体）。
2. 在 Inspector 设置模式、颜色和宽度。关闭 `Can Flash` 显示常亮轮廓。
3. 用 `outline.enabled` 开关高亮。禁用时整块代理被销毁，业务对象的网格与材质保持原样。

运行时类型是 `UPandaGF.Gameplay.OutLine`，脚本与 Shader 沿用 0.6.0 的 GUID，已有场景和预制体不需要重新绑定。描边模式保留原有的 0–4 序列化数值。

## 工作原理

一个材质两个 Pass，绘制同一份几何：

1. **Stencil Pass**：`ColorMask 0`，把模型本体覆盖到的像素写进模板缓冲（`Ref 1`）。它不产生可见像素。
2. **Outline Pass**：顶点沿法线向屏幕外扩一圈，`Stencil { Comp NotEqual }` 只在本体之外着色 —— 本体把内圈挖掉，留下的就是轮廓。

两个 Pass 的深度比较由材质属性 `_MaskZTest` / `_FillZTest` 控制。组件为五种模式各准备一份**共享材质**；颜色与宽度是普通 uniform，用 `MaterialPropertyBlock` 逐对象覆盖，因此不会给每个对象实例化材质。

外扩宽度有两种单位：`屏幕相对` 按视深/视野高度补偿，远近与缩放观感一致；`像素` 按设备像素定宽。

## 渲染管线

| 管线 | 接入方式 |
| --- | --- |
| Built-in | 直接使用 `Outline.shader` 中不带标签的 SubShader |
| URP | 同一个 Shader 资产里的 `"RenderPipeline" = "UniversalPipeline"` 分支；Pass 以 `SRPDefaultUnlit` 由 URP 的 DrawObjectsPass 绘制，材质与 Shader 资产不用替换 |
| HDRP | 需要单独启用 `Shaders~/HDRP/OutlineHDRP.shader`：把文件复制到 `Shaders/`（`~` 结尾的目录 Unity 不导入），运行时会按 `UPandaGF/OutLine/Outline (HDRP)` 名称查找；找不到会打一次错误日志并停用描边渲染 |

HDRP 模板只覆盖透视相机，且未在本仓库的 Built-in 验证工程里编译过，接入 HDRP 项目时需要在目标管线里重新验收五种模式与模板缓冲效果。URP 分支同样依赖项目的深度/模板缓冲配置，启用 Depth Priming 或自定义 Renderer 时建议实测。

## 模式与参数

| 模式 | Mask 深度比较 | Fill 深度比较 | 效果 |
| --- | --- | --- | --- |
| `全部描边` | Always | Always | 整个外轮廓可见，可透过遮挡物 |
| `可见描边` | Always | LessEqual | 只画通过深度测试的外轮廓 |
| `遮挡描边` | Always | Greater | 只画场景深度之后的外轮廓 |
| `描边与剪影` | LessEqual | Always | 可见处画轮廓，遮挡处填剪影 |
| `仅剪影` | LessEqual | Greater | 只填遮挡处剪影，宽度强制为零 |

- `OutlineColor` 默认白色，Alpha 控制整体透明度。
- `OutlineWidth` 默认 `4`，Inspector 范围 `0–200`。切换 `仅剪影` 时组件内部把宽度按 0 处理，组件上的配置值不变。
- `OutlineWidthUnit` 选择宽度单位：`屏幕相对`（默认）按视深/视野高度补偿，远近与缩放观感一致；`像素` 直接按设备像素定宽，分辨率变化时厚度不变，适合 HUD 或 DPI 敏感场景。
- `CanFlash` 默认开启，闪烁使用受 `Time.timeScale` 影响的时间，只调制材质 Alpha，组件上的颜色值可继续用作业务配置。
- `Precompute Outline` 在编辑器中预计算平滑法线并随对象序列化；启用后可从组件右键菜单执行 **重新计算描边法线**。模型重新导入、换网格或改法线后需要重建。

## 脚本示例

```csharp
using UnityEngine;
using UPandaGF.Gameplay;

public class SelectionHighlight : MonoBehaviour
{
    [SerializeField] private OutLine outline;

    private void Awake()
    {
        outline.OutlineMode = OutLine.描边模式.可见描边;
        outline.OutlineColor = Color.yellow;
        outline.OutlineWidth = 4f;
        outline.CanFlash = false;
        outline.enabled = false;
    }

    public void SetSelected(bool selected)
    {
        outline.enabled = selected;
    }
}
```

运行时也可用 `gameObject.AddComponent<OutLine>()`。根节点的直接子级增删会触发自动刷新；换网格、改材质槽、在现有节点上新增 Renderer 或增删更深层的子级后，调用 `outline.RefreshTargets()`。`TargetCount` 可用来确认当前被接管的 Renderer 数量。

## 网格与性能

- 可读网格：取一份副本，把全部子网格合并成 1 个绘制单元，并将**面积加权**的平滑法线写进副本的 `NORMAL` 通道（不再占用 UV3，也不会和模型的第二/第四套 UV 冲突）。相同源网格的多个物体共享同一份副本，最后一个使用者释放时销毁。
- 不可读网格：直接复用原网格（原始法线，硬边模型可能出现接缝），代理按原子网格数量重复使用描边材质。
- 每个参与描边的 Renderer 增加一个代理对象；描边本身是 2 次绘制。代理不投影、不接收光照探针、不产生运动矢量。
- 代理对象来自对象池（`Mesh` 与 `Skinned` 各缓存一份，每类上限 64）：频繁开关高亮只做重新挂接与属性刷新，不再反复创建/销毁 GameObject；池根节点隐藏且不随场景保存或卸载。
- 蒙皮网格的代理与源共用骨骼，表情权重逐帧同步，因此变形动画下描边与模型保持一致。
- 静态合批的 Renderer 会跳过（外扩依赖对象空间法线，合批会破坏它）。请在 Static 设置里清除 **Batching Static**。
- 遮挡取决于深度缓冲。不写深度的透明物体通常不会挡住描边。多个描边对象共用模板值 `1`，屏幕上重叠时可能互相裁剪。相机需要支持深度模板缓冲。
- 代理对象放在目标下并隐藏（`HideInHierarchy`），不会写入场景。需要查看它们时可临时关闭组件的 `hideFlags` 逻辑进行排查。

## 层级规则

一个 Renderer 只由离它最近的启用组件负责：子节点上的组件优先，父级组件扫描时会跳过已被更近组件接管的 Renderer。启用/禁用子级组件会自动通知父级重建，不需要手工刷新。

## 从 0.6.0 迁移

- 描边改为代理渲染，业务对象的 `Renderer.sharedMaterials` 不再被追加两个描边材质槽。若有代码依赖「末尾两个槽是描边材质」，需要改为读取代理对象（子物体名以 `(OutLine)` 结尾，带 `OutlineProxy` 标记组件）。
- 两个旧 Shader（`OutlineMask` / `OutlineFill`）与两个旧材质被一个 `Outline.shader` + `Resources/Materials/Outline.mat` 取代。直接引用旧素材的自定义材质需要改指新 Shader。
- `Precompute Outline` 的序列化字段改名，需要重新执行一次 **重新计算描边法线**。
- 平滑法线从 UV3 改到 `NORMAL`，因此不再要求模型预留 UV3。
- 新增 `OutlineWidthUnit` 字段，默认 `屏幕相对`，已有场景无需改配置；切到 `像素` 时宽度数值按设备像素解释。

## 演示与排查

编辑器菜单 **UPandaGF → Runtime → OutLine → 创建示例场景（新建场景）** 会生成相机、平行光、地面、遮挡板和五个不同模式的模型，保存到 `Assets/upanda-framework/Runtime/Game/OutLine/Demo/`。也可用 **在当前场景追加示例物体** 把内容追加到现有场景。

| 现象 | 检查项 |
| --- | --- |
| 完全没有描边 | 组件是否启用、Alpha 与宽度是否为 0、相机是否有模板缓冲；剪影模式需要写深度的遮挡物 |
| 材质缺失或粉色 | 运行 **UPandaGF → Runtime → OutLine → 描边材质修复**，检查 `Resources/Materials/Outline.mat` 的 Shader 引用 |
| 多材质模型没有描边 | 模型是否开启 **Read/Write**（不可读时按原子网格绘制，仍应有描边）、是否有 **Batching Static** |
| 换装或深层节点加入后没有描边 | 完成网格与材质变更后调用 `RefreshTargets()` |
| 父子都有组件但只出现一圈描边 | 正常行为：最近的启用组件接管 |
| HDRP 下完全没有描边 | `Shaders~/HDRP/OutlineHDRP.shader` 是否已复制到 `Shaders/`，Console 是否有 HDRP Shader 缺失的错误 |
| 描边粗细随分辨率变化 | 需要固定物理/像素厚度时把 `OutlineWidthUnit` 切到 `像素` |

[运行时组件](Scripts/OutLine.cs) · [代理池](Scripts/OutlineProxyPool.cs) · [Shader](Shaders/Outline.shader) · [共用实现](Shaders/OutlineShared.cginc) · [HDRP 模板](Shaders~/HDRP/OutlineHDRP.shader) · [模板材质](Resources/Materials/Outline.mat)
