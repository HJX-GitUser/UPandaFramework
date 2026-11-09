# UPandaFramework 0.7.1 发布说明

> 发布日期：2026-11-10 · 版本：`0.7.1` · 前一版本：`0.7.0`

## 修复

**URP 下描边没有效果**：URP 的 `DrawRenderers` 对每个 Renderer 只执行一个匹配 Pass，原来的两 Pass 材质在 URP 下只会执行不可见的模板 Pass，所以看不到描边。

- URP：运行时为目标创建两个代理 Renderer（模板 + 描边），分别使用单 Pass 的 `OutlineMask.shader`（队列 3100）与 `OutlineFill.shader`（队列 3101），靠渲染队列保证先写模板再描边；五种模式继续通过材质里的 `_MaskZTest` / `_FillZTest` 区分。
- Built-in：保持原 `Outline.shader` 两 Pass 方案不变。
- 序列化字段、组件 API、材质属性名均未变化，已有场景不需要重新绑定。

## 验证

| 项 | 结果 |
|---|---|
| URP 14 / Unity 2022.3 Player | 描边像素 302（本体 + 外圈），关闭后 0 |
| Built-in / Unity 2021.3 | 描边像素 302（与 URP 一致） |
| EditMode / PlayMode | 53 / 53、324 / 324 |

## 已知限制

- URP 需要相机带模板缓冲（URP 默认深度-模板附件即可）；自定义 Renderer / Depth Priming 组合建议在目标工程复测。
- 多个描边对象共用模板值 1，屏幕上重叠时可能互相裁剪（与 0.7.0 相同）。
- HDRP 模板仍未在目标管线验收。
