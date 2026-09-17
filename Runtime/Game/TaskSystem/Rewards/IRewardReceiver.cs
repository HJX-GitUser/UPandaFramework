namespace UPandaGF.RunTime.TaskSystem
{
    /// <summary>
    /// 奖励接收端：任务系统只负责"说出要发什么"，由具体实现决定发给谁、怎么发。
    /// 默认实现见 PlayerRewardService（也可自行实现，接到背包 / 角色属性系统上）。
    /// </summary>
    public interface IRewardReceiver
    {
        void GrantExperience(int amount);
        void GrantGold(int amount);
        void GrantItem(string itemId, int amount);
    }
}
