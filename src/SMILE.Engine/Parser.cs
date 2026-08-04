using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class Parser
{
    private readonly string _source;
    private readonly IReadOnlyList<SourceLine> _lines;
    private readonly List<Diagnostic> _diagnostics = new();

    public Parser(string source)
    {
        _source = NormalizeLegacySmartQuotes(source);
        _lines = SourceLine.Split(_source);
    }

    public ParseResult Parse()
    {
        var statements = new List<StatementSyntax>();

        for (int lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
        {
            StatementSyntax? statement = ParseLine(ref lineIndex);
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

    private StatementSyntax? ParseLine(ref int lineIndex)
    {
        SourceLine line = _lines[lineIndex];
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
            return ParsePrintStatement(line, keyword, ref lineIndex);
        }

        if (keyword.Text.Equals("LET", StringComparison.OrdinalIgnoreCase))
        {
            return ParseLetStatement(line, keyword, ref lineIndex);
        }

        if (keyword.Text.Equals("SET", StringComparison.OrdinalIgnoreCase))
        {
            return ParseSetStatement(line, keyword, ref lineIndex);
        }

        AddDiagnostic(
            "SMILE1001",
            "Unknown statement or keyword.",
            keyword.Span);
        return null;
    }

    private StatementSyntax? ParsePrintStatement(
        SourceLine line,
        IdentifierRead printKeyword,
        ref int lineIndex)
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

        if (IsBlockOpening(line, payloadStart))
        {
            SetBlockStringScanResult block = ConsumeBlock(lineIndex, payloadStart);
            lineIndex = block.ClosingLineIndex;
            AddDiagnostic(
                "SMILE1306",
                "A SET Block String Literal is valid only as the complete value of SET.",
                block.Token.Span);
            return null;
        }

        int misplacedPrintBlock = FindMisplacedBlockOpening(line.Text, payloadStart);
        if (misplacedPrintBlock >= 0 && IsBlockOpening(line, misplacedPrintBlock))
        {
            SetBlockStringScanResult block = ConsumeBlock(lineIndex, misplacedPrintBlock);
            lineIndex = block.ClosingLineIndex;
            AddDiagnostic(
                "SMILE1306",
                "A SET Block String Literal is valid only as the complete value of SET.",
                block.Token.Span);
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

    private StatementSyntax? ParseLetStatement(
        SourceLine line,
        IdentifierRead letKeyword,
        ref int lineIndex)
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

        if (IsBlockOpening(line, position))
        {
            SetBlockStringScanResult block = ConsumeBlock(lineIndex, position);
            lineIndex = block.ClosingLineIndex;
            AddDiagnostic(
                "SMILE1306",
                "A SET Block String Literal is valid only as the complete value of SET.",
                block.Token.Span);
            return null;
        }

        int misplacedLetBlock = FindMisplacedBlockOpening(line.Text, position);
        if (misplacedLetBlock >= 0 && IsBlockOpening(line, misplacedLetBlock))
        {
            SetBlockStringScanResult block = ConsumeBlock(lineIndex, misplacedLetBlock);
            lineIndex = block.ClosingLineIndex;
            AddDiagnostic(
                "SMILE1306",
                "A SET Block String Literal is valid only as the complete value of SET.",
                block.Token.Span);
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

    private StatementSyntax? ParseSetStatement(
        SourceLine line,
        IdentifierRead setKeyword,
        ref int lineIndex)
    {
        int position = SkipHorizontalWhitespace(line.Text, setKeyword.End);
        if (position >= line.Text.Length ||
            !SyntaxFacts.IsHorizontalWhitespace(line.Text[setKeyword.End]) ||
            !SyntaxFacts.IsIdentifierStart(line.Text[position]))
        {
            int invalidEnd = FindPotentialIdentifierEnd(line.Text, position);
            AddDiagnostic(
                "SMILE1301",
                "A variable name is required after SET.",
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
                "SMILE1301",
                "A variable name is required after SET.",
                line.Span(position, Math.Max(0, invalidEnd - position)));
            return null;
        }

        position = SkipHorizontalWhitespace(line.Text, name.End);
        if (position >= line.Text.Length || line.Text[position] != '=')
        {
            AddDiagnostic(
                "SMILE1302",
                "SET requires '=' after its target variable.",
                line.Span(position, 0));
            return null;
        }

        position = SkipHorizontalWhitespace(line.Text, position + 1);
        if (position >= line.Text.Length)
        {
            AddDiagnostic(
                "SMILE1303",
                "SET requires a value.",
                line.Span(position, 0));
            return null;
        }

        if (IsBlockOpening(line, position))
        {
            SetBlockStringScanResult block = ConsumeBlock(lineIndex, position);
            lineIndex = block.ClosingLineIndex;
            var value = new BlockStringLiteralExpressionSyntax(
                (string?)block.Token.Value ?? string.Empty,
                block.Token.Span);
            int statementEnd = block.Token.Span.Start + block.Token.Span.Length;
            return new SetStatementSyntax(
                name.Text,
                name.Span,
                value,
                new TextSpan(
                    line.Start + setKeyword.Start,
                    statementEnd - (line.Start + setKeyword.Start),
                    line.LineNumber,
                    setKeyword.Start + 1));
        }

        if (line.Text[position] == '"' &&
            !HasClosingQuoteOnLine(line.Text, position + 1))
        {
            AddDiagnostic(
                "SMILE1308",
                "The opening quote of a SET Block String Literal must end the physical SET line.",
                line.Span(position, 1));
            return null;
        }

        int misplacedBlockOpening = FindMisplacedBlockOpening(line.Text, position);
        if (misplacedBlockOpening >= 0 && IsBlockOpening(line, misplacedBlockOpening))
        {
            SetBlockStringScanResult block = ConsumeBlock(lineIndex, misplacedBlockOpening);
            lineIndex = block.ClosingLineIndex;
            AddDiagnostic(
                "SMILE1306",
                "A SET Block String Literal is valid only as the complete value of SET.",
                block.Token.Span);
            return null;
        }

        var parser = new ExpressionParser(this, line, position, line.Text.Length);
        ExpressionSyntax valueExpression = parser.ParseExpression();
        RequireOnlyTrailingWhitespace(
            line,
            parser.Position,
            "SMILE1111",
            "Unexpected text after SET value.");

        return new SetStatementSyntax(
            name.Text,
            name.Span,
            valueExpression,
            line.Span(setKeyword.Start, line.Text.Length - setKeyword.Start));
    }

    private SetBlockStringScanResult ConsumeBlock(int openingLineIndex, int openingQuoteColumn) =>
        SetBlockStringScanner.Scan(
            _source,
            _lines,
            openingLineIndex,
            openingQuoteColumn,
            _diagnostics);

    private bool IsBlockOpening(SourceLine line, int quoteColumn)
    {
        if (quoteColumn < 0 ||
            quoteColumn >= line.Text.Length ||
            line.Text[quoteColumn] != '"' ||
            line.Start + line.Text.Length >= _source.Length)
        {
            return false;
        }

        return IsOnlyHorizontalWhitespace(line.Text, quoteColumn + 1, line.Text.Length);
    }

    private static int FindMisplacedBlockOpening(string text, int start)
    {
        int end = TrimTrailingHorizontalWhitespace(text);
        if (end <= start || text[end - 1] != '"')
        {
            return -1;
        }

        bool insideString = false;
        bool escaped = false;
        int unmatchedQuote = -1;
        for (int position = start; position < end; position++)
        {
            char current = text[position];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (insideString && current == '\\')
            {
                escaped = true;
                continue;
            }

            if (current != '"')
            {
                continue;
            }

            insideString = !insideString;
            unmatchedQuote = insideString ? position : -1;
        }

        return insideString && unmatchedQuote == end - 1
            ? unmatchedQuote
            : -1;
    }

    private static bool HasClosingQuoteOnLine(string text, int position)
    {
        bool escaped = false;
        while (position < text.Length)
        {
            char current = text[position];
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if (current == '"')
            {
                return true;
            }

            position++;
        }

        return false;
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
                    if (SmileStringEscapes.TryAppend(escape, currentText))
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

}

internal sealed class Binder
{
    private static readonly ulong MinIntegerMagnitude = (ulong)long.MaxValue + 1UL;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VariableSymbol> _declaredVariables = new();
    private readonly BoundProgramExecutionTraceBuilder _execution = new();

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
            SetStatementSyntax set => BindSetStatement(set),
            PrintStatementSyntax print => BindPrintStatement(print),
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

        var symbol = new VariableSymbol(syntax.Name, syntax.NameSpan, initializer.Type);
        var statement = new BoundLetStatement(symbol, initializer);
        if (!_execution.TryAppend(statement, _diagnostics))
        {
            return null;
        }

        _variables.Add(syntax.Name, symbol);
        _declaredVariables.Add(symbol);
        return statement;
    }

    private BoundStatement? BindSetStatement(SetStatementSyntax syntax)
    {
        if (!_variables.TryGetValue(syntax.Name, out VariableSymbol? variable))
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1304",
                DiagnosticSeverity.Error,
                $"SET target variable '{syntax.Name}' is undefined.",
                syntax.NameSpan));
            return null;
        }

        int diagnosticCountBeforeValue = _diagnostics.Count;
        BoundExpression value = BindExpression(syntax.Value);
        if (value.Type is SmileType.Error ||
            _diagnostics.Count != diagnosticCountBeforeValue)
        {
            return null;
        }

        if (value.Type != variable.Type)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1305",
                DiagnosticSeverity.Error,
                $"SET value type '{value.Type}' does not match variable '{syntax.Name}' of type '{variable.Type}'.",
                syntax.Value.Span));
            return null;
        }

        var statement = new BoundSetStatement(variable, value);
        return _execution.TryAppend(statement, _diagnostics)
            ? statement
            : null;
    }

    private BoundStatement? BindPrintStatement(PrintStatementSyntax syntax)
    {
        int diagnosticCountBeforeValue = _diagnostics.Count;
        BoundExpression value = BindExpression(syntax.Value);
        if (value.Type is SmileType.Error ||
            _diagnostics.Count != diagnosticCountBeforeValue)
        {
            return null;
        }

        var statement = new BoundPrintStatement(value, syntax.IsBlankLine);
        return _execution.TryAppend(statement, _diagnostics)
            ? statement
            : null;
    }

    private BoundExpression BindExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ErrorExpressionSyntax => new BoundErrorExpression(),
            StringLiteralExpressionSyntax literal => new BoundStringLiteralExpression(literal.Value),
            BlockStringLiteralExpressionSyntax literal => new BoundStringLiteralExpression(literal.Value),
            IntegerLiteralExpressionSyntax literal => BindIntegerLiteral(literal),
            BooleanLiteralExpressionSyntax literal => new BoundBooleanLiteralExpression(literal.Value),
            NameExpressionSyntax name => BindNameExpression(name),
            UnaryExpressionSyntax unary => BindUnaryExpression(unary),
            BinaryExpressionSyntax binary => BindBinaryExpression(binary),
            ParenthesizedExpressionSyntax parenthesized => BindExpression(parenthesized.Expression),
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
            "SET" => SyntaxKind.SetKeyword,
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
