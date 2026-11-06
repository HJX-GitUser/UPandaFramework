# UI 生命周期约定

- ShowPanel 和 ShowPanelAsync 共用同一加载流程。同一路径并发请求共用一个实例；每个成功请求仍调用一次 OnOpen，参数以各次请求为准。
- ClosePanel 同时取消该路径的待打开请求，等待者返回 null。旧请求晚到的加载结果不会重新打开面板。
- 加载失败或 UIManager 销毁时返回 null，回调接口也一定结束。OnOpen 中抛异常会记录并使该次请求返回 null。
- Resources 面板只持有一份预制体引用，绑定到实例的 PanelResourceLease 在实例销毁时归还；外部直接 Destroy 也生效。
- AssetBundle 继续沿用现有包级生命周期，没有引入单资源卸载。
- UILoadInfo 路径应只对应一种面板类型；公共 panelDic 为兼容保留，业务应优先通过管理器 API 访问。
- BasePanel 包括 inactive 子控件；InputField 的变化、提交、结束编辑分别连接对应 Unity 事件。
- DragArea 使用同一父节点坐标系和按下事件的相机。WorldSpaceOverlayUI 销毁时恢复原材质并释放副本。

## P0 显式关闭（2026-11-06）

新增幂等 Shutdown()，由启动协调器逆序关闭时调用；终止待打开请求并销毁面板。OnDestroyed 复用同一清理入口。关闭后的实例不可重新使用。见 [P0 记录](../../../OptimzeLog/P0Startup/README.md)。
