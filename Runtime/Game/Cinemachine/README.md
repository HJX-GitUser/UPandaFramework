# Cinemachine 集成

框架包声明 `com.unity.cinemachine@2.10.3`，UPM 导入时自动安装；源码拷贝需在 Package Manager 手动安装同版本。

## 直接使用

安装后可直接给相机加 `CinemachineBrain`，创建 `CinemachineVirtualCamera` / `CinemachineFreeLook`。框架核心不强制引用 Cinemachine 程序集，业务按需引用 `Cinemachine` 与 `UPandaGF.Cinemachine`。

## 设置联动

`SettingsCinemachineBinder`（本程序集）监听 `graphics.fieldOfView`，写入虚拟相机的 `m_Lens.FieldOfView`：

1. 给 Virtual Camera 挂 `SettingsCinemachineBinder`；
2. 由 `UPGameRoot` 注入 `IGameServices` 后自动订阅设置变化；
3. 未指定 `target` 时优先取自身，再退回场景中的 `CinemachineVirtualCamera`。

如果项目有自己的相机控制/混合逻辑，也可以直接订阅 `ISettingsService.Changed`，不挂这个组件。
