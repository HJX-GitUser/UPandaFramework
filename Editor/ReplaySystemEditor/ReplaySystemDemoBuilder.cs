﻿//
//  ReplaySystemDemoBuilder.cs
//  路径：Assets/Scripts/upanda-framework/Editor/ReplaySystemEditor/
//
// =============================================================================
//  作用：一键重建 ReplaySystem 的示例场景
//        （原型：Runtime/Game/ReplaySystem/Sample/HFScene.unity）
//
//  菜单：
//    UPandaGF/Runtime/ReplaySystem/创建回放示例场景（新建场景）
//    UPandaGF/Runtime/ReplaySystem/在当前场景追加示例内容
//
//  重建出来的内容（逐项对齐原型）：
//
//    【3D 部分】
//      Main Camera        pos(0,4.5,-11)  pitch≈16.26°（取自原型序列化四元数）Tag=MainCamera
//      Directional Light  pos(0,3,0)      rot(50,-30,0)  软阴影，并设为 RenderSettings.sun
//      DemoGround         Cube  pos(0,-0.5,0)  scale(24,1,16)  BoxCollider（静态地面）
//      DemoCubeTower      空节点 pos(2.5,0,0)，下挂 6 个 Cube（都带 Rigidbody）
//        ├ DemoCube_0  local( 0.08, 0.55,0)   ├ DemoCube_3  local(-0.08, 3.70,0)
//        ├ DemoCube_1  local(-0.08, 1.60,0)   ├ DemoCube_4  local( 0.08, 4.75,0)
//        ├ DemoCube_2  local( 0.08, 2.65,0)   └ DemoCube_5  local(-0.08, 5.80,0)
//        （位置规律：y = 0.55 + 1.05*i，x 交替 ±0.08 —— 一座歪歪扭扭的“方块塔”）
//      DemoBouncyBall     Sphere pos(-2.5,4,0) scale 0.6，Rigidbody + 弹性物理材质 sphere.physicMaterial
//      DemoSpinner        Cube   pos(-1,1.5,3) scale 0.7，Rigidbody（自由落体/翻滚的物理方块）
//
//    【UI 部分】ReplayCanvas（ScreenSpaceOverlay + CanvasScaler 1920x1080 match 0.5 + GraphicRaycaster）
//      TimeText        anchor(0.5,1) pos(0,-20)   size(700,40)   "00:00 / 00:00"
//      ProgressSlider  anchor(0.5,1) pos(0,-70)   size(900,20)   0~1 进度条
//      BtnRecord       center(-460,170) 210x60 "录制"
//      BtnStopRecord   center(-230,170) 210x60 "停止录制"
//      BtnPlay         center(   0,170) 210x60 "回放"
//      BtnPause        center( 230,170) 210x60 "暂停/继续"
//      BtnStopPlay     center( 460,170) 210x60 "停止回放"
//      BtnSave         center(-120, 90) 210x50 "保存录制"
//      BtnLoad         center( 120, 90) 210x50 "加载回放"
//      SpeedLabel      anchor(0,0) pos( 80,40) 100x40 "速度"
//      SpeedSlider     anchor(0,0) pos(170,40) 320x20 0.1~5x
//      SpeedText       anchor(0,0) pos(360,40) 100x40 "1.0x"
//      EventSystem + StandaloneInputModule
//
//    【系统】ReplaySystem 空物体：ReplayManager(speed 1 / excludeUI true / autoSaveOnStop false)
//                                    + ReplayUI（12 个引用全部自动连好）
//
//  与原型的差异（有意为之，均不影响功能）：
//    · Slider 内部（Background / Fill Area / Fill / Handle Slide Area / Handle）用 Unity 官方默认
//      层级重建，锚点与原型一致，但不逐个照抄原型里被 Slider 运行时覆盖的中间值。
//    · 文本字体：统一用 **Unity 内置默认字体**（= 手动新建 Text 时 Unity 自动给的那个），
//      2021.2+ 为 LegacyRuntime.ttf、更早版本为 Arial.ttf，按新→旧依次尝试；中文靠 Unity
//      内部的 OS 字体回退渲染（原型的字体也是这个内置字体，fileID 10102）。
//    · 文本 Raycast Target 统一设为 false（按钮子文本不拦截点击，不影响 Button 自身响应）。
//    · 不复制原型里的 Lighting Settings 资产；用默认灯光设置（本示例只吃实时平行光）。
//
//  说明：录制时 ReplayManager 会自动登记 RigidbodyVelocityRecorder（见其 Awake），
//        所以这些物理物体不需要额外挂脚本 —— 与原型一致。
// =============================================================================

using ReplaySystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace UPandaGF.GFEditor
{
    /// <summary>
    /// ReplaySystem 示例场景一键生成器
    /// </summary>
    public static class ReplaySystemDemoBuilder
    {
        // ---------------- 菜单与路径 ----------------

        private const string MenuRoot = "UPandaGF/Runtime/ReplaySystem/";

        private const string DefaultSceneFolder = "Assets/Scripts/upanda-framework/Runtime/Game/ReplaySystem/Demo";
        private const string DefaultSceneName   = "ReplaySystemDemo.unity";

        // 弹性物理材质（原型里 DemoBouncyBall 的 SphereCollider 用的就是它）
        private const string PhysicsMaterialPath =
            "Assets/Scripts/upanda-framework/Runtime/Game/ReplaySystem/Sample/sphere.physicMaterial";

        // 内置 UI 资源路径（AssetDatabase.GetBuiltinExtraResource）
        private const string UISpritePath    = "UI/Skin/UISprite.psd";    // 按钮/进度填充
        private const string BackgroundPath  = "UI/Skin/Background.psd";  // 滑条底
        private const string KnobPath        = "UI/Skin/Knob.psd";        // 滑条把手

        // ---------------- 颜色（取自原型） ----------------

        private static readonly Color ButtonColor     = new Color(0.88f, 0.88f, 0.88f, 1f);
        private static readonly Color ButtonTextColor = new Color(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color LabelColor      = Color.white;
        private static readonly Color SliderBgColor   = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        private static readonly Color SliderFillColor = new Color(0.35f, 0.7f, 0.9f, 1f);

        // =====================================================================
        //  菜单入口 1：新建场景
        // =====================================================================

        [MenuItem(MenuRoot + "创建回放示例场景（新建场景）", false, 10)]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return; // 用户取消了
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            BuildSceneContents(includeCameraAndLight: true);

            EnsureFolder(DefaultSceneFolder);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath(DefaultSceneFolder + "/" + DefaultSceneName);
            bool saved = EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.Refresh();

            if (saved)
            {
                Debug.Log("[ReplaySystem] 示例场景已创建：" + scenePath +
                          "\n玩法：Play → 点「录制」等物理物体掉落/翻滚 → 点「停止录制」→ 点「回放」。" +
                          "\n（「保存录制」/「加载回放」用 FileManager 落盘，编辑器下加载会弹文件框）");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(scenePath));
            }
            else
            {
                Debug.LogError("[ReplaySystem] 场景保存失败：" + scenePath + "，请检查目录是否可写。");
            }
        }

        // =====================================================================
        //  菜单入口 2：追加到当前场景
        // =====================================================================

        [MenuItem(MenuRoot + "在当前场景追加示例内容", false, 11)]
        public static void AppendToCurrentScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[ReplaySystem] 当前没有打开的场景，无法追加。");
                return;
            }

            BuildSceneContents(includeCameraAndLight: false);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.LogWarning("[ReplaySystem] 已向场景 \"" + scene.name + "\" 追加：物理示例物体 + UI + EventSystem + ReplaySystem。" +
                             "\n若该场景里已有 Canvas / EventSystem / ReplaySystem，请自行删掉多余的那份。相机与灯光沿用场景现有的。");
        }

        // =====================================================================
        //  场景内容构建
        // =====================================================================

        private static void BuildSceneContents(bool includeCameraAndLight)
        {
            if (includeCameraAndLight)
            {
                CreateMainCamera();
                CreateDirectionalLight();
            }

            CreatePhysicsProps();
            UIReferences ui = CreateReplayUI();
            CreateEventSystem();
            CreateReplaySystem(ui);
        }

        // ---------------------------------------------------------------------
        //  3D：相机 / 灯光 / 物理示例物体
        // ---------------------------------------------------------------------

        /// <summary>主相机：pos(0,4.5,-11)、俯角 16.26°（原型序列化四元数换算所得）</summary>
        private static void CreateMainCamera()
        {
            GameObject go = CreateEmpty("Main Camera", new Vector3(0f, 4.5f, -11f), Quaternion.Euler(16.26f, 0f, 0f));
            go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
            go.tag = "MainCamera";
        }

        /// <summary>平行光：pos(0,3,0)、rot(50,-30,0)、软阴影，并登记为环境光的“太阳”</summary>
        private static void CreateDirectionalLight()
        {
            GameObject go = CreateEmpty("Directional Light", new Vector3(0f, 3f, 0f), Quaternion.Euler(50f, -30f, 0f));
            Light sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
        }

        /// <summary>地面 + 方块塔 + 弹跳球 + 翻滚方块（与原型一一对应）</summary>
        private static void CreatePhysicsProps()
        {
            // ---- 地面：Cube 拉扁成 24x1x16，位于 y=-0.5（顶面正好在 y=0）----
            CreatePrimitive("DemoGround", PrimitiveType.Cube, null,
                            new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 16f));

            // ---- 方块塔：6 个带 Rigidbody 的 Cube，x 交替 ±0.08，y 每层 +1.05 ----
            GameObject tower = CreateEmpty("DemoCubeTower", new Vector3(2.5f, 0f, 0f), Quaternion.identity);
            for (int i = 0; i < 6; i++)
            {
                float x = (i % 2 == 0) ? 0.08f : -0.08f;
                float y = 0.55f + 1.05f * i;

                GameObject cube = CreatePrimitive("DemoCube_" + i, PrimitiveType.Cube, tower.transform,
                                                 new Vector3(x, y, 0f), Vector3.one);
                AddDefaultRigidbody(cube);
            }

            // ---- 弹跳球：Sphere + 弹性物理材质 ----
            GameObject ball = CreatePrimitive("DemoBouncyBall", PrimitiveType.Sphere, null,
                                             new Vector3(-2.5f, 4f, 0f), new Vector3(0.6f, 0.6f, 0.6f));
            AddDefaultRigidbody(ball);

            SphereCollider ballCollider = ball.GetComponent<SphereCollider>();
            PhysicMaterial bouncy = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(PhysicsMaterialPath);
            if (bouncy != null && ballCollider != null)
            {
                ballCollider.material = bouncy;   // 让它弹起来（原型即如此）
            }
            else if (bouncy == null)
            {
                Debug.LogWarning("[ReplaySystem] 未找到物理材质：" + PhysicsMaterialPath + "，弹跳球将使用默认材质。");
            }

            // ---- 翻滚方块 ----
            GameObject spinner = CreatePrimitive("DemoSpinner", PrimitiveType.Cube, null,
                                                new Vector3(-1f, 1.5f, 3f), new Vector3(0.7f, 0.7f, 0.7f));
            AddDefaultRigidbody(spinner);
        }

        /// <summary>原型里所有 Rigidbody 的参数都相同：mass 1 / drag 0 / angularDrag 0.05 / 开重力</summary>
        private static void AddDefaultRigidbody(GameObject go)
        {
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.drag = 0f;
            rb.angularDrag = 0.05f;
            rb.useGravity = true;
            rb.isKinematic = false;
        }

        // ---------------------------------------------------------------------
        //  UI：画布 + 控件（返回给 ReplayUI 连线用）
        // ---------------------------------------------------------------------

        /// <summary>UI 控件引用集合（用于给 ReplayUI 自动连线）</summary>
        private class UIReferences
        {
            public Text timeText;
            public Slider progressSlider;
            public Button recordButton;
            public Button stopRecordButton;
            public Button playbackButton;
            public Button pauseResumeButton;
            public Button stopPlaybackButton;
            public Slider speedSlider;
            public Text speedText;
            public Button saveButton;
            public Button loadButton;
        }

        private static UIReferences CreateReplayUI()
        {
            // ---- 画布：ScreenSpaceOverlay + CanvasScaler(1920x1080, match 0.5) + GraphicRaycaster ----
            GameObject canvasGo = new GameObject("ReplayCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(canvasGo, "Create Replay Demo Canvas");

            Transform parent = canvasGo.transform;
            UIReferences ui = new UIReferences();

            // ---- 顶部：时间文本 + 进度条 ----
            ui.timeText = CreateText("TimeText", parent, "00:00 / 00:00",
                                     new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f),
                                     new Vector2(700f, 40f), TextAnchor.MiddleCenter, LabelColor);

            ui.progressSlider = CreateSlider("ProgressSlider", parent,
                                            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f),
                                            new Vector2(900f, 20f));

            // ---- 中间：5 个操作按钮（与原型坐标一致）----
            ui.recordButton       = CreateButton("BtnRecord",     parent, new Vector2(-460f, 170f), "录制");
            ui.stopRecordButton   = CreateButton("BtnStopRecord", parent, new Vector2(-230f, 170f), "停止录制");
            ui.playbackButton     = CreateButton("BtnPlay",       parent, new Vector2(   0f, 170f), "回放");
            ui.pauseResumeButton  = CreateButton("BtnPause",      parent, new Vector2( 230f, 170f), "暂停/继续");
            ui.stopPlaybackButton = CreateButton("BtnStopPlay",   parent, new Vector2( 460f, 170f), "停止回放");

            // ---- 中间偏下：文件按钮 ----
            ui.saveButton = CreateButton("BtnSave", parent, new Vector2(-120f, 90f), "保存录制", new Vector2(210f, 50f));
            ui.loadButton = CreateButton("BtnLoad", parent, new Vector2( 120f, 90f), "加载回放", new Vector2(210f, 50f));

            // ---- 左下角：速度标签 + 滑条 + 数值 ----
            ui.speedText = CreateText("SpeedText", parent, "1.0x",
                                      new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(360f, 40f),
                                      new Vector2(100f, 40f), TextAnchor.MiddleCenter, LabelColor);

            CreateText("SpeedLabel", parent, "速度",
                       new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(80f, 40f),
                       new Vector2(100f, 40f), TextAnchor.MiddleLeft, LabelColor);

            ui.speedSlider = CreateSlider("SpeedSlider", parent,
                                          new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(170f, 40f),
                                          new Vector2(320f, 20f));

            return ui;
        }

        /// <summary>EventSystem（新版输入系统下若报错，请自行替换为 InputSystemUIInputModule）</summary>
        private static void CreateEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
            {
                return; // 场景里已有就跳过
            }

            GameObject go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, "Create Replay Demo EventSystem");
        }

        // ---------------------------------------------------------------------
        //  系统：ReplaySystem（ReplayManager + ReplayUI 并完成连线）
        // ---------------------------------------------------------------------

        private static void CreateReplaySystem(UIReferences ui)
        {
            GameObject go = CreateEmpty("ReplaySystem", Vector3.zero, Quaternion.identity);

            // 注意：先加 ReplayManager，再把它的引用交给 ReplayUI
            ReplayManager manager = go.AddComponent<ReplayManager>();
            manager.speed = 1f;
            manager.excludeUI = true;
            manager.autoSaveOnStop = false;

            ReplayUI replayUI = go.AddComponent<ReplayUI>();
            replayUI.manager            = manager;
            replayUI.timeText           = ui.timeText;
            replayUI.progressSlider     = ui.progressSlider;
            replayUI.recordButton       = ui.recordButton;
            replayUI.stopRecordButton   = ui.stopRecordButton;
            replayUI.playbackButton     = ui.playbackButton;
            replayUI.pauseResumeButton  = ui.pauseResumeButton;
            replayUI.stopPlaybackButton = ui.stopPlaybackButton;
            replayUI.speedSlider        = ui.speedSlider;
            replayUI.speedText          = ui.speedText;
            replayUI.saveButton         = ui.saveButton;
            replayUI.loadButton         = ui.loadButton;

            EditorUtility.SetDirty(go);
        }

        // =====================================================================
        //  UI 小工具
        // =====================================================================

        private static GameObject CreateUIObject(string name, Transform parent,
                                                 Vector2 anchorMin, Vector2 anchorMax,
                                                 Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return go;
        }

        /// <summary>按钮 = Image(UISprite, Sliced) + Button + 子 Text（ReplayUI 靠 GetComponentInChildren&lt;Text&gt; 改“暂停/继续”字样）</summary>
        private static Button CreateButton(string name, Transform parent, Vector2 anchoredPosition, string label,
                                           Vector2? size = null)
        {
            Vector2 buttonSize = size ?? new Vector2(210f, 60f);

            GameObject go = CreateUIObject(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                           anchoredPosition, buttonSize);

            Image image = go.AddComponent<Image>();
            image.sprite = GetBuiltinSprite(UISpritePath);
            image.type = Image.Type.Sliced;
            image.color = ButtonColor;

            Button button = go.AddComponent<Button>();
            button.targetGraphic = image;

            // 子文本与按钮同尺寸、居中
            CreateText(name + "_Label", go.transform, label,
                       new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                       buttonSize, TextAnchor.MiddleCenter, ButtonTextColor);

            return button;
        }

        /// <summary>滑条 = Background + Fill Area/Fill + Handle Slide Area/Handle（Unity 官方默认结构）</summary>
        private static Slider CreateSlider(string name, Transform parent,
                                           Vector2 anchorMin, Vector2 anchorMax,
                                           Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = CreateUIObject(name, parent, anchorMin, anchorMax, anchoredPosition, size);
            Slider slider = go.AddComponent<Slider>();

            // 1) 底槽
            Image background = CreateImage("Background", go.transform,
                                           new Vector2(0f, 0.25f), new Vector2(1f, 0.75f), Vector2.zero, Vector2.zero,
                                           GetBuiltinSprite(BackgroundPath), SliderBgColor);

            // 2) 填充区（Fill 的锚点由 Slider 运行时驱动）
            GameObject fillArea = CreateUIObject("Fill Area", go.transform,
                                                 new Vector2(0f, 0.25f), new Vector2(1f, 0.75f),
                                                 new Vector2(-5f, 0f), new Vector2(-20f, 0f));
            Image fill = CreateImage("Fill", fillArea.transform,
                                     new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero,
                                     GetBuiltinSprite(UISpritePath), SliderFillColor);

            // 3) 把手区
            GameObject handleArea = CreateUIObject("Handle Slide Area", go.transform,
                                                   new Vector2(0f, 0f), new Vector2(1f, 1f),
                                                   Vector2.zero, new Vector2(-20f, 0f));
            Image handle = CreateImage("Handle", handleArea.transform,
                                       new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero,
                                       new Vector2(20f, 20f), GetBuiltinSprite(KnobPath), Color.white);
            handle.type = Image.Type.Simple;

            // 4) 连线
            slider.fillRect      = fill.rectTransform;
            slider.handleRect    = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction     = Slider.Direction.LeftToRight;
            slider.wholeNumbers  = false;

            // background 只是视觉底槽，不参与逻辑
            background.raycastTarget = false;

            return slider;
        }

        private static Image CreateImage(string name, Transform parent,
                                         Vector2 anchorMin, Vector2 anchorMax,
                                         Vector2 anchoredPosition, Vector2 size,
                                         Sprite sprite, Color color)
        {
            GameObject go = CreateUIObject(name, parent, anchorMin, anchorMax, anchoredPosition, size);
            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return image;
        }

        private static Text CreateText(string name, Transform parent, string content,
                                       Vector2 anchorMin, Vector2 anchorMax,
                                       Vector2 anchoredPosition, Vector2 size,
                                       TextAnchor alignment, Color color)
        {
            GameObject go = CreateUIObject(name, parent, anchorMin, anchorMax, anchoredPosition, size);
            Text text = go.AddComponent<Text>();
            text.font = GetBuiltinFont();
            text.fontSize = 22;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.raycastTarget = false;   // 文本不拦截点击（Button 由自身 Image 接收）
            return text;
        }

        /// <summary>
        /// 取 Unity 的内置默认字体 —— 等价于在 Hierarchy 里新建一个 Text 时 Unity 自动给的那个字体。
        /// 字体名随版本变化、取不到时的告警口径都在 UPandaGF.UnityBuiltinFont 里统一处理。
        /// </summary>
        private static Font GetBuiltinFont()
        {
            return UnityBuiltinFont.Get();
        }

        private static Sprite GetBuiltinSprite(string builtinPath)
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>(builtinPath);
        }

        // =====================================================================
        //  通用小工具
        // =====================================================================

        private static GameObject CreateEmpty(string name, Vector3 position, Quaternion rotation)
        {
            GameObject go = new GameObject(name);
            go.transform.SetPositionAndRotation(position, rotation);
            Undo.RegisterCreatedObjectUndo(go, "Create Replay Demo Object");
            return go;
        }

        /// <summary>创建图元并摆放（保留 CreatePrimitive 自带的 Collider，与原型的物理物体一致）</summary>
        private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent,
                                                  Vector3 localPosition, Vector3 localScale)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "Create Replay Demo Object");

            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            return go;
        }

        /// <summary>确保工程内目录存在（形如 Assets/A/B，逐级创建）</summary>
        private static void EnsureFolder(string projectRelativeFolder)
        {
            string full = projectRelativeFolder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(full))
            {
                return;
            }

            string[] parts = full.Split('/');
            string current = parts[0];   // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }
    }
}
