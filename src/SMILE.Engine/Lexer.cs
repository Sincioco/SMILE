using System.Text;

namespace SMILE.Engine;

public sealed class Lexer
{
    private readonly string _text;
    private readonly int _absoluteStart;
    private readonly int _end;
    private readonly bool _isFullSource;
    private readonly List<Diagnostic> _diagnostics = new();
    private int _currentLineNumber;
    private int _lineStart;
    private int _position;

    public Lexer(string text)
        : this(
            text,
            absoluteStart: 0,
            lineNumber: 1,
            start: 0,
            end: text.Length,
            isFullSource: true)
    {
    }

    internal Lexer(
        string text,
        int absoluteStart,
        int lineNumber,
        int start,
        int end,
        bool isFullSource = false)
    {
        _text = text;
        _absoluteStart = absoluteStart;
        _isFullSource = isFullSource;
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
        bool atPhysicalLineStart = _position == _lineStart;
        SkipHorizontalWhitespace();

        if (_position >= _end)
        {
            return Token(SyntaxKind.EndOfFileToken, _position, 0);
        }

        // Only the first token request for a physical line may classify a
        // comment. Later marker text remains ordinary inline source.
        if (_isFullSource &&
            atPhysicalLineStart &&
            TryLexFullLineComment(out SyntaxToken fullLineComment))
        {
            return fullLineComment;
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

        if (current == '"' && TryLexBlockStringLiteral(out SyntaxToken blockString))
        {
            return blockString;
        }

        if (SyntaxFacts.IsDoubleQuote(current))
        {
            return LexStringLiteral();
        }

        if (current == '$' &&
            _position + 1 < _end &&
            SyntaxFacts.IsDoubleQuote(_text[_position + 1]))
        {
            if (_isFullSource)
            {
                int lineEnd = _position + 2;
                while (lineEnd < _end && _text[lineEnd] is not ('\r' or '\n'))
                {
                    lineEnd++;
                }

                _position = InterpolatedStringScanner.Skip(_text, start, lineEnd);
                return Token(
                    SyntaxKind.InterpolatedStringStartToken,
                    start,
                    _position - start);
            }

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
                if (SmileStringEscapes.TryAppend(escape, builder))
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

    private bool TryLexFullLineComment(out SyntaxToken token)
    {
        token = null!;

        int lineEnd = _position;
        while (lineEnd < _end && _text[lineEnd] is not ('\r' or '\n'))
        {
            lineEnd++;
        }

        string physicalLine = _text[_lineStart..lineEnd];
        int relativeFirst = _position - _lineStart;
        if (!FullLineCommentFacts.TryClassify(
                physicalLine,
                relativeFirst,
                out FullLineCommentMarker marker,
                out int payloadStart))
        {
            return false;
        }

        int tokenStart = _lineStart;
        int tokenLength = lineEnd - tokenStart;
        _position = lineEnd;
        token = Token(
            SyntaxKind.FullLineCommentToken,
            tokenStart,
            tokenLength,
            new FullLineCommentTokenValue(marker, physicalLine[payloadStart..]));
        return true;
    }

    private bool TryLexBlockStringLiteral(out SyntaxToken token)
    {
        token = null!;

        // LexOne is intentionally bounded to one expression line. The public
        // full-source lexer can surface the dedicated block token, while the
        // indexed statement parser remains responsible for SET-only placement.
        if (!_isFullSource)
        {
            return false;
        }

        int afterQuote = _position + 1;
        while (afterQuote < _end && SyntaxFacts.IsHorizontalWhitespace(_text[afterQuote]))
        {
            afterQuote++;
        }

        if (afterQuote >= _end || _text[afterQuote] is not ('\r' or '\n'))
        {
            return false;
        }

        IReadOnlyList<SourceLine> lines = SourceLine.Split(_text);
        int lineIndex = -1;
        for (int index = 0; index < lines.Count; index++)
        {
            SourceLine candidate = lines[index];
            if (candidate.Start <= _position &&
                _position <= candidate.Start + candidate.Text.Length)
            {
                lineIndex = index;
                break;
            }
        }
        if (lineIndex < 0)
        {
            return false;
        }

        SourceLine line = lines[lineIndex];
        var blockDiagnostics = new List<Diagnostic>();
        SetBlockStringScanResult result = SetBlockStringScanner.Scan(
            _text,
            lines,
            lineIndex,
            _position - line.Start,
            blockDiagnostics);
        _diagnostics.AddRange(blockDiagnostics);

        SourceLine closingLine = lines[result.ClosingLineIndex];
        _position = result.Token.Span.Start + result.Token.Span.Length;
        _currentLineNumber = closingLine.LineNumber;
        _lineStart = closingLine.Start;
        token = result.Token;
        return true;
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

// Ordinary, interpolated, and SET block Strings all use this one escape table.
// Keeping the mapping at the lexical boundary prevents the special block
// source form from acquiring subtly different String semantics.
internal static class SmileStringEscapes
{
    public static bool TryAppend(char escape, StringBuilder builder)
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
}

internal sealed record SetBlockStringScanResult(
    SyntaxToken Token,
    int ClosingLineIndex);

// A block is scanned once, linearly, before expression binding. The resulting
// token retains its exact source text/span but carries the same normalized
// String value used by an ordinary String literal.
internal static class SetBlockStringScanner
{
    public static SetBlockStringScanResult Scan(
        string source,
        IReadOnlyList<SourceLine> lines,
        int openingLineIndex,
        int openingQuoteColumn,
        ICollection<Diagnostic> diagnostics)
    {
        SourceLine openingLine = lines[openingLineIndex];
        int tokenStart = openingLine.Start + openingQuoteColumn;
        int malformedClosingLineIndex = -1;
        int malformedSuffixStart = -1;

        for (int lineIndex = openingLineIndex + 1; lineIndex < lines.Count; lineIndex++)
        {
            SourceLine line = lines[lineIndex];
            int first = SkipHorizontalWhitespace(line.Text, 0);
            if (first >= line.Text.Length || line.Text[first] != '"')
            {
                continue;
            }

            int afterQuote = SkipHorizontalWhitespace(line.Text, first + 1);
            if (afterQuote < line.Text.Length &&
                first + 1 < line.Text.Length &&
                !SyntaxFacts.IsHorizontalWhitespace(line.Text[first + 1]))
            {
                // A content line may naturally begin with a quote, such as
                // "Hello". Only whitespace after the delimiter-looking quote
                // makes the line an attempted closing delimiter with a suffix.
                continue;
            }

            if (afterQuote < line.Text.Length)
            {
                // Only an exact whitespace-only quote line closes a valid
                // block. Remember a delimiter-looking line with a suffix, but
                // keep scanning: if a later exact delimiter exists, this line
                // is ordinary quote-leading content. If no exact close exists,
                // the remembered candidate gives the precise SMILE1307 that a
                // concatenation or same-line statement suffix requires.
                if (malformedClosingLineIndex < 0)
                {
                    malformedClosingLineIndex = lineIndex;
                    malformedSuffixStart = afterQuote;
                }

                continue;
            }

            return Complete(lineIndex, first);
        }

        if (malformedClosingLineIndex >= 0)
        {
            SourceLine malformedLine = lines[malformedClosingLineIndex];
            diagnostics.Add(new Diagnostic(
                "SMILE1307",
                DiagnosticSeverity.Error,
                "Unexpected content follows the closing SET Block String delimiter.",
                malformedLine.Span(
                    malformedSuffixStart,
                    malformedLine.Text.Length - malformedSuffixStart)));
            return Complete(
                malformedClosingLineIndex,
                SkipHorizontalWhitespace(malformedLine.Text, 0));
        }

        var unterminatedSpan = new TextSpan(
            tokenStart,
            Math.Max(0, source.Length - tokenStart),
            openingLine.LineNumber,
            openingQuoteColumn + 1);
        diagnostics.Add(new Diagnostic(
            "SMILE1003",
            DiagnosticSeverity.Error,
            "Unterminated SET Block String literal.",
            unterminatedSpan));
        return new SetBlockStringScanResult(
            new SyntaxToken(
                SyntaxKind.BlockStringLiteralToken,
                source[tokenStart..],
                string.Empty,
                unterminatedSpan),
            Math.Max(openingLineIndex, lines.Count - 1));

        SetBlockStringScanResult Complete(int closingLineIndex, int quoteColumn)
        {
            SourceLine closingLine = lines[closingLineIndex];
            string margin = closingLine.Text[..quoteColumn];
            string value = NormalizeContent(
                lines,
                openingLineIndex + 1,
                closingLineIndex,
                margin,
                diagnostics);
            int tokenEnd = closingLine.Start + closingLine.Text.Length;
            var span = new TextSpan(
                tokenStart,
                tokenEnd - tokenStart,
                openingLine.LineNumber,
                openingQuoteColumn + 1);
            return new SetBlockStringScanResult(
                new SyntaxToken(
                    SyntaxKind.BlockStringLiteralToken,
                    source[tokenStart..tokenEnd],
                    value,
                    span),
                closingLineIndex);
        }
    }

    private static string NormalizeContent(
        IReadOnlyList<SourceLine> lines,
        int contentStart,
        int contentEnd,
        string margin,
        ICollection<Diagnostic> diagnostics)
    {
        var value = new StringBuilder();
        for (int lineIndex = contentStart; lineIndex < contentEnd; lineIndex++)
        {
            SourceLine line = lines[lineIndex];
            int dataStart = line.Text.StartsWith(margin, StringComparison.Ordinal)
                ? margin.Length
                : 0;
            AppendDecodedLine(value, line, dataStart, diagnostics);
            if (lineIndex + 1 < contentEnd)
            {
                value.Append('\n');
            }
        }

        return value.ToString();
    }

    private static void AppendDecodedLine(
        StringBuilder value,
        SourceLine line,
        int start,
        ICollection<Diagnostic> diagnostics)
    {
        int position = start;
        while (position < line.Text.Length)
        {
            char current = line.Text[position];
            if (current != '\\')
            {
                value.Append(current);
                position++;
                continue;
            }

            if (position + 1 >= line.Text.Length)
            {
                diagnostics.Add(new Diagnostic(
                    "SMILE1209",
                    DiagnosticSeverity.Error,
                    "String literal ends with an unterminated escape sequence.",
                    line.Span(position, 1)));
                position++;
                continue;
            }

            char escape = line.Text[position + 1];
            if (SmileStringEscapes.TryAppend(escape, value))
            {
                position += 2;
                continue;
            }

            diagnostics.Add(new Diagnostic(
                "SMILE1208",
                DiagnosticSeverity.Error,
                $"Unknown string escape sequence '\\{escape}'.",
                line.Span(position, 2)));
            position += 2;
        }
    }

    private static int SkipHorizontalWhitespace(string text, int position)
    {
        while (position < text.Length && SyntaxFacts.IsHorizontalWhitespace(text[position]))
        {
            position++;
        }

        return position;
    }
}
