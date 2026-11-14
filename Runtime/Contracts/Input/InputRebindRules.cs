using System;
using System.Collections.Generic;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 改键规则：保留键判断与同上下文冲突检测。
    /// <para>默认保留 F1–F12（调试/工具快捷键）。Escape/Return/Tab 属于默认 UI/暂停键，
    /// 不在默认保留列表中，避免“无法把 Cancel 重新绑回 Escape”这类问题；
    /// 需要更严格的规则时可在设置层追加保留键。</para>
    /// </summary>
    public static class InputRebindRules
    {
        private static readonly KeyCode[] defaultReserved =
        {
            KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.F5, KeyCode.F6,
            KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10, KeyCode.F11, KeyCode.F12
        };

        /// <summary>默认保留键（F1–F12）。</summary>
        public static IReadOnlyList<KeyCode> DefaultReservedKeys => defaultReserved;

        /// <summary>是否为默认保留键。</summary>
        public static bool IsReserved(KeyCode key) => Array.IndexOf(defaultReserved, key) >= 0;

        /// <summary>
        /// 两条绑定是否在同一上下文内冲突：
        /// 轴绑定比较轴名；按键绑定比较 positive/negative；跨上下文不冲突；同动作不算冲突。
        /// </summary>
        public static bool Conflicts(InputBinding candidate, InputBinding existing)
        {
            if (candidate == null || existing == null) return false;
            if (candidate.context != existing.context) return false;
            if (candidate.action == existing.action) return false;

            bool candidateAxis = !string.IsNullOrEmpty(candidate.axis);
            bool existingAxis = !string.IsNullOrEmpty(existing.axis);
            if (candidateAxis || existingAxis)
                return candidateAxis && existingAxis && candidate.axis == existing.axis;

            return KeyEquals(candidate.positive, existing.positive, existing.negative)
                || KeyEquals(candidate.negative, existing.positive, existing.negative);
        }

        /// <summary>候选绑定是否与任意现有绑定冲突；返回第一条冲突绑定。</summary>
        public static bool ConflictsWithAny(InputBinding candidate, IEnumerable<InputBinding> existingBindings, out InputBinding conflict)
        {
            conflict = null;
            if (candidate == null || existingBindings == null) return false;
            foreach (var existing in existingBindings)
            {
                if (!Conflicts(candidate, existing)) continue;
                conflict = existing;
                return true;
            }
            return false;
        }

        private static bool KeyEquals(KeyCode key, KeyCode positive, KeyCode negative)
            => key != KeyCode.None && (key == positive || key == negative);
    }
}
