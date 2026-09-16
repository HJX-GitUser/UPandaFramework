// =============================================================================
//  HowUseMonoMgr.cs —— PublicMono 用法示例（纯文档性质，不参与任何业务逻辑，可整段删除）
// -----------------------------------------------------------------------------
//  为什么示例留在运行时程序集（而不是挪到 Editor）：
//      下面那个 SampleBehaviour 是可以直接挂到物体上试跑的；若把文件挪进 Editor 程序集，
//      挂到场景后打包会出现 "missing script"。本文件体积很小且完全惰性，保留更安全。
//
//  对应 PublicMono.cs 头部注释的六条约定，示例里逐条演示：
//      ① 注册 / 注销必须成对，且必须是"同一个方法引用"（lambda 注销不掉）
//      ② 建议带 owner（MonoBehaviour 传 this）→ 对象销毁后自动注销，杜绝僵尸监听者
//      ③ 同一方法重复注册会被忽略（不会每帧跑两遍）
//      ④ 某个监听器抛异常不会连累同一帧的其它监听器
//      ⑤ RunCoroutine 的 Task 一定会结束：完成 / 异常 / 取消 / 超时
//      ⑥ ⚠ Unity 对象不要用 "?. " 判空（它绕过 Unity 重载的 ==，对象已销毁时照样会调用）
// =============================================================================

using System;
using System.Collections;
using System.Threading;
using UnityEngine;

/// <summary>
/// 用法示例（普通 C# 对象版）：非 MonoBehaviour 的类如何接入帧更新与协程。
/// </summary>
public class HowUseMonoMgr
{
    private bool enabledMyUpdate;

    /// <summary>让 MyUpdate 每帧被调用（强引用注册：必须自己注销）</summary>
    public void EnableMyUpdate()
    {
        if (enabledMyUpdate) return;

        PublicMono mono = GetPublicMono();
        if (mono == null) return;

        mono.AddUpdateListener(MyUpdate);      // 注册的是命名方法 —— 这样才能注销掉
        enabledMyUpdate = true;
    }

    /// <summary>注销帧更新（必须与注册时是同一个方法引用）</summary>
    public void DisableMyUpdate()
    {
        if (!enabledMyUpdate) return;

        PublicMono mono = GetPublicMono();
        if (mono == null) return;

        mono.RemoveUpdateListener(MyUpdate);
        enabledMyUpdate = false;
    }

    private void MyUpdate()
    {
        // 每帧逻辑
    }

    /// <summary>FixedUpdate / LateUpdate 用法完全一样（注意用修正后的名字）</summary>
    public void EnableFixedUpdate()
    {
        PublicMono mono = GetPublicMono();
        if (mono != null) mono.AddFixedUpdateListener(MyFixedUpdate);
    }

    public void DisableFixedUpdate()
    {
        PublicMono mono = GetPublicMono();
        if (mono != null) mono.RemoveFixedUpdateListener(MyFixedUpdate);
    }

    private void MyFixedUpdate()
    {
        // 物理相关逻辑
    }

    /// <summary>启动协程但不等它（Fire and forget）</summary>
    public void StartMyIEnumerator()
    {
        PublicMono mono = GetPublicMono();
        if (mono != null) mono.StartCoroutine(SelfIEnumerator());
    }

    private IEnumerator SelfIEnumerator()
    {
        Debug.Log("开启协程");
        yield return new WaitForSeconds(3f);
        Debug.Log("协程结束");
    }

    /// <summary>
    /// 等待协程跑完：用 RunCoroutine + 超时。
    /// 协程出错 / 超时 / 宿主被销毁都会以异常或取消的形式回到这里，不会永久挂起。
    /// </summary>
    public async void RunMyCoroutineSafely()
    {
        PublicMono mono = GetPublicMono();
        if (mono == null) return;

        try
        {
            await mono.RunCoroutine(SelfIEnumerator(), 5f);      // 5 秒超时
            Debug.Log("协程正常结束");
        }
        catch (TimeoutException)
        {
            Debug.LogError("协程超时了");
        }
        catch (OperationCanceledException)
        {
            Debug.LogWarning("协程被取消（宿主被停用/销毁，或外部取消）");
        }
        catch (Exception e)
        {
            Debug.LogError($"协程内部出错：{e}");
        }
    }

    private CancellationTokenSource cancelSource;

    /// <summary>可取消的协程</summary>
    public async void StartCancelableCoroutine()
    {
        PublicMono mono = GetPublicMono();
        if (mono == null) return;

        cancelSource = new CancellationTokenSource();
        try
        {
            await mono.RunCoroutine(SelfIEnumerator(), cancelSource.Token);
        }
        catch (OperationCanceledException)
        {
            Debug.Log("协程已取消");
        }
        finally
        {
            cancelSource.Dispose();
            cancelSource = null;
        }
    }

    public void CancelMyCoroutine()
    {
        if (cancelSource != null) cancelSource.Cancel();
    }

    /// <summary>
    /// ⚠️ 取单例必须用 Unity 风格的判空，不要写 <c>PublicMono.Instance?.Xxx()</c>：
    /// <c>?.</c> 走的是 CLR 的真 null 判断，绕过 Unity 重载的 <c>==</c>，
    /// 对象"已被销毁但引用还在"时会照样调用进去（然后报 MissingReferenceException）。
    /// 另外退出播放时 Instance 返回 null，也必须判空。
    /// </summary>
    private static PublicMono GetPublicMono()
    {
        PublicMono mono = PublicMono.Instance;
        if (mono == null)
        {
            Debug.LogWarning("[PublicMono 示例] PublicMono 不可用（可能正在退出播放）");
        }
        return mono;
    }
}

/// <summary>
/// 用法示例（MonoBehaviour 版）：可以直接挂到物体上运行。
/// 演示"带 owner 注册"——对象销毁后回调自动注销，不必担心漏写注销。
/// </summary>
public class PublicMonoSampleBehaviour : MonoBehaviour
{
    private bool registered;

    private void OnEnable()
    {
        PublicMono mono = PublicMono.Instance;
        if (mono == null) return;

        // 第二个参数传 this：本组件被销毁后，PublicMono 会自动跳过并移除这条监听（无需手动注销）
        mono.AddUpdateListener(OnPublicUpdate, this);
        registered = true;
    }

    private void OnDisable()
    {
        // 虽然带 owner 会自动清理，但"成对注销"仍然是最佳实践：
        // 否则组件被禁用期间仍占着监听位（下次 OnEnable 会因查重被忽略，从而"看起来没生效"）
        if (!registered) return;

        PublicMono mono = PublicMono.Instance;
        if (mono != null) mono.RemoveUpdateListener(OnPublicUpdate);
        registered = false;
    }

    private void OnPublicUpdate()
    {
        // 这里的每帧逻辑由 PublicMono 统一驱动
    }
}
