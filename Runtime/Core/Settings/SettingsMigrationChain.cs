using System;
using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>
    /// 设置结构迁移链：从文件里的 schemaVersion 逐级迁到 <see cref="SettingsData.CurrentSchemaVersion"/>。
    /// <para>默认包含 0→1 的兼容迁移（旧文件没有 schemaVersion 时按 0 处理）。
    /// 迁移失败（缺步骤/未来版本/异常）时服务回退默认值，不覆盖原文件。</para>
    /// </summary>
    public sealed class SettingsMigrationChain
    {
        private readonly List<ISettingsMigration> migrations = new List<ISettingsMigration>();

        public SettingsMigrationChain(bool includeLegacy = true)
        {
            if (includeLegacy) Add(new LegacyV0ToV1Migration());
        }

        public int CurrentVersion { get { return SettingsData.CurrentSchemaVersion; } }

        public void Add(ISettingsMigration migration)
        {
            if (migration == null) throw new ArgumentNullException(nameof(migration));
            if (migration.ToVersion <= migration.FromVersion)
                throw new ArgumentException("迁移目标版本必须大于来源版本。", nameof(migration));
            migrations.RemoveAll(item => item.FromVersion == migration.FromVersion);
            migrations.Add(migration);
            migrations.Sort((left, right) => left.FromVersion.CompareTo(right.FromVersion));
        }

        /// <summary>把 data 迁移到当前版本；失败返回 false 并给出原因。</summary>
        public bool TryMigrate(SettingsData data, out string error)
        {
            error = null;
            if (data == null) { error = "设置数据为空"; return false; }

            int version = data.schemaVersion;
            if (version > CurrentVersion)
            {
                error = "设置文件版本高于当前程序：" + version + " > " + CurrentVersion;
                return false;
            }

            int guard = 0;
            while (version < CurrentVersion)
            {
                if (++guard > 32) { error = "迁移链疑似循环"; return false; }
                ISettingsMigration migration = null;
                for (int i = 0; i < migrations.Count; i++)
                {
                    if (migrations[i].FromVersion == version) { migration = migrations[i]; break; }
                }
                if (migration == null)
                {
                    error = "缺少 " + version + " → " + CurrentVersion + " 的设置迁移";
                    return false;
                }

                try { migration.Migrate(data); }
                catch (Exception exception)
                {
                    error = "设置迁移 " + migration.FromVersion + "→" + migration.ToVersion + " 失败：" + exception.Message;
                    return false;
                }

                version = migration.ToVersion;
                data.schemaVersion = version;
            }

            data.schemaVersion = CurrentVersion;
            data.EnsureDefaults();
            return true;
        }

        /// <summary>旧文件没有 schemaVersion 时按 0 处理；0→1 只补结构，不改字段语义。</summary>
        private sealed class LegacyV0ToV1Migration : ISettingsMigration
        {
            public int FromVersion { get { return 0; } }
            public int ToVersion { get { return 1; } }
            public void Migrate(SettingsData data) { data.EnsureDefaults(); }
        }
    }
}
