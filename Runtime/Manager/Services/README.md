# 应用服务与场景作用域

P1 的实现入口。契约位于 Runtime/Contracts，根节点在 Assets 就绪后装配 GameServices，并向场景 IGameServicesConsumer 注入。

| 文件 | 职责 |
| --- | --- |
| GameServices.cs | 显式装配及逆序关闭；场景资源、对象池扩展方法 |
| AssetService.cs | Resources/AB 统一凭据、取消等待和迟到资源释放 |
| SceneService.cs | 原生后端、串行队列、请求进度、场景 handle 作用域 |
| LifetimeScope.cs / ScopeSubscriptions.cs | 取消、逆序释放及两套事件的独立作用域订阅 |
| InputAndTime.cs | 输入动作映射、上下文栈、嵌套暂停、游戏时钟 |
| PauseMenuController.cs | 绑定现有界面的暂停菜单行为 |
| FileSaveService.cs | 现有版本化序列化的存档适配 |

所有 Unity 对象操作、场景与作用域 API 均在主线程使用。外部依赖通过构造函数或 Configure 注入。

[完整设计与兼容说明](../../../OptimzeLog/P1Framework/README.md) · [使用示例](../../../OptimzeLog/P1Framework/MIGRATION.md) · [验证记录](../../../OptimzeLog/P1Framework/VALIDATION.md)
