//
//  Outline.cs
//  QuickOutline
//
//  Created by Chris Nolet on 3/30/18.
//  Copyright © 2018 Chris Nolet. All rights reserved.
//
// =============================================================================
//  一、这个脚本做什么
// =============================================================================
//  给「自身 + 所有子物体」加描边。挂在物体根节点上，运行时自动完成四件事：
//
//    1. 从 Resources 加载并实例化两个描边材质（Materials/OutlineMask、Materials/OutlineFill）
//       用 Instantiate 是为了每个物体一份独立材质、互不干扰，OnDestroy 时销毁
//    2. 为每个网格计算「平滑法线」并写入 UV3（TEXCOORD3）
//       硬边模型（立方体等）在同一位置有多条不同法线，直接沿法线外扩会把描边撕开，
//       所以按「顶点位置分组取平均」得到一条统一法线（见 SmoothNormals）
//    3. 把 Mask / Fill 两个材质追加到每个 Renderer 的材质列表末尾
//       同时用 CombineSubmeshes 追加一个「重复子网格」，让这两个材质有几何可以绘制
//    4. needsUpdate 时刷新材质参数：_ZTest（模式）/ _OutlineColor / _OutlineWidth
//
//  二、描边原理（Shader 侧，详见 Shaders/OutlineFill.shader 头部注释）
//    ① OutlineMask（Queue = Transparent+100）：ColorMask 0 + Stencil{Ref 1, Pass Replace}
//       → 把「物体本体」覆盖到的像素的模板值写成 1
//    ② OutlineFill（Queue = Transparent+110）：顶点沿视图空间法线外扩 + Stencil{Ref 1, Comp NotEqual}
//       → 只在本体之外着色；外扩轮廓被本体「挖空」，剩下的一圈就是描边
//
//  三、五种描边模式（OutlineMode）
//    不在 Shader 里分支，而是用两个 Pass 的 _ZTest 组合出来（见 UpdateMaterialProperties）：
//
//      OutlineAll             mask=Always    fill=Always     描边始终完整显示，无视遮挡
//      OutlineVisible         mask=Always    fill=LessEqual  描边会被其它物体遮挡
//      OutlineHidden          mask=Always    fill=Greater    只在被其它物体挡住的地方显示描边
//      OutlineAndSilhouette   mask=LessEqual fill=Always     可见处正常描边 + 被遮挡处显示剪影
//      SilhouetteOnly         mask=LessEqual fill=Greater 且宽度=0 → 只画本体形状的透视剪影
//
//    （上表由代码中的 _ZTest 取值推导；mask=Always 表示无论是否被遮挡都写模板，
//      fill=LessEqual/Greater 表示外圈是否参与深度测试、以及"在前/在后"才绘制。）
// =============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UPandaGF;

// 同一物体上不允许挂多个描边组件（避免重复追加材质、描边叠加变粗）
[DisallowMultipleComponent]

public class OutDrawline : MonoBehaviour
{
    // 已处理过"平滑法线"的网格集合（静态共享）。
    // 关键点：平滑法线是直接写进 Mesh 资源的 UV3 的，同一个网格被多处引用时只需处理一次，
    // 否则会重复写入并造成不必要的开销。
    private static HashSet<Mesh> registeredMeshes = new HashSet<Mesh>();

    /// <summary>
    /// 描边模式。语义由两个 Pass 的深度测试组合决定（详见文件头注释）
    /// </summary>
    public enum Mode
    {
        OutlineAll,             // 描边始终显示（无视遮挡）
        OutlineVisible,         // 描边会被其它物体遮挡
        OutlineHidden,          // 只在本体被遮挡处显示描边
        OutlineAndSilhouette,   // 可见处描边 + 遮挡处剪影
        SilhouetteOnly          // 只显示本体形状的视角剪影（不扩边）
    }

    // ---------------------------------------------------------------------
    //  对外属性：外部改任意一个都会把 needsUpdate 置为 true，
    //  下一帧 Update() 里真正写进材质（避免在设置途中重复刷新材质）
    // ---------------------------------------------------------------------

    public Mode OutlineMode
    {
        get { return outlineMode; }
        set
        {
            outlineMode = value;
            needsUpdate = true;
        }
    }

    /// <summary>
    /// 描边颜色（作者设置的颜色）。
    /// 注意：呼吸闪烁（CanFlash）不再像以前那样每帧把 alpha 回写到这里，
    /// 而是在下发材质时用 flashAlpha 调制，因此外部设置的颜色不会被闪烁覆盖。
    /// </summary>
    public Color OutlineColor
    {
        get { return outlineColor; }
        set
        {
            outlineColor = value;
            needsUpdate = true;
        }
    }

    public float OutlineWidth
    {
        get { return outlineWidth; }
        set
        {
            outlineWidth = value;
            needsUpdate = true;
        }
    }

    /// <summary>
    /// 仅为在 Inspector 中序列化 List&lt;Vector3&gt; 而定义的可序列化包装类
    /// （Unity 不能直接序列化泛型 List 的字段数组）
    /// </summary>
    [Serializable]
    private class ListVector3
    {
        public List<Vector3> data;
    }

    [SerializeField]
    private Mode outlineMode;

    [SerializeField]
    private Color outlineColor = Color.white;

    // 描边宽度（0~200）。
    // 注：原实现在 Awake() 里有一行硬编码 outlineWidth = 4f，会无条件覆盖 Inspector 上的设置，
    //     已移除；现在 Inspector（或 OutlineWidth 属性）是唯一来源。
    [SerializeField, Range(0f, 200f)]
    private float outlineWidth = 4f;

    [Header("Optional")]

    //[SerializeField, Tooltip("Precompute enabled: Per-vertex calculations are performed in the editor and serialized with the object."
    //+ "Precompute disabled: Per-vertex calculations are performed at runtime in Awake(). This may cause a pause for large meshes.")]
    [SerializeField, Tooltip("预计算启用：在编辑器中执行逐顶点计算，并与对象序列化。预计算禁用：在运行时在Awake()中执行逐顶点计算。对于大型网格，这可能会导致暂停。")]
    private bool precomputeOutline;                     // 是否把平滑法线预计算并随对象序列化（大网格建议开启）

    [SerializeField, HideInInspector]
    private List<Mesh> bakeKeys = new List<Mesh>();     // 预计算结果：网格（与 bakeValues 一一对应）

    [SerializeField, HideInInspector]
    private List<ListVector3> bakeValues = new List<ListVector3>(); // 预计算结果：对应的平滑法线列表

    private Renderer[] renderers;                       // 自身及子物体上的所有 Renderer
    private Material outlineMaskMaterial;               // Mask 材质实例（写模板）
    private Material outlineFillMaterial;               // Fill 材质实例（画描边）

    private bool needsUpdate;                           // 材质参数脏标记：为 true 时下一帧刷新材质

    void Awake()
    {

        // Cache renderers 缓存自身与所有子物体的 Renderer
        renderers = GetComponentsInChildren<Renderer>();

        // Instantiate outline materials
        // 实例化两份材质（每个物体独立），从 Resources 目录加载
        outlineMaskMaterial = Instantiate(Resources.Load<Material>(@"Materials/OutlineMask"));
        outlineFillMaterial = Instantiate(Resources.Load<Material>(@"Materials/OutlineFill"));

        // 给实例改名，便于在 Profiler / Frame Debugger 里识别
        outlineMaskMaterial.name = "OutlineMask (Instance)";
        outlineFillMaterial.name = "OutlineFill (Instance)";

        // Retrieve or generate smooth normals 检索或生成平滑法线
        LoadSmoothNormals();
        // 立即应用材质属性（下一帧 Update 生效）
        needsUpdate = true;
    }

    void OnEnable()
    {
        foreach (var renderer in renderers)
        {

            // Append outline shaders
            // 把两个描边材质追加到材质列表末尾：
            // 渲染时第 i 个子网格用第 i 个材质，因此超出子网格数量的材质需要
            // CombineSubmeshes() 追加的"重复子网格"才能被绘制到
            var materials = renderer.sharedMaterials.ToList();

            materials.Add(outlineMaskMaterial);
            materials.Add(outlineFillMaterial);

            renderer.materials = materials.ToArray();
        }
    }

    void OnValidate()
    {

        // Update material properties 编辑器里改参数后立即刷新材质
        needsUpdate = true;

        // Clear cache when baking is disabled or corrupted
        // 关闭预计算、或缓存已损坏（键值数量不一致）时清空缓存
        if (!precomputeOutline && bakeKeys.Count != 0 || bakeKeys.Count != bakeValues.Count)
        {
            bakeKeys.Clear();
            bakeValues.Clear();
        }

        // Generate smooth normals when baking is enabled
        // 开启预计算且还没有缓存时，立刻在当前网格上计算并序列化
        if (precomputeOutline && bakeKeys.Count == 0)
        {
            Bake();
        }
    }
    public bool CanFlash = true;        // 是否让描边做呼吸闪烁
    float n = 2;                        // 闪烁方向：+2 渐亮 / -2 渐暗
    float colora;                       // 闪烁用的时间累计量（0~1 之间往复，越界即换向）
    float flashAlpha = 1f;              // 闪烁系数（0~1）：最终 alpha = outlineColor.a * flashAlpha
    void Update()
    {
        if (needsUpdate)
        {
            needsUpdate = false;

            UpdateMaterialProperties();
        }
        if (CanFlash)
        {
            // 呼吸闪烁：只调制 flashAlpha，不修改作者设置的 outlineColor
            if (colora < 0)
            {
                n = 2;
            }
            colora += Time.deltaTime * n * 3;
            if (colora > 1)
            {
                n = -2;
            }

            flashAlpha = Mathf.Clamp01(colora);

            // 直接改材质颜色（不用走 needsUpdate，避免每帧重新设置 _ZTest 等参数）
            if (outlineFillMaterial != null)
            {
                outlineFillMaterial.SetColor("_OutlineColor", GetRenderedColor());
            }
        }
        else if (flashAlpha < 1f)
        {
            // 关闭闪烁：恢复成作者设置的颜色（只做一次）
            flashAlpha = 1f;
            if (outlineFillMaterial != null)
            {
                outlineFillMaterial.SetColor("_OutlineColor", GetRenderedColor());
            }
        }
    }

    /// <summary>
    /// 实际下发给材质的颜色 = 作者设置的颜色 × 闪烁系数。
    /// 这样"闪烁"与"外部设置颜色"互不干扰：闪烁只调制亮度/透明度，不改写 outlineColor 本身。
    /// </summary>
    private Color GetRenderedColor()
    {
        Color c = outlineColor;
        c.a *= flashAlpha;
        return c;
    }

    void OnDisable()
    {
        foreach (var renderer in renderers)
        {

            // Remove outline shaders 从材质列表里移除两个描边材质
            var materials = renderer.sharedMaterials.ToList();

            materials.Remove(outlineMaskMaterial);
            materials.Remove(outlineFillMaterial);

            renderer.materials = materials.ToArray();
        }
    }

    void OnDestroy()
    {

        // Destroy material instances 销毁运行时实例化的两份材质，避免泄漏
        Destroy(outlineMaskMaterial);
        Destroy(outlineFillMaterial);
    }

    /// <summary>
    /// 预计算：为所有子物体的网格生成平滑法线，并序列化到 bakeKeys / bakeValues
    /// （<see cref="precomputeOutline"/> 为 true 时，在编辑器里由 OnValidate 调用一次，
    ///  之后运行时直接取用，省去 Awake 中的逐顶点计算）
    /// </summary>
    void Bake()
    {

        // Generate smooth normals for each mesh
        var bakedMeshes = new HashSet<Mesh>();

        foreach (var meshFilter in GetComponentsInChildren<MeshFilter>())
        {

            // Skip duplicates 同一网格只处理一次
            if (!bakedMeshes.Add(meshFilter.sharedMesh))
            {
                continue;
            }

            // Serialize smooth normals
            var smoothNormals = SmoothNormals(meshFilter.sharedMesh);

            bakeKeys.Add(meshFilter.sharedMesh);
            bakeValues.Add(new ListVector3() { data = smoothNormals });
        }
    }

    /// <summary>
    /// 取用或生成平滑法线，并写入网格的 UV3（TEXCOORD3）——着色器就是从 TEXCOORD3 读它来外扩顶点。
    /// 优先用预计算结果；未预计算则现场计算。
    /// </summary>
    void LoadSmoothNormals()
    {

        // Retrieve or generate smooth normals
        foreach (var meshFilter in GetComponentsInChildren<MeshFilter>())
        {

            // Skip if smooth normals have already been adopted
            // registeredMeshes 是静态的：同一网格资源被多个对象引用时只写一次
            if (!registeredMeshes.Add(meshFilter.sharedMesh))
            {
                continue;
            }

            // Retrieve or generate smooth normals
            // bakeKeys 里找得到就用预计算结果，否则现场计算
            var index = bakeKeys.IndexOf(meshFilter.sharedMesh);
            var smoothNormals = (index >= 0) ? bakeValues[index].data : SmoothNormals(meshFilter.sharedMesh);

            // Store smooth normals in UV3
            // 写入 UV3（即着色器里的 TEXCOORD3）
            // ⚠️ 注意：这里修改的是 sharedMesh（网格资源本身），编辑器里会弄脏该资源
            meshFilter.sharedMesh.SetUVs(3, smoothNormals);

            // Combine submeshes
            // 追加重复子网格，让末尾的两个描边材质有几何可画
            var renderer = meshFilter.GetComponent<Renderer>();

            if (renderer != null)
            {
                CombineSubmeshes(meshFilter.sharedMesh, renderer.sharedMaterials);
            }
        }

        // Clear UV3 on skinned mesh renderers
        // 蒙皮网格不能写平滑法线（顶点位置每帧变化），直接把 UV3 清零：
        // 着色器里 any(input.smoothNormal) 为 false，会自动回退到网格自带法线
        foreach (var skinnedMeshRenderer in GetComponentsInChildren<SkinnedMeshRenderer>())
        {

            // Skip if UV3 has already been reset
            if (!registeredMeshes.Add(skinnedMeshRenderer.sharedMesh))
            {
                continue;
            }

            // Clear UV3
            // 注：Mesh.uv4 这个属性对应的就是 UV3 通道（Unity 里 uv/uv2/uv3/uv4 为 0~3 号通道）
            skinnedMeshRenderer.sharedMesh.uv4 = new Vector2[skinnedMeshRenderer.sharedMesh.vertexCount];

            // Combine submeshes
            CombineSubmeshes(skinnedMeshRenderer.sharedMesh, skinnedMeshRenderer.sharedMaterials);
        }
    }

    /// <summary>
    /// 计算平滑法线：把「世界位置相同」的顶点分到一组，组内法线取平均后回写。
    /// 目的：硬边模型（一个位置有多条面法线）外扩时会裂开，用平均法线可以保证描边连续。
    /// </summary>
    /// <param name="mesh">目标网格（不会修改其原有法线，只产出新列表）</param>
    List<Vector3> SmoothNormals(Mesh mesh)
    {

        // Group vertices by location 按顶点坐标分组（坐标完全相同的顶点归为一组）
        var groups = mesh.vertices.Select((vertex, index) => new KeyValuePair<Vector3, int>(vertex, index)).GroupBy(pair => pair.Key);

        // Copy normals to a new list 先复制一份原法线（单点组保持原值）
        var smoothNormals = new List<Vector3>(mesh.normals);

        // Average normals for grouped vertices 对每组求平均法线
        foreach (var group in groups)
        {

            // Skip single vertices 只有一个顶点的组无需处理
            if (group.Count() == 1)
            {
                continue;
            }

            // Calculate the average normal 求和
            var smoothNormal = Vector3.zero;

            foreach (var pair in group)
            {
                smoothNormal += smoothNormals[pair.Value];
            }

            smoothNormal.Normalize();   // 归一化（向量单位化）

            // Assign smooth normal to each vertex 组内所有顶点统一使用这条平均法线
            foreach (var pair in group)
            {
                smoothNormals[pair.Value] = smoothNormal;
            }
        }

        return smoothNormals;
    }

    /// <summary>
    /// 追加一个「重复子网格」：把现有所有三角形复制到新增的子网格里。
    /// 因为 Unity 按"子网格索引 ↔ 材质索引"配对渲染，追加材质后必须有一个子网格去消费它，
    /// 否则新增的描边材质不会被绘制。
    /// </summary>
    /// <param name="mesh">目标网格</param>
    /// <param name="materials">该 Renderer 当前的材质数组</param>
    void CombineSubmeshes(Mesh mesh, Material[] materials)
    {

        // Skip meshes with a single submesh 只有单子网格时不需要合并
        if (mesh.subMeshCount == 1)
        {
            return;
        }

        // Skip if submesh count exceeds material count 子网格比材质还多，说明配置异常，直接跳过
        if (mesh.subMeshCount > materials.Length)
        {
            return;
        }

        // Append combined submesh 追加子网格并复制全部三角形
        mesh.subMeshCount++;
        mesh.SetTriangles(mesh.triangles, mesh.subMeshCount - 1);
    }
    /// <summary>
    /// 按当前模式把参数写进材质。五种模式的区别只有两处：
    ///   ① Mask / Fill 各自的 _ZTest（深度测试方式）
    ///   ② SilhouetteOnly 时把 _OutlineWidth 设为 0（不扩边，只留剪影）
    /// </summary>
    void UpdateMaterialProperties()
    {

        // Apply properties according to mode
        // 颜色对两种模式都是共用的（此处用 flashAlpha 调制后的颜色）
        outlineFillMaterial.SetColor("_OutlineColor", GetRenderedColor());

        switch (outlineMode)
        {
            case Mode.OutlineAll:
                // 两个 Pass 都不做深度测试 → 描边始终完整显示（无视遮挡）
                outlineMaskMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                outlineFillMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.OutlineVisible:
                // Mask 无视深度写模板；Fill 参与深度测试 → 描边会被其它物体挡住
                outlineMaskMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                outlineFillMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.OutlineHidden:
                // Fill 只在"已经比场景更远"的地方通过 → 只有本体被挡住时才露出描边
                outlineMaskMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                outlineFillMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Greater);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.OutlineAndSilhouette:
                // Mask 只在通过深度测试（真正可见）处写模板 → 被遮挡处不"挖洞"，
                // 于是外圈在遮挡处保留下来，形成剪影效果
                outlineMaskMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                outlineFillMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                outlineFillMaterial.SetFloat("_OutlineWidth", outlineWidth);
                break;

            case Mode.SilhouetteOnly:
                // 与上一种相同的模板布局，但宽度为 0：不扩边，只剩本体形状的剪影
                outlineMaskMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);
                outlineFillMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Greater);
                outlineFillMaterial.SetFloat("_OutlineWidth", 0f);
                break;
        }
    }
}
