namespace ZeroHourStudio.Domain.Entities
{
    public class ConflictReport
    {
        public string UnitName { get; set; } = string.Empty;
        public List<ConflictEntry> Conflicts { get; set; } = new();
        public bool HasConflicts => Conflicts.Count > 0;
        public int DuplicateCount => Conflicts.Count(c => c.Kind == ConflictKind.Duplicate);
        public int FileOverwriteCount => Conflicts.Count(c => c.Kind == ConflictKind.FileOverwrite);
    }

    public class ConflictEntry
    {
        public string DefinitionName { get; set; } = string.Empty;
        public string DefinitionType { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public string TargetFile { get; set; } = string.Empty;
        public ConflictKind Kind { get; set; }
        public string SuggestedRename { get; set; } = string.Empty;
    }

    public enum ConflictKind
    {
        Duplicate,
        NameCollision,
        FileOverwrite
    }
}
