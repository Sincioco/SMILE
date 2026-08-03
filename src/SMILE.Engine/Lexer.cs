using System.Text;

namespace SMILE.Engine;

public sealed class Lexer
{
    private readonly string _text;
    private readonly int _absoluteStart;
    private readonly int _end;
    private readonly List<Diagnostic> _diagnostics = new();
    private int _currentLineNumber;
    private int _lineStart;
    private int _position;

    public Lexer(string text)
        : this(text, absoluteStart: 0, lineNumber: 1, start: 0, end: text.Length)
    {
    }

    internal Lexer(string text, int absoluteStart, int lineNumber, int start, int end)
    {
        _text = text;
        _absoluteStart = absoluteStart;
        _currentLineNumber = lineNumber;
        _lineStart = 0;
        _position = start;
        _end = end;
    }

    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<SyntaxToken> Lex()
    {
        var tokens = new List<SyntaxToken>();
        while (true)
        {
            SyntaxToken token = LexToken();
            tokens.Add(token);
            if (token.Kind is SyntaxKind.EndOfFileToken)
            {
                return tokens;
            }
        }
    }

    internal SyntaxToken LexToken()
    {
        SkipHorizontalWhitespace();

        if (_position >= _end)
        {
            return Token(SyntaxKind.EndOfFileToken, _position, 0);
        }

        int start = _position;
        char current = _text[_position];
        if (current is '\r' or '\n')
        {
            if (current == '\r' && _position + 1 < _end && _text[_position + 1] == '\n')
            {
                _position += 2;
                SyntaxToken token = Token(SyntaxKind.EndOfLineToken, start, 2);
                MoveToNextLine();
                return token;
            }

            _position++;
            SyntaxToken singleCharacterNewline = Token(SyntaxKind.EndOfLineToken, start, 1);
            MoveToNextLine();
            return singleCharacterNewline;
        }

        if (SyntaxFacts.IsIdentifierStart(current))
        {
            return LexIdentifierOrKeyword();
        }

        if (current is >= '0' and <= '9')
        {
            return LexIntegerLiteral();
        }

        if (SyntaxFacts.IsDoubleQuote(current))
        {
            return LexStringLiteral();
        }

        if (current == '$' &&
            _position + 1 < _end &&
            SyntaxFacts.IsDoubleQuote(_text[_position + 1]))
        {
            _position += 2;
            return Token(SyntaxKind.InterpolatedStringStartToken, start, 2);
        }

        _position++;
        return current switch
        {
            '+' => Token(SyntaxKind.PlusToken, start, 1),
            '-' => Token(SyntaxKind.MinusToken, start, 1),
            '*' => Token(SyntaxKind.StarToken, start, 1),
            '/' => Token(SyntaxKind.SlashToken, start, 1),
            '=' => Token(SyntaxKind.EqualsToken, start, 1),
            '<' when Match('=') => Token(SyntaxKind.LessOrEqualsToken, start, 2),
            '<' when Match('>') => Token(SyntaxKind.NotEqualsToken, start, 2),
            '<' => Token(SyntaxKind.LessToken, start, 1),
            '>' when Match('=') => Token(SyntaxKind.GreaterOrEqualsToken, start, 2),
            '>' => Token(SyntaxKind.GreaterToken, start, 1),
            '(' => Token(SyntaxKind.OpenParenthesisToken, start, 1),
            ')' => Token(SyntaxKind.CloseParenthesisToken, start, 1),
            _ => BadToken(start)
        };
    }

    internal static SyntaxToken LexOne(
        string text,
        int absoluteStart,
        int lineNumber,
        int start,
        int end,
        ICollection<Diagnostic> diagnostics)
    {
        var lexer = new Lexer(text, absoluteStart, lineNumber, start, end);
        SyntaxToken token = lexer.LexToken();
        foreach (Diagnostic diagnostic in lexer.Diagnostics)
        {
            diagnostics.Add(diagnostic);
        }

        return token;
    }

    private SyntaxToken LexIdentifierOrKeyword()
    {
        int start = _position;
        _position++;
        while (_position < _end && SyntaxFacts.IsIdentifierPart(_text[_position]))
        {
            _position++;
        }

        string text = _text[start.._position];
        SyntaxKind kind = SyntaxFacts.GetKeywordKind(text);
        return Token(kind, start, _position - start, kind is SyntaxKind.IdentifierToken ? text : null);
    }

    private SyntaxToken LexIntegerLiteral()
    {
        int start = _position;
        _position++;
        while (_position < _end && _text[_position] is >= '0' and <= '9')
        {
            _position++;
        }

        // The binder deliberately parses the magnitude text later so unary
        // minus can accept -9223372036854775808 without first rejecting the
        // positive token as too large.
        string text = _text[start.._position];
        return Token(SyntaxKind.IntegerLiteralToken, start, _position - start, text);
    }

    private SyntaxToken LexStringLiteral()
    {
        int start = _position;
        _position++;
        var builder = new StringBuilder();

        while (_position < _end)
        {
            char current = _text[_position];
            if (SyntaxFacts.IsDoubleQuote(current))
            {
                _position++;
                return Token(SyntaxKind.StringLiteralToken, start, _position - start, builder.ToString());
            }

            if (current == '\\')
            {
                if (_position + 1 >= _end)
                {
                    AddDiagnostic(
                        "SMILE1209",
                        "String literal ends with an unterminated escape sequence.",
                        Span(_position, 1));
                    _position++;
                    return Token(SyntaxKind.StringLiteralToken, start, _position - start, builder.ToString());
                }

                char escape = _text[_position + 1];
                if (TryAppendEscape(escape, builder))
                {
                    _position += 2;
                    continue;
                }

                AddDiagnostic(
                    "SMILE1208",
                    $"Unknown string escape sequence '\\{escape}'.",
                    Span(_position, 2));
                _position += 2;
                continue;
            }

            builder.Append(current);
            _position++;
        }

        AddDiagnostic(
            "SMILE1003",
            "Unterminated string literal.",
            Span(start, Math.Max(0, _end - start)));
        return Token(SyntaxKind.StringLiteralToken, start, Math.Max(0, _end - start), builder.ToString());
    }

    private static bool TryAppendEscape(char escape, StringBuilder builder)
    {
        switch (escape)
        {
            case '\\':
                builder.Append('\\');
                return true;
            case '"':
                builder.Append('"');
                return true;
            case 'n':
                builder.Append('\n');
                return true;
            case 'r':
                builder.Append('\r');
                return true;
            case 't':
                builder.Append('\t');
                return true;
            case '0':
                builder.Append('\0');
                return true;
            case 'b':
                builder.Append('\b');
                return true;
            case 'f':
                builder.Append('\f');
                return true;
            default:
                return false;
        }
    }

    private void SkipHorizontalWhitespace()
    {
        while (_position < _end && SyntaxFacts.IsHorizontalWhitespace(_text[_position]))
        {
            _position++;
        }
    }

    private bool Match(char expected)
    {
        if (_position >= _end || _text[_position] != expected)
        {
            return false;
        }

        _position++;
        return true;
    }

    private SyntaxToken BadToken(int start)
    {
        AddDiagnostic("SMILE1005", "Invalid or unexpected character.", Span(start, 1));
        return Token(SyntaxKind.BadToken, start, 1, _text[start..(start + 1)]);
    }

    private SyntaxToken Token(SyntaxKind kind, int start, int length, object? value = null) =>
        new(kind, _text[start..Math.Min(_end, start + length)], value, Span(start, length));

    private TextSpan Span(int start, int length) =>
        new(_absoluteStart + start, length, _currentLineNumber, start - _lineStart + 1);

    private void MoveToNextLine()
    {
        _currentLineNumber++;
        _lineStart = _position;
    }

    private void AddDiagnostic(string code, string message, TextSpan span)
    {
        _diagnostics.Add(new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            message,
            span));
    }
}
