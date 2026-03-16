快速轮廓
由 Chris Nolet 开发 (c) 2018
使用说明
要为对象添加轮廓，请将 Outline.cs 脚本拖放到该对象上。轮廓材质将在运行时加载。
也可以通过编程方式添加轮廓：
```C#
var outline = gameObject.AddComponent<Outline>();

outline.OutlineMode = Outline.Mode.OutlineAll;
outline.OutlineColor = Color.yellow;
outline.OutlineWidth = 5f;

```
轮廓脚本在 Awake() 中执行少量工作。为获得最佳效果，请使用 outline.enabled 来切换轮廓显示。尽量避免移除并重新添加该组件。
对于大型网格，您可能还希望在编辑器中启用"预计算轮廓"选项。这将减少在 Awake() 中执行的工作量。
故障排除
如果轮廓显示偏离中心，请尝试以下操作：
在每个模型的导入设置中启用"读/写启用"。
在播放器设置中禁用"优化网格数据"。

Quick Outline
=============

Developed by Chris Nolet (c) 2018


Instructions
------------

To add an outline to an object, drag-and-drop the Outline.cs
script onto the object. The outline materials will be loaded
at runtime.

You can also add outlines programmatically with:

    var outline = gameObject.AddComponent<Outline>();

    outline.OutlineMode = Outline.Mode.OutlineAll;
    outline.OutlineColor = Color.yellow;
    outline.OutlineWidth = 5f;

The outline script does a small amount of work in Awake().
For best results, use outline.enabled to toggle the outline.
Avoid removing and re-adding the component if possible.

For large meshes, you may also like to enable 'Precompute
Outline' in the editor. This will reduce the amount of work
performed in Awake().


Troubleshooting
---------------

If the outline appears off-center, please try the following:

1. Set 'Read/Write Enabled' on each model's import settings.
2. Disable 'Optimize Mesh Data' in the player settings.
