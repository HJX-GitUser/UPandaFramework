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
    }
}
