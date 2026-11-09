using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace UPandaGF.Gameplay
{
    /// <summary>
    /// 一个被描边目标（Renderer）的运行时绑定。
    /// 它不修改目标本身的材质与网格，而是在目标下创建一个隐藏的代理 Renderer：
    /// 代理使用同一份几何（可读网格取一份合并+平滑法线的副本，不可读网格直接复用），
    /// 材质是五种模式共享的描边材质，颜色/宽度通过 MaterialPropertyBlock 逐对象覆盖。
    /// 禁用组件时整块代理被销毁，目标对象的资源保持原样。
    /// </summary>
    internal sealed class OutlineTarget
    {
        private static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");

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
            if (!target.CreateProxy(proxyMesh, skin))
            {
                target.Dispose();
                return null;
            }
            return target;
        }

        private bool CreateProxy(Mesh proxyMesh, SkinnedMeshRenderer skin)
        {
            this.proxyMesh = proxyMesh;
            proxyObject = new GameObject(source.name + " (OutLine)");
            proxyObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            proxyObject.transform.SetParent(source.transform, false);
            proxyObject.transform.localPosition = Vector3.zero;
            proxyObject.transform.localRotation = Quaternion.identity;
            proxyObject.transform.localScale = Vector3.one;

            OutlineProxy marker = proxyObject.AddComponent<OutlineProxy>();
            marker.Owner = owner;

            if (skin != null)
            {
                SkinnedMeshRenderer proxy = proxyObject.AddComponent<SkinnedMeshRenderer>();
                proxy.sharedMesh = proxyMesh;
                proxy.bones = skin.bones;
                proxy.rootBone = skin.rootBone;
                proxy.localBounds = Inflate(skin.localBounds);
                proxy.updateWhenOffscreen = true;
                proxy.quality = skin.quality;
                proxyRenderer = proxy;
            }
            else
            {
                MeshFilter proxyFilter = proxyObject.AddComponent<MeshFilter>();
                proxyFilter.sharedMesh = proxyMesh;
                proxyRenderer = proxyObject.AddComponent<MeshRenderer>();
            }

            // 描边只是叠加效果：不投影、不接收光照探针、不产生运动矢量。
            proxyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            proxyRenderer.receiveShadows = false;
            proxyRenderer.lightProbeUsage = LightProbeUsage.Off;
            proxyRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            proxyRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return true;
        }

        /// <summary>按当前模式和颜色刷新材质与逐对象属性。</summary>
        internal void ApplyAppearance(Color color, float width)
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
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(proxyObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(proxyObject);
                }
                proxyObject = null;
                proxyRenderer = null;
            }

            slotMaterials = null;
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
