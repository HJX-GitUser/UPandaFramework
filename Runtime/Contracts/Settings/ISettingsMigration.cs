using System;

namespace UPandaGF
{
    /// <summary>
    /// 单步设置结构迁移：把 <see cref="FromVersion"/> 的数据改成 <see cref="ToVersion"/>。
    /// <para>只处理字段改名/语义变化/结构变化；缺省字段由 <see cref="SettingsData.EnsureDefaults"/> 和
    /// <see cref="SettingsRegistry.NormalizeAll"/> 兜底。</para>
    /// </summary>
    public interface ISettingsMigration
    {
        int FromVersion { get; }
        int ToVersion { get; }
        void Migrate(SettingsData data);
    }
}
