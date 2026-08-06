using SMILE.Engine;
using System.Reflection;
using System.Text;

namespace SMILE.Tests;

[TestClass]
public sealed class IfStatementConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Parser_builds_one_canonical_clause_chain()
    {
        ParseResult result = _transpiler.Parse("""
IF Score >= 90 THEN
    PRINT A
ELSE IF Score >= 80 THEN
    PRINT B
ELSE IF Score >= 70 THEN
ELSE
    PRINT C
END IF
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        var conditional = (IfStatementSyntax)result.Program!.Statements.Single();
        Assert.HasCount(3, conditional.Clauses);
        Assert.IsTrue(conditional.HasElseClause);
        Assert.HasCount(1, conditional.ElseStatements);
        Assert.HasCount(0, conditional.Clauses[2].Statements);
    }

    [TestMethod]
    public void Standalone_else_followed_by_if_is_a_nested_statement()
    {
        ParseResult result = _transpiler.Parse("""
IF A = 1 THEN
ELSE
    IF B = 2 THEN
    END IF
END IF
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        var outer = (IfStatementSyntax)result.Program!.Statements.Single();
        Assert.HasCount(1, outer.Clauses);
        Assert.IsTrue(outer.HasElseClause);
        var nested = (IfStatementSyntax)outer.ElseStatements.Single();
        Assert.HasCount(1, nested.Clauses);
    }

    [TestMethod]
    public void Parser_accepts_case_tabs_empty_blocks_and_eof_after_end_if()
    {
        ParseResult result = _transpiler.Parse(
            "if\tTRUE = TRUE\tthen\nelse\tif\tFALSE = TRUE\tthen\nelse\nend\tif");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        var conditional = (IfStatementSyntax)result.Program!.Statements.Single();
        Assert.HasCount(2, conditional.Clauses);
        Assert.IsTrue(conditional.HasElseClause);
        Assert.IsTrue(conditional.Clauses.All(clause => clause.Statements.Count == 0));
        Assert.HasCount(0, conditional.ElseStatements);
    }

    [TestMethod]
    public void Then_inside_string_or_interpolation_is_not_the_header_terminator()
    {
        BindResult result = _transpiler.Bind("""
LET Word = "THEN"
IF Word = $"THEN" THEN
    PRINT Selected
END IF
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
    }

    [TestMethod]
    public void If_header_scanner_accepts_literal_braces_and_then_inside_interpolated_text()
    {
        BindResult result = _transpiler.Bind("""
IF $"{{THEN" = "{THEN" THEN
    PRINT Selected
END IF
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
    }

    [TestMethod]
    public void Else_if_header_scanner_accepts_literal_braces_and_then_inside_interpolated_text()
    {
        BindResult result = _transpiler.Bind("""
IF FALSE = TRUE THEN
ELSE IF $"{{THEN" = "{THEN" THEN
    PRINT Selected
END IF
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
    }

    [TestMethod]
    public void Set_block_string_lines_inside_if_are_consumed_as_one_statement()
    {
        BindResult result = _transpiler.Bind("""
LET Ready = TRUE
LET Message = ""
IF Ready = TRUE THEN
    SET Message ="
ELSE
END IF
"
END IF
PRINT {Message}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        var conditional = (BoundIfStatement)result.Program!.Statements[2];
        Assert.HasCount(1, conditional.Clauses[0].Statements);
    }

    [TestMethod]
    public void Parser_accepts_the_first_if_nesting_level()
    {
        ParseResult result = _transpiler.Parse(CreateNestedIfSource(1));

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.IsInstanceOfType<IfStatementSyntax>(result.Program!.Statements.Single());
    }

    [TestMethod]
    public void Complete_pipeline_accepts_exactly_128_if_nesting_levels()
    {
        string source = CreateNestedIfSource(128);

        EvaluationResult evaluation = _evaluator.Evaluate(source);
        TranspileResult transpile = _transpiler.Transpile(source, TargetLanguage.CSharp);

        Assert.IsTrue(evaluation.Success, Join(evaluation.Diagnostics));
        Assert.AreEqual("Reached\n", Normalize(evaluation.Output));
        Assert.IsTrue(transpile.Success, Join(transpile.Diagnostics));
        Assert.IsNotNull(transpile.GeneratedProgram);
    }

    [TestMethod]
    public void Parser_reports_SMILE1416_on_the_129th_if_keyword()
    {
        string source = CreateNestedIfSource(129);
        ParseResult result = _transpiler.Parse(source);
        TranspileResult transpile = _transpiler.Transpile(source, TargetLanguage.CSharp);

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(diagnostic => diagnostic.Code == "SMILE1416");
        Assert.AreEqual("Maximum IF nesting depth of 128 exceeded.", diagnostic.Message);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(129, diagnostic.Span.Line);
        Assert.AreEqual(1, diagnostic.Span.Column);
        Assert.AreEqual(2, diagnostic.Span.Length);
        Assert.IsFalse(transpile.Success);
        Assert.IsNull(transpile.GeneratedProgram);
        Assert.HasCount(1, transpile.Diagnostics);
        Assert.AreEqual("SMILE1416", transpile.Diagnostics[0].Code);
    }

    [TestMethod]
    public void Parser_recovers_from_1000_if_levels_without_a_diagnostic_storm()
    {
        ParseResult result = _transpiler.Parse(CreateNestedIfSource(1_000));

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1416", result.Diagnostics[0].Code);
    }

    [TestMethod]
    public void Over_limit_recovery_keeps_same_line_else_if_and_later_top_level_code_balanced()
    {
        string source = CreateNestedIfSource(
            129,
            innermostBody: "ELSE IF FALSE = TRUE THEN\n",
            trailingSource: "PRINT Recovered\n");

        ParseResult result = _transpiler.Parse(source);

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1416", result.Diagnostics[0].Code);
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Over_limit_recovery_counts_if_after_standalone_else_as_nested()
    {
        string source = CreateNestedIfSource(
            129,
            innermostBody: "ELSE\nIF FALSE = TRUE THEN\nEND IF\n",
            trailingSource: "PRINT Recovered\n");

        ParseResult result = _transpiler.Parse(source);

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1416", result.Diagnostics[0].Code);
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    [DataRow("END IF extra")]
    [DataRow("ENDIF")]
    public void Over_limit_recovery_balances_malformed_end_as_the_rejected_if_boundary(
        string malformedEnd)
    {
        string source = CreateNestedIfSource(
            128,
            innermostBody: $"IF TRUE = TRUE THEN\n{malformedEnd}\n",
            trailingSource: "PRINT Recovered\n");

        ParseResult result = _transpiler.Parse(source);

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1416", result.Diagnostics[0].Code);
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Over_limit_recovery_ignores_control_flow_text_inside_set_block_strings()
    {
        string source = CreateNestedIfSource(
            129,
            innermostBody: "SET Message =\"\nEND IF\nIF FALSE = TRUE THEN\nELSE\n\"\n",
            trailingSource: "PRINT Recovered\n");

        ParseResult result = _transpiler.Parse(source);

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1416", result.Diagnostics[0].Code);
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    public void Over_limit_recovery_ignores_control_flow_text_after_a_misplaced_set_block_opener()
    {
        string source = CreateNestedIfSource(
            129,
            innermostBody: "SET Message = \"prefix\" + \"\nEND IF\nIF FALSE = TRUE THEN\n\"\n",
            trailingSource: "PRINT Recovered\n");

        ParseResult result = _transpiler.Parse(source);

        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1416", result.Diagnostics[0].Code);
        Assert.IsInstanceOfType<PrintStatementSyntax>(result.Program!.Statements.Last());
    }

    [TestMethod]
    [DataRow("IF X = 1\nEND IF", "SMILE1405")]
    [DataRow("IF X = 1 THEN PRINT One\nEND IF", "SMILE1406")]
    [DataRow("ELSE", "SMILE1411")]
    [DataRow("ELSE IF TRUE = TRUE THEN", "SMILE1411")]
    [DataRow("END IF", "SMILE1411")]
    [DataRow("ELSE PRINT One", "SMILE1407")]
    [DataRow("ELSE-IF TRUE = TRUE THEN", "SMILE1407")]
    [DataRow("IF TRUE = TRUE THEN\nELSEIF FALSE = TRUE THEN\nEND IF", "SMILE1415")]
    [DataRow("IF TRUE = TRUE THEN", "SMILE1412")]
    [DataRow("IF TRUE = TRUE THEN\nELSE IF FALSE = TRUE\nEND IF", "SMILE1405")]
    [DataRow("IF TRUE = TRUE THEN\nELSE IF FALSE = TRUE THEN PRINT Wrong\nEND IF", "SMILE1406")]
    [DataRow("IF TRUE = TRUE THEN\nELSE\nELSE\nEND IF", "SMILE1409")]
    [DataRow("IF TRUE = TRUE THEN\nELSE\nELSE IF TRUE = TRUE THEN\nEND IF", "SMILE1410")]
    [DataRow("IF TRUE = TRUE THEN\nENDIF", "SMILE1413")]
    [DataRow("IF TRUE = TRUE THEN\nEND", "SMILE1413")]
    [DataRow("IF TRUE = TRUE THEN\nEND IF extra", "SMILE1413")]
    [DataRow("IF THEN\nEND IF", "SMILE1401")]
    [DataRow("IF TRUE = TRUE THEN\nELSE IF THEN\nEND IF", "SMILE1408")]
    [DataRow("IF Age >= THEN\nEND IF", "SMILE1201")]
    public void Invalid_block_forms_report_the_official_diagnostic(
        string source,
        string expectedCode)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode), Join(result.Diagnostics));
    }

    [TestMethod]
    public void Elseif_and_endif_are_not_combined_keywords()
    {
        IReadOnlyList<SyntaxToken> tokens = new Lexer("ELSEIF ENDIF IF THEN ELSE END").Lex();

        CollectionAssert.AreEqual(
            new[]
            {
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IfKeyword,
                SyntaxKind.ThenKeyword,
                SyntaxKind.ElseKeyword,
                SyntaxKind.EndKeyword,
                SyntaxKind.EndOfFileToken
            },
            tokens.Select(token => token.Kind).ToArray());
    }

    [TestMethod]
    [DataRow("IF")]
    [DataRow("if")]
    [DataRow("THEN")]
    [DataRow("Else")]
    [DataRow("end")]
    public void If_words_are_case_insensitive_reserved_keywords(string name)
    {
        BindResult result = _transpiler.Bind($"LET {name} = 1");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1115"), Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("IF Ready THEN\nEND IF")]
    [DataRow("IF TRUE THEN\nEND IF")]
    [DataRow("IF (Ready) THEN\nEND IF")]
    [DataRow("IF NOT Ready THEN\nEND IF")]
    [DataRow("IF Age >= 18 AND Ready THEN\nEND IF")]
    [DataRow("IF TRUE OR Age >= 18 THEN\nEND IF")]
    public void Every_boolean_leaf_requires_an_explicit_comparison(string ifSource)
    {
        string source = "LET Age = 49\nLET Ready = TRUE\n" + ifSource;
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1402"), Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("IF Ready = TRUE THEN\nEND IF")]
    [DataRow("IF TRUE = TRUE THEN\nEND IF")]
    [DataRow("IF NOT (Ready = TRUE) THEN\nEND IF")]
    [DataRow("IF Age >= 18 AND Ready = TRUE THEN\nEND IF")]
    [DataRow("IF (Age >= 18 AND Age <= 65) OR Ready = TRUE THEN\nEND IF")]
    [DataRow("IF Age + 1 >= 50 THEN\nEND IF")]
    public void Explicit_atomic_and_compound_conditions_bind(string ifSource)
    {
        string source = "LET Age = 49\nLET Ready = TRUE\n" + ifSource;
        BindResult result = _transpiler.Bind(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("IF Age + 1 THEN\nEND IF")]
    [DataRow("IF Name + \"!\" THEN\nEND IF")]
    public void Non_boolean_complete_conditions_report_SMILE1403(string ifSource)
    {
        string source = "LET Age = 49\nLET Name = \"Sin\"\n" + ifSource;
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1403"), Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("Ready", "SMILE1402")]
    [DataRow("(Ready)", "SMILE1402")]
    [DataRow("NOT Ready", "SMILE1402")]
    [DataRow("Age >= 18 AND Ready", "SMILE1402")]
    [DataRow("TRUE OR Age >= 18", "SMILE1402")]
    [DataRow("Age + 1", "SMILE1403")]
    [DataRow("Name + \"!\"", "SMILE1403")]
    public void Else_if_conditions_reject_the_same_invalid_shapes_as_initial_if(
        string condition,
        string expectedCode)
    {
        BindResult result = _transpiler.Bind($$"""
LET Age = 49
LET Ready = TRUE
LET Name = "Sin"
IF Ready = FALSE THEN
ELSE IF {{condition}} THEN
END IF
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
    [DataRow("Age >= 18 AND Ready = TRUE")]
    [DataRow("(Age >= 18 AND Age <= 65) OR Ready = TRUE")]
    [DataRow("Name + \"!\" = \"Sin!\"")]
    public void Else_if_conditions_accept_the_same_valid_shapes_as_initial_if(string condition)
    {
        BindResult result = _transpiler.Bind($$"""
LET Age = 49
LET Ready = TRUE
LET Name = "Sin"
IF Ready = FALSE THEN
ELSE IF {{condition}} THEN
END IF
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("left", false)]
    [DataRow("right", false)]
    [DataRow("atomic-call", false)]
    [DataRow("and", false)]
    [DataRow("or", false)]
    [DataRow("not", false)]
    [DataRow("left", true)]
    public void Future_expression_kinds_fail_closed_as_condition_calls(
        string shape,
        bool placeInElseIf)
    {
        BindResult result = BindSyntheticIfCondition(
            CreateFutureCondition(shape),
            placeInElseIf);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1404"), Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("IF FUNC(A) > 10 THEN\nEND IF")]
    [DataRow("IF TRUE = FALSE THEN\nELSE IF FUNC(A) > 10 THEN\nEND IF")]
    [DataRow("IF Result = FUNC(A) THEN\nEND IF")]
    public void Function_shaped_if_source_remains_invalid_without_call_grammar(string source)
    {
        // Functions are not syntax in v0.6.0.1, so these sources must fail at
        // the current expression parser and never reach generation or runtime.
        // SMILE1404 remains reserved for the day call syntax exists: that work
        // must update this regression to reject a syntactically valid call in
        // an IF condition through the binder's permanent call-free rule.
        ParseResult parse = _transpiler.Parse(source);
        TranspileResult transpile = _transpiler.Transpile(source, TargetLanguage.CSharp);
        EvaluationResult evaluation = _evaluator.Evaluate(source);

        Assert.IsFalse(parse.Success);
        Assert.IsTrue(parse.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsFalse(transpile.Success);
        Assert.IsNull(transpile.GeneratedProgram);
        Assert.IsTrue(transpile.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.IsFalse(evaluation.Success);
        Assert.AreEqual(string.Empty, evaluation.Output);
        Assert.IsTrue(evaluation.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [TestMethod]
    [DataRow("IF Ready = TRUE THEN\n    LET Local = 1\nEND IF")]
    [DataRow("IF Ready = TRUE THEN\nELSE IF Ready = FALSE THEN\n    LET Local = 1\nEND IF")]
    [DataRow("IF Ready = TRUE THEN\nELSE\n    LET Local = 1\nEND IF")]
    [DataRow("IF Ready = TRUE THEN\n    IF Ready = TRUE THEN\n        LET Local = 1\n    END IF\nEND IF")]
    public void Let_is_rejected_in_every_if_body_without_leaking_a_symbol(string ifSource)
    {
        BindResult result = _transpiler.Bind(
            "LET Ready = TRUE\n" + ifSource + "\nPRINT {Local}");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1414"), Join(result.Diagnostics));
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1106"), Join(result.Diagnostics));
        Assert.IsFalse(result.Program!.Variables.Any(variable => variable.Name == "Local"));
    }

    [TestMethod]
    public void Evaluator_executes_first_matching_clause_and_persists_selected_sets()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Score = 85
LET Grade = ""
IF Score >= 90 THEN
    SET Grade = "A"
ELSE IF Score >= 80 THEN
    SET Grade = "B"
ELSE IF Score >= 70 THEN
    SET Grade = "C"
ELSE
    SET Grade = "Below C"
END IF
PRINT {Grade}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("B\n", Normalize(result.Output));
    }

    [TestMethod]
    public void If_condition_observes_an_earlier_set_in_source_order()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Score = 1
LET Selected = "before"
SET Score = 2
IF Score = 2 THEN
    SET Selected = "after"
ELSE
    SET Selected = "stale"
END IF
PRINT {Selected}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("after\n", Normalize(result.Output));
    }

    [TestMethod]
    public void A_selected_empty_branch_executes_no_else_body()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Selected = "unchanged"
IF TRUE = TRUE THEN
ELSE
    SET Selected = "wrong"
END IF
PRINT {Selected}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("unchanged\n", Normalize(result.Output));
    }

    [TestMethod]
    [DataRow(49, "different")]
    [DataRow(50, "same")]
    public void Not_equals_comparisons_select_the_expected_branch(int age, string expected)
    {
        EvaluationResult result = _evaluator.Evaluate($$"""
LET Age = {{age}}
LET Selected = ""
IF Age <> 50 THEN
    SET Selected = "different"
ELSE
    SET Selected = "same"
END IF
PRINT {Selected}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(expected + "\n", Normalize(result.Output));
    }

    [TestMethod]
    public void A_known_unselected_branch_is_still_name_resolved_and_type_checked()
    {
        BindResult result = _transpiler.Bind("""
LET Ready = TRUE
LET Count = 1
IF Ready = TRUE THEN
    PRINT Selected
ELSE
    SET Count = "wrong type"
END IF
""");

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1305"), Join(result.Diagnostics));
    }

    [TestMethod]
    [DataRow("FALSE = TRUE AND 1 / 0 = 0", "ELSE")]
    [DataRow("TRUE = TRUE OR 1 / 0 = 0", "IF")]
    [DataRow("FALSE = TRUE AND 9223372036854775807 + 1 = 0", "ELSE")]
    [DataRow("TRUE = TRUE OR 9223372036854775807 + 1 = 0", "IF")]
    public void If_conditions_short_circuit_unreachable_runtime_failures(
        string condition,
        string expected)
    {
        EvaluationResult result = _evaluator.Evaluate($$"""
LET Selected = ""
IF {{condition}} THEN
    SET Selected = "IF"
ELSE
    SET Selected = "ELSE"
END IF
PRINT {Selected}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(expected + "\n", Normalize(result.Output));
        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code is "SMILE1206" or "SMILE1207"));
    }

    [TestMethod]
    [DataRow("Number = 5", "IF")]
    [DataRow("Number <> 5", "ELSE")]
    [DataRow("Number < 6", "IF")]
    [DataRow("Number <= 4", "ELSE")]
    [DataRow("Number > 4", "IF")]
    [DataRow("Number >= 6", "ELSE")]
    [DataRow("FirstName + LastName = \"SinCioco\"", "IF")]
    [DataRow("FirstName + LastName <> \"SinCioco\"", "ELSE")]
    public void If_conditions_execute_all_comparison_operators_and_String_expressions(
        string condition,
        string expected)
    {
        EvaluationResult result = _evaluator.Evaluate($$"""
LET Number = 5
LET FirstName = "Sin"
LET LastName = "Cioco"
LET Selected = ""
IF {{condition}} THEN
    SET Selected = "IF"
ELSE
    SET Selected = "ELSE"
END IF
PRINT {Selected}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(expected + "\n", Normalize(result.Output));
    }

    [TestMethod]
    [DataRow(100, "IF")]
    [DataRow(85, "FIRST ELSE IF")]
    [DataRow(75, "MIDDLE ELSE IF")]
    [DataRow(65, "FINAL ELSE IF")]
    [DataRow(50, "ELSE")]
    public void Clause_selection_matrix_executes_only_one_body(int score, string expected)
    {
        string source = $$"""
LET Score = {{score}}
LET Selected = ""
IF Score >= 90 THEN
    SET Selected = "IF"
ELSE IF Score >= 80 THEN
    SET Selected = "FIRST ELSE IF"
ELSE IF Score >= 70 THEN
    SET Selected = "MIDDLE ELSE IF"
ELSE IF Score >= 60 THEN
    SET Selected = "FINAL ELSE IF"
ELSE
    SET Selected = "ELSE"
END IF
PRINT {Selected}
""";

        EvaluationResult result = _evaluator.Evaluate(source);
        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(expected + "\n", Normalize(result.Output));
    }

    [TestMethod]
    public void False_if_without_else_executes_no_body()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Value = "unchanged"
IF TRUE = FALSE THEN
    SET Value = "changed"
END IF
PRINT {Value}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("unchanged\n", Normalize(result.Output));
    }

    [TestMethod]
    public void Nested_if_and_block_string_execute_recursively_with_exact_content()
    {
        EvaluationResult result = _evaluator.Evaluate("""
LET Outer = TRUE
LET Inner = TRUE
LET Message = ""
IF Outer = TRUE THEN
    IF Inner = TRUE THEN
        SET Message ="
        Grade B
        Ready for the next lesson.
        "
    END IF
END IF
PRINT {Message}
""");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual("Grade B\nReady for the next lesson.\n", Normalize(result.Output));
    }

    [TestMethod]
    public void Official_acceptance_program_matches_the_normative_output()
    {
        EvaluationResult result = _evaluator.Evaluate(OfficialAcceptanceSource);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.AreEqual(
            "Grade=B\nGrade B\nReady for the next lesson.\n",
            Normalize(result.Output));
    }

    [TestMethod]
    public void Branch_analysis_keeps_same_outgoing_value_known()
    {
        (BoundProgram program, VariableSymbol result) = BindResultVariable("""
LET Ready = TRUE
LET Result = ""
IF Ready = TRUE THEN
    SET Result = "same"
ELSE
    SET Result = "same"
END IF
""", "Result");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        Assert.IsTrue(analysis.FinalValues[result].IsKnown);
        Assert.AreEqual("same", analysis.FinalValues[result].Value.StringValue);
    }

    [TestMethod]
    public void Branch_analysis_marks_different_outgoing_values_unknown()
    {
        (BoundProgram program, VariableSymbol result) = BindResultVariable("""
LET Ready = TRUE
LET Result = ""
IF Ready = TRUE THEN
    SET Result = "yes"
ELSE
    SET Result = "no"
END IF
""", "Result");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        Assert.IsFalse(analysis.FinalValues[result].IsKnown);
        CollectionAssert.AreEquivalent(
            new[] { string.Empty, "yes", "no" },
            analysis.AssignedValues[result].Select(value => value.StringValue).ToArray());
    }

    [TestMethod]
    public void If_without_else_merges_the_changed_and_unchanged_paths()
    {
        (BoundProgram program, VariableSymbol result) = BindResultVariable("""
LET Ready = TRUE
LET Result = "before"
IF Ready = TRUE THEN
    SET Result = "after"
END IF
""", "Result");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        Assert.IsFalse(analysis.FinalValues[result].IsKnown);
    }

    [TestMethod]
    public void Nested_and_multi_clause_analysis_merges_recursively_and_unions_mutations()
    {
        (BoundProgram program, VariableSymbol result) = BindResultVariable("""
LET A = 1
LET B = 2
LET Result = "start"
IF A = 0 THEN
    SET Result = "zero"
ELSE IF A = 1 THEN
    IF B = 2 THEN
        SET Result = "nested"
    ELSE
        SET Result = "other"
    END IF
ELSE
    SET Result = "fallback"
END IF
""", "Result");

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        Assert.IsFalse(analysis.FinalValues[result].IsKnown);
        Assert.Contains(result, analysis.MutatedVariables);
        Assert.AreEqual("nested", analysis.FinalConcreteValues[result].StringValue);
        Assert.HasCount(9, analysis.EnumerateStatements());
    }

    [TestMethod]
    public void Branch_analysis_propagates_every_possible_value_through_a_direct_copy()
    {
        BindResult bind = _transpiler.Bind("""
LET Ready = TRUE
LET Source = ""
LET Copy = ""
IF Ready = TRUE THEN
    SET Source = "plain"
ELSE
    SET Source = "A\0B"
END IF
SET Copy = Source
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol copy = bind.Program!.Variables.Single(variable => variable.Name == "Copy");
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.IsFalse(analysis.FinalValues[copy].IsKnown);
        CollectionAssert.AreEquivalent(
            new[] { string.Empty, "plain", "A\0B" },
            analysis.AssignedValues[copy].Select(value => value.StringValue).ToArray());
    }

    [TestMethod]
    public void Branch_analysis_propagates_possible_values_through_concat_and_interpolation()
    {
        BindResult bind = _transpiler.Bind("""
LET Ready = TRUE
LET Source = ""
LET Decorated = ""
LET Message = ""
IF Ready = TRUE THEN
    SET Source = "A"
ELSE
    SET Source = "B"
END IF
SET Decorated = Source + "!"
SET Message = $"Value={Source}"
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol decorated = bind.Program!.Variables.Single(variable => variable.Name == "Decorated");
        VariableSymbol message = bind.Program.Variables.Single(variable => variable.Name == "Message");
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        CollectionAssert.AreEquivalent(
            new[] { string.Empty, "A!", "B!" },
            analysis.AssignedValues[decorated].Select(value => value.StringValue).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { string.Empty, "Value=A", "Value=B" },
            analysis.AssignedValues[message].Select(value => value.StringValue).ToArray());
    }

    [TestMethod]
    public void Branch_analysis_summarizes_many_independent_values_without_a_cartesian_expansion()
    {
        const int variableCount = 32;
        var source = new StringBuilder();
        source.AppendLine("LET Ready = TRUE");
        for (int index = 0; index < variableCount; index++)
        {
            source.Append("LET Value").Append(index).AppendLine(" = \"\"");
        }

        source.AppendLine("LET Combined = \"\"");
        for (int index = 0; index < variableCount; index++)
        {
            source.AppendLine("IF Ready = TRUE THEN");
            source.Append("    SET Value").Append(index).AppendLine(" = \"A\"");
            source.AppendLine("ELSE");
            source.Append("    SET Value").Append(index).Append(" = ");
            source.AppendLine(index == variableCount - 1 ? "\"Z\\0\"" : "\"B\"");
            source.AppendLine("END IF");
        }

        source.Append("SET Combined = $\"");
        for (int index = 0; index < variableCount; index++)
        {
            source.Append("{Value").Append(index).Append('}');
        }

        source.AppendLine("\"");

        BindResult bind = _transpiler.Bind(source.ToString());
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol combined = bind.Program!.Variables.Single(variable => variable.Name == "Combined");
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.Contains(combined, analysis.VariablesWithInexactAssignedValues);
        Assert.AreEqual(variableCount + 1, analysis.MaximumAssignedUtf8ByteLength(combined));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(combined));
    }

    [TestMethod]
    public void Repeated_branch_merges_bound_exact_candidates_but_keep_sound_String_facts()
    {
        const int branchCount = 20;
        var source = new StringBuilder();
        source.AppendLine("LET Ready = TRUE");
        source.AppendLine("LET Value = \"\"");
        for (int index = 0; index < branchCount; index++)
        {
            source.AppendLine("IF Ready = TRUE THEN");
            source.AppendLine("    SET Value = Value + \"A\"");
            source.AppendLine("ELSE");
            source.AppendLine("    SET Value = Value + \"B\"");
            source.AppendLine("END IF");
        }

        BindResult bind = _transpiler.Bind(source.ToString());
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol value = bind.Program!.Variables.Single(variable => variable.Name == "Value");
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.Contains(value, analysis.VariablesWithInexactAssignedValues);
        Assert.AreEqual(branchCount, analysis.MaximumAssignedUtf8ByteLength(value));
        Assert.IsFalse(analysis.AssignedValuesMayContainNul(value));
    }

    [TestMethod]
    public void Branch_analysis_ranges_cover_later_Integer_intermediates_from_every_path()
    {
        BindResult bind = _transpiler.Bind("""
LET Ready = TRUE
LET Source = 0
LET Result = 0
IF Ready = TRUE THEN
    SET Source = 1
ELSE
    SET Source = 2000000000
END IF
SET Result = Source * 2
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        BoundSetStatement resultSet = bind.Program!.Statements
            .OfType<BoundSetStatement>()
            .Last();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);
        AnalyzedIntegerRange range = analysis.GetPossibleIntegerRange(resultSet.Value);

        Assert.AreEqual(2L, range.Minimum);
        Assert.AreEqual(4000000000L, range.Maximum);
    }

    [TestMethod]
    public void Branch_analysis_records_possible_Integer_ranges_used_only_by_print()
    {
        BindResult bind = _transpiler.Bind("""
LET Ready = TRUE
LET Source = 0
IF Ready = TRUE THEN
    SET Source = 1
ELSE
    SET Source = 2000000000
END IF
PRINT {Source * 2}
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        BoundPrintStatement print = bind.Program!.Statements.OfType<BoundPrintStatement>().Single();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);
        AnalyzedIntegerRange range = analysis.GetPossibleIntegerRange(print.Value);

        Assert.AreEqual(2L, range.Minimum);
        Assert.AreEqual(4000000000L, range.Maximum);
        Assert.AreEqual(10, analysis.MaximumExpressionDisplayUtf8ByteLength(print.Value));
        Assert.IsFalse(analysis.ExpressionDisplayMayContainNul(print.Value));
    }

    [TestMethod]
    public void Expression_display_facts_cover_composite_String_values_from_every_branch()
    {
        BindResult bind = _transpiler.Bind("""
LET ChooseFirst = TRUE
LET Name = ""
IF ChooseFirst = TRUE THEN
    SET Name = "A"
ELSE
    SET Name = "B\0C"
END IF
PRINT {Name + "!"}
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        BoundPrintStatement print = bind.Program!.Statements.OfType<BoundPrintStatement>().Single();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);
        AnalyzedExpressionDisplayFacts facts = analysis.GetExpressionDisplayFacts(print.Value);

        Assert.AreEqual(4, facts.MaximumUtf8ByteLength);
        Assert.IsTrue(facts.MayContainNul);
    }

    [TestMethod]
    public void String_display_facts_measure_multibyte_UTF8_and_preserve_NUL_possibility()
    {
        BindResult bind = _transpiler.Bind("""
LET ChooseFirst = TRUE
LET Text = ""
IF ChooseFirst = TRUE THEN
    SET Text = "é"
ELSE
    SET Text = "😀\0"
END IF
PRINT {Text + "界"}
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol text = bind.Program!.Variables.Single(variable => variable.Name == "Text");
        BoundPrintStatement print = bind.Program.Statements.OfType<BoundPrintStatement>().Single();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        // 😀 occupies four UTF-8 bytes, NUL occupies one, and 界 occupies three.
        Assert.AreEqual(5, analysis.MaximumAssignedUtf8ByteLength(text));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(text));
        Assert.AreEqual(8, analysis.MaximumExpressionDisplayUtf8ByteLength(print.Value));
        Assert.IsTrue(analysis.ExpressionDisplayMayContainNul(print.Value));
    }

    [TestMethod]
    public void Independent_multi_value_Integer_operands_use_sound_interval_arithmetic()
    {
        BindResult bind = _transpiler.Bind("""
LET ChooseFirst = TRUE
LET Left = 0
LET Right = 0
LET Sum = 0
LET Difference = 0
LET Product = 0
LET Quotient = 0
IF ChooseFirst = TRUE THEN
    SET Left = -3
ELSE
    SET Left = 7
END IF
IF ChooseFirst = TRUE THEN
    SET Right = 2
ELSE
    SET Right = 5
END IF
SET Sum = Left + Right
SET Difference = Left - Right
SET Product = Left * Right
SET Quotient = Left / Right
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        BoundSetStatement[] calculatedSets = bind.Program!.Statements
            .OfType<BoundSetStatement>()
            .ToArray();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        AnalyzedIntegerRange sum = analysis.GetPossibleIntegerRange(
            calculatedSets.Single(statement => statement.Variable.Name == "Sum").Value);
        AnalyzedIntegerRange difference = analysis.GetPossibleIntegerRange(
            calculatedSets.Single(statement => statement.Variable.Name == "Difference").Value);
        AnalyzedIntegerRange product = analysis.GetPossibleIntegerRange(
            calculatedSets.Single(statement => statement.Variable.Name == "Product").Value);
        AnalyzedIntegerRange quotient = analysis.GetPossibleIntegerRange(
            calculatedSets.Single(statement => statement.Variable.Name == "Quotient").Value);

        Assert.AreEqual(new AnalyzedIntegerRange(-1, 12), sum);
        Assert.AreEqual(new AnalyzedIntegerRange(-8, 5), difference);
        Assert.AreEqual(new AnalyzedIntegerRange(-15, 35), product);
        Assert.AreEqual(new AnalyzedIntegerRange(-1, 3), quotient);
    }

    [TestMethod]
    public void Candidate_cap_retains_late_String_and_Integer_summary_extremes()
    {
        const int clauseCount = 70;
        var source = new StringBuilder();
        source.AppendLine($"LET Selector = {clauseCount}");
        source.AppendLine("LET Text = \"\"");
        source.AppendLine("LET Number = 0");
        for (int index = 1; index <= clauseCount; index++)
        {
            source.Append(index == 1 ? "IF" : "ELSE IF")
                .Append(" Selector = ")
                .Append(index)
                .AppendLine(" THEN");
            source.Append("    SET Text = ");
            source.AppendLine(index == clauseCount ? "\"終😀\\0Longest\"" : $"\"v{index}\"");
            source.Append("    SET Number = ");
            source.AppendLine(index switch
            {
                clauseCount - 1 => "-9223372036854775808",
                clauseCount => "9223372036854775807",
                _ => index.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        source.AppendLine("END IF");
        source.AppendLine("PRINT {Text + \"!\"}");
        source.AppendLine("PRINT {Number + 0}");

        BindResult bind = _transpiler.Bind(source.ToString());
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol text = bind.Program!.Variables.Single(variable => variable.Name == "Text");
        VariableSymbol number = bind.Program.Variables.Single(variable => variable.Name == "Number");
        BoundPrintStatement[] prints = bind.Program.Statements.OfType<BoundPrintStatement>().ToArray();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.Contains(text, analysis.VariablesWithInexactAssignedValues);
        Assert.Contains(number, analysis.VariablesWithInexactAssignedValues);
        Assert.AreEqual(15, analysis.MaximumAssignedUtf8ByteLength(text));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(text));
        Assert.AreEqual(16, analysis.MaximumExpressionDisplayUtf8ByteLength(prints[0].Value));
        Assert.IsTrue(analysis.ExpressionDisplayMayContainNul(prints[0].Value));
        Assert.AreEqual(
            new AnalyzedIntegerRange(long.MinValue, long.MaxValue),
            analysis.GetPossibleIntegerRange(prints[1].Value));
        Assert.AreEqual(20, analysis.MaximumExpressionDisplayUtf8ByteLength(prints[1].Value));
    }

    [TestMethod]
    public void Nested_IF_post_merge_display_facts_cover_every_recursive_path()
    {
        BindResult bind = _transpiler.Bind("""
LET Outer = TRUE
LET Inner = TRUE
LET Text = ""
IF Outer = TRUE THEN
    IF Inner = TRUE THEN
        SET Text = "A"
    ELSE
        SET Text = "漢\0"
    END IF
ELSE
    SET Text = "😀😀"
END IF
PRINT {Text + "!"}
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol text = bind.Program!.Variables.Single(variable => variable.Name == "Text");
        BoundPrintStatement print = bind.Program.Statements.OfType<BoundPrintStatement>().Single();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.IsFalse(analysis.FinalValues[text].IsKnown);
        Assert.AreEqual(8, analysis.MaximumAssignedUtf8ByteLength(text));
        Assert.IsTrue(analysis.AssignedValuesMayContainNul(text));
        Assert.AreEqual(9, analysis.MaximumExpressionDisplayUtf8ByteLength(print.Value));
        Assert.IsTrue(analysis.ExpressionDisplayMayContainNul(print.Value));
    }

    [TestMethod]
    public void No_success_interpolation_keeps_conservative_composite_display_facts()
    {
        BindResult bind = _transpiler.Bind("""
LET ChooseFirst = TRUE
LET Name = ""
LET Zero = 0
IF ChooseFirst = TRUE THEN
    SET Name = "A"
ELSE
    SET Name = "B\0C"
END IF
IF ChooseFirst = TRUE THEN
    PRINT Selected
ELSE
    PRINT $"{1 / Zero}{Name}"
END IF
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        BoundPrintStatement unreachablePrint = bind.Program!.Statements
            .OfType<BoundIfStatement>()
            .Last()
            .ElseStatements
            .OfType<BoundPrintStatement>()
            .Single();
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);
        AnalyzedExpressionDisplayFacts facts =
            analysis.GetExpressionDisplayFacts(unreachablePrint.Value);

        Assert.AreEqual(23, facts.MaximumUtf8ByteLength);
        Assert.IsTrue(facts.MayContainNul);
    }

    [TestMethod]
    public void Assigned_display_widths_include_Integer_and_Boolean_values()
    {
        BindResult bind = _transpiler.Bind("""
LET Score = 85
LET Ready = TRUE
IF Score = 85 THEN
    SET Ready = FALSE
ELSE
    SET Ready = TRUE
END IF
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol score = bind.Program!.Variables.Single(variable => variable.Name == "Score");
        VariableSymbol ready = bind.Program.Variables.Single(variable => variable.Name == "Ready");
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.AreEqual(2, analysis.MaximumAssignedUtf8ByteLength(score));
        Assert.AreEqual(5, analysis.MaximumAssignedUtf8ByteLength(ready));
    }

    [TestMethod]
    public void Unselected_interpolation_with_no_successful_prefix_is_analyzed_without_failure()
    {
        BindResult bind = _transpiler.Bind("""
LET Ready = TRUE
LET Source = 0
LET Zero = 0
IF Ready = TRUE THEN
    SET Source = 1
ELSE
    SET Source = 2
END IF
IF Ready = TRUE THEN
    PRINT Selected
ELSE
    PRINT $"{1 / Zero}{Source}"
END IF
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program!);

        Assert.IsNotNull(analysis);
    }

    [TestMethod]
    [DataRow("FALSE AND 1 / Zero > 0", false)]
    [DataRow("TRUE OR 1 / Zero > 0", true)]
    public void Possible_value_analysis_preserves_short_circuit_results(
        string initializer,
        bool expected)
    {
        BindResult bind = _transpiler.Bind($$"""
LET Zero = 0
LET Result = {{initializer}}
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        VariableSymbol result = bind.Program!.Variables.Single(variable => variable.Name == "Result");
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(bind.Program);

        Assert.HasCount(1, analysis.AssignedValues[result]);
        Assert.AreEqual(expected, analysis.AssignedValues[result][0].BooleanValue);
    }

    [TestMethod]
    public void Legacy_exact_trace_keeps_if_as_one_top_level_step()
    {
        BindResult bind = _transpiler.Bind("""
LET Ready = TRUE
LET Result = ""
IF Ready = TRUE THEN
    SET Result = "selected"
ELSE
    SET Result = "other"
END IF
PRINT {Result}
""");

        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(bind.Program!);
        Assert.HasCount(4, trace.Steps);
        Assert.IsInstanceOfType(trace.Steps[2].Statement, typeof(BoundIfStatement));
        Assert.AreEqual("selected", trace.FinalValues[bind.Program!.Variables[1]].StringValue);
    }

    private static ExpressionSyntax CreateFutureCondition(string shape)
    {
        var span = new TextSpan(0, 1, 1, 1);
        var futureCall = new FutureCallableExpressionSyntax(span);
        var one = new IntegerLiteralExpressionSyntax("1", span);
        var callableComparison = new BinaryExpressionSyntax(
            futureCall,
            new SyntaxToken(SyntaxKind.EqualsToken, "=", null, span),
            one,
            span);
        var knownComparison = new BinaryExpressionSyntax(
            new BooleanLiteralExpressionSyntax(true, span),
            new SyntaxToken(SyntaxKind.EqualsToken, "=", null, span),
            new BooleanLiteralExpressionSyntax(true, span),
            span);

        return shape switch
        {
            "left" => callableComparison,
            "right" => callableComparison with
            {
                Left = one,
                Right = futureCall
            },

            // Call syntax does not exist in v0.6.0. An otherwise unknown
            // expression node is the fail-closed equivalent of a future
            // zero-argument call appearing as an atomic condition.
            "atomic-call" => futureCall,
            "and" => new BinaryExpressionSyntax(
                knownComparison,
                new SyntaxToken(SyntaxKind.AndKeyword, "AND", null, span),
                callableComparison,
                span),
            "or" => new BinaryExpressionSyntax(
                callableComparison,
                new SyntaxToken(SyntaxKind.OrKeyword, "OR", null, span),
                knownComparison,
                span),
            "not" => new UnaryExpressionSyntax(
                new SyntaxToken(SyntaxKind.NotKeyword, "NOT", null, span),
                callableComparison,
                span),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, null)
        };
    }

    private static BindResult BindSyntheticIfCondition(
        ExpressionSyntax condition,
        bool placeInElseIf)
    {
        TextSpan span = condition.Span;
        var clauses = new List<ConditionalClauseSyntax>();
        if (placeInElseIf)
        {
            clauses.Add(new ConditionalClauseSyntax(
                new BinaryExpressionSyntax(
                    new BooleanLiteralExpressionSyntax(false, span),
                    new SyntaxToken(SyntaxKind.EqualsToken, "=", null, span),
                    new BooleanLiteralExpressionSyntax(true, span),
                    span),
                Array.Empty<StatementSyntax>(),
                span));
        }

        clauses.Add(new ConditionalClauseSyntax(
            condition,
            Array.Empty<StatementSyntax>(),
            span));
        var program = new SmileProgramSyntax(
            new StatementSyntax[]
            {
                new IfStatementSyntax(
                    clauses,
                    Array.Empty<StatementSyntax>(),
                    HasElseClause: false,
                    span)
            },
            span);

        Type binderType = typeof(SmileTranspiler).Assembly.GetType(
            "SMILE.Engine.Binder",
            throwOnError: true)!;
        object binder = Activator.CreateInstance(binderType, nonPublic: true)!;
        MethodInfo bindMethod = binderType.GetMethod(
            "Bind",
            BindingFlags.Instance | BindingFlags.Public)!;
        return (BindResult)bindMethod.Invoke(binder, new object[] { program })!;
    }

    private (BoundProgram Program, VariableSymbol Variable) BindResultVariable(
        string source,
        string variableName)
    {
        BindResult bind = _transpiler.Bind(source);
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        return (
            bind.Program!,
            bind.Program!.Variables.Single(variable => variable.Name == variableName));
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string CreateNestedIfSource(
        int depth,
        string innermostBody = "PRINT Reached\n",
        string trailingSource = "")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        var source = new StringBuilder();
        for (int level = 0; level < depth; level++)
        {
            source.AppendLine("IF TRUE = TRUE THEN");
        }

        source.Append(innermostBody);
        if (innermostBody.Length > 0 && innermostBody[^1] is not ('\r' or '\n'))
        {
            source.AppendLine();
        }

        for (int level = 0; level < depth; level++)
        {
            source.AppendLine("END IF");
        }

        source.Append(trailingSource);
        return source.ToString();
    }

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);

    private const string OfficialAcceptanceSource = """
LET Score = 85
LET Ready = TRUE
LET Grade = ""
LET Message = ""

IF Score >= 90 AND Ready = TRUE THEN
    SET Grade = "A"
ELSE IF Score >= 80 AND Ready = TRUE THEN
    SET Grade = "B"
ELSE IF Score >= 70 AND Ready = TRUE THEN
    SET Grade = "C"
ELSE
    SET Grade = "Below C"
END IF

IF Grade = "B" THEN
    SET Message ="
Grade B
Ready for the next lesson.
"
ELSE
    SET Message = "Unexpected grade"
END IF

PRINT Grade={Grade}
PRINT {Message}
""";

    private sealed record FutureCallableExpressionSyntax(TextSpan Span)
        : ExpressionSyntax(Span);
}
