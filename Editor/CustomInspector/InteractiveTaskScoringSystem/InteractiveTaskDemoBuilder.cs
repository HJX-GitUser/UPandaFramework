using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 一键生成"交互任务评分系统"的最小可运行示例：
    ///   1) 任务配置资产 TaskConfig_Demo.asset（2 个步骤：串联组 + 并联组）
    ///   2) 交互实体预制体 DemoInteractiveEntity.prefab
    ///   3) 演示场景 InteractiveTaskDemo.unity（含 4 个可点击方块、TaskDataManager、HUD）
    ///
    /// 菜单：UPandaGF/交互任务评分系统/创建演示场景
    /// 生成后直接点 Play，按屏幕提示点击方块即可完成任务。
    /// </summary>
    public static class InteractiveTaskDemoBuilder
    {
        private const string ModuleRoot = "Assets/Scripts/upanda-framework/Runtime/Game/InteractiveTaskScoringSystem";
        private const string DemoFolder = ModuleRoot + "/Example/Demo";
        private const string ConfigPath = DemoFolder + "/TaskConfig_Demo.asset";
        private const string PrefabPath = DemoFolder + "/DemoInteractiveEntity.prefab";
        private const string ScenePath = DemoFolder + "/InteractiveTaskDemo.unity";

        // ID 规则（与 OperationCheckBase.Reset / OperationGroupBase.Reset 一致）：
        //   步骤 = TaskStepData.stepID；操作组 = 步骤ID-组序号；叶子操作 = 组ID-操作序号
        private const string Step1ID = "Step1";
        private const string Step1GroupID = "Step1-0";
        private const string Step1OpA = "Step1-0-0";
        private const string Step1OpB = "Step1-0-1";
        private const string Step2ID = "Step2";
        private const string Step2GroupID = "Step2-0";
        private const string Step2OpA = "Step2-0-0";
        private const string Step2OpB = "Step2-0-1";

        [MenuItem("UPandaGF/Runtime/交互任务评分系统/创建演示场景", false)]
        public static void BuildDemo()
        {
            // 先询问是否保存当前场景，用户取消则不做任何改动
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder();
            TaskConfig config = CreateOrUpdateConfig();
            GameObject entityPrefab = CreateEntityPrefab();

            // ---- 建场景（DefaultGameObjects 会自带 Main Camera 与 Directional Light）----
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            SetupCamera();

            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetAsFirstSibling();

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(2f, 1f, 2f);

            // ---- 任务根节点：TaskDataManager + HUD ----
            GameObject root = new GameObject("TaskDemo");
            TaskDataManager manager = root.AddComponent<TaskDataManager>();
            manager.taskSteps = config;
            root.AddComponent<DemoTaskHud>();

            // ---- 步骤 1：串联组（必须按 A -> B 顺序点击）----
            CreateStep(root.transform, config.stepsConfig[0], new[] { Step1OpA, Step1OpB }, Step1GroupID, true, 0);
            // ---- 步骤 2：并联组（C、D 顺序不限，两个都点完才通过）----
            CreateStep(root.transform, config.stepsConfig[1], new[] { Step2OpA, Step2OpB }, Step2GroupID, false, 2);

            // ---- 4 个可交互实体 ----
            string[] names = { "EntityA", "EntityB", "EntityC", "EntityD" };
            string[] ids = { Step1OpA, Step1OpB, Step2OpA, Step2OpB };
            Vector3[] positions =
            {
                new Vector3(-2f, 0.5f, -2f),
                new Vector3(2f, 0.5f, -2f),
                new Vector3(-2f, 0.5f, 2f),
                new Vector3(2f, 0.5f, 2f),
            };
            for (int i = 0; i < names.Length; i++)
            {
                GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(entityPrefab);
                go.name = names[i];
                go.transform.position = positions[i];
                DemoInteractiveEntity entity = go.GetComponent<DemoInteractiveEntity>();
                entity.StepIDGroup = new[] { ids[i] };   // 该实体负责的操作ID
            }

            // ---- 保存场景 ----
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log($"[交互任务评分系统] 演示场景已生成：{ScenePath}\n直接点 Play，按屏幕提示点击方块；G=引导，K=跳过。");
        }

        private static void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cam.transform.position = new Vector3(0f, 6f, -9f);
            cam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            cam.clearFlags = CameraClearFlags.Skybox;
        }

        /// <summary>
        /// 创建一个任务步骤节点及其操作组/叶子操作。
        /// </summary>
        private static void CreateStep(Transform parent, TaskStepData data, string[] operationIDs, string groupID, bool series, int parallelCount)
        {
            GameObject stepGo = new GameObject("Task(" + data.stepID + ")");
            stepGo.transform.SetParent(parent, false);
            TaskStepBase step = stepGo.AddComponent<TaskStepBase>();
            step.taskStepData = data;
            step.EnableEntity = operationIDs;   // 进入该步骤时要激活的实体ID

            GameObject groupGo = new GameObject(series ? "SeriesOperationGroup" : "ParallelOperationGroup");
            groupGo.transform.SetParent(stepGo.transform, false);

            if (series)
            {
                SeriesOperationGroup group = groupGo.AddComponent<SeriesOperationGroup>();
                group.OperatingStepID = groupID;
                step.operationGroup = group;
            }
            else
            {
                ParallelOperationGroup group = groupGo.AddComponent<ParallelOperationGroup>();
                group.OperatingStepID = groupID;
                group.completeCount = parallelCount;   // 需要完成的子操作数量（0 或 <=0 时等于全部）
                step.operationGroup = group;
            }

            for (int i = 0; i < operationIDs.Length; i++)
            {
                GameObject opGo = new GameObject("OperationCheck(" + i + ")");
                opGo.transform.SetParent(groupGo.transform, false);
                OperationCheckBase op = opGo.AddComponent<OperationCheckBase>();
                op.OperatingStepID = operationIDs[i];
                // TargetEntity 留空：运行时由 OperationCheckBase.Start() 通过 TaskEntityManager 自动查找
            }
        }

        /// <summary>
        /// 生成/更新演示用任务配置资产（两个步骤，分数 10 / 20）。
        /// </summary>
        private static TaskConfig CreateOrUpdateConfig()
        {
            TaskConfig config = AssetDatabase.LoadAssetAtPath<TaskConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<TaskConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.stepsConfig = new[]
            {
                new TaskStepData
                {
                    stepID = Step1ID,
                    description = "第一步：先点 A，再点 B（串联操作组）",
                    baseScore = 10f,
                    tip = "必须按 A -> B 的顺序点击",
                },
                new TaskStepData
                {
                    stepID = Step2ID,
                    description = "第二步：点击 C 与 D（并联操作组，顺序不限）",
                    baseScore = 20f,
                    tip = "两个方块都点一次即可",
                },
            };
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        /// <summary>
        /// 生成交互实体预制体（方块 + 碰撞体 + 示例触发器 + 示例实体逻辑）。
        /// 注意：TaskTriggerExample 必须显式挂载（TaskEntityBase 已不再自动添加）。
        /// </summary>
        private static GameObject CreateEntityPrefab()
        {
            GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            temp.name = "DemoInteractiveEntity";
            temp.AddComponent<TaskTriggerExample>();
            temp.AddComponent<DemoInteractiveEntity>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        private static void EnsureFolder()
        {
            if (!Directory.Exists(DemoFolder))
            {
                Directory.CreateDirectory(DemoFolder);
                AssetDatabase.Refresh();
            }
        }
    }
}
