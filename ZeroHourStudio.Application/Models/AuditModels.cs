using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroHourStudio.Application.Models
{
    public enum AuditSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public enum AuditCategory
    {
        MissingFile,        // File not found on disk
        BrokenLink,         // Reference to non-existent object
        EmptyKey,           // Key has no value
        LogicError,         // Logic inconsistency
        SyntaxError         // INI syntax issue
    }

    public class AuditIssue
    {
        public AuditSeverity Severity { get; set; }
        public AuditCategory Category { get; set; }
        public string Message { get; set; } = "";
        public string Location { get; set; } = ""; // E.g., "Weapon.ini [WeaponName]"
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    public class DiagnosticReport
    {
        public string UnitName { get; set; } = "";
        public DateTime ScanTime { get; set; } = DateTime.Now;
        public List<AuditIssue> Issues { get; set; } = new List<AuditIssue>();
        public int ErrorCount => Issues.Count(i => i.Severity == AuditSeverity.Error || i.Severity == AuditSeverity.Critical);
        public int WarningCount => Issues.Count(i => i.Severity == AuditSeverity.Warning);
    }
}
