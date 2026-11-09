# OutLine 高亮描边

OutLine 用于 Unity 3D 模型的选中提示、交互高亮和遮挡剪影。当前版本面向项目的 Unity 2022.3 Built-in Render Pipeline。

## 快速接入

1. 在模型根对象添加 `OutLine` 组件。组件查找自身与子级的 `MeshRenderer` 和 `SkinnedMeshRenderer`，也会准备当前未激活的子对象。
2. 在 Inspector 设置模式、颜色和宽度。清除 `Can Flash` 可显示常亮轮廓。效果在 Play Mode 中生效。
3. 使用 `outline.enabled` 开关高亮。关闭后会移除插件材质并恢复插件替换过的网格引用。频繁切换时复用组件及其准备好的资源。

运行时组件类型为 `UPandaGF.Gameplay.OutLine`。组件与 Shader 保留原 GUID，描边模式保留原有的 0–4 序列化数值，已有场景、预制体和材质引用可继续使用。升级业务 C# 时，把 `QuickOutlineHighlight` 或 `OutDrawline` 改为 `OutLine`，并改用下表中的 `OutLine.描边模式` 成员。按名称查找 Shader 时，使用 `UPandaGF/OutLine/OutlineMask` 或 `UPandaGF/OutLine/OutlineFill`。

## 模式与参数

| 模式 | 效果 |
| --- | --- |
| `全部描边` | 整个外轮廓可见，轮廓可透过遮挡物 |
| `可见描边` | 只画通过场景深度测试的外轮廓 |
| `遮挡描边` | 只画场景深度之后的外轮廓 |
| `描边与剪影` | 可见处画外轮廓，遮挡处填充剪影 |
| `仅剪影` | 只填充遮挡处的剪影，描边宽度强制为零 |

- `OutlineColor` 默认白色，Alpha 控制整体透明度。
- `OutlineWidth` 默认 `4`，Inspector 范围为 `0–200`。宽度是外扩系数，最终像素宽度随投影和分辨率变化。透视相机按视深补偿，正交相机按视野高度补偿。
- `CanFlash` 默认开启，闪烁使用受 `Time.timeScale` 影响的时间。闪烁仅调制材质的 Alpha，组件上的颜色值可继续用作业务配置。
- `Precompute Outline` 会在编辑器中保存普通网格的平滑法线，减少首次使用时的顶点计算。启用后可从组件右键菜单执行 **Rebuild Outline Normals**。模型重新导入、替换或修改法线后可用该命令重建。运行时仍需创建并配置网格副本。

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

运行时也可用 `gameObject.AddComponent<OutLine>()` 添加组件。根节点的直接子级增删会触发自动刷新。替换网格、改变材质槽、在现有节点上新增 Renderer，或增删更深层的子级后，调用 `outline.RefreshTargets()`。同一 Renderer 由最近的启用描边组件负责，避免父子组件叠加。运行时生成可变几何时，用新的 Mesh 引用替换后再刷新。已缓存的副本对应源网格创建时的数据。

## 网格与性能

- Mask 先写模板缓冲，Fill 沿法线外扩并进行模板与深度测试。每个参与描边的 Renderer 增加两次几何绘制。
- 插件保持原始 Mesh 和材质资产的数据。需要平滑法线或合并子网格时，它给 Renderer 分配运行时副本。使用相同源网格和兼容材质槽布局的目标共享副本。短暂禁用组件保留已准备的副本，最后一个持有者释放后销毁副本。
- 运行时修改材质槽时，建议先禁用描边组件，改完再启用。`Renderer.materials` 会实例化整组材质，包括插件追加的两个槽。
- 普通可读网格优先使用平滑法线。第四套 UV 已占用、有附加顶点流、网格不可读，或属于蒙皮网格时，描边沿原顶点法线外扩。硬边模型在这种情况下可能出现接缝。
- 多子网格模型需要三角形拓扑、开启模型的 **Read/Write**，并至少为每个子网格分配一个材质槽。已有的额外材质层会继续绘制在最后一个原子网格上。静态合批 Renderer 会跳过，可在对象的 Static 设置里清除 **Batching Static**。
- 遮挡取决于深度缓冲。未写深度的透明材质通常不会像不透明物体一样挡住描边。多个描边对象共用模板值 `1`，屏幕上重叠时可能互相裁剪。相机目标需要支持深度模板缓冲。

## 演示与排查

打开 [QuickOutlineDemo.unity](Demo/QuickOutlineDemo.unity)，在 **Game** 视图进入 Play Mode。横向遮挡板同时覆盖五个模型的一部分，可对照上表检查遮挡差异。中间的立方体常亮，其余模型默认闪烁。编辑器菜单 **UPandaGF → Runtime → QuickOutline** 可新建演示场景并保存到框架的 `Runtime/高亮描边/`，或将地面、遮挡板和五个示例模型追加到当前场景。

| 现象 | 检查项 |
| --- | --- |
| 整个模型没有描边 | 检查组件是否启用、Alpha 和宽度，确认相机目标有模板缓冲。剪影模式需要深度写入的遮挡物 |
| 材质缺失或粉色 | 检查 [Mask 材质](Resources/Materials/OutlineMask.mat) 和 [Fill 材质](Resources/Materials/OutlineFill.mat) 的 Shader 引用及 Console。可运行同一菜单下的 **QuictOutLine材质修复** |
| 多材质模型被跳过 | 检查 Console、模型的 **Read/Write**、子网格拓扑、材质槽数量和 **Batching Static** |
| 换装或深层节点加入后无描边 | 完成网格和材质的变更，再调用 `RefreshTargets()` |

[运行时组件](Scripts/OutLine.cs) · [Mask Shader](Shaders/OutlineMask.shader) · [Fill Shader](Shaders/OutlineFill.shader)
