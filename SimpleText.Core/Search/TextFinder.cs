namespace SimpleText.Core.Search;

public static class TextFinder
{
    public static int? FindNext(string text, string term, int startOffset)
    {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(text)) return null;

        int foundIndex = text.IndexOf(term, startOffset, StringComparison.OrdinalIgnoreCase);
        if (foundIndex < 0)
            foundIndex = text.IndexOf(term, 0, StringComparison.OrdinalIgnoreCase);

        return foundIndex >= 0 ? foundIndex : null;
    }

    public static int? FindPrevious(string text, string term, int startOffset)
    {
        if (string.IsNullOrEmpty(term) || string.IsNullOrEmpty(text)) return null;

        int searchStart = startOffset - 1;
        if (searchStart < 0) searchStart = text.Length - 1;

        int foundIndex = text.LastIndexOf(term, searchStart, StringComparison.OrdinalIgnoreCase);
        if (foundIndex < 0)
            foundIndex = text.LastIndexOf(term, text.Length - 1, StringComparison.OrdinalIgnoreCase);

        return foundIndex >= 0 ? foundIndex : null;
    }
}
