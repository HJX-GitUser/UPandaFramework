# 第三方声明 / Third-Party Notices

UPandaFramework 的原创部分采用根目录的 MIT 许可。随附第三方内容沿用其权利人的版权声明和适用许可。许可全文与来源说明统一放在 [`Documentation~/LICENSES/`](Documentation~/LICENSES/README.md)；本文件负责说明每一项的本地形态、核对状态与分发结论。

## Unity Asset Bundle Browser

编辑器工具包含基于 Unity Asset Bundle Browser 的源码和图标。上游 `1.7.0` 许可文件署名 `Copyright © 2017 Unity Technologies ApS`，采用 Unity Companion License。该许可将使用与分发限定在其 Unity 使用范围内，并要求保留许可和版权声明。本地导入的确切上游版本仍待核对，分发时应随附对应版本的完整许可和声明。 [本地编辑器工具](Editor/AssetBundleTools/AssetEditor/) · [许可全文](Documentation~/LICENSES/Unity-Companion-License-1.0.md) · [上游 1.7.0 许可](https://github.com/Unity-Technologies/AssetBundles-Browser/blob/1.7.0/license.md)

## ExcelDataReader

随附的 `ExcelDataReader.dll` 与 `ExcelDataReader.DataSet.dll` 来自 NuGet 包 `ExcelDataReader 3.6.0` / `ExcelDataReader.DataSet 3.6.0` 的 `netstandard2.0` 目标，用于编辑器的 Excel 导表（`ExcelReaderFactory.CreateOpenXmlReader` 读取 `.xlsx`，`AsDataSet()` 转 `DataTable`）。上游采用 **MIT** 许可，NuGet 元数据已声明 `license type="expression">MIT`；该版本在 `netstandard2.0` 目标下不依赖 SharpZipLib（改用 `System.IO.Compression`），因此包内不再需要 GPL 组件。已在 Unity 2021.3 中实测读取含中文的 xlsx。 [本地 DLL](Libraries/ExcelDataReader.dll) · [DataSet DLL](Libraries/ExcelDataReader.DataSet.dll) · [3.6.0 包元数据](https://www.nuget.org/packages/ExcelDataReader/3.6.0) · [上游 MIT 许可](https://github.com/ExcelDataReader/ExcelDataReader/blob/develop/LICENSE)

SHA-256 `FE5129647F6E46CDF51C59DD5198527EBA24512439DCBAB8C32D8BD1A8809F19`（ExcelDataReader.dll）
SHA-256 `ED43E87D86EE8EA5385F55BCE311CE12427BD25C7DA5DA125404F12F5E51CF97`（ExcelDataReader.DataSet.dll）

## Unity XR Tunneling Vignette

随附的暗角材质、预制体、Shader、Shader Graph 和半球模型，与 Unity XR Interaction Toolkit Starter Assets 中的同名资产对应。官方包采用 Unity Companion License。本地文件对应的具体发布版本及随附声明待核对；许可全文已随包提供。 [本地暗角资产](Runtime/Shader/ShaderLibrary/Library/BuildIn/Tunneling%20Vignette/) · [许可全文](Documentation~/LICENSES/Unity-Companion-License-1.0.md) · [官方样例清单](https://docs.unity.cn/Packages/com.unity.xr.interaction.toolkit@2.6/manual/samples-starter-assets.html#tunneling-vignette) · [官方包许可](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.6/license/LICENSE.html)

## 待核对的随附素材

Shader 教学案例的体积云噪声贴图、水面法线贴图 `WaterBump.jpeg` 等，仍需确认原始来源与适用的再分发条款；体积云 Shader 中的 Amplify Shader Editor 标记说明生成工具。`Sword.rar` 已按发布决策从包内移除。**其余来源不明的素材在确认前不得进入对外发布包**，可在发布时移到 `Samples~` 或剔除。 [Shader 教学案例](Runtime/Shader/UnityShader学习文档/案例/) · [体积云 Shader](Runtime/Shader/ShaderLibrary/Library/BuildIn/体积云/Clouds.shader)

## 结论与待办

| 组件 | 核对状态 | 分发结论 |
|---|---|---|
| Unity Asset Bundle Browser | 许可类型已确认，本地版本待核对 | 可随包分发（Unity 使用范围内），需保留许可全文 |
| ExcelDataReader 3.6.0 | MIT，NuGet 元数据已声明 | 可随包分发，需保留 MIT 声明 |
| Tunneling Vignette | 许可类型已确认，版本待核对 | 可随包分发（Unity 使用范围内） |
| Shader 教学素材 | 来源不明 | 从发布包剔除或补授权后再发；`Sword.rar` 已移除 |
