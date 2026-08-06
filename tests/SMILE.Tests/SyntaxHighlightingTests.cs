using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using SMILE.Desktop.Controls;
using SMILE.Desktop.Highlighting;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class SyntaxHighlightingTests
{
    private static readonly PaletteContract[] PaletteContracts =
    [
        new(
            "smile",
            Comments: ["Comment"],
            Keywords: ["Keyword"],
            Identifiers: [],
            Strings: ["String"],
            Numbers: ["Number"],
            Operators: ["Operator"]),
        new(
            "csharp",
            Comments: ["Comment"],
            Keywords:
            [
                "Preprocessor",
                "ValueTypeKeywords",
                "ReferenceTypeKeywords",
                "ThisOrBaseReference",
                "NullOrValueKeywords",
                "Keywords",
                "GotoKeywords",
                "ContextKeywords",
                "ExceptionKeywords",
                "CheckedKeyword",
                "UnsafeKeywords",
                "OperatorKeywords",
                "ParameterModifiers",
                "Modifiers",
                "Visibility",
                "NamespaceKeywords",
                "GetSetAddRemove",
                "TrueFalse",
                "TypeKeywords",
                "SemanticKeywords"
            ],
            Identifiers: ["StringInterpolation", "MethodCall"],
            Strings: ["String", "Char"],
            Numbers: ["NumberLiteral"],
            Operators: ["Punctuation"]),
        CreateCFamilyContract("c"),
        new(
            "masm-x64",
            Comments: ["Comment"],
            Keywords: ["Instruction", "Directive"],
            Identifiers: ["Register"],
            Strings: ["String"],
            Numbers: ["Number"],
            Operators: ["Operator"]),
        new(
            "javascript",
            Comments: ["Comment"],
            Keywords:
            [
                "JavaScriptKeyWords",
                "JavaScriptIntrinsics",
                "JavaScriptLiterals",
                "JavaScriptGlobalFunctions"
            ],
            Identifiers: [],
            Strings: ["String", "Character", "Regex"],
            Numbers: ["Digits"],
            Operators: []),
        new(
            "java",
            Comments: ["Comment", "CommentTags", "JavaDocTags"],
            Keywords:
            [
                "AccessKeywords",
                "OperatorKeywords",
                "SelectionStatements",
                "IterationStatements",
                "ExceptionHandlingStatements",
                "ValueTypes",
                "ReferenceTypes",
                "Void",
                "JumpStatements",
                "Modifiers",
                "AccessModifiers",
                "Package",
                "Literals"
            ],
            Identifiers: ["MethodName"],
            Strings: ["String", "Character"],
            Numbers: ["Digits"],
            Operators: ["Punctuation"]),
        new(
            "cobol",
            Comments: ["Comment"],
            Keywords: ["Keyword"],
            Identifiers: [],
            Strings: ["String"],
            Numbers: ["Number"],
            Operators: ["Operator"]),
        CreateCFamilyContract("objective-c"),
        new(
            "swift",
            Comments: ["Comment"],
            Keywords: ["Keyword"],
            Identifiers: [],
            Strings: ["String"],
            Numbers: ["Number"],
            Operators: ["Operator"]),
        new(
            "python",
            Comments: ["Comment"],
            Keywords: ["Keywords"],
            Identifiers: ["MethodCall"],
            Strings: ["String"],
            Numbers: ["NumberLiteral"],
            Operators: []),
        CreateCFamilyContract("cpp")
    ];

    private static readonly HighlightingSample[] PaletteSamples =
    [
        new("smile", "LET LearnerName = 1\nREM palette comment", "LET", "LearnerName"),
        new("csharp", "LearnerName();\nint Value = 1;\n// palette comment", "int", "LearnerName"),
        new("c", "LearnerName();\nint Value = 1;\n// palette comment", "int", "LearnerName"),
        new(
            "masm-x64",
            "LearnerName PROC\n    mov rax, 1\n    ; palette comment\nLearnerName ENDP",
            "PROC",
            "LearnerName"),
        new(
            "javascript",
            "LearnerName();\nfunction Value() {}\n// palette comment",
            "function",
            "LearnerName"),
        new("java", "LearnerName();\nint Value = 1;\n// palette comment", "int", "LearnerName"),
        new(
            "cobol",
            "PROGRAM-ID. LearnerName.\n*> palette comment",
            "PROGRAM-ID",
            "LearnerName"),
        new(
            "objective-c",
            "LearnerName();\nint Value = 1;\n// palette comment",
            "int",
            "LearnerName"),
        new("swift", "LearnerName()\nlet Value = 1\n// palette comment", "let", "LearnerName"),
        new("python", "LearnerName()\nif True:\n    pass\n# palette comment", "if", "LearnerName"),
        new("cpp", "LearnerName();\nint Value = 1;\n// palette comment", "int", "LearnerName")
    ];

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
        Assert.AreSame(
            SyntaxHighlightingCatalog.GetDefinition("c"),
            SyntaxHighlightingCatalog.GetDefinition("cpp"));
    }

    [TestMethod]
    public void Every_named_color_obeys_the_shared_palette_for_all_languages()
    {
        foreach (PaletteContract contract in PaletteContracts)
        {
            IHighlightingDefinition definition =
                SyntaxHighlightingCatalog.GetDefinition(contract.LanguageId)!;
            string[] actualNames = definition.NamedHighlightingColors
                .Select(color => color.Name)
                .ToArray();

            CollectionAssert.AreEquivalent(
                contract.AllColorNames,
                actualNames,
                $"{contract.LanguageId} color categories changed without a palette classification.");
            AssertColorGroup(definition, contract.Comments, HighlightingPalette.CommentGreen);
            AssertColorGroup(definition, contract.Keywords, HighlightingPalette.KeywordBlue);
            AssertColorGroup(definition, contract.Identifiers, HighlightingPalette.IdentifierBlack);
            AssertColorGroup(definition, contract.Strings, HighlightingPalette.StringRed);
            AssertColorGroup(definition, contract.Numbers, HighlightingPalette.NumberDarkBlue);
            AssertColorGroup(definition, contract.Operators, HighlightingPalette.OperatorBlack);
        }
    }

    [TestMethod]
    public void Every_language_renders_comments_keywords_and_user_names_with_the_required_colors()
    {
        foreach (HighlightingSample sample in PaletteSamples)
        {
            IHighlightingDefinition definition =
                SyntaxHighlightingCatalog.GetDefinition(sample.LanguageId)!;
            var document = new TextDocument(sample.Source);
            var highlighter = new DocumentHighlighter(document, definition);

            AssertTokenForeground(
                document,
                highlighter,
                sample.Keyword,
                HighlightingPalette.KeywordBlue,
                $"{sample.LanguageId} keyword");
            AssertTokenForeground(
                document,
                highlighter,
                sample.Identifier,
                HighlightingPalette.IdentifierBlack,
                $"{sample.LanguageId} user name");
            AssertTokenForeground(
                document,
                highlighter,
                "palette comment",
                HighlightingPalette.CommentGreen,
                $"{sample.LanguageId} comment");
        }
    }

    [TestMethod]
    [DataRow("csharp", "/// <summary name=\"LearnerName\">palette comment</summary>")]
    [DataRow("csharp", "// TODO FIXME HACK UNDONE palette comment")]
    [DataRow("python", "# TODO FIXME HACK UNDONE palette comment")]
    [DataRow("java", "/** @param LearnerName palette comment */")]
    [DataRow("java", "// TODO 49 @param palette comment")]
    public void Nested_documentation_comment_sections_remain_exact_green(
        string languageId,
        string source)
    {
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition(languageId)!;
        var document = new TextDocument(source);
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightedLine line = highlighter.HighlightLine(1);

        for (int offset = 0; offset < source.Length; offset++)
        {
            Assert.AreEqual(
                HighlightingPalette.CommentGreen,
                EffectiveForeground(line, offset),
                $"{languageId} documentation comment offset {offset}");
        }
    }

    [TestMethod]
    [DataRow("c", "#define Value 49 // palette comment")]
    [DataRow("c", "#define Value 49 /* palette comment */")]
    [DataRow("objective-c", "#define Value 49 // palette comment")]
    [DataRow("objective-c", "#define Value 49 /* palette comment */")]
    [DataRow("cpp", "#define Value 49 // palette comment")]
    [DataRow("cpp", "#define Value 49 /* palette comment */")]
    public void C_family_comments_override_the_blue_preprocessor_span(
        string languageId,
        string source)
    {
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition(languageId)!;
        var document = new TextDocument(source);
        var highlighter = new DocumentHighlighter(document, definition);

        AssertTokenForeground(
            document,
            highlighter,
            "define",
            HighlightingPalette.KeywordBlue,
            $"{languageId} preprocessor directive");
        AssertTokenForeground(
            document,
            highlighter,
            "palette comment",
            HighlightingPalette.CommentGreen,
            $"{languageId} preprocessor comment");
    }

    [TestMethod]
    public void C_family_preprocessor_strings_keep_comment_markers_as_String_data()
    {
        const string source = "#define Text \"// not a comment\" // palette comment";
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("cpp")!;
        var document = new TextDocument(source);
        var highlighter = new DocumentHighlighter(document, definition);

        AssertTokenForeground(
            document,
            highlighter,
            "// not a comment",
            HighlightingPalette.StringRed,
            "C-family preprocessor String");
        AssertTokenForeground(
            document,
            highlighter,
            "palette comment",
            HighlightingPalette.CommentGreen,
            "C-family trailing preprocessor comment");
    }

    [TestMethod]
    public void Embedded_target_keyword_and_instruction_inventories_render_blue()
    {
        var inventories = new[]
        {
            new KeywordInventory(
                "swift",
                "actor as associatedtype async await break case catch class continue default defer deinit do else enum extension false fileprivate for func guard if import in init inline inout internal is let nil open operator precedencegroup print private protocol public repeat return rethrows self static struct subscript super switch throw throws true try typealias var where while"),
            new KeywordInventory(
                "cobol",
                "ACCEPT ADD ADVANCING AND BY CALL CLOSE COMP-5 COMPUTE CONTINUE COPY DATA DELETE DELIMITED IDENTIFICATION DIVISION ELSE END-EVALUATE END-IF END-PERFORM END-READ END-STRING END-WRITE ENVIRONMENT EVALUATE EXIT FD FILE-CONTROL FORMAT FREE FUNCTION GIVING GO GOBACK IF INPUT-OUTPUT INSPECT INTEGER-PART INTO IS LENGTH MOVE MULTIPLY NO NOT NUMVAL OCCURS OPEN OR OTHER PERFORM DISPLAY PIC PICTURE POINTER PROCEDURE PROGRAM-ID READ REDEFINES REFERENCE REPLACING RETURN-CODE RETURNING RUN SEARCH SECTION SELECT SIZE SOURCE SPACE SPACES STDERR STOP STRING SUBTRACT THEN THRU TIMES TO TRIM UNTIL UPON USING VALUE VARYING WHEN WITH WORKING-STORAGE WRITE ZERO"),
            new KeywordInventory(
                "masm-x64",
                "adc add and call cld cmp cqo dec div idiv imul inc int ja jae jb jbe je jge jmp jne jnz jo jz lea loop mov movsb movzx neg nop not or pop push rep ret rol ror sal sar sbb sete setg setge setl setle setne shl shr sub test xchg xor ALIGN BYTE casemap DWORD DUP END ENDP EQU EXTERN INCLUDE INVOKE option PROC PROTO PTR PUBLIC QWORD STRUCT")
        };

        foreach (KeywordInventory inventory in inventories)
        {
            foreach (string token in inventory.Source.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var document = new TextDocument(token);
                var highlighter = new DocumentHighlighter(
                    document,
                    SyntaxHighlightingCatalog.GetDefinition(inventory.LanguageId)!);
                AssertTokenForeground(
                    document,
                    highlighter,
                    token,
                    HighlightingPalette.KeywordBlue,
                    $"{inventory.LanguageId} keyword/instruction {token}");
            }
        }
    }

    [TestMethod]
    public void Every_MASM_register_emitted_by_the_generator_renders_black()
    {
        const string source = "al dl eax ecx edx rax rcx rdx r8 r8b r8d r9 r9d r10 r10d r11 r11d rdi rsi rsp";
        foreach (string register in source.Split(' '))
        {
            var document = new TextDocument(register);
            var highlighter = new DocumentHighlighter(
                document,
                SyntaxHighlightingCatalog.GetDefinition("masm-x64")!);
            AssertTokenForeground(
                document,
                highlighter,
                register,
                HighlightingPalette.IdentifierBlack,
                $"MASM register {register}");
        }
    }

    [TestMethod]
    public void CSharp_XML_documentation_palette_contains_only_exact_comment_green()
    {
        _ = SyntaxHighlightingCatalog.GetDefinition("csharp");
        IHighlightingDefinition xmlDocumentation =
            HighlightingManager.Instance.GetDefinition("XmlDoc")!;
        string[] expectedNames = ["XmlString", "DocComment", "XmlPunctuation", "KnownDocTags"];

        CollectionAssert.AreEquivalent(
            expectedNames,
            xmlDocumentation.NamedHighlightingColors.Select(color => color.Name).ToArray());
        AssertColorGroup(
            xmlDocumentation,
            expectedNames,
            HighlightingPalette.CommentGreen);
    }

    [STATestMethod]
    public void Smile_code_editor_makes_the_unhighlighted_identifier_foreground_explicitly_black()
    {
        var editor = new SmileCodeEditor();

        Assert.AreEqual(Brushes.Black, editor.Foreground);
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
    public void Smile_highlighting_colors_INPUT_case_insensitively_without_taking_String_or_comment_text()
    {
        const string source = """
INPUT Name
input Age
Input Ready
INPUTAge
REM INPUT remains comment text
// INPUT remains comment text
LET Text = "INPUT"
LET Message = $"INPUT {Text}"
SET Text ="
INPUT remains Block String data
"
INPUT Text
""";
        var document = new TextDocument(source);
        IHighlightingDefinition definition = SyntaxHighlightingCatalog.GetDefinition("smile")!;
        var highlighter = new DocumentHighlighter(document, definition);
        HighlightingColor keyword = definition.GetNamedColor("Keyword")!;
        HighlightingColor stringColor = definition.GetNamedColor("String")!;
        HighlightingColor comment = definition.GetNamedColor("Comment")!;

        HighlightedLine[] lines = Enumerable.Range(1, document.LineCount)
            .Select(highlighter.HighlightLine)
            .ToArray();

        AssertKeyword(document, lines[0], document.GetLineByNumber(1), "INPUT", keyword);
        AssertKeyword(document, lines[1], document.GetLineByNumber(2), "input", keyword);
        AssertKeyword(document, lines[2], document.GetLineByNumber(3), "Input", keyword);

        DocumentLine nearMiss = document.GetLineByNumber(4);
        AssertRangeDoesNotHaveColor(
            lines[3],
            nearMiss.Offset,
            5,
            keyword,
            "INPUTAge must remain one ordinary identifier");

        foreach (int lineNumber in new[] { 5, 6 })
        {
            DocumentLine line = document.GetLineByNumber(lineNumber);
            string lineText = document.GetText(line);
            int inputOffset = lineText.IndexOf("INPUT", StringComparison.Ordinal);
            AssertRangeHasColor(
                lines[lineNumber - 1],
                line.Offset,
                line.Length,
                comment,
                $"comment line {lineNumber}");
            AssertRangeDoesNotHaveColor(
                lines[lineNumber - 1],
                line.Offset + inputOffset,
                5,
                keyword,
                $"INPUT inside comment line {lineNumber}");
        }

        foreach (int lineNumber in new[] { 7, 8 })
        {
            DocumentLine line = document.GetLineByNumber(lineNumber);
            string lineText = document.GetText(line);
            int inputOffset = lineText.IndexOf("INPUT", StringComparison.Ordinal);
            AssertRangeHasColor(
                lines[lineNumber - 1],
                line.Offset + inputOffset,
                5,
                stringColor,
                $"INPUT inside String line {lineNumber}");
            AssertRangeDoesNotHaveColor(
                lines[lineNumber - 1],
                line.Offset + inputOffset,
                5,
                keyword,
                $"INPUT inside String line {lineNumber}");
        }

        DocumentLine blockContent = document.GetLineByNumber(10);
        AssertRangeHasColor(
            lines[9],
            blockContent.Offset,
            blockContent.Length,
            stringColor,
            "INPUT inside Block String content");
        AssertRangeDoesNotHaveColor(
            lines[9],
            blockContent.Offset,
            5,
            keyword,
            "INPUT inside Block String content");

        AssertKeyword(document, lines[11], document.GetLineByNumber(12), "INPUT", keyword);
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

    private static PaletteContract CreateCFamilyContract(string languageId) =>
        new(
            languageId,
            Comments: ["Comment"],
            Keywords:
            [
                "Preprocessor",
                "CompoundKeywords",
                "This",
                "Namespace",
                "Friend",
                "Modifiers",
                "TypeKeywords",
                "BooleanConstants",
                "Keywords",
                "LoopKeywords",
                "JumpKeywords",
                "ExceptionHandling",
                "ControlFlow"
            ],
            Identifiers: ["MethodName"],
            Strings: ["Character", "String"],
            Numbers: ["Digits"],
            Operators: ["Punctuation", "Operators"]);

    private static void AssertColorGroup(
        IHighlightingDefinition definition,
        IEnumerable<string> colorNames,
        Color expected)
    {
        foreach (string colorName in colorNames)
        {
            HighlightingColor color = definition.GetNamedColor(colorName)!;
            Assert.IsNotNull(color, $"{definition.Name}/{colorName}");
            Color? actual = color.Foreground?.GetColor(null!);
            Assert.IsTrue(actual.HasValue, $"{definition.Name}/{colorName} has no foreground.");
            Assert.AreEqual(expected, actual.Value, $"{definition.Name}/{colorName}");
        }
    }

    private static void AssertTokenForeground(
        TextDocument document,
        DocumentHighlighter highlighter,
        string token,
        Color expected,
        string description)
    {
        int offset = document.Text.IndexOf(token, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, offset, description);
        DocumentLine documentLine = document.GetLineByOffset(offset);
        HighlightedLine? highlightedLine = null;
        for (int lineNumber = 1; lineNumber <= documentLine.LineNumber; lineNumber++)
        {
            highlightedLine = highlighter.HighlightLine(lineNumber);
        }

        Assert.IsNotNull(highlightedLine, description);
        for (int index = 0; index < token.Length; index++)
        {
            Assert.AreEqual(
                expected,
                EffectiveForeground(highlightedLine, offset + index),
                $"{description} offset {index}");
        }
    }

    private static Color EffectiveForeground(HighlightedLine line, int offset)
    {
        Color effective = HighlightingPalette.IdentifierBlack;
        foreach (HighlightedSection section in line.Sections.Where(section =>
                     section.Offset <= offset &&
                     section.Offset + section.Length > offset))
        {
            Color? foreground = section.Color.Foreground?.GetColor(null!);
            if (foreground.HasValue)
            {
                effective = foreground.Value;
            }
        }

        return effective;
    }

    private sealed record PaletteContract(
        string LanguageId,
        string[] Comments,
        string[] Keywords,
        string[] Identifiers,
        string[] Strings,
        string[] Numbers,
        string[] Operators)
    {
        public string[] AllColorNames =>
            Comments
                .Concat(Keywords)
                .Concat(Identifiers)
                .Concat(Strings)
                .Concat(Numbers)
                .Concat(Operators)
                .ToArray();
    }

    private sealed record HighlightingSample(
        string LanguageId,
        string Source,
        string Keyword,
        string Identifier);

    private sealed record KeywordInventory(string LanguageId, string Source);

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
