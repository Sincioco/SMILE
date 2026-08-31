namespace SMILE.Engine;

public sealed record SmileFormatResult(
    string Source,
    string FormattedSource,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);

    public bool NeedsFormatting =>
        Success &&
        !string.Equals(Normalize(Source), FormattedSource, StringComparison.Ordinal);

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}

/// <summary>
/// Formats valid Core BASIC 2.1 source by using the parsed block structure to
/// determine indentation and logical paragraph boundaries. It changes only
/// whitespace, preserves learner tokens/comments/Text, and refuses invalid
/// input rather than guessing at a repair.
/// </summary>
public static class SmileSourceFormatter
{
    public const int MaximumLineLength = 100;

    public static SmileFormatResult Format(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var transpiler = new SmileTranspiler();
        ParseResult parsed = transpiler.Parse(source);
        if (!parsed.Success || parsed.Program is null)
        {
            return new SmileFormatResult(source, source, parsed.Diagnostics);
        }

        BindResult bound = transpiler.Bind(source);
        if (!bound.Success)
        {
            return new SmileFormatResult(source, source, bound.Diagnostics);
        }

        string normalized = NormalizeLineEndings(source);
        string wrapped = WrapLongCalls(normalized, parsed.Program);
        ParseResult workingParse = string.Equals(wrapped, normalized, StringComparison.Ordinal)
            ? parsed
            : transpiler.Parse(wrapped);
        if (!workingParse.Success || workingParse.Program is null)
        {
            return new SmileFormatResult(source, source, workingParse.Diagnostics);
        }

        var layout = new SourceLayout(wrapped, workingParse.Program);
        string formatted = layout.Render();

        ParseResult formattedParse = transpiler.Parse(formatted);
        BindResult formattedBind = transpiler.Bind(formatted);
        if (!formattedParse.Success || formattedParse.Program is null || !formattedBind.Success ||
            !HasSameProtectedContent(parsed.Program, formattedParse.Program, normalized, formatted))
        {
            var diagnostic = new Diagnostic(
                "SMILEF001",
                DiagnosticSeverity.Error,
                "Formatting could not be proven safe; the source was left unchanged.",
                new TextSpan(0, 0, 1, 1));
            return new SmileFormatResult(
                source,
                source,
                formattedParse.Diagnostics.Concat(formattedBind.Diagnostics).Append(diagnostic).ToArray());
        }

        return new SmileFormatResult(source, formatted, Array.Empty<Diagnostic>());
    }

    public static SmileFormatResult Check(string source) => Format(source);

    private static bool HasSameProtectedContent(
        SmileProgramSyntax before,
        SmileProgramSyntax after,
        string beforeSource,
        string afterSource) =>
        EnumerateStringLiterals(before.SourceItems).SequenceEqual(
            EnumerateStringLiterals(after.SourceItems),
            StringComparer.Ordinal) &&
        EnumerateComments(before.SourceItems).SequenceEqual(
            EnumerateComments(after.SourceItems),
            StringComparer.Ordinal) &&
        EnumerateApostropheComments(beforeSource).SequenceEqual(
            EnumerateApostropheComments(afterSource),
            StringComparer.Ordinal);

    private static IEnumerable<string> EnumerateApostropheComments(string source)
    {
        foreach (string line in source.Split('\n'))
        {
            bool insideText = false;
            for (int index = 0; index < line.Length; index++)
            {
                if (line[index] == '"')
                {
                    if (insideText && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        index++;
                        continue;
                    }

                    insideText = !insideText;
                }
                else if (line[index] == '\'' && !insideText)
                {
                    yield return line[index..].TrimEnd();
                    break;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateStringLiterals(IReadOnlyList<SourceItemSyntax> items)
    {
        foreach (ExpressionSyntax expression in EnumerateExpressions(items))
        {
            if (expression is StringLiteralExpressionSyntax text)
            {
                yield return text.Value;
            }
        }
    }

    private static IEnumerable<string> EnumerateComments(IReadOnlyList<SourceItemSyntax> items)
    {
        foreach (SourceItemSyntax item in items)
        {
            if (item is FullLineCommentSyntax comment)
            {
                yield return comment.Payload;
            }

            foreach (IReadOnlyList<SourceItemSyntax> children in ChildItemLists(item))
            {
                foreach (string nested in EnumerateComments(children))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<ExpressionSyntax> EnumerateExpressions(IReadOnlyList<SourceItemSyntax> items)
    {
        foreach (SourceItemSyntax item in items)
        {
            IEnumerable<ExpressionSyntax> roots = item switch
            {
                CoreAssignmentStatementSyntax assignment => new[] { assignment.Value },
                CoreArrayAssignmentStatementSyntax assignment => assignment.Indices.Append(assignment.Value),
                DimStatementSyntax declaration => declaration.ArraySizes,
                ConstStatementSyntax constant => new[] { constant.Initializer },
                CallStatementSyntax call => call.Arguments,
                ReturnStatementSyntax { Value: not null } returned => new[] { returned.Value },
                SelectStatementSyntax select => new[] { select.Selector }
                    .Concat(select.Cases.Where(clause => clause.Value is not null).Select(clause => clause.Value!)),
                CorePrintStatementSyntax print => print.Values,
                IfStatementSyntax conditional => conditional.Clauses.Select(clause => clause.Condition),
                ForStatementSyntax loop => new[] { loop.LowerBound, loop.UpperBound },
                DoStatementSyntax { UntilCondition: not null } loop => new[] { loop.UntilCondition },
                WaitStatementSyntax wait => new[] { wait.Duration },
                RandomStatementSyntax random => new[] { random.LowerBound, random.UpperBound },
                _ => Array.Empty<ExpressionSyntax>()
            };

            foreach (ExpressionSyntax root in roots)
            {
                foreach (ExpressionSyntax expression in EnumerateExpression(root))
                {
                    yield return expression;
                }
            }

            foreach (IReadOnlyList<SourceItemSyntax> children in ChildItemLists(item))
            {
                foreach (ExpressionSyntax nested in EnumerateExpressions(children))
                {
                    yield return nested;
                }
            }
        }
    }

    private static IEnumerable<ExpressionSyntax> EnumerateExpression(ExpressionSyntax expression)
    {
        yield return expression;
        IEnumerable<ExpressionSyntax> children = expression switch
        {
            UnaryExpressionSyntax unary => new[] { unary.Operand },
            BinaryExpressionSyntax binary => new[] { binary.Left, binary.Right },
            ParenthesizedExpressionSyntax parenthesized => new[] { parenthesized.Expression },
            CallExpressionSyntax call => call.Arguments,
            ArrayAccessExpressionSyntax array => array.Indices,
            _ => Array.Empty<ExpressionSyntax>()
        };
        foreach (ExpressionSyntax child in children)
        {
            foreach (ExpressionSyntax nested in EnumerateExpression(child))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<IReadOnlyList<SourceItemSyntax>> ChildItemLists(SourceItemSyntax item)
    {
        switch (item)
        {
            case RoutineDeclarationSyntax routine:
                yield return routine.SourceItems;
                break;
            case IfStatementSyntax conditional:
                foreach (ConditionalClauseSyntax clause in conditional.Clauses)
                {
                    yield return clause.SourceItems;
                }

                yield return conditional.ElseSourceItems;
                break;
            case SelectStatementSyntax select:
                foreach (SelectCaseClauseSyntax clause in select.Cases)
                {
                    yield return clause.SourceItems;
                }

                break;
            case ForStatementSyntax loop:
                yield return loop.SourceItems;
                break;
            case DoStatementSyntax loop:
                yield return loop.SourceItems;
                break;
        }
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string WrapLongCalls(string source, SmileProgramSyntax program)
    {
        var replacements = new List<(int Start, int Length, string Text)>();
        foreach (CallStatementSyntax call in EnumerateStatements(program.SourceItems).OfType<CallStatementSyntax>())
        {
            int lineStart = source.LastIndexOf('\n', Math.Max(0, call.Span.Start - 1)) + 1;
            int lineEnd = source.IndexOf('\n', call.Span.Start);
            if (lineEnd < 0)
            {
                lineEnd = source.Length;
            }

            string line = source[lineStart..lineEnd];
            if (line.Length <= MaximumLineLength || call.Arguments.Count < 2 ||
                call.Arguments.Any(argument =>
                    source.AsSpan(argument.Span.Start, argument.Span.Length).IndexOfAny('\r', '\n') >= 0))
            {
                continue;
            }

            int indentationLength = line.Length - line.TrimStart().Length;
            string indentation = line[..indentationLength];
            string continuationIndentation = indentation + "    ";
            string arguments = string.Join(
                ",\n",
                call.Arguments.Select(argument =>
                    continuationIndentation + source.Substring(argument.Span.Start, argument.Span.Length).Trim()));
            string replacement = $"{indentation}Call {call.Name}(\n{arguments}\n{indentation})";
            replacements.Add((lineStart, lineEnd - lineStart, replacement));
        }

        foreach ((int start, int length, string text) in replacements.OrderByDescending(item => item.Start))
        {
            source = source.Remove(start, length).Insert(start, text);
        }

        return source;
    }

    private static IEnumerable<StatementSyntax> EnumerateStatements(IReadOnlyList<SourceItemSyntax> items)
    {
        foreach (StatementSyntax statement in items.OfType<StatementSyntax>())
        {
            yield return statement;
            foreach (IReadOnlyList<SourceItemSyntax> children in ChildItemLists(statement))
            {
                foreach (StatementSyntax nested in EnumerateStatements(children))
                {
                    yield return nested;
                }
            }
        }
    }

    private sealed class SourceLayout
    {
        private readonly string[] _lines;
        private readonly int[] _lineStarts;
        private readonly int[] _depths;
        private readonly HashSet<int> _forcedBlankBefore = new();
        private readonly HashSet<int> _noBlankBefore = new();
        private readonly HashSet<int> _ordinaryClosingLines = new();
        private readonly HashSet<int> _commentLines = new();
        private readonly HashSet<int> _protectedTextLines = new();

        public SourceLayout(string source, SmileProgramSyntax program)
        {
            _lines = source.Split('\n');
            if (_lines.Length > 0 && _lines[^1].Length == 0)
            {
                _lines = _lines[..^1];
            }

            _lineStarts = new int[_lines.Length];
            int offset = 0;
            for (int index = 0; index < _lines.Length; index++)
            {
                _lineStarts[index] = offset;
                offset += _lines[index].Length + 1;
            }

            _depths = new int[_lines.Length];
            MarkItemList(program.SourceItems, 0, topLevel: true);
            MarkProtectedContent(program.SourceItems);
        }

        public string Render()
        {
            var output = new List<string>();
            bool authoredBlank = false;
            for (int line = 0; line < _lines.Length; line++)
            {
                string raw = _lines[line];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    authoredBlank = output.Count > 0;
                    continue;
                }

                bool wantsBlank = authoredBlank || _forcedBlankBefore.Contains(line);
                if (_ordinaryClosingLines.Contains(line) || _noBlankBefore.Contains(line))
                {
                    wantsBlank = false;
                }

                if (wantsBlank && output.Count > 0 && output[^1].Length > 0)
                {
                    output.Add(string.Empty);
                }

                string rendered;
                if (_protectedTextLines.Contains(line))
                {
                    rendered = raw;
                }
                else
                {
                    string content = _commentLines.Contains(line)
                        ? raw.TrimStart()
                        : raw.Trim();
                    rendered = new string(' ', _depths[line] * 4) + content.TrimEnd();
                }

                output.Add(rendered);
                authoredBlank = false;
            }

            while (output.Count > 0 && output[0].Length == 0)
            {
                output.RemoveAt(0);
            }

            while (output.Count > 0 && output[^1].Length == 0)
            {
                output.RemoveAt(output.Count - 1);
            }

            return string.Join("\n", output) + "\n";
        }

        private void MarkItemList(IReadOnlyList<SourceItemSyntax> items, int depth, bool topLevel = false)
        {
            for (int index = 0; index < items.Count; index++)
            {
                SourceItemSyntax item = items[index];
                switch (item)
                {
                    case BlankLineSyntax:
                        break;
                    case FullLineCommentSyntax comment:
                        MarkLine(StartLine(comment.Span), depth);
                        _commentLines.Add(StartLine(comment.Span));
                        break;
                    case StatementSyntax statement:
                        MarkStatement(statement, depth);
                        AddSemanticBoundary(items, index, statement, topLevel);
                        break;
                }
            }

            if (topLevel)
            {
                int prefix = 0;
                while (prefix < items.Count && items[prefix] is FullLineCommentSyntax)
                {
                    prefix++;
                }

                if (prefix > 0 && prefix < items.Count)
                {
                    _forcedBlankBefore.Add(StartLine(items[prefix].Span));
                }
            }
        }

        private void AddSemanticBoundary(
            IReadOnlyList<SourceItemSyntax> items,
            int index,
            StatementSyntax current,
            bool topLevel)
        {
            StatementSyntax? previous = items.Take(index).OfType<StatementSyntax>().LastOrDefault();
            if (previous is null)
            {
                return;
            }

            bool boundary = previous is OptionExplicitStatementSyntax ||
                current is RoutineDeclarationSyntax ||
                previous is RoutineDeclarationSyntax ||
                DeclarationGroup(previous) != DeclarationGroup(current) &&
                    DeclarationGroup(previous) is not null && DeclarationGroup(current) is not null ||
                IsDeclaration(previous) && !IsDeclaration(current) ||
                IsMajorControl(current) && !IsDeclaration(previous) ||
                IsMajorControl(previous);

            if (topLevel && current is RoutineDeclarationSyntax)
            {
                boundary = true;
            }

            if (boundary)
            {
                _forcedBlankBefore.Add(AttachedStartLine(items, index));
            }
        }

        private void MarkStatement(StatementSyntax statement, int depth)
        {
            int start = StartLine(statement.Span);
            int end = EndLine(statement.Span);
            switch (statement)
            {
                case RoutineDeclarationSyntax routine:
                {
                    int headerEnd = HeaderEndLine(start, end, routine.SourceItems);
                    MarkHeader(start, headerEnd, depth);
                    MarkItemList(routine.SourceItems, depth + 1);
                    MarkLine(end, depth);
                    int? bodyStart = FirstContentLine(routine.SourceItems);
                    if (bodyStart.HasValue)
                    {
                        _forcedBlankBefore.Add(bodyStart.Value);
                        _forcedBlankBefore.Add(end);
                    }

                    break;
                }
                case IfStatementSyntax conditional:
                    MarkIf(conditional, depth, start, end);
                    break;
                case SelectStatementSyntax select:
                    MarkSelect(select, depth, start, end);
                    break;
                case ForStatementSyntax loop:
                {
                int headerEnd = HeaderEndLine(start, end, loop.SourceItems);
                MarkHeader(start, headerEnd, depth);
                MarkItemList(loop.SourceItems, depth + 1);
                SuppressBlankBeforeFirstContent(loop.SourceItems);
                MarkLine(end, depth);
                    _ordinaryClosingLines.Add(end);
                    break;
                }
                case DoStatementSyntax loop:
                    MarkHeader(start, start, depth);
                    MarkItemList(loop.SourceItems, depth + 1);
                    SuppressBlankBeforeFirstContent(loop.SourceItems);
                    MarkLine(end, depth);
                    _ordinaryClosingLines.Add(end);
                    break;
                default:
                    MarkHeader(start, end, depth);
                    break;
            }
        }

        private void MarkIf(IfStatementSyntax conditional, int depth, int start, int end)
        {
            for (int index = 0; index < conditional.Clauses.Count; index++)
            {
                ConditionalClauseSyntax clause = conditional.Clauses[index];
                int headerStart = index == 0 ? start : StartLine(clause.Span);
                int headerEnd = HeaderEndLine(headerStart, end, clause.SourceItems);
                MarkHeader(headerStart, headerEnd, depth);
                MarkItemList(clause.SourceItems, depth + 1);
                if (index > 0)
                {
                    _noBlankBefore.Add(headerStart);
                }
                SuppressBlankBeforeFirstContent(clause.SourceItems);
            }

            if (conditional.HasElseClause)
            {
                int searchStart = conditional.Clauses.Count == 0
                    ? start
                    : EndLine(conditional.Clauses[^1].Span) + 1;
                int elseLine = FindKeywordLine(searchStart, end, "Else");
                MarkLine(elseLine, depth);
                _noBlankBefore.Add(elseLine);
                MarkItemList(conditional.ElseSourceItems, depth + 1);
                SuppressBlankBeforeFirstContent(conditional.ElseSourceItems);
            }

            MarkLine(end, depth);
            _ordinaryClosingLines.Add(end);
        }

        private void MarkSelect(SelectStatementSyntax select, int depth, int start, int end)
        {
            int headerEnd = select.Cases.Count == 0 ? end - 1 : StartLine(select.Cases[0].Span) - 1;
            MarkHeader(start, Math.Max(start, headerEnd), depth);
            for (int index = 0; index < select.Cases.Count; index++)
            {
                SelectCaseClauseSyntax clause = select.Cases[index];
                int caseLine = StartLine(clause.Span);
                int nextBoundary = index + 1 < select.Cases.Count ? StartLine(select.Cases[index + 1].Span) : end;
                int caseHeaderEnd = HeaderEndLine(caseLine, nextBoundary, clause.SourceItems);
                MarkHeader(caseLine, caseHeaderEnd, depth + 1);
                MarkItemList(clause.SourceItems, depth + 2);
                SuppressBlankBeforeFirstContent(clause.SourceItems);
                if (index > 0)
                {
                    _forcedBlankBefore.Add(caseLine);
                }
                else
                {
                    _noBlankBefore.Add(caseLine);
                }
            }

            MarkLine(end, depth);
            _ordinaryClosingLines.Add(end);
        }

        private void MarkProtectedContent(IReadOnlyList<SourceItemSyntax> items)
        {
            foreach (SourceItemSyntax item in items)
            {
                if (item is FullLineCommentSyntax comment)
                {
                    _commentLines.Add(StartLine(comment.Span));
                }

                foreach (IReadOnlyList<SourceItemSyntax> children in ChildItemLists(item))
                {
                    MarkProtectedContent(children);
                }
            }

            foreach (ExpressionSyntax expression in EnumerateExpressions(items))
            {
                if (expression is not StringLiteralExpressionSyntax text)
                {
                    continue;
                }

                int start = StartLine(text.Span);
                int end = EndLine(text.Span);
                if (start == end)
                {
                    continue;
                }

                for (int line = start; line <= end; line++)
                {
                    _protectedTextLines.Add(line);
                }
            }
        }

        private int HeaderEndLine(int start, int fallbackEnd, IReadOnlyList<SourceItemSyntax> body)
        {
            if (body.Count == 0)
            {
                return Math.Max(start, fallbackEnd - 1);
            }

            return Math.Max(start, StartLine(body[0].Span) - 1);
        }

        private int AttachedStartLine(IReadOnlyList<SourceItemSyntax> items, int statementIndex)
        {
            int line = StartLine(items[statementIndex].Span);
            for (int index = statementIndex - 1; index >= 0; index--)
            {
                if (items[index] is FullLineCommentSyntax comment)
                {
                    line = StartLine(comment.Span);
                    continue;
                }

                break;
            }

            return line;
        }

        private int? FirstContentLine(IReadOnlyList<SourceItemSyntax> items)
        {
            SourceItemSyntax? first = items.FirstOrDefault(item => item is not BlankLineSyntax);
            return first is null ? null : StartLine(first.Span);
        }

        private void SuppressBlankBeforeFirstContent(IReadOnlyList<SourceItemSyntax> items)
        {
            int? first = FirstContentLine(items);
            if (first.HasValue)
            {
                _noBlankBefore.Add(first.Value);
            }
        }

        private int FindKeywordLine(int start, int end, string keyword)
        {
            for (int line = Math.Max(0, start); line < Math.Min(end, _lines.Length); line++)
            {
                if (string.Equals(_lines[line].Trim(), keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return line;
                }
            }

            return Math.Clamp(start, 0, Math.Max(0, _lines.Length - 1));
        }

        private void MarkHeader(int start, int end, int depth)
        {
            for (int line = start; line <= end && line < _lines.Length; line++)
            {
                MarkLine(line, depth + (line == start ? 0 : 1));
            }
        }

        private void MarkLine(int line, int depth)
        {
            if (line >= 0 && line < _depths.Length)
            {
                _depths[line] = Math.Max(0, depth);
            }
        }

        private int StartLine(TextSpan span) => Math.Clamp(span.Line - 1, 0, Math.Max(0, _lines.Length - 1));

        private int EndLine(TextSpan span)
        {
            int position = span.Start + Math.Max(0, span.Length - 1);
            int index = Array.BinarySearch(_lineStarts, position);
            return index >= 0 ? index : Math.Max(0, ~index - 1);
        }

        private static bool IsDeclaration(StatementSyntax statement) =>
            statement is DimStatementSyntax or ConstStatementSyntax;

        private static string? DeclarationGroup(StatementSyntax statement) => statement switch
        {
            ConstStatementSyntax => "constant",
            DimStatementSyntax { IsArray: true } => "array",
            DimStatementSyntax => "scalar",
            _ => null
        };

        private static bool IsMajorControl(StatementSyntax statement) =>
            statement is IfStatementSyntax or SelectStatementSyntax or ForStatementSyntax or DoStatementSyntax;
    }
}
