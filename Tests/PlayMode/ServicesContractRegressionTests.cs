using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UPandaGF;
using UPandaGF.Gameplay.Tasks;

namespace UPandaGF.Tests
{

[Serializable]
public class ServicesContractPayload
{
    public int value;
}

/// <summary>
/// 服务契约回归：IHttpService / IPoolService / ILogService / QuestManager 注入存档。
/// 与 P1RootCompositionTests 互补：这里直接装配单个服务，验证契约行为与失败路径。
/// </summary>
public class ServicesContractRegressionTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private GameObject Go(string name) { var go = new GameObject(name); objects.Add(go); return go; }

    [TearDown] public void Cleanup()
    {
        foreach (var go in objects) if (go != null) UnityEngine.Object.DestroyImmediate(go);
        objects.Clear();
        foreach (var loader in TestObjectSearch.All<ResourcesLoader>()) UnityEngine.Object.DestroyImmediate(loader.gameObject);
    }

    private static IEnumerator Wait(Task task, float seconds = 10f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
        Assert.IsTrue(task.IsCompleted, "Probe timeout");
    }

    private sealed class FakeServices : IGameServices
    {
        public IAssetService Assets { get; set; }
        public ISaveService Saves { get; set; }
        public ILogService Log { get; set; }
        public IPoolService Pools { get; set; }
        public IHttpService Http { get; set; }
        public IAudioService Audio => null;
        public ISceneService Scenes => null;
        public IInputService Input => null;
        public IPauseService Pause => null;
        public IGameClock Clock => null;
        public ITimerService Timer => null;
        public IConfigService Config => null;
        public IGameFlowService Flow => null;
        public IAppLifecycle AppLifecycle => null;
    }

    private sealed class InMemorySaves : ISaveService
    {
        private readonly Dictionary<string, object> slots = new Dictionary<string, object>();
        public void Save<T>(string slot, T data) where T : class { slots[slot] = data; }
        public T Load<T>(string slot) where T : class
        {
            object value;
            return slots.TryGetValue(slot, out value) ? value as T : null;
        }
        public bool Exists(string slot) { return slots.ContainsKey(slot); }
        public bool Delete(string slot) { return slots.Remove(slot); }
    }

    private sealed class CountingReceiver : IRewardReceiver
    {
        public int Experience, Gold;
        public void GrantExperience(int amount) { Experience += amount; }
        public void GrantGold(int amount) { Gold += amount; }
        public void GrantItem(string itemId, int amount) { }
    }

    // ============================ ILogService ============================

    [Test] public void LogServiceHonoursRuntimeSwitches()
    {
        LogConfig previous = PLogger.cfg;
        try
        {
            PLogger.cfg = new LogConfig
            {
                openLog = true,
                openWarning = true,
                openError = true,
                addHeadFix = false,
                openTime = false,
                showThreadID = false
            };
            var log = new LogService();
            Assert.IsTrue(log.IsEnabled(LogLevel.Info));
            LogAssert.Expect(LogType.Log, "contract-info");
            log.Info("contract-info");
            LogAssert.Expect(LogType.Warning, "contract-warning");
            log.Warning("contract-warning");
            LogAssert.Expect(LogType.Error, "contract-error");
            log.Error("contract-error");

            PLogger.cfg.openLog = false;
            Assert.IsFalse(log.IsEnabled(LogLevel.Info));
            log.Info("contract-suppressed");   // 不应产生任何日志
        }
        finally { PLogger.cfg = previous; }
    }

    // ============================ IPoolService ============================

    [UnityTest] public IEnumerator PoolWithoutInjectedAssetsFailsClosed()
    {
        var pool = new GameObjectPoolMgr();
        LogAssert.Expect(LogType.Exception, new Regex("对象池未注入资源服务"));
        var task = pool.GetObjAsync("Missing/Path");
        yield return Wait(task);
        Assert.IsNull(task.Result);
        Assert.AreEqual(0, pool.TotalPooledCount);
    }

    [UnityTest] public IEnumerator ScopedPoolReturnsLeaseAndReleasesOnScopeDispose()
    {
        var scope = new LifetimeScope("ContractPool");
        var service = new PoolService(new AssetService(ResourcesLoader.Instance, null));
        var pool = service.CreatePool(scope);
        var load = pool.GetObjAsync("UI/Canvas");
        yield return Wait(load);
        Assert.IsNotNull(load.Result);
        Assert.AreEqual(0, pool.TotalPooledCount);   // 借出的实例不算空闲
        scope.Dispose();
        yield return null;
        Assert.IsTrue(load.Result == null);   // 作用域释放时销毁借出实例
        Assert.AreEqual(0, pool.TotalPooledCount);
        service.Dispose();
    }

    // ============================ IHttpService ============================

    [UnityTest] public IEnumerator HttpServiceReturnsSuccessFailureAndSupportsCancellation()
    {
        var server = new CoordinationHttpServer();
        var http = Go("ContractHttp").AddComponent<HttpManager>();
        try
        {
            var ok = http.GetAsync<ServicesContractPayload>(server.Url + "/ok", null, new HttpRequestOptions { RetryCount = 0 });
            yield return Wait(ok);
            Assert.IsTrue(ok.Result.Success, ok.Result.Error);
            Assert.AreEqual(200, ok.Result.StatusCode);
            Assert.AreEqual(1, ok.Result.Data.value);

            var failed = http.GetAsync<ServicesContractPayload>(server.Url + "/fail", null, new HttpRequestOptions { RetryCount = 0 });
            LogAssert.Expect(LogType.Error, new Regex("503"));
            yield return Wait(failed);
            Assert.IsFalse(failed.Result.Success);
            StringAssert.Contains("503", failed.Result.Error);

            var cancellation = new CancellationTokenSource();
            var slow = http.GetAsync<ServicesContractPayload>(server.Url + "/slow", null, new HttpRequestOptions { RetryCount = 0 }, cancellation.Token);
            yield return null;
            cancellation.Cancel();
            yield return Wait(slow);
            Assert.IsTrue(slow.IsCanceled);
            cancellation.Dispose();
        }
        finally
        {
            server.Dispose();
        }
    }

    // ============================ QuestManager 注入 ============================

    [Test] public void QuestManagerUsesInjectedSaveServiceAndRewardReceiver()
    {
        GameObject go = Go("ContractQuest");
        go.SetActive(false);
        var manager = go.AddComponent<QuestManager>();
        manager.initializeOnAwake = false;
        manager.DefinitionLoader = () => Array.Empty<QuestDefinition>();
        manager.autoLoadSaveOnAwake = false;
        go.SetActive(true);

        var saves = new InMemorySaves();
        var receiver = new CountingReceiver();
        manager.Configure(new FakeServices { Saves = saves, Log = LogService.Default });
        manager.RewardReceiver = receiver;

        var definition = ScriptableObject.CreateInstance<QuestDefinition>();
        try
        {
            definition.questId = "contract-quest";
            definition.reward = new QuestReward { gold = 7, experience = 3 };
            manager.GrantRewards(new QuestInstance(definition));
            Assert.AreEqual(7, receiver.Gold);
            Assert.AreEqual(3, receiver.Experience);

            Assert.IsTrue(manager.SaveToSlot());
            Assert.IsTrue(saves.Exists(QuestManager.DefaultSaveSlot));
            Assert.IsTrue(manager.LoadFromSlot());
            Assert.IsTrue(manager.DeleteSave());
            Assert.IsFalse(saves.Exists(QuestManager.DefaultSaveSlot));
        }
        finally { UnityEngine.Object.DestroyImmediate(definition); }
    }

    [Test] public void QuestManagerWithoutRewardReceiverDoesNotGrantOrThrow()
    {
        GameObject go = Go("ContractQuestNoReceiver");
        go.SetActive(false);
        var manager = go.AddComponent<QuestManager>();
        manager.initializeOnAwake = false;
        manager.DefinitionLoader = () => Array.Empty<QuestDefinition>();
        go.SetActive(true);

        var definition = ScriptableObject.CreateInstance<QuestDefinition>();
        try
        {
            definition.questId = "contract-quest-2";
            definition.reward = new QuestReward { gold = 5 };
            Assert.DoesNotThrow(() => manager.GrantRewards(new QuestInstance(definition)));
        }
        finally { UnityEngine.Object.DestroyImmediate(definition); }
    }
}

}