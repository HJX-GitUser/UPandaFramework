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

        private readonly OutLine owner;
        private readonly Renderer source;
        private readonly SkinnedMeshRenderer sourceSkin;
        private readonly Mesh sourceMesh;
        private readonly bool ownsMeshCopy;
        private readonly float[] blendShapeWeights;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        private GameObject proxyObject;
        private Renderer proxyRenderer;
        private Mesh proxyMesh;
        private OutlineProxyKind proxyKind;
        private Material[] slotMaterials;
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

        /// <summary>为 Renderer 建立代理；不支持或缺少网格时返回 null。</summary>
        internal static OutlineTarget Bind(OutLine owner, Renderer source, List<Vector3> bakedNormals)
        {
            if (owner == null || source == null)
            {
                return null;
            }

            var skin = source as SkinnedMeshRenderer;
            MeshFilter filter = null;
            Mesh mesh;
            if (skin != null)
            {
                mesh = skin.sharedMesh;
            }
            else
            {
                if (!source.TryGetComponent(out filter))
                {
                    return null;
                }
                mesh = filter.sharedMesh;
            }
            if (mesh == null || mesh.subMeshCount == 0)
            {
                return null;
            }

            // 可读网格取合并副本，顺带写入平滑法线；不可读网格直接复用原网格。
            bool copy = mesh.isReadable;
            Mesh proxyMesh = copy ? OutlineMeshCache.Acquire(mesh, bakedNormals) : mesh;
            if (proxyMesh == null)
            {
                return null;
            }

            var target = new OutlineTarget(owner, source, skin, mesh, copy);
            target.CreateProxy(proxyMesh, skin);
            return target;
        }

        private void CreateProxy(Mesh mesh, SkinnedMeshRenderer skin)
        {
            proxyKind = skin != null ? OutlineProxyKind.Skinned : OutlineProxyKind.Mesh;
            proxyMesh = mesh;

            // 代理来自对象池：频繁开关高亮时不再反复创建/销毁 GameObject。
            proxyObject = OutlineProxyPool.Rent(proxyKind, owner, source.transform);
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
            proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            proxyRenderer.receiveShadows = false;
            proxyRenderer.lightProbeUsage = LightProbeUsage.Off;
            proxyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            proxyRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        /// <summary>按当前模式、颜色、宽度与宽度单位刷新材质和逐对象属性。</summary>
        internal void ApplyAppearance(Color color, float width, float widthMode)
        {
            if (proxyRenderer == null)
            {
                return;
            }

            Material material = OutlineMaterials.Get(owner.OutlineMode);
            if (material == null)
            {
                proxyRenderer.enabled = false;
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
        }

        /// <summary>蒙皮网格的表情权重需要逐帧同步到代理，否则描边会和模型变形对不上。</summary>
        internal void SyncPose()
        {
            if (blendShapeWeights == null || sourceSkin == null || proxyRenderer == null)
            {
                return;
            }
            var proxy = proxyRenderer as SkinnedMeshRenderer;
            if (proxy == null || proxy.sharedMesh == null)
            {
                return;
            }

            int count = Mathf.Min(blendShapeWeights.Length, proxy.sharedMesh.blendShapeCount);
            for (int i = 0; i < count; i++)
            {
                float weight = sourceSkin.GetBlendShapeWeight(i);
                if (Mathf.Approximately(weight, blendShapeWeights[i]))
                {
                    continue;
                }
                blendShapeWeights[i] = weight;
                proxy.SetBlendShapeWeight(i, weight);
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
                OutlineProxyPool.Return(proxyObject, proxyKind);
                proxyObject = null;
                proxyRenderer = null;
            }

            slotMaterials = null;
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
