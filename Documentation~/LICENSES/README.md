# 第三方许可文本（随包分发）

本目录保存随 UPandaFramework 一起分发的第三方许可全文与来源说明。`THIRD_PARTY_NOTICES.md` 中的每一项都应指向这里的对应文件；无法确认许可的资产不得进入对外发布包。

| 组件 | 本地形态 | 许可 | 文本 |
|---|---|---|---|
| Unity Asset Bundle Browser | `Editor/AssetBundleTools/AssetEditor/` 源码与图标 | Unity Companion License v1.0 | [Unity-Companion-License-1.0.md](Unity-Companion-License-1.0.md) |
| Unity XR Tunneling Vignette | `Runtime/Shader/ShaderLibrary/Library/BuildIn/Tunneling Vignette/` | Unity Companion License v1.0 | [Unity-Companion-License-1.0.md](Unity-Companion-License-1.0.md) |
| SharpZipLib 0.86.0.518 | `Libraries/ICSharpCode.SharpZipLib.dll` | GPLv2 + Classpath 链接例外 | [GPL-2.0.txt](GPL-2.0.txt) · [SharpZipLib-0.86-exception.md](SharpZipLib-0.86-exception.md) |
| ExcelDataReader 2.1.2 | `Libraries/Excel.dll` | **未确认**（NuGet 包未声明许可） | 见 `THIRD_PARTY_NOTICES.md` 的待办说明 |
| Quick Outline | 已随描边重写移除 | — | — |

## 分发注意

- **SharpZipLib 0.86** 采用 GPLv2 加链接例外：可以链接进闭源商业应用，但分发该 DLL 时仍需随附 GPLv2 全文与例外声明，并满足 GPLv2 第 3 节对库本身源码的要求（随附源码，或提供至少三年有效的书面源码获取承诺）。源码包：<https://sourceforge.net/projects/sharpdevelop/files/SharpZipLib/0.86/SharpZipLib_0860_SourceSamples.zip/download>。
- **Unity Companion License** 允许复制、分发与再许可，但仅限用于 Unity 引擎相关的开发与分发，且必须保留许可与版权声明；不得把该代码作为独立产品单独分发。
- **ExcelDataReader 2.1.2** 的 NuGet 元数据没有许可字段，无法自证可再分发。发布前必须二选一：替换为上游 MIT 版本（3.x），或取得原作者书面授权。替换前不得把它标记为“已合规”。
