# UPandaFramework 0.7.0 发布说明

> 发布日期：2026-11-09 · 版本：`0.7.0` · 前一版本：`0.6.0`
> 完整变更见 [CHANGELOG.md](../CHANGELOG.md)。

## 亮点

1. **描边模块原创重写**：`OutLine` 改为代理渲染 + 单材质双 Pass（先写模板、再沿法线外扩），不再向业务 Renderer 追加材质槽；新增代理对象池、增量重建、屏幕相对/像素两种宽度单位、URP 同 Shader 分支与可选 HDRP 模板。
2. **存档加固**：`FileSaveService.Load` 只在主档确实损坏（`InvalidDataException`）时才回退备份，文件占用/权限错误直接抛出；单次读取复用字节；`Delete` 清理迁移遗留的 `*.legacy*.bak`；`OutlineMeshCache` 按 InstanceID 记账并在场景卸载时清理。
3. **无 AB 模式**：新增 `AssetLoaddingMethod.NoAssetBundle`，启动时跳过 AssetBundle 初始化，适合小游戏与原型。
4. **合规材料**：新增 `Documentation~/LICENSES/`，随包提供 Unity Companion License 全文与 ExcelDataReader 的 MIT 声明；导表库升级到 **ExcelDataReader 3.6.0（MIT）**，移除 GPL 的 SharpZipLib 0.86，并从包内剔除 `Sword.rar` 等来源不明的教学素材。

## 破坏性变更

- `OutLine` 不再修改目标 `Renderer.sharedMaterials`；依赖“末尾两个材质槽是描边材质”的代码需要改为读取代理对象（子物体名以 `(OutLine)` 结尾）。
- 旧 `OutlineMask.shader` / `OutlineFill.shader` 与两个材质被 `Outline.shader` + `Resources/Materials/Outline.mat` 取代。
- `Precompute Outline` 的序列化字段改名为 `bakedMeshes` / `bakedNormals`，旧数据需执行 **重新计算描边法线**。
- 平滑法线从 `UV3` 改到 `NORMAL`，模型不再需要预留 UV3。

## 升级步骤

1. 通过 Git URL 或本地 `file:` 安装 0.7.0，确认 `TextMeshPro` 依赖可解析。
2. 打开使用描边的场景/预制体，对启用 `Precompute Outline` 的对象执行 **重新计算描边法线**。
3. 若业务代码读取了描边材质槽，改为读取代理对象或 `OutLine.OutlineColor` / `OutlineWidth`。
4. 需要 HDRP 的项目，把 `Shaders~/HDRP/OutlineHDRP.shader` 复制到 `Shaders/`；本版本未在 HDRP 目标管线验收。

## 验证结果（本机）

| 项 | 结果 | 证据 |
|---|---|---|
| Unity 2021.3 编译 | 0 `error CS` | `OptimzeLog/P0P1-2026-11-09/compile-2021.log` |
| EditMode | 53 / 53 | `OptimzeLog/P0P1-2026-11-09/editmode-2021.xml` |
| PlayMode | 324 / 324 | `OptimzeLog/P0P1-2026-11-09/playmode-2021.xml` |
| Unity 2022.3 编译 | 0 `error CS` | `D:\upanda_build_p0p1\compile-2022.log`（本机构建工程） |
| Windows / Android / WebGL 构建 | 见 `OptimzeLog/P0P1-2026-11-09/builds/build-summary.txt` | 本机 2022.3 构建 |

## 已知限制

- HDRP 模板只覆盖透视相机，未在 HDRP 目标管线编译验收；URP 分支依赖项目深度/模板缓冲配置。
- 真机（Android/iOS）日志面板手测未执行，清单见 `OptimzeLog/LogConsoleDeviceChecklist-2026-11-09.md`。
- ExcelDataReader 已升级到 3.6.0（MIT），`SharpZipLib 0.86` 与 `Sword.rar` 已从包内移除；体积云噪声贴图、`WaterBump.jpeg` 等教学素材的来源仍需确认，确认前不得进入对外发布包。
- 静态合批的 Renderer 不支持描边；重叠描边对象共用模板值 1，可能互相裁剪。
