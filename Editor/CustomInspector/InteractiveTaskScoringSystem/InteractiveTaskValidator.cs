using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    public enum TaskIssueLevel
    {
        Error = 0,      // 会导致运行时报错或流程失效
        Warning = 1,    // 能跑但行为不符合预期
        Info = 2,       // 提示（可忽略）
    }

    /// <summary>
    /// 一条校验结果。<see cref="fix"/> 不为空时可由工具自动修复。
    /// </summary>
    public class TaskIssue
    {
        public TaskIssueLevel level;
        public string message;
        public UnityEngine.Object context;   // 点击可定位
        public string fixLabel;
        public Action fix;
        public bool isIDFix;                 // ID 类修复必须最先执行（修复其它项依赖正确 ID）
    }

    /// <summary>
    /// 任务流程静态校验与自动修复。
    ///
    /// 解决的问题：本模块的配置分散在"配置资产 + 场景层级 + 实体 StepIDGroup"三处，
    /// 且 ID 由层级推导，配错时往往只是运行时打一行日志、甚至静默跳过步骤。
    /// 本工具在开工前/提交前做一次体检，把静默失败变成显式报告，并支持一键修复。
    ///
    /// 校验项：
    ///   1. TaskDataManager 是否存在/唯一、taskSteps 是否配置；
    ///   2. 配置步骤数量与场景 TaskStepBase 节点数量是否一致；
    ///   3. 每步是否绑定正确的 TaskStepData、是否只有一个操作组；
    ///   4. 操作 ID 是否为空、与层级推导值是否一致、是否有实体声明、是否重复声明；
    ///   5. 实体 StepIDGroup 是否为空/重复/孤儿（声明了却没被任何操作引用）；
    ///   6. 操作组是否为空组、步骤 EnableEntity 是否缺失或引用了不存在的实体。
    /// </summary>
    public static class InteractiveTaskValidator
    {
        // ============================ 校验 ============================

        public static List<TaskIssue> ValidateScene(out int errorCount, out int warningCount)
        {
            List<TaskIssue> issues = new List<TaskIssue>();

            TaskDataManager[] managers = UnityEngine.Object.FindObjectsOfType<TaskDataManager>(true);
            if (managers == null || managers.Length == 0)
            {
                Add(issues, TaskIssueLevel.Error, "场景里没有 TaskDataManager，任务无法运行（请添加一个并配置 taskSteps）", null);
                return Finish(issues, out errorCount, out warningCount);
            }
            if (managers.Length > 1)
            {
                Add(issues, TaskIssueLevel.Warning, $"场景里有 {managers.Length} 个 TaskDataManager，建议只保留一个（否则 Instance 会被最后一个覆盖）", managers[1]);
            }

            // ---- 收集实体注册表 ----
            TaskEntityBase[] entities = UnityEngine.Object.FindObjectsOfType<TaskEntityBase>(true);
            Dictionary<string, List<TaskEntityBase>> declared = new Dictionary<string, List<TaskEntityBase>>();
            foreach (TaskEntityBase e in entities)
            {
                if (e == null) continue;
                if (e.StepIDGroup == null || e.StepIDGroup.Length == 0)
                {
                    Add(issues, TaskIssueLevel.Warning, $"{PathOf(e)} 没有配置 StepIDGroup，不会被任何操作使用", e);
                    continue;
                }
                foreach (string id in e.StepIDGroup)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        Add(issues, TaskIssueLevel.Warning, $"{PathOf(e)} 的 StepIDGroup 里存在空 ID", e);
                        continue;
                    }
                    if (!declared.ContainsKey(id)) declared[id] = new List<TaskEntityBase>();
                    declared[id].Add(e);
                }
            }
            foreach (KeyValuePair<string, List<TaskEntityBase>> kv in declared)
            {
                if (kv.Value.Count <= 1) continue;
                StringBuilder names = new StringBuilder();
                foreach (TaskEntityBase e in kv.Value) names.Append(PathOf(e)).Append(" / ");
                Add(issues, TaskIssueLevel.Error, $"操作ID「{kv.Key}」被 {kv.Value.Count} 个实体重复声明，FindEntity 只会返回其中一个：{names}", kv.Value[0]);
            }

            // ---- 逐个 TaskDataManager 校验 ----
            HashSet<string> usedIDs = new HashSet<string>();
            foreach (TaskDataManager mgr in managers)
            {
                if (mgr == null) continue;
                ValidateManager(mgr, declared, usedIDs, issues);
            }

            // ---- 孤儿 ID ----
            foreach (KeyValuePair<string, List<TaskEntityBase>> kv in declared)
            {
                if (usedIDs.Contains(kv.Key)) continue;
                if (!HasOperationWithID(managers, kv.Key))
                    Add(issues, TaskIssueLevel.Warning, $"操作ID「{kv.Key}」被 {PathOf(kv.Value[0])} 声明，但没有任何 OperationCheckBase 引用它", kv.Value[0]);
            }

            return Finish(issues, out errorCount, out warningCount);
        }

        private static void ValidateManager(TaskDataManager mgr, Dictionary<string, List<TaskEntityBase>> declared, HashSet<string> usedIDs, List<TaskIssue> issues)
        {
            TaskConfig config = mgr.taskSteps;
            // P2：步骤执行顺序由 TaskStepData.order 决定（order 相同再看节点层级顺序，
            // 与 TaskDataManager.SortStepsByOrder 口径一致），所以「按顺序应为」这类检查必须用同样的排序，否则会误报。
            TaskStepBase[] steps = SortByOrder(mgr.GetComponentsInChildren<TaskStepBase>(true));

            if (config == null)
            {
                Add(issues, TaskIssueLevel.Error, $"{PathOf(mgr)} 的 taskSteps 未配置（没有指定任务配置资产）", mgr);
            }
            else if (config.stepsConfig == null)
            {
                Add(issues, TaskIssueLevel.Error, $"{AssetDatabase.GetAssetPath(config)} 的 stepsConfig 为空", config);
            }
            else if (config.stepsConfig.Length != steps.Length)
            {
                Add(issues, TaskIssueLevel.Error,
                    $"配置资产有 {config.stepsConfig.Length} 个步骤，场景里有 {steps.Length} 个 TaskStepBase 节点 —— 数量必须一致，否则初始化会直接中止、任务不运行",
                    mgr);
            }

            // 重复 stepID
            if (config != null && config.stepsConfig != null)
            {
                HashSet<string> seen = new HashSet<string>();
                for (int i = 0; i < config.stepsConfig.Length; i++)
                {
                    TaskStepData d = config.stepsConfig[i];
                    if (d == null)
                    {
                        Add(issues, TaskIssueLevel.Error, $"配置资产第 {i} 项步骤数据为空", config);
                        continue;
                    }
                    if (string.IsNullOrEmpty(d.stepID))
                    {
                        Add(issues, TaskIssueLevel.Error, $"配置资产第 {i} 项 stepID 为空", config);
                        continue;
                    }
                    if (!seen.Add(d.stepID))
                        Add(issues, TaskIssueLevel.Error, $"配置资产里 stepID「{d.stepID}」重复", config);
                }
            }

            for (int i = 0; i < steps.Length; i++)
                ValidateStep(mgr, steps[i], i, declared, usedIDs, issues);
        }

        private static void ValidateStep(TaskDataManager mgr, TaskStepBase step, int index, Dictionary<string, List<TaskEntityBase>> declared, HashSet<string> usedIDs, List<TaskIssue> issues)
        {
            if (step == null) return;
            TaskConfig config = mgr.taskSteps;

            // ---- 绑定检查 ----
            if (step.taskStepData == null)
            {
                Add(issues, TaskIssueLevel.Error, $"{PathOf(step)} 未绑定 TaskStepData", step);
            }
            else if (config != null && config.stepsConfig != null && index < config.stepsConfig.Length)
            {
                TaskStepData expect = config.stepsConfig[index];
                if (expect != null && step.taskStepData != expect)
                {
                    TaskStepBase captured = step;
                    Add(issues, TaskIssueLevel.Warning,
                        $"{PathOf(step)} 绑定的是「{step.taskStepData.stepID}」，按顺序应为「{expect.stepID}」",
                        step, "同步顺序",
                        () =>
                        {
                            Undo.RecordObject(captured, "同步步骤配置");
                            captured.taskStepData = expect;
                            MarkDirty(captured);
                        });
                }
            }

            // ---- 直接子物体上的操作组 ----
            List<OperationGroupBase> groups = new List<OperationGroupBase>();
            for (int i = 0; i < step.transform.childCount; i++)
            {
                OperationGroupBase g = step.transform.GetChild(i).GetComponent<OperationGroupBase>();
                if (g != null) groups.Add(g);
            }
            if (groups.Count == 0)
            {
                Add(issues, TaskIssueLevel.Warning, $"{PathOf(step)} 没有操作检查组，进入该步骤后会直接完成并计分", step);
            }
            if (groups.Count > 1)
            {
                Add(issues, TaskIssueLevel.Error, $"{PathOf(step)} 下有 {groups.Count} 个操作检查组，一个步骤只允许一个", groups[1]);
            }
            foreach (OperationGroupBase g in groups)
                ValidateGroup(g, mgr, declared, usedIDs, issues);

            // ---- EnableEntity ----
            List<string> opIDs = CollectOperationIDs(step);
            if (step.EnableEntity == null || step.EnableEntity.Length == 0)
            {
                if (opIDs.Count > 0)
                {
                    TaskStepBase captured = step;
                    string[] ids = opIDs.ToArray();
                    Add(issues, TaskIssueLevel.Warning,
                        $"{PathOf(step)} 的 EnableEntity 为空：进入该步骤不会激活任何实体",
                        step, "补全",
                        () =>
                        {
                            Undo.RecordObject(captured, "补全 EnableEntity");
                            captured.EnableEntity = ids;
                            MarkDirty(captured);
                        });
                }
            }
            else
            {
                foreach (string id in step.EnableEntity)
                {
                    if (string.IsNullOrEmpty(id))
                    {
                        Add(issues, TaskIssueLevel.Warning, $"{PathOf(step)} 的 EnableEntity 里存在空 ID", step);
                    }
                    else if (!declared.ContainsKey(id))
                    {
                        Add(issues, TaskIssueLevel.Warning, $"{PathOf(step)} 的 EnableEntity 里的「{id}」没有任何实体声明", step);
                    }
                }
            }
        }

        private static void ValidateGroup(OperationGroupBase group, TaskDataManager mgr, Dictionary<string, List<TaskEntityBase>> declared, HashSet<string> usedIDs, List<TaskIssue> issues)
        {
            if (group == null) return;

            // ---- 组 ID 与层级是否一致 ----
            string computed = ComputeChildID(group.transform.parent, group.transform);
            if (string.IsNullOrEmpty(group.OperatingStepID))
            {
                Add(issues, TaskIssueLevel.Warning, $"{PathOf(group)} 的操作组ID为空，其子操作ID也无法推导",
                    group, "重算ID", () => { RecalcAllIDs(mgr); }, true);
            }
            else if (!string.IsNullOrEmpty(computed) && group.OperatingStepID != computed)
            {
                Add(issues, TaskIssueLevel.Warning,
                    $"{PathOf(group)} 的操作组ID「{group.OperatingStepID}」与层级推导值「{computed}」不一致（移动过节点？）",
                    group, "重算ID", () => { RecalcAllIDs(mgr); }, true);
            }

            // ---- 子步骤 ----
            int childCount = 0;
            for (int i = 0; i < group.transform.childCount; i++)
            {
                Transform child = group.transform.GetChild(i);
                OperationGroupBase sub = child.GetComponent<OperationGroupBase>();
                if (sub != null)
                {
                    childCount++;
                    ValidateGroup(sub, mgr, declared, usedIDs, issues);
                    continue;
                }
                OperationCheckBase leaf = child.GetComponent<OperationCheckBase>();
                if (leaf != null)
                {
                    childCount++;
                    ValidateLeaf(leaf, mgr, declared, usedIDs, issues);
                }
            }
            if (childCount == 0)
            {
                Add(issues, TaskIssueLevel.Warning, $"{PathOf(group)} 没有任何子操作，该组会被视为直接完成", group);
            }
        }

        private static void ValidateLeaf(OperationCheckBase leaf, TaskDataManager mgr, Dictionary<string, List<TaskEntityBase>> declared, HashSet<string> usedIDs, List<TaskIssue> issues)
        {
            if (leaf == null) return;
            string id = leaf.OperatingStepID;

            if (string.IsNullOrEmpty(id))
            {
                Add(issues, TaskIssueLevel.Error, $"{PathOf(leaf)} 的操作ID为空，运行时必然找不到实体",
                    leaf, "重算ID", () => { RecalcAllIDs(mgr); }, true);
                return;
            }

            usedIDs.Add(id);

            if (!declared.ContainsKey(id))
            {
                Add(issues, TaskIssueLevel.Error,
                    $"{PathOf(leaf)} 的操作ID「{id}」没有任何实体声明（StepIDGroup），运行时必然报「实体ID获取失败」", leaf);
            }
            else if (leaf.TargetEntity == null)
            {
                List<TaskEntityBase> candidates = declared[id];
                Add(issues, TaskIssueLevel.Info,
                    $"{PathOf(leaf)} 未预设 TargetEntity（运行时按ID自动查找，可忽略）",
                    leaf, "自动填充",
                    () =>
                    {
                        OperationCheckBase captured = leaf;
                        Undo.RecordObject(captured, "填充 TargetEntity");
                        captured.TargetEntity = candidates.Count > 0 ? candidates[0] : null;
                        MarkDirty(captured);
                    });
            }
            else if (!ContainsID(leaf.TargetEntity, id))
            {
                Add(issues, TaskIssueLevel.Warning,
                    $"{PathOf(leaf)} 的 TargetEntity（{PathOf(leaf.TargetEntity)}）并没有声明该操作ID「{id}」", leaf);
            }
        }

        /// <summary>
        /// P2：按 TaskStepData.order 稳定排序（与 TaskDataManager.SortStepsByOrder 口径完全一致）。
        /// 校验器用它保证「按顺序应为」的判断顺序与运行时实际执行顺序一致。
        /// </summary>
        private static TaskStepBase[] SortByOrder(TaskStepBase[] steps)
        {
            if (steps == null || steps.Length < 2) return steps;
            TaskStepBase[] copy = (TaskStepBase[])steps.Clone();
            int[] indexs = new int[copy.Length];
            for (int i = 0; i < indexs.Length; i++) indexs[i] = i;
            System.Array.Sort(indexs, (a, b) =>
            {
                int oa = copy[a] != null && copy[a].taskStepData != null ? copy[a].taskStepData.order : 0;
                int ob = copy[b] != null && copy[b].taskStepData != null ? copy[b].taskStepData.order : 0;
                return oa != ob ? oa.CompareTo(ob) : a.CompareTo(b);
            });
            TaskStepBase[] sorted = new TaskStepBase[copy.Length];
            for (int i = 0; i < sorted.Length; i++) sorted[i] = copy[indexs[i]];
            return sorted;
        }

        // ============================ 修复 ============================
        /// <summary>
        /// 按层级规则重算指定任务下所有操作组/操作的 ID：
        ///   操作组 = 步骤ID-同级序号；叶子操作 = 组ID-同级序号（与 OperationCheckBase.Reset 规则一致，支持嵌套组）
        /// </summary>
        public static void RecalcAllIDs(TaskDataManager mgr)
        {
            if (mgr == null) return;
            TaskStepBase[] steps = mgr.GetComponentsInChildren<TaskStepBase>(true);
            foreach (TaskStepBase step in steps)
            {
                if (step == null || step.taskStepData == null) continue;
                if (string.IsNullOrEmpty(step.taskStepData.stepID)) continue;
                RecalcChildren(step.transform, step.taskStepData.stepID);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[交互任务校验] 已按层级重算 {PathOf(mgr)} 下所有操作ID");
        }

        private static void RecalcChildren(Transform parent, string parentID)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                string id = parentID + "-" + i;   // 规则：父ID + "-" + GetSiblingIndex()
                OperationGroupBase group = child.GetComponent<OperationGroupBase>();
                if (group != null)
                {
                    Undo.RecordObject(group, "重算操作ID");
                    group.OperatingStepID = id;
                    MarkDirty(group);
                    RecalcChildren(child, id);
                    continue;
                }
                OperationCheckBase leaf = child.GetComponent<OperationCheckBase>();
                if (leaf != null)
                {
                    Undo.RecordObject(leaf, "重算操作ID");
                    leaf.OperatingStepID = id;
                    MarkDirty(leaf);
                }
            }
        }

        /// <summary>
        /// 一键修复：先重算 ID（ID 类修复必须最先做），再重新校验并应用其它可自动修复项。
        /// </summary>
        public static string OneClickFix()
        {
            TaskDataManager[] managers = UnityEngine.Object.FindObjectsOfType<TaskDataManager>(true);
            if (managers == null || managers.Length == 0) return "场景里没有 TaskDataManager，无需修复";

            foreach (TaskDataManager mgr in managers)
                if (mgr != null) RecalcAllIDs(mgr);

            int e, w;
            List<TaskIssue> issues = ValidateScene(out e, out w);
            int applied = 0;
            foreach (TaskIssue issue in issues)
            {
                if (issue.fix == null || issue.isIDFix) continue;
                issue.fix();
                applied++;
            }
            AssetDatabase.SaveAssets();

            List<TaskIssue> after = ValidateScene(out e, out w);
            return $"已重算ID并应用 {applied} 项修复；剩余 错误 {Count(after, TaskIssueLevel.Error)} · 警告 {Count(after, TaskIssueLevel.Warning)} · 提示 {Count(after, TaskIssueLevel.Info)}";
        }

        // ============================ 工具方法 ============================

        public static string BuildReport(List<TaskIssue> issues, int errorCount, int warningCount)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("交互任务评分系统 · 流程体检报告");
            sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"错误 {errorCount} · 警告 {warningCount} · 提示 {issues.Count - errorCount - warningCount}");
            sb.AppendLine(new string('-', 46));
            foreach (TaskIssue issue in issues)
            {
                sb.Append('[').Append(LevelText(issue.level)).Append("] ");
                if (issue.context != null) sb.Append(PathOf(issue.context)).Append(" : ");
                sb.AppendLine(issue.message);
            }
            return sb.ToString();
        }

        public static int Count(List<TaskIssue> issues, TaskIssueLevel level)
        {
            int n = 0;
            if (issues != null) foreach (TaskIssue i in issues) if (i.level == level) n++;
            return n;
        }

        public static string LevelText(TaskIssueLevel level)
        {
            switch (level)
            {
                case TaskIssueLevel.Error: return "错误";
                case TaskIssueLevel.Warning: return "警告";
                default: return "提示";
            }
        }

        /// <summary>
        /// 取对象的层级路径。参数类型必须是 UnityEngine.Object：
        /// 校验上下文里既有组件（Component，如 TaskStepBase）也有资产（ScriptableObject，如 TaskConfig）。
        /// </summary>
        public static string PathOf(UnityEngine.Object target)
        {
            if (target == null) return "(null)";

            Transform t = null;
            Component component = target as Component;
            if (component != null) t = component.transform;
            else
            {
                GameObject go = target as GameObject;
                if (go != null) t = go.transform;
            }
            if (t == null) return target.name;

            string p = t.name;
            t = t.parent;
            while (t != null)
            {
                p = t.name + "/" + p;
                t = t.parent;
            }
            return p;
        }

        private static List<string> CollectOperationIDs(TaskStepBase step)
        {
            List<string> ids = new List<string>();
            if (step == null) return ids;
            OperationCheckBase[] ops = step.GetComponentsInChildren<OperationCheckBase>(true);
            foreach (OperationCheckBase op in ops)
            {
                if (op != null && !string.IsNullOrEmpty(op.OperatingStepID) && !ids.Contains(op.OperatingStepID))
                    ids.Add(op.OperatingStepID);
            }
            return ids;
        }

        private static bool HasOperationWithID(TaskDataManager[] managers, string id)
        {
            foreach (TaskDataManager mgr in managers)
            {
                if (mgr == null) continue;
                OperationCheckBase[] ops = mgr.GetComponentsInChildren<OperationCheckBase>(true);
                foreach (OperationCheckBase op in ops)
                    if (op != null && op.OperatingStepID == id) return true;
            }
            return false;
        }

        private static bool ContainsID(TaskEntityBase entity, string id)
        {
            if (entity == null || entity.StepIDGroup == null) return false;
            foreach (string s in entity.StepIDGroup)
                if (s == id) return true;
            return false;
        }

        private static string ComputeChildID(Transform parent, Transform child)
        {
            if (parent == null || child == null) return null;
            GetUniTaskID provider = parent.GetComponent<GetUniTaskID>();
            if (provider == null) return null;
            return provider.GetID + "-" + child.GetSiblingIndex();
        }

        private static void MarkDirty(Component c)
        {
            if (c == null) return;
            EditorUtility.SetDirty(c);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }

        private static void Add(List<TaskIssue> issues, TaskIssueLevel level, string message, UnityEngine.Object context, string fixLabel = null, Action fix = null, bool isIDFix = false)
        {
            issues.Add(new TaskIssue
            {
                level = level,
                message = message,
                context = context,
                fixLabel = fixLabel,
                fix = fix,
                isIDFix = isIDFix,
            });
        }

        private static List<TaskIssue> Finish(List<TaskIssue> issues, out int errorCount, out int warningCount)
        {
            errorCount = Count(issues, TaskIssueLevel.Error);
            warningCount = Count(issues, TaskIssueLevel.Warning);
            return issues;
        }
    }
}
