using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UPandaGF.Gameplay;

namespace UPandaGF.Tests
{
    /// <summary>验证描边组件在运行时对网格、材质和层级关系的处理。</summary>
    public class QuickOutlinePlayModeTests
    {
        private readonly List<Object> fixtures = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // 先销毁使用网格的物体，再销毁测试创建的源网格。
            for (int i = fixtures.Count - 1; i >= 0; i--)
            {
                Object.Destroy(fixtures[i]);
            }
            fixtures.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator SharesPreparedMeshAndRestoresOriginalOnDisable()
        {
            var source = CreateMesh();
            var material = Resources.Load<Material>("Materials/OutlineMask");
            var first = CreateRenderer(source, material);
            var second = CreateRenderer(source, material);
            var plain = CreateRenderer(source, material);
            var firstOutline = first.gameObject.AddComponent<OutLine>();
            var secondOutline = second.gameObject.AddComponent<OutLine>();
            var firstFilter = first.GetComponent<MeshFilter>();
            var secondFilter = second.GetComponent<MeshFilter>();
            var prepared = firstFilter.sharedMesh;

            // 两个描边组件共享处理后的副本，普通 Renderer 继续使用原网格。
            Assert.That(prepared, Is.Not.EqualTo(source));
            Assert.That(secondFilter.sharedMesh, Is.EqualTo(prepared));
            Assert.That(prepared.HasVertexAttribute(VertexAttribute.TexCoord3), Is.True);
            Assert.That(plain.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(source));
            Assert.That(source.HasVertexAttribute(VertexAttribute.TexCoord3), Is.False);
            Assert.That(first.sharedMaterials, Has.Length.EqualTo(3));
            Assert.That(first.sharedMaterials[0], Is.EqualTo(material));

            firstOutline.enabled = false;
            Assert.That(firstFilter.sharedMesh, Is.EqualTo(source));
            Assert.That(first.sharedMaterials, Is.EqualTo(new[] { material }));
            Assert.That(secondFilter.sharedMesh, Is.EqualTo(prepared));

            // 最后一个使用者销毁后，网格副本才会释放。
            Object.Destroy(firstOutline);
            yield return null;
            Assert.That(prepared != null, Is.True);

            Object.Destroy(secondOutline);
            yield return null;
            Assert.That(secondFilter.sharedMesh, Is.EqualTo(source));
            Assert.That(second.sharedMaterials, Is.EqualTo(new[] { material }));
            yield return null;
            Assert.That(prepared == null, Is.True);
        }

        [UnityTest]
        public IEnumerator DiscoversNewChildAndHandsItToNearestEnabledOutline()
        {
            var source = CreateMesh();
            var material = Resources.Load<Material>("Materials/OutlineMask");
            var root = new GameObject("Outline root");
            fixtures.Add(root);
            var parent = root.AddComponent<OutLine>();
            parent.OutlineColor = Color.red;
            parent.CanFlash = false;

            var child = CreateRenderer(source, material);
            child.transform.SetParent(root.transform, false);
            yield return null;
            Assert.That(child.sharedMaterials, Has.Length.EqualTo(3));
            Assert.That(child.sharedMaterials[2].GetColor("_OutlineColor"), Is.EqualTo(Color.red));

            // 子级组件优先接管同一 Renderer，禁用后交回父级。
            var nested = child.gameObject.AddComponent<OutLine>();
            nested.OutlineColor = Color.green;
            nested.CanFlash = false;
            yield return null;
            Assert.That(child.sharedMaterials, Has.Length.EqualTo(3));
            Assert.That(child.sharedMaterials[2].GetColor("_OutlineColor"), Is.EqualTo(Color.green));

            nested.enabled = false;
            yield return null;
            Assert.That(child.sharedMaterials, Has.Length.EqualTo(3));
            Assert.That(child.sharedMaterials[2].GetColor("_OutlineColor"), Is.EqualTo(Color.red));

            child.transform.SetParent(null);
            yield return null;
            Assert.That(child.GetComponent<MeshFilter>().sharedMesh, Is.EqualTo(source));
            Assert.That(child.sharedMaterials, Is.EqualTo(new[] { material }));
        }

        [UnityTest]
        public IEnumerator PreservesOriginalMaterialLayersAndUv4OnMultiSubmesh()
        {
            var source = CreateMesh(twoSubmeshes: true);
            var uv4 = new[]
            {
                new Vector2(0.1f, 0.2f), new Vector2(0.3f, 0.4f),
                new Vector2(0.5f, 0.6f), new Vector2(0.7f, 0.8f)
            };
            source.uv4 = uv4;
            var mask = Resources.Load<Material>("Materials/OutlineMask");
            var fill = Resources.Load<Material>("Materials/OutlineFill");
            var original = new[] { mask, fill, mask };
            var renderer = CreateRenderer(source, original);
            var outline = renderer.gameObject.AddComponent<OutLine>();
            var filter = renderer.GetComponent<MeshFilter>();
            var prepared = filter.sharedMesh;

            // 第三个槽复用最后一个原子网格，第四个槽使用合并后的描边几何。
            Assert.That(prepared, Is.Not.EqualTo(source));
            Assert.That(prepared.subMeshCount, Is.EqualTo(4));
            Assert.That(prepared.GetTriangles(0), Is.EqualTo(source.GetTriangles(0)));
            Assert.That(prepared.GetTriangles(1), Is.EqualTo(source.GetTriangles(1)));
            Assert.That(prepared.GetTriangles(2), Is.EqualTo(source.GetTriangles(1)));
            Assert.That(prepared.GetTriangles(3), Is.EqualTo(source.triangles));
            Assert.That(source.subMeshCount, Is.EqualTo(2));
            Assert.That(source.uv4, Is.EqualTo(uv4));
            Assert.That(prepared.uv4, Is.EqualTo(uv4));
            Assert.That(renderer.sharedMaterials, Has.Length.EqualTo(5));
            for (int i = 0; i < original.Length; i++)
            {
                Assert.That(renderer.sharedMaterials[i], Is.EqualTo(original[i]));
            }

            outline.enabled = false;
            Assert.That(filter.sharedMesh, Is.EqualTo(source));
            Assert.That(renderer.sharedMaterials, Is.EqualTo(original));
            yield return null;
        }

        private Mesh CreateMesh(bool twoSubmeshes = false)
        {
            var mesh = new Mesh { name = "QuickOutline test mesh" };
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

        private MeshRenderer CreateRenderer(Mesh mesh, params Material[] materials)
        {
            var gameObject = new GameObject("Outline target");
            fixtures.Add(gameObject);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            return renderer;
        }
    }
}
