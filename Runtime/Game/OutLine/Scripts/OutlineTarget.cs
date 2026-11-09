using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UPandaGF.Gameplay
{
    /// <summary>
    /// 一个被描边目标（Renderer）的运行时绑定。
    /// 它不修改目标本身的材质与网格，而是在目标下挂一个隐藏的代理 Renderer：
    /// 代理使用同一份几何（可读网格取一份合并+平滑法线的副本，不可读网格直接复用），
    /// 材质是五种模式共享的描边材质，颜色/宽度/宽度单位通过 MaterialPropertyBlock 逐对象覆盖。
    /// 禁用组件时代理回到对象池，目标对象的资源保持原样。
    /// </summary>
    internal sealed class OutlineTarget
    {
        private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int WidthModeId = Shader.PropertyToID("_WidthMode");

        private const int LargeVertexThreshold = 20000;

        // 同一个网格只提示一次，避免多物体共用网格时刷屏。
        private static readonly HashSet<int> WarnedUnreadable = new HashSet<int>();
        private static readonly HashSet<int> WarnedLarge = new HashSet<int>();

        private readonly OutLine owner;
        private readonly Renderer source;
        private readonly SkinnedMeshRenderer sourceSkin;
        private readonly Mesh sourceMesh;
        private readonly bool ownsMeshCopy;
        private readonly float[] blendShapeWeights;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private GameObject proxyObject;
        private Renderer proxyRenderer;
        private Renderer fillRenderer;
        private Mesh proxyMesh;
        private OutlineProxyKind proxyKind;
        private Material[] slotMaterials;
        private Material[] fillSlotMaterials;
        private bool disposed;

        private OutlineTarget(OutLine owner, Renderer source, SkinnedMeshRenderer sourceSkin, Mesh sourceMesh, bool ownsMeshCopy)
        {
            this.owner = owner;
            this.source = source;
            this.sourceSkin = sourceSkin;
            this.sourceMesh = sourceMesh;
            this.ownsMeshCopy = ownsMeshCopy;
            blendShapeWeights = sourceSkin != null && sourceMesh != null && sourceMesh.blendShapeCount > 0
                ? new float[sourceMesh.blendShapeCount]
                : null;
        }

        /// <summary>为 Renderer 建立代理；不支持或缺少网格时返回 null。retry 表示失败是暂时的（目标正在激活/停用）。</summary>
        internal static OutlineTarget Bind(OutLine owner, Renderer source, List<Vector3> bakedNormals, out bool retry)
        {
            retry = false;
            if (owner == null || source == null)
            {
                return null;
            }

            var skin = source as SkinnedMeshRenderer;
            Mesh mesh = MeshOf(source);
            if (mesh == null || mesh.subMeshCount == 0)
            {
                return null;
            }

            if (!mesh.isReadable)
            {
                WarnUnreadable(mesh, owner);
            }
            else if (!owner.PrecomputesNormals && mesh.vertexCount > LargeVertexThreshold)
            {
                WarnLargeMesh(mesh, owner);
            }

            // 可读网格取合并副本，顺带写入平滑法线；不可读网格直接复用原网格。
            bool copy = mesh.isReadable;
            Mesh proxyMesh = copy ? OutlineMeshCache.Acquire(mesh, bakedNormals) : mesh;
            if (proxyMesh == null)
            {
                return null;
            }

            var target = new OutlineTarget(owner, source, skin, mesh, copy);
            if (!target.CreateProxy(proxyMesh, skin))
            {
                retry = true;
                target.Dispose();
                return null;
            }
            return target;
        }

        /// <summary>取 Renderer 当前使用的网格；没有网格时返回 null。</summary>
        internal static Mesh MeshOf(Renderer source)
        {
            if (source is SkinnedMeshRenderer skin)
            {
                return skin.sharedMesh;
            }
            return source.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        }

        /// <summary>来源 Renderer，用于增量重建时匹配旧绑定。</summary>
        internal Renderer SourceRenderer => source;

        /// <summary>来源 Renderer 与网格都没变时可以复用现有代理，避免整树重建。</summary>
        internal bool CanReuse(Mesh mesh)
        {
            return !disposed && source != null && sourceMesh == mesh;
        }

        /// <summary>切换代理在 Hierarchy 中的可见性，便于排查。</summary>
        internal void SetProxyVisible(bool visible)
        {
            if (proxyObject == null)
            {
                return;
            }
            proxyObject.hideFlags = visible
                ? HideFlags.DontSave
                : HideFlags.HideInHierarchy | HideFlags.DontSave;
        }

        private static void WarnUnreadable(Mesh mesh, UnityEngine.Object context)
        {
            if (!WarnedUnreadable.Add(mesh.GetInstanceID()))
            {
                return;
            }
            Debug.LogWarning("OutLine：网格 \"" + mesh.name +
                "\" 不可读，无法生成平滑法线，硬边处可能出现接缝。开启模型 Read/Write 可改善。", context);
        }

        private static void WarnLargeMesh(Mesh mesh, UnityEngine.Object context)
        {
            if (!WarnedLarge.Add(mesh.GetInstanceID()))
            {
                return;
            }
            Debug.LogWarning("OutLine：网格 \"" + mesh.name + "\" 有 " + mesh.vertexCount +
                " 个顶点，首次启用会同步计算平滑法线。建议在组件上开启 Precompute Outline。", context);
        }


        private bool CreateProxy(Mesh mesh, SkinnedMeshRenderer skin)
        {
            proxyKind = skin != null ? OutlineProxyKind.Skinned : OutlineProxyKind.Mesh;
            proxyMesh = mesh;

            // 代理来自对象池：频繁开关高亮时不再反复创建/销毁 GameObject。
            proxyObject = OutlineProxyPool.Rent(proxyKind, owner, source.transform);
            if (proxyObject == null)
            {
                // 目标正在激活/停用，Unity 拒绝了挂接；由调用方下一帧重试。
                return false;
            }
            proxyObject.name = source.name + " (OutLine)";

            if (skin != null)
            {
                var proxy = proxyObject.GetComponent<SkinnedMeshRenderer>();
                proxy.sharedMesh = mesh;
                proxy.bones = skin.bones;
                proxy.rootBone = skin.rootBone;
                proxy.localBounds = Inflate(skin.localBounds);
                proxy.updateWhenOffscreen = true;
                proxy.quality = skin.quality;
                proxyRenderer = proxy;
            }
            else
            {
                proxyObject.GetComponent<MeshFilter>().sharedMesh = mesh;
                proxyRenderer = proxyObject.GetComponent<MeshRenderer>();
            }

            // 描边只是叠加效果：不投影、不接收光照探针、不产生运动矢量。
            ConfigureRenderer(proxyRenderer);

            // URP 下模板与描边拆成两个 Renderer；Fill 是对象池建好的子物体。
            if (OutlineMaterials.IsURP)
            {
                Transform fill = proxyObject.transform.Find("Fill");
                fillRenderer = fill != null ? fill.GetComponent<Renderer>() : null;
                if (fillRenderer != null)
                {
                    if (skin != null)
                    {
                        var fillSkin = fillRenderer as SkinnedMeshRenderer;
                        fillSkin.sharedMesh = mesh;
                        fillSkin.bones = skin.bones;
                        fillSkin.rootBone = skin.rootBone;
                        fillSkin.localBounds = Inflate(skin.localBounds);
                        fillSkin.updateWhenOffscreen = true;
                        fillSkin.quality = skin.quality;
                    }
                    else
                    {
                        fill.GetComponent<MeshFilter>().sharedMesh = mesh;
                    }
                    ConfigureRenderer(fillRenderer);
                }
            }
            return true;
        }

        private static void ConfigureRenderer(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        /// <summary>按当前模式、颜色、宽度与宽度单位刷新材质和逐对象属性。</summary>
        internal void ApplyAppearance(Color color, float width, float widthMode)
        {
            if (proxyRenderer == null)
            {
                return;
            }

            bool urp = OutlineMaterials.IsURP;
            Material material = urp ? OutlineMaterials.GetMask(owner.OutlineMode) : OutlineMaterials.Get(owner.OutlineMode);
            if (material == null)
            {
                proxyRenderer.enabled = false;
                if (fillRenderer != null)
                {
                    fillRenderer.enabled = false;
                }
                return;
            }
            proxyRenderer.enabled = true;

            int slots = proxyMesh != null ? Mathf.Max(1, proxyMesh.subMeshCount) : 1;
            if (slotMaterials == null || slotMaterials.Length != slots || slotMaterials[0] != material)
            {
                slotMaterials = new Material[slots];
                for (int i = 0; i < slots; i++)
                {
                    slotMaterials[i] = material;
                }
                proxyRenderer.sharedMaterials = slotMaterials;
            }

            block.SetColor(ColorId, color);
            block.SetFloat(WidthId, width);
            block.SetFloat(WidthModeId, widthMode);
            proxyRenderer.SetPropertyBlock(block);

            if (!urp || fillRenderer == null)
            {
                return;
            }

            Material fill = OutlineMaterials.GetFill(owner.OutlineMode);
            if (fill == null)
            {
                fillRenderer.enabled = false;
                return;
            }
            fillRenderer.enabled = true;
            if (fillSlotMaterials == null || fillSlotMaterials.Length != slots || fillSlotMaterials[0] != fill)
            {
                fillSlotMaterials = new Material[slots];
                for (int i = 0; i < slots; i++)
                {
                    fillSlotMaterials[i] = fill;
                }
                fillRenderer.sharedMaterials = fillSlotMaterials;
            }
            fillRenderer.SetPropertyBlock(block);
        }

        /// <summary>蒙皮网格的表情权重需要逐帧同步到代理，否则描边会和模型变形对不上。</summary>
        internal void SyncPose()
        {
            if (blendShapeWeights == null || sourceSkin == null || proxyRenderer == null)
            {
                return;
            }

            var root = proxyRenderer as SkinnedMeshRenderer;
            var fill = fillRenderer as SkinnedMeshRenderer;
            int count = Mathf.Min(blendShapeWeights.Length, sourceSkin.sharedMesh != null
                ? sourceSkin.sharedMesh.blendShapeCount
                : blendShapeWeights.Length);
            for (int i = 0; i < count; i++)
            {
                blendShapeWeights[i] = sourceSkin.GetBlendShapeWeight(i);
                if (root != null && i < root.sharedMesh.blendShapeCount)
                {
                    root.SetBlendShapeWeight(i, blendShapeWeights[i]);
                }
                if (fill != null && i < fill.sharedMesh.blendShapeCount)
                {
                    fill.SetBlendShapeWeight(i, blendShapeWeights[i]);
                }
            }
        }

        internal void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;

            if (proxyObject != null)
            {
                // 目标正在停用/销毁时不要跨层级搬运代理：Unity 会拒绝 SetParent 并报错，
                // 这种情况直接销毁，避免池里留下挂在待销毁层级下的对象。
                bool allowPool = source != null && source.gameObject.activeInHierarchy;
                OutlineProxyPool.Return(proxyObject, proxyKind, allowPool);
                proxyObject = null;
                proxyRenderer = null;
                fillRenderer = null;
            }

            slotMaterials = null;
            fillSlotMaterials = null;
            proxyMesh = null;
            if (ownsMeshCopy)
            {
                OutlineMeshCache.Release(sourceMesh);
            }
        }

        private static Bounds Inflate(Bounds bounds)
        {
            bounds.Expand(Mathf.Max(1f, bounds.size.magnitude * 0.2f));
            return bounds;
        }
    }
}
