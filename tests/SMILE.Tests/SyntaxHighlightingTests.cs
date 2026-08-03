using System.Diagnostics;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using SMILE.Desktop.Highlighting;
using SMILE.Engine;

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
            "cobol",
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
            SyntaxHighlightingCatalog.GetDefinition("cobol"),
            SyntaxHighlightingCatalog.GetDefinition("COBOL"));
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

    [TestMethod]
    public void Objective_c_uses_safe_c_family_highlighting()
    {
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("c"),
            SyntaxHighlightingCatalog.GetDefinition("objective-c"));
    }

    [TestMethod]
    public void Objective_c_highlighting_tokenizes_generated_source_quickly()
    {
        const string source = """
LET Name = "Sin"
PRINT "Hello " + Name + "!"
""";
        TranspileResult result = new SmileTranspiler().Transpile(source, TargetLanguage.ObjectiveC);
        Assert.IsTrue(result.Success);

        var document = new TextDocument(result.GeneratedProgram!.PrimaryFile.Content);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("objective-c")!;
        var highlighter = new DocumentHighlighter(document, definition);

        var stopwatch = Stopwatch.StartNew();
        for (int line = 1; line <= document.LineCount; line++)
        {
            // This is the same synchronous tokenizer AvalonEdit uses when the
            // Objective-C pane repaints. Keeping it fast protects the ComboBox
            // selection path from turning into a frozen UI.
            highlighter.HighlightLine(line);
        }

        Assert.IsLessThan(
            500L,
            stopwatch.ElapsedMilliseconds,
            $"Objective-C highlighting took {stopwatch.ElapsedMilliseconds} ms.");
    }
}
