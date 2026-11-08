using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UPandaGF.Gameplay.Replay
{
    public partial class ReplayManager
    {
        private CancellationTokenSource saveCancellation, loadCancellation;
        private Task<string> saving;
        private int loadGeneration;
        public bool IsFileOperationRunning => (saving != null && !saving.IsCompleted) || loadCancellation != null;
        public string Save()
        {
            if (!HasData) return null;
            return FileManager.Save(frames);
        }
        public Task<string> SaveAsync(CancellationToken token = default)
        {
            if (destroyed || !HasData) return Task.FromResult<string>(null);
            if (saving != null && !saving.IsCompleted) return saving;
            saveCancellation?.Dispose(); saveCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            return saving = FileManager.SaveAsync(frames, saveCancellation.Token);
        }
        public bool Load(string path)
        {
            var data = FileManager.Load(path); if (data == null || data.Count == 0) return false;
            return Adopt(data);
        }
        public async Task<bool> LoadAsync(string path, CancellationToken token = default)
        {
            if (destroyed) return false;
            loadCancellation?.Cancel(); loadCancellation?.Dispose();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token); loadCancellation = cancellation;
            int epoch = ++loadGeneration;
            try
            {
                var data = await FileManager.LoadAsync(path, cancellation.Token);
                if (destroyed || epoch != loadGeneration || cancellation.IsCancellationRequested) return false;
                if (data == null || data.Count == 0) return false;
                long bytes = 0, snapshots = 0; var slice = System.Diagnostics.Stopwatch.StartNew();
                foreach (var frame in data)
                {
                    bytes += 96;
                    foreach (var snapshot in frame.snapshots)
                    {
                        bytes += EstimateSnapshot(snapshot) + 8; snapshots++;
                        if ((snapshots & 127) == 0 && slice.ElapsedMilliseconds >= 4)
                        { await Task.Yield(); cancellation.Token.ThrowIfCancellationRequested(); slice.Restart(); }
                    }
                }
                if (destroyed || epoch != loadGeneration || !FitsLoadedBudget(data.Count, snapshots, bytes)) return false;
                loadCancellation = null; return AdoptPrepared(data, snapshots, bytes);
            }
            finally { if (ReferenceEquals(loadCancellation, cancellation)) loadCancellation = null; cancellation.Dispose(); }
        }
        private bool Adopt(List<RecordFrame> data)
        {
            long bytes = 0, snapshots = 0;
            foreach (var frame in data) { bytes += 96; foreach (var snapshot in frame.snapshots) { bytes += EstimateSnapshot(snapshot) + 8; snapshots++; } }
            if (!FitsLoadedBudget(data.Count, snapshots, bytes)) return false;
            return AdoptPrepared(data, snapshots, bytes);
        }
        private bool FitsLoadedBudget(int frameCount, long snapshots, long bytes)
        {
            var options = recording ?? (recording = new ReplayRecordingSettings());
            bool fits = (options.maximumFrames <= 0 || frameCount <= options.maximumFrames) && (options.maximumSnapshots <= 0 || snapshots <= options.maximumSnapshots)
                && (options.maximumMemoryMB <= 0 || bytes <= (long)options.maximumMemoryMB * 1024 * 1024);
            if (!fits) Debug.LogWarning("[ReplaySystem] 录制数据超过当前预算，保留现有数据"); return fits;
        }
        private bool AdoptPrepared(List<RecordFrame> data, long snapshots, long bytes)
        { ClearRecording(); frames.AddRange(data); Statistics.estimatedDataBytes = bytes; Statistics.snapshotReferences = snapshots; return true; }
        public void CancelFileOperations()
        {
            loadGeneration++; loadCancellation?.Cancel(); loadCancellation = null;
            saveCancellation?.Cancel(); saveCancellation?.Dispose(); saveCancellation = null; saving = null;
        }
    }
}
