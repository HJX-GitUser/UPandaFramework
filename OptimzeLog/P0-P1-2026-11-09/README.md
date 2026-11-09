# 描边重写与存档加固验证（2026-11-09）

## 目标

- **P0**：在 Unity 内完成 OutLine 原创重写（代理渲染 + 单材质双 Pass + URP 分支）的编译与 PlayMode 回归，补齐此前只能做静态编译的空缺。
- **P1**：`FileSaveService` 读取异常回退收窄、单次读取与迁移副本清理；`OutlineMeshCache` 按 InstanceID 记账并在场景卸载时清理；`OutLine` 增量重建与代理调试开关。

## 环境

- Unity：`2021.3.45f2c1`，batchmode + `-nographics`；本机许可在完整文件访问权限下可正常激活。
- 工程：`Framework Validation` 的完整副本 `_p0validation`。原工程当时被 Unity 编辑器占用，副本避免打断正在使用的会话。
- 框架：仓库工作树同步到副本的 `Assets/upanda-framework`，与待提交内容一致。

## 结果

| 项 | 结果 | 原始文件 |
| --- | --- | --- |
| Unity 编译 | 日志中 0 个 `error CS`（测试启动前完成程序集编译） | 运行日志（未入库） |
| PlayMode | **323 / 323 通过**，0 失败 0 跳过 | [playmode-2026-11-09.xml](playmode-2026-11-09.xml) |
| EditMode | **53 / 53 通过**，0 失败 0 跳过 | [editmode-2026-11-09.xml](editmode-2026-11-09.xml) |
| OutLine 专项 | 9 / 9（代理渲染、子网格合并、网格共享与释放、层级接管、像素宽度、代理池复用、增量重建） | 同上 |
| 存档专项 | 6 / 6（往返、路径校验、中断恢复、备份回退、双损坏、迁移副本清理、占用时抛错） | 同上 |

## 复现

```powershell
$unity = 'D:\Unity\2021.3.45f2c1\Editor\Unity.exe'
& $unity -batchmode -nographics -projectPath "<工程副本>" `
  -runTests -testPlatform PlayMode -testResults "<输出>.xml" -logFile "<日志>.log"
& $unity -batchmode -nographics -projectPath "<工程副本>" `
  -runTests -testPlatform EditMode -testResults "<输出>.xml" -logFile "<日志>.log"
```

## 后续修复验证（同日）

高亮 demo 停止运行时出现 `Cannot set the parent of the GameObject ... while activating or deactivating the parent GameObject ...`：代理池在目标停用/销毁过程中仍尝试把代理搬回池。已修复为「目标停用/销毁时直接销毁代理；挂接被拒绝时下一帧重试」，并补充回归用例。

- PlayMode：**324 / 324**（新增「停用父物体不报错、重新激活后重新挂接代理」）
- EditMode：**53 / 53**
- 原始文件：[playmode-fix-2026-11-09.xml](playmode-fix-2026-11-09.xml) · [editmode-fix-2026-11-09.xml](editmode-fix-2026-11-09.xml)

## 备注

- 测试在工程副本中运行，`Assets/upanda-framework` 与仓库一致；原工程未参与本轮批处理。
- Shader（Built-in + URP 分支）在导入阶段无编译错误；URP/HDRP 分支的实际观感仍需在对应管线的目标工程验收。HDRP 模板位于 `Shaders~`，Unity 不导入，需要手动启用。
- 本轮新增用例：主档被占用时抛 `IOException` 而不回退、`Delete` 清理 `*.legacy.<guid>.bak`、像素宽度切换 `_WidthMode`、代理池复用同一实例、增量重建保留未受影响的代理。
- 批处理结束时 Unity 进程未自行退出（测试结果已落盘），由脚本强制结束；不影响测试结论。
