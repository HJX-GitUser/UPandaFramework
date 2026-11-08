# 日志控制台图标设计

图标位置：`Runtime/Composition/LogConsole/Icons`，共 36 张 PNG：23 个功能图标、13 张界面纹理。图标由项目方委托设计，作为 UPandaFramework 自有资产随包分发，供 `UPandaLogConsole` 使用。

## 视觉规范

- 原创 SVG 几何图形，24 单位网格，圆角端点；常规线宽 1.8，强调线宽 2.1。
- 功能图标使用蓝灰 `#425570`、蓝色 `#377FE6` 和淡蓝 `#E7F0FD`。
- 日志级别使用蓝色信息圆、黄色警告三角、红色错误八角形，同时依靠颜色和形状区分。
- 当前日志控制台使用深色面板与灰白文字；选中行使用蓝底搭配白色文字。
- `UnityIcon.png` 用于显示场景信息，图形采用场景立方体。
- 图标保留透明背景；滚动条使用 14px 竖向滑轨与圆角 thumb，移除上下/左右箭头图标。

## 兼容性

保留全部原 PNG 文件名、源尺寸、`.meta` 与 GUID；无需重新绑定组件。滚动条 GUISkin 的 8 个既有失效 GUID 已修正为当前纹理 GUID。运行时代码与视图见 `Runtime/Composition/LogConsole/`。Unity 导入器原有 NPOT 缩放规则保留，因此部分非二次幂 PNG 的导入尺寸与源尺寸不同。

## 文件与维护

- `Sources/*.svg`：36 份可编辑矢量源稿。
- `generate_icons.py`：从统一几何定义重建 SVG 与原路径 PNG。
- `manifest.json`：文件名、源尺寸、SHA-256。
- `preview.png`：图标总览与静态面板组合预览，不是运行截图。

从 Unity 项目根目录执行：

```powershell
python -m pip install resvg-py Pillow
python Assets/upanda-framework/Editor/PLogger/LogConsoleIconDesign/generate_icons.py
```

生成器使用项目内相对路径定位 `Assets/upanda-framework`；若包安装在 `Packages/` 或其它位置，请直接修改脚本里的 `PACKAGE` 路径。

图标采用原生 SVG 绘制与 resvg 导出，没有使用生成式位图或图片编辑 API。后续调整应修改生成器，再同步重建源稿与 PNG，避免两者不一致。

## 验证

- 36 张 PNG 均发生变化，全部为 RGBA，源尺寸逐项保持。
- PNG 文件总大小从 213,006 bytes 降至 38,166 bytes（约减少 82%）；磁盘体积变化不等于 GPU 内存变化。
- 已检查浅色背景、深色背景与小尺寸图形；主要适用场景为深色日志控制台。
- Unity 2022.3.48f1c1 已确认 36 张 PNG 导入成功，源尺寸/GUID 保持，滚动条皮肤引用有效。
