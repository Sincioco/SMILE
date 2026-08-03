using System.Globalization;
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
            return new PrintStatementSyntax(
                new StringLiteralExpressionSyntax(string.Empty, line.Span(afterKeyword, 0)),
                fullSpan,
                IsBlankLine: true);
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
        if (StartsInterpolatedString(line.Text, payloadStart) ||
            SyntaxFacts.IsDoubleQuote(line.Text[payloadStart]))
        {
            var parser = new ExpressionParser(this, line, payloadStart, line.Text.Length);
            value = parser.ParseExpression();
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
            int invalidEnd = FindPotentialIdentifierEnd(line.Text, position);
            AddDiagnostic(
                "SMILE1112",
                "LET requires a valid variable name.",
                line.Span(position, Math.Max(0, invalidEnd - position)));
            return null;
        }

        IdentifierRead name = ReadIdentifier(line, position);
        if (name.End < line.Text.Length &&
            !SyntaxFacts.IsHorizontalWhitespace(line.Text[name.End]) &&
            line.Text[name.End] != '=')
        {
            int invalidEnd = FindPotentialIdentifierEnd(line.Text, position);
            AddDiagnostic(
                "SMILE1112",
                "LET requires a valid variable name.",
                line.Span(position, invalidEnd - position));
            return null;
        }

        if (SyntaxFacts.IsReservedKeyword(name.Text))
        {
            AddDiagnostic(
                "SMILE1115",
                $"'{name.Text}' is a reserved SMILE keyword and cannot be used as a variable name.",
                name.Span);
            return null;
        }

        position = SkipHorizontalWhitespace(line.Text, name.End);
        if (position >= line.Text.Length || line.Text[position] != '=')
        {
            AddDiagnostic("SMILE1113", "LET requires '=' before its initializer.", line.Span(position, 0));
            return null;
        }

        position = SkipHorizontalWhitespace(line.Text, position + 1);
        if (position >= line.Text.Length)
        {
            AddDiagnostic(
                "SMILE1116",
                "LET requires an initializer expression.",
                line.Span(position, 0));
            return null;
        }

        var parser = new ExpressionParser(this, line, position, line.Text.Length);
        ExpressionSyntax initializer = parser.ParseExpression();
        RequireOnlyTrailingWhitespace(line, parser.Position, "SMILE1111", "Unexpected text after LET initializer.");

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
                ExpressionSyntax expression = parser.ParseExpression();
                if (!parser.IsAtEndAfterWhitespace())
                {
                    AddDiagnostic("SMILE1201", "Invalid or unexpected token in expression.", line.Span(parser.Position, Math.Max(0, close - parser.Position)));
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

        if (parts.Count == 1 && parts[0] is InterpolationExpressionPartSyntax expressionPart)
        {
            return expressionPart.Expression;
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
                position = SkipInterpolatedString(line.Text, position, end);
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

    private static int SkipInterpolatedString(string text, int start, int end)
    {
        int position = start + 2;
        while (position < end)
        {
            if (text[position] == '\\')
            {
                position += position + 1 < end ? 2 : 1;
                continue;
            }

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
            if (text[position] == '\\')
            {
                position += position + 1 < end ? 2 : 1;
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

    private static int FindInterpolationClose(string text, int start, int end)
    {
        int position = start;
        while (position < end)
        {
            if (StartsInterpolatedString(text, position))
            {
                position = SkipInterpolatedString(text, position, end);
                continue;
            }

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

    private static int FindPotentialIdentifierEnd(string text, int start)
    {
        int position = start;
        while (position < text.Length &&
            !SyntaxFacts.IsHorizontalWhitespace(text[position]) &&
            text[position] != '=')
        {
            position++;
        }

        return position;
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
        private SyntaxToken? _current;

        public ExpressionParser(Parser owner, SourceLine line, int start, int end)
        {
            _owner = owner;
            _line = line;
            Position = start;
            _end = end;
        }

        public int Position { get; private set; }

        public ExpressionSyntax ParseExpression(int parentPrecedence = 0)
        {
            ExpressionSyntax left;
            SyntaxToken current = Current;
            int unaryPrecedence = SyntaxFacts.GetUnaryOperatorPrecedence(current.Kind);
            if (unaryPrecedence != 0 && unaryPrecedence >= parentPrecedence)
            {
                SyntaxToken operatorToken = NextToken();
                ExpressionSyntax operand = ParseExpression(unaryPrecedence);
                int start = operatorToken.Span.Start - _line.Start;
                int end = operand.Span.Start - _line.Start + operand.Span.Length;
                left = new UnaryExpressionSyntax(operatorToken, operand, _line.Span(start, end - start));
            }
            else
            {
                left = ParsePrimaryExpression();
            }

            while (true)
            {
                current = Current;
                int precedence = SyntaxFacts.GetBinaryOperatorPrecedence(current.Kind);
                if (precedence == 0 || precedence <= parentPrecedence)
                {
                    break;
                }

                SyntaxToken operatorToken = NextToken();
                ExpressionSyntax right = ParseExpression(precedence);
                int start = left.Span.Start - _line.Start;
                int end = right.Span.Start - _line.Start + right.Span.Length;
                left = new BinaryExpressionSyntax(left, operatorToken, right, _line.Span(start, end - start));
            }

            return left;
        }

        public bool IsAtEndAfterWhitespace() =>
            SkipHorizontalWhitespace(_line.Text, Position) >= _end;

        private SyntaxToken Current
        {
            get
            {
                _current ??= ReadToken();
                return _current;
            }
        }

        private SyntaxToken NextToken()
        {
            SyntaxToken token = Current;
            _current = null;
            Position = Math.Max(Position, token.Span.Start - _line.Start + token.Span.Length);
            return token;
        }

        private SyntaxToken ReadToken() =>
            Lexer.LexOne(_line.Text, _line.Start, _line.LineNumber, Position, _end, _owner._diagnostics);

        private ExpressionSyntax ParsePrimaryExpression()
        {
            SyntaxToken token = Current;
            switch (token.Kind)
            {
                case SyntaxKind.OpenParenthesisToken:
                    return ParseParenthesizedExpression();

                case SyntaxKind.InterpolatedStringStartToken:
                    return ParseInterpolatedString();

                case SyntaxKind.StringLiteralToken:
                    NextToken();
                    return new StringLiteralExpressionSyntax((string?)token.Value ?? string.Empty, token.Span);

                case SyntaxKind.IntegerLiteralToken:
                    NextToken();
                    return new IntegerLiteralExpressionSyntax((string?)token.Value ?? token.Text, token.Span);

                case SyntaxKind.TrueKeyword:
                case SyntaxKind.FalseKeyword:
                    NextToken();
                    return new BooleanLiteralExpressionSyntax(token.Kind is SyntaxKind.TrueKeyword, token.Span);

                case SyntaxKind.IdentifierToken:
                    NextToken();
                    return new NameExpressionSyntax(token.Text, token.Span);

                default:
                    _owner.AddDiagnostic("SMILE1201", "Invalid or unexpected token in expression.", token.Span);
                    if (token.Kind is not SyntaxKind.EndOfFileToken)
                    {
                        NextToken();
                    }

                    return new ErrorExpressionSyntax(token.Span);
            }
        }

        private ExpressionSyntax ParseParenthesizedExpression()
        {
            SyntaxToken open = NextToken();
            ExpressionSyntax expression = ParseExpression();
            SyntaxToken close;
            if (Current.Kind is SyntaxKind.CloseParenthesisToken)
            {
                close = NextToken();
            }
            else
            {
                _owner.AddDiagnostic("SMILE1205", "Missing closing parenthesis.", Current.Span);
                close = new SyntaxToken(SyntaxKind.CloseParenthesisToken, string.Empty, null, Current.Span);
            }

            int start = open.Span.Start - _line.Start;
            int end = close.Span.Start - _line.Start + close.Span.Length;
            return new ParenthesizedExpressionSyntax(open, expression, close, _line.Span(start, Math.Max(0, end - start)));
        }

        private ExpressionSyntax ParseInterpolatedString()
        {
            if (!StartsInterpolatedString(_line.Text, Position))
            {
                _owner.AddDiagnostic("SMILE1201", "Invalid or unexpected token in expression.", _line.Span(Position, 0));
                return new ErrorExpressionSyntax(_line.Span(Position, 0));
            }

            int start = Position;
            Position += 2;
            _current = null;
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

                if (current == '\\')
                {
                    if (Position + 1 >= _end)
                    {
                        _owner.AddDiagnostic(
                            "SMILE1209",
                            "String literal ends with an unterminated escape sequence.",
                            _line.Span(Position, 1));
                        Position++;
                        return new InterpolatedStringExpressionSyntax(parts, _line.Span(start, Position - start));
                    }

                    char escape = _line.Text[Position + 1];
                    if (TryAppendEscape(escape, currentText))
                    {
                        Position += 2;
                        continue;
                    }

                    _owner.AddDiagnostic(
                        "SMILE1208",
                        $"Unknown string escape sequence '\\{escape}'.",
                        _line.Span(Position, 2));
                    Position += 2;
                    continue;
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
                        return new ErrorExpressionSyntax(_line.Span(start, Math.Max(0, Position - start)));
                    }

                    if (IsOnlyHorizontalWhitespace(_line.Text, Position + 1, close))
                    {
                        _owner.AddDiagnostic("SMILE1105", "Interpolation expression cannot be empty.", _line.Span(Position, close - Position + 1));
                        return new ErrorExpressionSyntax(_line.Span(start, close - start + 1));
                    }

                    var parser = new ExpressionParser(_owner, _line, Position + 1, close);
                    ExpressionSyntax expression = parser.ParseExpression();
                    if (!parser.IsAtEndAfterWhitespace())
                    {
                        _owner.AddDiagnostic("SMILE1201", "Invalid or unexpected token in expression.", _line.Span(parser.Position, Math.Max(0, close - parser.Position)));
                        return new ErrorExpressionSyntax(_line.Span(start, close - start + 1));
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
                    return new ErrorExpressionSyntax(_line.Span(start, Position - start + 1));
                }

                currentText.Append(current);
                Position++;
            }

            _owner.AddDiagnostic("SMILE1110", "Unterminated interpolated string.", _line.Span(start, Math.Max(0, _end - start)));
            return new ErrorExpressionSyntax(_line.Span(start, Math.Max(0, _end - start)));

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
}

internal sealed class Binder
{
    private static readonly ulong MinIntegerMagnitude = (ulong)long.MaxValue + 1UL;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VariableSymbol, SmileValue> _constantValues = new();
    private readonly List<VariableSymbol> _declaredVariables = new();

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
            new BoundProgram(statements, _declaredVariables.ToArray()),
            _diagnostics);
    }

    private BoundStatement? BindStatement(StatementSyntax statement) =>
        statement switch
        {
            LetStatementSyntax let => BindLetStatement(let),
            PrintStatementSyntax print => new BoundPrintStatement(BindExpression(print.Value), print.IsBlankLine),
            _ => null
        };

    private BoundStatement? BindLetStatement(LetStatementSyntax syntax)
    {
        if (_variables.ContainsKey(syntax.Name))
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1107",
                DiagnosticSeverity.Error,
                $"Variable '{syntax.Name}' is already declared.",
                syntax.NameSpan));
            return null;
        }

        // A declaration is intentionally absent while its initializer binds.
        // That single ordering rule gives us declaration-before-use and makes
        // self-reference naturally become the normal undefined-variable error.
        int diagnosticCountBeforeInitializer = _diagnostics.Count;
        BoundExpression initializer = BindExpression(syntax.Initializer);
        if (initializer.Type is SmileType.Error ||
            _diagnostics.Count != diagnosticCountBeforeInitializer)
        {
            return null;
        }

        if (!BoundConstantEvaluator.TryEvaluate(initializer, _constantValues, out SmileValue constantValue, _diagnostics))
        {
            return null;
        }

        var symbol = new VariableSymbol(syntax.Name, syntax.NameSpan, initializer.Type);
        _variables.Add(syntax.Name, symbol);
        _constantValues.Add(symbol, constantValue);
        _declaredVariables.Add(symbol);
        return new BoundLetStatement(symbol, initializer, constantValue);
    }

    private BoundExpression BindExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ErrorExpressionSyntax => new BoundErrorExpression(),
            StringLiteralExpressionSyntax literal => new BoundStringLiteralExpression(literal.Value),
            IntegerLiteralExpressionSyntax literal => BindIntegerLiteral(literal),
            BooleanLiteralExpressionSyntax literal => new BoundBooleanLiteralExpression(literal.Value),
            NameExpressionSyntax name => BindNameExpression(name),
            UnaryExpressionSyntax unary => BindUnaryExpression(unary),
            BinaryExpressionSyntax binary => BindBinaryExpression(binary),
            ParenthesizedExpressionSyntax parenthesized => BindExpression(parenthesized.Expression),
            ConcatenationExpressionSyntax concatenation => BindBinaryLikeConcatenation(concatenation),
            InterpolatedStringExpressionSyntax interpolated => BindInterpolatedString(interpolated),
            _ => new BoundErrorExpression()
        };

    private BoundExpression BindIntegerLiteral(IntegerLiteralExpressionSyntax syntax)
    {
        if (TryParseIntegerMagnitude(syntax.Text, out ulong magnitude) &&
            magnitude <= long.MaxValue)
        {
            return new BoundIntegerLiteralExpression((long)magnitude);
        }

        _diagnostics.Add(new Diagnostic(
            "SMILE1202",
            DiagnosticSeverity.Error,
            "Integer literal is outside the signed 64-bit range.",
            syntax.Span));
        return new BoundErrorExpression();
    }

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
        return new BoundErrorExpression();
    }

    private BoundExpression BindUnaryExpression(UnaryExpressionSyntax syntax)
    {
        if (syntax.OperatorToken.Kind is SyntaxKind.MinusToken &&
            syntax.Operand is IntegerLiteralExpressionSyntax literal &&
            TryParseIntegerMagnitude(literal.Text, out ulong magnitude) &&
            magnitude == MinIntegerMagnitude)
        {
            return new BoundIntegerLiteralExpression(long.MinValue);
        }

        BoundExpression operand = BindExpression(syntax.Operand);
        if (operand.Type is SmileType.Error)
        {
            return new BoundErrorExpression();
        }

        BoundUnaryOperator? op = BoundUnaryOperator.Bind(syntax.OperatorToken.Kind, operand.Type);
        if (op is null)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1203",
                DiagnosticSeverity.Error,
                $"Unary operator '{syntax.OperatorToken.Text}' is not defined for type '{operand.Type}'.",
                syntax.OperatorToken.Span));
            return new BoundErrorExpression();
        }

        return new BoundUnaryExpression(op, operand, syntax.OperatorToken.Span);
    }

    private BoundExpression BindBinaryExpression(BinaryExpressionSyntax syntax)
    {
        BoundExpression left = BindExpression(syntax.Left);
        BoundExpression right = BindExpression(syntax.Right);
        if (left.Type is SmileType.Error || right.Type is SmileType.Error)
        {
            return new BoundErrorExpression();
        }

        BoundBinaryOperator? op = BoundBinaryOperator.Bind(syntax.OperatorToken.Kind, left.Type, right.Type);
        if (op is null)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1204",
                DiagnosticSeverity.Error,
                $"Binary operator '{syntax.OperatorToken.Text}' is not defined for types '{left.Type}' and '{right.Type}'.",
                syntax.OperatorToken.Span));
            return new BoundErrorExpression();
        }

        return new BoundBinaryExpression(left, op, right, syntax.OperatorToken.Span);
    }

    private BoundExpression BindBinaryLikeConcatenation(ConcatenationExpressionSyntax syntax)
    {
        BoundExpression left = BindExpression(syntax.Left);
        BoundExpression right = BindExpression(syntax.Right);
        BoundBinaryOperator op = BoundBinaryOperator.Bind(SyntaxKind.PlusToken, SmileType.String, SmileType.String)!;
        return new BoundBinaryExpression(left, op, right, syntax.Span);
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

    private static bool TryParseIntegerMagnitude(string text, out ulong magnitude) =>
        ulong.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out magnitude);
}

internal static class SyntaxFacts
{
    public static bool IsHorizontalWhitespace(char value) =>
        value is ' ' or '\t';

    public static bool IsDoubleQuote(char value) =>
        value is '"' or '\u201c' or '\u201d';

    public static bool IsReservedKeyword(string text) =>
        GetKeywordKind(text) is not SyntaxKind.IdentifierToken;

    public static SyntaxKind GetKeywordKind(string text) =>
        text.ToUpperInvariant() switch
        {
            "LET" => SyntaxKind.LetKeyword,
            "PRINT" => SyntaxKind.PrintKeyword,
            "TRUE" => SyntaxKind.TrueKeyword,
            "FALSE" => SyntaxKind.FalseKeyword,
            "NOT" => SyntaxKind.NotKeyword,
            "AND" => SyntaxKind.AndKeyword,
            "OR" => SyntaxKind.OrKeyword,
            _ => SyntaxKind.IdentifierToken
        };

    public static int GetUnaryOperatorPrecedence(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.PlusToken or
            SyntaxKind.MinusToken or
            SyntaxKind.NotKeyword => 7,
            _ => 0
        };

    public static int GetBinaryOperatorPrecedence(SyntaxKind kind) =>
        kind switch
        {
            SyntaxKind.StarToken or
            SyntaxKind.SlashToken => 6,
            SyntaxKind.PlusToken or
            SyntaxKind.MinusToken => 5,
            SyntaxKind.LessToken or
            SyntaxKind.LessOrEqualsToken or
            SyntaxKind.GreaterToken or
            SyntaxKind.GreaterOrEqualsToken => 4,
            SyntaxKind.EqualsToken or
            SyntaxKind.NotEqualsToken => 3,
            SyntaxKind.AndKeyword => 2,
            SyntaxKind.OrKeyword => 1,
            _ => 0
        };

    public static bool IsAsciiLetter(char value) =>
        value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    public static bool IsAsciiUppercaseLetter(char value) =>
        value is >= 'A' and <= 'Z';

    public static bool IsIdentifierStart(char value) =>
        IsAsciiLetter(value) || value == '_';

    public static bool IsIdentifierPart(char value) =>
        IsIdentifierStart(value) || value is >= '0' and <= '9';
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
