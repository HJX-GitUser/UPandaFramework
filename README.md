# UPandaFramework

Unity的前端框架

## 模块介绍

### 日志系统

1.对Debug进行了封装，可以剔除日志，避免运行过程中的字符串拼接照成的性能损耗。
UPandaGF->启动日志/剔除日志
2.可配置日志输出到本地
3.整合了LogView，可以在项目出包后开启日志面板查看日志输出，调试信息等。

### 资源管理

1.AB包打包工具
基于官方的Assetbudle Broswer进行扩展，新增资源上传页签，创建资源对比文件进行热更新
UPandaGF->AB包工具->Assetbudle Broswer

2.IAssetsLoader
AB包加载接口
IAssetsLoader loader = UPGameRoot.Instance.GetAssetsLoader()
直接用编辑器里的路径加载资源，开发过程中UPGameRoot的Inspector面板可以切换为Edior模式加载资源。打包时再切换为AssetBundle模式

3.ResourcesLoader
封装Unity内置的Resources.Load方法，避免资源重复加载，增加引用计数和异步加载状态管理

4.StreamingAssetsLoadTool
StreamingAssets路径下的资源加载工具，处理好了跨平台兼容性，提供多种加载方式

### 数据表

#### ExcelTool

**Excel工具**

1.  根据Excel配置的数据生成数据类和数据容器类
2.  把Excel里的数据生成二进制文件放入StreamingAssets里，用BinaryDataMgr类得到相关数据

### 事件系统

#### EventCenterModule

**事件中心**

监听、移除：

```C#
using UnityEngine;

public class EnentCenterUse_AddListener : MonoBehaviour
{

    private void OnEnable()
    {
        EventCenter.Instance.AddEventListener<EnentTest1>(TestEvent1);//事件注册
        EventCenter.Instance.AddEventListener<EnentTest1>(TestEvent2);
    }

    private void OnDisable()
    {
        EventCenter.Instance.RemoveEventListener<EnentTest1>(TestEvent1);//事件注销
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

public class EnentTest1 : EventArgBase//声名事件对象
{
    public override int EventID => typeof(EnentTest1).GetHashCode();
    public string arg0 { get;private set; }
    public EnentTest1(string arg)
    {
        arg0 = arg;
    }
}



```

触发示例：

```C#
using UnityEngine;

public class EnentCenterUse_Trigger : MonoBehaviour
{
    public float triggerInterval = 1f;
    private float _nextPrintTime = 0;
    private void Update()
    {
        if (Time.time >= _nextPrintTime)
        {
            _nextPrintTime = Time.time + triggerInterval;
            EventCenter.Instance.EventTrigger(new EnentTest1($"触发测试 {_nextPrintTime}"));//触发事件
        }
    }
}
```

