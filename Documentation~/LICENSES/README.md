# 第三方许可文本（随包分发）

本目录保存随 UPandaFramework 一起分发的第三方许可全文与来源说明。`THIRD_PARTY_NOTICES.md` 中的每一项都应指向这里的对应文件；无法确认许可的资产不得进入对外发布包。

| 组件 | 本地形态 | 许可 | 文本 |
|---|---|---|---|
| Unity Asset Bundle Browser | `Editor/AssetBundleTools/AssetEditor/` 源码与图标 | Unity Companion License v1.0 | [Unity-Companion-License-1.0.md](Unity-Companion-License-1.0.md) |
| Unity XR Tunneling Vignette | `Runtime/Shader/ShaderLibrary/Library/BuildIn/Tunneling Vignette/` | Unity Companion License v1.0 | [Unity-Companion-License-1.0.md](Unity-Companion-License-1.0.md) |
| ExcelDataReader 3.6.0 | `Libraries/ExcelDataReader.dll` + `Libraries/ExcelDataReader.DataSet.dll`（netstandard2.0） | MIT | [MIT-ExcelDataReader.txt](MIT-ExcelDataReader.txt) |
| Quick Outline | 已随描边重写移除 | — | — |
| SharpZipLib 0.86 | 已随 ExcelDataReader 升级移除 | — | — |

## 分发注意

- **Unity Companion License** 允许复制、分发与再许可，但仅限用于 Unity 引擎相关的开发与分发，且必须保留许可与版权声明；不得把该代码作为独立产品单独分发。
- **ExcelDataReader 3.6.0** 采用 MIT，可随包分发；保留版权与许可声明即可。该版本在 `netstandard2.0` 目标下用 `System.IO.Compression` 读取 xlsx，不再依赖 GPL 的 SharpZipLib。
- **Shader 教学素材**中仍有来源待确认的贴图；在确认前不得进入对外发布包。`Sword.rar` 已移除。
