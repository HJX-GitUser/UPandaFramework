# QuickOutline 高亮描边

本地组件是 [OutDrawline](Scripts/OutDrawline.cs)。Mask pass 写模板，Fill pass 沿平滑法线外扩，用深度和模板测试控制描边可见范围。组件运行在 `UPandaGF.Gameplay`，从 Resources 加载并实例化两份描边材质，无需启动根。原项目归属及说明保留在 [Readme.txt](Readme.txt)。

## 接入与使用

1. 在模型根对象上挂 `OutDrawline`。Awake 收集自身和活动子级的 Renderer。
2. 确认 `Resources/Materials/OutlineMask`、`Resources/Materials/OutlineFill` 各自绑定当前目录的同名 Shader。仓库内材质原有的 Shader GUID 与当前 Shader 的 meta 不一致，可先运行 Unity 菜单 `UPandaGF → Runtime → QuickOutline → QuictOutLine材质修复`。
3. 设置 `OutlineMode`、`OutlineColor`、`OutlineWidth`。如果只需要常亮轮廓，把 `CanFlash` 设为 false。用 `enabled` 切换显示。禁用移除两份材质，销毁时释放材质实例。

```csharp
var outline = gameObject.AddComponent<OutDrawline>();
outline.OutlineMode = OutDrawline.Mode.OutlineVisible;
outline.OutlineColor = Color.yellow;
outline.OutlineWidth = 4f;
outline.CanFlash = false;
```

[Mask 材质](Resources/Materials/OutlineMask.mat) [Fill 材质](Resources/Materials/OutlineFill.mat) [材质修复菜单实现](../../../Editor/QuickOutlineEditor/QuickOutlineMaterialRepair.cs)

## 网格与渲染边界

平滑法线写到共享网格的 UV4，并可能追加合并子网格。使用相同 Mesh 的对象会看到同一网格变化，禁用组件只撤下材质。模型通常需要开启 Read/Write，使用了 UV4 的材质或子网格布局需要额外核对。预计算选项可以减少 Awake 时的法线计算，仍需写网格。传统 Mask/Fill pass 的效果取决于当前渲染管线和深度模板设置。

随附 [QuickOutlineDemo.unity](Demo/QuickOutlineDemo.unity) 中描边组件引用的 GUID 与当前脚本不一致。修复材质后可用 `UPandaGF → Runtime → QuickOutline → 创建描边示例场景（新建场景）` 按当前类型重建，在目标管线核对普通描边、被遮挡和可见轮廓。 [示例生成器](../../../Editor/QuickOutlineEditor/QuickOutlineDemoBuilder.cs) [Mask Shader](Shaders/OutlineMask.shader) [Fill Shader](Shaders/OutlineFill.shader)
