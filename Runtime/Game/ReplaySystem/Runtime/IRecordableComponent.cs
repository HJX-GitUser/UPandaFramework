using UnityEngine;

namespace ReplaySystem
{
    /// <summary>
    /// 可录制组件扩展接口：为其他组件（如 Rigidbody velocity、ParticleSystem time、
    /// AudioSource time 等）扩展录制/回放能力。
    /// 未来新增支持时，实现该接口并调用 ReplayManager.RegisterRecordable() 注册即可。
    /// </summary>
    public interface IRecordableComponent
    {
        /// <summary>扩展唯一键，用于在 ObjectSnapshot.componentData 中存取数据</summary>
        string Key { get; }

        /// <summary>该物体是否可被本扩展处理（例如是否挂有 Rigidbody）</summary>
        bool CanHandle(GameObject go);

        /// <summary>录制一帧：捕获组件当前状态（返回值会被装箱，需注意 GC）</summary>
        object Capture(GameObject go);

        /// <summary>回放一帧：把捕获到的状态恢复到组件</summary>
        void Apply(GameObject go, object state);
    }
}
