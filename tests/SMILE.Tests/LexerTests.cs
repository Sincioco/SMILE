using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class LexerTests
{
    [TestMethod]
    public void Lexer_tokenizes_keywords_literals_operators_and_newlines()
    {
        var lexer = new Lexer("LET Age = 49\r\nPRINT TRUE AND NOT FALSE");

        SyntaxToken[] tokens = lexer.Lex().ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                SyntaxKind.LetKeyword,
                SyntaxKind.IdentifierToken,
                SyntaxKind.EqualsToken,
                SyntaxKind.IntegerLiteralToken,
                SyntaxKind.EndOfLineToken,
                SyntaxKind.PrintKeyword,
                SyntaxKind.TrueKeyword,
                SyntaxKind.AndKeyword,
                SyntaxKind.NotKeyword,
                SyntaxKind.FalseKeyword,
                SyntaxKind.EndOfFileToken
            },
            tokens.Select(token => token.Kind).ToArray());
        Assert.AreEqual(2, tokens[5].Span.Line);
        Assert.AreEqual(1, tokens[5].Span.Column);
        Assert.AreEqual("49", tokens[3].Value);
    }

    [TestMethod]
    public void Lexer_decodes_the_official_string_escape_sequences()
    {
        const string source = """
"A\\B\"C\nD\rE\tF\0G\bH\fI"
""";

        SyntaxToken token = new Lexer(source).Lex().First();

        Assert.AreEqual(SyntaxKind.StringLiteralToken, token.Kind);
        Assert.AreEqual("A\\B\"C\nD\rE\tF\0G\bH\fI", token.Value);
    }

    [TestMethod]
    public void Lexer_reports_unknown_string_escape_sequences()
    {
        var lexer = new Lexer("\"Bad\\q\"");

        _ = lexer.Lex();

        Diagnostic diagnostic = lexer.Diagnostics.Single();
        Assert.AreEqual("SMILE1208", diagnostic.Code);
        Assert.AreEqual(1, diagnostic.Span.Line);
        Assert.AreEqual(5, diagnostic.Span.Column);
    }

    [TestMethod]
    public void Lexer_reports_unterminated_string_escape_sequences()
    {
        var lexer = new Lexer("\"Bad\\");

        _ = lexer.Lex();

        Diagnostic diagnostic = lexer.Diagnostics.Single();
        Assert.AreEqual("SMILE1209", diagnostic.Code);
        Assert.AreEqual(1, diagnostic.Span.Line);
        Assert.AreEqual(5, diagnostic.Span.Column);
    }

    [TestMethod]
    public void Lexer_reports_bad_tokens_with_real_line_and_column()
    {
        var lexer = new Lexer("LET A = 1\n@");

        _ = lexer.Lex();

        Diagnostic diagnostic = lexer.Diagnostics.Single();
        Assert.AreEqual("SMILE1005", diagnostic.Code);
        Assert.AreEqual(2, diagnostic.Span.Line);
        Assert.AreEqual(1, diagnostic.Span.Column);
    }
}
