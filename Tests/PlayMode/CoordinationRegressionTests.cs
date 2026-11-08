using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UPandaGF;

namespace UPandaGF.Tests
{
using Object = UnityEngine.Object;

[PrebuildSetup(typeof(CoordinationFixtures))]
[PostBuildCleanup(typeof(CoordinationFixtures))]
public class CoordinationRegressionTests
{
    private readonly List<GameObject> objects = new List<GameObject>();
    private CoordinationHttpServer server;
    private float previousScale;
    private LogConfig previousConfig;
    [SetUp] public void Setup()
    {
        previousScale=Time.timeScale; previousConfig=PLogger.cfg; PLogger.cfg=new LogConfig();
        server=new CoordinationHttpServer();
    }
    [UnityTearDown] public IEnumerator Cleanup()
    {
        Time.timeScale=previousScale;
        foreach(var go in objects) if(go!=null) Object.DestroyImmediate(go);
        objects.Clear();
        foreach(var mono in TestObjectSearch.All<PublicMono>()) Object.DestroyImmediate(mono.gameObject);
        server.Dispose(); PLogger.cfg=previousConfig;
        // A canceled observer cannot undo Unity's native additive load. Drain and unload it.
        for(int i=0;i<4;i++) yield return null;
        var scene=SceneManager.GetSceneByPath(CoordinationFixtures.ScenePath);
        if(scene.IsValid()&&scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
    }
    private T Component<T>() where T:Component
    {
        var go=new GameObject("Coordination_"+typeof(T).Name); objects.Add(go); return go.AddComponent<T>();
    }
    private static IEnumerator Until(Func<bool> predicate, float timeout=2f)
    {
        float end=Time.realtimeSinceStartup+timeout;
        while(!predicate()&&Time.realtimeSinceStartup<end) yield return null;
        Assert.IsTrue(predicate(),"Operation did not reach its expected terminal state");
    }
    private GenericHttpPack<string> Pack(string path="/ok") => new GenericHttpPack<string>{Url=server.Url+path,RetryCount=0};
    private static IEnumerator Never(Action cleanup=null)
    {
        try { while(true) yield return null; } finally { cleanup?.Invoke(); }
    }
    private static IEnumerator NestedFailure() { yield return ChildFailure(); }
    private static IEnumerator ChildFailure() { yield return null; throw new InvalidOperationException("coordination nested failure"); }
    private static IEnumerator Effect(Action effect) { effect(); yield return null; }

    [Test] public void RequestHeadersOverrideGlobalDefaults()
    {
        var manager=Component<HttpManager>(); manager.SetGlobalHeader("Authorization","global");
        var pack=manager.CreateRequest<string>(HttpRequestType.Get,server.Url+"/ok"); pack.Headers["Authorization"]="local";
        Assert.IsTrue(manager.Send(pack)); Assert.AreEqual("local",pack.Headers["Authorization"]);
    }
    [Test] public void SamePackCannotBeQueuedTwice()
    {
        var client=Component<HttpClient>(); var pack=Pack(); client.SendRequest(pack); client.SendRequest(pack);
        var queue=(System.Collections.ICollection)typeof(HttpClient).GetField("_sendQueue",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(client);
        Assert.AreEqual(1,queue.Count);
    }
    [UnityTest] public IEnumerator DestroyCancelsQueuedAndRunningHttpRequests()
    {
        var client=Component<HttpClient>(); var running=Pack("/slow"); var queued=Pack(); int callbacks=0;
        running.OnSuccess=(s,c)=>callbacks++; queued.OnSuccess=(s,c)=>callbacks++;
        client.SendRequest(running); client.SendRequest(queued); yield return Until(()=>running.AttemptCount==1);
        Object.DestroyImmediate(client.gameObject);
        Assert.IsTrue(running.IsCanceled); Assert.IsTrue(queued.IsCanceled);
        Assert.IsNull(running.WebRequest); Assert.IsNull(queued.WebRequest);
        yield return new WaitForSecondsRealtime(.2f); Assert.AreEqual(0,callbacks);
    }
    [UnityTest] public IEnumerator SuccessCallbackExceptionDoesNotTriggerFailure()
    {
        var client=Component<HttpClient>(); var pack=Pack(); int successes=0,failures=0;
        pack.OnSuccess=(s,c)=>{successes++;throw new InvalidOperationException("coordination success failure");};
        pack.OnFailure=e=>failures++;
        LogAssert.Expect(LogType.Exception,new Regex("coordination success failure"));
        client.SendRequest(pack); yield return Until(()=>pack.AttemptCount>0&&pack.WebRequest==null);
        Assert.AreEqual(1,successes); Assert.AreEqual(0,failures);
    }
    [UnityTest] public IEnumerator ThrowingFailureCallbackIsInvokedOnlyOnceAndQueueContinues()
    {
        var client=Component<HttpClient>(); var failed=Pack("/fail"); var next=Pack(); int failures=0,successes=0;
        failed.OnFailure=e=>{failures++;throw new InvalidOperationException("coordination failure callback");}; next.OnSuccess=(s,c)=>successes++;
        // LogError 不受 OPEN_PLOG 编译剔除，断言必须无条件注册，否则无宏的测试工程会把错误当未处理日志。
        LogAssert.Expect(LogType.Error,new Regex("请求失败（重试已用尽）"));
        LogAssert.Expect(LogType.Exception,new Regex("coordination failure callback"));
        client.SendRequest(failed); client.SendRequest(next); yield return Until(()=>successes==1);
        Assert.AreEqual(1,failures);
    }
    [UnityTest] public IEnumerator RetryCreationFailureDoesNotBlockFollowingRequest()
    {
        var client=Component<HttpClient>(); var failed=new CoordinationCountingPack{Url=server.Url+"/fail",RetryCount=1,throwOnSecond=true};
        var next=Pack(); int failures=0,successes=0; failed.OnFailure=e=>failures++;next.OnSuccess=(s,c)=>successes++;
        LogAssert.Expect(LogType.Error,new Regex("coordination retry create failure"));
        client.SendRequest(failed);client.SendRequest(next);yield return Until(()=>successes==1&&failures==1);
        Assert.AreEqual(1,failures);Assert.IsNull(failed.WebRequest);
    }
    [UnityTest] public IEnumerator RetryCancellationDoesNotRecreateCanceledRequest()
    {
        var client=Component<HttpClient>();var pack=new CoordinationCountingPack{Url=server.Url+"/fail",RetryCount=1};
        pack.OnRetry=(n,e)=>client.CancelAll(); client.SendRequest(pack);
        yield return Until(()=>pack.IsCanceled&&pack.WebRequest==null); Assert.AreEqual(1,pack.creates);
    }
    [UnityTest] public IEnumerator RetryDelayUsesRealtimeWhenGameIsPaused()
    {
        var client=Component<HttpClient>();var pack=Pack("/fail");pack.RetryCount=1;pack.RetryDelay=.05f;int failures=0;
        pack.OnRetry=(n,e)=>Time.timeScale=0;pack.OnFailure=e=>failures++;
        LogAssert.Expect(LogType.Error,new Regex("请求失败（重试已用尽）"));
        client.SendRequest(pack);yield return Until(()=>failures==1,.8f);Assert.AreEqual(2,pack.AttemptCount);
    }
    [Serializable] private class Payload { public int value; }
    [UnityTest] public IEnumerator JsonSuccessListenersAreIsolatedAndStateIsCommittedBeforeCallbacks()
    {
        var client=Component<HttpClient>();var pack=new GenericHttpPack<Payload>{Url=server.Url+"/ok",RetryCount=0};int hits=0,failures=0;
        pack.OnSuccess=(data,code)=>{Assert.IsTrue(pack.IsCompleted);Assert.AreEqual(1,data.value);throw new InvalidOperationException("typed success failure");};
        pack.OnSuccess+=(data,code)=>hits++;pack.OnFailure=e=>failures++;
        LogAssert.Expect(LogType.Exception,new Regex("typed success failure"));client.SendRequest(pack);
        yield return Until(()=>pack.IsCompleted);Assert.AreEqual(1,hits);Assert.AreEqual(0,failures);
    }
    [UnityTest] public IEnumerator ThrowingRetryListenerDoesNotBlockOtherListenersOrRetry()
    {
        var client=Component<HttpClient>();var pack=Pack("/fail");pack.RetryCount=1;pack.RetryDelay=0;int retries=0,successes=0;
        pack.OnRetry=(n,e)=>throw new InvalidOperationException("retry listener failure");
        pack.OnRetry+=(n,e)=>{retries++;pack.Url=server.Url+"/ok";};pack.OnSuccess=(data,code)=>successes++;
        LogAssert.Expect(LogType.Exception,new Regex("retry listener failure"));client.SendRequest(pack);
        yield return Until(()=>pack.IsCompleted);Assert.AreEqual(1,retries);Assert.AreEqual(1,successes);Assert.AreEqual(2,pack.AttemptCount);
    }
    [UnityTest] public IEnumerator CancellationInsideProgressStopsDeliveryAndAllowsFreshRequests()
    {
        var client=Component<HttpClient>();var pack=Pack("/slow");int callbacks=0;
        pack.OnProgress=p=>client.CancelAll();pack.OnSuccess=(s,c)=>callbacks++;pack.OnFailure=e=>callbacks++;
        client.SendRequest(pack);yield return Until(()=>pack.IsCompleted);Assert.IsTrue(pack.IsCanceled);Assert.IsNull(pack.WebRequest);
        var next=Pack();next.OnSuccess=(s,c)=>callbacks++;client.SendRequest(next);yield return Until(()=>next.IsCompleted);Assert.AreEqual(1,callbacks);
    }
    [UnityTest] public IEnumerator DisabledClientCancelsOldBatchAndCanServeAfterReenable()
    {
        var client=Component<HttpClient>();var first=Pack("/slow");client.SendRequest(first);yield return Until(()=>first.AttemptCount==1);
        client.enabled=false;Assert.IsTrue(first.IsCanceled&&first.IsCompleted);Assert.IsNull(first.WebRequest);
        client.enabled=true;var next=Pack();client.SendRequest(next);yield return Until(()=>next.IsCompleted);Assert.IsFalse(next.IsCanceled);
    }
    [UnityTest] public IEnumerator CompletedPackCannotBeSentAgain()
    {
        var manager=Component<HttpManager>();var pack=manager.CreateRequest<string>(HttpRequestType.Get,server.Url+"/ok");pack.RetryCount=0;
        Assert.IsTrue(manager.Send(pack));yield return Until(()=>pack.IsCompleted);Assert.IsFalse(manager.Send(pack));Assert.AreEqual(1,pack.AttemptCount);
    }
    [UnityTest] public IEnumerator OrdinaryEventRemovalStillUsesCurrentSnapshot()
    {
        var center=new EventCenter();int hits=0;UnityAction<Message> second=e=>hits++;
        center.AddEventListener<Message>(e=>center.RemoveEventListener(second),10);center.AddEventListener(second);
        center.EventTrigger(new Message());center.EventTrigger(new Message());Assert.AreEqual(1,hits);
        var bus=new EventBus();var b=new CoordinationPlainListener();var a=new CoordinationPlainListener{action=()=>bus.Unsubscribe(b)};
        bus.Subscribe(a);bus.Subscribe(b);bus.Dispatch(new CoordinationMessage());bus.Dispatch(new CoordinationMessage());Assert.AreEqual(1,b.hits);
        yield break;
    }
    [Test] public void ClearAndRegisterNewEventListenerDoesNotReviveOldBatch()
    {
        var center=new EventCenter();int hits=0;
        center.AddEventListener<Message>(e=>{center.Clear();center.AddEventListener<Message>(v=>hits++);},10);
        center.AddEventListener<Message>(e=>Assert.Fail("Old batch resumed"));center.EventTrigger(new Message());Assert.AreEqual(0,hits);
        center.EventTrigger(new Message());Assert.AreEqual(1,hits);
    }
    [Test] public void FrameRemovalAfterReRegistrationDoesNotRemoveTheOldTombstoneTwice()
    {
        var mono=Component<PublicMono>();int hits=0;UnityAction second=()=>hits++;
        UnityAction first=()=>{mono.RemoveUpdateListener(second);mono.AddUpdateListener(second);mono.RemoveUpdateListener(second);};
        mono.AddUpdateListener(first);mono.AddUpdateListener(second);mono.SendMessage("Update");mono.RemoveUpdateListener(first);mono.SendMessage("Update");
        Assert.AreEqual(0,hits);Assert.AreEqual(0,mono.UpdateListenerCount);
    }
    [Test] public void NestedFrameDispatchCanClearListenersWithoutInvalidatingOuterIteration()
    {
        var mono=Component<PublicMono>();bool nested=false;int hits=0;
        mono.AddUpdateListener(()=>{if(!nested){nested=true;mono.SendMessage("Update");}else mono.ClearAllListeners();});
        mono.AddUpdateListener(()=>hits++);mono.SendMessage("Update");Assert.AreEqual(0,hits);Assert.AreEqual(0,mono.UpdateListenerCount);
    }
    [UnityTest] public IEnumerator CancelContinuationMayDisableSchedulerWithoutCorruptingJobIteration()
    {
        var mono=Component<PublicMono>();var token=new CancellationTokenSource();var first=mono.RunCoroutine(Never());var second=mono.RunCoroutine(Never(),token.Token);
        var continuation=second.ContinueWith(t=>mono.gameObject.SetActive(false),TaskContinuationOptions.ExecuteSynchronously);token.Cancel();
        yield return Until(()=>first.IsCompleted&&second.IsCompleted&&continuation.IsCompleted);
        Assert.IsTrue(first.IsCanceled&&second.IsCanceled);Assert.IsFalse(continuation.IsFaulted);Assert.AreEqual(0,mono.RunningCoroutineCount);token.Dispose();
    }
    [Test] public void StopBatchRejectsReentrantJobsButAllowsLaterUse()
    {
        var mono=Component<PublicMono>();int hits=0;Task reentrant=null;var first=mono.RunCoroutine(Never());
        var continuation=first.ContinueWith(t=>reentrant=mono.RunCoroutine(Effect(()=>hits++)),TaskContinuationOptions.ExecuteSynchronously);
        mono.StopAllCoroutines();Assert.IsTrue(continuation.IsCompleted);Assert.IsTrue(reentrant.IsCanceled);Assert.AreEqual(0,hits);
        var fresh=mono.RunCoroutine(Effect(()=>hits++));Assert.AreEqual(1,hits);mono.StopAllCoroutines();Assert.IsTrue(fresh.IsCanceled);
    }
    [UnityTest] public IEnumerator WorkerCancellationCleansUpOnUnityThread()
    {
        var mono=Component<PublicMono>();var token=new CancellationTokenSource();int main=Thread.CurrentThread.ManagedThreadId,cleanupThread=0;
        var task=mono.RunCoroutine(Never(()=>cleanupThread=Thread.CurrentThread.ManagedThreadId),token.Token);
        var worker=Task.Run(()=>token.Cancel());yield return Until(()=>worker.IsCompleted&&task.IsCompleted);
        Assert.IsTrue(task.IsCanceled);Assert.AreEqual(main,cleanupThread);token.Dispose();
    }
    [UnityTest] public IEnumerator TimeoutRunsFinallyDuringGamePause()
    {
        var mono=Component<PublicMono>();bool cleaned=false;Time.timeScale=0;
        LogAssert.Expect(LogType.Error,new Regex("协程超时"));var task=mono.RunCoroutine(Never(()=>cleaned=true),.03f);
        yield return Until(()=>task.IsCompleted);Assert.IsTrue(task.IsFaulted);Assert.IsInstanceOf<TimeoutException>(task.Exception.InnerException);Assert.IsTrue(cleaned);
    }
    [UnityTest] public IEnumerator NestedCoroutineNormalCompletionRunsInnerAndOuterFinally()
    {
        var mono=Component<PublicMono>();var order=new List<int>();var task=mono.RunCoroutine(Outer(order));
        yield return Until(()=>task.IsCompleted);Assert.IsFalse(task.IsFaulted||task.IsCanceled);CollectionAssert.AreEqual(new[]{1,2,3},order);
    }
    private static IEnumerator Outer(List<int> order) { try{yield return Inner(order);order.Add(2);}finally{order.Add(3);} }
    private static IEnumerator Inner(List<int> order) {try{yield return new WaitForSecondsRealtime(.01f);}finally{order.Add(1);} }
    [UnityTest] public IEnumerator ValidSceneTaskCompletesAndThrowingProgressListenerDoesNotBlockIt()
    {
        var service=new SceneService(new UnitySceneBackend());
        Action<SceneTransitionProgress> listener=e=>throw new InvalidOperationException("scene progress listener failure");
        // Throw only once to avoid an expectation for every native frame.
        Action<SceneTransitionProgress> once=null;once=e=>{service.Progress-=once;listener(e);};
        LogAssert.Expect(LogType.Exception,new Regex("scene progress listener failure"));
        service.Progress+=once;
        try{var task=service.LoadAsync(CoordinationFixtures.ScenePath,LoadSceneMode.Additive);yield return Until(()=>task.IsCompleted);Assert.IsFalse(task.IsCanceled||task.IsFaulted);Assert.IsTrue(SceneManager.GetSceneByPath(CoordinationFixtures.ScenePath).isLoaded);}
        finally{service.Progress-=once;_=service.ShutdownAsync();}
    }
    [UnityTest] public IEnumerator SceneCallbackIsDeliveredOnceAndIsolatesMulticastExceptions()
    {
        var loader=Component<AssetsLoader>();
        var service=new SceneService(new UnitySceneBackend(),loader);
        int hits=0;UnityAction callback=()=>{hits++;throw new InvalidOperationException("scene success callback failure");};callback+=()=>hits++;
        LogAssert.Expect(LogType.Exception,new Regex("scene success callback failure"));
        try
        {
            var init=loader.Init(AssetLoaddingMethod.Editor,"http://localhost/","Unused",null);yield return Until(()=>init.IsCompleted);
            loader.ConfigureScenes(service);
            loader.LoadSceneAsync(CoordinationFixtures.ScenePath,LoadSceneMode.Additive,()=>{},callback);
            yield return Until(()=>hits==2);
            yield return null;Assert.AreEqual(2,hits);
        }
        finally{_=service.ShutdownAsync();}
    }
    [UnityTest] public IEnumerator SceneCancellationStopsProgressAndDoesNotClaimNativeRollback()
    {
        var service=new SceneService(new UnitySceneBackend());
        var token=new CancellationTokenSource();int progress=0;Action<SceneTransitionProgress> listener=e=>{progress++;token.Cancel();};
        service.Progress+=listener;
        try
        {
            var task=service.LoadAsync(CoordinationFixtures.ScenePath,LoadSceneMode.Additive,token.Token);
            yield return Until(()=>task.IsCompleted);Assert.IsTrue(task.IsCanceled);int canceledAt=progress;
            yield return new WaitForSecondsRealtime(.1f);Assert.AreEqual(canceledAt,progress);
        }
        finally{service.Progress-=listener;token.Dispose();_=service.ShutdownAsync();}
    }
    [Test] public void CanceledBeforeSubmissionReleasesItsNativeRequest()
    {
        var manager=Component<HttpManager>();var pack=Pack();pack.CreateWebRequest();pack.Cancel();
        Assert.IsFalse(manager.Send(pack));Assert.IsNull(pack.WebRequest);Assert.IsTrue(pack.IsCompleted);Assert.AreEqual(0,pack.AttemptCount);
    }
    private sealed class ListenerPayload { public int hits;public void Receive(Message message){hits++;} }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference RegisterAndDestroyOwner(EventCenter center)
    {
        var owner=new GameObject("TransientOwner");var payload=new ListenerPayload();
        center.AddEventListener<Message>(owner,payload.Receive);center.EventTrigger(new Message());Object.DestroyImmediate(owner);
        Assert.AreEqual(0,center.GetListenerCount<Message>());return new WeakReference(payload);
    }
    [Test] public void PurgedOwnerDoesNotRemainStronglyHeldByCachedSnapshot()
    {
        var center=new EventCenter();var payload=RegisterAndDestroyOwner(center);
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Assert.IsFalse(payload.IsAlive);GC.KeepAlive(center);
    }
    [Test] public void HostDestroyedInsideRequestCreationCannotRepopulateItsQueue()
    {
        var manager=Component<HttpManager>();var pack=new CoordinationCountingPack{Url=server.Url+"/ok"};
        pack.beforeCreate=()=>Object.DestroyImmediate(manager.gameObject);
        Assert.IsFalse(manager.Send(pack));Assert.IsTrue(pack.IsCanceled&&pack.IsCompleted);Assert.IsNull(pack.WebRequest);
    }
    private class Message:EventArgBase{}
    [Test] public void OnceListenerSurvivesNestedDispatchWithoutDoubleInvocation()
    {
        var center=new EventCenter(); bool nested=false;int once=0;
        center.AddEventListener<Message>(e=>{if(!nested){nested=true;center.EventTrigger(new Message());}});
        center.AddEventListenerOnce<Message>(e=>once++);center.EventTrigger(new Message());Assert.AreEqual(1,once);
    }
    [Test] public void OwnerDestroyedEarlierInDispatchIsSkipped()
    {
        var center=new EventCenter();var owner=new GameObject("EventOwner");objects.Add(owner);int hits=0;
        center.AddEventListener<Message>(e=>Object.DestroyImmediate(owner),10);
        center.AddEventListener<Message>(owner,e=>hits++);center.EventTrigger(new Message());Assert.AreEqual(0,hits);
    }
    [Test] public void DestroyedOwnerCannotBecomeAnOwnerlessSubscription()
    {
        var center=new EventCenter();var owner=new GameObject("DeadOwner");Object.DestroyImmediate(owner);int hits=0;
        center.AddEventListener<Message>(owner,e=>hits++);center.EventTrigger(new Message());Assert.AreEqual(0,hits);
    }
    [Test] public void ClearStopsRemainingEventCenterBatch()
    {
        var center=new EventCenter();int hits=0;
        center.AddEventListener<Message>(e=>center.Clear(),10);center.AddEventListener<Message>(e=>hits++);
        center.EventTrigger(new Message());Assert.AreEqual(0,hits);
    }
    [Test] public void DestroyedUnityEventBusSubscriberIsRemoved()
    {
        var bus=new EventBus();var listener=Component<CoordinationBusListener>();bus.Subscribe(listener);
        Object.DestroyImmediate(listener.gameObject);bus.Dispatch(new CoordinationMessage());
        Assert.AreEqual(0,listener.hits);Assert.AreEqual(0,bus.GetSubscriptionCount<CoordinationMessage>());
    }
    [Test] public void ClearStopsRemainingEventBusBatch()
    {
        var bus=new EventBus();var first=new CoordinationPlainListener{action=()=>bus.Clear()};var second=new CoordinationPlainListener();
        bus.Subscribe(first);bus.Subscribe(second);bus.Dispatch(new CoordinationMessage());Assert.AreEqual(0,second.hits);
    }
    [UnityTest] public IEnumerator NestedCoroutineFailureFaultsItsTask()
    {
        var mono=Component<PublicMono>();
        LogAssert.Expect(LogType.Error,new Regex("coordination nested failure"));
        var task=mono.RunCoroutine(NestedFailure());yield return Until(()=>task.IsCompleted,.3f);Assert.IsTrue(task.IsFaulted);var observed=task.Exception;
    }
    [UnityTest] public IEnumerator CancelDisposesNestedIteratorsAndRunsFinally()
    {
        var mono=Component<PublicMono>();bool cleaned=false;var token=new CancellationTokenSource();
        var task=mono.RunCoroutine(NestedNever(()=>cleaned=true),token.Token);token.Cancel();
        yield return Until(()=>task.IsCompleted);Assert.IsTrue(task.IsCanceled);Assert.IsTrue(cleaned);token.Dispose();
    }
    private static IEnumerator NestedNever(Action cleanup) { yield return Never(cleanup); }
    [UnityTest] public IEnumerator PreCanceledCoroutineDoesNotExecuteUserCode()
    {
        var mono=Component<PublicMono>();int hits=0;var token=new CancellationTokenSource();token.Cancel();
        var task=mono.RunCoroutine(Effect(()=>hits++),token.Token);yield return Until(()=>task.IsCompleted);
        Assert.IsTrue(task.IsCanceled);Assert.AreEqual(0,hits);token.Dispose();
    }
    [Test] public void InactiveHostRejectsCoroutineWithoutDanglingTask()
    {
        var mono=Component<PublicMono>();mono.gameObject.SetActive(false);
        var task=mono.RunCoroutine(Never());Assert.IsTrue(task.IsCompleted);Assert.IsTrue(task.IsCanceled||task.IsFaulted);
    }
    [UnityTest] public IEnumerator StopAllCoroutinesAlsoCompletesManagedTasks()
    {
        var mono=Component<PublicMono>();var task=mono.RunCoroutine(Never());mono.StopAllCoroutines();
        yield return Until(()=>task.IsCompleted,.3f);Assert.IsTrue(task.IsCanceled);Assert.AreEqual(0,mono.RunningCoroutineCount);
    }
    [UnityTest] public IEnumerator DisableDuringFirstMoveCannotLeaveARegisteredJob()
    {
        var mono=Component<PublicMono>();var task=mono.RunCoroutine(Effect(()=>mono.gameObject.SetActive(false)));
        yield return Until(()=>task.IsCompleted,.3f);Assert.IsTrue(task.IsCanceled);Assert.AreEqual(0,mono.RunningCoroutineCount);
    }
    [UnityTest] public IEnumerator InvalidSceneFaultsTaskInsteadOfHanging()
    {
        var service=new SceneService(new UnitySceneBackend());
        var task=service.LoadAsync("coordination_missing_scene_"+Guid.NewGuid().ToString("N"));
        yield return Until(()=>task.IsCompleted,.3f);
        Assert.IsTrue(task.IsFaulted);var observed=task.Exception;
        Assert.IsInstanceOf<ArgumentException>(observed.InnerException);
        _=service.ShutdownAsync();
    }
}

public struct CoordinationMessage{}
public class CoordinationBusListener:MonoBehaviour,IEventListener<CoordinationMessage>
{
    public int hits;public void OnEvent(CoordinationMessage message){hits++;}
}
public class CoordinationPlainListener:IEventListener<CoordinationMessage>
{
    public Action action;public int hits;public void OnEvent(CoordinationMessage message){hits++;action?.Invoke();}
}
public class CoordinationCountingPack:GenericHttpPack<string>
{
    public int creates;public bool throwOnSecond;public Action beforeCreate;
    public override void CreateWebRequest(){creates++;if(throwOnSecond&&creates==2)throw new InvalidOperationException("coordination retry create failure");beforeCreate?.Invoke();base.CreateWebRequest();}
}

}