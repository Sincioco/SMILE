using System.Text;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class SetBlockStringConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Block_syntax_retains_exact_text_span_and_binds_to_an_ordinary_String_literal()
    {
        const string blockText = "\" \t\r\n  A \t\r\n  \" \t";
        const string source = "LET Value = \"\"\r\nSET Value = " + blockText + "\r\nPRINT {Value}";

        ParseResult parse = _transpiler.Parse(source);
        Assert.IsTrue(parse.Success, JoinDiagnostics(parse.Diagnostics));
        var setSyntax = (SetStatementSyntax)parse.Program!.Statements[1];
        var blockSyntax = (BlockStringLiteralExpressionSyntax)setSyntax.Value;
        Assert.AreEqual(blockText, source.Substring(blockSyntax.Span.Start, blockSyntax.Span.Length));
        Assert.AreEqual("A \t", blockSyntax.Value);
        Assert.AreEqual(2, blockSyntax.Span.Line);
        Assert.AreEqual(source.IndexOf(blockText, StringComparison.Ordinal) - source.LastIndexOf('\n', source.IndexOf(blockText, StringComparison.Ordinal)) , blockSyntax.Span.Column);
        Assert.AreEqual(
            source.IndexOf(blockText, StringComparison.Ordinal) + blockText.Length,
            setSyntax.Span.Start + setSyntax.Span.Length);

        BindResult bind = _transpiler.Bind(source);
        Assert.IsTrue(bind.Success, JoinDiagnostics(bind.Diagnostics));
        var boundSet = (BoundSetStatement)bind.Program!.Statements[1];
        var boundLiteral = (BoundStringLiteralExpression)boundSet.Value;
        Assert.AreEqual("A \t", boundLiteral.Value);
        Assert.AreEqual(SmileType.String, boundLiteral.Type);
    }

    [TestMethod]
    [DataRow("\"\nS\n I\n  N\n\"", "S\n I\n  N")]
    [DataRow("\" \t\nS\n\"", "S")]
    [DataRow("\"\n    S\n     I\n      N\n    \"", "S\n I\n  N")]
    [DataRow("\"\n  Left\n    Right\n    \"", "  Left\nRight")]
    [DataRow("\"\n\t\tS\n\t\t\tI\n\t\t\"", "S\n\tI")]
    [DataRow("\"\nFirst\n\nThird\n\"", "First\n\nThird")]
    [DataRow("\"\n\"", "")]
    [DataRow("\"\n\nHello\n\"", "\nHello")]
    [DataRow("\"\nHello\n\"", "Hello")]
    [DataRow("\"\nHello\n\n\"", "Hello\n")]
    [DataRow("\"\nHello\n\n\n\"", "Hello\n\n")]
    [DataRow("\"\nHello \t\nWorld\n\"", "Hello \t\nWorld")]
    [DataRow("\"\nHe said \"Hello\".\n\"", "He said \"Hello\".")]
    [DataRow("\"\n\" Hello\n\"", "\" Hello")]
    [DataRow("\"\n\\\"\n\"", "\"")]
    [DataRow("\"\nHello {Name}\n\"", "Hello {Name}")]
    public void Block_normalization_preserves_exact_logical_content(
        string blockSource,
        string expectedValue)
    {
        BoundStringLiteralExpression value = BindBlockValue(blockSource);

        Assert.AreEqual(expectedValue, value.Value);
    }

    [TestMethod]
    public void All_official_escapes_use_the_ordinary_String_values()
    {
        const string blockSource = "\"\n\\\\|\\\"|\\n|\\r|\\t|\\0|\\b|\\f\n\"";

        BoundStringLiteralExpression value = BindBlockValue(blockSource);

        Assert.AreEqual("\\|\"|\n|\r|\t|\0|\b|\f", value.Value);
    }

    [TestMethod]
    public void Embedded_NUL_is_preserved_as_an_exact_String_value_and_output_byte()
    {
        const string blockSource = "\"\nA\\0B\n\"";
        const string source = "LET Value = \"\"\nSET Value =" + blockSource + "\nPRINT {Value}";

        BoundStringLiteralExpression value = BindBlockValue(blockSource);
        EvaluationResult evaluation = _evaluator.Evaluate(source);

        Assert.AreEqual("A\0B", value.Value);
        Assert.IsTrue(evaluation.Success, JoinDiagnostics(evaluation.Diagnostics));
        CollectionAssert.AreEqual(
            new byte[] { 0x41, 0x00, 0x42, 0x0A },
            Encoding.UTF8.GetBytes(evaluation.Output));
    }

    [TestMethod]
    public void CRLF_LF_and_standalone_CR_produce_the_same_bound_value()
    {
        const string lf = """
LET Value = ""
SET Value ="
    S
     I
      N
    "
""";
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        string cr = lf.Replace("\n", "\r", StringComparison.Ordinal);

        string lfValue = BindProgramBlockValue(lf).Value;
        string crlfValue = BindProgramBlockValue(crlf).Value;
        string crValue = BindProgramBlockValue(cr).Value;

        Assert.AreEqual("S\n I\n  N", lfValue);
        Assert.AreEqual(lfValue, crlfValue);
        Assert.AreEqual(lfValue, crValue);
    }

    [TestMethod]
    [DataRow("\"\nBad\\q\n\"", "SMILE1208")]
    [DataRow("\"\nEnds with \\\nNext\n\"", "SMILE1209")]
    public void Block_content_keeps_dedicated_escape_diagnostics(
        string blockSource,
        string expectedCode)
    {
        string source = "LET Value = \"\"\nSET Value =" + blockSource;

        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(
            result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
            JoinDiagnostics(result.Diagnostics));
    }

    [TestMethod]
    public void Block_PRINT_uses_no_automatic_leading_or_trailing_newline()
    {
        const string source = """
LET Value = ""
SET Value ="
First

Third
"
PRINT {Value}
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        Assert.AreEqual("First\n\nThird\n", result.Output);
    }

    private BoundStringLiteralExpression BindBlockValue(string blockSource) =>
        BindProgramBlockValue("LET Value = \"\"\nSET Value =" + blockSource);

    private BoundStringLiteralExpression BindProgramBlockValue(string source)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        var set = result.Program!.Statements.OfType<BoundSetStatement>().Single();
        Assert.IsInstanceOfType(set.Value, typeof(BoundStringLiteralExpression));
        return (BoundStringLiteralExpression)set.Value;
    }

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
