namespace FlipNoteChrome.Core.Storage;

public sealed class Note
{
    public string StableKey { get; set; } = "";
    public string ExePath { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string NormalizedTitle { get; set; } = "";
    public string TitleHash { get; set; } = "";
    public string AliasesJson { get; set; } = "[]"; // JSON array of alternative normalized titles
    public string Markdown { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool Pinned { get; set; }

    // Non-persisted helpers
    public int WordCount => string.IsNullOrWhiteSpace(Markdown) ? 0 : Markdown.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
    public int CharCount => Markdown?.Length ?? 0;
}
