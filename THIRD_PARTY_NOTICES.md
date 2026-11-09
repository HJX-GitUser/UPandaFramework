# 第三方声明 / Third-Party Notices

UPandaFramework 的原创部分采用根目录的 MIT 许可。随附第三方内容沿用其权利人的版权声明和适用许可。本文件记录已经识别的来源，并标出仍待核对的导入版本或再分发权限。 [框架 MIT 许可](LICENSE)

## Unity Asset Bundle Browser

编辑器工具包含基于 Unity Asset Bundle Browser 的源码和图标。上游 `1.7.0` 许可文件署名 `Copyright © 2017 Unity Technologies ApS`，采用 Unity Companion License。该许可将使用与分发限定在其 Unity 使用范围内，并要求保留许可和版权声明。本地导入的确切上游版本仍待核对，分发时应随附对应版本的完整许可和声明。 [本地编辑器工具](Editor/AssetBundleTools/AssetEditor/) · [上游 1.7.0 许可](https://github.com/Unity-Technologies/AssetBundles-Browser/blob/1.7.0/license.md) · [Unity Companion License](https://unity.com/legal/licenses/unity-companion-license)

## SharpZipLib

随附的 `ICSharpCode.SharpZipLib.dll` 程序集版本为 `0.86.0.518`。文件版权信息为 `Copyright 2001-2010 Mike Krueger, John Reilly`。上游说明旧版采用 GPLv2 加链接例外，并于 `1.0.0` 改用 MIT。分发该旧 DLL 时，应按对应版本的 GPLv2 和例外条款保留许可声明，并履行该库本身的对应源码提供义务。 [本地 DLL](Libraries/ICSharpCode.SharpZipLib.dll) · [旧版许可与链接例外](https://icsharpcode.github.io/SharpZipLib/legacy.html) · [GPLv2 原文](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html) · [1.0.0 发布记录](https://github.com/icsharpcode/SharpZipLib/releases/tag/v1.0.0)

SHA-256 `40B3D590F95191F3E33E5D00E534FA40F823D9B1BB2A9AFE05F139C4E0A3AF8D`

## ExcelDataReader

随附的 `Excel.dll` 产品名为 ExcelDataReader，程序集版本为 `2.1.2.0`。当前上游项目采用 MIT，本地 DLL 和对应 NuGet 发布页未提供可核对的该版本许可文本。确切许可与版权声明待从原发布包或对应上游提交确认。 [本地 DLL](Libraries/Excel.dll) · [2.1.2 发布页](https://www.nuget.org/packages/ExcelDataReader/2.1.2) · [当前上游许可](https://github.com/ExcelDataReader/ExcelDataReader/blob/develop/LICENSE)

SHA-256 `91B44F95992F2D649CB6262C40850A7A4FE007271169AB1687002788010AA1B9`

## Unity XR Tunneling Vignette

随附的暗角材质、预制体、Shader、Shader Graph 和半球模型，与 Unity XR Interaction Toolkit Starter Assets 中的同名资产对应。官方包采用 Unity Companion License。本地文件对应的具体发布版本及随附声明待核对。 [本地暗角资产](Runtime/Shader/ShaderLibrary/Library/BuildIn/Tunneling%20Vignette/) · [官方样例清单](https://docs.unity.cn/Packages/com.unity.xr.interaction.toolkit@2.6/manual/samples-starter-assets.html#tunneling-vignette) · [官方包许可](https://docs.unity3d.com/Packages/com.unity.xr.interaction.toolkit@2.6/license/LICENSE.html)

## 待核对的随附素材

Shader 教学案例的体积云噪声贴图、水面法线贴图 `WaterBump.jpeg` 和 `Sword.rar` 中的模型及贴图，仍需确认原始来源与适用的再分发条款。体积云 Shader 中的 Amplify Shader Editor 标记说明生成工具。 [Shader 教学案例](Runtime/Shader/UnityShader学习文档/案例/) · [体积云 Shader](Runtime/Shader/ShaderLibrary/Library/BuildIn/体积云/Clouds.shader)
