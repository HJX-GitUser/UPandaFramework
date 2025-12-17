```markdown
# UPandaFramework

Unity 的前端框架，提供了一套完整的开发工具和模块，帮助开发者快速构建游戏项目。

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

3. **ResourcesLoader**
   - 封装 Unity 内置的 `Resources.Load` 方法，避免资源重复加载，增加引用计数和异步加载状态管理。

4. **StreamingAssetsLoadTool**
   - `StreamingAssets` 路径下的资源加载工具，处理好了跨平台兼容性，提供多种加载方式。

### 数据表

#### ExcelTool

**Excel 工具**

1. 根据 Excel 配置的数据生成数据类和数据容器类。
2. 把 Excel 里的数据生成二进制文件放入 `StreamingAssets` 里，用 `BinaryDataMgr` 类获取相关数据。

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
          EventCenter.Instance.AddEventListener<EnentTest1>(TestEvent2);
      }

      private void OnDisable()
      {
          EventCenter.Instance.RemoveEventListener<EnentTest1>(TestEvent1); // 事件注销
      }

      private void OnDestroy()
      {
          EventCenter.Instance.RemoveEventListener<EnentTest1>(TestEvent2);
      }

      private void TestEvent1(EventArgBase arg0)
      {
          EnentTest1 arg = arg0 as EnentTest1;
          PLoger.Log($"事件id:{arg.EventID} 参数：{arg.arg0} 事件触发1");
      }

      private void TestEvent2(EventArgBase arg0)
      {
          EnentTest1 arg = arg0 as EnentTest1;
          PLoger.Log($"事件id:{arg.EventID} 参数：{arg.arg0} 事件触发2");
      }
  }

  public class EnentTest1 : EventArgBase // 声明事件对象
  {
      public override int EventID => typeof(EnentTest1).GetHashCode();
      public string arg0 { get; private set; }
      public EnentTest1(string arg)
      {
          arg0 = arg;
      }
  }
  ```

- 触发示例：
  ```csharp
  using UnityEngine;

  public class EventCenterUse_Trigger : MonoBehaviour
  {
      public float triggerInterval = 1f;
      private float _nextPrintTime = 0;

      private void Update()
      {
          if (Time.time >= _nextPrintTime)
          {
              _nextPrintTime = Time.time + triggerInterval;
              EventCenter.Instance.EventTrigger(new EnentTest1($"触发测试 {_nextPrintTime}")); // 触发事件
          }
      }
  }
  ```

## 安装与使用

### 安装

1. 克隆仓库：
   ```bash
   git clone https://gitee.com/he-jinxian/upanda-framework.git
   ```

2. 将 `Runtime` 和 `Editor` 文件夹复制到 Unity 项目的 `Assets` 目录下。

3. 在 Unity 编辑器中，点击菜单栏 `UPandaGF -> 工具 -> 初始化框架` 来初始化框架。

### 使用示例

- **日志系统**
  - 使用 `PLoger.Log("日志信息")` 输出日志。
  - 可通过 `UPandaGF -> 日志系统 -> 启用日志 / 剔除日志` 控制日志输出。

- **资源管理**
  - 使用 `IAssetsLoader` 接口加载资源：
    ```csharp
    IAssetsLoader loader = UPGameRoot.Instance.GetAssetsLoader();
    loader.LoadAsync<GameObject>("Assets/Resources/Prefabs/MyPrefab.prefab", obj => {
        Instantiate(obj);
    });
    ```

- **事件系统**
  - 注册事件监听器并触发事件，如上文所示。

## 贡献

欢迎提交 Pull Request 或 Issue 来帮助改进框架。

## 许可证

本项目使用 MIT 许可证。
```