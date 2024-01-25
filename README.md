# PandaFramework
Unity的前端框架
## 目录结构介绍
---

### **Editor**
以下工具在Tools菜单目录里使用
#### AssetBundleTools
AB包管理工具，
1. 可以给AB包生成MD5码并生成对比文件
2. 上传AB包到指定资源服务器

#### ExcelDll
读取Excel的插件
#### ExcelTool
Excel工具
1. 根据Excel配置的数据生成数据类和数据容器类
2. 把Excel里的数据生成二进制文件放入StreamingAssets里，用BinaryDataMgr类得到相关数据

#### CreatDirectory
项目目录生成工具
1. 3rd 第三方插件和资源目录
2. ArtAssets 艺术资源目录
3. Resources
4. Scene
5. StreamingAssets
6. Scripts
7. AssetBundle

---
### **Extend**
代码扩展


---
### **Manager**
开发工具
