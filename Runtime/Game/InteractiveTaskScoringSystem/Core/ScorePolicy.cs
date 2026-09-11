namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 评分策略：把“基础分 + 错误次数”映射为最终得分。
    /// 抽成接口是为了：① 让评分逻辑可以脱离 Unity 单独测试；② 后续做难度分级/权重时只换实现，不动流程代码。
    /// </summary>
    public interface IScorePolicy
    {
        float CalcScore(float baseScore, int errors);
        string Description { get; }
    }

    /// <summary>
    /// 默认策略（与原实现行为完全一致，便于零风险替换）：
    /// 0 次错误 = 满分；1 次错误 = 半分；2 次及以上 = 0 分。
    /// </summary>
    public class DefaultScorePolicy : IScorePolicy
    {
        public string Description { get { return "0错满分 / 1错半分 / 2错及以上0分"; } }

        public float CalcScore(float baseScore, int errors)
        {
            if (errors <= 0) return baseScore;
            if (errors == 1) return baseScore * 0.5f;
            return 0f;
        }
    }

    /// <summary>
    /// 线性扣分策略：每次错误扣固定分，最低 0 分（可直接用于“简单/普通/困难”这类难度分级）。
    /// </summary>
    public class LinearPenaltyScorePolicy : IScorePolicy
    {
        public float penaltyPerError = 5f;

        public string Description { get { return $"每次错误扣 {penaltyPerError} 分（最低 0）"; } }

        public float CalcScore(float baseScore, int errors)
        {
            if (errors <= 0) return baseScore;
            float s = baseScore - penaltyPerError * errors;
            return s > 0f ? s : 0f;
        }
    }
}
