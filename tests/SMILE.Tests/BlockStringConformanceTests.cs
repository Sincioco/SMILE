using System.Text;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class BlockStringConformanceTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void LET_and_SET_block_syntax_retain_exact_spans_and_bind_as_ordinary_String_literals()
    {
        const string blockText = "\" \t\r\n  A \t\r\n  \" \t";
        const string source =
            "LET Direct = " + blockText +
            "\r\nLET Assigned = \"\"" +
            "\r\nSET Assigned = " + blockText +
            "\r\nPRINT {Direct}\r\nPRINT {Assigned}";

        ParseResult parse = _transpiler.Parse(source);

        Assert.IsTrue(parse.Success, JoinDiagnostics(parse.Diagnostics));
        Assert.HasCount(5, parse.Program!.Statements);
        var letSyntax = (LetStatementSyntax)parse.Program.Statements[0];
        var setSyntax = (SetStatementSyntax)parse.Program.Statements[2];
        var letBlock = (BlockStringLiteralExpressionSyntax)letSyntax.Initializer;
        var setBlock = (BlockStringLiteralExpressionSyntax)setSyntax.Value;
        int firstBlockStart = source.IndexOf(blockText, StringComparison.Ordinal);
        int secondBlockStart = source.IndexOf(blockText, firstBlockStart + blockText.Length, StringComparison.Ordinal);

        AssertBlockSyntax(letBlock, letSyntax, blockText, "A \t", firstBlockStart, source);
        AssertBlockSyntax(setBlock, setSyntax, blockText, "A \t", secondBlockStart, source);
        Assert.AreEqual(1, letBlock.Span.Line);
        Assert.AreEqual(14, letBlock.Span.Column);
        Assert.AreEqual(5, setBlock.Span.Line);
        Assert.AreEqual(16, setBlock.Span.Column);
        Assert.AreEqual(4, parse.Program.Statements[1].Span.Line);
        Assert.AreEqual(8, parse.Program.Statements[3].Span.Line);

        BindResult bind = _transpiler.Bind(source);

        Assert.IsTrue(bind.Success, JoinDiagnostics(bind.Diagnostics));
        var boundLet = (BoundLetStatement)bind.Program!.Statements[0];
        var boundSet = (BoundSetStatement)bind.Program.Statements[2];
        AssertBoundStringLiteral(boundLet.Initializer, "A \t");
        AssertBoundStringLiteral(boundSet.Value, "A \t");
    }

    [TestMethod]
    public void Full_source_lexer_emits_exact_Block_tokens_for_LET_and_SET_and_resumes_after_each_close()
    {
        const string blockText = "\" \t\r\n  A \t\r\n  \" \t";
        const string source =
            "LET Direct = " + blockText +
            "\r\nLET Assigned = \"\"" +
            "\r\nSET Assigned = " + blockText +
            "\r\nPRINT TRUE";
        var lexer = new Lexer(source);

        SyntaxToken[] tokens = lexer.Lex().ToArray();
        SyntaxToken[] blocks = tokens
            .Where(token => token.Kind is SyntaxKind.BlockStringLiteralToken)
            .ToArray();

        Assert.HasCount(0, lexer.Diagnostics, JoinDiagnostics(lexer.Diagnostics));
        Assert.HasCount(2, blocks);
        CollectionAssert.AreEqual(new[] { blockText, blockText }, blocks.Select(block => block.Text).ToArray());
        CollectionAssert.AreEqual(new[] { "A \t", "A \t" }, blocks.Select(block => block.Value).ToArray());
        CollectionAssert.AreEqual(new[] { 1, 5 }, blocks.Select(block => block.Span.Line).ToArray());
        SyntaxToken print = tokens.Single(token => token.Kind is SyntaxKind.PrintKeyword);
        Assert.AreEqual(8, print.Span.Line);
        Assert.AreEqual(1, print.Span.Column);
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
    [DataRow("\"\n   \n\t\n\"", "   \n\t")]
    [DataRow("\"\nHe said \"Hello\".\n\"", "He said \"Hello\".")]
    [DataRow("\"\n\" Hello\n\"", "\" Hello")]
    [DataRow("\"\n\"\"\"\n\"", "\"\"\"")]
    [DataRow("\"\n` ${ \\\\( \\\\#( )SMILE\"\n\"", "` ${ \\( \\#( )SMILE\"")]
    [DataRow("\"\n\\\"\n\"", "\"")]
    [DataRow("\"\nHello {Name}\n\"", "Hello {Name}")]
    public void LET_and_SET_share_exact_block_normalization(
        string blockSource,
        string expectedValue)
    {
        (BoundStringLiteralExpression letValue, BoundStringLiteralExpression setValue) =
            BindBothBlockValues(blockSource);

        Assert.AreEqual(expectedValue, letValue.Value);
        Assert.AreEqual(expectedValue, setValue.Value);
    }

    [TestMethod]
    public void LET_and_SET_share_all_official_escape_values()
    {
        const string blockSource = "\"\n\\\\|\\\"|\\n|\\r|\\t|\\0|\\b|\\f\n\"";

        (BoundStringLiteralExpression letValue, BoundStringLiteralExpression setValue) =
            BindBothBlockValues(blockSource);

        const string expected = "\\|\"|\n|\r|\t|\0|\b|\f";
        Assert.AreEqual(expected, letValue.Value);
        Assert.AreEqual(expected, setValue.Value);
    }

    [TestMethod]
    public void LET_and_SET_preserve_embedded_NUL_text_after_it_and_exact_output_bytes()
    {
        const string blockSource = "\"\nA\\0B\n\"";
        string source = ProgramForBoth(blockSource) + "\nPRINT {Direct}\nPRINT {Assigned}";

        (BoundStringLiteralExpression letValue, BoundStringLiteralExpression setValue) =
            BindBothBlockValues(blockSource);
        EvaluationResult evaluation = _evaluator.Evaluate(source);

        Assert.AreEqual("A\0B", letValue.Value);
        Assert.AreEqual(letValue.Value, setValue.Value);
        Assert.IsTrue(evaluation.Success, JoinDiagnostics(evaluation.Diagnostics));
        CollectionAssert.AreEqual(
            new byte[] { 0x41, 0x00, 0x42, 0x0A, 0x41, 0x00, 0x42, 0x0A },
            Encoding.UTF8.GetBytes(evaluation.Output));
    }

    [TestMethod]
    public void CRLF_LF_and_standalone_CR_produce_the_same_LET_and_SET_values()
    {
        // Raw string literals preserve the source file's physical line endings. Normalize
        // the seed so Git checkout settings cannot change the three cases under test.
        string lf = """
LET Direct ="
    S
     I
      N
    "
LET Assigned = ""
SET Assigned ="
    S
     I
      N
    "
""".ReplaceLineEndings("\n");
        string crlf = lf.Replace("\n", "\r\n", StringComparison.Ordinal);
        string cr = lf.Replace("\n", "\r", StringComparison.Ordinal);

        (string Let, string Set) lfValues = BindProgramBlockValues(lf);
        (string Let, string Set) crlfValues = BindProgramBlockValues(crlf);
        (string Let, string Set) crValues = BindProgramBlockValues(cr);

        Assert.AreEqual("S\n I\n  N", lfValues.Let);
        Assert.AreEqual(lfValues.Let, lfValues.Set);
        Assert.AreEqual(lfValues, crlfValues);
        Assert.AreEqual(lfValues, crValues);
    }

    [TestMethod]
    [DataRow("\"\nBad\\q\n\"", "SMILE1208")]
    [DataRow("\"\nEnds with \\\nNext\n\"", "SMILE1209")]
    public void LET_and_SET_block_content_share_escape_diagnostics(
        string blockSource,
        string expectedCode)
    {
        string[] sources =
        [
            "LET Value =" + blockSource,
            "LET Value = \"\"\nSET Value =" + blockSource
        ];

        foreach (string source in sources)
        {
            ParseResult result = _transpiler.Parse(source);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(
                result.Diagnostics.Any(diagnostic => diagnostic.Code == expectedCode),
                JoinDiagnostics(result.Diagnostics));
        }
    }

    [TestMethod]
    public void LET_Block_PRINT_uses_no_automatic_leading_or_trailing_newline()
    {
        const string source = """
LET Value ="
First

Third
"
PRINT {Value}
""";

        EvaluationResult result = _evaluator.Evaluate(source);

        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        Assert.AreEqual("First\n\nThird\n", result.Output);
    }

    private (BoundStringLiteralExpression Let, BoundStringLiteralExpression Set)
        BindBothBlockValues(string blockSource) =>
        BindProgramBlockExpressions(ProgramForBoth(blockSource));

    private (string Let, string Set) BindProgramBlockValues(string source)
    {
        (BoundStringLiteralExpression Let, BoundStringLiteralExpression Set) values =
            BindProgramBlockExpressions(source);
        return (values.Let.Value, values.Set.Value);
    }

    private (BoundStringLiteralExpression Let, BoundStringLiteralExpression Set)
        BindProgramBlockExpressions(string source)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        var let = result.Program!.Statements.OfType<BoundLetStatement>().First();
        var set = result.Program.Statements.OfType<BoundSetStatement>().Single();
        Assert.IsInstanceOfType(let.Initializer, typeof(BoundStringLiteralExpression));
        Assert.IsInstanceOfType(set.Value, typeof(BoundStringLiteralExpression));
        return (
            (BoundStringLiteralExpression)let.Initializer,
            (BoundStringLiteralExpression)set.Value);
    }

    private static string ProgramForBoth(string blockSource) =>
        "LET Direct =" + blockSource +
        "\nLET Assigned = \"\"\nSET Assigned =" + blockSource;

    private static void AssertBlockSyntax(
        BlockStringLiteralExpressionSyntax block,
        StatementSyntax statement,
        string expectedText,
        string expectedValue,
        int expectedStart,
        string source)
    {
        Assert.AreEqual(expectedText, source.Substring(block.Span.Start, block.Span.Length));
        Assert.AreEqual(expectedValue, block.Value);
        Assert.AreEqual(expectedStart, block.Span.Start);
        Assert.AreEqual(expectedText.Length, block.Span.Length);
        Assert.AreEqual(
            expectedStart + expectedText.Length,
            statement.Span.Start + statement.Span.Length);
    }

    private static void AssertBoundStringLiteral(BoundExpression expression, string expectedValue)
    {
        Assert.IsInstanceOfType(expression, typeof(BoundStringLiteralExpression));
        var literal = (BoundStringLiteralExpression)expression;
        Assert.AreEqual(expectedValue, literal.Value);
        Assert.AreEqual(SmileType.String, literal.Type);
    }

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
