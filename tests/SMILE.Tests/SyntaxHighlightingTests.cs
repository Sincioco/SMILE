using ICSharpCode.AvalonEdit.Highlighting;
using SMILE.Desktop.Highlighting;

namespace SMILE.Tests;

[TestClass]
public sealed class SyntaxHighlightingTests
{
    [TestMethod]
    public void Syntax_highlighting_catalog_resolves_every_supported_language()
    {
        string[] languageIds =
        {
            "smile",
            "csharp",
            "c",
            "masm-x64",
            "javascript",
            "java",
            "objective-c",
            "swift"
        };

        foreach (string languageId in languageIds)
        {
            IHighlightingDefinition? definition = SyntaxHighlightingCatalog.GetDefinition(languageId);
            Assert.IsNotNull(definition, languageId);
        }
    }

    [TestMethod]
    public void Syntax_highlighting_catalog_matches_ids_case_insensitively()
    {
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("smile"),
            SyntaxHighlightingCatalog.GetDefinition("SMILE"));
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("csharp"),
            SyntaxHighlightingCatalog.GetDefinition("CSharp"));
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("masm-x64"),
            SyntaxHighlightingCatalog.GetDefinition("MASM-X64"));
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("objective-c"),
            SyntaxHighlightingCatalog.GetDefinition("Objective-C"));
    }

    [TestMethod]
    public void Syntax_highlighting_catalog_returns_plain_text_for_unknown_ids()
    {
        Assert.IsNull(SyntaxHighlightingCatalog.GetDefinition(null));
        Assert.IsNull(SyntaxHighlightingCatalog.GetDefinition(""));
        Assert.IsNull(SyntaxHighlightingCatalog.GetDefinition("   "));
        Assert.IsNull(SyntaxHighlightingCatalog.GetDefinition("unknown"));
    }
}
