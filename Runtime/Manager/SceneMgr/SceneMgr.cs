using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UPandaGF;

/// <summary>场景服务。异步等待通过 PublicMono 托管，失败/取消均有明确 Task 终态。</summary>
public class SceneMgr : LazySingletonBase<SceneMgr>
{
    private ISceneService service;
    public void Configure(ISceneService value)
    {
        if (service != null) service.Progress -= OnProgress;
        service = value;
        if (service != null) service.Progress += OnProgress;
    }
    private void OnProgress(SceneTransitionProgress progress)
    {
        if (progress.Phase == SceneTransitionPhase.Loading || progress.Phase == SceneTransitionPhase.Completed)
            EventCenter.Instance.EventTrigger(new SceneMgr_SceneAsynLoadProgress(progress.Progress, progress.RequestId, progress.Scene, progress.Phase));
    }
    private void EnsureNoManagedTransition()
    {
        if (service is SceneService scenes && scenes.HasPendingRequests)
            throw new InvalidOperationException("已有受管理的场景操作；请使用 ISceneService 排队。");
    }
    public void LoadScene(string sceneName, UnityAction Callback = null) { LoadScene(sceneName, LoadSceneMode.Single, Callback); }
    public void LoadScene(string sceneName, LoadSceneMode loadSceneMode, UnityAction Callback = null)
    { EnsureNoManagedTransition(); SceneManager.LoadScene(sceneName, loadSceneMode); Callback?.Invoke(); }

    // 保留 Asyn 拼写及全部既有重载；旧回调仅在成功时调用，失败由 PublicMono 记录，取消静默结束。
    public void LoadSceneAsyn(string sceneName, UnityAction Callback = null) { LoadSceneAsyn(sceneName, LoadSceneMode.Single, Callback); }
    public void LoadSceneAsyn(string sceneName, LoadSceneMode loadSceneMode, UnityAction Callback = null)
    { _ = Deliver(LoadSceneAsync(sceneName, loadSceneMode), Callback); }
    public Task LoadSceneAsyn(string sceneName) { return LoadSceneAsync(sceneName); }
    public Task LoadSceneAsyn(string sceneName, LoadSceneMode loadSceneMode) { return LoadSceneAsync(sceneName, loadSceneMode); }

    /// <summary>取消/超时终止本次等待及进度通知，不能撤销已提交给 Unity 的原生场景切换。</summary>
    public Task LoadSceneAsync(string sceneName, LoadSceneMode loadSceneMode = LoadSceneMode.Single,
        CancellationToken cancellationToken = default, float timeoutSeconds = 0f)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled(cancellationToken);
        if (service != null) return service.LoadAsync(sceneName, loadSceneMode, cancellationToken, timeoutSeconds);
        var mono = PublicMono.Instance;
        if (mono == null) return Task.FromCanceled(new CancellationToken(true));
        return mono.RunCoroutine(ReallyLoadSceneAsyn(sceneName, loadSceneMode), timeoutSeconds, cancellationToken);
    }
    private static async Task Deliver(Task task, UnityAction callback)
    {
        try { await task; }
        catch (OperationCanceledException) { return; }
        catch (Exception) { return; } // PublicMono 已记录原始失败，避免重复报错。
        if (callback == null) return;
        foreach (UnityAction action in callback.GetInvocationList())
            try { action(); } catch (Exception e) { Debug.LogException(e); }
    }
    private static IEnumerator ReallyLoadSceneAsyn(string sceneName, LoadSceneMode mode)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) throw new ArgumentException("场景名称不能为空", nameof(sceneName));
        if (!Application.CanStreamedLevelBeLoaded(sceneName)) throw new ArgumentException("场景不在 Build Settings 或已加载的 AB 中：" + sceneName, nameof(sceneName));
        var operation = SceneManager.LoadSceneAsync(sceneName, mode);
        if (operation == null) throw new InvalidOperationException("Unity 未创建场景加载操作：" + sceneName);
        while (!operation.isDone)
        {
            EventCenter.Instance.EventTrigger(new SceneMgr_SceneAsynLoadProgress(operation.progress));
            yield return null;
        }
        EventCenter.Instance.EventTrigger(new SceneMgr_SceneAsynLoadProgress(operation.progress));
    }
    public string GetActiveScene() { return SceneManager.GetActiveScene().name; }
    public AsyncOperation UnloadSceneAsync(string sceneName) { EnsureNoManagedTransition(); return SceneManager.UnloadSceneAsync(sceneName); }
    public AsyncOperation UnloadSceneAsync(int sceneIndex) { EnsureNoManagedTransition(); return SceneManager.UnloadSceneAsync(sceneIndex); }
}
public class SceneMgr_SceneAsynLoadProgress : EventArgBase
{
    public float progress { get; private set; }
    public long RequestId { get; }
    public string SceneName { get; }
    public SceneTransitionPhase Phase { get; }
    public SceneMgr_SceneAsynLoadProgress(float value, long requestId, string scene, SceneTransitionPhase phase)
    { progress = value; RequestId = requestId; SceneName = scene; Phase = phase; }
    public SceneMgr_SceneAsynLoadProgress(float progress) { this.progress = progress; }
}
