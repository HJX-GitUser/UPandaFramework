# 框架示例程序集

UPandaGF.Samples 引用 Contracts、Runtime、Extensions 和 TMP。零散示例放在此处，原 Demo/Example/Test/Study 目录通过 .asmref 加入同一程序集。
示例不再进入核心 Runtime；Editor 演示构建器引用 Samples。脚本移动保留 .meta GUID。
仅复制 Runtime/Manager 与 Runtime/Contracts 即可编译核心，不需要带入 Samples。

`Startup/GameLaunchExample` 是启动示例：框架就绪 → 打开加载界面 → （可选）热更程序集 → 加载首场景。场景加载统一走 `ISceneService`，AB 场景与随包（Build Settings）场景由框架自动区分，调用方不再需要 `sceneIsAB` 之类的开关。

[移动清单及迁移](../OptimzeLog/P1Framework/MIGRATION.md)
