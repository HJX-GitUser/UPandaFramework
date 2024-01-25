# PandaFramework
Unity的前端框架
## 目录结构介绍
---

### **Editor**
以下工具在Tools菜单目录里使用
#### AssetBundleTools AB包管理工具
1. 可以给AB包生成MD5码并生成对比文件
2. 上传AB包到指定资源服务器

#### ExcelDll 读取Excel的插件

#### ExcelTool Excel工具
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
1. 利用帧更新或定时更新处理逻辑
2. 利用协同程序处理逻辑
3. 可以统一执行管理帧更新或定时更新相关逻辑

不继承MonoBehaviour的脚本也能利用该工具实现以上逻辑

