using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UPandaGF
{
    /// <summary>一次设置变更的事件数据。</summary>
    public readonly struct SettingsChange
    {
        public readonly string Key;
        public readonly SettingsCategory Category;
        public readonly object OldValue;
        public readonly object NewValue;
        /// <summary>是否已写入平台（NextLaunch/RestartRequired 为 false）。</summary>
        public readonly bool Applied;
        /// <summary>是否处于预览事务中（尚未提交）。</summary>
        public readonly bool Preview;

        public SettingsChange(string key, SettingsCategory category, object oldValue, object newValue, bool applied, bool preview)
        {
            Key = key;
            Category = category;
            OldValue = oldValue;
            NewValue = newValue;
            Applied = applied;
            Preview = preview;
        }

        public override string ToString()
        {
            return Key + ": " + (OldValue == null ? "null" : OldValue.ToString()) + " -> " + (NewValue == null ? "null" : NewValue.ToString());
        }
    }

    /// <summary>某个设置域应用失败的事件数据。</summary>
    public readonly struct SettingsApplyFailure
    {
        public readonly string Applier;
        public readonly string Message;

        public SettingsApplyFailure(string applier, string message)
        {
            Applier = applier;
            Message = message;
        }

        public override string ToString() { return Applier + ": " + Message; }
    }

    /// <summary>
    /// 玩家系统设置服务：加载/校验/应用/保存/恢复默认，描述表驱动，平台应用器可替换。
    /// <para>数据与存档分离（删档不清设置）；默认路径 <c>persistentDataPath/Settings/settings.json</c>。</para>
    /// <para>主线程使用：Get/Set/Reset/Describe 与事件都在主线程；设置文件很小（&lt; 64KB），
    /// <see cref="LoadAsync"/> 同步读取并返回已完成的 Task，避免主线程等待后台线程造成死锁。</para>
    /// <para>预览事务：<see cref="SettingsApplyMode.PreviewConfirm"/> 项在 <see cref="Set{T}"/> 后立即生效，
    /// 调用 <see cref="CommitAsync"/> 保留，<see cref="Rollback"/> 或超时自动回滚。</para>
    /// </summary>
    public interface ISettingsService
    {
        /// <summary>是否已完成加载（加载失败也算完成，此时使用默认值）。</summary>
        bool IsLoaded { get; }
        /// <summary>是否有预览事务待确认。</summary>
        bool IsPreviewing { get; }
        /// <summary>预览自动回滚秒数；默认 15，0 表示不自动回滚。</summary>
        float PreviewSeconds { get; set; }

        /// <summary>已应用/已保存的值（只读使用；不要直接修改字段）。</summary>
        SettingsData Current { get; }
        /// <summary>预览中的草稿值；没有预览事务时与 <see cref="Current"/> 相同。</summary>
        SettingsData Pending { get; }

        /// <summary>加载完成（含失败后回退默认值）。</summary>
        event Action Loaded;
        /// <summary>加载失败（已回退默认值）；参数为错误信息。</summary>
        event Action<string> LoadFailed;
        /// <summary>单项变更；预览期间也会触发（Preview = true）。</summary>
        event Action<SettingsChange> Changed;
        /// <summary>保存成功。</summary>
        event Action<SettingsSaveReason> Saved;
        /// <summary>保存失败；参数为错误信息。</summary>
        event Action<string> SaveFailed;
        /// <summary>某个应用器失败（已隔离，不影响其它设置域）。</summary>
        event Action<SettingsApplyFailure> ApplyFailed;
        /// <summary>预览事务开始/结束。</summary>
        event Action<bool> PreviewStateChanged;

        /// <summary>读取设置文件并归一化；重复调用无副作用。失败时使用默认值并触发 LoadFailed。</summary>
        Task LoadAsync(CancellationToken token = default);
        /// <summary>把引导期设置应用到平台（音频/画质/显示/帧率/输入绑定）；应在首场景加载前调用。</summary>
        Task ApplyBootAsync(CancellationToken token = default);

        /// <summary>按 key 取强类型值；key 未注册抛 KeyNotFoundException。</summary>
        T Get<T>(string key);
        /// <summary>按 key 取强类型值；key 未注册或类型不符返回 false。</summary>
        bool TryGet<T>(string key, out T value);

        /// <summary>
        /// 修改设置：归一化 → 校验可用性 → 应用 → 标脏保存。
        /// <para>返回 false 表示 key 不存在/不可用/类型不符；值未变化返回 true。
        /// mode 默认取描述表的 ApplyMode。</para>
        /// </summary>
        bool Set<T>(string key, T value, SettingsApplyMode? mode = null);

        /// <summary>提交预览事务（并把脏值落盘）。没有预览事务时等价于保存。</summary>
        Task<bool> CommitAsync(CancellationToken token = default);
        /// <summary>放弃预览事务并回滚到预览前的值；没有预览事务时无操作。</summary>
        void Rollback();

        /// <summary>立即保存（同步写小文件）；PlayMode/退出/切后台时调用。</summary>
        Task SaveAsync(SettingsSaveReason reason, CancellationToken token = default);

        /// <summary>恢复默认：Item=单个 key；Category=分区名（枚举名，大小写不敏感）；All=全部。</summary>
        void Reset(SettingsScope scope, string categoryOrKey = null);

        /// <summary>当前平台/依赖是否可用；不可用时 reason 为玩家可读原因。</summary>
        bool IsAvailable(string key, out string reason);

        /// <summary>返回描述表快照（按分区/Order 排序）；category 为 null 表示全部。</summary>
        IReadOnlyList<SettingDescriptor> Describe(SettingsCategory? category = null);
    }
}
