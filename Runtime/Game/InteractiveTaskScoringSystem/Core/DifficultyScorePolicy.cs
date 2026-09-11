namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>难度等级（配合 <see cref="DifficultyScorePolicy"/> 使用）</summary>
    public enum TaskDifficulty
    {
        /// <summary>简单：每次错误只轻微扣分，最低保底 70%</summary>
        Easy = 0,
        /// <summary>普通：0错满分 / 1错半分 / 2错0分（与默认策略完全一致）</summary>
        Normal = 1,
        /// <summary>困难：错一次就直接 0 分</summary>
        Hard = 2,
    }

    /// <summary>
    /// 难度分级评分策略：错误系数表 + 超时扣分（P2 新增）。
    ///
    /// 计算方式：
    ///   1) 错误系数：score = 基础分 × 系数[错误次数]（超出表尾则取最后一项）；
    ///   2) 超时扣分：若设置了 parTime 与 overtimePenaltyPerSecond，则超过 parTime 的每一秒再扣若干分；
    ///   3) 结果钳制到 ≥ 0。
    ///
    /// 之所以实现 <see cref="IScorePolicyEx"/>：超时扣分需要知道本步耗时，
    /// 而旧的 <c>CalcScore(baseScore, errors)</c> 拿不到时间；同时保留旧签名，未实现 Ex 的策略不受影响。
    ///
    /// 用法：<c>taskDataManager.scorePolicy = DifficultyScorePolicy.Create(TaskDifficulty.Hard, 15f, 2f);</c>
    /// </summary>
    public class DifficultyScorePolicy : IScorePolicyEx
    {
        /// <summary>难度等级</summary>
        public TaskDifficulty difficulty = TaskDifficulty.Normal;

        /// <summary>达标耗时（秒）；≤0 表示不考核时间</summary>
        public float parTime = 0f;

        /// <summary>超过 parTime 后每秒扣的分；≤0 表示不扣</summary>
        public float overtimePenaltyPerSecond = 0f;

        private static readonly float[] EasyRates = { 1f, 0.9f, 0.8f, 0.7f };
        private static readonly float[] NormalRates = { 1f, 0.5f, 0f };
        private static readonly float[] HardRates = { 1f, 0f };

        /// <summary>当前难度对应的错误系数表（内部使用，不返回可写引用）</summary>
        private float[] Rates
        {
            get
            {
                switch (difficulty)
                {
                    case TaskDifficulty.Easy: return EasyRates;
                    case TaskDifficulty.Hard: return HardRates;
                    default: return NormalRates;
                }
            }
        }

        /// <summary>取某个难度的错误系数表副本（供 UI / 文档展示，改副本不会影响内部数据）</summary>
        public static float[] GetRateTable(TaskDifficulty level)
        {
            switch (level)
            {
                case TaskDifficulty.Easy: return (float[])EasyRates.Clone();
                case TaskDifficulty.Hard: return (float[])HardRates.Clone();
                default: return (float[])NormalRates.Clone();
            }
        }

        /// <summary>工厂方法：一行创建一个难度策略</summary>
        public static DifficultyScorePolicy Create(TaskDifficulty level, float parTime = 0f, float overtimePenaltyPerSecond = 0f)
        {
            return new DifficultyScorePolicy
            {
                difficulty = level,
                parTime = parTime,
                overtimePenaltyPerSecond = overtimePenaltyPerSecond
            };
        }

        /// <summary>按错误次数取系数</summary>
        public float RateAt(int errors)
        {
            float[] rates = Rates;
            if (errors <= 0) return rates[0];
            return rates[errors < rates.Length ? errors : rates.Length - 1];
        }

        public float CalcScore(ScoreContext context)
        {
            float score = context.baseScore * RateAt(context.errors);
            if (parTime > 0f && overtimePenaltyPerSecond > 0f)
            {
                float over = context.duration - parTime;
                if (over > 0f) score -= over * overtimePenaltyPerSecond;
            }
            return score > 0f ? score : 0f;
        }

        /// <summary>兼容旧的 IScorePolicy 签名（拿不到耗时，等价于 duration = 0）</summary>
        public float CalcScore(float baseScore, int errors)
        {
            ScoreContext ctx = new ScoreContext { baseScore = baseScore, errors = errors };
            return CalcScore(ctx);
        }

        public string Description
        {
            get
            {
                float[] rates = Rates;
                string table = string.Join("/", System.Array.ConvertAll(rates, r => r.ToString("0.##")));
                string time = (parTime > 0f && overtimePenaltyPerSecond > 0f)
                    ? $"，超过 {parTime} 秒后每秒扣 {overtimePenaltyPerSecond} 分"
                    : "，不考核耗时";
                return $"{difficulty} 难度：错误系数 {table}{time}";
            }
        }
    }
}
