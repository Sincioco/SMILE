using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class PythonCodeGenerator : ICodeGenerator
{
    private static readonly IReadOnlyDictionary<VariableSymbol, SmileValue> EmptyValues =
        new Dictionary<VariableSymbol, SmileValue>();

    public TargetLanguage Language => TargetLanguage.Python;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        bool hasInput = TargetRuntimeFacts.HasInput(program);
        bool requiresUtf8Output = TargetRuntimeFacts.RequiresUtf8Output(program);
        bool checkedArithmetic = TargetRuntimeFacts.NeedsCheckedIntegerArithmetic(program);
        var knownValues = new Dictionary<BoundStatement, IReadOnlyDictionary<VariableSymbol, SmileValue>>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> values =
                statement is BoundWhileStatement loop
                    ? analysis.GetWhileFacts(loop).ValuesAtHead
                    : analysis.GetStatementFacts(statement).ValuesBefore;
            knownValues[statement] = GeneratorConditionFacts.KnownValues(values);
        }

        var source = new StringBuilder();
        bool emittedHelper = false;

        if (requiresUtf8Output || checkedArithmetic)
        {
            source.AppendLine("import sys");
            source.AppendLine();
        }

        if (requiresUtf8Output)
        {
            source.AppendLine("sys.stdout.reconfigure(encoding=\"utf-8\", errors=\"strict\")");
            source.AppendLine("sys.stderr.reconfigure(encoding=\"utf-8\", errors=\"strict\")");
            source.AppendLine();
            source.AppendLine();
        }

        if (hasInput)
        {
            AppendInputHelpers(source, program);
            emittedHelper = true;
        }
        else if (checkedArithmetic)
        {
            AppendFailureHelper(source);
            emittedHelper = true;
        }

        if (PythonGenerationFacts.NeedsTextHelper(program))
        {
            if (emittedHelper)
            {
                source.AppendLine();
                source.AppendLine();
            }

            source.AppendLine("def _smile_text(value: object) -> str:");
            source.AppendLine("    if isinstance(value, bool):");
            source.AppendLine("        return \"TRUE\" if value else \"FALSE\"");
            source.AppendLine();
            source.AppendLine("    return str(value)");
            emittedHelper = true;
        }

        if (PythonGenerationFacts.NeedsDivisionHelper(program) && !checkedArithmetic)
        {
            if (emittedHelper)
            {
                source.AppendLine();
                source.AppendLine();
            }

            source.AppendLine("def _smile_div(left: int, right: int) -> int:");
            source.AppendLine("    quotient = abs(left) // abs(right)");
            source.AppendLine("    return -quotient if (left < 0) != (right < 0) else quotient");
            emittedHelper = true;
        }

        if (checkedArithmetic)
        {
            if (emittedHelper)
            {
                source.AppendLine();
                source.AppendLine();
            }

            AppendCheckedArithmeticHelpers(source);
            emittedHelper = true;
        }

        if (emittedHelper)
        {
            source.AppendLine();
            source.AppendLine();
        }

        source.AppendLine("def main() -> None:");
        AppendSourceItems(
            source,
            program.SourceItems,
            "    ",
            identifiers,
            knownValues,
            checkedArithmetic);
        if (program.Statements.Count == 0)
        {
            source.AppendLine("    pass");
        }

        source.AppendLine();
        source.AppendLine();
        source.AppendLine("if __name__ == \"__main__\":");
        source.AppendLine("    main()");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.py", TextOutput.EnsureOneTrailingNewLinePreservingExistingLineEndings(source.ToString()), IsPrimary: true) });
    }

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<BoundStatement, IReadOnlyDictionary<VariableSymbol, SmileValue>> knownValues,
        bool checkedArithmetic)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            if (sourceItem is BoundFullLineComment comment)
            {
                TargetComments.Append(source, TargetLanguage.Python, indent, comment.Payload);
                continue;
            }

            if (sourceItem is BoundBlankLine)
            {
                source.AppendLine();
                continue;
            }

            var statement = (BoundStatement)sourceItem;
            IReadOnlyDictionary<VariableSymbol, SmileValue> values =
                knownValues.TryGetValue(statement, out var statementValues)
                    ? statementValues
                    : EmptyValues;
            var expressions = new PythonExpressionWriter(identifiers, values, checkedArithmetic);

            switch (statement)
            {
                case BoundLetStatement let:
                    source.AppendLine($"{indent}{identifiers.Get(let.Variable)} = {WriteDirectExpression(let.Initializer, indent, expressions)}");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {WriteDirectExpression(set.Value, indent, expressions)}");
                    break;

                case BoundInputStatement input:
                    source.Append(indent).Append(identifiers.Get(input.Variable)).Append(" = ")
                        .Append(input.Variable.Type switch
                        {
                            SmileType.String => "_smile_input_string",
                            SmileType.Integer => "_smile_input_integer",
                            SmileType.Boolean => "_smile_input_boolean",
                            _ => throw new InvalidOperationException("Unsupported INPUT target type.")
                        })
                        .Append('(').Append(TargetEscapes.PythonString(input.Variable.Name))
                        .AppendLine(")");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "print()"
                        : $"print({WriteDirectDisplayExpression(print.Value, indent, expressions)})");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(source, conditional, indent, identifiers, knownValues, expressions, checkedArithmetic);
                    break;

                case BoundWhileStatement loop:
                    AppendWhileStatement(
                        source,
                        loop,
                        indent,
                        identifiers,
                        knownValues,
                        expressions,
                        checkedArithmetic);
                    break;
            }
        }
    }

    private static string WriteDirectExpression(
        BoundExpression expression,
        string structuralIndent,
        PythonExpressionWriter expressions) =>
        expression is BoundStringLiteralExpression literal && literal.Value.Contains('\n')
            ? TargetMultilineLiterals.Python(literal.Value, structuralIndent)
            : expressions.Write(expression);

    private static string WriteDirectDisplayExpression(
        BoundExpression expression,
        string structuralIndent,
        PythonExpressionWriter expressions) =>
        expression is BoundStringLiteralExpression literal && literal.Value.Contains('\n')
            ? TargetMultilineLiterals.Python(literal.Value, structuralIndent)
            : expressions.WriteDisplay(expression);

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<BoundStatement, IReadOnlyDictionary<VariableSymbol, SmileValue>> knownValues,
        PythonExpressionWriter expressions,
        bool checkedArithmetic)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if " : "elif ")
                .Append(expressions.Write(clause.Condition))
                .AppendLine(":");
            AppendSourceItems(
                source,
                clause.SourceItems,
                indent + "    ",
                identifiers,
                knownValues,
                checkedArithmetic);
            if (clause.Statements.Count == 0)
            {
                source.Append(indent).AppendLine("    pass");
            }
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else:");
            AppendSourceItems(
                source,
                conditional.ElseSourceItems,
                indent + "    ",
                identifiers,
                knownValues,
                checkedArithmetic);
            if (conditional.ElseStatements.Count == 0)
            {
                source.Append(indent).AppendLine("    pass");
            }
        }
    }

    private static void AppendWhileStatement(
        StringBuilder source,
        BoundWhileStatement loop,
        string indent,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<BoundStatement, IReadOnlyDictionary<VariableSymbol, SmileValue>> knownValues,
        PythonExpressionWriter expressions,
        bool checkedArithmetic)
    {
        source.Append(indent).Append("while ")
            .Append(expressions.Write(loop.Condition)).AppendLine(":");
        AppendSourceItems(
            source,
            loop.SourceItems,
            indent + "    ",
            identifiers,
            knownValues,
            checkedArithmetic);
        if (loop.Statements.Count == 0)
        {
            source.Append(indent).AppendLine("    pass");
        }
    }

    private static void AppendInputHelpers(StringBuilder source, BoundProgram program)
    {
        source.AppendLine("_smile_skip_lf = False");
        source.AppendLine();
        source.AppendLine();
        AppendFailureHelper(source);
        source.AppendLine();
        source.AppendLine();
        source.AppendLine("def _smile_next_byte() -> int:");
        source.AppendLine("    value = sys.stdin.buffer.read(1)");
        source.AppendLine("    return -1 if value == b\"\" else value[0]");
        source.AppendLine();
        source.AppendLine();
        source.AppendLine("def _smile_read_line(variable_name: str) -> str:");
        source.AppendLine("    global _smile_skip_lf");
        source.AppendLine("    values = bytearray()");
        source.AppendLine("    first_byte = True");
        source.AppendLine("    try:");
        source.AppendLine("        while True:");
        source.AppendLine("            value = _smile_next_byte()");
        source.AppendLine("            if first_byte:");
        source.AppendLine("                first_byte = False");
        source.AppendLine("                if _smile_skip_lf:");
        source.AppendLine("                    _smile_skip_lf = False");
        source.AppendLine("                    if value == 10:");
        source.AppendLine("                        continue");
        source.AppendLine("            if value < 0:");
        source.AppendLine("                if len(values) == 0:");
        source.AppendLine("                    _smile_fail(f\"SMILE Runtime Error SMILER1501: Input ended before a value was received for '{variable_name}'.\")");
        source.AppendLine("                break");
        source.AppendLine("            if value == 10:");
        source.AppendLine("                break");
        source.AppendLine("            if value == 13:");
        source.AppendLine("                _smile_skip_lf = True");
        source.AppendLine("                break");
        source.AppendLine("            values.append(value)");
        source.AppendLine($"            if len(values) > {SmileLanguage.MaximumInputLineUtf8Bytes}:");
        source.AppendLine($"                _smile_fail(f\"SMILE Runtime Error SMILER1502: Input for '{{variable_name}}' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.\")");
        source.AppendLine("        return values.decode(\"utf-8\", errors=\"strict\")");
        source.AppendLine("    except (OSError, UnicodeError):");
        source.AppendLine("        _smile_fail(f\"SMILE Runtime Error SMILER1506: Input for '{variable_name}' could not be read as valid UTF-8 text.\")");
        source.AppendLine("        return \"\"");

        if (TargetRuntimeFacts.HasInput(program, SmileType.String))
        {
            source.AppendLine();
            source.AppendLine();
            source.AppendLine("def _smile_input_string(variable_name: str) -> str:");
            source.AppendLine("    return _smile_read_line(variable_name)");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine();
            source.AppendLine();
            source.AppendLine("def _smile_input_integer(variable_name: str) -> int:");
            source.AppendLine("    text = _smile_read_line(variable_name).strip(\" \\t\")");
            source.AppendLine("    digits = text[1:] if text[:1] in (\"+\", \"-\") else text");
            source.AppendLine("    if not digits or any(character < \"0\" or character > \"9\" for character in digits):");
            source.AppendLine("        _smile_fail(f\"SMILE Runtime Error SMILER1503: Input for '{variable_name}' is not a valid Integer.\")");
            source.AppendLine("    value = int(text)");
            source.AppendLine("    if value < -9223372036854775808 or value > 9223372036854775807:");
            source.AppendLine("        _smile_fail(f\"SMILE Runtime Error SMILER1504: Input for '{variable_name}' is outside the signed 64-bit Integer range.\")");
            source.AppendLine("    return value");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Boolean))
        {
            source.AppendLine();
            source.AppendLine("def _smile_ascii_equals(text: str, expected: str) -> bool:");
            source.AppendLine("    return len(text) == len(expected) and all(");
            source.AppendLine("        actual == upper or actual == upper.lower()");
            source.AppendLine("        for actual, upper in zip(text, expected)");
            source.AppendLine("    )");
            source.AppendLine();
            source.AppendLine("def _smile_input_boolean(variable_name: str) -> bool:");
            source.AppendLine("    text = _smile_read_line(variable_name).strip(\" \\t\")");
            source.AppendLine("    if _smile_ascii_equals(text, \"TRUE\"):");
            source.AppendLine("        return True");
            source.AppendLine("    if _smile_ascii_equals(text, \"FALSE\"):");
            source.AppendLine("        return False");
            source.AppendLine("    _smile_fail(f\"SMILE Runtime Error SMILER1505: Input for '{variable_name}' must be TRUE or FALSE.\")");
            source.AppendLine("    return False");
        }
    }

    private static void AppendFailureHelper(StringBuilder source)
    {
        source.AppendLine("def _smile_fail(message: str) -> None:");
        source.AppendLine("    sys.stderr.write(message + \"\\n\")");
        source.AppendLine("    raise SystemExit(1)");
    }

    private static void AppendCheckedArithmeticHelpers(StringBuilder source)
    {
        source.AppendLine("def _smile_checked(value: int) -> int:");
        source.AppendLine("    if value < -9223372036854775808 or value > 9223372036854775807:");
        source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\")");
        source.AppendLine("    return value");
        source.AppendLine();
        source.AppendLine();
        source.AppendLine("def _smile_add(left: int, right: int) -> int:");
        source.AppendLine("    return _smile_checked(left + right)");
        source.AppendLine();
        source.AppendLine();
        source.AppendLine("def _smile_subtract(left: int, right: int) -> int:");
        source.AppendLine("    return _smile_checked(left - right)");
        source.AppendLine();
        source.AppendLine();
        source.AppendLine("def _smile_multiply(left: int, right: int) -> int:");
        source.AppendLine("    return _smile_checked(left * right)");
        source.AppendLine();
        source.AppendLine();
        source.AppendLine("def _smile_negate(value: int) -> int:");
        source.AppendLine("    return _smile_checked(-value)");
        source.AppendLine();
        source.AppendLine();
        source.AppendLine("def _smile_div(left: int, right: int) -> int:");
        source.AppendLine("    if right == 0:");
        source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1207: Division by zero.\")");
        source.AppendLine("    if left == -9223372036854775808 and right == -1:");
        source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\")");
        source.AppendLine("    quotient = abs(left) // abs(right)");
        source.AppendLine("    return -quotient if (left < 0) != (right < 0) else quotient");
    }

}

internal static class PythonGenerationFacts
{
    public static bool NeedsTextHelper(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let => ContainsTextConversion(let.Initializer),
            BoundSetStatement set => ContainsTextConversion(set.Value),
            BoundPrintStatement print when !print.IsBlankLine =>
                print.Value.Type is not SmileType.String || ContainsTextConversion(print.Value),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                ContainsTextConversion(clause.Condition)),
            BoundWhileStatement loop => ContainsTextConversion(loop.Condition),
            _ => false
        });

    public static bool NeedsDivisionHelper(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let => ContainsDivision(let.Initializer),
            BoundSetStatement set => ContainsDivision(set.Value),
            BoundPrintStatement print when !print.IsBlankLine => ContainsDivision(print.Value),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                ContainsDivision(clause.Condition)),
            BoundWhileStatement loop => ContainsDivision(loop.Condition),
            _ => false
        });

    private static bool ContainsTextConversion(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary => ContainsTextConversion(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsTextConversion(binary.Left) || ContainsTextConversion(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                (hole.Expression.Type is not SmileType.String || ContainsTextConversion(hole.Expression))),
            _ => false
        };

    private static bool ContainsDivision(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary => ContainsDivision(unary.Operand),
            BoundBinaryExpression binary =>
                binary.Operator.Kind is BoundBinaryOperatorKind.Division ||
                ContainsDivision(binary.Left) ||
                ContainsDivision(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole && ContainsDivision(hole.Expression)),
            _ => false
        };
}

internal sealed class PythonExpressionWriter
{
    private const int OrPrecedence = 1;
    private const int AndPrecedence = 2;
    private const int NotPrecedence = 3;
    private const int ComparisonPrecedence = 4;
    private const int AdditionPrecedence = 5;
    private const int MultiplicationPrecedence = 6;
    private const int UnaryPrecedence = 7;
    private const int CallPrecedence = 8;

    private readonly TargetIdentifierMap _identifiers;
    private readonly IReadOnlyDictionary<VariableSymbol, SmileValue> _values;
    private readonly bool _checkedRuntimeArithmetic;

    public PythonExpressionWriter(
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        bool checkedRuntimeArithmetic = false)
    {
        _identifiers = identifiers;
        _values = values;
        _checkedRuntimeArithmetic = checkedRuntimeArithmetic;
    }

    public string Write(BoundExpression expression) =>
        WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

    public string WriteDisplay(BoundExpression expression) =>
        expression.Type is SmileType.String
            ? Write(expression)
            : $"_smile_text({Write(expression)})";

    private string WriteExpression(
        BoundExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator) =>
        expression switch
        {
            BoundStringLiteralExpression literal => TargetEscapes.PythonString(literal.Value),
            BoundIntegerLiteralExpression literal => literal.Value.ToString(CultureInfo.InvariantCulture),
            BoundBooleanLiteralExpression literal => literal.Value ? "True" : "False",
            BoundVariableExpression variable => _identifiers.Get(variable.Variable),
            BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
            BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
            BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
            _ => TargetEscapes.PythonString(string.Empty)
        };

    private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
    {
        if (expression.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation)
        {
            string operand = expression.Operand is BoundBinaryExpression
                ? "(" + WriteExpression(expression.Operand, 0, isRightChild: false, parentOperator: null) + ")"
                : WriteExpression(expression.Operand, NotPrecedence, isRightChild: true, parentOperator: null);
            string logicalText = "not " + operand;
            return NotPrecedence < parentPrecedence ? "(" + logicalText + ")" : logicalText;
        }

        if (_checkedRuntimeArithmetic &&
            expression.Operator.Kind is BoundUnaryOperatorKind.Negation &&
            expression.Operand.Type is SmileType.Integer)
        {
            string call = "_smile_negate(" +
                WriteExpression(expression.Operand, 0, isRightChild: false, parentOperator: null) +
                ")";
            return CallPrecedence < parentPrecedence ? "(" + call + ")" : call;
        }

        string op = expression.Operator.Kind is BoundUnaryOperatorKind.Negation ? "-" : "+";
        string value = WriteExpression(
            expression.Operand,
            UnaryPrecedence,
            isRightChild: true,
            parentOperator: null);
        string text = op + value;
        return UnaryPrecedence < parentPrecedence ? "(" + text + ")" : text;
    }

    private string WriteBinary(
        BoundBinaryExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (_checkedRuntimeArithmetic &&
            expression.Left.Type is SmileType.Integer &&
            expression.Operator.Kind is BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Subtraction or
                BoundBinaryOperatorKind.Multiplication)
        {
            string helper = expression.Operator.Kind switch
            {
                BoundBinaryOperatorKind.Addition => "_smile_add",
                BoundBinaryOperatorKind.Subtraction => "_smile_subtract",
                BoundBinaryOperatorKind.Multiplication => "_smile_multiply",
                _ => throw new InvalidOperationException("Unsupported checked Python Integer operator.")
            };
            string call = helper + "(" +
                WriteExpression(expression.Left, 0, isRightChild: false, parentOperator: null) +
                ", " +
                WriteExpression(expression.Right, 0, isRightChild: false, parentOperator: null) +
                ")";
            return CallPrecedence < parentPrecedence ? "(" + call + ")" : call;
        }

        if (expression.Operator.Kind is BoundBinaryOperatorKind.Division)
        {
            string call =
                "_smile_div(" +
                WriteExpression(expression.Left, 0, isRightChild: false, parentOperator: null) +
                ", " +
                WriteExpression(expression.Right, 0, isRightChild: false, parentOperator: null) +
                ")";
            return CallPrecedence < parentPrecedence ? "(" + call + ")" : call;
        }

        int precedence = Precedence(expression.Operator.Kind);
        string left = WriteExpression(
            expression.Left,
            precedence,
            isRightChild: false,
            parentOperator: expression.Operator.Kind);
        string right = WriteExpression(
            expression.Right,
            precedence,
            isRightChild: true,
            parentOperator: expression.Operator.Kind);
        string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;

        return NeedsParentheses(
            expression.Operator.Kind,
            precedence,
            parentPrecedence,
            isRightChild,
            parentOperator)
            ? "(" + text + ")"
            : text;
    }

    private string WriteInterpolatedString(BoundInterpolatedStringExpression expression)
    {
        if (!expression.Parts.Any(part => part is BoundInterpolationExpressionPart))
        {
            string literalText = string.Concat(
                expression.Parts.OfType<BoundInterpolatedTextPart>().Select(part => part.Text));
            return TargetEscapes.PythonString(literalText);
        }

        if (expression.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                ContainsStringLiteral(hole.Expression) &&
                !TryGetDisplayText(hole.Expression, out _)))
        {
            // Python 3.10 cannot parse a backslash or a same-quote String
            // literal inside an f-string hole. If branch-aware facts cannot
            // prove that hole, keep it live by lowering the complete template
            // to ordinary String concatenation instead of folding today's
            // selected branch into one unrelated literal.
            string[] runtimeSegments = expression.Parts
                .Select(part => part switch
                {
                    BoundInterpolatedTextPart text => TargetEscapes.PythonString(text.Text),
                    BoundInterpolationExpressionPart hole => WriteDisplay(hole.Expression),
                    _ => TargetEscapes.PythonString(string.Empty)
                })
                .Where(segment => segment != TargetEscapes.PythonString(string.Empty))
                .ToArray();
            return runtimeSegments.Length == 0
                ? TargetEscapes.PythonString(string.Empty)
                : string.Join(" + ", runtimeSegments);
        }

        var fStringText = new StringBuilder();
        bool emittedExpressionHole = false;

        foreach (BoundInterpolatedPart part in expression.Parts)
        {
            switch (part)
            {
                case BoundInterpolatedTextPart literal:
                    fStringText.Append(TargetEscapes.PythonFStringText(literal.Text));
                    break;

                case BoundInterpolationExpressionPart hole when
                    ContainsStringLiteral(hole.Expression) &&
                    TryGetDisplayText(hole.Expression, out string foldedText):
                    // Python 3.10 rejects backslashes and same-quote literals
                    // inside f-string expressions. Current SMILE holes are
                    // compile-time constants, so fold only that unsafe hole
                    // into f-string text while preserving all safe holes.
                    fStringText.Append(TargetEscapes.PythonFStringText(foldedText));
                    break;

                case BoundInterpolationExpressionPart hole:
                    fStringText.Append('{').Append(WriteDisplay(hole.Expression)).Append('}');
                    emittedExpressionHole = true;
                    break;
            }
        }

        if (!emittedExpressionHole)
        {
            if (BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value))
            {
                return TargetEscapes.PythonString(value.ToDisplayText());
            }

            // Every folded hole above was individually proved from the same
            // abstract-known environment, so reaching this defensive path is
            // only possible for a future bound node. Preserve a live f-string
            // rather than consulting the selected concrete execution trace.
            return "f\"" + fStringText + "\"";
        }

        return "f\"" + fStringText + "\"";
    }

    private bool TryGetDisplayText(BoundExpression expression, out string text)
    {
        if (BoundExpressionEvaluator.TryEvaluate(expression, _values, out SmileValue value))
        {
            text = value.ToDisplayText();
            return true;
        }

        text = string.Empty;
        return false;
    }

    private static bool ContainsStringLiteral(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundUnaryExpression unary => ContainsStringLiteral(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsStringLiteral(binary.Left) || ContainsStringLiteral(binary.Right),
            BoundInterpolatedStringExpression => true,
            _ => false
        };

    private static string OperatorText(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
            BoundBinaryOperatorKind.Subtraction => "-",
            BoundBinaryOperatorKind.Multiplication => "*",
            BoundBinaryOperatorKind.Equality => "==",
            BoundBinaryOperatorKind.Inequality => "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => "and",
            BoundBinaryOperatorKind.LogicalOr => "or",
            _ => string.Empty
        };

    private static int Precedence(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Multiplication => MultiplicationPrecedence,
            BoundBinaryOperatorKind.Addition or
            BoundBinaryOperatorKind.Subtraction or
            BoundBinaryOperatorKind.StringConcatenation => AdditionPrecedence,
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals or
            BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality => ComparisonPrecedence,
            BoundBinaryOperatorKind.LogicalAnd => AndPrecedence,
            BoundBinaryOperatorKind.LogicalOr => OrPrecedence,
            _ => 0
        };

    private static bool NeedsParentheses(
        BoundBinaryOperatorKind currentOperator,
        int precedence,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (precedence < parentPrecedence)
        {
            return true;
        }

        // Python chains adjacent comparisons. Parenthesize either child so a
        // nested SMILE equality tree never becomes Python's chained syntax.
        if (parentOperator.HasValue &&
            IsComparison(currentOperator) &&
            IsComparison(parentOperator.Value))
        {
            return true;
        }

        return isRightChild &&
            precedence == parentPrecedence &&
            parentOperator is not (
                BoundBinaryOperatorKind.Addition or
                BoundBinaryOperatorKind.Multiplication or
                BoundBinaryOperatorKind.StringConcatenation or
                BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr);
    }

    private static bool IsComparison(BoundBinaryOperatorKind kind) =>
        kind is BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality or
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals;
}
