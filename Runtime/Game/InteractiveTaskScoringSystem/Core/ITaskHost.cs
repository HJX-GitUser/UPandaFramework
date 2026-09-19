namespace UPandaGF.RunTime.InteractiveTaskScoringSystem
{
    /// <summary>
    /// 任务宿主接口：交互实体只依赖这个接口，而不是具体的 <see cref="TaskDataManager"/> 单例。
    ///
    /// 好处：① 可以在没有场景的情况下注入假的宿主做逻辑测试；
    ///      ② 将来支持“一台机器跑多份任务/多个学员”时，宿主可以换成实例而不是单例。
    /// 默认实现仍是场景里的 TaskDataManager（它实现本接口）。
    /// </summary>
    public interface ITaskHost
    {
        /// <summary>提交一次交互点击，交给当前步骤做操作检查</summary>
        void OperationCheck(TaskEntityBase arg);

        /// <summary>触发当前步骤的引导提示</summary>
        void OperationInstructions();

        /// <summary>跳过当前步骤</summary>
        void SkipTask();

        /// <summary>
        /// 记录一条动作级日志（P2 新增，用于回放复盘）。
        /// 由步骤/实体在关键节点调用：Step_Enter / Step_Error / Step_Skip / Step_Complete 等。
        /// </summary>
        /// <param name="stepID">所属步骤ID（与步骤无关的任务级事件可传 null）</param>
        /// <param name="action">动作名，建议使用 Step_ 前缀的动作常量</param>
        /// <param name="isCorrect">该动作是否算正向（错误动作为 false）</param>
        /// <param name="details">可读描述</param>
        void RecordStepAction(string stepID, string action, bool isCorrect, string details);
    }
}
