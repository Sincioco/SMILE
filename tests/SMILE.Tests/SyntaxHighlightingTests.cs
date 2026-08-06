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
    public void Smile_highlighting_colors_every_full_line_comment_form_with_contextual_REM_rules()
    {
        string source = string.Join(
            '\n',
            "REM",
            "rem lowercase comment",
            "    Rem comment after spaces",
            "\trEm\tcomment after a tab",
            "//comment",
            "    #comment",
            "\t--comment",
            "# final comment without a trailing newline");
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightingColor comment = definition.GetNamedColor("Comment")!;

        HighlightedLine[] lines = Enumerable.Range(1, document.LineCount)
            .Select(highlighter.HighlightLine)
            .ToArray();

        for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
        {
            DocumentLine documentLine = document.GetLineByNumber(lineNumber);
            AssertRangeHasColor(
                lines[lineNumber - 1],
                documentLine.Offset,
                documentLine.Length,
                comment,
                $"full-line comment {lineNumber}");
        }
    }

    [TestMethod]
    public void Smile_highlighting_keeps_near_misses_strings_PRINT_text_and_block_content_out_of_comments()
    {
        const string source = """
REMEMBER
REMARK
REMOTE
REM:
REM#
LET REM = "// String data"
PRINT // raw template data
PRINT # raw template data
PRINT -- raw template data
PRINT REM raw template data
LET Inline = "# String data" // not an inline comment
LET Interpolated = $"-- {REM}" # not an inline comment
SET REM ="
REM Block String data
// Block String data
# Block String data
-- Block String data

"
-- highlighting resumes after the block
""";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightingColor comment = definition.GetNamedColor("Comment")!;
        HighlightingColor stringColor = definition.GetNamedColor("String")!;

        HighlightedLine[] lines = Enumerable.Range(1, document.LineCount)
            .Select(highlighter.HighlightLine)
            .ToArray();

        for (int lineNumber = 1; lineNumber <= 12; lineNumber++)
        {
            DocumentLine line = document.GetLineByNumber(lineNumber);
            AssertRangeDoesNotHaveColor(
                lines[lineNumber - 1],
                line.Offset,
                line.Length,
                comment,
                $"line {lineNumber} is not a full-line comment");
        }

        for (int lineNumber = 14; lineNumber <= 17; lineNumber++)
        {
            DocumentLine line = document.GetLineByNumber(lineNumber);
            AssertRangeHasColor(
                lines[lineNumber - 1],
                line.Offset,
                line.Length,
                stringColor,
                $"Block String content line {lineNumber}");
            AssertRangeDoesNotHaveColor(
                lines[lineNumber - 1],
                line.Offset,
                line.Length,
                comment,
                $"Block String content line {lineNumber} must remain String-owned");
        }

        DocumentLine closingDelimiter = document.GetLineByNumber(19);
        AssertRangeHasColor(
            lines[18],
            closingDelimiter.Offset,
            closingDelimiter.Length,
            stringColor,
            "Block String closing delimiter");

        DocumentLine finalComment = document.GetLineByNumber(20);
        AssertRangeHasColor(
            lines[19],
            finalComment.Offset,
            finalComment.Length,
            comment,
            "comment highlighting after a Block String");
    }

    [TestMethod]
    public void Smile_highlighting_colors_IF_clause_and_terminator_keywords_individually()
    {
        const string source = """
LET Score = 85
LET Message = ""
IF Score >= 90 THEN
    SET Message ="
    Grade A
    "
ELSE IF Score >= 80 THEN
    SET Message = "Grade B"
ELSE
    IF Score >= 70 THEN
        SET Message = "Grade C"
    END IF
END IF
PRINT {Message}
""";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightingColor keyword = definition.GetNamedColor("Keyword")!;
        HighlightingColor stringColor = definition.GetNamedColor("String")!;

        HighlightedLine[] lines = Enumerable.Range(1, document.LineCount)
            .Select(highlighter.HighlightLine)
            .ToArray();

        AssertKeyword(document, lines[2], document.GetLineByNumber(3), "IF", keyword);
        AssertKeyword(document, lines[2], document.GetLineByNumber(3), "THEN", keyword);
        AssertKeyword(document, lines[6], document.GetLineByNumber(7), "ELSE", keyword);
        AssertKeyword(document, lines[6], document.GetLineByNumber(7), "IF", keyword);
        AssertKeyword(document, lines[6], document.GetLineByNumber(7), "THEN", keyword);
        AssertKeyword(document, lines[8], document.GetLineByNumber(9), "ELSE", keyword);
        AssertKeyword(document, lines[9], document.GetLineByNumber(10), "IF", keyword);
        AssertKeyword(document, lines[9], document.GetLineByNumber(10), "THEN", keyword);
        AssertKeyword(document, lines[11], document.GetLineByNumber(12), "END", keyword);
        AssertKeyword(document, lines[11], document.GetLineByNumber(12), "IF", keyword);
        AssertKeyword(document, lines[12], document.GetLineByNumber(13), "END", keyword);
        AssertKeyword(document, lines[12], document.GetLineByNumber(13), "IF", keyword);

        DocumentLine blockContent = document.GetLineByNumber(5);
        AssertRangeHasColor(
            lines[4],
            blockContent.Offset,
            blockContent.Length,
            stringColor,
            "Block String content inside IF");

        DocumentLine printLine = document.GetLineByNumber(14);
        AssertKeyword(document, lines[13], printLine, "PRINT", keyword);
        AssertRangeDoesNotHaveColor(
            lines[13],
            printLine.Offset,
            5,
            stringColor,
            "highlighting must leave nested IF and Block String state");
    }

    [TestMethod]
    public void Smile_highlighting_keeps_malformed_IF_text_safe()
    {
        const string source = """
IF TRUE = TRUE THEN extra
    PRINT Initial branch
ELSE IF TRUE = FALSE
    PRINT Incomplete ELSE IF header
END IF trailing
""";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightingColor keyword = definition.GetNamedColor("Keyword")!;

        HighlightedLine[] lines = Enumerable.Range(1, document.LineCount)
            .Select(highlighter.HighlightLine)
            .ToArray();

        AssertKeyword(document, lines[0], document.GetLineByNumber(1), "IF", keyword);
        AssertKeyword(document, lines[0], document.GetLineByNumber(1), "THEN", keyword);
        AssertKeyword(document, lines[2], document.GetLineByNumber(3), "ELSE", keyword);
        AssertKeyword(document, lines[2], document.GetLineByNumber(3), "IF", keyword);
        AssertKeyword(document, lines[4], document.GetLineByNumber(5), "END", keyword);
        AssertKeyword(document, lines[4], document.GetLineByNumber(5), "IF", keyword);
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

    private static void AssertKeyword(
        TextDocument document,
        HighlightedLine line,
        DocumentLine documentLine,
        string keyword,
        HighlightingColor color)
    {
        string lineText = document.GetText(documentLine);
        int relativeOffset = lineText.IndexOf(keyword, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, relativeOffset, lineText);
        AssertRangeHasColor(
            line,
            documentLine.Offset + relativeOffset,
            keyword.Length,
            color,
            $"{keyword} keyword");
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
