using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UPandaGF.Gameplay
{
    /// <summary>管理模型及其子物体的描边材质与运行时网格。</summary>
    [MovedFrom(true, "UPandaGF.Gameplay", "UPandaGF.Gameplay", "QuickOutlineHighlight")]
    [DisallowMultipleComponent]
    public class OutLine : MonoBehaviour
    {
        /// <summary>数值对应已有场景和预制体的序列化数据。</summary>
        public enum 描边模式
        {
            全部描边 = 0,
            可见描边 = 1,
            遮挡描边 = 2,
            描边与剪影 = 3,
            仅剪影 = 4
        }

        private static readonly int ZTestId = Shader.PropertyToID("_ZTest");
        private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int SmoothNormalsId = Shader.PropertyToID("_UseSmoothNormals");
        // 按源网格、材质槽布局和平滑法线策略复用副本。
        private static readonly Dictionary<(Mesh source, int extraSlots, bool smooth), PreparedMesh> Meshes =
            new Dictionary<(Mesh, int, bool), PreparedMesh>();
        // 每个 Renderer 同时只能由一个启用的描边组件管理。
        private static readonly Dictionary<Renderer, Target> AttachedTargets = new Dictionary<Renderer, Target>();

        [Serializable]
        private class ListVector3
        {
            public List<Vector3> data = null;
        }

        private sealed class PreparedMesh
        {
            public (Mesh source, int extraSlots, bool smooth) Key;
            public Mesh Mesh;
            public int References;
        }

        private sealed class Target
        {
            public OutLine Owner;
            public Renderer Renderer;
            public MeshFilter Filter;
            public SkinnedMeshRenderer Skin;
            public PreparedMesh Prepared;
        }

        [SerializeField] private 描边模式 outlineMode;
        [SerializeField] private Color outlineColor = Color.white;
        [SerializeField, Range(0f, 200f)] private float outlineWidth = 4f;
        [SerializeField] public bool CanFlash = true;

        [Header("Optional")]
        [SerializeField, Tooltip("在编辑器中预计算并保存普通网格的描边法线。")]
        private bool precomputeOutline = false;

        [SerializeField, HideInInspector] private List<Mesh> bakeKeys = new List<Mesh>();
        [SerializeField, HideInInspector] private List<ListVector3> bakeValues = new List<ListVector3>();

        private List<Target> targets = new List<Target>();
        private List<Target> nextTargets = new List<Target>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<float> blendWeights = new List<float>();
        private readonly HashSet<Renderer> warnedTargets = new HashSet<Renderer>();
        private readonly Material[] fillMaterials = new Material[2];
        private Material maskMaterial;
        private Material fillTemplate;
        private Transform previousParent;
        private bool targetsDirty;
        private bool propertiesDirty = true;
        private bool warnedMaterials;
        private float flashPhase;
        private float displayedAlpha = 1f;

        public 描边模式 OutlineMode
        {
            get => outlineMode;
            set
            {
                outlineMode = value;
                propertiesDirty = true;
            }
        }

        public Color OutlineColor
        {
            get => outlineColor;
            set
            {
                outlineColor = value;
                propertiesDirty = true;
            }
        }

        public float OutlineWidth
        {
            get => outlineWidth;
            set
            {
                outlineWidth = value;
                propertiesDirty = true;
            }
        }

        private void OnEnable()
        {
            previousParent = transform.parent;
            InvalidateAncestors(previousParent);
            RefreshTargets();
        }

        private void OnTransformChildrenChanged()
        {
            targetsDirty = true;
        }

        private void OnTransformParentChanged()
        {
            InvalidateAncestors(previousParent);
            previousParent = transform.parent;
            InvalidateAncestors(previousParent);
            targetsDirty = true;
        }

        private static void InvalidateAncestors(Transform parent)
        {
            for (; parent != null; parent = parent.parent)
            {
                if (parent.TryGetComponent<OutLine>(out var outline))
                {
                    outline.targetsDirty = true;
                }
            }
        }

        private void LateUpdate()
        {
            if (targetsDirty)
            {
                RefreshTargets();
            }

            if (targets.Count == 0)
            {
                return;
            }

            if (CanFlash)
            {
                // 在 0 到 2 之间循环，再映射到 0 到 1 的往返透明度。
                flashPhase = Mathf.Repeat(flashPhase + Time.deltaTime * 6f, 2f);
            }

            float alpha = CanFlash ? Mathf.PingPong(flashPhase, 1f) : 1f;
            if (propertiesDirty)
            {
                ApplyMaterialProperties();
                SetColor(alpha);
            }
            else if (displayedAlpha != alpha)
            {
                SetColor(alpha);
            }
        }

        /// <summary>更换网格、修改材质槽或调整深层子物体后，重新收集描边目标。</summary>
        public void RefreshTargets()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            targetsDirty = false;
            foreach (var target in targets)
            {
                Detach(target);
            }

            GetComponentsInChildren(true, renderers);
            warnedTargets.RemoveWhere(renderer => renderer == null);
            foreach (var renderer in renderers)
            {
                if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && Owns(renderer))
                {
                    Attach(renderer);
                }
            }
            renderers.Clear();

            // 新目标先获取副本，再释放旧引用，避免刷新时重复创建网格。
            ReleaseMeshes(targets);
            (targets, nextTargets) = (nextTargets, targets);
            if (targets.Count > 0)
            {
                ApplyMaterialProperties();
                SetColor(CanFlash ? Mathf.PingPong(flashPhase, 1f) : 1f);
            }
        }

        // 跳过由更近的描边组件负责的 Renderer。
        private bool Owns(Renderer renderer)
        {
            for (var node = renderer.transform; node != transform; node = node.parent)
            {
                if (node.TryGetComponent<OutLine>(out var closer) && closer.enabled)
                {
                    return false;
                }
            }
            return true;
        }

        private void Attach(Renderer renderer)
        {
            if (AttachedTargets.TryGetValue(renderer, out var attached))
            {
                attached.Owner.Detach(attached);
                attached.Owner.targetsDirty = true;
            }

            var skin = renderer as SkinnedMeshRenderer;
            MeshFilter filter = null;
            if (skin == null)
            {
                renderer.TryGetComponent(out filter);
            }

            var source = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
            if (source == null || source.subMeshCount == 0)
            {
                return;
            }

            renderer.GetSharedMaterials(materials);
            int subMeshes = source.subMeshCount;
            if (renderer.isPartOfStaticBatch ||
                materials.Count < subMeshes ||
                (subMeshes > 1 && !source.isReadable))
            {
                if (warnedTargets.Add(renderer))
                {
                    Debug.LogWarning("OutLine skipped a renderer. Disable static batching, assign a material slot " +
                        "for every submesh, and enable Read/Write for multi-submesh meshes.", renderer);
                }
                return;
            }

            if (subMeshes > 1)
            {
                for (int i = 0; i < subMeshes; i++)
                {
                    if (source.GetTopology(i) != MeshTopology.Triangles)
                    {
                        if (warnedTargets.Add(renderer))
                        {
                            Debug.LogWarning("OutLine requires triangle topology on multi-submesh meshes.", renderer);
                        }
                        return;
                    }
                }
            }

            if (!EnsureMaterials())
            {
                return;
            }

            bool smooth = skin == null && source.isReadable &&
                source.HasVertexAttribute(VertexAttribute.Normal) &&
                !source.HasVertexAttribute(VertexAttribute.TexCoord3) &&
                ((MeshRenderer)renderer).additionalVertexStreams == null;
            int extraSlots = subMeshes > 1 ? materials.Count - subMeshes : 0;
            var prepared = smooth || subMeshes > 1 ? AcquireMesh(source, extraSlots, smooth) : null;
            var target = new Target { Owner = this, Renderer = renderer, Filter = filter, Skin = skin, Prepared = prepared };

            if (prepared != null)
            {
                AssignMesh(target, prepared.Mesh);
            }
            materials.Add(maskMaterial);
            materials.Add(GetFillMaterial(smooth));
            renderer.SetSharedMaterials(materials);
            AttachedTargets.Add(renderer, target);
            nextTargets.Add(target);
        }

        private bool EnsureMaterials()
        {
            if (maskMaterial != null)
            {
                return true;
            }

            var mask = Resources.Load<Material>("Materials/OutlineMask");
            var fill = Resources.Load<Material>("Materials/OutlineFill");
            if (mask == null || fill == null || mask.shader == null || fill.shader == null ||
                mask.shader.name != "UPandaGF/OutLine/OutlineMask" ||
                fill.shader.name != "UPandaGF/OutLine/OutlineFill")
            {
                if (!warnedMaterials)
                {
                    Debug.LogError("OutLine could not load its outline materials. Check Resources/Materials and the shaders.", this);
                    warnedMaterials = true;
                }
                return false;
            }

            maskMaterial = new Material(mask) { name = "OutlineMask (Instance)", hideFlags = HideFlags.DontSave };
            fillTemplate = fill;
            return true;
        }

        private Material GetFillMaterial(bool smooth)
        {
            int index = smooth ? 1 : 0;
            if (fillMaterials[index] == null)
            {
                var material = new Material(fillTemplate)
                {
                    name = smooth ? "OutlineFill (Smooth Normals)" : "OutlineFill (Instance)",
                    hideFlags = HideFlags.DontSave
                };
                material.SetFloat(SmoothNormalsId, index);
                fillMaterials[index] = material;
            }
            return fillMaterials[index];
        }

        // 模板遮罩与填充的深度比较组合决定五种显示效果。
        private void ApplyMaterialProperties()
        {
            var maskTest = CompareFunction.Always;
            var fillTest = CompareFunction.Always;
            float width = outlineWidth;

            switch (outlineMode)
            {
                case 描边模式.可见描边:
                    fillTest = CompareFunction.LessEqual;
                    break;
                case 描边模式.遮挡描边:
                    fillTest = CompareFunction.Greater;
                    break;
                case 描边模式.描边与剪影:
                    maskTest = CompareFunction.LessEqual;
                    break;
                case 描边模式.仅剪影:
                    maskTest = CompareFunction.LessEqual;
                    fillTest = CompareFunction.Greater;
                    width = 0f;
                    break;
            }

            maskMaterial.SetFloat(ZTestId, (float)maskTest);
            foreach (var material in fillMaterials)
            {
                if (material != null)
                {
                    material.SetFloat(ZTestId, (float)fillTest);
                    material.SetFloat(WidthId, width);
                }
            }
            propertiesDirty = false;
        }

        private void SetColor(float alpha)
        {
            displayedAlpha = alpha;
            Color color = outlineColor;
            color.a *= alpha;
            foreach (var material in fillMaterials)
            {
                if (material != null)
                {
                    material.SetColor(ColorId, color);
                }
            }
        }

        private void OnDisable()
        {
            foreach (var target in targets)
            {
                Detach(target);
            }
            InvalidateAncestors(transform.parent);
        }

        private void OnDestroy()
        {
            ReleaseMeshes(targets);
            Destroy(maskMaterial);
            foreach (var material in fillMaterials)
            {
                Destroy(material);
            }
        }

        private void Detach(Target target)
        {
            var renderer = target.Renderer;
            if (!AttachedTargets.TryGetValue(renderer, out var attached) || attached != target)
            {
                return;
            }

            AttachedTargets.Remove(renderer);
            if (renderer == null)
            {
                return;
            }

            renderer.GetSharedMaterials(materials);
            bool changed = materials.Remove(maskMaterial);
            foreach (var material in fillMaterials)
            {
                if (material != null)
                {
                    changed |= materials.Remove(material);
                }
            }
            if (changed)
            {
                renderer.SetSharedMaterials(materials);
            }

            var prepared = target.Prepared;
            if (prepared != null &&
                ((target.Skin != null && target.Skin.sharedMesh == prepared.Mesh) ||
                 (target.Filter != null && target.Filter.sharedMesh == prepared.Mesh)))
            {
                AssignMesh(target, prepared.Key.source);
            }
        }

        private void AssignMesh(Target target, Mesh mesh)
        {
            // 程序生成的源网格可能在副本仍被使用时销毁。
            mesh = mesh != null ? mesh : null;
            if (target.Skin == null)
            {
                target.Filter.sharedMesh = mesh;
                return;
            }

            var skin = target.Skin;
            // 换入或恢复蒙皮网格时保留原包围盒与当前表情权重。
            var bounds = skin.localBounds;
            blendWeights.Clear();
            for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++)
            {
                blendWeights.Add(skin.GetBlendShapeWeight(i));
            }
            skin.sharedMesh = mesh;
            skin.localBounds = bounds;
            int restoredWeights = mesh != null ? Mathf.Min(blendWeights.Count, mesh.blendShapeCount) : 0;
            for (int i = 0; i < restoredWeights; i++)
            {
                skin.SetBlendShapeWeight(i, blendWeights[i]);
            }
        }

        private PreparedMesh AcquireMesh(Mesh source, int extraSlots, bool smooth)
        {
            var key = (source, extraSlots, smooth);
            if (!Meshes.TryGetValue(key, out var prepared))
            {
                var mesh = Instantiate(source);
                mesh.name = source.name + " (OutLine)";
                mesh.hideFlags = HideFlags.DontSave;
                if (smooth)
                {
                    mesh.SetUVs(3, GetSmoothNormals(source));
                }
                if (source.subMeshCount > 1)
                {
                    AddOutlineSubmesh(source, mesh, extraSlots);
                }
                prepared = new PreparedMesh { Key = key, Mesh = mesh };
                Meshes.Add(key, prepared);
            }
            prepared.References++;
            return prepared;
        }

        private static void ReleaseMeshes(List<Target> bindings)
        {
            foreach (var target in bindings)
            {
                var prepared = target.Prepared;
                if (prepared != null && --prepared.References == 0)
                {
                    Meshes.Remove(prepared.Key);
                    Destroy(prepared.Mesh);
                }
            }
            bindings.Clear();
        }

        private static void AddOutlineSubmesh(Mesh source, Mesh mesh, int extraSlots)
        {
            int originalCount = source.subMeshCount;
            int combinedSlot = originalCount + extraSlots;
            // 合并时使用绝对索引。部分 16 位网格通过基顶点寻址更多顶点。
            bool upgradeIndices = mesh.indexFormat == IndexFormat.UInt16 && mesh.vertexCount > 65535;
            if (upgradeIndices)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.subMeshCount = combinedSlot + 1;
            var combined = new List<int>();
            var indices = new List<int>();
            for (int i = 0; i < originalCount; i++)
            {
                source.GetTriangles(indices, i);
                combined.AddRange(indices);
                if (upgradeIndices)
                {
                    mesh.SetTriangles(indices, i, false);
                }
            }

            // 为额外的原材质层保留最后一个原子网格的几何，再追加描边专用槽。
            for (int i = originalCount; i < combinedSlot; i++)
            {
                mesh.SetTriangles(indices, i, false);
            }
            mesh.SetTriangles(combined, combinedSlot, false);
        }

        private List<Vector3> GetSmoothNormals(Mesh mesh)
        {
            if (precomputeOutline)
            {
                int index = bakeKeys.IndexOf(mesh);
                if (index >= 0 && index < bakeValues.Count &&
                    bakeValues[index]?.data?.Count == mesh.vertexCount)
                {
                    return bakeValues[index].data;
                }
            }
            return CalculateSmoothNormals(mesh);
        }

        private static List<Vector3> CalculateSmoothNormals(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = new List<Vector3>(vertices.Length);
            mesh.GetNormals(normals);
            // 将同一位置的法线汇总，让硬边两侧的顶点沿同一方向外扩。
            var sums = new Dictionary<Vector3, Vector3>(vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                sums.TryGetValue(vertices[i], out var sum);
                sums[vertices[i]] = sum + normals[i];
            }
            for (int i = 0; i < vertices.Length; i++)
            {
                normals[i] = sums[vertices[i]].normalized;
            }
            return normals;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            propertiesDirty = true;
            if (!precomputeOutline)
            {
                bakeKeys.Clear();
                bakeValues.Clear();
            }
            else if (bakeKeys.Count == 0 || bakeKeys.Count != bakeValues.Count)
            {
                // 导入过程中也可能触发 OnValidate，延迟到下一次编辑器更新再读取网格。
                EditorApplication.delayCall -= BakeIfNeeded;
                EditorApplication.delayCall += BakeIfNeeded;
            }
        }

        private void BakeIfNeeded()
        {
            if (this != null && precomputeOutline && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Bake();
            }
        }

        [ContextMenu("Rebuild Outline Normals")]
        private void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Undo.RecordObject(this, "Bake OutLine Normals");
            bakeKeys.Clear();
            bakeValues.Clear();
            var baked = new HashSet<Mesh>();
            foreach (var filter in GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh != null && mesh.isReadable && mesh.HasVertexAttribute(VertexAttribute.Normal) &&
                    !mesh.HasVertexAttribute(VertexAttribute.TexCoord3) && baked.Add(mesh))
                {
                    bakeKeys.Add(mesh);
                    bakeValues.Add(new ListVector3 { data = CalculateSmoothNormals(mesh) });
                }
            }
            EditorUtility.SetDirty(this);
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }
#endif
    }
}
