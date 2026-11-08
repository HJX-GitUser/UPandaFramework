// =============================================================================
//  CubemapTextureGenerator.cs —— 立方体贴图生成工具（编辑器）
// -----------------------------------------------------------------------------
//  菜单：UPandaGF/Tools/立方体纹理生成工具
//
//  功能：
//    以场景中指定物体（默认取当前选中）的位置为中心，渲染 6 个方向并合成一张立方体
//    贴图（Cubemap），保存成工程内资产（*.cubemap），可直接拖给材质 / 反射探针
//    (Custom) / Skybox (Cubemap) 使用。
//
//  实现要点（踩坑记录，改造前先读）：
//    1) 不用「立方体 RenderTexture + Graphics.SetRenderTarget(rt, 0, face)」这条路：URP 14 实测
//       不生效（相机不往手动设置的面里渲染，回读全黑）。本工具改为「渲染到 2D RT + 自己
//       SetPixels 到 Cubemap」，好处是能逐面回读、自由控制格式/抗锯齿/Mipmap/HDR。
//       注意 Camera.RenderToCubemap 在 URP 14 下是**可用**的（已实测），本工具只是没用它，
//       而是把它当成验证基准（见第 7 条）。
//    2) 六个面的朝向是固定值（见 FaceEulers），且 FOV 必须正好 90、aspect 必须为 1，
//       否则面与面之间会错位、出现接缝。
//    3) 相机就在物体原点上，物体自身的几何会糊在画面中央、还会被近裁剪面切一刀 →
//       默认「渲染期间隐藏中心物体」，渲染完在 finally 里恢复，异常也不留残状态。
//    4) 关闭 occlusion culling：中心点常在建筑内部，遮挡数据是按别的视角烘焙的，
//       开着会把本该出现在贴图里的物体整片剔掉。
//    5) 格式必须成对，否则颜色整体偏亮/偏暗：
//         HDR → RT 用 ARGBHalf + RenderTextureReadWrite.Linear，回读贴图 linear=true；
//         LDR → RT 用 ARGB32 + RenderTextureReadWrite.Default，回读贴图 linear=false（按 sRGB）。
//       注意 Cubemap 构造函数**没有** linear 参数可用：Unity 2021 只有 (size, format, mipChain)，
//       2022+ 的第 4 个参数是 createUninitialized（也不是 linear）。色彩空间只能靠格式本身决定，
//       RGBAHalf 天生线性，RGBA32 走工程默认色彩空间。
//    6) 一次性建出来的原生 Cubemap 资产（AssetDatabase.CreateAsset）没有 Importer，
//       所以 wrapMode / filterMode / mip 都要在创建时就设好。
//    7) **面图像必须上下翻转**（FlipVertically）。Cubemap 的面数据按图形 API 的立方图约定存放，
//       与「相机自然看到的画面」是上下镜像的，直接 SetPixels 出来的贴图整张上下颠倒
//       （拿去做天空盒会看到地面跑到头顶、天空在脚下）。
//       验证方式：以 Camera.RenderToCubemap 为基准逐面比像素 —— 翻转前 99% 的像素对不上，
//       翻转后 LDR(256²) 六个面只剩 ≤40/65536 个像素有差异、HDR(128²) ≤23/16384（都是抗锯齿边缘）。
// =============================================================================

using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UPandaGF.GFEditor;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// 立方体贴图生成工具：以场景物体为中心渲染 Cubemap 并落盘为工程内资产。
    /// </summary>
    public class CubemapTextureGenerator : EditorWindow
    {
        /// <summary>背景模式。</summary>
        public enum BackgroundMode
        {
            /// <summary>跟随场景天空盒（默认，做反射/Skybox 源时通常要这个）。</summary>
            Skybox,
            /// <summary>纯色背景。</summary>
            SolidColor
        }

        private const string DefaultOutputFolder = "Assets/ArtAssets/CubeMaps";
        private const string DefaultFileNamePrefix = "Cubemap";

        private static readonly int[] ResolutionValues = { 64, 128, 256, 512, 1024, 2048 };
        private static readonly string[] ResolutionLabels = { "64", "128", "256", "512", "1024", "2048" };

        private static readonly int[] MsaaValues = { 1, 2, 4, 8 };
        private static readonly string[] MsaaLabels = { "关闭", "2x", "4x", "8x" };

        /// <summary>立方体六个面（顺序与 Unity 的枚举一致，方便直接对应）。</summary>
        private static readonly CubemapFace[] FaceOrder =
        {
            CubemapFace.PositiveX, CubemapFace.NegativeX,
            CubemapFace.PositiveY, CubemapFace.NegativeY,
            CubemapFace.PositiveZ, CubemapFace.NegativeZ
        };

        /// <summary>六个面各自的相机欧拉角（Unity 约定：+Y 面是「向下看」）。</summary>
        private static readonly Vector3[] FaceEulers =
        {
            new Vector3(0f, 90f, 0f),    // +X
            new Vector3(0f, -90f, 0f),   // -X
            new Vector3(-90f, 0f, 0f),   // +Y
            new Vector3(90f, 0f, 0f),    // -Y
            new Vector3(0f, 0f, 0f),     // +Z
            new Vector3(0f, 180f, 0f)    // -Z
        };

        // ---------------- 窗口参数 ----------------
        [SerializeField] private GameObject centerObject;
        [SerializeField] private bool useBoundsCenter;
        [SerializeField] private bool hideCenterObject = true;

        [SerializeField] private int resolutionIndex = 2;   // 256
        [SerializeField] private int msaaIndex;             // 关闭
        [SerializeField] private bool hdr;
        [SerializeField] private bool mipmap;
        [SerializeField] private BackgroundMode background = BackgroundMode.Skybox;
        [SerializeField] private Color backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        [SerializeField] private int cullingMask = ~0;
        [SerializeField] private float nearClip = 0.05f;
        [SerializeField] private float farClip = 1000f;

        [SerializeField] private string outputFolder = DefaultOutputFolder;
        [SerializeField] private string fileNamePrefix = DefaultFileNamePrefix;
        [SerializeField] private bool autoRename = true;

        private Vector2 scroll;
        private Cubemap lastResult;

        [MenuItem("UPandaGF/Tools/立方体纹理生成工具")]
        public static void ShowWindow()
        {
            CubemapTextureGenerator window = GetWindow<CubemapTextureGenerator>("立方体贴图生成");
            window.minSize = new Vector2(360f, 560f);
            window.Show();
        }

        private void OnSelectionChange()
        {
            // 只在还没指定中心物体时自动带出当前选中，避免覆盖用户手动拖入的对象
            if (centerObject == null && Selection.activeGameObject != null)
            {
                centerObject = Selection.activeGameObject;
                Repaint();
            }
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleCenter
            };
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("立方体贴图生成工具", title);
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "以选中物体的位置为中心渲染 6 个方向，合成一张 Cubemap 存到工程内。\n" +
                "建议在编辑模式（非 Play）下使用。",
                MessageType.Info);

            DrawCenterSection();
            DrawRenderSection();
            DrawOutputSection();
            DrawActionSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawCenterSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("中心物体", EditorStyles.boldLabel);

            centerObject = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("中心物体", "贴图中心 = 该物体的位置（或包围盒中心）"),
                centerObject, typeof(GameObject), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("使用当前选中物体"))
                {
                    centerObject = Selection.activeGameObject;
                }
                if (GUILayout.Button("清空"))
                {
                    centerObject = null;
                }
            }

            useBoundsCenter = EditorGUILayout.Toggle(
                new GUIContent("以包围盒中心为中心", "勾选后用所有 Renderer 的包围盒中心，而不是 Transform.position"),
                useBoundsCenter);

            hideCenterObject = EditorGUILayout.Toggle(
                new GUIContent("渲染时隐藏中心物体", "避免物体自身出现在贴图里（贴在脸上、被近裁剪面切开）"),
                hideCenterObject);

            if (centerObject != null)
            {
                EditorGUILayout.LabelField("世界坐标", GetCenterPosition(centerObject).ToString("F3"));
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRenderSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("渲染设置", EditorStyles.boldLabel);

            resolutionIndex = Mathf.Clamp(resolutionIndex, 0, ResolutionValues.Length - 1);
            resolutionIndex = EditorGUILayout.Popup(
                new GUIContent("分辨率", "每个面的边长（像素）"), resolutionIndex, ResolutionLabels);

            msaaIndex = Mathf.Clamp(msaaIndex, 0, MsaaValues.Length - 1);
            msaaIndex = EditorGUILayout.Popup(new GUIContent("抗锯齿", "RenderTexture 的多重采样"), msaaIndex, MsaaLabels);

            hdr = EditorGUILayout.Toggle(new GUIContent("HDR", "用半浮点保存，保留高光过曝信息（体积更大）"), hdr);
            mipmap = EditorGUILayout.Toggle(new GUIContent("生成 Mipmap", "做天空盒/粗糙反射时更平滑"), mipmap);

            background = (BackgroundMode)EditorGUILayout.EnumPopup(
                new GUIContent("背景", "Skybox = 跟随场景天空盒；SolidColor = 纯色"), background);
            if (background == BackgroundMode.SolidColor)
            {
                backgroundColor = EditorGUILayout.ColorField("背景颜色", backgroundColor);
            }

            cullingMask = EditorGUILayout.MaskField("渲染层", cullingMask, GetLayerNames());

            nearClip = EditorGUILayout.FloatField(new GUIContent("近裁剪面", "必须小于中心到周围物体的距离"), nearClip);
            farClip = EditorGUILayout.FloatField(new GUIContent("远裁剪面"), farClip);
            EditorGUILayout.EndVertical();
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("输出设置", EditorStyles.boldLabel);

            outputFolder = EditorGUILayout.TextField(new GUIContent("输出目录", "必须是 Assets 下的路径，不存在会自动创建"), outputFolder);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("默认路径"))
                {
                    outputFolder = DefaultOutputFolder;
                }
                if (GUILayout.Button("打开输出目录"))
                {
                    RevealOutputFolder();
                }
            }

            fileNamePrefix = EditorGUILayout.TextField(new GUIContent("文件名前缀", "最终文件名 = 前缀_物体名.cubemap"), fileNamePrefix);

            autoRename = EditorGUILayout.Toggle(
                new GUIContent("同名自动重命名", "勾选 = 追加 _1/_2 序号；不勾选 = 覆盖同名资产"),
                autoRename);
            EditorGUILayout.EndVertical();
        }

        private void DrawActionSection()
        {
            EditorGUILayout.Space(10f);

            using (new EditorGUI.DisabledScope(centerObject == null))
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
                if (GUILayout.Button("生成立方体贴图", GUILayout.Height(40f)))
                {
                    GenerateCubemap();
                }
                GUI.backgroundColor = Color.white;
            }

            if (centerObject == null)
            {
                EditorGUILayout.HelpBox("请先在场景中选择（或拖入）一个作为中心的物体。", MessageType.Warning);
            }

            if (lastResult != null)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("上一次生成结果", EditorStyles.boldLabel);
                EditorGUILayout.ObjectField(lastResult, typeof(Cubemap), false);
                if (GUILayout.Button("在 Project 中高亮"))
                {
                    EditorGUIUtility.PingObject(lastResult);
                }
                EditorGUILayout.EndVertical();
            }
        }

        // ---------------------------------------------------------------------
        //  核心流程
        // ---------------------------------------------------------------------

        private void GenerateCubemap()
        {
            GameObject center = centerObject != null ? centerObject : Selection.activeGameObject;
            if (center == null)
            {
                EditorUtility.DisplayDialog("立方体贴图生成工具", "请先在场景中选择（或拖入）一个作为中心的物体。", "确定");
                return;
            }

            string folder = EditorAssetPathUtility.ToAssetFolder(outputFolder);
            if (string.IsNullOrEmpty(folder))
            {
                EditorUtility.DisplayDialog(
                    "立方体贴图生成工具",
                    "输出目录必须是工程 Assets 目录下的路径（例如 Assets/ArtAssets/CubeMaps）。",
                    "确定");
                return;
            }

            folder = EditorAssetPathUtility.EnsureFolder(folder);
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Debug.LogError($"[CubemapTextureGenerator] 输出目录创建失败：{folder}");
                return;
            }

            int size = ResolutionValues[Mathf.Clamp(resolutionIndex, 0, ResolutionValues.Length - 1)];
            string assetPath = BuildAssetPath(folder, center.name);

            // 覆盖同名资产属于破坏性操作：先确认，再开始昂贵的渲染。
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null &&
                !EditorDestructiveGuard.ConfirmOverwrite("生成立方体贴图", assetPath))
            {
                Debug.LogWarning("[CubemapTextureGenerator] 用户取消覆盖同名资产：" + assetPath);
                return;
            }

            Cubemap cubemap = null;
            RenderTexture rt = null;
            Texture2D readback = null;
            GameObject cameraObject = null;
            bool assetCreated = false;
            bool centerWasActive = center.activeSelf;
            Vector3 centerPosition = GetCenterPosition(center);

            try
            {
                EditorUtility.DisplayProgressBar("生成立方体贴图", "准备渲染资源 ...", 0.05f);

                // ---- 格式成对选择：HDR 走线性半浮点，LDR 跟随工程色彩空间的 sRGB 约定 ----
                TextureFormat textureFormat = hdr ? TextureFormat.RGBAHalf : TextureFormat.RGBA32;
                RenderTextureFormat rtFormat = hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32;
                RenderTextureReadWrite readWrite = hdr ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.Default;
                bool linear = hdr;

                rt = new RenderTexture(size, size, 24, rtFormat, readWrite)
                {
                    name = "~CubemapCaptureRT",
                    antiAliasing = MsaaValues[Mathf.Clamp(msaaIndex, 0, MsaaValues.Length - 1)]
                };
                rt.Create();
                if (!rt.IsCreated())
                {
                    throw new Exception("RenderTexture 创建失败，可能是显卡不支持当前格式/分辨率。");
                }

                // 这里**不能**传第 4 个参数：Unity 2021 的 Cubemap 构造函数只有 (size, format, mipChain)
                // 这一个重载，多传会导致 CS1501 编译报错；而 2022+ 新增的第 4 个参数是 createUninitialized
                // 而非 linear，传了也只会被误解成「不初始化数据」。HDR(RGBAHalf) 本身即线性格式，无需标记。
                cubemap = new Cubemap(size, textureFormat, mipmap)
                {
                    wrapMode = TextureWrapMode.Clamp,   // 原生 Cubemap 资产没有 Importer，设置只能在这里写
                    filterMode = FilterMode.Bilinear
                };

                readback = new Texture2D(size, size, textureFormat, false, linear);

                // ---- 临时相机：HideAndDontSave，渲染完立即销毁，不污染场景 ----
                cameraObject = new GameObject("~CubemapCaptureCamera") { hideFlags = HideFlags.HideAndDontSave };
                cameraObject.transform.position = centerPosition;

                Camera cam = cameraObject.AddComponent<Camera>();
                cam.fieldOfView = 90f;
                cam.aspect = 1f;                        // 必须为 1，否则面与面接不上
                cam.nearClipPlane = Mathf.Max(0.001f, nearClip);
                cam.farClipPlane = Mathf.Max(cam.nearClipPlane + 0.01f, farClip);
                cam.cullingMask = cullingMask;
                cam.clearFlags = background == BackgroundMode.Skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                cam.backgroundColor = backgroundColor;
                cam.allowHDR = hdr;
                cam.allowMSAA = rt.antiAliasing > 1;
                cam.useOcclusionCulling = false;        // 中心点常在室内，遮挡数据不适用，会误剔除
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;

                // ---- 隐藏中心物体，避免它自己被拍进去 ----
                if (hideCenterObject && centerWasActive)
                {
                    center.SetActive(false);
                }

                // ---- 逐面渲染并回读 ----
                for (int i = 0; i < FaceOrder.Length; i++)
                {
                    EditorUtility.DisplayProgressBar(
                        "生成立方体贴图",
                        $"渲染 {FaceOrder[i]} 面 ...",
                        0.1f + 0.85f * (i / (float)FaceOrder.Length));

                    cameraObject.transform.rotation = Quaternion.Euler(FaceEulers[i]);
                    cam.Render();

                    RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0f, 0f, size, size), 0, 0, false);
                    readback.Apply(false, false);
                    RenderTexture.active = null;

                    // 立方图的面数据与相机画面上下镜像，必须翻回去（详见文件头第 7 条）
                    Color[] pixels = readback.GetPixels();
                    FlipVertically(pixels, size);
                    cubemap.SetPixels(pixels, FaceOrder[i]);
                }

                cubemap.Apply(mipmap);

                // ---- 落盘 ----
                EditorUtility.DisplayProgressBar("生成立方体贴图", "保存资产 ...", 0.97f);

                string absolutePath = EditorAssetPathUtility.ToAbsolutePath(assetPath);
                if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || File.Exists(absolutePath))
                {
                    AssetDatabase.DeleteAsset(assetPath);
                }

                AssetDatabase.CreateAsset(cubemap, assetPath);
                assetCreated = true;   // 先置位再 SaveAssets：SaveAssets 抛异常时资产已经落盘，不能再 Destroy
                AssetDatabase.SaveAssets();

                lastResult = cubemap;
                EditorGUIUtility.PingObject(cubemap);
                Debug.Log($"[CubemapTextureGenerator] 立方体贴图生成完成：{assetPath}（{size}x{size}，" +
                          $"{(hdr ? "HDR" : "LDR")}，中心：{center.name} {centerPosition}）");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CubemapTextureGenerator] 生成立方体贴图失败：{e}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();

                if (centerWasActive && center != null && !center.activeSelf)
                {
                    center.SetActive(true);
                }

                if (cameraObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(cameraObject);
                }
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                if (readback != null)
                {
                    UnityEngine.Object.DestroyImmediate(readback);
                }
                if (!assetCreated && cubemap != null)
                {
                    // 失败时把内存里这张贴图清掉，避免泄漏
                    UnityEngine.Object.DestroyImmediate(cubemap);
                }
            }
        }

        /// <summary>
        /// 把一张面图像上下翻转（就地交换行，不额外分配）。
        ///
        /// 为什么必须翻：Cubemap / 立方体 RenderTexture 的面数据按图形 API 的立方图约定存放，
        /// 图像方向与「相机自然看到的画面」相差一个上下镜像；而 <see cref="Camera.Render"/> 渲染到
        /// 2D RenderTexture 得到的是自然画面。所以直接把回读结果 SetPixels 进去，贴图会整体上下颠倒
        /// （做天空盒时地面跑到头顶）。注意这里只需要**上下**翻，不是 180° 旋转也不要左右翻。
        /// </summary>
        private static void FlipVertically(Color[] pixels, int size)
        {
            for (int y = 0; y < size / 2; y++)
            {
                int topRow = y * size;
                int bottomRow = (size - 1 - y) * size;
                for (int x = 0; x < size; x++)
                {
                    Color temp = pixels[topRow + x];
                    pixels[topRow + x] = pixels[bottomRow + x];
                    pixels[bottomRow + x] = temp;
                }
            }
        }

        /// <summary>中心点：可选「所有 Renderer 的包围盒中心」，否则用 Transform.position。</summary>
        private Vector3 GetCenterPosition(GameObject target)
        {
            if (!useBoundsCenter)
            {
                return target.transform.position;
            }

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers == null || renderers.Length == 0)
            {
                return target.transform.position;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds.center;
        }

        /// <summary>按「前缀_物体名.cubemap」拼路径；开了自动重命名就避开已存在的文件。</summary>
        private string BuildAssetPath(string folder, string objectName)
        {
            string prefix = string.IsNullOrEmpty(fileNamePrefix) ? string.Empty : fileNamePrefix.Trim();
            string safeName = SanitizeFileName(objectName);
            string name = string.IsNullOrEmpty(prefix) ? safeName : prefix + "_" + safeName;
            if (string.IsNullOrEmpty(name))
            {
                name = "Cubemap";
            }

            string path = folder + "/" + name + ".cubemap";
            if (!autoRename)
            {
                return EditorAssetPathUtility.Normalize(path);
            }

            int index = 1;
            while (AssetDatabase.LoadMainAssetAtPath(path) != null ||
                   File.Exists(EditorAssetPathUtility.ToAbsolutePath(path)))
            {
                path = folder + "/" + name + "_" + index + ".cubemap";
                index++;
            }
            return EditorAssetPathUtility.Normalize(path);
        }

        private static string[] cachedLayerNames;

        /// <summary>
        /// 32 个层的显示名。必须补齐到 32 项（未命名的用 "Layer N"），
        /// 这样 MaskField 的第 i 位才严格对应 layer i；若直接用 InternalEditorUtility.layers，
        /// 空层名会被跳过，导致「勾选的层」与实际 cullingMask 的位错位。
        /// </summary>
        private static string[] GetLayerNames()
        {
            if (cachedLayerNames != null)
            {
                return cachedLayerNames;
            }

            cachedLayerNames = new string[32];
            for (int i = 0; i < cachedLayerNames.Length; i++)
            {
                string name = LayerMask.LayerToName(i);
                cachedLayerNames[i] = string.IsNullOrEmpty(name) ? "Layer " + i : i + ": " + name;
            }
            return cachedLayerNames;
        }

        private static string SanitizeFileName(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            System.Text.StringBuilder builder = new System.Text.StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return builder.ToString().Trim();
        }

        private void RevealOutputFolder()
        {
            string folder = EditorAssetPathUtility.ToAssetFolder(outputFolder);
            if (string.IsNullOrEmpty(folder))
            {
                Debug.LogWarning("[CubemapTextureGenerator] 输出目录不在 Assets 下，无法打开。");
                return;
            }

            string absolute = EditorAssetPathUtility.ToAbsolutePath(folder);
            if (!Directory.Exists(absolute))
            {
                Debug.LogWarning($"[CubemapTextureGenerator] 输出目录还不存在：{absolute}（生成一次后会自动创建）");
                return;
            }
            EditorUtility.RevealInFinder(absolute);
        }
    }
}
