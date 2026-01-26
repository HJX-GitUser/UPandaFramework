using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.VisualScripting.TextureAssets;
using UnityEngine;
using UnityEngine.Events;

namespace UPandaGF
{
    /// <summary>
    /// 对象池中的容器类，把对象都分好类，方便看
    /// </summary>
    public class PoolData
    {
        public GameObject fatherObj;//容器的父对象
        public Stack<GameObject> poolList;//容器

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="obj">需要存入的对象</param>
        /// <param name="poolObj">对象池</param>
        public PoolData(GameObject obj, GameObject poolObj)
        {
            if (PoolMgr.isOpenLayout && poolObj != null)
            {
                fatherObj = new GameObject(obj.name + "_F");
                fatherObj.transform.SetParent(poolObj.transform);
            }
            poolList = new Stack<GameObject>();
            PushObj(obj);
        }

        /// <summary>
        /// 往容器里放对象
        /// </summary>
        /// <param name="obj"></param>
        public void PushObj(GameObject obj)
        {
            obj.SetActive(false);
            poolList.Push(obj);
            if (PoolMgr.isOpenLayout)
                obj.transform.SetParent(fatherObj.transform);
        }

        /// <summary>
        /// 从容器里取出对象
        /// </summary>
        /// <returns></returns>
        public GameObject GetObj()
        {
            GameObject obj = null;
            obj = poolList.Pop();
            obj.SetActive(true);
            if (PoolMgr.isOpenLayout)
                obj.transform.parent = null;
            return obj;
        }
    }

    /// <summary>
    /// 对象池模块
    /// </summary>
    public class PoolMgr : LazySingletonBase<PoolMgr>
    {
        /// <summary>
        /// 对象池容器
        /// </summary>
        public Dictionary<string, PoolData> poolDic = new Dictionary<string, PoolData>();
        /// <summary>
        /// 对象池Obj
        /// </summary>
        private GameObject poolObj;
        /// <summary>
        /// 对象在Hierarchy中放回对象池后是否按层级结构存放
        /// 建议打包的时候设为false，可以节约一点性能
        /// </summary>
        public static bool isOpenLayout = true;


        private ResourcesLoader resourcesLoader;
        private IAssetsLoader assetsLoader;

        protected override void OnInit()
        {
            PLogger.Log("PoolMgr Init!");
            resourcesLoader = ResourcesLoader.Instance;
            assetsLoader = UPGameRoot.Instance.GetAssetsLoader();
        }

        /// <summary>
        /// 异步取对象 回调返回
        /// </summary>
        /// <param name="path">路径【Resources路径下不需要后缀，AssetBundle需要后缀】</param>
        /// <param name="callback"></param>
        /// <param name="loadMethod">对象加载方式</param>
        public void GetObjAsync(string path, UnityAction<GameObject> callback, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            if (poolDic.ContainsKey(path) && poolDic[path].poolList.Count > 0)
            {
                callback(poolDic[path].GetObj());
            }
            else
            {
                switch (loadMethod)
                {
                    case AssetLoadMethod.Resources:
                        ResourcesLoader.Instance.LoadAsync<GameObject>(path, (obj) =>
                        {
                            obj.name = path;
                            GameObject result = GameObject.Instantiate(obj);
                            callback(result);
                        });
                        break;
                    case AssetLoadMethod.AssetBundle:
                        assetsLoader.LoadAsync<GameObject>(path, (obj) =>
                        {
                            obj.name = path;
                            GameObject result = GameObject.Instantiate(obj);
                            callback(result);
                        });
                        break;
                }
            }
        }

        /// <summary>
        /// 异步取对象
        /// </summary>
        /// <param name="path">路径【Resources路径下不需要后缀，AssetBundle需要后缀】</param>
        /// <param name="callback"></param>
        /// <param name="loadMethod">对象加载方式</param>
        public async Task<GameObject> GetObjAsync(string path, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            GameObject result = null;
            if (poolDic.ContainsKey(path) && poolDic[path].poolList.Count > 0)
            {
                result = poolDic[path].GetObj();
            }
            else
            {
                GameObject obj = await LoadObjAsync(path, loadMethod);
                if (obj == null)
                {
                    PLogger.LogError($"加载失败！\npath:{path}\nAssetLoadMethod:{loadMethod}");
                }
                result = GameObject.Instantiate(obj);
            }
            return result;
        }


        public GameObject GetObj(string path, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            GameObject obj = null;
            if (poolDic.ContainsKey(path) && poolDic[path].poolList.Count > 0)
            {
                obj = poolDic[path].GetObj();
            }
            else
            {
                if (loadMethod == AssetLoadMethod.AssetBundle)
                {
                    PLogger.LogError("使用AssetBundle加载不支持同步方式获取，请使用异步方法加载:【GetObjAsync(path,AssetLoadMethod.AssetBundle);】");
                    return null;
                }
                obj = GameObject.Instantiate(Resources.Load<GameObject>(path));
            }

            return obj;
        }

        /// <summary>
        /// 加载对象
        /// </summary>
        /// <param name="path">路径【Resources路径下不需要后缀，AssetBundle需要后缀】</param>
        /// <param name="loadMethod">对象加载方式</param>
        /// <returns></returns>
        private async Task<GameObject> LoadObjAsync(string path, AssetLoadMethod loadMethod = AssetLoadMethod.Resources)
        {
            GameObject obj = null;
            switch (loadMethod)
            {
                case AssetLoadMethod.Resources:
                    bool isLoaded = false;
                    resourcesLoader.LoadAsync<GameObject>(path, (asset) =>
                    {
                        obj = asset;
                        isLoaded = true;
                    });
                    while (isLoaded == false)
                    {
                        await Task.Yield();
                    }
                    break;
                case AssetLoadMethod.AssetBundle:
                    obj = await assetsLoader.LoadAsync<GameObject>(path);
                    break;
            }
            return obj;
        }

        /// <summary>
        /// 往对象池放对象
        /// </summary>
        /// <param name="name">对象池内容器的名字</param>
        /// <param name="obj"></param>
        public void PushObj(string name, GameObject obj)
        {
            if(obj == null)
            {
                PLogger.LogError($"{name} 对象为null");
                return;
            }
            if (poolObj == null && isOpenLayout) poolObj = new GameObject("Pool");
            //对象池里有该容器
            if (poolDic.ContainsKey(name))
            {
                poolDic[name].PushObj(obj);
            }
            //对象池里没有有该容器
            else
            {
                poolDic.Add(name, new PoolData(obj, poolObj));
            }
        }

        /// <summary>
        /// 清空缓存池的方法 
        /// 主要用在 场景切换时
        /// </summary>
        public void Clear()
        {
            poolDic.Clear();
            poolObj = null;
        }
    }
}

