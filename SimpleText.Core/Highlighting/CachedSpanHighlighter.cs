namespace SimpleText.Core.Highlighting;

/// <summary>
/// Base for highlighters backed by a real parser: the whole document is parsed once per
/// text version (correctness for multi-line constructs like code fences requires full-document
/// context), cached, and region requests are served by filtering the cached spans.
/// Not thread-safe — frontends call from their UI thread only.
/// </summary>
public abstract class CachedSpanHighlighter : ISpanHighlighter
{
    /// <summary>
    /// Above this size we skip parsing entirely: a full reparse runs synchronously on the UI
    /// thread on every debounce tick, so a multi-megabyte document would freeze typing.
    /// Highlighting silently disables for such files rather than stall the editor.
    /// </summary>
    private const int MaxParseLength = 2_000_000;

    /// <summary>
    /// A single highlighter instance is shared across every same-mode tab (the registry hands
    /// out singletons), so a single-slot cache would thrash — re-parsing the whole document on
    /// every scroll/region pass whenever the user alternates tabs. A small LRU keyed by text
    /// keeps each recently-viewed document parsed. Most-recently-used entry is last.
    /// </summary>
    private const int CacheCapacity = 8;

    private readonly List<(string Text, List<HighlightSpan> Spans)> _cache = new(CacheCapacity);

    public abstract string FileTypeName { get; }

    /// <summary>
    /// Parses the full document and returns all spans. Overlapping spans are allowed;
    /// the base class orders them (outer constructs before inner) so that appliers,
    /// which let later spans win, style nested constructs correctly. May throw on
    /// pathological input — the base class contains the fault and degrades to no-highlight.
    /// </summary>
    protected abstract List<HighlightSpan> ParseDocument(string text);

    private List<HighlightSpan> GetOrParse(string text)
    {
        for (int i = 0; i < _cache.Count; i++)
        {
            if (string.Equals(_cache[i].Text, text, StringComparison.Ordinal))
            {
                var hit = _cache[i];
                if (i != _cache.Count - 1)
                {
                    _cache.RemoveAt(i);
                    _cache.Add(hit); // promote to most-recently-used
                }
                return hit.Spans;
            }
        }

        List<HighlightSpan> spans;
        if (text.Length > MaxParseLength)
        {
            spans = [];
        }
        else
        {
            try
            {
                spans = ParseDocument(text);
            }
            catch
            {
                // A parser fault (e.g. Markdig's nesting-depth limit on adversarial input)
                // must degrade to no-highlight, never escape to the caller's UI message loop.
                spans = [];
            }

            // Outer-before-inner: ascending start, then longer span first on ties.
            spans.Sort(static (a, b) => a.Start != b.Start
                ? a.Start.CompareTo(b.Start)
                : b.Length.CompareTo(a.Length));
        }

        if (_cache.Count >= CacheCapacity)
            _cache.RemoveAt(0); // evict least-recently-used
        _cache.Add((text, spans));
        return spans;
    }

    public List<HighlightSpan> GetHighlights(string text, int startOffset, int length)
    {
        var allSpans = GetOrParse(text);

        int regionEnd = startOffset + length;
        var result = new List<HighlightSpan>();
        foreach (var span in allSpans)
        {
            if (span.Start >= regionEnd) break;
            int end = span.Start + span.Length;
            if (end <= startOffset) continue;

            // Clamp to the region so one huge construct (e.g. a fenced block spanning the
            // whole file) does not make every visible-region pass format the entire document.
            int clampedStart = Math.Max(span.Start, startOffset);
            int clampedEnd = Math.Min(end, regionEnd);
            result.Add(new HighlightSpan(clampedStart, clampedEnd - clampedStart, span.Kind));
        }
        return result;
    }
}
