using System.Reflection;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class SetStatementConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Lexer_emits_SET_keyword_and_one_exact_block_string_token()
    {
        const string blockText = "\" \t\r\n  A \t\r\n  \" \t";
        const string source = "SET Value = " + blockText + "\r\nPRINT TRUE";
        var lexer = new Lexer(source);

        SyntaxToken[] tokens = lexer.Lex().ToArray();

        Assert.HasCount(0, lexer.Diagnostics);
        Assert.AreEqual(SyntaxKind.SetKeyword, tokens[0].Kind);
        SyntaxToken block = tokens.Single(token => token.Kind is SyntaxKind.BlockStringLiteralToken);
        Assert.AreEqual(blockText, block.Text);
        Assert.AreEqual("A \t", block.Value);
        Assert.AreEqual(source.IndexOf(blockText, StringComparison.Ordinal), block.Span.Start);
        Assert.AreEqual(blockText.Length, block.Span.Length);
        Assert.AreEqual(1, block.Span.Line);
        Assert.AreEqual(source.IndexOf(blockText, StringComparison.Ordinal) + 1, block.Span.Column);

        SyntaxToken print = tokens.Single(token => token.Kind is SyntaxKind.PrintKeyword);
        Assert.AreEqual(4, print.Span.Line);
        Assert.AreEqual(1, print.Span.Column);
    }

    [TestMethod]
    public void SET_has_one_canonical_syntax_and_bound_statement()
    {
        const string source = "LET Counter = 0\nSET counter = Counter + 1";

        ParseResult parse = _transpiler.Parse(source);
        Assert.IsTrue(parse.Success, JoinDiagnostics(parse.Diagnostics));
        SmileProgramSyntax syntaxProgram = parse.Program!;
        Assert.HasCount(2, syntaxProgram.Statements);
        var syntax = (SetStatementSyntax)syntaxProgram.Statements[1];
        Assert.AreEqual("counter", syntax.Name);
        Assert.AreEqual(2, syntax.NameSpan.Line);
        Assert.AreEqual(5, syntax.NameSpan.Column);
        Assert.IsInstanceOfType(syntax.Value, typeof(BinaryExpressionSyntax));

        BindResult bind = _transpiler.Bind(source);
        Assert.IsTrue(bind.Success, JoinDiagnostics(bind.Diagnostics));
        BoundProgram boundProgram = bind.Program!;
        var declaration = (BoundLetStatement)boundProgram.Statements[0];
        var assignment = (BoundSetStatement)boundProgram.Statements[1];
        Assert.AreSame(declaration.Variable, assignment.Variable);
        Assert.IsInstanceOfType(assignment.Value, typeof(BoundBinaryExpression));
        Assert.AreEqual(SmileType.Integer, assignment.Value.Type);
    }

    [TestMethod]
    public void Bound_LET_no_longer_owns_a_permanent_current_value()
    {
        PropertyInfo? constantValue = typeof(BoundLetStatement).GetProperty(
            "ConstantValue",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.IsNull(constantValue);
    }

    [TestMethod]
    [DataRow("SET = 1", "SMILE1301")]
    [DataRow("LET Counter = 0\nSET Counter 1", "SMILE1302")]
    [DataRow("LET Counter = 0\nSET Counter =", "SMILE1303")]
    [DataRow("LET Counter = 0\nSET Counter =    ", "SMILE1303")]
    public void Parser_reports_dedicated_ordinary_SET_diagnostics(
        string source,
        string expectedCode)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, expectedCode);
    }

    [TestMethod]
    [DataRow("SET Counter = 1", "SMILE1304")]
    [DataRow("LET Counter = 0\nSET Counter = \"One\"", "SMILE1305")]
    [DataRow("LET Ready = FALSE\nSET Ready = 1", "SMILE1305")]
    [DataRow("LET Name = \"Sin\"\nSET Name = TRUE", "SMILE1305")]
    [DataRow("LET Counter = 0\nSET Counter =\"\nOne\n\"", "SMILE1305")]
    public void Binder_reports_dedicated_SET_target_and_type_diagnostics(
        string source,
        string expectedCode)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, expectedCode);
    }

    [TestMethod]
    [DataRow("SET")]
    [DataRow("set")]
    [DataRow("Set")]
    [DataRow("sEt")]
    public void SET_is_reserved_as_a_variable_name_in_every_casing(string spelling)
    {
        BindResult result = _transpiler.Bind($"LET {spelling} = 1");

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, "SMILE1115");
    }

    [TestMethod]
    [DataRow("LET Name =\"\nS\n\"")]
    [DataRow("PRINT \"\nS\n\"")]
    [DataRow("LET Name = \"\"\nLET Prefix = \"X\"\nSET Name = Prefix + \"\nS\n\"")]
    [DataRow("LET Name = \"\"\nSET Name = (\"\nS\n\")")]
    [DataRow("LET Name = \"Sin\"\nLET Message = \"\"\nSET Message =$\"\nHello {Name}\n\"")]
    public void Block_string_is_rejected_outside_a_complete_SET_value(string source)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, "SMILE1306");
    }

    [TestMethod]
    [DataRow("LET Name = \"\"\nSET Name =\"\nS\n\" + \"!\"")]
    [DataRow("LET Name = \"\"\nSET Name =\"\nS\n\" PRINT {Name}")]
    public void Content_after_a_block_closing_delimiter_reports_SMILE1307(string source)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, "SMILE1307");
    }

    [TestMethod]
    public void Block_opening_quote_that_does_not_end_the_SET_line_reports_SMILE1308()
    {
        const string source = """
LET Name = ""
SET Name =" block
S
"
""";

        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, "SMILE1308");
    }

    [TestMethod]
    public void Unterminated_SET_block_reports_SMILE1003_from_the_opening_quote()
    {
        const string source = "LET Name = \"\"\nSET Name =\"\nS\n I";
        int openingQuote = source.IndexOf("=\"\n", StringComparison.Ordinal) + 1;

        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(item => item.Code == "SMILE1003");
        Assert.AreEqual(openingQuote, diagnostic.Span.Start);
        Assert.AreEqual(source.Length - openingQuote, diagnostic.Span.Length);
        Assert.AreEqual(2, diagnostic.Span.Line);
    }

    [TestMethod]
    public void Evaluator_executes_INTEGER_String_and_Boolean_mutation_in_source_order()
    {
        const string source = """
LET Counter = 0
LET Name = "Sin"
LET Ready = FALSE

PRINT {Counter}
SET counter = Counter + 1
SET NAME = Name + " Cioco"
SET ready = NOT Ready
LET Summary = $"{Name}:{Counter}:{Ready}"

PRINT {Counter}
PRINT {Name}
PRINT {Ready}
PRINT {Summary}
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        Assert.AreEqual("0\n1\nSin Cioco\nTRUE\nSin Cioco:1:TRUE\n", result.Output);
    }

    [TestMethod]
    [DataRow(
        "LET Flag = FALSE\nSET Flag = TRUE\nPRINT {Flag OR (1 / 0 = 0)}",
        "TRUE\n")]
    [DataRow(
        "LET Flag = TRUE\nSET Flag = FALSE\nPRINT {Flag AND (1 / 0 = 0)}",
        "FALSE\n")]
    public void Mutation_aware_short_circuit_skips_unreachable_failures(
        string source,
        string expectedOutput)
    {
        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        Assert.AreEqual(expectedOutput, result.Output);
        Assert.IsFalse(result.Diagnostics.Any(diagnostic => diagnostic.Code is "SMILE1206" or "SMILE1207"));
    }

    [TestMethod]
    [DataRow(
        "LET Flag = TRUE\nSET Flag = FALSE\nPRINT {Flag OR (1 / 0 = 0)}",
        "SMILE1207")]
    [DataRow(
        "LET Flag = FALSE\nSET Flag = TRUE\nPRINT {Flag AND (9223372036854775807 + 1 = 0)}",
        "SMILE1206")]
    public void Mutation_aware_analysis_reports_reachable_failures(
        string source,
        string expectedCode)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        AssertDiagnostic(result.Diagnostics, expectedCode);
    }

    [TestMethod]
    public void Execution_trace_records_before_after_assignment_history_and_mutation()
    {
        BindResult bind = _transpiler.Bind("""
LET Counter = 1
LET Stable = 9
SET Counter = Counter + 1
PRINT {Counter}
""");
        Assert.IsTrue(bind.Success, JoinDiagnostics(bind.Diagnostics));
        BoundProgram program = bind.Program!;
        VariableSymbol counter = program.Variables.Single(variable => variable.Name == "Counter");
        VariableSymbol stable = program.Variables.Single(variable => variable.Name == "Stable");

        BoundProgramExecutionTrace trace = BoundProgramExecutionTrace.Create(program);

        Assert.HasCount(4, trace.Steps);
        BoundStatementExecution declaration = trace.Steps[0];
        Assert.IsFalse(declaration.ValuesBefore.ContainsKey(counter));
        Assert.AreEqual(1L, declaration.Value.IntegerValue);
        Assert.AreEqual(1L, declaration.ValuesAfter[counter].IntegerValue);

        BoundStatementExecution assignment = trace.Steps[2];
        Assert.IsInstanceOfType(assignment.Statement, typeof(BoundSetStatement));
        Assert.AreEqual(1L, assignment.ValuesBefore[counter].IntegerValue);
        Assert.AreEqual(2L, assignment.Value.IntegerValue);
        Assert.AreEqual(2L, assignment.ValuesAfter[counter].IntegerValue);

        BoundStatementExecution print = trace.Steps[3];
        Assert.AreEqual(2L, print.ValuesBefore[counter].IntegerValue);
        Assert.AreEqual(2L, print.Value.IntegerValue);
        Assert.AreEqual(2L, print.ValuesAfter[counter].IntegerValue);

        CollectionAssert.AreEqual(
            new long[] { 1, 2 },
            trace.AssignedValues[counter].Select(value => value.IntegerValue).ToArray());
        CollectionAssert.AreEqual(
            new long[] { 9 },
            trace.AssignedValues[stable].Select(value => value.IntegerValue).ToArray());
        Assert.Contains(counter, trace.MutatedVariables);
        Assert.DoesNotContain(stable, trace.MutatedVariables);
        Assert.AreEqual(2L, trace.FinalValues[counter].IntegerValue);
        Assert.AreEqual(9L, trace.FinalValues[stable].IntegerValue);
    }

    private static void AssertDiagnostic(
        IEnumerable<Diagnostic> diagnostics,
        string expectedCode)
    {
        Assert.IsTrue(
            diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            JoinDiagnostics(diagnostics));
    }

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
