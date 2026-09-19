using UnityEngine;
using UnityEngine.UI;

namespace UPandaGF
{
    /// <summary>
    /// Unity 内置字体 / 系统字体的统一入口 —— 编辑器工具与运行时 UI 都从这里取字体。
    ///
    /// 为什么需要它：
    ///   ① 内置字体名**随 Unity 版本变化**：≤2021.3 是 "Arial.ttf"，2022.1+ 才是 "LegacyRuntime.ttf"；
    ///   ② <c>Resources.GetBuiltinResource&lt;Font&gt;(名字)</c> 在名字不存在时会**直接往控制台打一条 error**
    ///      （不是静默返回 null），所以不能写成"两个名字轮流试"；
    ///   ③ 内置字体不含中文字形，运行时需要换成系统动态字体
    ///      （动态字体不是资产、不能序列化进场景，只能在运行时赋值）。
    ///
    /// 选用建议：
    ///   · 编辑器工具搭 UI（字体要写进场景 / 预制体）→ <see cref="Get"/>          —— 可序列化的内置字体资产
    ///   · 运行时代码                                → <see cref="GetDefault"/>   —— 优先带中文字形的系统字体
    ///   · 已有 UI 批量换字体                        → <see cref="ReplaceBuiltinFonts"/>
    /// </summary>
    public static class UnityBuiltinFont
    {
        private static Font builtinFont;
        private static bool builtinFontResolved;
        private static Font cjkRuntimeFont;

        /// <summary>Unity 2022.1 起内置字体从 Arial.ttf 改名为 LegacyRuntime.ttf。</summary>
        public static bool IsUnity2022OrNewer
        {
            get
            {
                string version = Application.unityVersion;      // 形如 "2021.3.23f1"
                if (string.IsNullOrEmpty(version)) return false;

                int dot = version.IndexOf('.');
                if (dot <= 0) return false;

                int major;
                if (!int.TryParse(version.Substring(0, dot), out major)) return false;
                return major >= 2022;
            }
        }

        /// <summary>
        /// Unity 内置字体资产（可安全写进场景 / 预制体）。
        /// 整个进程只解析一次；取不到时只警告一次，避免刷屏。
        /// </summary>
        public static Font Get()
        {
            if (builtinFont != null) return builtinFont;
            if (builtinFontResolved) return null;           // 已确认取不到，不再重复尝试

            builtinFontResolved = true;
            string fontName = IsUnity2022OrNewer ? "LegacyRuntime.ttf" : "Arial.ttf";
            builtinFont = Resources.GetBuiltinResource<Font>(fontName);

            if (builtinFont == null)
            {
                Debug.LogWarning("[UnityBuiltinFont] 取内置字体失败（" + fontName + "），" +
                                 "请给 Text 手动指定一个字体资产。");
            }
            return builtinFont;
        }

        /// <summary>
        /// 运行时动态创建的系统字体（按优先级挑一个带中文字形的），**仅在 Play 模式下可用**；
        /// 编辑器（未运行）返回 null。动态字体不是资产，别把它写进场景 / 预制体。
        /// </summary>
        public static Font GetCjkRuntimeFont()
        {
            if (cjkRuntimeFont != null) return cjkRuntimeFont;
            if (!Application.isPlaying) return null;

            string[] candidates = new string[]
            {
                "Microsoft YaHei", "微软雅黑", "SimHei", "黑体", "SimSun",
                "PingFang SC", "Hiragino Sans GB", "Noto Sans CJK SC", "Source Han Sans SC", "Arial"
            };

            try
            {
                cjkRuntimeFont = Font.CreateDynamicFontFromOSFont(candidates, 24);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[UnityBuiltinFont] 创建系统字体失败，将退回内置字体：" + exception.Message);
            }
            return cjkRuntimeFont;
        }

        /// <summary>默认字体：运行期优先系统字体（含中文），编辑器期用内置字体资产（可序列化）。</summary>
        public static Font GetDefault()
        {
            if (Application.isPlaying)
            {
                Font runtimeFont = GetCjkRuntimeFont();
                if (runtimeFont != null) return runtimeFont;
            }
            return Get();
        }

        /// <summary>判断传入字体是不是 Unity 内置字体（内置字体没有中文字形，运行时通常要替换掉）。</summary>
        public static bool IsBuiltin(Font font)
        {
            if (font == null) return false;
            return font.name == "Arial" || font.name == "LegacyRuntime";
        }

        /// <summary>
        /// 把整棵 UI 里"字体为空或仍是内置字体"的 Text 换成运行时中文字体（只在 Play 模式下生效）。
        /// </summary>
        public static void ReplaceBuiltinFonts(GameObject root)
        {
            if (root == null || !Application.isPlaying) return;

            Font font = GetCjkRuntimeFont();
            if (font == null) return;

            Text[] texts = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].font == null || IsBuiltin(texts[i].font)) texts[i].font = font;
            }
        }
    }
}
