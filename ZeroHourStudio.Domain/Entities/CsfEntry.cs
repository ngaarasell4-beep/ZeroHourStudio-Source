namespace ZeroHourStudio.Domain.Entities
{
    public class CsfEntry
    {
        public string Label { get; set; } = string.Empty;
        public string EnglishText { get; set; } = string.Empty;
        public string ArabicText { get; set; } = string.Empty;

        public CsfEntry() { }

        public CsfEntry(string label, string englishText, string arabicText = "")
        {
            Label = label;
            EnglishText = englishText;
            ArabicText = arabicText;
        }

        public override string ToString() => $"{Label}: {EnglishText}";
    }
}
