# UPandaFramework
Unity的前端框架
## 目录结构介绍
---

### **Editor**
以下工具在Tools菜单目录里使用
#### AssetBundleTools 
**AB包管理工具**
1. 可以给AB包生成MD5码并生成对比文件
2. 上传AB包到指定资源服务器

#### ExcelDll 
**读取Excel的插件**

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
##### BaseSingleton
单例基类 懒汉模式
##### BaseMonoSingleton
继承了MonoBehavior的单例基类
过场景会移除,用的时候生成,过场景建议手动删除
这个可以也自己手动管理挂载到场景上
##### BaseMonoSingletonAuto
自动生成到场景的单例基类，需要时直接用。
继承这种单例模式可以让其一直存在在场景中


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
事件需要先在EEventDefine的枚举里定义好
```C#
/// <summary>
/// 事件中心的事件定义枚举
/// 所有的事件要在这里定义出来
/// </summary>
public enum EEventDefine
{
    /// <summary>
    /// 场景管理模块，场景进度
    /// </summary>
    SceneMgr_SceneAsynLoadProgress

}
```
监听和触发：
```C#
//监听
EventCenter.Instance.AddEventListener(EEventDefine.SceneMgr_SceneAsynLoadProgress,()=>{//执行的事件})
//触发
EventCenter.Instance.EventTrigger(EEventDefine.SceneMgr_SceneAsynLoadProgress, ao.progress);
```

