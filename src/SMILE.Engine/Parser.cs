using System.Text;

namespace SMILE.Engine;

internal sealed class Lexer
{
    private readonly string _source;
    private readonly List<Diagnostic> _diagnostics = new();
    private int _position;
    private int _line = 1;
    private int _column = 1;

    public Lexer(string source)
    {
        _source = NormalizeLegacySmartQuotes(source);
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public SyntaxToken Lex()
    {
        // The lexer turns raw characters into tokens. It does not decide
        // whether a whole statement is valid; that job belongs to the parser.
        SkipHorizontalWhitespace();

        if (IsAtEnd)
        {
            return new SyntaxToken(
                SyntaxKind.EndOfFileToken,
                string.Empty,
                null,
                new TextSpan(_position, 0, _line, _column),
                HasError: false);
        }

        int start = _position;
        int line = _line;
        int column = _column;

        if (IsNewLineStart())
        {
            string text = ReadNewLine();
            return new SyntaxToken(
                SyntaxKind.NewLineToken,
                text,
                null,
                new TextSpan(start, text.Length, line, column),
                HasError: false);
        }

        if (IsDoubleQuote(Current))
        {
            return ReadStringLiteral(start, line, column);
        }

        if (char.IsLetter(Current))
        {
            while (!IsAtEnd && char.IsLetter(Current))
            {
                Advance();
            }

            string text = _source[start.._position];
            SyntaxKind kind = text.Equals("PRINT", StringComparison.OrdinalIgnoreCase)
                ? SyntaxKind.PrintKeyword
                : SyntaxKind.BadToken;

            return new SyntaxToken(
                kind,
                text,
                null,
                new TextSpan(start, _position - start, line, column),
                HasError: false);
        }

        Advance();
        var span = new TextSpan(start, 1, line, column);
        _diagnostics.Add(new Diagnostic(
            "SMILE1005",
            DiagnosticSeverity.Error,
            "Invalid or unexpected character.",
            span));

        return new SyntaxToken(
            SyntaxKind.BadToken,
            _source[start.._position],
            null,
            span,
            HasError: true);
    }

    private SyntaxToken ReadStringLiteral(int start, int line, int column)
    {
        Advance();
        var builder = new StringBuilder();

        // SMILE v0.1 keeps strings simple: no escape language yet, just text
        // between straight or smart double quotes on one line.
        while (!IsAtEnd && !IsNewLineStart())
        {
            if (IsDoubleQuote(Current))
            {
                Advance();
                return new SyntaxToken(
                    SyntaxKind.StringLiteralToken,
                    _source[start.._position],
                    builder.ToString(),
                    new TextSpan(start, _position - start, line, column),
                    HasError: false);
            }

            builder.Append(Current);
            Advance();
        }

        var span = new TextSpan(start, _position - start, line, column);
        _diagnostics.Add(new Diagnostic(
            "SMILE1003",
            DiagnosticSeverity.Error,
            "Unterminated string literal.",
            span));

        return new SyntaxToken(
            SyntaxKind.BadToken,
            _source[start.._position],
            builder.ToString(),
            span,
            HasError: true);
    }

    private void SkipHorizontalWhitespace()
    {
        while (!IsAtEnd && (Current == ' ' || Current == '\t'))
        {
            Advance();
        }
    }

    private string ReadNewLine()
    {
        if (Current == '\r' && Peek(1) == '\n')
        {
            _position += 2;
            _line++;
            _column = 1;
            return "\r\n";
        }

        char value = Current;
        _position++;
        _line++;
        _column = 1;
        return value.ToString();
    }

    private void Advance()
    {
        _position++;
        _column++;
    }

    private bool IsAtEnd => _position >= _source.Length;

    private char Current => IsAtEnd ? '\0' : _source[_position];

    private char Peek(int offset)
    {
        int index = _position + offset;
        return index >= _source.Length ? '\0' : _source[index];
    }

    private bool IsNewLineStart() => Current is '\r' or '\n';

    private static bool IsDoubleQuote(char value) =>
        value is '"' or '\u201c' or '\u201d';

    private static string NormalizeLegacySmartQuotes(string text) =>
        text
            .Replace("\u00e2\u20ac\u0153", "\u201c", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u009d", "\u201d", StringComparison.Ordinal);
}

internal sealed class Parser
{
    private readonly List<SyntaxToken> _tokens = new();
    private readonly List<Diagnostic> _diagnostics = new();
    private int _position;

    public Parser(string source)
    {
        var lexer = new Lexer(source);
        SyntaxToken token;

        do
        {
            token = lexer.Lex();
            _tokens.Add(token);
        }
        while (token.Kind != SyntaxKind.EndOfFileToken);

        _diagnostics.AddRange(lexer.Diagnostics);
    }

    public ParseResult Parse()
    {
        var statements = new List<StatementSyntax>();

        // A v0.1 SMILE program is just zero or more lines. Blank lines become
        // newline tokens that the parser skips, while PRINT lines become nodes
        // in the syntax tree.
        while (Current.Kind != SyntaxKind.EndOfFileToken)
        {
            if (Current.Kind == SyntaxKind.NewLineToken)
            {
                NextToken();
                continue;
            }

            StatementSyntax? statement = ParseStatement();
            if (statement is not null)
            {
                statements.Add(statement);
            }

            SkipToNextLine();
        }

        TextSpan span = statements.Count == 0
            ? new TextSpan(0, 0, 1, 1)
            : new TextSpan(
                statements[0].Span.Start,
                statements[^1].Span.Start + statements[^1].Span.Length - statements[0].Span.Start,
                statements[0].Span.Line,
                statements[0].Span.Column);

        return new ParseResult(
            new SmileProgramSyntax(statements, span),
            _diagnostics);
    }

    private StatementSyntax? ParseStatement()
    {
        if (Current.Kind != SyntaxKind.PrintKeyword)
        {
            if (!Current.HasError)
            {
                _diagnostics.Add(new Diagnostic(
                    "SMILE1001",
                    DiagnosticSeverity.Error,
                    "Unknown statement or keyword.",
                    Current.Span));
            }

            return null;
        }

        SyntaxToken printKeyword = NextToken();

        // The grammar only has PRINT plus one string literal. That is why the
        // AST can stay tiny: no expression tree is needed until SMILE grows
        // variables or arithmetic.
        if (Current.Kind != SyntaxKind.StringLiteralToken)
        {
            if (!Current.HasError)
            {
                TextSpan span = Current.Kind is SyntaxKind.NewLineToken or SyntaxKind.EndOfFileToken
                    ? new TextSpan(
                        printKeyword.Span.Start + printKeyword.Span.Length,
                        0,
                        printKeyword.Span.Line,
                        printKeyword.Span.Column + printKeyword.Span.Length)
                    : Current.Span;

                _diagnostics.Add(new Diagnostic(
                    "SMILE1002",
                    DiagnosticSeverity.Error,
                    "PRINT requires a quoted string.",
                    span));
            }

            return null;
        }

        if (Current.Span.Start == printKeyword.Span.Start + printKeyword.Span.Length)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1006",
                DiagnosticSeverity.Error,
                "PRINT requires a space or tab before its quoted string.",
                new TextSpan(
                    printKeyword.Span.Start + printKeyword.Span.Length,
                    0,
                    printKeyword.Span.Line,
                    printKeyword.Span.Column + printKeyword.Span.Length)));

            return null;
        }

        SyntaxToken text = NextToken();

        if (Current.Kind is not SyntaxKind.NewLineToken and not SyntaxKind.EndOfFileToken)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1004",
                DiagnosticSeverity.Error,
                "Unexpected text after statement.",
                Current.Span));
        }

        var spanLength = text.Span.Start + text.Span.Length - printKeyword.Span.Start;
        return new PrintStatementSyntax(
            text.Value ?? string.Empty,
            new TextSpan(printKeyword.Span.Start, spanLength, printKeyword.Span.Line, printKeyword.Span.Column));
    }

    private void SkipToNextLine()
    {
        while (Current.Kind is not SyntaxKind.NewLineToken and not SyntaxKind.EndOfFileToken)
        {
            NextToken();
        }

        if (Current.Kind == SyntaxKind.NewLineToken)
        {
            NextToken();
        }
    }

    private SyntaxToken NextToken()
    {
        SyntaxToken current = Current;
        _position++;
        return current;
    }

    private SyntaxToken Current => Peek(0);

    private SyntaxToken Peek(int offset)
    {
        int index = _position + offset;
        return index >= _tokens.Count ? _tokens[^1] : _tokens[index];
    }
}
