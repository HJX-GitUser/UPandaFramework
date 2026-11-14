# 系统设置示例（Settings）

演示 `ISettingsService` + `SettingsPanel` 的完整使用场景：

- 从场景按钮打开框架内置设置面板；
- 业务如何**消费**设置：UI 缩放、灯光亮度、鼠标灵敏度 → 旋转速度；
- 推荐设置、提交预览、恢复默认、打印当前设置。

## 导入与运行

1. `Package Manager → UPandaFramework → Samples → Settings → Import`；
2. 菜单 **UPandaGF/示例/创建设置系统示例场景**，生成
   `Assets/UPandaFrameworkDemos/Settings/SettingsDemo.unity`；
3. 打开场景并 Play。等 `UPGameRoot` 初始化完成后，左上角出现示例面板；
4. 点「打开设置」进入框架设置面板；点「推荐设置」演示批量修改 + 提交预览；点「恢复默认」回到默认值。

> 场景里的 `UPGameRoot` 已设置为 `Editor` 资源模式，不依赖 AssetBundle；出包前需把场景加入 Build Settings，
> 并按项目实际情况切换资源模式。

## 场景结构（由生成器创建）

| 物体 | 作用 |
| --- | --- |
| `UPGameRoot` | 框架启动根；自动执行 `Settings → Localization → UI` 启动步骤（加载并应用设置、加载语言表、创建 UI） |
| `Main Camera` | 演示相机 |
| `Directional Light` | 接收 `graphics.brightness` 映射的灯光 |
| `SpinCube` | 按 `input.mouseSensitivity` 调整旋转速度 |
| `SettingsDemo` | `SettingsDemoController`：IMGUI 操作面板与设置消费示例 |

## 代码阅读顺序

1. `SettingsDemoController.Configure`：拿到 `IGameServices.Settings`，订阅 `Changed` / `PreviewStateChanged`；
2. `OpenSettings`：`ui.ShowPanel<SettingsPanel>()`；
3. `ApplyRecommended`：`settings.Set(...)` + `CommitAsync()`；
4. `ApplyUiScale` / `ApplyLight` / `ApplySpinSpeed`：业务读取当前值并驱动实际对象；
5. `ResetAll` / `PrintSettings`：恢复默认与诊断输出。

## 扩展说明

- 新增设置项只需改 `SettingsData` + `SettingsRegistry`，本示例与设置面板都不需要修改；
- 业务自己的消费点应该放在设置变更事件里（或每帧读 `Current` 快照），不要直接改平台 API；
- 语言已接入本地化：设置面板语言下拉可切换“跟随系统 + 内置/项目语言”，场景 `LocalizedText` 自动刷新；
- 音频已注册 Master/Music/SFX/UI/Voice 与失焦静音；框架默认应用器只写 AudioListener/两通道，完整五通道需在场景挂 `SettingsAudioMixerBinder` 并指定 AudioMixer。
- 画面亮度可用 `SettingsVolumeBrightnessBinder` 反射写入 URP/HDRP Volume；相机 FOV 由 `SettingsCameraBinder` 应用；输入偏好由 `SettingsInputProfile` 暴露给玩法。
