using System.IO;
using System.Reflection;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace SMILE.Desktop.Highlighting;

public static class SyntaxHighlightingCatalog
{
    private const string ResourcePrefix = "SMILE.Desktop.Highlighting.";

    private static readonly IReadOnlyDictionary<string, Lazy<IHighlightingDefinition>> Definitions =
        new Dictionary<string, Lazy<IHighlightingDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            ["smile"] = Embedded("SMILE.xshd"),
            ["csharp"] = BuiltIn("C#", "csharp"),
            ["c"] = BuiltIn("C++", "c"),
            ["masm-x64"] = Embedded("MasmX64.xshd"),
            ["javascript"] = BuiltIn("JavaScript", "javascript"),
            ["java"] = BuiltIn("Java", "java"),
            ["cobol"] = Embedded("Cobol.xshd"),
            ["objective-c"] = Embedded("ObjectiveC.xshd"),
            ["swift"] = Embedded("Swift.xshd")
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
        new(() =>
            HighlightingManager.Instance.GetDefinition(builtInName) ??
            throw new InvalidOperationException(
                $"Built-in AvalonEdit highlighting '{builtInName}' for '{languageId}' was not found."));

    private static Lazy<IHighlightingDefinition> Embedded(string fileName) =>
        new(() => LoadEmbedded(ResourcePrefix + fileName));

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
