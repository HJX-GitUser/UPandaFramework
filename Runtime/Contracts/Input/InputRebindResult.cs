namespace UPandaGF
{
    /// <summary>
    /// 运行时改键结果（<see cref="IInputService.TryRebind"/>）。
    /// </summary>
    public enum InputRebindResult
    {
        /// <summary>改键成功或绑定无变化（同动作同键）。</summary>
        Success,
        /// <summary>绑定非法：action/context 为空、axis 与按键同时存在或都为空。</summary>
        InvalidBinding,
        /// <summary>与同一上下文内其它动作冲突。</summary>
        Conflict,
        /// <summary>按键在保留键列表中（默认 F1–F12，可由实现覆盖）。</summary>
        ReservedKey
    }
}
