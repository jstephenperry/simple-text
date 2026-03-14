namespace SimpleText.Core.FileTypes;

public sealed record FileTypeInfo(
    string DisplayName,
    string PrimaryExtension,
    string? ModeKey = null,
    params string[] AlternateExtensions)
{
    public IEnumerable<string> AllExtensions
    {
        get
        {
            yield return PrimaryExtension;
            foreach (var ext in AlternateExtensions)
                yield return ext;
        }
    }

    public bool MatchesExtension(string? extension)
    {
        if (extension == null) return false;
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return AllExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }
}
