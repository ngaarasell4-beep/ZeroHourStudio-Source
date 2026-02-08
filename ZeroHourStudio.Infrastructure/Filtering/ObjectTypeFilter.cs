using System;
using System.Collections.Generic;
using System.Linq;
using ZeroHourStudio.Infrastructure.Monitoring;

namespace ZeroHourStudio.Infrastructure.Filtering
{
    /// <summary>
    /// فلتر صارم لأنواع الكائنات
    /// يقبل فقط: INFANTRY, VEHICLE, AIRCRAFT
    /// يرفض: BUILDING, STRUCTURE, PROP, DECORATION, OBJECTPRELOAD, CINE_*
    /// </summary>
    public static class ObjectTypeFilter
    {
        private static readonly HashSet<string> CombatTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "INFANTRY",
            "VEHICLE",
            "AIRCRAFT"
        };

        private static readonly HashSet<string> ForbiddenTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "BUILDING",
            "STRUCTURE",
            "PROP",
            "DECORATION",
            "OBJECTPRELOAD",
            "DOZER",
            "SUPPLIED_CRATE",
            "CRATE"
        };

        /// <summary>
        /// فحص صارم: هل الكائن وحدة قتالية؟
        /// </summary>
        public static bool IsCombatUnit(string? kindOf, string? objectName, out string rejectReason)
        {
            rejectReason = string.Empty;

            if (string.IsNullOrWhiteSpace(kindOf))
            {
                rejectReason = "No KindOf specified";
                MonitoringService.Instance.Log("OBJECT_FILTER", objectName ?? "UNKNOWN", "REJECT", rejectReason);
                return false;
            }

            // رفض CINE_ مباشرة
            if (!string.IsNullOrWhiteSpace(objectName) && 
                (objectName.StartsWith("CINE_", StringComparison.OrdinalIgnoreCase) ||
                 objectName.StartsWith("Cine_", StringComparison.Ordinal) ||
                 objectName.StartsWith("cine_", StringComparison.Ordinal)))
            {
                rejectReason = "CINEMATIC object (CINE_ prefix)";
                MonitoringService.Instance.Log("OBJECT_FILTER", objectName, "REJECT", rejectReason);
                return false;
            }

            // تحديد النوع
            string? objectType = null;
            foreach (var combatType in CombatTypes)
            {
                if (kindOf.Contains(combatType, StringComparison.OrdinalIgnoreCase))
                {
                    objectType = combatType;
                    break;
                }
            }

            if (objectType == null)
            {
                // فحص إذا كان نوع ممنوع
                foreach (var forbidden in ForbiddenTypes)
                {
                    if (kindOf.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                    {
                        rejectReason = $"Non-combat type: {forbidden}";
                        MonitoringService.Instance.Log("OBJECT_FILTER", objectName ?? "UNKNOWN", "REJECT", rejectReason);
                        return false;
                    }
                }

                rejectReason = "Not a combat unit type";
                MonitoringService.Instance.Log("OBJECT_FILTER", objectName ?? "UNKNOWN", "REJECT", rejectReason);
                return false;
            }

            // قبول
            MonitoringService.Instance.Log("OBJECT_FILTER", objectName ?? "UNKNOWN", "ACCEPT", $"Type={objectType}");
            return true;
        }

        /// <summary>
        /// استخراج نوع الكائن من KindOf
        /// </summary>
        public static string GetObjectType(string kindOf)
        {
            foreach (var combatType in CombatTypes)
            {
                if (kindOf.Contains(combatType, StringComparison.OrdinalIgnoreCase))
                {
                    return combatType;
                }
            }
            return "UNKNOWN";
        }
    }
}
