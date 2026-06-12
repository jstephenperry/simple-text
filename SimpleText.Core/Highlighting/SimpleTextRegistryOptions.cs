using System.Collections;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;

namespace SimpleText.Core.Highlighting;

/// <summary>
/// IRegistryOptions over the bundled TextMateSharp.Grammars set, extended with the
/// embedded reStructuredText grammar (not bundled upstream) and a repaired AsciiDoc
/// grammar. Shared by TextMateHighlighter and the AvaloniaEdit TextMate integration.
/// </summary>
public sealed class SimpleTextRegistryOptions : IRegistryOptions
{
    public const string MarkdownScope = "text.html.markdown";
    public const string AsciiDocScope = "document.adoc";
    public const string ReStructuredTextScope = "source.rst";

    private const string RstGrammarResource = "SimpleText.Core.Highlighting.Grammars.rst.tmLanguage.json";

    private readonly RegistryOptions _inner;
    private IRawGrammar? _rstGrammar;
    private IRawGrammar? _asciiDocGrammar;

    public SimpleTextRegistryOptions() : this(ThemeName.LightPlus)
    {
    }

    public SimpleTextRegistryOptions(ThemeName theme)
    {
        _inner = new RegistryOptions(theme);
    }

    public static string? ScopeForMode(string? mode) => mode switch
    {
        TextModes.Markdown => MarkdownScope,
        TextModes.AsciiDoc => AsciiDocScope,
        TextModes.ReStructuredText => ReStructuredTextScope,
        _ => null,
    };

    public IRawTheme LoadTheme(ThemeName name) => _inner.LoadTheme(name);

    public IRawTheme GetTheme(string scopeName) => _inner.GetTheme(scopeName);

    public IRawGrammar GetGrammar(string scopeName)
    {
        if (scopeName == ReStructuredTextScope)
            return _rstGrammar ??= LoadEmbeddedRstGrammar();
        if (scopeName == AsciiDocScope)
            return _asciiDocGrammar ??= FlattenRepositories(_inner.GetGrammar(scopeName));
        return _inner.GetGrammar(scopeName);
    }

    public ICollection<string> GetInjections(string scopeName) => _inner.GetInjections(scopeName);

    public IRawTheme GetDefaultTheme() => _inner.GetDefaultTheme();

    private static IRawGrammar LoadEmbeddedRstGrammar()
    {
        using var stream = typeof(SimpleTextRegistryOptions).Assembly.GetManifestResourceStream(RstGrammarResource)
            ?? throw new InvalidOperationException($"Embedded grammar resource '{RstGrammarResource}' not found.");
        using var reader = new StreamReader(stream);
        return GrammarReader.ReadGrammarSync(reader);
    }

    /// <summary>
    /// TextMateSharp 2.0.4 resolves "#name" includes only against the nearest enclosing
    /// repository instead of the full TextMate repository chain up to the grammar root.
    /// In the bundled AsciiDoc grammar that silently disables every delimited block rule
    /// (listing/literal/example/quote/sidebar/passthrough/comment), because those rules
    /// live in a nested repository but reference root-level rules such as "#callout".
    /// Entry names are globally unique in that grammar, so hoisting every nested
    /// repository entry into the root repository and dropping the nested dictionaries
    /// gives all includes a single resolvable lookup context. The raw grammar model is
    /// dictionary-based (TextMateSharp.Internal.Grammars.Parser.Raw), which this relies on.
    /// </summary>
    private static IRawGrammar FlattenRepositories(IRawGrammar grammar)
    {
        if (grammar is not IDictionary<string, object> root ||
            grammar.GetRepository() is not IDictionary<string, object> rootRepo)
            return grammar;

        var nestedOwners = new List<IDictionary<string, object>>();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Collect(root);

        foreach (var owner in nestedOwners)
        {
            var nested = (IDictionary<string, object>)owner["repository"];
            foreach (var entry in nested)
                if (!rootRepo.ContainsKey(entry.Key))
                    rootRepo[entry.Key] = entry.Value;
            owner.Remove("repository");
        }
        return grammar;

        void Collect(object node)
        {
            if (node is IDictionary<string, object> dict)
            {
                if (!visited.Add(dict))
                    return;
                if (!ReferenceEquals(dict, root) &&
                    dict.TryGetValue("repository", out var repo) &&
                    repo is IDictionary<string, object>)
                    nestedOwners.Add(dict);
                foreach (var value in dict.Values.ToList())
                    Collect(value);
            }
            else if (node is IEnumerable items and not string)
            {
                foreach (var item in items)
                    Collect(item!);
            }
        }
    }
}
