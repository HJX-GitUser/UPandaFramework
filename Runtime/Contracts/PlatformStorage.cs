using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>平台存储能力。Android 配额查询及 WebGL 桥接入口由 Unity 主线程调用。</summary>
    public static class PlatformStorage
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        public const bool SupportsBackgroundThreads = false;
        public const bool RequiresMainThreadSpaceProbe = true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        public const bool SupportsBackgroundThreads = true;
        public const bool RequiresMainThreadSpaceProbe = true;
#else
        public const bool SupportsBackgroundThreads = true;
        public const bool RequiresMainThreadSpaceProbe = false;
#endif
        /// <summary>WebGL 返回当前 origin 的可用存储配额，不是设备物理磁盘余量。</summary>
        public static Task<long?> AvailableSpaceAsync(string path)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int id = ++next; var completion = new TaskCompletionSource<long?>(); quotas.Add(id, completion);
            try { UPanda_StorageQuota(id, quotaCallback); } catch { quotas.Remove(id); throw; }
            return completion.Task;
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                string existing = Path.GetFullPath(path);
                while (!Directory.Exists(existing)) { existing = Path.GetDirectoryName(existing); if (string.IsNullOrEmpty(existing)) return Task.FromResult<long?>(null); }
                using (var stat = new AndroidJavaObject("android.os.StatFs", existing)) return Task.FromResult<long?>(stat.Call<long>("getAvailableBytes"));
            }
            catch (Exception) { return Task.FromResult<long?>(null); }
#else
            return Task.FromResult(AvailableDiskSpace(path));
#endif
        }
        /// <summary>选取包含目标的最长挂载点，避免 Unix /data 等独立分区误查 /。</summary>
        public static long? AvailableDiskSpace(string path)
        {
            try
            {
                string full = Path.GetFullPath(path);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                string existing = full;
                while (!Directory.Exists(existing)) { existing = Path.GetDirectoryName(existing); if (string.IsNullOrEmpty(existing)) return null; }
                ulong available, total, free;
                return GetDiskFreeSpaceEx(existing, out available, out total, out free) ? (long?)Math.Min(available, (ulong)long.MaxValue) : null;
#else
                DriveInfo selected = null; int length = -1;
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                foreach (var drive in DriveInfo.GetDrives())
                {
                    string mount = drive.RootDirectory.FullName.TrimEnd(Path.DirectorySeparatorChar);
                    if ((full.Equals(mount, comparison) || full.StartsWith(mount + Path.DirectorySeparatorChar, comparison)) && mount.Length > length)
                    { selected = drive; length = mount.Length; }
                }
                return selected?.AvailableFreeSpace;
#endif
            }
            catch (Exception) { return null; }
        }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetDiskFreeSpaceEx(string path, out ulong available, out ulong total, out ulong free);
#endif
        /// <summary>WebGL 将虚拟文件系统提交至 IndexedDB；其余平台文件关闭/Flush 已完成写入。</summary>
        public static Task FlushAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int id = ++next; var completion = new TaskCompletionSource<bool>(); flushes.Add(id, completion);
            try { UPanda_SyncPersistent(id, flushCallback); } catch { flushes.Remove(id); throw; }
            return completion.Task;
#else
            return Task.CompletedTask;
#endif
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        private delegate void FlushCallback(int id, int error);
        private delegate void QuotaCallback(int id, double bytes);
        private static readonly FlushCallback flushCallback = Flushed;
        private static readonly QuotaCallback quotaCallback = Quota;
        private static readonly Dictionary<int, TaskCompletionSource<bool>> flushes = new Dictionary<int, TaskCompletionSource<bool>>();
        private static readonly Dictionary<int, TaskCompletionSource<long?>> quotas = new Dictionary<int, TaskCompletionSource<long?>>();
        private static int next;
        [DllImport("__Internal")] private static extern void UPanda_SyncPersistent(int id, FlushCallback callback);
        [DllImport("__Internal")] private static extern void UPanda_StorageQuota(int id, QuotaCallback callback);
        [AOT.MonoPInvokeCallback(typeof(FlushCallback))]
        private static void Flushed(int id, int error)
        { TaskCompletionSource<bool> completion; if (!flushes.TryGetValue(id, out completion)) return; flushes.Remove(id); if (error == 0) completion.TrySetResult(true); else completion.TrySetException(new IOException("浏览器 IndexedDB 持久化失败；检查配额或隐私存储策略")); }
        [AOT.MonoPInvokeCallback(typeof(QuotaCallback))]
        private static void Quota(int id, double bytes)
        { TaskCompletionSource<long?> completion; if (!quotas.TryGetValue(id, out completion)) return; quotas.Remove(id); completion.TrySetResult(double.IsNaN(bytes) || bytes < 0 ? (long?)null : (long)Math.Min(bytes, long.MaxValue)); }
#endif
    }
}
