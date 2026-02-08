using System;
using System.Collections.Generic;
using System.Linq;
using ZeroHourStudio.Application.Models;
using ZeroHourStudio.Infrastructure.Monitoring;

namespace ZeroHourStudio.Infrastructure.Filtering
{
    /// <summary>
    /// تحقق صارم من اكتمال الأسلحة
    /// يرفض أي سلاح ناقص تلقائياً
    /// </summary>
    public class WeaponCompletionValidator
    {
        /// <summary>
        /// التحقق الصارم: هل السلاح كامل وصالح للنقل؟
        /// </summary>
        public bool IsWeaponComplete(WeaponChain weapon, out string rejectReason)
        {
            rejectReason = string.Empty;

            // فحص 1: اسم السلاح
            if (string.IsNullOrWhiteSpace(weapon.WeaponName))
            {
                rejectReason = "No weapon name";
                MonitoringService.Instance.Log("WEAPON_VALIDATE", "UNKNOWN", "REJECT", rejectReason);
                return false;
            }

            var weaponName = weapon.WeaponName;

            // فحص 2: Projectile (إجباري)
            if (string.IsNullOrWhiteSpace(weapon.ProjectileName))
            {
                rejectReason = "Missing projectile";
                MonitoringService.Instance.Log("WEAPON_VALIDATE", weaponName, "REJECT", rejectReason);
                return false;
            }

            // فحص 3: عدد الملفات المرتبطة
            if (weapon.RelatedFiles == null || weapon.RelatedFiles.Count == 0)
            {
                rejectReason = "No related files";
                MonitoringService.Instance.Log("WEAPON_VALIDATE", weaponName, "REJECT", rejectReason);
                return false;
            }

            // فحص 4: الملفات المفقودة
            if (weapon.MissingFiles != null && weapon.MissingFiles.Count > 0)
            {
                rejectReason = $"{weapon.MissingFiles.Count} missing files: {string.Join(", ", weapon.MissingFiles.Take(3))}";
                MonitoringService.Instance.Log("WEAPON_VALIDATE", weaponName, "REJECT", rejectReason,
                    $"Missing: {string.Join(", ", weapon.MissingFiles)}");
                return false;
            }

            // فحص 5: علامة الاكتمال
            if (!weapon.IsComplete)
            {
                rejectReason = "Weapon marked as incomplete";
                MonitoringService.Instance.Log("WEAPON_VALIDATE", weaponName, "REJECT", rejectReason);
                return false;
            }

            // فحص 6: فحص الحدود
            var depCount = weapon.RelatedFiles?.Count ?? 0;
            if (!DependencyLimits.IsWithinDependencyLimit(depCount, weaponName, out var limitReason))
            {
                rejectReason = limitReason;
                return false;
            }

            // قبول السلاح
            MonitoringService.Instance.Log("WEAPON_VALIDATE", weaponName, "ACCEPT", "Complete weapon",
                $"Files: {weapon.RelatedFiles?.Count}, Projectile: {weapon.ProjectileName}");
            return true;
        }
    }
}
