namespace UPandaGF
{
    /// <summary>
    /// 输入上下文名常量。
    /// <para>0.8.1 之前上下文名是裸字符串（"Gameplay"/"UI"），拼写错误只会表现为
    /// “输入忽然失效/屏蔽失效”。0.8.2 起统一使用这里的常量，并在
    /// <c>InputService.PushContext</c> 中拒绝空名。</para>
    /// <para>自定义上下文仍允许：业务可定义自己的常量，但所有“需要被暂停屏蔽”的上下文
    /// 仍应使用 <see cref="Gameplay"/>，或自行压入 <see cref="UI"/> / <see cref="Rebind"/>
    /// 阻断层。</para>
    /// </summary>
    public static class InputContexts
    {
        /// <summary>游戏玩法上下文：暂停时会自动失效（InputService.Allowed 的既有语义）。</summary>
        public const string Gameplay = "Gameplay";

        /// <summary>UI 上下文：暂停时仍可读取，通常由模态面板/暂停菜单压栈阻断 Gameplay。</summary>
        public const string UI = "UI";

        /// <summary>按键捕获上下文：设置系统重绑键位时压栈，屏蔽普通游戏/UI 动作。</summary>
        public const string Rebind = "Rebind";

        /// <summary>演出/过场上下文（预留）：阻断 Gameplay，UI 是否可用由宿主决定。</summary>
        public const string Cutscene = "Cutscene";
    }
}
