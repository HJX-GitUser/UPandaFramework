# 第三方声明 / Third-Party Notices

UPandaFramework 的原创部分采用根目录的 MIT 许可。随附第三方内容沿用其权利人的版权声明和适用许可。许可全文与来源说明统一放在 [`Documentation~/LICENSES/`](Documentation~/LICENSES/README.md)；本文件负责说明每一项的本地形态、核对状态与分发结论。

## Unity Asset Bundle Browser

编辑器工具包含基于 Unity Asset Bundle Browser 的源码和图标。上游 `1.7.0` 许可文件署名 `Copyright © 2017 Unity Technologies ApS`，采用 Unity Companion License。该许可将使用与分发限定在其 Unity 使用范围内，并要求保留许可和版权声明。本地导入的确切上游版本仍待核对，分发时应随附对应版本的完整许可和声明。 [本地编辑器工具](Editor/AssetBundleTools/AssetEditor/) · [许可全文](Documentation~/LICENSES/Unity-Companion-License-1.0.md) · [上游 1.7.0 许可](https://github.com/Unity-Technologies/AssetBundles-Browser/blob/1.7.0/license.md)

## SharpZipLib

随附的 `ICSharpCode.SharpZipLib.dll` 程序集版本为 `0.86.0.518`。文件版权信息为 `Copyright 2001-2010 Mike Krueger, John Reilly`。上游说明旧版采用 GPLv2 加链接例外（可链接进闭源商业应用），并于 `1.0.0` 改用 MIT。分发该旧 DLL 时，应随附 GPLv2 与例外条款全文，并履行该库本身的对应源码提供义务。该 DLL 由 `Excel.dll`（ExcelDataReader 2.1.2）在打开 `.xlsx` 时使用，不能单独删除。 [本地 DLL](Libraries/ICSharpCode.SharpZipLib.dll) · [许可与例外](Documentation~/LICENSES/SharpZipLib-0.86-exception.md) · [GPLv2 全文](Documentation~/LICENSES/GPL-2.0.txt) · [旧版许可说明](https://icsharpcode.github.io/SharpZipLib/legacy.html) · [0.86 源码包](https://sourceforge.net/projects/sharpdevelop/files/SharpZipLib/0.86/SharpZipLib_0860_SourceSamples.zip/download)

SHA-256 `40B3D590F95191F3E33E5D00E534FA40F823D9B1BB2A9AFE05F139C4E0A3AF8D`

## ExcelDataReader

随附的 `Excel.dll` 产品名为 ExcelDataReader，程序集版本为 `2.1.2.0`，用于编辑器的 Excel 导表。NuGet 包 `ExcelDataReader 2.1.2` 的元数据没有 `license` 字段，且声明依赖 `SharpZipLib 0.86.0`；本地 DLL 也未附许可文本，因此**当前无法自证可再分发**。发布前必须二选一：替换为上游 MIT 版本（3.x）并重新验证导表，或取得原作者书面授权。 [本地 DLL](Libraries/Excel.dll) · [2.1.2 包元数据](https://www.nuget.org/packages/ExcelDataReader/2.1.2) · [当前上游许可（MIT）](https://github.com/ExcelDataReader/ExcelDataReader/blob/develop/LICENSE)

SHA-256 `91B44F95992F2D649CB6262C40850A7A4FE007271169AB1687002788010AA1B9`

## Unity XR Tunneling Vignette

随附的暗角材质、预制体、Shader、Shader Graph 和半球模型，与 Unity XR Interaction Toolkit Starter Assets 中的同名资产对应。官方包采用 Unity Companion License。本地文件对应的具体发布版本及随附声明待核对；许可全文已随包提供。 [本地暗角资产](Runtime/Shader/ShaderLibrary/Library/BuildIn/Tunneling%20Vignette/) · [许可全文](Documentation~/LICENSES/Unity-Companion-License-1.0.md) · [官方样例清单](https://docs.unity.cn/Packages/com.unity.xr.interaction.toolkit@2.6/manual/samples-starter-assets.html#tunneling-vignette) · [官方包许可](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.6/license/LICENSE.html)

## 待核对的随附素材

Shader 教学案例的体积云噪声贴图、水面法线贴图 `WaterBump.jpeg` 和 `Sword.rar` 中的模型及贴图，仍需确认原始来源与适用的再分发条款。体积云 Shader 中的 Amplify Shader Editor 标记说明生成工具。**在来源确认前，这些素材不得进入对外发布包**；可先移到 `Samples~` 或发布时剔除。 [Shader 教学案例](Runtime/Shader/UnityShader学习文档/案例/) · [体积云 Shader](Runtime/Shader/ShaderLibrary/Library/BuildIn/体积云/Clouds.shader)

## 结论与待办

| 组件 | 核对状态 | 分发结论 |
|---|---|---|
| Unity Asset Bundle Browser | 许可类型已确认，本地版本待核对 | 可随包分发（Unity 使用范围内），需保留许可全文 |
| SharpZipLib 0.86 | 许可与例外已确认，源码提供义务待落实 | 可随包分发，但发布说明必须给出源码获取方式 |
| ExcelDataReader 2.1.2 | **无法确认** | 未处理前不得对外发布导表工具 |
| Tunneling Vignette | 许可类型已确认，版本待核对 | 可随包分发（Unity 使用范围内） |
| Shader 教学素材 | **来源不明** | 从发布包剔除或补授权后再发 |
