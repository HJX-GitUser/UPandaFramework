namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 一次评分的上下文。
    /// 默认策略只用到 baseScore / errors；需要难度分级、超时扣分等的策略可额外读 duration / isSkip。
    /// （纯 C# 结构体，不依赖 UnityEngine）
    /// </summary>
    public struct ScoreContext
    {
        public string stepID;      // 步骤ID（便于日志与调试）
        public float baseScore;    // 配置里的基础分
        public int errors;         // 本步错误次数
        public float duration;     // 本步耗时（秒；未完成时为 0）
        public bool isSkip;        // 本步是否被跳过
    }

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
    /// 增强版评分策略：比 <see cref="IScorePolicy"/> 多拿一个 <see cref="ScoreContext"/>（含本步耗时）。
    ///
    /// 为什么另开接口而不是直接改 IScorePolicy：已经写好的自定义策略（只实现两个参数）不用改就能继续用；
    /// <see cref="TaskStepBase"/> 会优先调用本接口，实现类没实现时才回退到 <c>CalcScore(baseScore, errors)</c>。
    /// </summary>
    public interface IScorePolicyEx : IScorePolicy
    {
        float CalcScore(ScoreContext context);
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
