namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 任务级进度（纯 C#）：当前步骤下标、总分、推进与完成判定。
    ///
    /// 从 TaskDataManager 抽出来的原因：
    ///   1) 这些是纯数据运算，放在 MonoBehaviour 里无法单测；
    ///   2) “下标”既是索引又被当作完成哨兵，集中到一处才好保证边界正确；
    ///   3) 存档只需要序列化本对象。
    /// </summary>
    public class TaskProgress
    {
        /// <summary>步骤总数</summary>
        public int stepCount { get; private set; }

        /// <summary>当前步骤下标；等于 stepCount 表示全部完成</summary>
        public int currentIndex { get; private set; }

        /// <summary>累计总分</summary>
        public float totalScore { get; private set; }

        /// <summary>是否全部完成</summary>
        public bool isFinished { get { return stepCount <= 0 || currentIndex >= stepCount; } }

        /// <summary>初始化（= 重新开始）</summary>
        public void Init(int steps)
        {
            stepCount = steps > 0 ? steps : 0;
            currentIndex = 0;
            totalScore = 0f;
        }

        /// <summary>累加得分（未跳过的步骤才调用）</summary>
        public void AddScore(float score)
        {
            totalScore += score;
        }

        /// <summary>
        /// 推进到下一步。返回 true 表示还有下一步；返回 false 表示刚刚完成最后一步（currentIndex 变为 stepCount）。
        /// </summary>
        public bool MoveNext()
        {
            if (currentIndex < stepCount - 1)
            {
                currentIndex++;
                return true;
            }
            currentIndex = stepCount;
            return false;
        }

        /// <summary>下标是否指向一个有效步骤</summary>
        public bool HasStep(int index)
        {
            return index >= 0 && index < stepCount;
        }
    }
}
