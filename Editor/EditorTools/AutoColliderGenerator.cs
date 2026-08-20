using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UPandaGF.GFEditor
{
    public enum ColliderType
    {
        BoxCollider,      // 盒型碰撞体
        SphereCollider,   // 球型碰撞体
        CapsuleCollider,  // 胶囊碰撞体
        MeshCollider,     // 网格碰撞体（精确但性能消耗大）
        CompoundCollider  // 复合碰撞体（多个子物体分别生成）
    }

    public enum FitMode
    {
        RendererBounds,   // 基于渲染器边界（包含所有Renderer）
        MeshBounds,       // 基于网格边界（仅MeshFilter）
        ColliderBounds,   // 基于现有碰撞体边界
        ChildrenBounds,   // 基于所有子物体边界
        ManualSize        // 手动设置大小
    }

    public class AutoColliderGenerator : EditorWindow
    {
        // 设置参数
        private ColliderType colliderType = ColliderType.BoxCollider;
        private FitMode fitMode = FitMode.RendererBounds;
        private bool isTrigger = false;
        private PhysicMaterial physicMaterial;
        private float padding = 0.01f;  // 边界填充
        private float offsetY = 0f;     // Y轴偏移

        // 高级设置
        [SerializeField]
        private bool showAdvanced = false;
        private bool removeExisting = true;  // 移除现有碰撞体
        private bool applyToChildren = false; // 应用到子物体

        // 手动设置
        private Vector3 manualSize = Vector3.one;
        private Vector3 manualCenter = Vector3.zero;

        [MenuItem("UPandaGF/Tools/碰撞体生成器")]
        public static void ShowWindow()
        {
            var window = GetWindow<AutoColliderGenerator>("碰撞体生成器");
            window.minSize = new Vector2(350, 420);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            // 标题样式
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel);
            titleStyle.fontSize = 16;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            EditorGUILayout.LabelField("自动碰撞体生成器", titleStyle);

            EditorGUILayout.Space(15);

            // 主要设置区域
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("基础设置", EditorStyles.boldLabel);

            colliderType = (ColliderType)EditorGUILayout.EnumPopup("碰撞体类型", colliderType);
            fitMode = (FitMode)EditorGUILayout.EnumPopup("适配模式", fitMode);

            EditorGUILayout.Space(5);

            // 根据适配模式显示不同参数
            if (fitMode == FitMode.ManualSize)
            {
                manualSize = EditorGUILayout.Vector3Field("手动大小", manualSize);
                manualCenter = EditorGUILayout.Vector3Field("中心偏移", manualCenter);
            }
            else
            {
                padding = EditorGUILayout.FloatField("边界填充", padding);
                offsetY = EditorGUILayout.FloatField("Y轴偏移", offsetY);
            }

            EditorGUILayout.Space(5);

            isTrigger = EditorGUILayout.Toggle("设为触发器", isTrigger);
            physicMaterial = (PhysicMaterial)EditorGUILayout.ObjectField(
                "物理材质", physicMaterial, typeof(PhysicMaterial), false);

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 高级设置
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "高级设置", true);
            if (showAdvanced)
            {
                EditorGUILayout.BeginVertical("box");
                removeExisting = EditorGUILayout.Toggle("移除现有碰撞体", removeExisting);
                applyToChildren = EditorGUILayout.Toggle("应用到子物体", applyToChildren);
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(10);
            }

            EditorGUILayout.Space(10);

            // 操作按钮
            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.2f);
            if (GUILayout.Button("生成碰撞体", GUILayout.Height(40)))
            {
                GenerateColliders();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(5);

            GUI.backgroundColor = new Color(0.9f, 0.3f, 0.3f);
            if (GUILayout.Button("移除所有碰撞体", GUILayout.Height(30)))
            {
                RemoveAllColliders();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(20);

            // 说明信息
            EditorGUILayout.HelpBox(
                "使用说明：\n" +
                "1. 在场景中选择目标物体\n" +
                "2. 选择碰撞体类型和适配模式\n" +
                "3. 点击生成按钮",
                MessageType.Info);
        }

        private void GenerateColliders()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("错误", "请先选择要生成碰撞体的物体！", "确定");
                return;
            }

            // 使用 HashSet 避免重复处理同一物体（如父子重叠）
            HashSet<GameObject> objectsToProcess = new HashSet<GameObject>();

            foreach (GameObject go in selectedObjects)
            {
                objectsToProcess.Add(go);

                if (applyToChildren && colliderType != ColliderType.CompoundCollider)
                {
                    // 获取所有子物体（包括自身，但排除自身已在上面添加）
                    Transform[] allChildren = go.GetComponentsInChildren<Transform>(true);
                    foreach (Transform child in allChildren)
                    {
                        if (child.gameObject != go)
                        {
                            objectsToProcess.Add(child.gameObject);
                        }
                    }
                }
            }

            int successCount = 0;

            Undo.SetCurrentGroupName("生成自动碰撞体");
            int group = Undo.GetCurrentGroup();

            foreach (GameObject go in objectsToProcess)
            {
                if (GenerateColliderForObject(go))
                {
                    successCount++;
                }
            }

            Undo.CollapseUndoOperations(group);

            EditorUtility.DisplayDialog("完成",
                $"成功为 {successCount} 个物体生成了碰撞体！", "确定");
        }

        private bool GenerateColliderForObject(GameObject go)
        {
            // 计算边界（ManualSize 模式不需要 Renderer/MeshFilter）
            Bounds bounds = CalculateBounds(go);
            if (bounds.size == Vector3.zero && fitMode != FitMode.ManualSize)
            {
                return false;
            }

            // 移除现有碰撞体
            if (removeExisting)
            {
                RemoveExistingColliders(go);
            }

            // 根据类型创建碰撞体
            switch (colliderType)
            {
                case ColliderType.BoxCollider:
                    CreateBoxCollider(go, bounds);
                    break;
                case ColliderType.SphereCollider:
                    CreateSphereCollider(go, bounds);
                    break;
                case ColliderType.CapsuleCollider:
                    CreateCapsuleCollider(go, bounds);
                    break;
                case ColliderType.MeshCollider:
                    CreateMeshCollider(go);
                    break;
                case ColliderType.CompoundCollider:
                    CreateCompoundColliders(go);
                    return true; // 复合碰撞体已处理所有子物体
            }

            return true;
        }

        private Bounds CalculateBounds(GameObject go)
        {
            if (fitMode == FitMode.ManualSize)
            {
                // 手动模式：直接使用本地坐标，无需转换
                return new Bounds(manualCenter, manualSize);
            }

            Bounds bounds = new Bounds(go.transform.position, Vector3.zero);
            bool hasBounds = false;

            switch (fitMode)
            {
                case FitMode.RendererBounds:
                    Renderer[] renderers = go.GetComponentsInChildren<Renderer>(false);
                    foreach (Renderer r in renderers)
                    {
                        if (!hasBounds)
                        {
                            bounds = r.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(r.bounds);
                        }
                    }
                    break;

                case FitMode.MeshBounds:
                    MeshFilter[] meshFilters = go.GetComponentsInChildren<MeshFilter>(false);
                    foreach (MeshFilter mf in meshFilters)
                    {
                        if (mf.sharedMesh != null)
                        {
                            // 使用 mf.transform 正确转换到世界坐标
                            Transform t = mf.transform;
                            Bounds meshBounds = mf.sharedMesh.bounds;
                            Vector3 worldCenter = t.TransformPoint(meshBounds.center);
                            Vector3 worldSize = Vector3.Scale(meshBounds.size, t.lossyScale);

                            Bounds worldBounds = new Bounds(worldCenter, worldSize);

                            if (!hasBounds)
                            {
                                bounds = worldBounds;
                                hasBounds = true;
                            }
                            else
                            {
                                bounds.Encapsulate(worldBounds);
                            }
                        }
                    }
                    break;

                case FitMode.ColliderBounds:
                    Collider[] colliders = go.GetComponentsInChildren<Collider>(false);
                    foreach (Collider c in colliders)
                    {
                        if (!hasBounds)
                        {
                            bounds = c.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            bounds.Encapsulate(c.bounds);
                        }
                    }
                    break;

                case FitMode.ChildrenBounds:
                    Transform[] children = go.GetComponentsInChildren<Transform>(true);
                    foreach (Transform child in children)
                    {
                        if (child == go.transform) continue;

                        Renderer childRenderer = child.GetComponent<Renderer>();
                        if (childRenderer != null)
                        {
                            if (!hasBounds)
                            {
                                bounds = childRenderer.bounds;
                                hasBounds = true;
                            }
                            else
                            {
                                bounds.Encapsulate(childRenderer.bounds);
                            }
                        }
                    }
                    break;
            }

            // 应用填充和偏移
            if (hasBounds)
            {
                bounds.Expand(padding * 2);
                bounds.center += new Vector3(0, offsetY, 0);
            }

            return bounds;
        }

        private void CreateBoxCollider(GameObject go, Bounds worldBounds)
        {
            BoxCollider box = Undo.AddComponent<BoxCollider>(go);

            // 将世界坐标转换为本地坐标
            Vector3 localCenter = go.transform.InverseTransformPoint(worldBounds.center);
            Vector3 localSize = go.transform.InverseTransformVector(worldBounds.size);
            // 注意：InverseTransformVector 已经考虑了旋转和缩放，无需再乘以 localScale

            box.center = localCenter;
            box.size = localSize;
            box.isTrigger = isTrigger;
            box.material = physicMaterial;

            // 确保尺寸不为零
            Vector3 size = new Vector3(Mathf.Abs(box.size.x), Mathf.Abs(box.size.y), Mathf.Abs(box.size.z));
            size.x = Mathf.Max(size.x, 0.001f);
            size.y = Mathf.Max(size.y, 0.001f);
            size.z = Mathf.Max(size.z, 0.001f);
            box.size = size;
        }

        private void CreateSphereCollider(GameObject go, Bounds worldBounds)
        {
            SphereCollider sphere = Undo.AddComponent<SphereCollider>(go);

            // 世界中心转本地
            Vector3 localCenter = go.transform.InverseTransformPoint(worldBounds.center);
            sphere.center = localCenter; // offsetY 已在 CalculateBounds 中应用

            // 将世界尺寸转为本地尺寸，取最大维度的一半作为半径
            Vector3 localSize = go.transform.InverseTransformVector(worldBounds.size);
            float radius = Mathf.Max(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y), Mathf.Abs(localSize.z)) / 2f;
            sphere.radius = Mathf.Max(radius, 0.001f);

            sphere.isTrigger = isTrigger;
            sphere.material = physicMaterial;
        }

        private void CreateCapsuleCollider(GameObject go, Bounds worldBounds)
        {
            CapsuleCollider capsule = Undo.AddComponent<CapsuleCollider>(go);

            Vector3 localCenter = go.transform.InverseTransformPoint(worldBounds.center);
            capsule.center = localCenter; // offsetY 已在 CalculateBounds 中应用

            // 将世界尺寸转为本地尺寸
            Vector3 localSize = go.transform.InverseTransformVector(worldBounds.size);
            float absX = Mathf.Abs(localSize.x);
            float absY = Mathf.Abs(localSize.y);
            float absZ = Mathf.Abs(localSize.z);

            // 自动选择方向：基于最长的轴
            if (absY >= absX && absY >= absZ)
            {
                // Y轴为主方向
                capsule.direction = 1;
                capsule.height = Mathf.Max(absY, 0.002f);
                capsule.radius = Mathf.Max(Mathf.Max(absX, absZ) / 2f, 0.001f);
            }
            else if (absX >= absZ)
            {
                // X轴为主方向
                capsule.direction = 0;
                capsule.height = Mathf.Max(absX, 0.002f);
                capsule.radius = Mathf.Max(Mathf.Max(absY, absZ) / 2f, 0.001f);
            }
            else
            {
                // Z轴为主方向
                capsule.direction = 2;
                capsule.height = Mathf.Max(absZ, 0.002f);
                capsule.radius = Mathf.Max(Mathf.Max(absX, absY) / 2f, 0.001f);
            }

            capsule.isTrigger = isTrigger;
            capsule.material = physicMaterial;
        }

        private void CreateMeshCollider(GameObject go)
        {
            MeshFilter mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                // 没有网格时，创建盒型碰撞体作为后备
                Debug.LogWarning($"物体 {go.name} 没有网格，使用盒型碰撞体代替");
                Bounds fallbackBounds = CalculateBounds(go);
                CreateBoxCollider(go, fallbackBounds);
                return;
            }

            MeshCollider meshCol = Undo.AddComponent<MeshCollider>(go);
            meshCol.sharedMesh = mf.sharedMesh;
            meshCol.convex = true;
            meshCol.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation;
            meshCol.isTrigger = isTrigger;
            meshCol.material = physicMaterial;
        }

        private void CreateCompoundColliders(GameObject go)
        {
            // 为每个子网格创建单独的盒型碰撞体
            MeshFilter[] meshFilters = go.GetComponentsInChildren<MeshFilter>(true);

            foreach (MeshFilter mf in meshFilters)
            {
                if (mf.gameObject == go) continue;

                GameObject child = mf.gameObject;
                if (removeExisting)
                {
                    RemoveExistingColliders(child);
                }

                // 计算子物体的世界边界
                Bounds meshBounds = mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
                Transform t = mf.transform;
                Vector3 worldCenter = t.TransformPoint(meshBounds.center);
                Vector3 worldSize = Vector3.Scale(meshBounds.size, t.lossyScale);
                Bounds worldBounds = new Bounds(worldCenter, worldSize);

                // 为子物体添加盒型碰撞体
                CreateBoxCollider(child, worldBounds);
            }
        }

        private void RemoveExistingColliders(GameObject go)
        {
            Collider[] existingColliders = go.GetComponents<Collider>();
            foreach (Collider c in existingColliders)
            {
                Undo.DestroyObjectImmediate(c);
            }
        }

        private void RemoveAllColliders()
        {
            GameObject[] selectedObjects = Selection.gameObjects;

            if (selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("错误", "请先选择要移除碰撞体的物体！", "确定");
                return;
            }

            int count = 0;
            Undo.SetCurrentGroupName("移除所有碰撞体");

            foreach (GameObject go in selectedObjects)
            {
                Collider[] colliders = go.GetComponentsInChildren<Collider>(true);
                foreach (Collider c in colliders)
                {
                    Undo.DestroyObjectImmediate(c);
                    count++;
                }
            }

            EditorUtility.DisplayDialog("完成", $"移除了 {count} 个碰撞体", "确定");
        }
    }

    // 快捷菜单项：右键菜单
    public class AutoColliderContextMenu
    {
        [MenuItem("GameObject/UPandaGF/自动碰撞体/盒型碰撞体", false, 20)]
        static void AddBoxCollider()
        {
            AutoColliderGenerator.ShowWindow();
        }

        [MenuItem("GameObject/UPandaGF/自动碰撞体/快速适配 (Renderer)", false, 21)]
        static void QuickFitRenderer()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                QuickGenerate(go, ColliderType.BoxCollider, FitMode.RendererBounds);
            }
        }

        [MenuItem("GameObject/UPandaGF/自动碰撞体/快速适配 (Mesh)", false, 22)]
        static void QuickFitMesh()
        {
            foreach (GameObject go in Selection.gameObjects)
            {
                QuickGenerate(go, ColliderType.BoxCollider, FitMode.MeshBounds);
            }
        }

        private static void QuickGenerate(GameObject go, ColliderType type, FitMode mode)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r == null) return;

            Undo.SetCurrentGroupName("快速生成碰撞体");

            // 移除现有
            Collider[] existing = go.GetComponents<Collider>();
            foreach (Collider c in existing) Undo.DestroyObjectImmediate(c);

            // 创建新的
            Bounds bounds = r.bounds;
            BoxCollider box = Undo.AddComponent<BoxCollider>(go);
            box.center = go.transform.InverseTransformPoint(bounds.center);
            box.size = go.transform.InverseTransformVector(bounds.size);
        }
    }
}