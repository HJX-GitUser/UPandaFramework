using System;

namespace UPandaGF
{
    /// <summary>
    /// 首启隐私同意的数据契约与最小操作入口：
    /// <see cref="PrivacyOptions.termsVersion"/> = 0 未处理、-1 拒绝、正数已同意的条款版本。
    /// <para>UI 流程由项目实现；这里保证同意状态可持久化、可判断、可测试，避免业务各自约定字段。</para>
    /// </summary>
    public static class SettingsConsent
    {
        public const int CurrentTermsVersion = 1;

        public static bool NeedsConsent(this ISettingsService settings)
        {
            if (settings == null) return false;
            return settings.Get<int>("privacy.termsVersion") == 0;
        }

        public static bool IsAccepted(this ISettingsService settings)
        {
            if (settings == null) return false;
            return settings.Get<int>("privacy.termsVersion") >= CurrentTermsVersion;
        }

        public static bool IsDeclined(this ISettingsService settings)
        {
            if (settings == null) return false;
            return settings.Get<int>("privacy.termsVersion") < 0;
        }

        /// <summary>记录同意并写入三类 SDK 开关；任何一步失败返回 false。</summary>
        public static bool Accept(this ISettingsService settings, bool telemetry, bool crashReport, bool personalizedAds)
        {
            if (settings == null) return false;
            bool ok = settings.Set("privacy.telemetry", telemetry);
            ok &= settings.Set("privacy.crashReport", crashReport);
            ok &= settings.Set("privacy.personalizedAds", personalizedAds);
            ok &= settings.Set("privacy.consentUtc", DateTime.UtcNow.ToString("o"));
            ok &= settings.Set("privacy.termsVersion", CurrentTermsVersion);
            ok &= settings.Set("system.firstRunCompleted", true);
            return ok;
        }

        /// <summary>拒绝：所有可选 SDK 关闭，记录 -1，避免每次启动重复弹窗。</summary>
        public static bool Decline(this ISettingsService settings)
        {
            if (settings == null) return false;
            bool ok = settings.Set("privacy.telemetry", false);
            ok &= settings.Set("privacy.crashReport", false);
            ok &= settings.Set("privacy.personalizedAds", false);
            ok &= settings.Set("privacy.consentUtc", DateTime.UtcNow.ToString("o"));
            ok &= settings.Set("privacy.termsVersion", -1);
            ok &= settings.Set("system.firstRunCompleted", true);
            return ok;
        }
    }
}
