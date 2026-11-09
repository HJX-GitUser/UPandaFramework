using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UPandaGF.Gameplay;

namespace UPandaGF.Tests
{
    /// <summary>
    /// 验证描边组件的新实现：隐藏代理渲染、网格副本共享、层级就近接管，
    /// 以及「业务对象的网格与材质保持原样」这一关键约束。
    /// </summary>
    public class OutlinePlayModeTests
    {
        private readonly List<Object> fixtures = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            for (int i = fixtures.Count - 1; i >= 0; i--)
            {
                Object.Destroy(fixtures[i]);
            }
            fixtures.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator ProxyMergesSubmeshesAndLeavesSourceUntouched()
        {
            Mesh source = CreateMesh(twoSubmeshes: true);
            MeshRenderer renderer = CreateRenderer(source);
            OutLine outline = renderer.gameObject.AddComponent<OutLine>();
            outline.CanFlash = false;
            yield return null;

            Renderer proxy = FindProxy(renderer);
            Assert.That(proxy, Is.Not.Null);

            // 业务对象本身完全不被改动。
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(source));
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));

            Mesh proxyMesh = proxy.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(proxyMesh, Is.Not.EqualTo(source));
            Assert.That(proxyMesh.subMeshCount, Is.EqualTo(1), "多子网格应合并为一个描边绘制单元");
            Assert.That(proxyMesh.vertexCount, Is.EqualTo(source.vertexCount));
            Assert.That(proxyMesh.HasVertexAttribute(VertexAttribute.TexCoord3), Is.False,
                "平滑法线写进 NORMAL，不再占用 UV3");
            Assert.That(proxy.sharedMaterials.Length, Is.EqualTo(1));
            Assert.That(proxy.sharedMaterial.shader.name, Is.EqualTo(OutLine.ShaderName));
        }

        [UnityTest]
        public IEnumerator AppliesColorAndWidthThroughPropertyBlock()
        {
            Mesh source = CreateMesh();
            MeshRenderer renderer = CreateRenderer(source);
            OutLine outline = renderer.gameObject.AddComponent<OutLine>();
            outline.CanFlash = false;
            outline.OutlineColor = Color.red;
            outline.OutlineWidth = 7f;
            yield return null;

            Renderer proxy = FindProxy(renderer);
            var block = new MaterialPropertyBlock();
            proxy.GetPropertyBlock(block);
            Assert.That(block.GetColor("_OutlineColor"), Is.EqualTo(Color.red));
            Assert.That(block.GetFloat("_OutlineWidth"), Is.EqualTo(7f));

            // 共享材质保持原样：颜色与宽度不在材质上，避免每个对象实例化一份。
            Assert.That(proxy.sharedMaterial.GetColor("_OutlineColor"), Is.EqualTo(Color.white));
        }

        [UnityTest]
        public IEnumerator SilhouetteModeZeroesWidthAndKeepsSharedMaterialState()
        {
            Mesh source = CreateMesh();
            MeshRenderer renderer = CreateRenderer(source);
            OutLine outline = renderer.gameObject.AddComponent<OutLine>();
            outline.CanFlash = false;
            outline.OutlineMode = OutLine.描边模式.仅剪影;
            yield return null;

            Renderer proxy = FindProxy(renderer);
            var block = new MaterialPropertyBlock();
            proxy.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_OutlineWidth"), Is.EqualTo(0f));

            Assert.That(proxy.sharedMaterial.GetFloat("_MaskZTest"),
                Is.EqualTo((float)CompareFunction.LessEqual));
            Assert.That(proxy.sharedMaterial.GetFloat("_FillZTest"),
                Is.EqualTo((float)CompareFunction.Greater));
        }

        [UnityTest]
        public IEnumerator SharesProxyMeshAndReleasesItWithLastUser()
        {
            Mesh source = CreateMesh();
            MeshRenderer first = CreateRenderer(source);
            MeshRenderer second = CreateRenderer(source);
            OutLine firstOutline = first.gameObject.AddComponent<OutLine>();
            OutLine secondOutline = second.gameObject.AddComponent<OutLine>();

            Mesh shared = FindProxy(first).GetComponent<MeshFilter>().sharedMesh;
            Assert.That(FindProxy(second).GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(shared));

            Object.Destroy(firstOutline);
            yield return null;
            Assert.That(shared != null, Is.True, "仍有使用者时网格副本不应销毁");
            Assert.That(FindProxy(second), Is.Not.Null);

            Object.Destroy(secondOutline);
            yield return null;
            Assert.That(shared == null, Is.True, "最后一个使用者释放后网格副本应销毁");
        }

        [UnityTest]
        public IEnumerator NearestComponentTakesOverRenderers()
        {
            Mesh source = CreateMesh();
            var root = new GameObject("Outline root");
            fixtures.Add(root);
            OutLine parentOutline = root.AddComponent<OutLine>();
            parentOutline.CanFlash = false;

            MeshRenderer child = CreateRenderer(source);
            child.transform.SetParent(root.transform, false);
            yield return null;
            Assert.That(parentOutline.TargetCount, Is.EqualTo(1));
            Assert.That(FindProxy(child), Is.Not.Null);

            // 子节点上的组件接管同一个 Renderer，父级不再重复描边。
            OutLine nested = child.gameObject.AddComponent<OutLine>();
            nested.CanFlash = false;
            yield return null;
            Assert.That(nested.TargetCount, Is.EqualTo(1));
            Assert.That(parentOutline.TargetCount, Is.EqualTo(0));

            // 禁用子级后交回父级。
            nested.enabled = false;
            yield return null;
            Assert.That(parentOutline.TargetCount, Is.EqualTo(1));
            Assert.That(FindProxy(child), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator NonReadableMeshReusesSourceGeometry()
        {
            Mesh source = CreateMesh(twoSubmeshes: true);
            source.UploadMeshData(true);
            Assert.That(source.isReadable, Is.False);

            MeshRenderer renderer = CreateRenderer(source);
            OutLine outline = renderer.gameObject.AddComponent<OutLine>();
            outline.CanFlash = false;
            yield return null;

            Renderer proxy = FindProxy(renderer);
            Assert.That(proxy, Is.Not.Null);
            Assert.That(proxy.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(source),
                "不可读网格无法生成副本，直接复用原始几何");
            Assert.That(proxy.sharedMaterials.Length, Is.EqualTo(2), "每个子网格各取一份描边材质");
        }

        [UnityTest]
        public IEnumerator DisablingRemovesProxy()
        {
            Mesh source = CreateMesh();
            MeshRenderer renderer = CreateRenderer(source);
            OutLine outline = renderer.gameObject.AddComponent<OutLine>();
            outline.CanFlash = false;
            yield return null;
            Assert.That(FindProxy(renderer), Is.Not.Null);

            outline.enabled = false;
            yield return null;
            Assert.That(FindProxy(renderer), Is.Null);
            Assert.That(renderer.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(source));
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PixelWidthUnitSwitchesShaderMode()
        {
            Mesh source = CreateMesh();
            MeshRenderer renderer = CreateRenderer(source);
            OutLine outline = renderer.gameObject.AddComponent<OutLine>();
            outline.CanFlash = false;
            outline.OutlineWidth = 6f;
            outline.OutlineWidthUnit = OutLine.宽度单位.像素;
            yield return null;

            Renderer proxy = FindProxy(renderer);
            var block = new MaterialPropertyBlock();
            proxy.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_WidthMode"), Is.EqualTo(1f));
            Assert.That(block.GetFloat("_OutlineWidth"), Is.EqualTo(6f));

            outline.OutlineWidthUnit = OutLine.宽度单位.屏幕相对;
            yield return null;
            proxy.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_WidthMode"), Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator ProxyIsReusedFromPoolWhenToggled()
        {
            Mesh source = CreateMesh();
            MeshRenderer renderer = CreateRenderer(source);
            OutLine outline = renderer.gameObject.AddComponent<OutLine>();
            outline.CanFlash = false;
            yield return null;

            int proxyId = FindProxy(renderer).gameObject.GetInstanceID();

            outline.enabled = false;
            yield return null;
            Assert.That(FindProxy(renderer), Is.Null, "禁用后代理应脱离目标并回到对象池");

            outline.enabled = true;
            yield return null;
            Renderer reused = FindProxy(renderer);
            Assert.That(reused, Is.Not.Null);
            Assert.That(reused.gameObject.GetInstanceID(), Is.EqualTo(proxyId), "重新启用应复用池中的代理");
        }

        private static Renderer FindProxy(Renderer source)
        {
            Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer candidate = renderers[i];
                if (candidate != source && candidate.GetComponent<OutlineProxy>() != null)
                {
                    return candidate;
                }
            }
            return null;
        }

        private Mesh CreateMesh(bool twoSubmeshes = false)
        {
            var mesh = new Mesh { name = "Outline test mesh" };
            fixtures.Add(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.subMeshCount = twoSubmeshes ? 2 : 1;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            if (twoSubmeshes)
            {
                mesh.SetTriangles(new[] { 2, 1, 3 }, 1);
            }
            mesh.RecalculateNormals();
            return mesh;
        }

        private MeshRenderer CreateRenderer(Mesh mesh)
        {
            var gameObject = new GameObject("Outline target");
            fixtures.Add(gameObject);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            var material = new Material(Shader.Find("Standard"));
            fixtures.Add(material);

            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
