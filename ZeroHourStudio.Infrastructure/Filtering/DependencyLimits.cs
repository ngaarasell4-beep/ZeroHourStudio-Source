using System;
using ZeroHourStudio.Infrastructure.Monitoring;

namespace ZeroHourStudio.Infrastructure.Filtering
{
    /// <summary>
    /// حدود صارمة غير قابلة للتفاوض
    /// </summary>
    public static class DependencyLimits
    {
        /// <summary>
        /// الحد الأقصى لعدد التبعيات لسلاح واحد
        /// </summary>
        public const int MAX_DEPENDENCIES = 80;

        /// <summary>
        /// الحد الأقصى لعمق سلسلة التبعيات
        /// </summary>
        public const int MAX_DEPTH = 4;

        /// <summary>
        /// فحص صارم: هل عدد التبعيات مقبول؟
        /// </summary>
        public static bool IsWithinDependencyLimit(int count, string weaponName, out string rejectReason)
        {
            if (count > MAX_DEPENDENCIES)
            {
                rejectReason = $"Dependency overflow: {count} > {MAX_DEPENDENCIES}";
                MonitoringService.Instance.Log("DEPENDENCY_LIMIT", weaponName, "REJECT", rejectReason, 
                    $"Weapon has {count} dependencies which exceeds limit of {MAX_DEPENDENCIES}");
                return false;
            }

            rejectReason = string.Empty;
            return true;
        }

        /// <summary>
        /// فحص صارم: هل العمق مقبول؟
        /// </summary>
        public static bool IsWithinDepthLimit(int depth, string weaponName, out string rejectReason)
        {
            if (depth > MAX_DEPTH)
            {
                rejectReason = $"Depth overflow: {depth} > {MAX_DEPTH}";
                MonitoringService.Instance.Log("DEPTH_LIMIT", weaponName, "REJECT", rejectReason,
                    $"Dependency chain depth {depth} exceeds limit of {MAX_DEPTH}");
                return false;
            }

            rejectReason = string.Empty;
            return true;
        }

        /// <summary>
        /// تسجيل رفض بسبب تجاوز الحدود
        /// </summary>
        public static void LogLimitViolation(string weaponName, string limitType, int actual, int max)
        {
            var reason = $"{limitType} exceeded: {actual} > {max}";
            MonitoringService.Instance.Log("LIMIT_VIOLATION", weaponName, "REJECT", reason,
                $"Actual: {actual}, Max: {max}, Violation: {actual - max} over limit");
        }
    }
}
