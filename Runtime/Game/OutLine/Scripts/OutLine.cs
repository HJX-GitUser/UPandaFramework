/* ==========================================================================================
 *  OutLine —— 描边高亮（原创重写版）
 *
 *  【做法】不改动业务对象的材质与网格，而是在被描边对象下创建一个隐藏的代理 Renderer：
 *     · 可读网格：取一份副本，合并全部子网格，并把面积加权的平滑法线写进副本的 NORMAL；
 *     · 不可读网格：直接复用原网格（沿用原始法线，硬边处可能出现接缝）；
 *     · 材质：五种模式各一份共享材质，颜色与宽度用 MaterialPropertyBlock 逐对象覆盖。
 *  代理只做「模板 + 外扩轮廓」两件事，禁用组件时整块代理销毁，业务资源原样保留。
 *
 *  【模式】五种模式的差异只有两个 Pass 的深度比较（写入共享材质）与轮廓宽度：
 *    全部描边 / 可见描边 / 遮挡描边 / 描边与剪影 / 仅剪影。
 *  枚举数值 0~4 是序列化契约，调整顺序会影响已有场景。
 *
 *  【层级】一个 Renderer 只由离它最近的启用组件负责：子节点上的组件优先，
 *  父级组件在扫描时会跳过已被更近组件接管的 Renderer，避免父子叠加、描边变粗。
 * ========================================================================================== */

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
    /// <summary>给模型及其子物体叠加描边高亮的组件。</summary>
    [MovedFrom(true, "UPandaGF.Gameplay", "UPandaGF.Gameplay", "OutDrawline")]
    [DisallowMultipleComponent]
    [AddComponentMenu("UPandaGF/Gameplay/OutLine")]
    public sealed class OutLine : MonoBehaviour
    {
        /// <summary>描边 Shader 的声明名，编辑器工具与测试按此查找。</summary>
        public const string ShaderName = "UPandaGF/OutLine/Outline";

        /// <summary>模板材质在 Resources 下的路径（不含扩展名）。</summary>
        public const string MaterialResourcePath = "Materials/Outline";

        /// <summary>序列化数值 0~4 对应已有场景与预制体，不要调整顺序。</summary>
        public enum 描边模式
        {
            全部描边 = 0,
            可见描边 = 1,
            遮挡描边 = 2,
            描边与剪影 = 3,
            仅剪影 = 4
        }

        [Serializable]
        private sealed class BakedNormals
        {
            public List<Vector3> values = new List<Vector3>();
        }

        [Header("描边")]
        [SerializeField, Tooltip("深度比较组合决定轮廓在遮挡物前还是后显示。")]
        private 描边模式 outlineMode = 描边模式.全部描边;

        [SerializeField, Tooltip("轮廓颜色；Alpha 为整体透明度，会被闪烁再次调制。")]
        private Color outlineColor = Color.white;

        [SerializeField, Range(0f, 200f), Tooltip("屏幕相对厚度，透视按视深、正交按视野高度补偿。")]
        private float outlineWidth = 4f;

        [Header("行为")]
        [Tooltip("开启后轮廓做呼吸闪烁，受 Time.timeScale 影响。")]
        public bool CanFlash = true;

        [SerializeField, Tooltip("在编辑器中预计算平滑法线并随对象序列化，避免运行时逐顶点计算。")]
        private bool precomputeOutline;

        [SerializeField, HideInInspector]
        private List<Mesh> bakedMeshes = new List<Mesh>();

        [SerializeField, HideInInspector]
        private List<BakedNormals> bakedNormals = new List<BakedNormals>();

        private readonly List<OutlineTarget> targets = new List<OutlineTarget>();
        private readonly List<OutlineTarget> previous = new List<OutlineTarget>();
        private readonly List<Renderer> scanned = new List<Renderer>();
        private bool hierarchyDirty = true;
        private bool appearanceDirty = true;
        private float phase;
        private float alpha = 1f;
        private Transform lastParent;

        /// <summary>当前被描边的 Renderer 数量，用于排查层级接管是否符合预期。</summary>
        public int TargetCount => targets.Count;

        public 描边模式 OutlineMode
        {
            get => outlineMode;
            set
            {
                if (outlineMode == value)
                {
                    return;
                }
                outlineMode = value;
                appearanceDirty = true;
            }
        }

        public Color OutlineColor
        {
            get => outlineColor;
            set
            {
                outlineColor = value;
                appearanceDirty = true;
            }
        }

        public float OutlineWidth
        {
            get => outlineWidth;
            set
            {
                outlineWidth = Mathf.Max(0f, value);
                appearanceDirty = true;
            }
        }

        /// <summary>是否闪烁。与 <see cref="CanFlash"/> 同义，便于脚本按属性书写。</summary>
        public bool Flash
        {
            get => CanFlash;
            set => CanFlash = value;
        }

        private void OnEnable()
        {
            lastParent = transform.parent;
            NotifyAncestors(lastParent);
            Rebuild();
        }

        private void OnDisable()
        {
            ClearTargets();
            NotifyAncestors(transform.parent);
        }

        private void OnDestroy()
        {
            ClearTargets();
        }

        private void OnTransformChildrenChanged()
        {
            // 子物体变化会改变描边目标的集合，下一帧统一重建。
            hierarchyDirty = true;
        }

        private void OnTransformParentChanged()
        {
            NotifyAncestors(lastParent);
            lastParent = transform.parent;
            NotifyAncestors(lastParent);
            hierarchyDirty = true;
        }

        private void LateUpdate()
        {
            if (hierarchyDirty)
            {
                Rebuild();
            }
            if (targets.Count == 0)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].SyncPose();
            }

            if (CanFlash)
            {
                phase = Mathf.Repeat(phase + Time.deltaTime * 6f, 2f);
            }
            float next = CanFlash ? Mathf.PingPong(phase, 1f) : 1f;
            if (appearanceDirty || !Mathf.Approximately(next, alpha))
            {
                alpha = next;
                ApplyAppearance();
            }
        }

        /// <summary>网格、材质槽或深层子物体变化后调用，重新收集描边目标。</summary>
        public void RefreshTargets()
        {
            hierarchyDirty = false;
            Rebuild();
        }

        private void Rebuild()
        {
            hierarchyDirty = false;

            // 先建立新绑定，再释放旧绑定：新绑定先拿到网格副本的引用，
            // 副本的引用计数不会在中途归零，重建时不会出现网格被销毁的空档。
            previous.AddRange(targets);
            targets.Clear();

            if (isActiveAndEnabled)
            {
                GetComponentsInChildren(true, scanned);
                for (int i = 0; i < scanned.Count; i++)
                {
                    Renderer renderer = scanned[i];
                    if (renderer == null || renderer.GetComponent<OutlineProxy>() != null)
                    {
                        continue;
                    }
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    {
                        continue;
                    }
                    if (!Owns(renderer))
                    {
                        continue;
                    }

                    OutlineTarget target = OutlineTarget.Bind(this, renderer, BakedFor(renderer));
                    if (target != null)
                    {
                        targets.Add(target);
                    }
                }
                scanned.Clear();
            }

            for (int i = 0; i < previous.Count; i++)
            {
                previous[i].Dispose();
            }
            previous.Clear();

            appearanceDirty = true;
            alpha = CanFlash ? Mathf.PingPong(phase, 1f) : 1f;
            ApplyAppearance();
        }

        private void ClearTargets()
        {
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].Dispose();
            }
            targets.Clear();
            for (int i = 0; i < previous.Count; i++)
            {
                previous[i].Dispose();
            }
            previous.Clear();
        }

        private void ApplyAppearance()
        {
            appearanceDirty = false;
            Color tint = outlineColor;
            tint.a *= alpha;
            float width = outlineMode == 描边模式.仅剪影 ? 0f : outlineWidth;
            for (int i = 0; i < targets.Count; i++)
            {
                targets[i].ApplyAppearance(tint, width);
            }
        }

        /// <summary>离 Renderer 最近的启用组件负责它；没有更近的组件时归当前组件。</summary>
        private bool Owns(Renderer renderer)
        {
            for (Transform node = renderer.transform; node != null; node = node.parent)
            {
                if (node == transform)
                {
                    return true;
                }
                if (node.TryGetComponent<OutLine>(out OutLine closer) && closer != this && closer.isActiveAndEnabled)
                {
                    return false;
                }
            }
            return true;
        }

        private static void NotifyAncestors(Transform parent)
        {
            for (; parent != null; parent = parent.parent)
            {
                if (parent.TryGetComponent<OutLine>(out OutLine outline))
                {
                    outline.hierarchyDirty = true;
                }
            }
        }

        private List<Vector3> BakedFor(Renderer renderer)
        {
            if (!precomputeOutline)
            {
                return null;
            }

            Mesh mesh;
            if (renderer is SkinnedMeshRenderer skin)
            {
                mesh = skin.sharedMesh;
            }
            else if (renderer.TryGetComponent(out MeshFilter filter))
            {
                mesh = filter.sharedMesh;
            }
            else
            {
                mesh = null;
            }
            return BakedFor(mesh);
        }

        private List<Vector3> BakedFor(Mesh mesh)
        {
            if (!precomputeOutline || mesh == null)
            {
                return null;
            }
            int index = bakedMeshes.IndexOf(mesh);
            if (index < 0 || index >= bakedNormals.Count)
            {
                return null;
            }
            BakedNormals baked = bakedNormals[index];
            return baked != null && baked.values != null && baked.values.Count == mesh.vertexCount
                ? baked.values
                : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            outlineWidth = Mathf.Clamp(outlineWidth, 0f, 200f);
            if (Application.isPlaying)
            {
                appearanceDirty = true;
                return;
            }

            if (!precomputeOutline)
            {
                bakedMeshes.Clear();
                bakedNormals.Clear();
                return;
            }
            if (bakedMeshes.Count == 0 || bakedMeshes.Count != bakedNormals.Count)
            {
                // 导入过程中也会触发 OnValidate，网格读取推迟到下一次编辑器更新。
                EditorApplication.delayCall -= BakeIfEnabled;
                EditorApplication.delayCall += BakeIfEnabled;
            }
        }

        private void BakeIfEnabled()
        {
            if (this != null && precomputeOutline && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RebuildBakedNormals();
            }
        }

        /// <summary>重新计算并保存普通网格的平滑法线；模型重新导入或换网格后执行。</summary>
        [ContextMenu("重新计算描边法线")]
        private void RebuildBakedNormals()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            Undo.RecordObject(this, "Rebuild OutLine Normals");
            bakedMeshes.Clear();
            bakedNormals.Clear();

            var seen = new HashSet<Mesh>();
            foreach (MeshFilter filter in GetComponentsInChildren<MeshFilter>(true))
            {
                CollectBaked(filter != null ? filter.sharedMesh : null, seen);
            }
            foreach (SkinnedMeshRenderer skin in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                CollectBaked(skin != null ? skin.sharedMesh : null, seen);
            }

            EditorUtility.SetDirty(this);
            PrefabUtility.RecordPrefabInstancePropertyModifications(this);
        }

        private void CollectBaked(Mesh mesh, HashSet<Mesh> seen)
        {
            if (mesh == null || !mesh.isReadable || !mesh.HasVertexAttribute(VertexAttribute.Normal) || !seen.Add(mesh))
            {
                return;
            }
            bakedMeshes.Add(mesh);
            bakedNormals.Add(new BakedNormals { values = OutlineMeshCache.ComputeSmoothNormals(mesh) });
        }
#endif
    }
}
