using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class LanguageTests
{
    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    [DataRow("PRINT \"Hello\"", "Hello")]
    [DataRow("print \"Hello\"", "Hello")]
    [DataRow("PrInT \"Hello\"", "Hello")]
    [DataRow("PRINT \u201cHello\u201d", "Hello")]
    public void Parser_accepts_print_keyword_and_quote_variants(string source, string expectedText)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsTrue(result.Success);
        var statement = (PrintStatementSyntax)result.Program!.Statements.Single();
        Assert.AreEqual(expectedText, statement.Text);
    }

    [TestMethod]
    [DataRow("PRINT \"One\"\r\nPRINT \"Two\"\r\n", 2)]
    [DataRow("PRINT \"One\"\n\nPRINT \"Two\"", 2)]
    [DataRow("", 0)]
    [DataRow("\r\n\n", 0)]
    public void Parser_accepts_crlf_lf_blank_lines_and_optional_final_newline(
        string source,
        int expectedStatements)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsTrue(result.Success);
        Assert.HasCount(expectedStatements, result.Program!.Statements);
    }

    [TestMethod]
    [DataRow("PRINT", "SMILE1002", 1, 6)]
    [DataRow("PRINT Hello", "SMILE1002", 1, 7)]
    [DataRow("PRINT \"Unclosed", "SMILE1003", 1, 7)]
    [DataRow("PRONT \"Typo\"", "SMILE1001", 1, 1)]
    [DataRow("PRINT \"Hello\" extra", "SMILE1004", 1, 15)]
    [DataRow("@", "SMILE1005", 1, 1)]
    public void Parser_reports_friendly_diagnostics_without_throwing(
        string source,
        string expectedCode,
        int expectedLine,
        int expectedColumn)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(d => d.Code == expectedCode);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(expectedLine, diagnostic.Span.Line);
        Assert.AreEqual(expectedColumn, diagnostic.Span.Column);
    }

    [TestMethod]
    public void Transpile_many_reports_the_same_diagnostics_for_each_target()
    {
        IReadOnlyList<TranspileResult> results = _transpiler.TranspileMany(
            "PRINT Hello",
            TargetLanguageInfo.All);

        Assert.HasCount(TargetLanguageInfo.All.Count, results);
        Assert.IsTrue(results.All(result => !result.Success));
        Assert.IsTrue(results.All(result => result.GeneratedProgram is null));
        Assert.IsTrue(results.All(result => result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1002")));
    }
}
