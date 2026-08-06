using System.IO;
using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace SMILE.Desktop.Highlighting;

public static class SyntaxHighlightingCatalog
{
    private const string ResourcePrefix = "SMILE.Desktop.Highlighting.";

    private static readonly Lazy<IHighlightingDefinition> CSharpDefinition =
        BuiltIn("C#", "csharp");
    private static readonly Lazy<IHighlightingDefinition> CFamilyDefinition =
        BuiltIn("C++", "c-family");
    private static readonly Lazy<IHighlightingDefinition> JavaScriptDefinition =
        BuiltIn("JavaScript", "javascript");
    private static readonly Lazy<IHighlightingDefinition> JavaDefinition =
        BuiltIn("Java", "java");
    private static readonly Lazy<IHighlightingDefinition> PythonDefinition =
        BuiltIn("Python", "python");

    private static readonly IReadOnlyDictionary<string, Lazy<IHighlightingDefinition>> Definitions =
        new Dictionary<string, Lazy<IHighlightingDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            ["smile"] = Embedded("SMILE.xshd", "smile"),
            ["csharp"] = CSharpDefinition,
            ["c"] = CFamilyDefinition,
            ["masm-x64"] = Embedded("MasmX64.xshd", "masm-x64"),
            ["javascript"] = JavaScriptDefinition,
            ["java"] = JavaDefinition,
            ["cobol"] = Embedded("Cobol.xshd", "cobol"),
            // SMILE's current Objective-C output is a Foundation-free console
            // profile built from C-compatible syntax, so AvalonEdit's mature
            // C/C++ highlighter gives learners useful colors without putting a
            // custom Objective-C regex set on the UI-thread language switch path.
            ["objective-c"] = CFamilyDefinition,
            ["swift"] = Embedded("Swift.xshd", "swift"),
            ["python"] = PythonDefinition,
            ["cpp"] = CFamilyDefinition
        };

    public static IHighlightingDefinition? GetDefinition(string? languageId)
    {
        string? trimmed = languageId?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        return Definitions.TryGetValue(trimmed, out Lazy<IHighlightingDefinition>? definition)
            ? definition.Value
            : null;
    }

    private static Lazy<IHighlightingDefinition> BuiltIn(string builtInName, string languageId) =>
        new(() => HighlightingPalette.Apply(
            languageId,
            HighlightingManager.Instance.GetDefinition(builtInName) ??
                throw new InvalidOperationException(
                    $"Built-in AvalonEdit highlighting '{builtInName}' for '{languageId}' was not found.")));

    private static Lazy<IHighlightingDefinition> Embedded(string fileName, string languageId) =>
        new(() => HighlightingPalette.Apply(
            languageId,
            LoadEmbedded(ResourcePrefix + fileName)));

    private static IHighlightingDefinition LoadEmbedded(string resourceName)
    {
        Assembly assembly = typeof(SyntaxHighlightingCatalog).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Syntax-highlighting resource '{resourceName}' was not found.");

        using XmlReader reader = XmlReader.Create(stream);
        try
        {
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Syntax-highlighting resource '{resourceName}' could not be loaded: {ex.Message}",
                ex);
        }
    }
}
