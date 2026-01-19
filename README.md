# UPandaFramework

Unity 的前端框架

项目地址：https://gitee.com/he-jinxian/upanda-framework.git

## 模块介绍

### 日志系统

1. 对 `Debug` 进行了封装，可以剔除日志，避免运行过程中的字符串拼接造成的性能损耗。
   - 启动日志 / 剔除日志：`UPandaGF -> 启动日志/剔除日志`
2. 可配置日志输出到本地。
3. 整合了 `LogView`，可以在项目出包后开启日志面板查看日志输出和调试信息。

### 资源管理

1. **AB包打包工具**
   - 基于官方的 `AssetBundle Browser` 进行扩展，新增资源上传页签，创建资源对比文件进行热更新。
   - 路径：`UPandaGF -> AB包工具 -> AssetBundle Browser`

2. **IAssetsLoader**
   - AB包加载接口。
   - 使用方式：
     ```csharp
     IAssetsLoader loader = UPGameRoot.Instance.GetAssetsLoader();
     ```
   - 直接用编辑器里的路径加载资源，开发过程中 `UPGameRoot` 的 Inspector 面板可以切换为 Editor 模式加载资源，打包时再切换为 AssetBundle 模式。
   - AssetBundle支持本地加载、远程加载或者资源更新下载到本地后加载

3. **ResourcesLoader**
   - 封装 Unity 内置的 `Resources.Load` 方法，避免资源重复加载，增加引用计数和异步加载状态管理。

4. **StreamingAssetsLoadTool**
   - `StreamingAssets` 路径下的资源加载工具，处理好了跨平台兼容性，提供多种加载方式。

### 数据表

#### ExcelTool

   - 根据 Excel 配置的数据生成数据类和数据容器类。

### 事件系统

#### EventCenterModule

**事件中心**

- 监听、移除示例：
  ```csharp
  using UnityEngine;

  public class EventCenterUse_AddListener : MonoBehaviour
  {
      private void OnEnable()
      {
          EventCenter.Instance.AddEventListener<EnentTest1>(TestEvent1); // 事件注册
      }

      private void OnDestroy()
      {
          EventCenter.Instance.RemoveEventListener<EnentTest1>(TestEvent1);
      }

      private void TestEvent1(EventArgBase arg0)
      {
          EnentTest1 arg = arg0 as EnentTest1;
          PLoger.Log($"事件id:{arg.EventID} 参数：{arg.arg0} 事件触发1");
      }
  }

  public class EnentTest1 : EventArgBase // 声明事件对象
  {
      public override int EventID => typeof(EnentTest1).GetHashCode();
      public string arg0 { get; private set; }//自定义参数
      public EnentTest1(string arg)
      {
          arg0 = arg;
      }
  }
  ```

- 触发示例：
  ```csharp
    EventCenter.Instance.EventTrigger(new EnentTest1("触发测试")); // 触发事件
  ```

## 贡献

欢迎提交 Pull Request 或 Issue 来帮助改进框架。

## 许可证

本项目使用 MIT 许可证。