# UPandaFramework
Unity的前端框架
## 目录结构介绍
---
#### AssetBundleTools 
**AB包管理工具**
UPandaGF->AB包工具->Assetbudle Broswer

#### ExcelTool 
**Excel工具**
1. 根据Excel配置的数据生成数据类和数据容器类
2. 把Excel里的数据生成二进制文件放入StreamingAssets里，用BinaryDataMgr类得到相关数据

#### CreatDirectory 项目目录生成工具

1. 3rd 第三方插件和资源目录
2. ArtAssets 艺术资源目录
3. Resources
4. Scene
5. StreamingAssets
6. Scripts
7. AssetBundle

---
### **Extend**代码扩展

---

### **Manager**开发工具

---

#### Singleton
##### LazySingletonBase
单例基类 懒汉模式
##### EagerSingletonBase
单例基类 饿汉模式
##### LazyMonoSingletonBase
继承MonoBehaviour的单例基类，懒汉模式
##### EagerMonoSingletonBase
继承MonoBehaviour的单例基类，饿汉模式


#### PublicMono
**公共Mono模块**
1. 利用帧更新或定时更新处理逻辑
2. 利用协同程序处理逻辑
3. 可以统一执行管理帧更新或定时更新相关逻辑

不继承MonoBehaviour的脚本也能利用该工具实现以上逻辑

```C#
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
/// <summary>
/// 使用方式
/// </summary>
public class HowUseMonoMgr
{
    /// <summary>
    /// 调用该方法就可以让MyUpdate()方法在每帧调用
    /// </summary>
    public void EnableMyUpdate()
    {
        MonoMgr.Instance.AddUpdateListener(MyUpdate);
    }
    public void DisableMyUpdate()
    {
        MonoMgr.Instance.RemoveUpdateListener(MyUpdate);
    }

    private void MyUpdate()
    {
        Debug.Log("更新");
    }
    /// <summary>
    /// 开启协程的方式
    /// </summary>
    public void StartMyIEnumerator()
    {
        MonoMgr.Instance.StartCoroutine(SelfIEnumerator());
    }

    private IEnumerator SelfIEnumerator()
    {
        Debug.Log("开启协程");
        yield return new WaitForSeconds(3f);
        Debug.Log("协程结束");
    }
}

```

#### ObjPool 
**对象池**

对象池还需要优化，目前提供基础功能
使用前对象需要做成预制体放进Resources里
```C#
//对象在Hierarchy中放回对象池后是否按层级结构存放
//建议打包的时候设为false，可以节约一点性能
//默认是true
PoolMgr.isOpenLayout;

PoolMgr.Instance.GetObj(string name)//可以得到对象。
//name是在Resources里的路径，也被用作对象的标识，存的时候也必须是一样的
PoolMgr.Instance.PushObj(string name, GameObject obj)//把对象放入对象池。
```

#### EventCenterModule
**事件中心模块**
示例场景：EventCenterUse

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

public class EnentTest1 : EventArgBase//声名事件
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

