using System.Diagnostics;
using System.Text;
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
            "swift",
            "python",
            "cpp"
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
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("python"),
            SyntaxHighlightingCatalog.GetDefinition("Python"));
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("cpp"),
            SyntaxHighlightingCatalog.GetDefinition("CPP"));
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

    [TestMethod]
    public void Python_highlighting_tokenizes_generated_source_quickly()
    {
        const string source = """
LET Name = "Sin"
LET Age = 49
PRINT $"Hello {Name}; age={Age}"
""";
        TranspileResult result = new SmileTranspiler().Transpile(source, TargetLanguage.Python);
        Assert.IsTrue(result.Success);

        var document = new TextDocument(result.GeneratedProgram!.PrimaryFile.Content);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("python")!;
        var highlighter = new DocumentHighlighter(document, definition);

        var stopwatch = Stopwatch.StartNew();
        for (int line = 1; line <= document.LineCount; line++)
        {
            highlighter.HighlightLine(line);
        }

        Assert.IsLessThan(
            500L,
            stopwatch.ElapsedMilliseconds,
            $"Python highlighting took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [TestMethod]
    public void Cpp_highlighting_tokenizes_generated_source_quickly()
    {
        const string source = """
LET Name = "Sin"
LET Age = 49
PRINT $"Hello {Name}; age={Age}"
""";
        TranspileResult result = new SmileTranspiler().Transpile(source, TargetLanguage.Cpp);
        Assert.IsTrue(result.Success);

        var document = new TextDocument(result.GeneratedProgram!.PrimaryFile.Content);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("cpp")!;
        var highlighter = new DocumentHighlighter(document, definition);

        var stopwatch = Stopwatch.StartNew();
        for (int line = 1; line <= document.LineCount; line++)
        {
            highlighter.HighlightLine(line);
        }

        Assert.IsLessThan(
            500L,
            stopwatch.ElapsedMilliseconds,
            $"C++ highlighting took {stopwatch.ElapsedMilliseconds} ms.");
    }

    [TestMethod]
    public void Smile_highlighting_colors_SET_and_a_complete_multiline_block_then_resumes()
    {
        const string source = """
LET Name = ""
SET Name ="
He said "Hello".
"
PRINT {Name}
""";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightingColor keyword = definition.GetNamedColor("Keyword")!;
        HighlightingColor stringColor = definition.GetNamedColor("String")!;

        HighlightedLine[] lines = Enumerable.Range(1, document.LineCount)
            .Select(highlighter.HighlightLine)
            .ToArray();

        DocumentLine setLine = document.GetLineByNumber(2);
        AssertRangeHasColor(lines[1], setLine.Offset, 3, keyword, "SET keyword");

        DocumentLine contentLine = document.GetLineByNumber(3);
        AssertRangeHasColor(
            lines[2],
            contentLine.Offset,
            contentLine.Length,
            stringColor,
            "block content containing ordinary quotes");

        DocumentLine closingLine = document.GetLineByNumber(4);
        AssertRangeHasColor(
            lines[3],
            closingLine.Offset,
            closingLine.Length,
            stringColor,
            "closing delimiter");

        DocumentLine printLine = document.GetLineByNumber(5);
        AssertRangeHasColor(lines[4], printLine.Offset, 5, keyword, "PRINT after block");
        AssertRangeDoesNotHaveColor(
            lines[4],
            printLine.Offset,
            5,
            stringColor,
            "highlighting must leave block state after its closing delimiter");
    }

    [TestMethod]
    public void Smile_highlighting_keeps_an_unterminated_block_safe_and_multiline()
    {
        const string source = """
LET Name = ""
SET Name ="
He said "Hello".
Still inside the block
""";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightingColor stringColor = definition.GetNamedColor("String")!;

        HighlightedLine[] lines = Enumerable.Range(1, document.LineCount)
            .Select(highlighter.HighlightLine)
            .ToArray();

        DocumentLine finalLine = document.GetLineByNumber(document.LineCount);
        AssertRangeHasColor(
            lines[^1],
            finalLine.Offset,
            finalLine.Length,
            stringColor,
            "unterminated block content");
    }

    [TestMethod]
    public void Smile_multiline_block_highlighting_remains_fast_for_a_large_edit_buffer()
    {
        var source = new StringBuilder();
        for (int index = 0; index < 250; index++)
        {
            source.AppendLine($"LET Value{index} = \"\"");
            source.AppendLine($"SET Value{index} =\"");
            source.AppendLine("He said \"Hello\".");
            source.AppendLine("  Indented content");
            source.AppendLine("\"");
        }

        var document = new TextDocument(source.ToString());
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);

        var stopwatch = Stopwatch.StartNew();
        for (int line = 1; line <= document.LineCount; line++)
        {
            highlighter.HighlightLine(line);
        }

        stopwatch.Stop();
        Assert.IsLessThan(
            1_000L,
            stopwatch.ElapsedMilliseconds,
            $"SMILE block highlighting took {stopwatch.ElapsedMilliseconds} ms for {document.LineCount} lines.");
    }

    private static void AssertRangeHasColor(
        HighlightedLine line,
        int offset,
        int length,
        HighlightingColor color,
        string description)
    {
        HighlightedSection? section = line.Sections.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Color, color) &&
            candidate.Offset <= offset &&
            candidate.Offset + candidate.Length >= offset + length);

        Assert.IsNotNull(section, $"Expected {description} to use the '{color.Name}' color.");
    }

    private static void AssertRangeDoesNotHaveColor(
        HighlightedLine line,
        int offset,
        int length,
        HighlightingColor color,
        string description)
    {
        HighlightedSection? section = line.Sections.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Color, color) &&
            candidate.Offset < offset + length &&
            candidate.Offset + candidate.Length > offset);

        Assert.IsNull(section, description);
    }
}
