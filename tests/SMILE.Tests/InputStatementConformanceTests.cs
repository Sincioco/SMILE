using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class InputStatementConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    [DataRow("INPUT")]
    [DataRow("input")]
    [DataRow("Input")]
    [DataRow("iNpUt")]
    public void Lexer_emits_the_case_insensitive_INPUT_keyword(string spelling)
    {
        var lexer = new Lexer($"{spelling} Name");

        SyntaxToken[] tokens = lexer.Lex().ToArray();

        Assert.HasCount(0, lexer.Diagnostics);
        Assert.AreEqual(SyntaxKind.InputKeyword, tokens[0].Kind);
        Assert.AreEqual(SyntaxKind.IdentifierToken, tokens[1].Kind);
    }

    [TestMethod]
    public void Every_ASCII_case_variant_of_INPUT_uses_the_same_syntax()
    {
        const string canonical = "INPUT";
        for (int mask = 0; mask < 1 << canonical.Length; mask++)
        {
            string spelling = string.Concat(canonical.Select((letter, index) =>
                (mask & 1 << index) == 0
                    ? letter
                    : char.ToLowerInvariant(letter)));
            ParseResult result = _transpiler.Parse($"{spelling} Name");

            Assert.IsTrue(result.Success, spelling + Environment.NewLine + Join(result.Diagnostics));
            Assert.IsInstanceOfType<InputStatementSyntax>(result.Program!.Statements.Single());
        }
    }

    [TestMethod]
    public void Parser_and_binder_build_one_canonical_INPUT_node_with_exact_spans()
    {
        const string source = "LET Name = \"\"\nInPuT\tname   ";

        ParseResult parse = _transpiler.Parse(source);

        Assert.IsTrue(parse.Success, Join(parse.Diagnostics));
        var syntax = (InputStatementSyntax)parse.Program!.Statements[1];
        Assert.AreEqual("name", syntax.Name);
        Assert.AreEqual(2, syntax.NameSpan.Line);
        Assert.AreEqual(7, syntax.NameSpan.Column);
        Assert.AreEqual(4, syntax.NameSpan.Length);
        Assert.AreEqual(2, syntax.Span.Line);
        Assert.AreEqual(1, syntax.Span.Column);
        Assert.AreEqual("InPuT\tname   ".Length, syntax.Span.Length);

        BindResult bind = _transpiler.Bind(source);
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        var declaration = (BoundLetStatement)bind.Program!.Statements[0];
        var input = (BoundInputStatement)bind.Program.Statements[1];
        Assert.AreSame(declaration.Variable, input.Variable);
        Assert.AreEqual(SmileType.String, input.Variable.Type);
    }

    [TestMethod]
    [DataRow("INPUT\"Name\"", "SMILE1501", "INPUT must be followed by a space or tab.", 6, 0)]
    [DataRow("INPUT", "SMILE1502", "INPUT requires a target variable.", 6, 0)]
    [DataRow("INPUT   \t", "SMILE1502", "INPUT requires a target variable.", 10, 0)]
    [DataRow("INPUT 49", "SMILE1503", "INPUT target must be one identifier.", 7, 2)]
    [DataRow("INPUT {Name}", "SMILE1503", "INPUT target must be one identifier.", 7, 6)]
    [DataRow("INPUT \"Name\"", "SMILE1503", "INPUT target must be one identifier.", 7, 6)]
    [DataRow("INPUT Name Extra", "SMILE1504", "Unexpected content follows the INPUT target.", 12, 5)]
    [DataRow("INPUT Name, Other", "SMILE1504", "Unexpected content follows the INPUT target.", 11, 7)]
    [DataRow("INPUT GetName()", "SMILE1504", "Unexpected content follows the INPUT target.", 14, 2)]
    public void Parser_reports_the_dedicated_INPUT_diagnostic(
        string statement,
        string expectedCode,
        string expectedMessage,
        int expectedColumn,
        int expectedLength)
    {
        ParseResult result = _transpiler.Parse(statement);

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(item => item.Code == expectedCode);
        Assert.AreEqual(expectedMessage, diagnostic.Message);
        Assert.AreEqual(1, diagnostic.Span.Line);
        Assert.AreEqual(expectedColumn, diagnostic.Span.Column);
        Assert.AreEqual(expectedLength, diagnostic.Span.Length);
    }

    [TestMethod]
    [DataRow("LET Age = INPUT")]
    [DataRow("LET Age = 0\nIF INPUT Age >= 18 THEN\n    PRINT Adult\nEND IF")]
    [DataRow("LET Name = \"\"\nPRINT {INPUT Name}")]
    public void INPUT_is_rejected_in_every_normative_expression_context(string source)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, "SMILE1201");
    }

    [TestMethod]
    public void INPUTAge_remains_one_unknown_identifier()
    {
        ParseResult result = _transpiler.Parse("INPUTAge");

        Assert.IsFalse(result.Success);
        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SMILE1001", result.Diagnostics[0].Code);
        Assert.AreEqual("INPUTAge".Length, result.Diagnostics[0].Span.Length);
    }

    [TestMethod]
    public void Binder_reports_undefined_INPUT_target_at_the_name_span()
    {
        BindResult result = _transpiler.Bind("LET Name = \"\"\nINPUT Missing");

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(item => item.Code == "SMILE1505");
        Assert.AreEqual("INPUT target variable 'Missing' is undefined.", diagnostic.Message);
        Assert.AreEqual(2, diagnostic.Span.Line);
        Assert.AreEqual(7, diagnostic.Span.Column);
        Assert.AreEqual("Missing".Length, diagnostic.Span.Length);
    }

    [TestMethod]
    [DataRow("INPUT")]
    [DataRow("input")]
    [DataRow("Input")]
    [DataRow("iNpUt")]
    public void INPUT_is_reserved_as_a_variable_name_in_every_casing(string spelling)
    {
        BindResult result = _transpiler.Bind($"LET {spelling} = 1");

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, "SMILE1115");
    }

    [TestMethod]
    public void Contextual_REM_identifier_remains_a_valid_INPUT_target()
    {
        BindResult result = _transpiler.Bind("LET REM = \"\"\nINPUT rem");

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        var declaration = (BoundLetStatement)result.Program!.Statements[0];
        var input = (BoundInputStatement)result.Program.Statements[1];
        Assert.AreSame(declaration.Variable, input.Variable);
    }

    [TestMethod]
    public void INPUT_is_bound_in_every_IF_body_and_nested_IF()
    {
        const string source = """
LET Choice = 1
LET First = ""
LET Second = ""
LET Third = ""
LET Nested = ""
IF Choice = 1 THEN
    INPUT First
ELSE IF Choice = 2 THEN
    INPUT Second
ELSE
    INPUT Third
END IF
IF Choice = 1 THEN
    IF Choice = 1 THEN
        INPUT Nested
    END IF
END IF
""";

        BindResult result = _transpiler.Bind(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.HasCount(4, EnumerateStatements(result.Program!.Statements)
            .OfType<BoundInputStatement>());
    }

    [TestMethod]
    public void Comments_and_blank_lines_remain_ordered_around_INPUT()
    {
        const string source = "LET Age = 0\n\n// Read age.\nINPUT Age\n\nPRINT {Age}";

        ParseResult parse = _transpiler.Parse(source);
        BindResult bind = _transpiler.Bind(source);

        Assert.IsTrue(parse.Success, Join(parse.Diagnostics));
        Assert.IsTrue(bind.Success, Join(bind.Diagnostics));
        CollectionAssert.AreEqual(
            new[]
            {
                typeof(LetStatementSyntax),
                typeof(BlankLineSyntax),
                typeof(FullLineCommentSyntax),
                typeof(InputStatementSyntax),
                typeof(BlankLineSyntax),
                typeof(PrintStatementSyntax)
            },
            parse.Program!.SourceItems.Select(item => item.GetType()).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                typeof(BoundLetStatement),
                typeof(BoundBlankLine),
                typeof(BoundFullLineComment),
                typeof(BoundInputStatement),
                typeof(BoundBlankLine),
                typeof(BoundPrintStatement)
            },
            bind.Program!.SourceItems.Select(item => item.GetType()).ToArray());
    }

    [TestMethod]
    public void INPUT_text_inside_a_SET_Block_String_remains_String_data()
    {
        const string source = "LET Text = \"\"\nSET Text =\"\nINPUT Missing\n\"\nPRINT {Text}";

        BindResult result = _transpiler.Bind(source);

        Assert.IsTrue(result.Success, Join(result.Diagnostics));
        Assert.HasCount(0, EnumerateStatements(result.Program!.Statements)
            .OfType<BoundInputStatement>());
        var set = (BoundSetStatement)result.Program.Statements[1];
        Assert.AreEqual("INPUT Missing", ((BoundStringLiteralExpression)set.Value).Value);
    }

    private static IEnumerable<BoundStatement> EnumerateStatements(
        IReadOnlyList<BoundStatement> statements)
    {
        foreach (BoundStatement statement in statements)
        {
            yield return statement;
            if (statement is not BoundIfStatement conditional)
            {
                continue;
            }

            foreach (BoundConditionalClause clause in conditional.Clauses)
            {
                foreach (BoundStatement nested in EnumerateStatements(clause.Statements))
                {
                    yield return nested;
                }
            }

            foreach (BoundStatement nested in EnumerateStatements(conditional.ElseStatements))
            {
                yield return nested;
            }
        }
    }

    private static void AssertDiagnostic(
        IEnumerable<Diagnostic> diagnostics,
        string code) =>
        Assert.IsTrue(
            diagnostics.Any(diagnostic => diagnostic.Code == code),
            Join(diagnostics));

    private static string Join(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
