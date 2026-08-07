using System.Diagnostics;
using System.Text;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class WhileStatementConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Parser_builds_one_canonical_WHILE_with_ordered_source_items()
    {
        ParseResult result = _transpiler.Parse(
            "LET Count = 0\n" +
            "WHILE Count < 2\n" +
            "    // preserved comment\n" +
            "\n" +
            "    SET Count = Count + 1\n" +
            "END WHILE");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        var loop = (WhileStatementSyntax)result.Program!.Statements[1];
        Assert.HasCount(3, loop.SourceItems);
        Assert.IsInstanceOfType<FullLineCommentSyntax>(loop.SourceItems[0]);
        Assert.IsInstanceOfType<BlankLineSyntax>(loop.SourceItems[1]);
        Assert.IsInstanceOfType<SetStatementSyntax>(loop.SourceItems[2]);
        Assert.HasCount(1, loop.Statements);
        Assert.AreEqual(2, loop.KeywordSpan.Line);
        Assert.AreEqual(1, loop.KeywordSpan.Column);
        Assert.AreEqual(5, loop.KeywordSpan.Length);
    }

    [TestMethod]
    [DataRow("WHILE Count < 5\nEND WHILE")]
    [DataRow("WHILE\tCount < 5\nEND\tWHILE")]
    [DataRow("while Count < 5\nend while")]
    [DataRow("WhIlE (Count < 5)\nEnD WhIlE")]
    [DataRow("WHILE Count < 5 AND Continue = TRUE\nEND WHILE")]
    public void Parser_accepts_official_header_casing_whitespace_and_EOF_forms(string whileSource)
    {
        ParseResult result = _transpiler.Parse(whileSource);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.IsInstanceOfType<WhileStatementSyntax>(result.Program!.Statements.Single());
    }

    [TestMethod]
    [DataRow(
        "WHILE\nEND WHILE",
        "SMILE1602",
        "WHILE requires a condition.")]
    [DataRow(
        "WHILE(Count < 5)\nEND WHILE",
        "SMILE1601",
        "WHILE must be followed by a space or tab.")]
    [DataRow(
        "WHILE Count < 5 THEN\nEND WHILE",
        "SMILE1606",
        "Unexpected content follows the WHILE condition.")]
    [DataRow(
        "WHILE Count < 5 DO\nEND WHILE",
        "SMILE1606",
        "Unexpected content follows the WHILE condition.")]
    [DataRow(
        "WHILE Count < 5 extra\nEND WHILE",
        "SMILE1606",
        "Unexpected content follows the WHILE condition.")]
    public void Invalid_WHILE_headers_report_the_official_diagnostic(
        string source,
        string expectedCode,
        string expectedMessage)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.First(item => item.Code == expectedCode);
        Assert.AreEqual(expectedMessage, diagnostic.Message);
        Assert.AreEqual(1, diagnostic.Span.Line);
    }

    [TestMethod]
    [DataRow("WHILE TRUE = TRUE", "SMILE1607")]
    [DataRow("END WHILE", "SMILE1609")]
    [DataRow("WHILE TRUE = TRUE\nEND WHILE extra", "SMILE1608")]
    [DataRow("WHILE TRUE = TRUE\nEND IF", "SMILE1607")]
    [DataRow("IF TRUE = TRUE THEN\nEND WHILE", "SMILE1609")]
    public void Missing_malformed_stray_and_mismatched_terminators_report_the_WHILE_diagnostic(
        string source,
        string expectedCode)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("ENDWHILE")]
    [DataRow("WEND")]
    [DataRow("LOOP")]
    public void Unsupported_WHILE_aliases_remain_invalid_unknown_statements(string alias)
    {
        ParseResult result = _transpiler.Parse(alias);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1609"));
    }

    [TestMethod]
    public void WHILE_is_reserved_but_alias_and_near_miss_words_remain_identifiers()
    {
        IReadOnlyList<SyntaxToken> tokens = new Lexer(
            "WHILE while While WHILECount WEND ENDWHILE LOOP BREAK CONTINUE").Lex();

        CollectionAssert.AreEqual(
            new[]
            {
                SyntaxKind.WhileKeyword,
                SyntaxKind.WhileKeyword,
                SyntaxKind.WhileKeyword,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.EndOfFileToken
            },
            tokens.Select(token => token.Kind).ToArray());

        BindResult binding = _transpiler.Bind("LET while = 1");
        Assert.IsFalse(binding.Success);
        Assert.IsTrue(binding.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1115"));
    }

    [TestMethod]
    [DataRow("Ready", "SMILE1603")]
    [DataRow("TRUE", "SMILE1603")]
    [DataRow("NOT Ready", "SMILE1603")]
    [DataRow("Count < 5 AND Ready", "SMILE1603")]
    [DataRow("Count + 1", "SMILE1604")]
    public void WHILE_conditions_reject_implicit_Boolean_leaves_and_non_Boolean_results(
        string condition,
        string expectedCode)
    {
        BindResult result = _transpiler.Bind($$"""
LET Count = 0
LET Ready = TRUE
WHILE {{condition}}
END WHILE
""");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("Ready = TRUE")]
    [DataRow("TRUE = TRUE")]
    [DataRow("NOT (Ready = FALSE)")]
    [DataRow("Count < 5 AND Ready = TRUE")]
    [DataRow("(Count >= 0 AND Count <= 5) OR Ready = FALSE")]
    public void Explicit_atomic_and_compound_WHILE_conditions_bind(string condition)
    {
        BindResult result = _transpiler.Bind($$"""
LET Count = 0
LET Ready = TRUE
WHILE {{condition}}
    SET Count = Count + 1
END WHILE
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
    }

    [TestMethod]
    public void Function_shaped_WHILE_condition_remains_invalid_without_call_grammar()
    {
        ParseResult result = _transpiler.Parse("WHILE FUNC(Value) > 0\nEND WHILE");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    [DataRow("LET Local = 1", 4)]
    [DataRow("LET Local =\"\nBlock content\n\"", 4)]
    [DataRow("IF Ready = TRUE THEN\n        LET Local = 1\n    END IF", 5)]
    [DataRow("IF Ready = TRUE THEN\n        LET Local =\"\nBlock content\n\"\n    END IF", 5)]
    [DataRow("WHILE Count < 2\n        LET Local = 1\n    END WHILE", 5)]
    public void LET_is_rejected_recursively_anywhere_inside_WHILE(string body, int expectedLine)
    {
        BindResult result = _transpiler.Bind($$"""
LET Count = 0
LET Ready = TRUE
WHILE Count < 1
    {{body}}
    SET Count = Count + 1
END WHILE
PRINT {Local}
""");

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.First(item => item.Code == "SMILE1610");
        Assert.AreEqual("LET is not permitted inside WHILE v1.0.", diagnostic.Message);
        Assert.AreEqual(expectedLine, diagnostic.Span.Line);
        Assert.AreEqual(3, diagnostic.Span.Length);
        Assert.IsTrue(result.Diagnostics.Any(item => item.Code == "SMILE1106"));
        Assert.IsFalse(result.Program!.Variables.Any(variable => variable.Name == "Local"));
    }

    [TestMethod]
    public void Comments_and_Block_String_content_cannot_open_or_close_WHILE()
    {
        BindResult result = _transpiler.Bind("""
LET Ready = FALSE
LET Text = ""
WHILE Ready = TRUE
    // END WHILE
    REM WHILE TRUE = TRUE
    SET Text ="
END WHILE
WHILE TRUE = TRUE
"
END WHILE
PRINT {Text}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        var loop = (BoundWhileStatement)result.Program!.Statements[2];
        Assert.HasCount(3, loop.SourceItems);
        Assert.HasCount(1, loop.Statements);
    }

    [TestMethod]
    public void Parser_accepts_exactly_128_WHILE_levels_and_reports_SMILE1611_at_129()
    {
        ParseResult accepted = _transpiler.Parse(CreateNestedWhileSource(128));
        ParseResult rejected = _transpiler.Parse(CreateNestedWhileSource(129));

        Assert.IsTrue(accepted.Success, Join(accepted.Diagnostics));
        Diagnostic diagnostic = rejected.Diagnostics.Single(item => item.Code == "SMILE1611");
        Assert.AreEqual(
            "Maximum combined IF/WHILE nesting depth of 128 exceeded at WHILE.",
            diagnostic.Message);
        Assert.AreEqual(129, diagnostic.Span.Line);
        Assert.AreEqual(1, diagnostic.Span.Column);
        Assert.AreEqual(5, diagnostic.Span.Length);
    }

    [TestMethod]
    public void Thousand_WHILE_levels_recover_without_stack_overflow_or_a_diagnostic_storm()
    {
        var stopwatch = Stopwatch.StartNew();
        ParseResult result = _transpiler.Parse(
            CreateNestedWhileSource(1_000, trailingSource: "PRINT Recovered\n"));
        stopwatch.Stop();

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1611"));
        Assert.IsLessThanOrEqualTo(3, result.Diagnostics.Count, Join(result.Diagnostics));
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
        Assert.IsLessThan(5_000L, stopwatch.ElapsedMilliseconds);
    }

    [TestMethod]
    public void Combined_IF_WHILE_depth_cannot_bypass_the_shared_limit()
    {
        ParseResult accepted = _transpiler.Parse(CreateAlternatingSource(128, startWithWhile: false));
        ParseResult rejectedAtIf = _transpiler.Parse(CreateAlternatingSource(129, startWithWhile: false));
        ParseResult rejectedAtWhile = _transpiler.Parse(CreateAlternatingSource(129, startWithWhile: true));

        Assert.IsTrue(accepted.Success, Join(accepted.Diagnostics));
        Assert.IsTrue(rejectedAtIf.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1416"));
        Assert.IsTrue(rejectedAtWhile.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1611"));
    }

    [TestMethod]
    public void Over_limit_WHILE_recovery_leaves_outer_IF_closers_for_their_owners()
    {
        var source = new StringBuilder();
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("IF TRUE = TRUE THEN");
        }

        source.AppendLine("WHILE TRUE = TRUE");
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("END IF");
        }

        source.AppendLine("PRINT Recovered");

        ParseResult result = _transpiler.Parse(source.ToString());

        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1611"));
        Assert.IsLessThanOrEqualTo(3, result.Diagnostics.Count, Join(result.Diagnostics));
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Over_limit_IF_recovery_leaves_outer_WHILE_closers_for_their_owners()
    {
        var source = new StringBuilder();
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("WHILE FALSE = TRUE");
        }

        source.AppendLine("IF TRUE = TRUE THEN");
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("END WHILE");
        }

        source.AppendLine("PRINT Recovered");

        ParseResult result = _transpiler.Parse(source.ToString());

        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1416"));
        Assert.IsLessThanOrEqualTo(3, result.Diagnostics.Count, Join(result.Diagnostics));
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    [DataRow(false, "SMILE1416")]
    [DataRow(true, "SMILE1611")]
    public void Alternating_1000_level_recovery_is_iterative_bounded_and_preserves_later_source(
        bool startWithWhile,
        string expectedDiagnostic)
    {
        ParseResult result = _transpiler.Parse(
            CreateAlternatingSource(
                1_000,
                startWithWhile,
                trailingSource: "PRINT Recovered"));

        Assert.IsTrue(result.Diagnostics.Any(
            diagnostic => diagnostic.Code == expectedDiagnostic));
        Assert.IsLessThanOrEqualTo(3, result.Diagnostics.Count, Join(result.Diagnostics));
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Over_limit_WHILE_recovery_leaves_outer_ELSE_for_its_IF_owner()
    {
        ParseResult result = _transpiler.Parse(
            CreateRejectedWhileAtOuterIfClauseBoundary("ELSE"));

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1611", result.Diagnostics[0].Code);
        IfStatementSyntax innermost = GetInnermostIf(result.Program!, depth: 128);
        Assert.IsTrue(innermost.HasElseClause);
        Assert.IsInstanceOfType<PrintStatementSyntax>(innermost.ElseStatements.Single());
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Over_limit_WHILE_recovery_leaves_outer_ELSE_IF_for_its_IF_owner()
    {
        ParseResult result = _transpiler.Parse(
            CreateRejectedWhileAtOuterIfClauseBoundary(
                "ELSE IF FALSE = TRUE THEN"));

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1611", result.Diagnostics[0].Code);
        IfStatementSyntax innermost = GetInnermostIf(result.Program!, depth: 128);
        Assert.HasCount(2, innermost.Clauses);
        Assert.IsInstanceOfType<PrintStatementSyntax>(
            innermost.Clauses[1].Statements.Single());
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    [DataRow("ELSE")]
    [DataRow("ELSE IF FALSE = TRUE THEN")]
    public void Over_limit_WHILE_recovery_keeps_rejected_subtree_IF_clauses_local(
        string clauseHeader)
    {
        ParseResult result = _transpiler.Parse(
            CreateRejectedWhileWithLocalIfClause(clauseHeader));

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1611", result.Diagnostics[0].Code);
        IfStatementSyntax innermost = GetInnermostIf(result.Program!, depth: 128);
        Assert.HasCount(1, innermost.Clauses);
        Assert.IsFalse(innermost.HasElseClause);
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Evaluator_matches_the_normative_WHILE_acceptance_program_exactly()
    {
        EvaluationResult result = _evaluator.Evaluate("""
REM SMILE v0.8.0 WHILE acceptance program

LET Count = 0
LET Total = 0

PRINT Enter a positive count:
INPUT Count

WHILE Count > 0
    SET Total = Total + Count
    PRINT Count={Count}, Total={Total}
    SET Count = Count - 1
END WHILE

PRINT Done. Total={Total}
""", "3\n");

        Assert.IsTrue(result.Success, Join(result.Diagnostics) + result.StandardError);
        Assert.AreEqual(
            "Enter a positive count:\n" +
            "Count=3, Total=3\n" +
            "Count=2, Total=5\n" +
            "Count=1, Total=6\n" +
            "Done. Total=6\n",
            Normalize(result.Output));
        Assert.AreEqual(string.Empty, result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void Evaluator_preserves_zero_iterations_nested_loops_and_IF_mutation()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Zero = 0
LET Row = 1
LET Column = 1

WHILE Zero > 0
    PRINT Never
END WHILE

WHILE Row <= 2
    SET Column = 1
    WHILE Column <= 2
        IF Column = 1 THEN
            PRINT {Row},{Column}
        ELSE
            PRINT {Row},{Column}
        END IF
        SET Column = Column + 1
    END WHILE
    SET Row = Row + 1
END WHILE
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("1,1\n1,2\n2,1\n2,2\n", Normalize(result.Output));
    }

    [TestMethod]
    public void Runtime_error_in_WHILE_condition_preserves_prior_stdout_and_stops_execution()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Divisor = 1

PRINT Before condition.
INPUT Divisor

WHILE 10 / Divisor > 1
    PRINT unreachable body
END WHILE

PRINT unreachable after loop
""", "0\n");

        Assert.IsFalse(result.Success);
        Assert.HasCount(0, result.Diagnostics, Join(result.Diagnostics));
        Assert.AreEqual("Before condition.\n", Normalize(result.StandardOutput));
        Assert.AreEqual(
            "SMILE Runtime Error SMILER1207: Division by zero.\n",
            Normalize(result.StandardError));
        Assert.AreEqual(1, result.ExitCode);
        Assert.IsNotNull(result.RuntimeError);
        Assert.AreEqual("SMILER1207", result.RuntimeError.Code);
    }

    [TestMethod]
    public void WHILE_inside_IF_executes_only_selected_then_and_else_bodies()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Gate = TRUE
LET Count = 0

IF Gate = TRUE THEN
    WHILE Count < 2
        PRINT Selected={Count}
        SET Count = Count + 1
    END WHILE
ELSE
    WHILE Count < 5
        PRINT unselected first loop
        SET Count = Count + 1
    END WHILE
END IF

SET Gate = FALSE

IF Gate = TRUE THEN
    WHILE Count < 5
        PRINT unselected second loop
        SET Count = Count + 1
    END WHILE
ELSE
    WHILE Count < 3
        PRINT Else={Count}
        SET Count = Count + 1
    END WHILE
END IF

PRINT Done={Count}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics) + result.StandardError);
        Assert.AreEqual("Selected=0\nSelected=1\nElse=2\nDone=3\n", Normalize(result.Output));
        Assert.AreEqual(string.Empty, result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void Unreached_WHILE_body_INPUT_consumes_no_line()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Count = 0
LET Value = "Before"
WHILE Count > 0
    INPUT Value
END WHILE
PRINT {Value}
""", string.Empty);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("Before\n", Normalize(result.Output));
        Assert.AreEqual(0, result.ExitCode);
    }

    [TestMethod]
    public async Task Infinite_WHILE_evaluation_observes_host_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        Task<EvaluationResult> evaluation = Task.Run(() =>
            _evaluator.Evaluate("WHILE TRUE = TRUE\nEND WHILE", cancellation.Token));

        await Task.Delay(50);
        cancellation.Cancel();

        try
        {
            _ = await evaluation.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Fail("The infinite WHILE evaluator did not observe cancellation.");
        }
        catch (OperationCanceledException)
        {
            // Host cancellation intentionally propagates instead of becoming a SMILE runtime error.
        }
    }

    [TestMethod]
    public void Fixed_point_records_stable_loop_head_and_post_loop_facts()
    {
        BindResult bind = _transpiler.Bind("""
LET Count = 0
LET Unchanged = 49
WHILE Count < 3
    SET Count = Count + 1
END WHILE
PRINT {Unchanged}
""");
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        var loop = (BoundWhileStatement)bind.Program!.Statements[2];
        VariableSymbol count = bind.Program.Variables.Single(variable => variable.Name == "Count");
        VariableSymbol unchanged = bind.Program.Variables.Single(variable => variable.Name == "Unchanged");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);
        BoundWhileStatementAnalysis facts = analysis.GetWhileFacts(loop);

        Assert.AreEqual(0, facts.Ordinal);
        Assert.AreEqual(0, analysis.GetWhileOrdinal(loop));
        Assert.IsFalse(facts.IncomingConditionIsKnownFalse);
        Assert.IsFalse(facts.ValuesAtHead[count].IsKnown);
        Assert.IsFalse(facts.ValuesAfter[count].IsKnown);
        Assert.IsTrue(facts.ValuesAtHead[unchanged].IsKnown);
        Assert.AreEqual(49L, facts.ValuesAtHead[unchanged].Value.IntegerValue);
        Assert.IsTrue(analysis.FinalValues[unchanged].IsKnown);
        Assert.AreEqual(49L, analysis.FinalValues[unchanged].Value.IntegerValue);
    }

    [TestMethod]
    public void Known_false_loop_preserves_incoming_facts_but_records_body_once()
    {
        BindResult bind = _transpiler.Bind("""
LET Count = 0
WHILE Count > 0
    SET Count = Count + 1
END WHILE
PRINT {Count}
""");
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        var loop = (BoundWhileStatement)bind.Program!.Statements[1];
        VariableSymbol count = bind.Program.Variables.Single();

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);
        BoundWhileStatementAnalysis facts = analysis.GetWhileFacts(loop);

        Assert.IsTrue(facts.IncomingConditionIsKnownFalse);
        Assert.IsTrue(facts.ValuesAfter[count].IsKnown);
        Assert.AreEqual(0L, facts.ValuesAfter[count].Value.IntegerValue);
        Assert.IsTrue(analysis.FinalValues[count].IsKnown);
        Assert.AreEqual(0L, analysis.FinalValues[count].Value.IntegerValue);
        Assert.HasCount(4, analysis.EnumerateStatements());
        int[] ordinals = analysis.EnumerateStatements()
            .Select(statement => analysis.GetStatementFacts(statement).Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(0, 4).ToArray(), ordinals);
    }

    [TestMethod]
    public void Nested_WHILE_ordinals_and_statement_facts_are_deterministic_and_recorded_once()
    {
        BindResult bind = _transpiler.Bind("""
LET Row = 0
LET Column = 0
WHILE Row < 2
    SET Column = 0
    WHILE Column < 2
        SET Column = Column + 1
    END WHILE
    SET Row = Row + 1
END WHILE
""");
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        var outer = (BoundWhileStatement)bind.Program!.Statements[2];
        var inner = (BoundWhileStatement)outer.Statements[1];

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.AreEqual(0, analysis.GetWhileOrdinal(outer));
        Assert.AreEqual(1, analysis.GetWhileOrdinal(inner));
        Assert.AreEqual(0, analysis.GetWhileFacts(outer).Ordinal);
        Assert.AreEqual(1, analysis.GetWhileFacts(inner).Ordinal);
        Assert.HasCount(7, analysis.EnumerateStatements());
        Assert.HasCount(
            7,
            analysis.EnumerateStatements().Distinct(ReferenceEqualityComparer.Instance).ToArray());
    }

    [TestMethod]
    [DataRow("SET Text = \"Fixed\"")]
    [DataRow("SET Text = \"\nFixed\n\"")]
    [DataRow("INPUT Text")]
    [DataRow("SET Text = Other")]
    [DataRow("SET Text = Text")]
    [DataRow("SET Text = Text + \"\"")]
    public void WHILE_accepts_every_official_finite_String_assignment_shape(string assignment)
    {
        TranspileResult result = _transpiler.Transpile($$"""
LET Text = ""
LET Other = "Bounded"
LET Continue = FALSE
WHILE Continue = TRUE
    {{assignment}}
END WHILE
PRINT {Text}
""", TargetLanguage.CSharp);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1612"));
    }

    [TestMethod]
    [DataRow("SET Text = Text + \"x\"")]
    [DataRow("SET Text = Other + \"x\"\n    SET Other = Text")]
    public void Unbounded_String_recurrence_reports_SMILE1612_on_the_WHILE_opener(string assignments)
    {
        string source = $$"""
        LET Text = ""
        LET Other = ""
        LET Continue = FALSE
        WHILE Continue = TRUE
            {{assignments}}
        END WHILE
        """;
        BindResult bind = _transpiler.Bind(source);
        Assert.IsFalse(bind.Success);
        Assert.IsNotNull(bind.Program);
        Diagnostic diagnostic = bind.Diagnostics.Single(item => item.Code == "SMILE1612");

        Assert.AreEqual(
            "A WHILE loop produces a String value without a finite compile-time UTF-8 size bound.",
            diagnostic.Message);
        Assert.AreEqual(4, diagnostic.Span.Line);
        Assert.AreEqual(1, diagnostic.Span.Column);
        Assert.AreEqual(5, diagnostic.Span.Length);
        TranspileResult transpile = _transpiler.Transpile(source, TargetLanguage.CSharp);
        Assert.IsFalse(transpile.Success);
        Assert.IsTrue(transpile.Diagnostics.Any(item => item.Code == "SMILE1612"));
    }

    private static string CreateNestedWhileSource(int depth, string trailingSource = "")
    {
        var source = new StringBuilder();
        for (int index = 0; index < depth; index++)
        {
            source.AppendLine("WHILE FALSE = TRUE");
        }

        source.AppendLine("PRINT Reached");
        for (int index = 0; index < depth; index++)
        {
            source.AppendLine("END WHILE");
        }

        source.Append(trailingSource);
        return source.ToString();
    }

    private static string CreateAlternatingSource(
        int depth,
        bool startWithWhile,
        string trailingSource = "")
    {
        var openers = new List<bool>(depth);
        var source = new StringBuilder();
        for (int index = 0; index < depth; index++)
        {
            bool isWhile = (index % 2 == 0) == startWithWhile;
            openers.Add(isWhile);
            source.AppendLine(isWhile ? "WHILE FALSE = TRUE" : "IF FALSE = TRUE THEN");
        }

        source.AppendLine("PRINT Reached");
        for (int index = openers.Count - 1; index >= 0; index--)
        {
            source.AppendLine(openers[index] ? "END WHILE" : "END IF");
        }

        source.Append(trailingSource);
        return source.ToString();
    }

    private static string CreateRejectedWhileAtOuterIfClauseBoundary(string clauseHeader)
    {
        var source = new StringBuilder();
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("IF TRUE = TRUE THEN");
        }

        source.AppendLine("WHILE TRUE = TRUE");
        source.AppendLine(clauseHeader);
        source.AppendLine("PRINT Owned clause");
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("END IF");
        }

        source.AppendLine("PRINT Recovered");
        return source.ToString();
    }

    private static string CreateRejectedWhileWithLocalIfClause(string clauseHeader)
    {
        var source = new StringBuilder();
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("IF TRUE = TRUE THEN");
        }

        source.AppendLine("WHILE TRUE = TRUE");
        source.AppendLine("IF TRUE = TRUE THEN");
        source.AppendLine(clauseHeader);
        source.AppendLine("PRINT Rejected local clause");
        source.AppendLine("END IF");
        source.AppendLine("END WHILE");
        for (int index = 0; index < 128; index++)
        {
            source.AppendLine("END IF");
        }

        source.AppendLine("PRINT Recovered");
        return source.ToString();
    }

    private static IfStatementSyntax GetInnermostIf(SmileProgramSyntax program, int depth)
    {
        var current = (IfStatementSyntax)program.Statements[0];
        for (int index = 1; index < depth; index++)
        {
            current = (IfStatementSyntax)current.Clauses[0].Statements.Single();
        }

        return current;
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);
}
