using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UPandaGF.EditorTools
{
    /// <summary>
    /// 日志系统自检（菜单：UPandaGF/日志系统/日志自检）。
    ///
    /// 与单元测试不同，这里用 <c>Application.logMessageReceived</c> 抓取**真实产出**的日志来断言链路，
    /// 覆盖 2026-09 修复的每一项：cfg 默认实例、24 小时制时间、null 安全、LogFormat 顺序、
    /// 级别开关、长度截断、富文本剥离、LogConfig 的 Equals/GetHashCode 契约、LogException。
    ///
    /// 注意：未定义 OPEN_PLOG 时所有 PLogger 调用已被编译期剔除，链路断言会自动跳过（只留配置类断言）。
    /// </summary>
    public static class PLoggerSelfCheck
    {
        private static int total;
        private static readonly List<string> fails = new List<string>();
        private static readonly List<string> skips = new List<string>();
        private static readonly List<string> captured = new List<string>();

        private static void OnLogReceived(string condition, string stackTrace, LogType type)
        {
            captured.Add(condition);
        }

        [MenuItem("UPandaGF/日志系统/日志自检", false, 21)]
        public static void Run()
        {
            EditorUtility.DisplayDialog("日志系统自检", RunSilent(), "确定");
        }

        /// <summary>无弹窗版本：返回报告字符串（弹窗会阻塞编辑器，自动化 / CI 请用这个入口）</summary>
        public static string RunSilent()
        {
            total = 0;
            fails.Clear();
            skips.Clear();
            captured.Clear();

            LogConfig backup = new LogConfig(PLogger.cfg);
            Application.logMessageReceived += OnLogReceived;
            try
            {
                // ---------- 配置类断言（与宏无关） ----------
                Check(() => PLogger.cfg != null, "PLogger.cfg 不为 null（配置缺失也不会让整个日志系统静默失效）");

                LogConfig a = new LogConfig();
                LogConfig b = new LogConfig();
                Check(() => a.Equals(b) && a.GetHashCode() == b.GetHashCode(), "LogConfig：Equals 与 GetHashCode 一致");
                b.openError = false;
                Check(() => !a.Equals(b), "LogConfig：字段不同则判为不等");
                Check(() => new LogConfig(a).Equals(a), "LogConfig：复制构造产生相等配置");

                // ---------- 链路断言（需要 OPEN_PLOG） ----------
#if !OPEN_PLOG
                skips.Add("当前未定义 OPEN_PLOG：所有 PLogger 调用已在编译期被剔除，跳过链路断言（菜单 UPandaGF/日志系统/启动日志 可启用）");
#else
                // 1) 链路 + 前缀 + 时间(HH) + 线程号
                captured.Clear();
                PLogger.Log("自检-链路");
                Check(() => Find("自检-链路") != null, "日志链路可通（能抓到自己发出的日志）");
                Check(() =>
                {
                    string s = Find("自检-链路");
                    return s != null && s.StartsWith("<" + backup.logHeadFix + ">");
                }, "日志前缀生效");
                Check(() =>
                {
                    string s = Find("自检-链路");
                    return s != null && s.Contains(DateTime.Now.ToString("HH:mm"));
                }, "时间戳为 24 小时制 HH:mm（原实现用 hh，12 小时制）");
                Check(() =>
                {
                    string s = Find("自检-链路");
                    return s != null && s.Contains("ThreadID:" + Thread.CurrentThread.ManagedThreadId);
                }, "线程号生效");

                // 2) null 安全
                captured.Clear();
                bool nullSafe = true;
                try { PLogger.Log(null); } catch (Exception) { nullSafe = false; }
                Check(() => nullSafe && Find("null") != null, "PLogger.Log(null) 不抛异常且输出 null");

                // 3) LogFormat：先格式化再拼前缀
                captured.Clear();
                PLogger.LogFormat("HP={0}", 7);
                Check(() => Find("HP=7") != null, "LogFormat 能正确替换参数");

                captured.Clear();
                PLogger.cfg.logHeadFix = "{0}";
                bool braceSafe = true;
                try { PLogger.LogFormat("x={0}", 1); } catch (Exception) { braceSafe = false; }
                Check(() => braceSafe && Find("x=1") != null, "前缀含 { } 时 LogFormat 不抛异常（原实现会抛 FormatException）");
                PLogger.cfg.logHeadFix = backup.logHeadFix;

                captured.Clear();
                bool badFormatSafe = true;
                try { PLogger.LogFormat("坏格式 {0}{1}", 1); } catch (Exception) { badFormatSafe = false; }
                Check(() => badFormatSafe, "格式串与参数不匹配时降级输出，不打断业务");

                // 4) 级别开关
                captured.Clear();
                PLogger.cfg.openWarning = false;
                PLogger.LogWarning("自检-warning");
                PLogger.LogError("自检-error");
                Check(() => Find("自检-warning") == null && Find("自检-error") != null,
                    "openWarning=false 时 Warning 静默、Error 仍输出");
                PLogger.cfg.openWarning = backup.openWarning;

                captured.Clear();
                PLogger.cfg.openLog = false;
                PLogger.Log("自检-总开关关闭");
                Check(() => Find("自检-总开关关闭") == null, "openLog=false 时普通日志静默");
                PLogger.cfg.openLog = backup.openLog;

                // 5) 长度截断
                captured.Clear();
                PLogger.cfg.maxLogLength = 5;
                PLogger.Log("abcdefghij");
                Check(() => Find("abcde") != null && Find("已截断") != null, "maxLogLength 会截断超长日志");
                PLogger.cfg.maxLogLength = backup.maxLogLength;

                // 6) LogException
                captured.Clear();
                PLogger.LogException("自检-异常");
                Check(() => Find("[Exception]") != null, "LogException 可用");
#endif

                // ---------- 富文本剥离（反射调用私有静态方法，与宏无关） ----------
                MethodInfo strip = typeof(PLogHelper).GetMethod("StripRichText", BindingFlags.NonPublic | BindingFlags.Static);
                Check(() => strip != null, "PLogHelper.StripRichText 存在（写文件时会剥掉富文本标签）");
                if (strip != null)
                {
                    Check(() => (string)strip.Invoke(null, new object[] { "<color=red>血条</color>" }) == "血条",
                        "富文本标签被剥离（日志文件里不再出现 <color=...> 噪音）");
                    Check(() => (string)strip.Invoke(null, new object[] { "a < b && c > d" }) == "a < b && c > d",
                        "正文里的 < > 不会被误删");
                }
            }
            finally
            {
                Application.logMessageReceived -= OnLogReceived;
                PLogger.cfg = backup;   // 还原自检期间改过的配置
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"日志系统自检：{total - fails.Count} / {total} 项通过");
            if (skips.Count > 0)
            {
                sb.AppendLine($"（跳过 {skips.Count} 项）");
                foreach (string s in skips) sb.AppendLine("· " + s);
            }
            if (fails.Count > 0)
            {
                sb.AppendLine(new string('-', 40));
                foreach (string f in fails) sb.AppendLine("× " + f);
            }
            sb.AppendLine();
            sb.AppendLine("自检会产生若干条测试日志（含 1 条 Error 与 1 条 Exception），属于预期结果。");

            string report = sb.ToString();
            if (fails.Count == 0) Debug.Log("[日志自检] " + report);
            else Debug.LogError("[日志自检] " + report);
            return report;
        }

        private static string Find(string fragment)
        {
            for (int i = 0; i < captured.Count; i++)
            {
                if (captured[i] != null && captured[i].Contains(fragment)) return captured[i];
            }
            return null;
        }

        private static void Check(Func<bool> assert, string name)
        {
            total++;
            bool ok;
            try { ok = assert(); }
            catch (Exception e)
            {
                ok = false;
                Debug.LogError($"[日志自检] 断言「{name}」执行异常：{e.Message}");
            }
            if (!ok) fails.Add(name);
        }
    }
}
