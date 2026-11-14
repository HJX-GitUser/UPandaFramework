# 本地化 CSV 工具

菜单：

- `UPandaGF/本地化/导入 CSV...`：解析 CSV、校验、生成项目 catalog + 语言表；
- `UPandaGF/本地化/校验 CSV...`：只校验不改文件。

CSV 格式（UTF-8）：

```csv
key,zh-Hans,en
settings.panel.title,系统设置,Settings
quest.gold,金币：{0},Gold: {0}
```

规则：

- 第一列固定为 `key`，其余列是语言码（BCP-47，如 `zh-Hans`、`en`、`ja`）；
- 同一个 key 不允许重复；
- 占位符只支持 `string.Format` 的数字形式（`{0}`、`{1}`、可选格式 `{0:0.0}`），同一 key 在各语言中的占位符集合必须一致；
- 空翻译只给警告，不阻断导入；
- 输出：`Assets/UPandaFrameworkLocalization/Resources/LocalizationProject/catalog.json` + `<locale>.json`；
- 导入后把 `UPGameRoot` Inspector 的 `localization.projectCatalogPath` 填为
  `LocalizationProject/catalog`，运行时会先加载内置表，再用项目表覆盖/新增语言。

生成过程对已存在文件走临时文件 + `File.Replace`，校验失败时不写任何文件。
