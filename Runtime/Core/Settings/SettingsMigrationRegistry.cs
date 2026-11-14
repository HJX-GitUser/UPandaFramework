using System;
using System.Collections.Generic;

namespace UPandaGF
{
    /// <summary>
    /// 项目自定义迁移的注册入口：在启动前（如 <c>[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]</c>）
    /// 调用 <see cref="Register"/>，<see cref="SettingsMigrationRegistry.CreateChain"/> 会把这些迁移
    /// 和内置 0→1 迁移一起交给 SettingsService。
    /// <para>同 FromVersion 的后注册者覆盖内置迁移；注册必须在设置加载前完成。</para>
    /// </summary>
    public static class SettingsMigrationRegistry
    {
        private static readonly List<ISettingsMigration> Registered = new List<ISettingsMigration>();
        private static readonly object Gate = new object();

        public static void Register(ISettingsMigration migration)
        {
            if (migration == null) throw new ArgumentNullException(nameof(migration));
            lock (Gate)
            {
                Registered.RemoveAll(item => item.FromVersion == migration.FromVersion);
                Registered.Add(migration);
                Registered.Sort((left, right) => left.FromVersion.CompareTo(right.FromVersion));
            }
        }

        public static bool Unregister(ISettingsMigration migration)
        {
            if (migration == null) return false;
            lock (Gate) return Registered.Remove(migration);
        }

        public static void Clear()
        {
            lock (Gate) Registered.Clear();
        }

        public static SettingsMigrationChain CreateChain()
        {
            var chain = new SettingsMigrationChain();
            lock (Gate)
            {
                for (int i = 0; i < Registered.Count; i++) chain.Add(Registered[i]);
            }
            return chain;
        }
    }
}
