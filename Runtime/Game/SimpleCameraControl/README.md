# 自由相机控制

[SimpleCameraController](SimpleCameraController.cs) 是带 Camera 的独立场景组件，编入 `UPandaGF.Gameplay`。它用旧 Input Manager 更新目标位置和旋转，再用 SmoothDamp 与 Slerp 平滑相机。无需 UPGameRoot 或服务注入。

## 操作

挂到要控制的相机。默认右键旋转，按住右键时用 W/A/S/D、Q/E 移动，左 Shift 加速；中键平移。Inspector 可配置速度、平滑时间、按键和距离范围。`SetPose(Transform arg)` 设置目标位置和旋转，`ResetCamera()` 回到默认目标姿态。Start 会用场景当前姿态初始化，业务控制可在这一步之后设置。

编辑器 Play 模式下，F 可以聚焦 Unity 当前选中的对象。直接取该对象的 Renderer bounds，缺少 Renderer 时使用固定范围。Player 没有 Editor Selection，F 不产生效果。

## 当前滚轮行为

`HandleZoom()` 会调整并钳制 `currentZoom`，但目标位置用更新后的同一个距离沿视线先加后减，结果基本不变。滚轮目前只会间接改变中键平移的倍率。在不同输入配置下验证相机时可分别核对旋转、平移、F 聚焦以及滚轮后的相机位置。 [滚轮与聚焦实现](SimpleCameraController.cs#L87)
