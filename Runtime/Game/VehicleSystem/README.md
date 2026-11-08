# 车辆控制与视角

`VehicleSystem` 是基于 Rigidbody 和 WheelCollider 的后驱车辆示例，代码在 `UPandaGF.Gameplay`。输入、物理和视角分成三个组件，既可接框架的 `IGameServices`，也可在没有启动根的演示场景运行。 [VehicleInput](Runtime/VehicleInput.cs) [VehicleController](Runtime/VehicleController.cs) [CameraModeManager](Runtime/CameraModeManager.cs)

## 搭建

1. 在车体根放 Rigidbody、`VehicleController` 和 `VehicleInput`，为前后轮分别配置 WheelCollider，并按相同顺序绑定视觉网格数组。车辆用 FixedUpdate 施加前轮转向、后轮扭矩、换向制动和手刹，Update 同步网格位置与旋转。
2. 在独立场景对象挂 `CameraModeManager`，为它明确指定车辆 Transform、相机 Transform 和 VehicleInput。组件在 LateUpdate 应用第三人称、第一人称、环绕视角及切换插值。
3. 使用默认输入映射时，W/S 或上下方向键驱动，A/D 或左右方向键转向，空格拉手刹，C 循环切视角，1/2/3 直达，环绕视角用鼠标轴。

`VehicleInput.Configure(IGameServices services)` 采用服务的动作映射。未注入时 Awake 创建基于旧 Input Manager 的本地 `InputService`，替换或销毁时释放本地服务。动态生成车辆时可以显式注入服务。 [动作绑定与输入后端](../../Core/Services/InputAndTime.cs)

## 边界与验证

- 车轮数组按 Inspector 引用使用。未赋值或轮子与模型顺序错误时，物理或视觉行为会失败。
- 相机没收到输入引用时可能搜索场景中的其他 VehicleInput。多车场景需要明确绑定车体和输入。
- 环绕模式的鼠标轴直接读旧 Input Manager，绕过动作上下文和暂停屏蔽。只开启新版 Input System 时需要自行接入这一段。
- 前后最高速度字段限制对应方向继续施加驱动扭矩，外力和坡度仍可使车体超过该值。

用 `UPandaGF → Runtime → VehicleSystem → 创建驾驶示例场景（新建场景）` 生成驾驶场景。检查前进中按倒车、倒车中按前进、手刹和三种视角切换。项目交付的框架目录没有现成驾驶场景。 [场景生成器](../../../Editor/VerticalSceneEditor/VerticalSceneDemoBuilder.cs)
