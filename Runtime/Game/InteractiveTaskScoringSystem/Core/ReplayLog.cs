using System;
using System.Collections.Generic;
using System.Text;

namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 一条动作级日志（P2 新增）。
    /// 注意：字段全部是 public 且类型可被 Unity 序列化，因此可以直接用 JsonUtility 落盘/读盘。
    /// </summary>
    [Serializable]
    public class ReplayEntry
    {
        public float time;        // 绝对时间（Time.time）
        public float offset;      // 相对任务开始的秒数（时间轴展示用）
        public string stepID;     // 所属步骤ID（任务级事件为 null/空）
        public string action;     // Step_Enter / Step_Complete / Step_Error / Step_Skip / Task_Complete / Task_Restart
        public bool isCorrect;    // 这次动作是否算“正确/正向”
        public string details;    // 可读描述
        public float score;       // 记录时该步骤的得分
        public int errors;        // 记录时该步骤的错误次数
    }

    /// <summary>
    /// 动作级回放日志（纯 C#，不含任何 UnityEngine 依赖，可单测）。
    ///
    /// 定位：解决“记录 + 复盘”。真正把交互重演到场景里（自动重放点击）不在本类范围，
    /// 见 ReadMe「已知限制」——重演需要重新注入输入/动画，可靠性远低于可视化复盘。
    ///
    /// 用法：
    ///   var log = new ReplayLog("DemoTask", 3.5f);
    ///   log.Add(Time.time, "Step1", "Step_Complete", true, "完成步骤 Step1");
    ///   File.WriteAllText(path, JsonUtility.ToJson(log, true));   // JsonUtility 在调用方（Unity 侧）使用
    /// </summary>
    [Serializable]
    public class ReplayLog
    {
        public int logVersion = 1;
        public string taskName = "";
        public string createTime = "";
        public float startTime;        // 任务开始时间（Time.time）
        public float endTime;          // 最后一条日志时间
        public float totalScore;       // 导出时的总分（由 TaskDataManager 回填）
        public int stepCount;          // 步骤总数（由 TaskDataManager 回填）
        public List<ReplayEntry> entries = new List<ReplayEntry>();

        public ReplayLog() { }

        public ReplayLog(string taskName, float startTime)
        {
            Reset(taskName, startTime);
        }

        /// <summary>复位为一次新的任务记录</summary>
        public void Reset(string taskName, float startTime)
        {
            this.taskName = taskName ?? "";
            this.startTime = startTime;
            endTime = startTime;
            totalScore = 0f;
            stepCount = 0;
            createTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            if (entries == null) entries = new List<ReplayEntry>();
            else entries.Clear();
        }

        /// <summary>追加一条动作记录</summary>
        public ReplayEntry Add(float time, string stepID, string action, bool isCorrect, string details, float score = 0f, int errors = 0)
        {
            if (entries == null) entries = new List<ReplayEntry>();
            ReplayEntry entry = new ReplayEntry
            {
                time = time,
                offset = time - startTime,
                stepID = stepID,
                action = action,
                isCorrect = isCorrect,
                details = details,
                score = score,
                errors = errors
            };
            entries.Add(entry);
            if (time > endTime) endTime = time;
            return entry;
        }

        /// <summary>记录条数</summary>
        public int Count { get { return entries == null ? 0 : entries.Count; } }

        /// <summary>任务总耗时（秒）</summary>
        public float Duration { get { return endTime > startTime ? endTime - startTime : 0f; } }

        /// <summary>错误动作条数（action == Step_Error）</summary>
        public int ErrorCount
        {
            get
            {
                if (entries == null) return 0;
                int n = 0;
                for (int i = 0; i < entries.Count; i++)
                    if (entries[i] != null && entries[i].action == "Step_Error") n++;
                return n;
            }
        }

        /// <summary>按步骤过滤日志（stepID 为空则返回全部）</summary>
        public List<ReplayEntry> FilterByStep(string stepID)
        {
            List<ReplayEntry> result = new List<ReplayEntry>();
            if (entries == null) return result;
            foreach (ReplayEntry e in entries)
            {
                if (e == null) continue;
                if (string.IsNullOrEmpty(stepID) || e.stepID == stepID) result.Add(e);
            }
            return result;
        }

        /// <summary>生成可读时间轴（查看器与日志输出共用）</summary>
        public string[] BuildTimeline()
        {
            string[] lines = new string[Count];
            for (int i = 0; i < Count; i++)
            {
                ReplayEntry e = entries[i];
                if (e == null) { lines[i] = ""; continue; }
                lines[i] = $"[+{e.offset:F2}s] {e.stepID}  {e.action}  {(e.isCorrect ? "√" : "×")}  得分 {e.score:F1}  错误 {e.errors}  {e.details}";
            }
            return lines;
        }

        /// <summary>导出 CSV（便于交给策划/用 Excel 复盘）</summary>
        public string ToCsv()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("offset,time,stepID,action,isCorrect,score,errors,details");
            if (entries != null)
            {
                foreach (ReplayEntry e in entries)
                {
                    if (e == null) continue;
                    sb.AppendLine($"{e.offset:F3},{e.time:F3},{Csv(e.stepID)},{Csv(e.action)},{(e.isCorrect ? 1 : 0)},{e.score:F1},{e.errors},{Csv(e.details)}");
                }
            }
            return sb.ToString();
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }

        /// <summary>摘要文本（窗口标题栏/日志用）</summary>
        public string Summary()
        {
            return $"{taskName}  记录 {Count} 条  错误 {ErrorCount} 次  耗时 {Duration:F2}s  总分 {totalScore:F1}  创建于 {createTime}";
        }
    }
}
