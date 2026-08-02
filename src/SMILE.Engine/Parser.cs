using System.Text;

namespace SMILE.Engine;

internal sealed class Parser
{
    private readonly string _source;
    private readonly List<Diagnostic> _diagnostics = new();

    public Parser(string source)
    {
        _source = NormalizeLegacySmartQuotes(source);
    }

    public ParseResult Parse()
    {
        var statements = new List<StatementSyntax>();

        foreach (SourceLine line in SourceLine.Split(_source))
        {
            StatementSyntax? statement = ParseLine(line);
            if (statement is not null)
            {
                statements.Add(statement);
            }
        }

        TextSpan span = statements.Count == 0
            ? new TextSpan(0, 0, 1, 1)
            : new TextSpan(
                statements[0].Span.Start,
                statements[^1].Span.Start + statements[^1].Span.Length - statements[0].Span.Start,
                statements[0].Span.Line,
                statements[0].Span.Column);

        return new ParseResult(new SmileProgramSyntax(statements, span), _diagnostics);
    }

    private StatementSyntax? ParseLine(SourceLine line)
    {
        int first = SkipHorizontalWhitespace(line.Text, 0);
        if (first >= line.Text.Length)
        {
            return null;
        }

        if (!SyntaxFacts.IsIdentifierStart(line.Text[first]))
        {
            AddDiagnostic(
                "SMILE1005",
                "Invalid or unexpected character.",
                line.Span(first, 1));
            return null;
        }

        IdentifierRead keyword = ReadIdentifier(line, first);
        if (keyword.Text.Equals("PRINT", StringComparison.OrdinalIgnoreCase))
        {
            return ParsePrintStatement(line, keyword);
        }

        if (keyword.Text.Equals("LET", StringComparison.OrdinalIgnoreCase))
        {
            return ParseLetStatement(line, keyword);
        }

        AddDiagnostic(
            "SMILE1001",
            "Unknown statement or keyword.",
            keyword.Span);
        return null;
    }

    private StatementSyntax? ParsePrintStatement(SourceLine line, IdentifierRead printKeyword)
    {
        int afterKeyword = printKeyword.End;
        int payloadStart = SkipHorizontalWhitespace(line.Text, afterKeyword);
        TextSpan fullSpan = line.Span(printKeyword.Start, line.Text.Length - printKeyword.Start);

        if (payloadStart >= line.Text.Length)
        {
            // PRINT by itself is an expression too: it means "print the empty
            // string and then the normal PRINT newline."
            return new PrintStatementSyntax(
                new StringLiteralExpressionSyntax(string.Empty, line.Span(afterKeyword, 0)),
                fullSpan);
        }

        if (!SyntaxFacts.IsHorizontalWhitespace(line.Text[afterKeyword]))
        {
            AddDiagnostic(
                "SMILE1101",
                "PRINT requires a space or tab before its payload.",
                line.Span(afterKeyword, 0));
            return null;
        }

        TextSpan? duplicatePrint = FindStandalonePrintKeyword(line, payloadStart, line.Text.Length);
        if (duplicatePrint is not null)
        {
            AddDiagnostic(
                "SMILE1102",
                "Only one PRINT statement is allowed per line.",
                duplicatePrint.Value);
            return null;
        }

        ExpressionSyntax? value;
        if (StartsInterpolatedString(line.Text, payloadStart))
        {
            var parser = new ExpressionParser(this, line, payloadStart, line.Text.Length);
            value = parser.ParseInterpolatedString();
            RequireOnlyTrailingWhitespace(line, parser.Position, "SMILE1111", "Unexpected text after PRINT expression.");
        }
        else if (SyntaxFacts.IsDoubleQuote(line.Text[payloadStart]))
        {
            var parser = new ExpressionParser(this, line, payloadStart, line.Text.Length);
            value = parser.ParseStringExpression();
            RequireOnlyTrailingWhitespace(line, parser.Position, "SMILE1111", "Unexpected text after PRINT expression.");
        }
        else
        {
            value = ParseRawTemplate(line, payloadStart, TrimTrailingHorizontalWhitespace(line.Text));
        }

        return value is null ? null : new PrintStatementSyntax(value, fullSpan);
    }

    private StatementSyntax? ParseLetStatement(SourceLine line, IdentifierRead letKeyword)
    {
        int position = SkipHorizontalWhitespace(line.Text, letKeyword.End);
        if (position >= line.Text.Length || !SyntaxFacts.IsIdentifierStart(line.Text[position]))
        {
            AddDiagnostic("SMILE1112", "LET requires a variable name.", line.Span(position, 0));
            return null;
        }

        IdentifierRead name = ReadIdentifier(line, position);
        position = SkipHorizontalWhitespace(line.Text, name.End);
        if (position >= line.Text.Length || line.Text[position] != '=')
        {
            AddDiagnostic("SMILE1113", "LET requires '=' before its initializer.", line.Span(position, 0));
            return null;
        }

        position = SkipHorizontalWhitespace(line.Text, position + 1);
        var parser = new ExpressionParser(this, line, position, line.Text.Length);
        ExpressionSyntax? initializer = parser.ParseStringExpression();
        RequireOnlyTrailingWhitespace(line, parser.Position, "SMILE1111", "Unexpected text after LET initializer.");

        if (initializer is null)
        {
            return null;
        }

        return new LetStatementSyntax(
            name.Text,
            name.Span,
            initializer,
            line.Span(letKeyword.Start, line.Text.Length - letKeyword.Start));
    }

    private ExpressionSyntax? ParseRawTemplate(SourceLine line, int start, int end)
    {
        var parts = new List<InterpolatedPartSyntax>();
        var currentText = new StringBuilder();
        int currentTextStart = start;
        int position = start;

        while (position < end)
        {
            char current = line.Text[position];
            if (current == '{')
            {
                if (position + 1 < end && line.Text[position + 1] == '{')
                {
                    currentText.Append('{');
                    position += 2;
                    continue;
                }

                FlushText(position);
                int close = FindInterpolationClose(line.Text, position + 1, end);
                if (close < 0)
                {
                    AddDiagnostic("SMILE1103", "Unterminated interpolation expression.", line.Span(position, 1));
                    return null;
                }

                if (IsOnlyHorizontalWhitespace(line.Text, position + 1, close))
                {
                    AddDiagnostic("SMILE1105", "Interpolation expression cannot be empty.", line.Span(position, close - position + 1));
                    return null;
                }

                var parser = new ExpressionParser(this, line, position + 1, close);
                ExpressionSyntax? expression = parser.ParseStringExpression();
                if (expression is null || !parser.IsAtEndAfterWhitespace())
                {
                    if (expression is not null)
                    {
                        AddDiagnostic("SMILE1108", "Invalid string expression.", line.Span(parser.Position, Math.Max(0, close - parser.Position)));
                    }

                    return null;
                }

                parts.Add(new InterpolationExpressionPartSyntax(expression, line.Span(position, close - position + 1)));
                position = close + 1;
                currentTextStart = position;
                continue;
            }

            if (current == '}')
            {
                if (position + 1 < end && line.Text[position + 1] == '}')
                {
                    currentText.Append('}');
                    position += 2;
                    continue;
                }

                AddDiagnostic("SMILE1104", "Unexpected closing brace in template.", line.Span(position, 1));
                return null;
            }

            currentText.Append(current);
            position++;
        }

        FlushText(end);

        if (parts.Count == 1 && parts[0] is InterpolatedTextPartSyntax textPart)
        {
            return new StringLiteralExpressionSyntax(textPart.Text, line.Span(start, end - start));
        }

        return new InterpolatedStringExpressionSyntax(parts, line.Span(start, end - start));

        void FlushText(int flushPosition)
        {
            if (currentText.Length == 0)
            {
                return;
            }

            parts.Add(new InterpolatedTextPartSyntax(
                currentText.ToString(),
                line.Span(currentTextStart, flushPosition - currentTextStart)));
            currentText.Clear();
        }
    }

    private void RequireOnlyTrailingWhitespace(
        SourceLine line,
        int position,
        string diagnosticCode,
        string message)
    {
        int next = SkipHorizontalWhitespace(line.Text, position);
        if (next >= line.Text.Length)
        {
            return;
        }

        if (line.Text[next] == ';')
        {
            AddDiagnostic(
                "SMILE1109",
                "Semicolons cannot separate SMILE statements.",
                line.Span(next, 1));
            return;
        }

        AddDiagnostic(diagnosticCode, message, line.Span(next, line.Text.Length - next));
    }

    private TextSpan? FindStandalonePrintKeyword(SourceLine line, int start, int end)
    {
        int position = start;
        while (position < end)
        {
            char current = line.Text[position];
            if (StartsInterpolatedString(line.Text, position))
            {
                position = SkipInterpolatedString(line.Text, position + 2, end);
                continue;
            }

            if (SyntaxFacts.IsDoubleQuote(current))
            {
                position = SkipQuotedText(line.Text, position + 1, end);
                continue;
            }

            if (current == '{')
            {
                int close = FindInterpolationClose(line.Text, position + 1, end);
                position = close < 0 ? end : close + 1;
                continue;
            }

            if (SyntaxFacts.IsIdentifierStart(current))
            {
                IdentifierRead identifier = ReadIdentifier(line, position);
                if (identifier.Text.Equals("PRINT", StringComparison.OrdinalIgnoreCase))
                {
                    return identifier.Span;
                }

                position = identifier.End;
                continue;
            }

            position++;
        }

        return null;
    }

    private int SkipInterpolatedString(string text, int start, int end)
    {
        int position = start;
        while (position < end)
        {
            if (text[position] == '{')
            {
                int close = FindInterpolationClose(text, position + 1, end);
                position = close < 0 ? end : close + 1;
                continue;
            }

            if (SyntaxFacts.IsDoubleQuote(text[position]))
            {
                return position + 1;
            }

            position++;
        }

        return end;
    }

    private static int SkipQuotedText(string text, int start, int end)
    {
        int position = start;
        while (position < end)
        {
            if (SyntaxFacts.IsDoubleQuote(text[position]))
            {
                return position + 1;
            }

            position++;
        }

        return end;
    }

    private static int FindInterpolationClose(string text, int start, int end)
    {
        int position = start;
        while (position < end)
        {
            if (SyntaxFacts.IsDoubleQuote(text[position]))
            {
                position = SkipQuotedText(text, position + 1, end);
                continue;
            }

            if (text[position] == '}')
            {
                return position;
            }

            position++;
        }

        return -1;
    }

    private static bool StartsInterpolatedString(string text, int position) =>
        position + 1 < text.Length &&
        text[position] == '$' &&
        SyntaxFacts.IsDoubleQuote(text[position + 1]);

    private static int SkipHorizontalWhitespace(string text, int position)
    {
        while (position < text.Length && SyntaxFacts.IsHorizontalWhitespace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static int TrimTrailingHorizontalWhitespace(string text)
    {
        int end = text.Length;
        while (end > 0 && SyntaxFacts.IsHorizontalWhitespace(text[end - 1]))
        {
            end--;
        }

        return end;
    }

    private static bool IsOnlyHorizontalWhitespace(string text, int start, int end)
    {
        for (int position = start; position < end; position++)
        {
            if (!SyntaxFacts.IsHorizontalWhitespace(text[position]))
            {
                return false;
            }
        }

        return true;
    }

    private IdentifierRead ReadIdentifier(SourceLine line, int start)
    {
        int position = start + 1;
        while (position < line.Text.Length && SyntaxFacts.IsIdentifierPart(line.Text[position]))
        {
            position++;
        }

        return new IdentifierRead(
            start,
            position,
            line.Text[start..position],
            line.Span(start, position - start));
    }

    internal void AddDiagnostic(string code, string message, TextSpan span)
    {
        _diagnostics.Add(new Diagnostic(
            code,
            DiagnosticSeverity.Error,
            message,
            span));
    }

    private static string NormalizeLegacySmartQuotes(string text) =>
        text
            .Replace("\u00e2\u20ac\u0153", "\u201c", StringComparison.Ordinal)
            .Replace("\u00e2\u20ac\u009d", "\u201d", StringComparison.Ordinal);

    private readonly record struct IdentifierRead(
        int Start,
        int End,
        string Text,
        TextSpan Span);

    private sealed class ExpressionParser
    {
        private readonly Parser _owner;
        private readonly SourceLine _line;
        private readonly int _end;

        public ExpressionParser(Parser owner, SourceLine line, int start, int end)
        {
            _owner = owner;
            _line = line;
            Position = start;
            _end = end;
        }

        public int Position { get; private set; }

        public ExpressionSyntax? ParseStringExpression()
        {
            ExpressionSyntax? left = ParseStringTerm();
            if (left is null)
            {
                return null;
            }

            while (true)
            {
                Position = SkipHorizontalWhitespace(_line.Text, Position);
                if (Position >= _end || _line.Text[Position] != '+')
                {
                    return left;
                }

                int operatorPosition = Position;
                Position = SkipHorizontalWhitespace(_line.Text, Position + 1);
                ExpressionSyntax? right = ParseStringTerm();
                if (right is null)
                {
                    _owner.AddDiagnostic(
                        "SMILE1108",
                        "Invalid string expression.",
                        _line.Span(operatorPosition, 1));
                    return left;
                }

                int start = left.Span.Start - _line.Start;
                int end = right.Span.Start - _line.Start + right.Span.Length;
                left = new ConcatenationExpressionSyntax(left, right, _line.Span(start, end - start));
            }
        }

        public ExpressionSyntax? ParseInterpolatedString()
        {
            if (!StartsInterpolatedString(_line.Text, Position))
            {
                _owner.AddDiagnostic("SMILE1108", "Invalid string expression.", _line.Span(Position, 0));
                return null;
            }

            int start = Position;
            Position += 2;
            var parts = new List<InterpolatedPartSyntax>();
            var currentText = new StringBuilder();
            int currentTextStart = Position;

            while (Position < _end)
            {
                char current = _line.Text[Position];
                if (SyntaxFacts.IsDoubleQuote(current))
                {
                    FlushText(Position);
                    Position++;
                    return new InterpolatedStringExpressionSyntax(parts, _line.Span(start, Position - start));
                }

                if (current == '{')
                {
                    if (Position + 1 < _end && _line.Text[Position + 1] == '{')
                    {
                        currentText.Append('{');
                        Position += 2;
                        continue;
                    }

                    FlushText(Position);
                    int close = FindInterpolationClose(_line.Text, Position + 1, _end);
                    if (close < 0)
                    {
                        _owner.AddDiagnostic("SMILE1103", "Unterminated interpolation expression.", _line.Span(Position, 1));
                        return null;
                    }

                    if (IsOnlyHorizontalWhitespace(_line.Text, Position + 1, close))
                    {
                        _owner.AddDiagnostic("SMILE1105", "Interpolation expression cannot be empty.", _line.Span(Position, close - Position + 1));
                        return null;
                    }

                    var parser = new ExpressionParser(_owner, _line, Position + 1, close);
                    ExpressionSyntax? expression = parser.ParseStringExpression();
                    if (expression is null || !parser.IsAtEndAfterWhitespace())
                    {
                        if (expression is not null)
                        {
                            _owner.AddDiagnostic("SMILE1108", "Invalid string expression.", _line.Span(parser.Position, Math.Max(0, close - parser.Position)));
                        }

                        return null;
                    }

                    parts.Add(new InterpolationExpressionPartSyntax(expression, _line.Span(Position, close - Position + 1)));
                    Position = close + 1;
                    currentTextStart = Position;
                    continue;
                }

                if (current == '}')
                {
                    if (Position + 1 < _end && _line.Text[Position + 1] == '}')
                    {
                        currentText.Append('}');
                        Position += 2;
                        continue;
                    }

                    _owner.AddDiagnostic("SMILE1104", "Unexpected closing brace in template.", _line.Span(Position, 1));
                    return null;
                }

                currentText.Append(current);
                Position++;
            }

            _owner.AddDiagnostic("SMILE1110", "Unterminated interpolated string.", _line.Span(start, Math.Max(0, _end - start)));
            return null;

            void FlushText(int flushPosition)
            {
                if (currentText.Length == 0)
                {
                    return;
                }

                parts.Add(new InterpolatedTextPartSyntax(
                    currentText.ToString(),
                    _line.Span(currentTextStart, flushPosition - currentTextStart)));
                currentText.Clear();
            }
        }

        public bool IsAtEndAfterWhitespace() =>
            SkipHorizontalWhitespace(_line.Text, Position) >= _end;

        private ExpressionSyntax? ParseStringTerm()
        {
            Position = SkipHorizontalWhitespace(_line.Text, Position);
            if (Position >= _end)
            {
                _owner.AddDiagnostic("SMILE1108", "Invalid string expression.", _line.Span(Position, 0));
                return null;
            }

            if (StartsInterpolatedString(_line.Text, Position))
            {
                return ParseInterpolatedString();
            }

            if (SyntaxFacts.IsDoubleQuote(_line.Text[Position]))
            {
                return ParseStringLiteral();
            }

            if (SyntaxFacts.IsIdentifierStart(_line.Text[Position]))
            {
                int start = Position;
                Position++;
                while (Position < _end && SyntaxFacts.IsIdentifierPart(_line.Text[Position]))
                {
                    Position++;
                }

                return new NameExpressionSyntax(
                    _line.Text[start..Position],
                    _line.Span(start, Position - start));
            }

            _owner.AddDiagnostic("SMILE1108", "Invalid string expression.", _line.Span(Position, 1));
            return null;
        }

        private ExpressionSyntax? ParseStringLiteral()
        {
            int start = Position;
            Position++;
            var builder = new StringBuilder();

            while (Position < _end)
            {
                if (SyntaxFacts.IsDoubleQuote(_line.Text[Position]))
                {
                    Position++;
                    return new StringLiteralExpressionSyntax(
                        builder.ToString(),
                        _line.Span(start, Position - start));
                }

                builder.Append(_line.Text[Position]);
                Position++;
            }

            _owner.AddDiagnostic(
                "SMILE1003",
                "Unterminated string literal.",
                _line.Span(start, Math.Max(0, _end - start)));
            return null;
        }
    }
}

internal sealed class Binder
{
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.OrdinalIgnoreCase);

    public BindResult Bind(SmileProgramSyntax program)
    {
        var statements = new List<BoundStatement>();

        foreach (StatementSyntax statement in program.Statements)
        {
            BoundStatement? bound = BindStatement(statement);
            if (bound is not null)
            {
                statements.Add(bound);
            }
        }

        return new BindResult(
            new BoundProgram(statements, _variables.Values.ToArray()),
            _diagnostics);
    }

    private BoundStatement? BindStatement(StatementSyntax statement) =>
        statement switch
        {
            LetStatementSyntax let => BindLetStatement(let),
            PrintStatementSyntax print => new BoundPrintStatement(BindExpression(print.Value)),
            _ => null
        };

    private BoundStatement BindLetStatement(LetStatementSyntax syntax)
    {
        BoundExpression initializer = BindExpression(syntax.Initializer);
        var symbol = new VariableSymbol(syntax.Name, syntax.NameSpan, SmileType.String);

        if (initializer is not BoundStringLiteralExpression)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1114",
                DiagnosticSeverity.Error,
                "LET currently requires a string literal initializer.",
                syntax.Initializer.Span));
        }

        if (_variables.ContainsKey(syntax.Name))
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1107",
                DiagnosticSeverity.Error,
                $"Variable '{syntax.Name}' is already declared.",
                syntax.NameSpan));
        }
        else
        {
            _variables.Add(syntax.Name, symbol);
        }

        return new BoundLetStatement(symbol, initializer);
    }

    private BoundExpression BindExpression(ExpressionSyntax expression) =>
        expression switch
        {
            StringLiteralExpressionSyntax literal => new BoundStringLiteralExpression(literal.Value),
            NameExpressionSyntax name => BindNameExpression(name),
            ConcatenationExpressionSyntax concatenation => new BoundConcatenationExpression(
                BindExpression(concatenation.Left),
                BindExpression(concatenation.Right)),
            InterpolatedStringExpressionSyntax interpolated => BindInterpolatedString(interpolated),
            _ => new BoundStringLiteralExpression(string.Empty)
        };

    private BoundExpression BindNameExpression(NameExpressionSyntax syntax)
    {
        if (_variables.TryGetValue(syntax.Name, out VariableSymbol? variable))
        {
            return new BoundVariableExpression(variable);
        }

        _diagnostics.Add(new Diagnostic(
            "SMILE1106",
            DiagnosticSeverity.Error,
            $"Undefined variable '{syntax.Name}'.",
            syntax.Span));
        return new BoundStringLiteralExpression(string.Empty);
    }

    private BoundExpression BindInterpolatedString(InterpolatedStringExpressionSyntax syntax)
    {
        var parts = new List<BoundInterpolatedPart>();
        foreach (InterpolatedPartSyntax part in syntax.Parts)
        {
            switch (part)
            {
                case InterpolatedTextPartSyntax text:
                    parts.Add(new BoundInterpolatedTextPart(text.Text));
                    break;

                case InterpolationExpressionPartSyntax expression:
                    parts.Add(new BoundInterpolationExpressionPart(BindExpression(expression.Expression)));
                    break;
            }
        }

        return new BoundInterpolatedStringExpression(parts);
    }
}

internal static class SyntaxFacts
{
    public static bool IsHorizontalWhitespace(char value) =>
        value is ' ' or '\t';

    public static bool IsDoubleQuote(char value) =>
        value is '"' or '\u201c' or '\u201d';

    public static bool IsIdentifierStart(char value) =>
        char.IsLetter(value) || value == '_';

    public static bool IsIdentifierPart(char value) =>
        char.IsLetterOrDigit(value) || value == '_';
}

internal sealed record SourceLine(
    string Text,
    int Start,
    int LineNumber)
{
    public TextSpan Span(int columnOffset, int length) =>
        new(Start + columnOffset, length, LineNumber, columnOffset + 1);

    public static IReadOnlyList<SourceLine> Split(string source)
    {
        var lines = new List<SourceLine>();
        int lineStart = 0;
        int lineNumber = 1;
        int position = 0;

        while (position < source.Length)
        {
            if (source[position] is '\r' or '\n')
            {
                lines.Add(new SourceLine(source[lineStart..position], lineStart, lineNumber));

                if (source[position] == '\r' && position + 1 < source.Length && source[position + 1] == '\n')
                {
                    position += 2;
                }
                else
                {
                    position++;
                }

                lineStart = position;
                lineNumber++;
                continue;
            }

            position++;
        }

        if (lineStart < source.Length)
        {
            lines.Add(new SourceLine(source[lineStart..], lineStart, lineNumber));
        }

        return lines;
    }
}
