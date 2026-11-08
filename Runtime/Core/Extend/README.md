# 常用扩展方法

扩展方法位于 `UPandaGF` 命名空间。只使用扩展方法的调用方引用 `UPandaGF.Core`；需要 UPGameRoot 时再引用 `UPandaGF.Runtime`。组件相关操作需要有效的 Unity 对象和主线程。 [Core 程序集](../UPandaGF.Core.asmdef)

## 组件

- `GetOrAddComponent<T>()` 支持 GameObject 和 Transform，先取当前对象的组件，缺少时才添加。添加组件可能同步执行 Unity 生命周期函数，调用方若持有创建中的状态应预留重入边界。
- `DestoryChildren()` 保留源码中的拼写。它按倒序向 Unity 提交直接子节点的销毁请求，层级实际移除遵循 `Object.Destroy` 的时序。
- `AddHotFixComponent(hotfixAssembly, hotfixTypeName)` 接收已加载的程序集和带命名空间的类型全名。它检查类型属于 Component、可以实例化，再用 `AddComponent(Type)` 创建。无对象、无程序集、找不到类型或不合格类型时会报错并返回空。调用前应确保类型名有效，Unity 的组件添加规则仍然适用。

```csharp
var body = gameObject.GetOrAddComponent<Rigidbody>();
var view = gameObject.AddHotFixComponent(loadedAssembly, "MyGame.Hotfix.GameView");
if (view == null)
    return;
```

此片段假设 `loadedAssembly` 已由启动流程加载，`GameView` 是实际可添加的组件。 [组件扩展实现](GetComponentExtend.cs)

## 时间文本

`FormatTime()` 把有限、非负的 float 秒数向下取整，显示为 `HH:mm:ss`。小时超过两位会继续显示完整数字，例如 `3661.9f.FormatTime()` 得到 `01:01:01`。传入负值会产生带负号的分段结果，需要由调用方先处理倒计时结束的显示。 [时间格式化](FloatValueExtend.cs)
