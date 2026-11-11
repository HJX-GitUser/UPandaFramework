# UPandaFramework 0.8.1 发布说明

> 发布日期：2026-11-12 · 版本：`0.8.1` · 前一版本：`0.8.0`

## 重点

本版本把 UI 框架从「能打开面板」补齐为可支撑主菜单、暂停菜单、设置、HUD、弹窗、引导与 Toast 的完整 UI 框架，并加入 Built-in/URP/HDRP 的 UI 相机适配。主要能力：

- 面板状态机、`UIPanelHandle`、五层栈（Background/Main/Popup/Guide/Toast）、模态暂停/输入上下文、返回键与 Toast 队列。
- 异步打开/关闭竞态修复：加载中关闭取消、迟到租约归还、重复打开只 `OnRefresh`、作用域关闭与外部销毁收口。
- 面板池、`UIEventScope` 自动解绑、一次遍历控件缓存、`Observable`/`UIBinder` 数据绑定。
- `UIItemPool` + 设置描述表行池、`UIFocusNavigator` 焦点导航、`UICanvasScale`/`UISafeArea`。
- 热更新友好的非泛型 `OpenAsync(Type)`/`ClosePanel(Type)`/`RegisterPanels`、面板诊断表。
- 面板校验器、重写的脚本生成器、CI 用例下限与迁移文档。

## URP UI 相机（方案 B）

URP 下默认采用 **主相机 Base + UI 相机 Overlay + Camera Stack**：

- `UIManager` 创建/接收 UI 相机后，若存在 `Camera.main`，自动把 UI 相机设为 `Overlay`、关闭其 post-processing，并加入主相机的 Camera Stack；主相机设为 Base。
- 主相机尚未加载时先让 UI 相机以 Base 渲染，收到 `SceneManager.sceneLoaded` 后自动重试；也可调用 `UIManager.RefreshCameraStack()` 手动重试。
- 运行时切换画质档位/渲染管线时自动从旧主相机 stack 移除并重新适配；`Shutdown` 会解除 stack 引用并退订事件。
- Built-in 保持 `Depth + depth 10`；HDRP 追加 `HDAdditionalCameraData`（clearDepth）。
- 包本身不引用 URP/HDRP 程序集，通过反射适配；未安装对应管线时只警告一次。

前置要求：

- URP 主相机需要 `Tag = MainCamera`（`Camera.main`）。
- 主相机 Culling Mask 不包含 UI 层，UI 相机只渲染 UI 层。
- Canvas 使用 `Screen Space - Camera`，`Render Camera` 指向 UI 相机。
- 多主相机/自定义 Camera Stack 项目在切换后调用 `UIManager.RefreshCameraStack()`，可用 `UiCameraStackOwner` 查询当前 stack 持有者。

## 验证

| 项目 | 结果 |
|---|---|
| 编译 | 0 个 `error CS` |
| 全量 EditMode | 68 / 68 |
| 全量 PlayMode | 383 / 383 |

验证工程为 Built-in；URP/HDRP 分支做了编译、反射容错与 API 测试，但**未在真实 URP/HDRP 宿主做像素/相机栈复验**，建议在目标管线工程各跑一次 UI 渲染确认。

## 升级提示

- 旧 `ShowPanelAsync`/`ShowPanel`/`GetPanel`/`ClosePanel<T>` 保留，业务可渐进迁移到 `OpenAsync`/`UIPanelHandle`。
- `E_UI_Layer` 新名与旧名同值；旧 `Bot/Mid/Top/System` 继续可编译。
- 详细迁移见 `Documentation~/MIGRATION-UI.md`。
