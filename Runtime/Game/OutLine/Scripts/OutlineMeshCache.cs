using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UPandaGF.Gameplay
{
    /// <summary>
    /// 描边用网格副本缓存。
    /// 原始网格不会被修改：需要平滑法线或合并子网格时，取一份可读副本，
    /// 把全部三角形合并到 0 号子网格，并把计算好的法线写进副本的 NORMAL 通道
    /// （不占用 UV 通道，因此不会再和模型的第二套/第四套 UV 打架）。
    /// 相同源网格 + 相同来源的组件共享同一份副本，最后一个使用者释放时才销毁。
    ///
    /// 键使用源网格的 InstanceID 而不是对象引用：网格被销毁后仍能安全地查询与释放，
    /// 场景卸载时再扫一遍，清掉源网格已销毁的残留条目。
    /// </summary>
    internal static class OutlineMeshCache
    {
        private sealed class Entry
        {
            internal Mesh Source;
            internal Mesh Copy;
            internal int Users;
        }

        private static readonly Dictionary<int, Entry> Entries = new Dictionary<int, Entry>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            Clear();
        }

        private static void HandleSceneUnloaded(Scene scene)
        {
            Sweep();
        }

        /// <summary>取一份可直接用于代理渲染的网格；调用方必须在销毁代理时调用 <see cref="Release"/>。</summary>
        internal static Mesh Acquire(Mesh source, List<Vector3> bakedNormals)
        {
            if (source == null)
            {
                return null;
            }

            int id = source.GetInstanceID();
            if (Entries.TryGetValue(id, out Entry entry) && entry.Copy != null)
            {
                entry.Users++;
                return entry.Copy;
            }

            Mesh built = Build(source, bakedNormals);
            Entries[id] = new Entry { Source = source, Copy = built, Users = 1 };
            return built;
        }

        internal static void Release(Mesh source)
        {
            if (ReferenceEquals(source, null))
            {
                return;
            }

            int id = source.GetInstanceID();
            if (!Entries.TryGetValue(id, out Entry entry))
            {
                return;
            }
            entry.Users--;
            if (entry.Users > 0)
            {
                return;
            }

            Entries.Remove(id);
            DestroyCopy(entry.Copy);
        }

        /// <summary>清掉源网格已销毁（或副本丢失）的条目，避免跨场景累积。</summary>
        internal static void Sweep()
        {
            List<int> stale = null;
            foreach (KeyValuePair<int, Entry> pair in Entries)
            {
                if (pair.Value.Source == null || pair.Value.Copy == null)
                {
                    if (stale == null)
                    {
                        stale = new List<int>();
                    }
                    stale.Add(pair.Key);
                }
            }
            if (stale == null)
            {
                return;
            }

            for (int i = 0; i < stale.Count; i++)
            {
                if (!Entries.TryGetValue(stale[i], out Entry entry))
                {
                    continue;
                }
                Entries.Remove(stale[i]);
                DestroyCopy(entry.Copy);
            }
        }

        internal static void Clear()
        {
            foreach (Entry entry in Entries.Values)
            {
                DestroyCopy(entry.Copy);
            }
            Entries.Clear();
        }

        private static void DestroyCopy(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(mesh);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// 面积加权的平滑法线：先按量化后的顶点位置把「同一位置」的顶点归组，
        /// 再把相邻三角形的面法线按面积累加到组上。相比逐顶点平均法线，
        /// 它不会被三角形的切分密度带偏，硬边处也能得到统一的外扩方向。
        /// </summary>
        internal static List<Vector3> ComputeSmoothNormals(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector3[] normals = mesh.normals;

            var groups = new Dictionary<(int x, int y, int z), int>(vertices.Length);
            var groupOf = new int[vertices.Length];
            int groupCount = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                var key = (
                    Mathf.RoundToInt(vertex.x * 1000f),
                    Mathf.RoundToInt(vertex.y * 1000f),
                    Mathf.RoundToInt(vertex.z * 1000f));
                if (!groups.TryGetValue(key, out int group))
                {
                    group = groupCount++;
                    groups.Add(key, group);
                }
                groupOf[i] = group;
            }

            var accumulated = new Vector3[groupCount];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                // 叉积长度正比于三角形面积，等于按面积加权。
                Vector3 face = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                accumulated[groupOf[a]] += face;
                accumulated[groupOf[b]] += face;
                accumulated[groupOf[c]] += face;
            }

            var result = new List<Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 smooth = accumulated[groupOf[i]];
                if (smooth.sqrMagnitude > 1e-12f)
                {
                    result.Add(smooth.normalized);
                }
                else
                {
                    // 正反面完全抵消（例如单面片）时退回原始法线，避免出现零向量。
                    result.Add(i < normals.Length ? normals[i] : Vector3.up);
                }
            }
            return result;
        }

        private static Mesh Build(Mesh source, List<Vector3> bakedNormals)
        {
            Mesh instance = UnityEngine.Object.Instantiate(source);
            instance.name = source.name + " (OutLine)";
            instance.hideFlags = HideFlags.DontSave;

            if (source.subMeshCount > 1)
            {
                MergeSubmeshes(instance);
            }

            if (instance.HasVertexAttribute(VertexAttribute.Normal))
            {
                List<Vector3> normals = bakedNormals != null && bakedNormals.Count == instance.vertexCount
                    ? bakedNormals
                    : ComputeSmoothNormals(instance);
                instance.SetNormals(normals);
            }

            // 外扩发生在着色器里，网格包围盒需要放大，否则大宽度时会被视锥剔除掉一角。
            Bounds bounds = source.bounds;
            bounds.Expand(Mathf.Max(1f, bounds.size.magnitude * 0.2f));
            instance.bounds = bounds;
            return instance;
        }

        private static void MergeSubmeshes(Mesh instance)
        {
            int count = instance.subMeshCount;
            var combined = new List<int>(instance.vertexCount * 3);
            var part = new List<int>();
            for (int i = 0; i < count; i++)
            {
                instance.GetTriangles(part, i);
                combined.AddRange(part);
            }
            instance.subMeshCount = 1;
            // 包围盒由 Build 统一设置，这里不必计算。
            instance.SetTriangles(combined, 0, false);
        }
    }
}
