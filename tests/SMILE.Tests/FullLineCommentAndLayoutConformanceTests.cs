using System.Text;
using System.Text.RegularExpressions;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class FullLineCommentAndLayoutConformanceTests
{
    private const string RequireAllTargetsEnvironmentVariable = "SMILE_REQUIRE_ALL_TARGETS";
    private const string RequireJavaEnvironmentVariable = "SMILE_REQUIRE_JAVA";
    private const string RequireZeroTargetWarningsEnvironmentVariable =
        "SMILE_REQUIRE_ZERO_TARGET_WARNINGS";

    private const string NormativeAcceptanceSource = """
REM Traditional BASIC comment
// C-family comment
# Script-language comment
-- SQL-style comment

LET REM = "REM variable"

LET Score = 85
LET Grade = ""
LET Message = ""

IF Score >= 90 THEN
    // This branch is not selected.

    SET Grade = "A"
ELSE IF Score >= 80 THEN
    # This branch is selected.

    SET Grade = "B"
ELSE
    -- Fallback branch.

    SET Grade = "C"
END IF

SET Message ="
REM String data

// String data
# String data
-- String data
"

PRINT {REM}

PRINT Grade={Grade}

PRINT // Printed raw text

PRINT {Message}
""";

    private const string NormativeAcceptanceOutput =
        "REM variable\n" +
        "Grade=B\n" +
        "// Printed raw text\n" +
        "REM String data\n" +
        "\n" +
        "// String data\n" +
        "# String data\n" +
        "-- String data\n";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("REM", FullLineCommentMarker.Rem, "")]
    [DataRow("rem\tTabbed payload", FullLineCommentMarker.Rem, "\tTabbed payload")]
    [DataRow("  ReM Unicode 台灣!", FullLineCommentMarker.Rem, " Unicode 台灣!")]
    [DataRow("//payload", FullLineCommentMarker.SlashSlash, "payload")]
    [DataRow(" \t// punctuation: !?", FullLineCommentMarker.SlashSlash, " punctuation: !?")]
    [DataRow("#", FullLineCommentMarker.Hash, "")]
    [DataRow("\t#payload", FullLineCommentMarker.Hash, "payload")]
    [DataRow("--payload", FullLineCommentMarker.DashDash, "payload")]
    [DataRow("   -- Unicode 🙂", FullLineCommentMarker.DashDash, " Unicode 🙂")]
    public void Parser_recognizes_each_first_non_whitespace_marker_at_EOF(
        string source,
        FullLineCommentMarker expectedMarker,
        string expectedPayload)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        var comment = (FullLineCommentSyntax)result.Program!.SourceItems.Single();
        Assert.AreEqual(expectedMarker, comment.Marker);
        Assert.AreEqual(expectedPayload, comment.Payload);
        Assert.AreEqual(0, comment.Span.Start);
        Assert.AreEqual(source.Length, comment.Span.Length);
        Assert.AreEqual(1, comment.Span.Line);
        Assert.AreEqual(1, comment.Span.Column);
        Assert.HasCount(0, result.Program.Statements);
    }

    [TestMethod]
    public void Parser_never_rewrites_comment_payload_Block_String_data_or_source_spans()
    {
        const string preservedText = "\u00e2\u20ac\u0153 literal payload";
        string commentLine = "// " + preservedText;
        string source =
            commentLine + "\n" +
            "LET Text = \"\"\n" +
            "SET Text =\"\n" +
            preservedText + "\n" +
            "\"\n" +
            "PRINT ok";

        ParseResult parsing = _transpiler.Parse(source);
        BindResult binding = _transpiler.Bind(source);

        Assert.IsTrue(parsing.Success, JoinDiagnostics(parsing.Diagnostics));
        var comment = (FullLineCommentSyntax)parsing.Program!.SourceItems[0];
        Assert.AreEqual(" " + preservedText, comment.Payload);
        Assert.AreEqual(commentLine, source.Substring(comment.Span.Start, comment.Span.Length));

        Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
        var boundComment = (BoundFullLineComment)binding.Program!.SourceItems[0];
        Assert.AreEqual(" " + preservedText, boundComment.Payload);
        var set = (BoundSetStatement)binding.Program.Statements[1];
        Assert.AreEqual(preservedText, ((BoundStringLiteralExpression)set.Value).Value);

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            StringAssert.Contains(generated, preservedText, language.ToString());
        }
    }

    [TestMethod]
    public void Every_marker_accepts_the_complete_whitespace_payload_and_EOF_matrix()
    {
        var markers = new[]
        {
            (Text: "REM", Kind: FullLineCommentMarker.Rem, NeedsBoundary: true),
            (Text: "//", Kind: FullLineCommentMarker.SlashSlash, NeedsBoundary: false),
            (Text: "#", Kind: FullLineCommentMarker.Hash, NeedsBoundary: false),
            (Text: "--", Kind: FullLineCommentMarker.DashDash, NeedsBoundary: false)
        };

        foreach ((string markerText, FullLineCommentMarker markerKind, bool needsBoundary) in markers)
        {
            string separatedPayload = needsBoundary ? " payload" : "payload";
            foreach (string source in new[]
            {
                markerText,
                "  " + markerText,
                "\t" + markerText,
                markerText + separatedPayload,
                markerText + (needsBoundary ? " 台灣🙂" : "台灣🙂"),
                markerText + (needsBoundary ? " !?:;" : "!?:;")
            })
            {
                ParseResult result = _transpiler.Parse(source);
                Assert.IsTrue(result.Success, $"{source}{Environment.NewLine}{JoinDiagnostics(result.Diagnostics)}");
                Assert.AreEqual(
                    markerKind,
                    ((FullLineCommentSyntax)result.Program!.SourceItems.Single()).Marker);
            }
        }
    }

    [TestMethod]
    public void All_eight_ASCII_case_permutations_of_REM_are_comments()
    {
        for (int casing = 0; casing < 8; casing++)
        {
            char[] marker = "REM".ToCharArray();
            for (int character = 0; character < marker.Length; character++)
            {
                if ((casing & (1 << character)) != 0)
                {
                    marker[character] = char.ToLowerInvariant(marker[character]);
                }
            }

            ParseResult result = _transpiler.Parse(new string(marker) + " payload");
            Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
            Assert.IsInstanceOfType<FullLineCommentSyntax>(result.Program!.SourceItems.Single());
        }
    }

    [TestMethod]
    [DataRow("REMEMBER")]
    [DataRow("REMARK")]
    [DataRow("REMOTE")]
    [DataRow("REM:")]
    [DataRow("REM#")]
    [DataRow("REM\u00A0not-ASCII-horizontal-whitespace")]
    public void REM_near_misses_follow_the_ordinary_statement_grammar(string source)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.Program!.SourceItems.OfType<FullLineCommentSyntax>().Any());
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1001"),
            JoinDiagnostics(result.Diagnostics));
    }

    [TestMethod]
    public void REM_remains_a_valid_identifier_and_PRINT_marker_text_remains_data()
    {
        const string source = """
LET REM = "REM variable"
PRINT {REM}
PRINT // text
PRINT # text
PRINT -- text
PRINT REM text
PRINT https://example.com
""";

        BindResult binding = _transpiler.Bind(source);
        EvaluationResult evaluation = _evaluator.Evaluate(source);

        Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
        Assert.AreEqual("REM", binding.Program!.Variables.Single().Name);
        Assert.IsTrue(evaluation.Success, JoinDiagnostics(evaluation.Diagnostics));
        Assert.AreEqual(
            "REM variable\n// text\n# text\n-- text\nREM text\nhttps://example.com\n",
            NormalizeNewlines(evaluation.Output));
    }

    [TestMethod]
    public void Parser_and_binder_retain_leading_consecutive_trailing_and_whitespace_only_lines()
    {
        const string source = "\n \t\nREM one\n\nLET A = 49\n\n\nPRINT {A}\n\t";

        ParseResult parsing = _transpiler.Parse(source);
        BindResult binding = _transpiler.Bind(source);

        Assert.IsTrue(parsing.Success, JoinDiagnostics(parsing.Diagnostics));
        CollectionAssert.AreEqual(
            new[]
            {
                typeof(BlankLineSyntax),
                typeof(BlankLineSyntax),
                typeof(FullLineCommentSyntax),
                typeof(BlankLineSyntax),
                typeof(LetStatementSyntax),
                typeof(BlankLineSyntax),
                typeof(BlankLineSyntax),
                typeof(PrintStatementSyntax),
                typeof(BlankLineSyntax)
            },
            parsing.Program!.SourceItems.Select(item => item.GetType()).ToArray());
        Assert.AreEqual(0, parsing.Program.Span.Start);
        Assert.AreEqual(source.Length, parsing.Program.Span.Length);
        Assert.AreEqual(1, parsing.Program.SourceItems[0].Span.Line);
        Assert.AreEqual(9, parsing.Program.SourceItems[^1].Span.Line);
        Assert.AreEqual(0, parsing.Program.SourceItems[0].Span.Length);
        Assert.AreEqual(1, parsing.Program.SourceItems[^1].Span.Length);

        Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
        CollectionAssert.AreEqual(
            new[]
            {
                typeof(BoundBlankLine),
                typeof(BoundBlankLine),
                typeof(BoundFullLineComment),
                typeof(BoundBlankLine),
                typeof(BoundLetStatement),
                typeof(BoundBlankLine),
                typeof(BoundBlankLine),
                typeof(BoundPrintStatement),
                typeof(BoundBlankLine)
            },
            binding.Program!.SourceItems.Select(item => item.GetType()).ToArray());
        Assert.HasCount(2, binding.Program.Statements);
    }

    [TestMethod]
    public void Layout_items_keep_following_diagnostics_on_their_physical_source_line()
    {
        ParseResult result = _transpiler.Parse("REM first\n\n \t\nBROKEN");

        Diagnostic diagnostic = result.Diagnostics.Single();
        Assert.AreEqual("SMILE1001", diagnostic.Code);
        Assert.AreEqual(4, diagnostic.Span.Line);
        Assert.AreEqual(1, diagnostic.Span.Column);
    }

    [TestMethod]
    public void Public_lexer_returns_one_structured_comment_token_then_the_normal_EOL()
    {
        const string source = " \t// payload\r\nREM\r\n#x\n--\tlast";
        var lexer = new Lexer(source);

        SyntaxToken[] tokens = lexer.Lex().ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                SyntaxKind.FullLineCommentToken,
                SyntaxKind.EndOfLineToken,
                SyntaxKind.FullLineCommentToken,
                SyntaxKind.EndOfLineToken,
                SyntaxKind.FullLineCommentToken,
                SyntaxKind.EndOfLineToken,
                SyntaxKind.FullLineCommentToken,
                SyntaxKind.EndOfFileToken
            },
            tokens.Select(token => token.Kind).ToArray());
        var first = (FullLineCommentTokenValue)tokens[0].Value!;
        Assert.AreEqual(FullLineCommentMarker.SlashSlash, first.Marker);
        Assert.AreEqual(" payload", first.Payload);
        Assert.AreEqual(" \t// payload", tokens[0].Text);
        Assert.AreEqual(1, tokens[0].Span.Column);
        Assert.AreEqual(2, tokens[2].Span.Line);
        Assert.AreEqual("", ((FullLineCommentTokenValue)tokens[2].Value!).Payload);
        Assert.AreEqual("x", ((FullLineCommentTokenValue)tokens[4].Value!).Payload);
        Assert.AreEqual("\tlast", ((FullLineCommentTokenValue)tokens[6].Value!).Payload);
        Assert.HasCount(0, lexer.Diagnostics);
    }

    [TestMethod]
    public void Public_lexer_handles_many_consecutive_comments_iteratively()
    {
        const int count = 10_000;
        string source = string.Join('\n', Enumerable.Repeat("// comment", count));
        var lexer = new Lexer(source);

        SyntaxToken[] tokens = lexer.Lex().ToArray();

        Assert.AreEqual(count, tokens.Count(token => token.Kind is SyntaxKind.FullLineCommentToken));
        Assert.AreEqual(count - 1, tokens.Count(token => token.Kind is SyntaxKind.EndOfLineToken));
        Assert.AreEqual(SyntaxKind.EndOfFileToken, tokens[^1].Kind);
        Assert.HasCount(0, lexer.Diagnostics);
    }

    [TestMethod]
    public void Public_lexer_resumes_comment_recognition_after_an_interpolated_String_line()
    {
        const string source = "PRINT $\"value\"\n// after interpolation";
        var lexer = new Lexer(source);

        SyntaxToken[] tokens = lexer.Lex().ToArray();

        Assert.HasCount(
            1,
            tokens.Where(token => token.Kind is SyntaxKind.FullLineCommentToken));
        Assert.HasCount(
            0,
            tokens.Where(token => token.Kind is SyntaxKind.BlockStringLiteralToken));
        Assert.HasCount(0, lexer.Diagnostics);
    }

    [TestMethod]
    public void Inline_markers_are_not_comments_and_bounded_expression_lexing_stays_ordinary()
    {
        ParseResult statement = _transpiler.Parse("LET Value = 1 // trailing");
        SyntaxToken[] tokens = new Lexer("LET Value = 1 // trailing").Lex().ToArray();

        Assert.IsFalse(statement.Success);
        Assert.IsFalse(statement.Program!.SourceItems.OfType<FullLineCommentSyntax>().Any());
        Assert.IsFalse(tokens.Any(token => token.Kind is SyntaxKind.FullLineCommentToken));
        Assert.IsGreaterThanOrEqualTo(
            2,
            tokens.Count(token => token.Kind is SyntaxKind.SlashToken));
    }

    [TestMethod]
    public void Strings_interpolation_and_Block_String_content_keep_markers_and_blank_lines_as_data()
    {
        string source =
            "LET Slash = \"//\"\n" +
            "LET Hash = \"#\"\n" +
            "LET Dash = \"--\"\n" +
            "LET RemText = \"REM\"\n" +
            "LET Text = \"\"\n" +
            "SET Text =\"\n" +
            "REM data  \t\n" +
            "\n" +
            "// data\n" +
            "# data\n" +
            "-- A\\0B\n" +
            "\"\n" +
            "PRINT $\"{Slash}|{Hash}|{Dash}|{RemText}\"\n" +
            "PRINT {Text}";
        string blockValue = "REM data  \t\n\n// data\n# data\n-- A\0B";

        ParseResult parsing = _transpiler.Parse(source);
        BindResult binding = _transpiler.Bind(source);
        EvaluationResult evaluation = _evaluator.Evaluate(source);

        Assert.IsTrue(parsing.Success, JoinDiagnostics(parsing.Diagnostics));
        Assert.AreEqual(0, parsing.Program!.SourceItems.OfType<FullLineCommentSyntax>().Count());
        Assert.AreEqual(0, parsing.Program.SourceItems.OfType<BlankLineSyntax>().Count());
        const string blockOnlySource = "SET Text =\"\nREM data\n\n// data\n# data\n-- data\n\"";
        SyntaxToken[] blockTokens = new Lexer(blockOnlySource).Lex().ToArray();
        Assert.HasCount(
            1,
            blockTokens.Where(token => token.Kind is SyntaxKind.BlockStringLiteralToken));
        Assert.HasCount(
            0,
            blockTokens.Where(token => token.Kind is SyntaxKind.FullLineCommentToken));
        Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
        var set = (BoundSetStatement)binding.Program!.Statements[5];
        Assert.AreEqual(blockValue, ((BoundStringLiteralExpression)set.Value).Value);
        Assert.IsTrue(evaluation.Success, JoinDiagnostics(evaluation.Diagnostics));
        Assert.AreEqual(
            "//|#|--|REM\n" + blockValue + "\n",
            NormalizeNewlines(evaluation.Output));
    }

    [TestMethod]
    public void Comments_and_blanks_cannot_redirect_IF_clauses_or_depth_recovery()
    {
        const string source = """
LET Grade = ""
IF TRUE = TRUE THEN
    // ELSE
    # END IF
    -- IF FALSE = TRUE THEN
    REM SET Grade ="

    SET Grade = "A"
ELSE
    SET Grade = "B"
END IF
PRINT {Grade}
""";

        BindResult binding = _transpiler.Bind(source);
        EvaluationResult evaluation = _evaluator.Evaluate(source);

        Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
        var conditional = (BoundIfStatement)binding.Program!.Statements[1];
        Assert.HasCount(6, conditional.Clauses[0].SourceItems);
        Assert.HasCount(1, conditional.Clauses[0].Statements);
        Assert.AreEqual("A\n", NormalizeNewlines(evaluation.Output));

        var deep = new StringBuilder();
        for (int depth = 0; depth < 129; depth++)
        {
            deep.AppendLine("IF TRUE = TRUE THEN");
        }

        deep.AppendLine("// END IF")
            .AppendLine("# IF FALSE = TRUE THEN")
            .AppendLine("-- ELSE")
            .AppendLine("REM END IF");
        for (int depth = 0; depth < 129; depth++)
        {
            deep.AppendLine("END IF");
        }

        deep.AppendLine("PRINT Recovered");
        ParseResult recovered = _transpiler.Parse(deep.ToString());

        Assert.HasCount(1, recovered.Diagnostics);
        Assert.AreEqual("SMILE1416", recovered.Diagnostics[0].Code);
        Assert.IsInstanceOfType<PrintStatementSyntax>(recovered.Program!.Statements.Last());
    }

    [TestMethod]
    public void Comment_blank_and_mixed_layout_only_IF_bodies_remain_semantically_empty()
    {
        var bodies = new[]
        {
            "    REM comment only",
            string.Empty,
            "    // comment\n\n    # second comment"
        };

        foreach (string body in bodies)
        {
            string source = "IF TRUE = TRUE THEN\n" + body + "\nELSE\n    -- explicit ELSE comment\nEND IF";
            BindResult binding = _transpiler.Bind(source);
            EvaluationResult evaluation = _evaluator.Evaluate(source);

            Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
            var conditional = (BoundIfStatement)binding.Program!.Statements.Single();
            Assert.HasCount(0, conditional.Clauses[0].Statements);
            Assert.HasCount(0, conditional.ElseStatements);
            Assert.AreEqual(string.Empty, evaluation.Output);

            foreach (TargetLanguage language in TargetLanguageInfo.All)
            {
                _ = Generate(source, language);
            }

            StringAssert.Contains(
                Generate(source, TargetLanguage.Python).PrimaryFile.Content,
                "pass");
            StringAssert.Contains(
                Generate(source, TargetLanguage.Cobol).PrimaryFile.Content,
                "CONTINUE");
        }
    }

    [TestMethod]
    public void Thousand_level_recovery_ignores_all_comments_Block_Strings_and_mixed_terminators()
    {
        var source = new StringBuilder();
        for (int depth = 0; depth < 1_000; depth++)
        {
            source.AppendLine("IF TRUE = TRUE THEN");
        }

        source.AppendLine("REM END IF")
            .AppendLine("// IF FALSE = TRUE THEN")
            .AppendLine("# ELSE")
            .AppendLine("-- END IF extra")
            .AppendLine("SET Message =\"")
            .AppendLine("END IF")
            .AppendLine("IF FALSE = TRUE THEN")
            .AppendLine("ELSE")
            .AppendLine("\"")
            .AppendLine("ELSE IF FALSE = TRUE THEN")
            .AppendLine("ELSE")
            .AppendLine("IF FALSE = TRUE THEN")
            .AppendLine("END IF")
            .AppendLine("END IF extra");

        // The malformed END balances one over-limit IF. The remaining 999
        // canonical terminators close the skipped subtree and the 128 parser
        // frames that were entered before the safety limit.
        for (int depth = 0; depth < 999; depth++)
        {
            source.AppendLine("END IF");
        }

        source.AppendLine("PRINT Recovered");
        ParseResult result = _transpiler.Parse(source.ToString());

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1416", result.Diagnostics[0].Code);
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Comment_only_and_blank_only_programs_are_valid_and_semantically_empty()
    {
        const string source = "\nREM quiet\n\n// still quiet\n\t";

        ParseResult parsing = _transpiler.Parse(source);
        BindResult binding = _transpiler.Bind(source);
        EvaluationResult evaluation = _evaluator.Evaluate(source);

        Assert.IsTrue(parsing.Success, JoinDiagnostics(parsing.Diagnostics));
        Assert.IsTrue(binding.Success, JoinDiagnostics(binding.Diagnostics));
        Assert.HasCount(0, binding.Program!.Statements);
        Assert.IsTrue(evaluation.Success, JoinDiagnostics(evaluation.Diagnostics));
        Assert.AreEqual(string.Empty, evaluation.Output);

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            GeneratedProgram generated = Generate(source, language);
            StringAssert.Contains(generated.PrimaryFile.Content, "quiet");
        }

        StringAssert.Contains(Generate(source, TargetLanguage.Python).PrimaryFile.Content, "pass");

        const string blankOnlySource = "\n \t\n";
        BindResult blankBinding = _transpiler.Bind(blankOnlySource);
        EvaluationResult blankEvaluation = _evaluator.Evaluate(blankOnlySource);
        Assert.IsTrue(blankBinding.Success, JoinDiagnostics(blankBinding.Diagnostics));
        Assert.HasCount(0, blankBinding.Program!.Statements);
        Assert.IsTrue(blankEvaluation.Success, JoinDiagnostics(blankEvaluation.Diagnostics));
        Assert.AreEqual(string.Empty, blankEvaluation.Output);
        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            _ = Generate(blankOnlySource, language);
        }
    }

    [TestMethod]
    public async Task Layout_does_not_change_binding_analysis_or_all_target_execution()
    {
        const string compact = """
LET Value = 1
LET Result = 0
IF Value = 1 THEN
    SET Result = Value + 1
ELSE
    SET Result = 9
END IF
PRINT {Result}
""";
        const string laidOut = """
REM same semantics

LET Value = 1
// between declarations
LET Result = 0

IF Value = 1 THEN
    # selected

    SET Result = Value + 1
ELSE
    -- fallback
    SET Result = 9
END IF

PRINT {Result}
""";

        BindResult compactBinding = _transpiler.Bind(compact);
        BindResult laidOutBinding = _transpiler.Bind(laidOut);
        EvaluationResult compactEvaluation = _evaluator.Evaluate(compact);
        EvaluationResult laidOutEvaluation = _evaluator.Evaluate(laidOut);

        Assert.IsTrue(compactBinding.Success, JoinDiagnostics(compactBinding.Diagnostics));
        Assert.IsTrue(laidOutBinding.Success, JoinDiagnostics(laidOutBinding.Diagnostics));
        CollectionAssert.AreEqual(
            compactBinding.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray(),
            laidOutBinding.Diagnostics.Select(diagnostic => diagnostic.Code).ToArray());
        Assert.AreEqual(compactEvaluation.Output, laidOutEvaluation.Output);

        BoundProgramExecutionTrace compactTrace =
            BoundProgramExecutionTrace.Create(compactBinding.Program!);
        BoundProgramExecutionTrace laidOutTrace =
            BoundProgramExecutionTrace.Create(laidOutBinding.Program!);
        Assert.HasCount(compactTrace.Steps.Count, laidOutTrace.Steps);
        CollectionAssert.AreEqual(
            FinalValuesByName(compactTrace).ToArray(),
            FinalValuesByName(laidOutTrace).ToArray());

        BoundProgramAnalysis compactAnalysis = BoundProgramAnalysis.Create(compactBinding.Program!);
        BoundProgramAnalysis laidOutAnalysis = BoundProgramAnalysis.Create(laidOutBinding.Program!);
        Assert.HasCount(
            compactAnalysis.EnumerateStatements().Count,
            laidOutAnalysis.EnumerateStatements());
        CollectionAssert.AreEqual(
            compactAnalysis.FinalConcreteValues
                .OrderBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key.Name + "=" + pair.Value.ToDisplayText())
                .ToArray(),
            laidOutAnalysis.FinalConcreteValues
                .OrderBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
                .Select(pair => pair.Key.Name + "=" + pair.Value.ToDisplayText())
                .ToArray());

        await AssertAvailableTargetsMatchEvaluator(
            compact,
            "2\n",
            "compact semantic-equivalence program");
        await AssertAvailableTargetsMatchEvaluator(
            laidOut,
            "2\n",
            "commented semantic-equivalence program");
    }

    [TestMethod]
    public void All_ten_targets_map_markers_once_and_preserve_adjacent_blank_counts_and_order()
    {
        const string source = "REM alpha\n\n//beta\n\n\n# gamma\n--delta";

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string targetMarker = TargetMarker(language);
            string generated = NormalizeNewlines(Generate(source, language).PrimaryFile.Content);
            string[] lines = generated.Split('\n');
            int alpha = SingleLineIndex(lines, "alpha");
            int beta = SingleLineIndex(lines, "beta");
            int gamma = SingleLineIndex(lines, "gamma");
            int delta = SingleLineIndex(lines, "delta");

            Assert.AreEqual(targetMarker + " alpha", lines[alpha].TrimStart(), language.ToString());
            Assert.AreEqual(targetMarker + "beta", lines[beta].TrimStart(), language.ToString());
            Assert.AreEqual(targetMarker + " gamma", lines[gamma].TrimStart(), language.ToString());
            Assert.AreEqual(targetMarker + "delta", lines[delta].TrimStart(), language.ToString());
            Assert.AreEqual(2, beta - alpha, $"{language} did not preserve one blank line.");
            Assert.AreEqual(3, gamma - beta, $"{language} did not preserve two blank lines.");
            Assert.AreEqual(1, delta - gamma, $"{language} changed adjacent comment order.");
        }
    }

    [TestMethod]
    public void All_ten_targets_keep_leading_and_trailing_source_blanks_beside_user_layout()
    {
        const string source = "\nREM anchor\n\n";

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string[] lines = NormalizeNewlines(Generate(source, language).PrimaryFile.Content)
                .Split('\n');
            int anchor = SingleLineIndex(lines, "anchor");

            Assert.IsGreaterThan(0, anchor, $"{language} lost the leading source blank.");
            Assert.AreEqual(string.Empty, lines[anchor - 1], $"{language} lost the leading source blank.");
            Assert.IsLessThan(lines.Length - 1, anchor, $"{language} lost the trailing source blank.");
            Assert.AreEqual(string.Empty, lines[anchor + 1], $"{language} lost the trailing source blank.");
        }
    }

    [TestMethod]
    public void User_LET_blank_PRINT_example_preserves_the_boundary_without_changing_PRINT_meaning()
    {
        const string source = "LET a = 49\n\nPRINT a";
        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsTrue(evaluation.Success, JoinDiagnostics(evaluation.Diagnostics));
        Assert.AreEqual("a\n", NormalizeNewlines(evaluation.Output));

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string generated = NormalizeNewlines(Generate(source, language).PrimaryFile.Content);
            string expectedBoundary = language switch
            {
                TargetLanguage.CSharp => "int a = 49;\n\n        Console.WriteLine(\"a\");",
                TargetLanguage.C => "int a = 49;\n\n\n    printf(\"a\\n\");",
                TargetLanguage.MasmX64 =>
                    "Update the runtime signed Integer storage.\n\n\n; PRINT #1",
                TargetLanguage.JavaScript => "let a = 49;\n\nconsole.log(\"a\");",
                TargetLanguage.Java => "int a = 49;\n\n        System.out.println(\"a\");",
                TargetLanguage.Cobol =>
                    "SMILE PRINT reads current storage when it directly names a variable.\n\n    DISPLAY \"a\".",
                TargetLanguage.ObjectiveC => "int a = 49;\n\n\n    printf(\"a\\n\");",
                TargetLanguage.Swift => "let a: Int = 49\n\nprint(\"a\")",
                TargetLanguage.Python => "a = 49\n\n    print(\"a\")",
                TargetLanguage.Cpp => "int a = 49;\n\n\n    std::cout << \"a\" << '\\n';",
                _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
            };

            StringAssert.Contains(generated, expectedBoundary, language.ToString());
        }
    }

    [TestMethod]
    public void Split_targets_place_layout_in_the_one_correct_user_code_region()
    {
        const string source = """
LET a = 49

IF a = 49 THEN
    REM branch comment

ELSE
    # explicit else comment
END IF
PRINT a
""";

        string python = Generate(source, TargetLanguage.Python).PrimaryFile.Content;
        int pythonMain = python.IndexOf("def main", StringComparison.Ordinal);
        int pythonComment = python.IndexOf("branch comment", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, pythonMain, python);
        Assert.IsGreaterThan(pythonMain, pythonComment, python);
        StringAssert.Contains(python, "pass");

        string masm = Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content;
        int masmCode = masm.IndexOf(".code", StringComparison.OrdinalIgnoreCase);
        int masmComment = masm.IndexOf("branch comment", StringComparison.Ordinal);
        Assert.IsGreaterThan(masmCode, masmComment, masm);
        Assert.HasCount(1, Regex.Matches(masm, "branch comment").Cast<Match>(), masm);

        string cobol = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;
        int procedure = cobol.IndexOf("PROCEDURE DIVISION", StringComparison.OrdinalIgnoreCase);
        int cobolComment = cobol.IndexOf("branch comment", StringComparison.Ordinal);
        Assert.IsGreaterThan(procedure, cobolComment, cobol);
        Assert.HasCount(1, Regex.Matches(cobol, "branch comment").Cast<Match>(), cobol);
        StringAssert.Contains(cobol, "CONTINUE");
    }

    [TestMethod]
    public void Target_comment_sanitization_is_deterministic_and_prevents_source_injection()
    {
        string source =
            "REM Ends with backslash \\\n" +
            "REM Backslash before hspace \\   \n" +
            "REM Java-looking \\u000A escape\n" +
            "REM Unsafe " + '\0' + '\u0001' + '\u2028' + '\u2029' + " controls\n" +
            "REM Multilingual 台灣 مرحبا 🙂\n" +
            "PRINT safe";

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string first = Generate(source, language).PrimaryFile.Content;
            string second = Generate(source, language).PrimaryFile.Content;

            Assert.AreEqual(first, second, $"{language} comment generation was not deterministic.");
            Assert.DoesNotContain('\0', first, $"{language} retained a source NUL.");
            Assert.DoesNotContain('\u0001', first, $"{language} retained a C0 control.");
            Assert.DoesNotContain('\u2028', first, $"{language} retained U+2028.");
            Assert.DoesNotContain('\u2029', first, $"{language} retained U+2029.");
            StringAssert.Contains(first, "Multilingual 台灣 مرحبا 🙂");
            StringAssert.Contains(first, "safe");

            if (language is TargetLanguage.Java)
            {
                Assert.IsFalse(first.Contains("\\u000A", StringComparison.Ordinal), first);
                StringAssert.Contains(first, "\\u005Cu{5C}u000A");
                StringAssert.Contains(first, "\\u005Cu{0}");
            }
            else
            {
                StringAssert.Contains(first, "\\u{0}");
                StringAssert.Contains(first, "\\u{2028}");
            }

            if (language is TargetLanguage.C or TargetLanguage.Cpp or TargetLanguage.ObjectiveC)
            {
                string[] generatedLines = NormalizeNewlines(first).Split('\n');
                string backslashLine = generatedLines.Single(line =>
                    line.Contains("Ends with backslash", StringComparison.Ordinal));
                string hspaceLine = generatedLines.Single(line =>
                    line.Contains("Backslash before hspace", StringComparison.Ordinal));
                Assert.IsTrue(backslashLine.EndsWith("\\u{5C}", StringComparison.Ordinal), first);
                Assert.IsTrue(hspaceLine.EndsWith("\\u{5C}   ", StringComparison.Ordinal), first);
            }
        }
    }

    [TestMethod]
    public void COBOL_wraps_long_comments_below_the_conservative_limit_without_losing_payload()
    {
        string payload = string.Concat(Enumerable.Repeat("A🙂B", 150));
        string cobol = NormalizeNewlines(
            Generate("//" + payload, TargetLanguage.Cobol).PrimaryFile.Content);
        string[] commentLines = cobol
            .Split('\n')
            .Where(line =>
                line.TrimStart().StartsWith("*>", StringComparison.Ordinal) &&
                line.Contains("A🙂B", StringComparison.Ordinal))
            .ToArray();

        Assert.IsGreaterThan(1, commentLines.Length, cobol);
        Assert.IsTrue(
            commentLines.All(line => Encoding.UTF8.GetByteCount(line) <= 200),
            cobol);
        string reconstructed = string.Concat(commentLines.Select(line =>
        {
            string trimmed = line.TrimStart();
            return trimmed[2..];
        }));
        Assert.AreEqual(payload, reconstructed);
    }

    [TestMethod]
    public void Deep_COBOL_comments_cap_visual_indent_and_count_tabs_conservatively()
    {
        string payload = string.Concat(Enumerable.Repeat("Z\t🙂", 100));
        var source = new StringBuilder();
        for (int depth = 0; depth < 20; depth++)
        {
            source.AppendLine("IF TRUE = TRUE THEN");
        }

        source.Append("//").Append(payload).AppendLine();
        for (int depth = 0; depth < 20; depth++)
        {
            source.AppendLine("END IF");
        }

        string[] sourceCommentLines = NormalizeNewlines(
                Generate(source.ToString(), TargetLanguage.Cobol).PrimaryFile.Content)
            .Split('\n')
            .Where(line =>
                line.Contains('Z') ||
                line.Contains("🙂", StringComparison.Ordinal))
            .ToArray();

        Assert.IsGreaterThan(1, sourceCommentLines.Length);
        foreach (string line in sourceCommentLines)
        {
            int indentLength = line.Length - line.TrimStart(' ').Length;
            Assert.IsLessThanOrEqualTo(40, indentLength, line);
            Assert.IsLessThanOrEqualTo(200, CobolSourceWidth(line), line);
        }
    }

    [TestMethod]
    public async Task Available_targets_run_the_normative_v061_program_with_zero_warnings()
    {
        EvaluationResult reference = _evaluator.Evaluate(NormativeAcceptanceSource);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));
        Assert.AreEqual(NormativeAcceptanceOutput, NormalizeNewlines(reference.Output));

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string[] lines = NormalizeNewlines(
                    Generate(NormativeAcceptanceSource, language).PrimaryFile.Content)
                .Split('\n');
            string marker = TargetMarker(language);
            foreach (string payload in new[]
            {
                " Traditional BASIC comment",
                " C-family comment",
                " Script-language comment",
                " SQL-style comment"
            })
            {
                int lineIndex = SingleLineIndex(lines, payload.TrimStart());
                Assert.AreEqual(marker + payload, lines[lineIndex].TrimStart(), language.ToString());
            }

            int finalHeaderComment = SingleLineIndex(lines, "SQL-style comment");
            Assert.AreEqual(
                string.Empty,
                lines[finalHeaderComment + 1],
                $"{language} lost the normative blank after the opening comment group.");
        }

        await AssertAvailableTargetsMatchEvaluator(
            NormativeAcceptanceSource,
            NormativeAcceptanceOutput,
            "v0.6.1 normative comment and layout acceptance");
    }

    [TestMethod]
    public async Task Available_targets_compile_target_safe_comments_without_swallowing_output()
    {
        string source =
            "REM Ends with backslash \\\n" +
            "REM Backslash before hspace \\   \n" +
            "REM Java-looking \\u000A escape\n" +
            "REM Unsafe " + '\0' + '\u0001' + '\u2028' + '\u2029' + " controls\n" +
            "REM Multilingual 台灣 مرحبا 🙂\n" +
            "REM " + new string('L', 500) + "\n" +
            "PRINT safe";

        await AssertAvailableTargetsMatchEvaluator(
            source,
            "safe\n",
            "target-safe comment payloads");
    }

    private async Task AssertAvailableTargetsMatchEvaluator(
        string source,
        string expectedOutput,
        string scenario)
    {
        EvaluationResult reference = _evaluator.Evaluate(source);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));
        Assert.AreEqual(expectedOutput, NormalizeNewlines(reference.Output));

        bool requireAllTargets = EnvironmentFlagIsEnabled(RequireAllTargetsEnvironmentVariable);
        bool requireJava = EnvironmentFlagIsEnabled(RequireJavaEnvironmentVariable);
        bool requireZeroWarnings = EnvironmentFlagIsEnabled(
            RequireZeroTargetWarningsEnvironmentVariable);
        var failures = new List<string>();
        int executed = 0;

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            GeneratedProgram generated = Generate(source, language);
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            if (!status.IsAvailable)
            {
                TestContext.WriteLine($"{language}: unavailable - {status.Message}");
                if (requireAllTargets ||
                    (requireJava && language is TargetLanguage.Java) ||
                    (requireZeroWarnings && language is TargetLanguage.CSharp))
                {
                    failures.Add($"{language}: required toolchain unavailable - {status.Message}");
                }

                continue;
            }

            BuildRunResult result = await toolchain.BuildAndRunAsync(
                generated,
                CancellationToken.None);
            string compilerOutput = FormatBuildAndErrorOutput(result);
            int failureCountBeforeTarget = failures.Count;
            if (GeneratedTargetWarningDetector.ContainsCompilerWarning(language, compilerOutput))
            {
                failures.Add(
                    $"{language}: generated {scenario} target emitted a compiler warning." +
                    Environment.NewLine + compilerOutput);
            }

            if (!result.Success || result.ExitCode != 0)
            {
                failures.Add(
                    $"{language}: {scenario} build/run failed." +
                    Environment.NewLine + compilerOutput);
            }
            else if (!string.Equals(
                    expectedOutput,
                    NormalizeNewlines(result.StandardOutput),
                    StringComparison.Ordinal))
            {
                failures.Add($"{language}: {scenario} stdout differed from SmileEvaluator.");
            }

            if (failureCountBeforeTarget == failures.Count)
            {
                executed++;
                TestContext.WriteLine($"{language}: {scenario} matched with zero detected warnings");
            }
        }

        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine + Environment.NewLine, failures));
        }

        if (requireAllTargets)
        {
            Assert.AreEqual(TargetLanguageInfo.All.Count, executed);
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No target toolchains are installed.");
        }
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static IEnumerable<string> FinalValuesByName(BoundProgramExecutionTrace trace) =>
        trace.FinalValues
            .OrderBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => pair.Key.Name + "=" + pair.Value.ToDisplayText());

    private static int SingleLineIndex(IReadOnlyList<string> lines, string payload)
    {
        int[] indices = lines
            .Select((line, index) => (line, index))
            .Where(pair => pair.line.Contains(payload, StringComparison.Ordinal))
            .Select(pair => pair.index)
            .ToArray();
        Assert.HasCount(1, indices, $"Expected one generated line containing '{payload}'.");
        return indices[0];
    }

    private static string TargetMarker(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.Python => "#",
            TargetLanguage.Cobol => "*>",
            TargetLanguage.MasmX64 => ";",
            _ => "//"
        };

    private static int CobolSourceWidth(string line)
    {
        int column = 0;
        foreach (Rune rune in line.EnumerateRunes())
        {
            column += rune.Value == '\t'
                ? 8 - (column % 8)
                : Encoding.UTF8.GetByteCount(rune.ToString());
        }

        return column;
    }

    private static bool EnvironmentFlagIsEnabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private static string FormatBuildAndErrorOutput(BuildRunResult result) =>
        string.Join(
            Environment.NewLine,
            new[] { result.BuildOutput, result.StandardError }
                .Where(output => !string.IsNullOrWhiteSpace(output)));

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
