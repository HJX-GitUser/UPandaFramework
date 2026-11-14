using System;
using UnityEngine;

namespace UPandaGF
{
    /// <summary>
    /// 玩家键位覆盖层（可 JSON 持久化的强类型 POCO）。
    /// <para>只保存“相对默认表的变化”（见 <see cref="IInputService.ExportOverrides"/>），
    /// 新版本新增动作时自动回落到开发者默认键位，不会被旧覆盖层覆盖。</para>
    /// </summary>
    [Serializable]
    public sealed class InputBindingOverlay
    {
        /// <summary>覆盖层结构版本，升级格式时递增并做迁移。</summary>
        public int schemaVersion = 1;

        /// <summary>被玩家修改/新增的绑定。</summary>
        public InputBinding[] bindings = Array.Empty<InputBinding>();

        public static string ToJson(InputBindingOverlay overlay)
            => JsonUtility.ToJson(overlay ?? new InputBindingOverlay(), true);

        /// <summary>解析 JSON；空文本返回 null，格式错误时抛 <see cref="ArgumentException"/>。</summary>
        public static InputBindingOverlay FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var overlay = JsonUtility.FromJson<InputBindingOverlay>(json);
            if (overlay == null) return null;
            if (overlay.bindings == null) overlay.bindings = Array.Empty<InputBinding>();
            return overlay;
        }
    }
}
