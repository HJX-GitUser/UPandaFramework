# 本地多语言（Localization）

离线多语言模块：内置 `zh-Hans`/`en`，项目通过 catalog + JSON 表扩展任意语言；设置项 `language.ui` 驱动切换，切换后 UI 即时刷新、无需重启。

## 组成

| 文件/资源 | 职责 |
| --- | --- |
| `Runtime/Contracts/Localization/ILocalizationService.cs` | 服务契约：当前语言、`Get/Format`、切换事件、加载 |
| `Runtime/Contracts/Localization/LocaleInfo.cs` | 语言码/显示名/表路径 |
| `Runtime/Contracts/Localization/LocalizationSettings.cs` | 开发者配置（内置/项目 catalog 路径、预加载、缺失策略） |
| `Runtime/Localization/LocalizationService.cs` | catalog 合并、表加载、系统语言解析、回退与缺失日志 |
| `Runtime/Localization/LocalizationCatalog.cs` | catalog/表 JSON POCO 与解析 |
| `Runtime/Localization/LocalizationAssetSource.cs` | 通过 `IAssetService` 读 TextAsset（读取后归还租约） |
| `Runtime/Localization/SystemLocaleMapper.cs` | `Application.systemLanguage` / 区域写法 → 项目语言码 |
| `Runtime/Resources/Localization/catalog.json` | 内置语言目录 |
| `Runtime/Resources/Localization/zh-Hans.json`、`en.json` | 内置语言表（含设置面板全部 key） |
| `Runtime/UI/UIMgr/LocalizedText.cs` | uGUI Text 本地化组件 |
| `Runtime/UI/UIMgr/LocalizedTMPText.cs` | TMP 本地化组件 |
| `Runtime/UI/UIMgr/LocalizationBinder.cs` | 代码绑定（返回 IDisposable） |

## 数据格式

Catalog：

```json
{
  "schemaVersion": 1,
  "defaultLocale": "zh-Hans",
  "fallbackLocale": "en",
  "locales": [
    { "code": "zh-Hans", "displayName": "简体中文", "table": "Localization/zh-Hans", "overrideTable": "" }
  ]
}
```

语言表：

```json
{
  "schemaVersion": 1,
  "locale": "zh-Hans",
  "entries": [ { "key": "settings.panel.title", "value": "系统设置" } ]
}
```

- 内置表固定在包内 Resources；项目覆盖表由 `UPGameRootConfig.localization.projectCatalogPath` 指定。
- 项目 catalog 对已有语言填 `overrideTable`（只覆盖部分 key），新语言填 `table`（项目表路径）。
- 表加载后立即归还 `TextAsset`，运行时只保留字典。

## 接入

`GameServices` 已装配 `ILocalizationService Localization`；`UPGameRoot` 启动顺序：

```
Services → Settings → Localization → UI
```

业务：

```csharp
string text = services.Localization.Get("quest.accept");
string formatted = services.Localization.Format("quest.reward", gold);
services.Localization.LocaleChanged += change => Debug.Log(change.CurrentLocale);
```

UI：

- 场景/预制体挂 `LocalizedText`（uGUI）或 `LocalizedTMPText`（TMP），填 key；组件实现 `IGameServicesConsumer`，由框架注入。
- 动态代码可用 `LocalizationBinder.Bind(services.Localization, text, "key", args)`，返回值加入 `BasePanel.OpenEvents`。

设置面板：

- `language.ui` 在面板中显示为语言行（跟随系统 + catalog 内语言）。
- 面板标题/分类/设置项/选项/改键提示全部走本地化 key；无本地化服务时回退 `SettingsText` 中文。

## 项目新增语言（无需改代码）

1. 用 `UPandaGF/本地化/导入 CSV...` 导入 `key,zh-Hans,ja` 形式的 CSV；工具生成 `Assets/UPandaFrameworkLocalization/Resources/LocalizationProject/`。
2. 在 `UPGameRoot` 的 Inspector → Configuration 里设置
   `localization.projectCatalogPath = "LocalizationProject/catalog"`（Resources 模式）。
3. 运行后设置面板语言行出现该语言；也可直接 `SetLocale("ja")`。

CSV 首列为 `key`，其余列为语言码；缺失翻译/占位符不一致会被校验拦下。

## 缺失与回退

- 查找链：当前语言 → `fallbackLocale` → `defaultLocale` → key。
- `language.ui` 为空 = 跟随系统；不支持的系统语言回退 default。
- 缺失 key 默认返回 key 并只警告一次；可在 `LocalizationSettings.missingPolicy` 改为空串/fallback。
- 内置表缺失/损坏不会阻断启动；面板回退 `SettingsText`。

## 字体与限制（P0）

- 包内没有 TMP FontAsset；宿主需给 TMP 字体配置 `zh-Hans`/`en` fallback，否则可能出现缺字形。
- uGUI Text 在运行时优先使用 `UnityBuiltinFont.GetDefault()`（含中文字形）。
- P1：按语言自动换 TMP 字体、字幕/语音语言、复数/RTL、懒加载/热重载、XLSX 导入。
