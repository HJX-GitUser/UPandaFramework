# 系统设置（Settings）

玩家设置服务：音频/画面/显示/输入/语言/辅助功能/游戏性/隐私/系统状态，描述表驱动、独立持久化、平台应用器可替换。

与 `IConfigService`（开发者配置）和 `ISaveService`（游戏存档）分离：删档不影响设置，渠道配置不覆盖玩家值。

## 组成

| 文件 | 职责 |
| --- | --- |
| `Runtime/Contracts/Settings/SettingsData.cs` | 强类型设置数据（可 JSON 序列化，新增字段自动参与读写） |
| `Runtime/Contracts/Settings/SettingsEnums.cs` | 分区/值类型/生效方式/平台/业务枚举 |
| `Runtime/Contracts/Settings/SettingDescriptor.cs` | 单项元数据（key/默认/范围/平台/生效/本地化/同步分级）与值比较工具 |
| `Runtime/Contracts/Settings/ISettingsService.cs` | 服务契约与事件（Changed/Saved/SaveFailed/ApplyFailed/PreviewStateChanged） |
| `Runtime/Core/Settings/SettingsRegistry.cs` | 描述表、默认值生成、归一化、自检（`Validate`） |
| `Runtime/Core/Settings/SettingsStore.cs` | JSON 原子写 + `.bak` 回退 + 原始 JSON 保留（未知字段合并） |
| `Runtime/Core/Settings/SettingsAppliers.cs` | 音频/画质/显示/输入/帧率应用器（`FrameRateSettingsApplier` 最后执行） |
| `Runtime/Core/Settings/SettingsService.cs` | 加载/迁移/修改/预览提交/回滚/重置/保存/生命周期接入 |
| `Runtime/Core/Settings/SettingsMigrationChain.cs` | `ISettingsMigration` 逐级迁移（内置 0→1） |
| `Runtime/Core/Settings/SettingsMigrationRegistry.cs` | 项目启动前注册自定义迁移 |
| `Runtime/Core/Settings/SettingsSafeMode.cs` | 连续启动失败计数与安全模式 |
| `Runtime/Core/Settings/SettingsJsonMerge.cs` | 保存时保留未知字段（对象递归、对象数组按 key 合并） |
| `Runtime/Contracts/Settings/SettingsConsent.cs` | 首启隐私同意/拒绝 API（`termsVersion`/`consentUtc`） |
| `Runtime/Composition/FrameRateCounter.cs` | 基于 `Time.unscaledDeltaTime` 的真实 FPS 统计（DebugerInit 使用） |
| `Runtime/Game/SettingsCameraBinder.cs` | `graphics.fieldOfView` → Camera |
| `Runtime/Game/SettingsInputProfile.cs` | 输入灵敏度/死区/反转/震动/瞄准方式只读消费入口 |
| `Runtime/Game/SettingsVolumeBrightnessBinder.cs` | `graphics.brightness` → URP/HDRP ColorAdjustments（反射） |
| `Runtime/Audio/SettingsAudioMixerBinder.cs` | 五通道 AudioMixer/静音/失焦静音 |
| `Runtime/Audio/SettingsAudioPreview.cs` | 设置面板试听短音 |
| `Runtime/UI/UIMgr/SettingsPresenter.cs` | 描述表 → `UISettingItem`，值双向绑定、分区过滤、键位捕获 |
| `Runtime/UI/UIMgr/SettingsPanel.cs` | 设置面板：分区按钮、运行时构建的滚动列表、预览确认、恢复默认 |
| `Runtime/Resources/UI/SettingsPanel.prefab` | 面板根预制体（布局运行时构建；生成器见 `Editor/UIEditor/SettingsPanelGenerator.cs`） |
| `Tests/EditMode/Settings/SettingsServiceTests.cs` | 描述表一致性、默认值、归一化、持久化、预览、损坏恢复、平台可用性、键位覆盖、帧率/VSync 联动 |
| `Tests/EditMode/Settings/SettingsPresenterTests.cs` | Presenter 行生成、下拉/滑块/开关绑定、分区过滤、键位列表、面板预制体存在性 |
| `Tests/EditMode/Settings/SettingsMigrationAndSafeModeTests.cs` | schema 迁移、安全模式、未知字段保留、分辨率选择 |
| `Tests/EditMode/FrameRateCounterTests.cs` | 不受 timeScale 影响的真实帧率统计 |
| `Tests/PlayMode/SettingsPanelSmokeTests.cs` | 面板/下拉/焦点/安全区/设置行运行时构建 |
| `Tests/PlayMode/SettingsConsumptionPlayModeTests.cs` | FOV 真正写入 Camera |

## 接入

`GameServices` 已装配 `ISettingsService Settings` 与 `ILocalizationService Localization`；`UPGameRoot` 的启动顺序为：

```text
Services → Settings → Localization → UI
```

`Settings` 步骤执行：

```csharp
await services.Settings.LoadAsync(ct);     // 读取 + 迁移 + 归一化
await services.Settings.ApplyBootAsync(ct); // 音频/画质/显示/输入绑定
```

业务读取与修改：

```csharp
float volume = services.Settings.Get<float>("audio.master");
services.Settings.Set("audio.master", 0.5f);            // 即时生效 + 防抖保存
services.Settings.Set("graphics.preset", QualityPreset.Low); // 预览确认项：15s 内 Commit，否则回滚
await services.Settings.CommitAsync();
services.Settings.Reset(SettingsScope.Category, "Audio");
```

运行时改键复用 0.8.2 的输入覆盖层：

```csharp
var bindings = services.Settings.Get<InputBinding[]>("input.bindings");
services.Settings.Set("input.bindings", services.Input.ExportOverrides());
```

打开设置面板（需要 `UPGameRoot` 已就绪并完成 `UI` 步骤）：

```csharp
await services.UI.ShowPanelAsync<SettingsPanel>();                 // 默认从“音频”开始
await services.UI.ShowPanelAsync<SettingsPanel>(SettingsCategory.Input); // 指定起始分区
```

面板预制体已随包提供（`Resources/UI/SettingsPanel`）；若修改了根节点尺寸/锚点，可运行菜单
`UPandaGF/UI/生成设置面板预制体` 重新生成。布局与行由代码构建，**新增设置项不需要重新生成预制体**。

## 新增一个设置项（扩展点）

1. 在 `SettingsData` 对应分区加字段（默认值取安全值）。
2. 在 `SettingsRegistry.BuildXxx()` 加描述：key、类型、默认、范围/选项、平台、生效方式、Order、同步分级。
3. 需要平台调用时，在 `SettingsAppliers.cs` 的对应应用器里加映射；新平台实现 `ISettingsApplier` 并注册到 `GameServices`。
4. 加本地化 key（`settings.<category>.<item>.label` 等）。
5. 加测试：默认值/越界/应用/旧文件兼容；`Registry_EverySettingsFieldHasDescriptor` 会强制字段与描述一一对应。
6. 可选项**不需要** bump `schemaVersion`；改名/语义变化才写迁移。

不允许：在 UI 里按 key 写 `switch`、每项单独存储、面板直接调 `QualitySettings`/`Screen`、新增项就 bump 版本。

## 能力与边界

- **设置面板已提供**：运行时构建 UI，预制体只有根节点；打开方式
  `services.UI.ShowPanelAsync<SettingsPanel>()`。新增设置项不改面板，只有全新控件类型才需要扩展 `UISettingItem`/行。
- **画质预设会展开子项**：`graphics.preset` 设置后同步写入 AA/阴影/纹理/渲染缩放；单独改子项会把预设标成 `Custom`，避免面板显示与实际应用不一致。
- **显示健壮性**：`DisplaySettingsApplier` 用 `Screen.resolutions` 选择精确/最近分辨率；不支持的分辨率会回退并写回实际值；应用异常时回退窗口化 1280×720。设置面板把宽/高/刷新率合并为一行“分辨率”，通过下拉选择平台支持的模式。
- **帧率与 VSync**：`FrameRateSettingsApplier` 最后应用（Order 90），避免分辨率初始化覆盖；平台在 `QualitySettings.vSyncCount > 0` 时会忽略 `Application.targetFrameRate`，所以改帧率会自动关闭 VSync，并在两者冲突时输出警告。`Unlimited = -1` 是平台默认帧率，不保证真正无上限。
- **安全模式**：`SettingsSafeMode` 独立记录连续启动失败，达到 2 次后强制窗口化 1280×720 + 低画质 + UI 缩放 1；完整启动成功后自动清除。文件在 `persistentDataPath/Settings/safe-mode.json`。
- **结构迁移**：`ISettingsMigration` + `SettingsMigrationChain` 逐级迁移；旧文件缺少 `schemaVersion` 按 0 处理，未来版本迁移失败时回退默认值且不覆盖原文件。项目可在启动前通过 `SettingsMigrationRegistry.Register(...)` 注入自定义迁移。
- **未知字段保留**：保存时用 `SettingsJsonMerge` 把已知字段覆盖到原始 JSON 上，保留未来版本/其它模块写入的未知字段（对象递归合并，对象数组按 `key`/`id`/`name` 合并，无 key 数组整体替换；解析失败退回已知 JSON）。
- **消费入口**：`graphics.fieldOfView` 由 `SettingsCameraBinder` 应用到相机；使用 Cinemachine 时可挂 `SettingsCinemachineBinder` 写入虚拟相机镜头；`SettingsInputProfile` 暴露输入灵敏度/死区/反转/震动/瞄准方式；`audio.ui`、`audio.voice`、`audio.muteWhenUnfocused` 由 `SettingsAudioMixerBinder` 写入 AudioMixer；`graphics.renderScale` 由图形应用器消费；`graphics.brightness` 可由 `SettingsVolumeBrightnessBinder` 反射写入 URP/HDRP `ColorAdjustments.postExposure`。
- **显示器**：`display.monitor` 由 `DisplaySettingsApplier` 通过 `Screen.GetDisplayLayout` / `Screen.MoveMainWindowTo` 切换；设置面板显示显示器下拉。
- **音频试听**：设置面板“试听音效”用 `SettingsAudioPreview` 运行时生成短音，不依赖音频资源。
- **首启同意**：`SettingsConsent` 扩展提供 `NeedsConsent`/`Accept`/`Decline`，状态落在 `privacy.termsVersion`（0 未处理、-1 拒绝、正数为已同意版本）和 `privacy.consentUtc`；普通设置页不显示这两项。
- **语言已接入本地化模块**：`language.ui` 由 `ILocalizationService` 消费；设置面板语言行可直接切换，场景 `LocalizedText`/`LocalizedTMPText` 即时刷新。项目覆盖表见 [`本地化 README`](../../Localization/README.md)。
- **键位改绑**：可在面板内点击键位行后按新键；轴绑定（移动）不可重绑；冲突/保留键会被拒绝并提示。
- **保存与加载都是主线程同步小文件操作**：保存防抖 1 秒 + 退出/切后台强制保存；加载同步读取以减少启动复杂度。文件 < 64KB 时开销可忽略；云端/大文件同步需另开异步通道。

## 验证

- 描述表自检：`new SettingsRegistry().Validate()` 返回空列表。
- 字段↔描述一致性：`Registry_EverySettingsFieldHasDescriptor`。
- 损坏恢复：主文件损坏回退 `.bak`；两者都坏回退默认值并触发 `LoadFailed`。
- 帧率统计：`FrameRateCounterTests` 验证不受 `Time.timeScale` 影响。
- 真机/Player 验证（分辨率、VSync、Input System 后端）需在目标设备补测。
