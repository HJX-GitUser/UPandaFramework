using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UPandaGF;
using Object = UnityEngine.Object;

[PrebuildSetup(typeof(LifecycleFixtures))]
[PostBuildCleanup(typeof(LifecycleFixtures))]
public class LifecycleRegressionTests
{
    private string relative, directory;
    private readonly List<GameObject> objects = new List<GameObject>();
    private readonly List<AudioClip> clips = new List<AudioClip>();
    private readonly List<GameObjectPoolMgr> pools = new List<GameObjectPoolMgr>();
    private LifecycleFixtureConfig fixtures;
    private LifecycleBundleServer server;
    private readonly List<AssetBundle> externalBundles = new List<AssetBundle>();
    private string ResourceClip => fixtures.resourceKey + "/Clip";
    private string ResourcePrefab => fixtures.resourceKey + "/AudioPrefab";
    private static BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [OneTimeSetUp] public void Begin()
    {
        if (!Application.isEditor) Assert.Ignore("Native lifecycle fixtures currently target Windows Editor PlayMode.");
        fixtures = JsonUtility.FromJson<LifecycleFixtureConfig>(File.ReadAllText(LifecycleFixtureConfig.FilePath));
        server = new LifecycleBundleServer(fixtures.bundleDirectory);
    }
    [OneTimeTearDown] public void End() { server?.Dispose(); }
    [SetUp] public void SetUp()
    {
        relative = "UPandaLifecycleAudit/" + Guid.NewGuid().ToString("N");
        directory = Path.Combine(Application.persistentDataPath, relative);
        Directory.CreateDirectory(directory);
    }
    [TearDown] public void TearDown()
    {
        AudioMgr.Release();
        foreach (var pool in pools) pool.Dispose();
        foreach (var go in objects) if (go != null) Object.DestroyImmediate(go);
        foreach (var clip in clips) if (clip != null) Object.DestroyImmediate(clip);
        foreach (var bundle in externalBundles) if (bundle != null) bundle.Unload(true);
        externalBundles.Clear();
        var loaders = Object.FindObjectsByType<ABLoadMgr>(FindObjectsSortMode.None);
        foreach (var loader in loaders) Object.DestroyImmediate(loader.gameObject);
        foreach (var loader in Object.FindObjectsByType<ResourcesLoader>(FindObjectsSortMode.None)) Object.DestroyImmediate(loader.gameObject);
        foreach (var mono in Object.FindObjectsByType<PublicMono>(FindObjectsSortMode.None)) Object.DestroyImmediate(mono.gameObject);
        // Only the explicitly created random fixture directory may be removed.
        if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.Combine(Application.persistentDataPath,"UPandaLifecycleAudit")) + Path.DirectorySeparatorChar))
            Directory.Delete(directory, true);
        pools.Clear(); objects.Clear(); clips.Clear();
        string streaming = Path.Combine(Application.streamingAssetsPath, relative);
        if (Directory.Exists(streaming) && Path.GetFullPath(streaming).StartsWith(Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "UPandaLifecycleAudit")) + Path.DirectorySeparatorChar))
            Directory.Delete(streaming, true);
    }
    private static T Get<T>(object target, string field) { return (T)target.GetType().GetField(field, Private).GetValue(target); }
    private static void Set(object target, string field, object value) { target.GetType().GetField(field, Private).SetValue(target, value); }
    private GameObject Go(string name) { var go=new GameObject(name); objects.Add(go); return go; }
    private AudioClip Clip(string name) { var clip=AudioClip.Create(name,22050,1,22050,false); clips.Add(clip); return clip; }
    private GameObjectPoolMgr Pool(LifecycleDelayedAssets loader = null)
    {
        var pool=new GameObjectPoolMgr(); pools.Add(pool);
        if(loader!=null)Set(pool,"assetsLoader",loader);
        return pool;
    }
    private AudioMgr Audio(LifecycleDelayedAssets loader=null)
    {
        var audio=AudioMgr.Instance;
        if(loader!=null)Set(audio,"assetsLoader",loader);
        return audio;
    }
    private static IEnumerator Wait(Task task, float seconds=10)
    {
        float until=Time.realtimeSinceStartup+seconds;
        while(!task.IsCompleted&&Time.realtimeSinceStartup<until)yield return null;
        Assert.IsTrue(task.IsCompleted,"Probe timeout");
    }
    private IEnumerator NativeSetup(bool mixed=false)
    {
        string source=fixtures.bundleDirectory;
        foreach(string name in new[]{"AuditBundles","audit_target","audit_shared"})
            if(!mixed||name!="audit_shared")File.Copy(Path.Combine(source,name),Path.Combine(directory,name),true);
        if(mixed)
        {
            string streaming=Path.Combine(Application.streamingAssetsPath,relative); Directory.CreateDirectory(streaming);
            File.Copy(Path.Combine(source,"audit_shared"),Path.Combine(streaming,"audit_shared"),true);
        }
        var task=ABLoadMgr.Instance.Init(relative,"AuditBundles",ABLoadPath.PersistentDataPath);
        yield return Wait(task);
        Assert.IsFalse(task.IsFaulted);Assert.IsTrue(ABLoadMgr.Instance.IsInitialized,"Native AB baseline initialization failed");
        var references=new ABSourcesRelated();
        references.bundleInfo["audit_target"]=new AssetBundleLoadInfo{bundleName="audit_target",loadPath=ABLoadPath.PersistentDataPath};
        references.bundleInfo["audit_shared"]=new AssetBundleLoadInfo{bundleName="audit_shared",loadPath=mixed?ABLoadPath.StreamingAssetsPath:ABLoadPath.PersistentDataPath};
        ABLoadMgr.Instance.SetABSourcesRelated(references);
    }
    [UnityTest] public IEnumerator MixedDependencyLocationsLoadSuccessfully()
    {
        yield return NativeSetup(true);
        var manager=ABLoadMgr.Instance;
        string sharedPath=Path.Combine(Application.streamingAssetsPath,relative,"audit_shared");
        var baseline=AssetBundle.LoadFromFile(sharedPath);Assert.IsNotNull(baseline,"Dependency fixture is valid");baseline.Unload(true);
        var task=manager.GetAssetBundle("audit_target",ABLoadPath.PersistentDataPath);
        yield return Wait(task);Assert.IsFalse(task.IsFaulted);Assert.IsNotNull(task.Result);
    }
    [UnityTest] public IEnumerator TaskAndCallbackShareNativeInflightRequest()
    {
        yield return NativeSetup();var manager=ABLoadMgr.Instance;
        var task=manager.GetAssetBundle("audit_target",ABLoadPath.PersistentDataPath);
        bool called=false;AssetBundle callbackResult=null;
        manager.GetAssetBundle("audit_target",ABLoadPath.PersistentDataPath,b=>{called=true;callbackResult=b;});
        float until=Time.realtimeSinceStartup+10;
        while((!called||!task.IsCompleted)&&Time.realtimeSinceStartup<until)yield return null;
        Assert.IsTrue(called&&task.IsCompleted);
        bool taskOk=!task.IsFaulted&&task.Result!=null;
        Assert.IsTrue(taskOk); Assert.IsNotNull(callbackResult); Assert.AreSame(task.Result,callbackResult);
    }
    [UnityTest] public IEnumerator ClearInvalidatesRemoteTaskAndPreventsCacheWrite()
    {
        yield return NativeSetup();var manager=ABLoadMgr.Instance;
        manager.remoteURL=server.Url;manager.PathUrl="";
        var remoteRef=new ABSourcesRelated();remoteRef.bundleInfo["audit_shared"]=new AssetBundleLoadInfo{bundleName="audit_shared",loadPath=ABLoadPath.RemotePath};manager.SetABSourcesRelated(remoteRef);
        var task=manager.GetAssetBundle("audit_target",ABLoadPath.RemotePath);
        manager.ClearAB();
        yield return Wait(task);
        int cached=Get<Dictionary<string,AssetBundle>>(manager,"_loadedBundles").Count;
        Assert.AreEqual(0,cached);Assert.IsNull(task.Result);
        yield return new WaitForSecondsRealtime(.6f);
        Assert.AreEqual(0,Get<Dictionary<string,AssetBundle>>(manager,"_loadedBundles").Count);
        Assert.IsFalse(manager.IsInitialized);
    }
    [UnityTest] public IEnumerator DestroyedCallbackHostCompletesCallerExactlyOnce()
    {
        yield return NativeSetup();var manager=ABLoadMgr.Instance;
        manager.remoteURL=server.Url;manager.PathUrl="";
        var remoteRef=new ABSourcesRelated();remoteRef.bundleInfo["audit_shared"]=new AssetBundleLoadInfo{bundleName="audit_shared",loadPath=ABLoadPath.RemotePath};manager.SetABSourcesRelated(remoteRef);
        int calls=0;manager.GetAssetBundle("audit_target",ABLoadPath.RemotePath,b=>{calls++;Assert.IsNull(b);});
        Object.DestroyImmediate(manager.gameObject);
        yield return new WaitForSecondsRealtime(1.25f);
        Assert.AreEqual(1,calls);
    }
    [UnityTest] public IEnumerator ClearPreservesIndependentlyOwnedBundles()
    {
        yield return NativeSetup();var manager=ABLoadMgr.Instance;
        var external=AssetBundle.LoadFromFile(Path.Combine(fixtures.bundleDirectory,"audit_external"));Assert.IsNotNull(external);externalBundles.Add(external);
        manager.ClearAB();Assert.IsTrue(external!=null);
    }
    [UnityTest] public IEnumerator PoolReturnsResourcesCountAfterAllIdleInstancesAreCleared()
    {
        var pool=Pool(); var task=pool.GetObjAsync(ResourcePrefab);yield return Wait(task);
        Assert.IsNotNull(task.Result);pool.PushObj(ResourcePrefab,task.Result);
        var loader=ResourcesLoader.Instance;int before=loader.GetAssetsRefCount<GameObject>(ResourcePrefab);
        pool.Clear();yield return null;
        Assert.AreEqual(0,pool.TotalPooledCount);Assert.AreEqual(0,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));Assert.AreEqual(1,before);
    }
    [UnityTest] public IEnumerator DisposePreventsPendingPrewarmFromRecreatingOldPool()
    {
        var loader=new LifecycleDelayedAssets();var pool=Pool(loader);var prefab=Go("delayed prefab");
        var task=pool.Prewarm("fixture.prefab",2,AssetLoadMethod.AssetBundle);
        pool.Dispose();loader.Complete(prefab);yield return Wait(task);
        Assert.AreEqual(0,pool.TotalPooledCount);Assert.IsFalse(pool.Contains("fixture.prefab"));
    }
    [Test] public void ThrowingDespawnAllowsLaterReturn()
    {
        var pool=Pool();var go=Go("throwing pool item");var callback=go.AddComponent<LifecyclePoolable>();callback.throwOnDespawn=true;
        LogAssert.Expect(LogType.Exception,new Regex("Audit expected OnDespawn error"));pool.PushObj("key",go);callback.throwOnDespawn=false;pool.PushObj("key",go);
        Assert.AreEqual(1,pool.TotalPooledCount);Assert.IsTrue(Get<HashSet<GameObject>>(pool,"pooledObjects").Contains(go));
    }
    [UnityTest] public IEnumerator SharedClipReleaseCrossCheck()
    {
        var pool=Pool();var task=pool.GetObjAsync(ResourcePrefab);yield return Wait(task);
        Assert.IsNotNull(task.Result);objects.Add(task.Result);
        var source=task.Result.GetComponent<AudioSource>();var clip=source.clip;Assert.IsNotNull(clip);
        var audio=Audio();audio.PlayUniqueSound(ResourceClip);
        float until=Time.realtimeSinceStartup+5;while(Get<AudioSource>(audio,"uniqueSound").clip==null&&Time.realtimeSinceStartup<until)yield return null;
        Assert.AreSame(clip,Get<AudioSource>(audio,"uniqueSound").clip,"Both modules must share the native clip before release.");
        audio.StopUniqueSound();
        Assert.IsNotNull(source.clip);
        Assert.AreEqual(0,ResourcesLoader.Instance.GetAssetsRefCount<AudioClip>(ResourceClip));
        yield return null;yield return null;
        Assert.IsNotNull(source.clip);
    }
    [Test] public void LaterDirectBgmInvalidatesDelayedRequest()
    {
        var loader=new LifecycleDelayedAssets();var audio=Audio(loader);var direct=Clip("direct");var old=Clip("old delayed");
        audio.PlayBKMusic("old",AssetLoadMethod.AssetBundle,0);audio.PlayBKMusic(direct,0);loader.Complete(old);
        Assert.AreSame(direct,Get<AudioSource>(audio,"bgmCurrent").clip);
    }
    [Test] public void LaterDirectUniqueSoundInvalidatesDelayedRequest()
    {
        var loader=new LifecycleDelayedAssets();var audio=Audio(loader);var direct=Clip("direct");var old=Clip("old delayed");
        audio.PlayUniqueSound("old",AssetLoadMethod.AssetBundle);audio.PlayUniqueSound(direct);loader.Complete(old);
        Assert.AreSame(direct,Get<AudioSource>(audio,"uniqueSound").clip);
    }
    [Test] public void StopAllSoundCancelsPendingStackedSoundExactlyOnce()
    {
        var loader=new LifecycleDelayedAssets();var audio=Audio(loader);var clip=Clip("delayed");AudioSource received=null;int calls=0;
        audio.LoadSoundAndPlay("old",true,AssetLoadMethod.AssetBundle,s=>{calls++;received=s;});audio.StopAllSound();
        Assert.AreEqual(1,calls);Assert.IsNull(received);loader.Complete(clip);
        Assert.IsNull(received);Assert.AreEqual(1,calls);Assert.AreEqual(0,audio.PlayingSoundCount);
    }
    [UnityTest] public IEnumerator AudioRebindsToRecreatedPublicMono()
    {
        var audio=Audio();audio.PlayBKMusic(Clip("one"),0);
        Object.DestroyImmediate(PublicMono.Instance.gameObject);var mono=PublicMono.Instance;
        audio.PlayBKMusic(Clip("two"),1);
        Assert.AreEqual(1,mono.UpdateListenerCount);
        yield return null;yield return null;yield return null;
        Assert.Greater(Get<float>(audio,"bgmFadeElapsed"),0f);
    }
    [UnityTest] public IEnumerator BorrowedInstancesKeepOnePrefabReferenceUntilDestroyed()
    {
        var pool=Pool();var first=pool.GetObjAsync(ResourcePrefab);var second=pool.GetObjAsync(ResourcePrefab);
        yield return Wait(Task.WhenAll(first,second));Assert.IsNotNull(first.Result);Assert.IsNotNull(second.Result);Assert.AreNotSame(first.Result,second.Result);
        objects.Add(first.Result);objects.Add(second.Result);
        var loader=ResourcesLoader.Instance;Assert.AreEqual(1,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));
        pool.Clear();Assert.IsNotNull(first.Result.GetComponent<AudioSource>().clip);Assert.AreEqual(1,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));
        Object.Destroy(first.Result);yield return null;Assert.AreEqual(1,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));
        Object.Destroy(second.Result);yield return null;Assert.AreEqual(0,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));
    }
    [UnityTest] public IEnumerator OldInstanceReleaseCannotDecrementNewResourcesGeneration()
    {
        var pool=Pool();var old=pool.GetObjAsync(ResourcePrefab);yield return Wait(old);objects.Add(old.Result);
        pool.Clear();var loader=ResourcesLoader.Instance;loader.ClearDic();
        var fresh=pool.GetObjAsync(ResourcePrefab);yield return Wait(fresh);objects.Add(fresh.Result);
        Assert.AreEqual(1,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));
        Object.Destroy(old.Result);yield return null;
        Assert.AreEqual(1,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));
        pool.Clear();Object.Destroy(fresh.Result);yield return null;
        Assert.AreEqual(0,loader.GetAssetsRefCount<GameObject>(ResourcePrefab));
    }
    [UnityTest] public IEnumerator StaleBorrowerReturnIsDestroyedInsteadOfRepopulatingPool()
    {
        var pool=Pool();var task=pool.GetObjAsync(ResourcePrefab);yield return Wait(task);objects.Add(task.Result);
        pool.Clear();pool.PushObj(ResourcePrefab,task.Result);yield return null;
        Assert.IsTrue(task.Result==null);Assert.AreEqual(0,pool.TotalPooledCount);
        Assert.AreEqual(0,ResourcesLoader.Instance.GetAssetsRefCount<GameObject>(ResourcePrefab));
    }
    [UnityTest] public IEnumerator ThrowingSpawnDestroysFailedInstanceWithoutPoisoningPool()
    {
        var pool=Pool();var go=Go("spawn failure");var behavior=go.AddComponent<LifecyclePoolable>();pool.PushObj("key",go);behavior.throwOnSpawn=true;
        LogAssert.Expect(LogType.Exception,new Regex("Audit expected OnSpawn error"));
        var task=pool.GetObjAsync("key");yield return Wait(task);
        Assert.IsNull(task.Result);Assert.IsFalse(Get<HashSet<GameObject>>(pool,"pooledObjects").Contains(go));
        yield return null;Assert.IsTrue(go==null);Assert.AreEqual(0,pool.TotalPooledCount);
    }
    [UnityTest] public IEnumerator CallbackReentryDoesNotDuplicatePoolMembership()
    {
        var pool=Pool();var go=Go("reentrant");var behavior=go.AddComponent<LifecyclePoolable>();behavior.onDespawn=()=>pool.PushObj("key",go);
        pool.PushObj("key",go);Assert.AreEqual(1,pool.TotalPooledCount);
        var task=pool.GetObjAsync("key");yield return Wait(task);Assert.AreSame(go,task.Result);Assert.AreEqual(0,pool.TotalPooledCount);
    }
    [UnityTest] public IEnumerator PoolTimeoutDiscardsLateResultAndAllowsRetry()
    {
        var loader=new LifecycleDelayedAssets();var pool=Pool(loader);pool.loadTimeout=.02f;var prefab=Go("late prefab");
#if OPEN_PLOG
        LogAssert.Expect(LogType.Error,new Regex("对象池资源加载超时"));
#endif
        var task=pool.GetObjAsync("fixture",AssetLoadMethod.AssetBundle);yield return Wait(task);
        Assert.IsNull(task.Result);loader.Complete(prefab);yield return null;Assert.AreEqual(0,pool.TotalPooledCount);
        var retry=pool.GetObjAsync("fixture",AssetLoadMethod.AssetBundle);loader.Complete(prefab);yield return Wait(retry);
        Assert.IsNotNull(retry.Result);objects.Add(retry.Result);
    }
    [Test] public void SameBgmEarlyReturnStillCancelsOlderPendingIntent()
    {
        var loader=new LifecycleDelayedAssets();var audio=Audio(loader);var a=Clip("A");var b=Clip("B");
        audio.PlayBKMusic("A",AssetLoadMethod.AssetBundle,0);loader.Complete(a);
        audio.PlayBKMusic("B",AssetLoadMethod.AssetBundle,0);audio.PlayBKMusic("A",AssetLoadMethod.AssetBundle,0);loader.Complete(b);
        Assert.AreSame(a,Get<AudioSource>(audio,"bgmCurrent").clip);Assert.AreEqual("A",audio.CurrentBgmPath);
    }
    [Test] public void DisposeCompletesStackedWaiterAndLateResultCannotReachNewAudioInstance()
    {
        var loader=new LifecycleDelayedAssets();var audio=Audio(loader);int calls=0;AudioSource received=null;
        audio.LoadSoundAndPlay("pending",true,AssetLoadMethod.AssetBundle,source=>{calls++;received=source;});AudioMgr.Release();
        Assert.AreEqual(1,calls);Assert.IsNull(received);
        var next=Audio();next.PlayUniqueSound(Clip("new"));loader.Complete(Clip("old"));
        Assert.AreEqual(1,calls);Assert.AreEqual(0,next.PlayingSoundCount);Assert.AreEqual(0,audio.PlayingSoundCount);
    }
    [UnityTest] public IEnumerator OwnerlessFrameListenerSurvivesWhileDestroyedOwnerIsRemoved()
    {
        var mono=PublicMono.Instance;int strong=0,owned=0;var owner=Go("owner");
        mono.AddUpdateListener(()=>strong++);mono.AddUpdateListener(()=>owned++,owner);Object.DestroyImmediate(owner);
        yield return null;yield return null;yield return null;
        Assert.Greater(strong,0);Assert.AreEqual(0,owned);Assert.AreEqual(1,mono.UpdateListenerCount);
    }
    [UnityTest] public IEnumerator ClearDuringAssetLoadCompletesWaiterAndSupportsReinitialization()
    {
        yield return NativeSetup();var manager=ABLoadMgr.Instance;
        var bundle=manager.GetAssetBundle("audit_target",ABLoadPath.PersistentDataPath);yield return Wait(bundle);Assert.IsNotNull(bundle.Result);
        string name=bundle.Result.GetAllAssetNames()[0];var pending=manager.LoadResAsync<GameObject>("audit_target",name,ABLoadPath.PersistentDataPath);
        if(pending.IsCompleted)Assert.Ignore("Native fixture completed without an in-flight window");
        manager.ClearAB();yield return Wait(pending);Assert.IsNull(pending.Result);
        var init=manager.Init(relative,"AuditBundles",ABLoadPath.PersistentDataPath);yield return Wait(init);Assert.IsTrue(manager.IsInitialized);
        var fresh=manager.LoadResAsync<GameObject>("audit_target",name,ABLoadPath.PersistentDataPath);yield return Wait(fresh);Assert.IsNotNull(fresh.Result);
    }
    [UnityTest] public IEnumerator AssetsLoaderReadinessTracksClearAndCanceledInitialization()
    {
        yield return NativeSetup();var loader=Go("assets loader").AddComponent<AssetsLoader>();
        var refs=new ABSourcesRelated{mainBundleInfo=new AssetBundleLoadInfo{bundleName="AuditBundles",loadPath=ABLoadPath.PersistentDataPath}};
        refs.bundleInfo["audit_shared"]=new AssetBundleLoadInfo{bundleName="audit_shared",loadPath=ABLoadPath.PersistentDataPath};
        var init=loader.Init(AssetLoaddingMethod.Assetbundles,"",relative,refs);yield return Wait(init);Assert.IsTrue(loader.IsInitialized);
        ABLoadMgr.Instance.ClearAB();Assert.IsFalse(loader.IsInitialized);
        var retry=loader.Init(AssetLoaddingMethod.Assetbundles,"",relative,refs);loader.ClearAB();yield return Wait(retry);Assert.IsFalse(loader.IsInitialized);
    }

    [Test] public void StopCancellationCallbackMayStartANewUniqueRequest()
    {
        var loader=new LifecycleDelayedAssets();var audio=Audio(loader);int canceled=0;
        audio.LoadSoundAndPlay("old sfx",true,AssetLoadMethod.AssetBundle,source=>{
            Assert.IsNull(source);canceled++;audio.PlayUniqueSound("new unique",AssetLoadMethod.AssetBundle);
        });
        audio.StopAllSound();Assert.AreEqual(1,canceled);
        var clip=Clip("new request");loader.Complete(clip);
        Assert.AreSame(clip,Get<AudioSource>(audio,"uniqueSound").clip);
    }

    [UnityTest] public IEnumerator NewPoolCanRetireBorrowerFromDisposedOwner()
    {
        var old=Pool();var task=old.GetObjAsync(ResourcePrefab);yield return Wait(task);objects.Add(task.Result);
        old.Dispose();var next=Pool();next.PushObj(ResourcePrefab,task.Result);yield return null;
        Assert.IsTrue(task.Result==null);Assert.AreEqual(0,next.TotalPooledCount);
        Assert.AreEqual(0,ResourcesLoader.Instance.GetAssetsRefCount<GameObject>(ResourcePrefab));
    }

}
public class LifecyclePoolable : MonoBehaviour,IPoolable
{
    public bool throwOnDespawn,throwOnSpawn;
    public Action onDespawn;
    public void OnSpawn() { if(throwOnSpawn)throw new InvalidOperationException("Audit expected OnSpawn error"); }
    public void OnDespawn() { if(throwOnDespawn)throw new InvalidOperationException("Audit expected OnDespawn error"); onDespawn?.Invoke(); }
}
public class LifecycleDelayedAssets : IAssetsLoader
{
    private readonly List<Action<Object>> pending=new List<Action<Object>>();
    public void Complete(Object asset) { var list=pending.ToArray();pending.Clear();foreach(var callback in list)callback(asset); }
    public Task<T> LoadAsync<T>(string path)where T:Object{var tcs=new TaskCompletionSource<T>();pending.Add(o=>tcs.TrySetResult(o as T));return tcs.Task;}
    public void LoadAsync<T>(string path,UnityAction<T> callback)where T:Object{pending.Add(o=>callback(o as T));}
    public Task<Object> LoadAsync(string path,Type type){var tcs=new TaskCompletionSource<Object>();pending.Add(o=>tcs.TrySetResult(o));return tcs.Task;}
    public void LoadAsync(string path,Type type,UnityAction<Object> callback){pending.Add(o=>callback(o));}
    public void LoadSceneAsync(string path,UnityAction a=null,UnityAction b=null){throw new NotSupportedException();}
    public void LoadSceneAsync(string path,LoadSceneMode mode,UnityAction a=null,UnityAction b=null){throw new NotSupportedException();}
    public void LoadAssemblyAsync(string path,UnityAction<Assembly> callback){throw new NotSupportedException();}
    public Task<Assembly> LoadAssemblyAsync(string path){throw new NotSupportedException();}
    public bool UnLoadAB(string name){return true;}
    public void ClearAB(){}
}
