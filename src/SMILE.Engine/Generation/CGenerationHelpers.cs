using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed record CPrintfPlan(
    string FormatText,
    IReadOnlyList<string> Arguments)
{
    public static CPrintfPlan FromPrint(
        BoundPrintStatement print,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        var format = new StringBuilder();
        var arguments = new List<string>();

        // FormatText is raw printf format text, not C source text. Literal
        // percent signs are doubled here for printf safety; C string escaping
        // happens later exactly once when the call is emitted.
        if (!print.IsBlankLine)
        {
            AppendExpression(format, arguments, print.Value, renderExpression, usesSigned64Integers);
        }

        format.Append('\n');
        return new CPrintfPlan(format.ToString(), arguments);
    }

    private static void AppendExpression(
        StringBuilder format,
        List<string> arguments,
        BoundExpression expression,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        if (expression.Type is not SmileType.String)
        {
            AppendTypedArgument(format, arguments, expression, renderExpression, usesSigned64Integers);
            return;
        }

        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                AppendLiteralToFormat(format, literal.Value);
                break;

            case BoundVariableExpression:
                format.Append("%s");
                arguments.Add(renderExpression(expression));
                break;

            case BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary:
                AppendExpression(format, arguments, binary.Left, renderExpression, usesSigned64Integers);
                AppendExpression(format, arguments, binary.Right, renderExpression, usesSigned64Integers);
                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolatedPart part in interpolated.Parts)
                {
                    switch (part)
                    {
                        case BoundInterpolatedTextPart text:
                            AppendLiteralToFormat(format, text.Text);
                            break;

                        case BoundInterpolationExpressionPart interpolation:
                            AppendExpression(format, arguments, interpolation.Expression, renderExpression, usesSigned64Integers);
                            break;
                    }
                }

                break;

            default:
                // Current String expressions are literals, variables,
                // concatenation, or interpolation. Keeping a defensive %s
                // fallback makes future String nodes fail visibly in target
                // compilation rather than silently dropping output.
                format.Append("%s");
                arguments.Add(renderExpression(expression));
                break;
        }
    }

    private static void AppendTypedArgument(
        StringBuilder format,
        List<string> arguments,
        BoundExpression expression,
        Func<BoundExpression, string> renderExpression,
        bool usesSigned64Integers)
    {
        string rendered = renderExpression(expression);
        switch (expression.Type)
        {
            case SmileType.Integer:
                if (usesSigned64Integers)
                {
                    // int64_t is not guaranteed to alias long long on every
                    // C implementation. The explicit value-preserving cast
                    // keeps the conventional %lld format portable.
                    format.Append("%lld");
                    arguments.Add("(long long)(" + rendered + ")");
                }
                else
                {
                    format.Append("%d");
                    arguments.Add(rendered);
                }

                break;

            case SmileType.Boolean:
                format.Append("%s");
                string condition = expression is BoundVariableExpression or BoundBooleanLiteralExpression
                    ? rendered
                    : "(" + rendered + ")";
                arguments.Add(condition + " ? \"TRUE\" : \"FALSE\"");
                break;

            case SmileType.String:
                format.Append("%s");
                arguments.Add(rendered);
                break;
        }
    }

    private static void AppendLiteralToFormat(StringBuilder format, string text)
    {
        foreach (char value in text)
        {
            // A user-authored '%' is data, never a printf directive. Doubling
            // it keeps every generated format string compiler-owned and safe.
            if (value == '%')
            {
                format.Append("%%");
            }
            else
            {
                format.Append(value);
            }
        }
    }
}

internal static class CGenerationFacts
{
    public static bool NeedsBooleanHeader(BoundProgram program) =>
        program.Variables.Any(variable => variable.Type is SmileType.Boolean) ||
        EnumerateRootExpressions(program).Any(ContainsBooleanLiteral);

    public static bool NeedsStringComparison(BoundProgramAnalysis analysis)
    {
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            bool needsComparison = statement switch
            {
                BoundLetStatement let when
                    let.Variable.Type is not SmileType.String || !facts.Value.IsKnown =>
                    ContainsStringComparison(let.Initializer),
                BoundSetStatement set when
                    set.Variable.Type is not SmileType.String || !facts.Value.IsKnown =>
                    ContainsStringComparison(set.Value),
                BoundPrintStatement { IsBlankLine: false } print =>
                    ContainsStringComparison(print.Value),
                BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    ContainsStringComparison(clause.Condition)),
                BoundWhileStatement loop => ContainsStringComparison(loop.Condition),
                _ => false
            };

            if (needsComparison)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<BoundExpression> EnumerateRootExpressions(BoundProgram program)
    {
        foreach (BoundStatement statement in BoundStatementTree.Enumerate(program))
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    yield return let.Initializer;
                    break;

                case BoundSetStatement set:
                    yield return set.Value;
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    yield return print.Value;
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        yield return clause.Condition;
                    }

                    break;

                case BoundWhileStatement loop:
                    yield return loop.Condition;
                    break;
            }
        }
    }

    private static bool ContainsBooleanLiteral(BoundExpression expression) =>
        expression switch
        {
            BoundBooleanLiteralExpression => true,
            BoundUnaryExpression unary => ContainsBooleanLiteral(unary.Operand),
            BoundBinaryExpression binary => ContainsBooleanLiteral(binary.Left) ||
                ContainsBooleanLiteral(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsBooleanLiteral(interpolation.Expression)),
            _ => false
        };

    private static bool ContainsStringComparison(BoundExpression expression) =>
        expression switch
        {
            BoundBinaryExpression binary =>
                binary.Left.Type is SmileType.String &&
                    binary.Operator.Kind is BoundBinaryOperatorKind.Equality or
                        BoundBinaryOperatorKind.Inequality ||
                ContainsStringComparison(binary.Left) ||
                ContainsStringComparison(binary.Right),
            BoundUnaryExpression unary =>
                ContainsStringComparison(unary.Operand),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                ContainsStringComparison(interpolation.Expression)),
            _ => false
        };

    internal static bool ShouldUseExactStorageComparison(
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (expression.Left.Type is not SmileType.String ||
            expression.Operator.Kind is not (BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) ||
            !IsDirectStringStorageOperand(expression.Left) ||
            !IsDirectStringStorageOperand(expression.Right) ||
            expression.Left is not BoundVariableExpression &&
            expression.Right is not BoundVariableExpression)
        {
            return false;
        }

        return RequiresExactLength(expression.Left, exactStringLengths) ||
            RequiresExactLength(expression.Right, exactStringLengths);
    }

    private static bool IsDirectStringStorageOperand(BoundExpression expression) =>
        expression is BoundVariableExpression or BoundStringLiteralExpression;

    private static bool RequiresExactLength(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths) =>
        expression switch
        {
            BoundVariableExpression variable => exactStringLengths.ContainsKey(variable.Variable),
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            _ => false
        };

}

internal static class CGeneratedRuntime
{
    public static void Append(
        StringBuilder source,
        BoundProgram program,
        TargetIntegerProfile integers,
        bool checkedArithmetic,
        bool includeInput = true)
    {
        // Objective-C still uses the historical shared INPUT runtime while it
        // is paused. Active C opts out and emits the language's native input
        // statements directly at each INPUT source position.
        bool hasInput = includeInput && TargetRuntimeFacts.HasInput(program);
        if (hasInput)
        {
            AppendInput(source, program);
        }

        if (checkedArithmetic)
        {
            if (hasInput)
            {
                source.AppendLine();
            }

            AppendArithmetic(source, program, integers);
        }

        if (hasInput || checkedArithmetic)
        {
            source.AppendLine();
        }
    }

    public static void AppendInputStatement(
        StringBuilder source,
        string indent,
        BoundInputStatement input,
        int ordinal,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        string name = identifiers.Get(input.Variable);
        string declaredName = TargetEscapes.CString(input.Variable.Name);
        switch (input.Variable.Type)
        {
            case SmileType.String:
                string buffer = $"smileInput{ordinal}Buffer";
                source.Append(indent).AppendLine("{");
                source.Append(indent).Append("    static unsigned char ").Append(buffer)
                    .Append('[').Append(SmileLanguage.MaximumInputLineUtf8Bytes + 1)
                    .AppendLine("] = { 0 };");
                source.Append(indent).Append("    size_t smileInputLength = _smile_input_string(")
                    .Append(buffer).Append(", ").Append(declaredName).AppendLine(");");
                source.Append(indent).Append("    ").Append(name).Append(" = (const char *)")
                    .Append(buffer).AppendLine(";");
                source.Append(indent).Append("    ").Append(exactStringLengths[input.Variable])
                    .AppendLine(" = smileInputLength;");
                source.Append(indent).AppendLine("}");
                break;

            case SmileType.Integer:
                source.Append(indent).Append(name).Append(" = _smile_input_integer(")
                    .Append(declaredName).AppendLine(");");
                break;

            case SmileType.Boolean:
                source.Append(indent).Append(name).Append(" = _smile_input_boolean(")
                    .Append(declaredName).AppendLine(");");
                break;

            default:
                throw new InvalidOperationException("Unsupported C-family INPUT target type.");
        }
    }

    private static void AppendInput(StringBuilder source, BoundProgram program)
    {
        source.AppendLine("static void _smile_input_error(int code, const char *variableName)");
        source.AppendLine("{");
        source.AppendLine("    switch (code)");
        source.AppendLine("    {");
        source.AppendLine("        case 1: fprintf(stderr, \"SMILE Runtime Error SMILER1501: Input ended before a value was received for '%s'.\\n\", variableName); break;");
        source.AppendLine($"        case 2: fprintf(stderr, \"SMILE Runtime Error SMILER1502: Input for '%s' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.\\n\", variableName); break;");
        source.AppendLine("        case 3: fprintf(stderr, \"SMILE Runtime Error SMILER1503: Input for '%s' is not a valid Integer.\\n\", variableName); break;");
        source.AppendLine("        case 4: fprintf(stderr, \"SMILE Runtime Error SMILER1504: Input for '%s' is outside the signed 64-bit Integer range.\\n\", variableName); break;");
        source.AppendLine("        case 5: fprintf(stderr, \"SMILE Runtime Error SMILER1505: Input for '%s' must be TRUE or FALSE.\\n\", variableName); break;");
        source.AppendLine("        default: fprintf(stderr, \"SMILE Runtime Error SMILER1506: Input for '%s' could not be read as valid UTF-8 text.\\n\", variableName); break;");
        source.AppendLine("    }");
        source.AppendLine("    exit(1);");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static bool _smile_valid_utf8(const unsigned char *bytes, size_t length)");
        source.AppendLine("{");
        source.AppendLine("    size_t index = 0;");
        source.AppendLine("    while (index < length)");
        source.AppendLine("    {");
        source.AppendLine("        unsigned char first = bytes[index++];");
        source.AppendLine("        if (first <= 0x7f) continue;");
        source.AppendLine("        int continuationCount;");
        source.AppendLine("        uint32_t scalar;");
        source.AppendLine("        uint32_t minimum;");
        source.AppendLine("        if ((first & 0xe0) == 0xc0) { continuationCount = 1; scalar = first & 0x1f; minimum = 0x80; }");
        source.AppendLine("        else if ((first & 0xf0) == 0xe0) { continuationCount = 2; scalar = first & 0x0f; minimum = 0x800; }");
        source.AppendLine("        else if ((first & 0xf8) == 0xf0) { continuationCount = 3; scalar = first & 0x07; minimum = 0x10000; }");
        source.AppendLine("        else return false;");
        source.AppendLine("        if (index + (size_t)continuationCount > length) return false;");
        source.AppendLine("        while (continuationCount-- > 0)");
        source.AppendLine("        {");
        source.AppendLine("            unsigned char next = bytes[index++];");
        source.AppendLine("            if ((next & 0xc0) != 0x80) return false;");
        source.AppendLine("            scalar = (scalar << 6) | (next & 0x3f);");
        source.AppendLine("        }");
        source.AppendLine("        if (scalar < minimum || scalar > 0x10ffff || (scalar >= 0xd800 && scalar <= 0xdfff)) return false;");
        source.AppendLine("    }");
        source.AppendLine("    return true;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static size_t _smile_read_line(unsigned char *buffer, const char *variableName)");
        source.AppendLine("{");
        source.AppendLine("#ifdef _WIN32");
        source.AppendLine("    static int binaryInputConfigured = 0;");
        source.AppendLine("    if (!binaryInputConfigured)");
        source.AppendLine("    {");
        source.AppendLine("        if (_setmode(_fileno(stdin), _O_BINARY) == -1) _smile_input_error(6, variableName);");
        source.AppendLine("        binaryInputConfigured = 1;");
        source.AppendLine("    }");
        source.AppendLine("#endif");
        source.AppendLine("    static bool skipLf = false;");
        source.AppendLine("    size_t length = 0;");
        source.AppendLine("    bool firstByte = true;");
        source.AppendLine("    for (;;)");
        source.AppendLine("    {");
        source.AppendLine("        int value = fgetc(stdin);");
        source.AppendLine("        if (firstByte)");
        source.AppendLine("        {");
        source.AppendLine("            firstByte = false;");
        source.AppendLine("            if (skipLf)");
        source.AppendLine("            {");
        source.AppendLine("                skipLf = false;");
        source.AppendLine("                if (value == '\\n') continue;");
        source.AppendLine("            }");
        source.AppendLine("        }");
        source.AppendLine("        if (value == EOF)");
        source.AppendLine("        {");
        source.AppendLine("            if (ferror(stdin)) _smile_input_error(6, variableName);");
        source.AppendLine("            if (length == 0) _smile_input_error(1, variableName);");
        source.AppendLine("            break;");
        source.AppendLine("        }");
        source.AppendLine("        if (value == '\\n') break;");
        source.AppendLine("        if (value == '\\r')");
        source.AppendLine("        {");
        source.AppendLine("            skipLf = true;");
        source.AppendLine("            break;");
        source.AppendLine("        }");
        source.AppendLine($"        if (length == {SmileLanguage.MaximumInputLineUtf8Bytes}) _smile_input_error(2, variableName);");
        source.AppendLine("        buffer[length++] = (unsigned char)value;");
        source.AppendLine("    }");
        source.AppendLine("    if (!_smile_valid_utf8(buffer, length)) _smile_input_error(6, variableName);");
        source.AppendLine("    buffer[length] = 0;");
        source.AppendLine("    return length;");
        source.AppendLine("}");

        if (TargetRuntimeFacts.HasInput(program, SmileType.String))
        {
            source.AppendLine();
            source.AppendLine("static size_t _smile_input_string(unsigned char *buffer, const char *variableName)");
            source.AppendLine("{");
            source.AppendLine("    return _smile_read_line(buffer, variableName);");
            source.AppendLine("}");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine();
            source.AppendLine("static int64_t _smile_input_integer(const char *variableName)");
            source.AppendLine("{");
            source.AppendLine($"    unsigned char buffer[{SmileLanguage.MaximumInputLineUtf8Bytes + 1}];");
            source.AppendLine("    size_t length = _smile_read_line(buffer, variableName);");
            source.AppendLine("    size_t first = 0;");
            source.AppendLine("    while (first < length && (buffer[first] == ' ' || buffer[first] == '\\t')) ++first;");
            source.AppendLine("    while (length > first && (buffer[length - 1] == ' ' || buffer[length - 1] == '\\t')) --length;");
            source.AppendLine("    bool negative = first < length && buffer[first] == '-';");
            source.AppendLine("    if (first < length && (buffer[first] == '+' || buffer[first] == '-')) ++first;");
            source.AppendLine("    if (first == length) _smile_input_error(3, variableName);");
            source.AppendLine("    uint64_t magnitude = 0;");
            source.AppendLine("    uint64_t limit = negative ? UINT64_C(9223372036854775808) : UINT64_C(9223372036854775807);");
            source.AppendLine("    for (; first < length; ++first)");
            source.AppendLine("    {");
            source.AppendLine("        unsigned char digit = buffer[first];");
            source.AppendLine("        if (digit < '0' || digit > '9') _smile_input_error(3, variableName);");
            source.AppendLine("        digit = (unsigned char)(digit - '0');");
            source.AppendLine("        if (magnitude > (limit - digit) / 10) _smile_input_error(4, variableName);");
            source.AppendLine("        magnitude = magnitude * 10 + digit;");
            source.AppendLine("    }");
            source.AppendLine("    if (!negative) return (int64_t)magnitude;");
            source.AppendLine("    return magnitude == UINT64_C(9223372036854775808) ? INT64_MIN : -(int64_t)magnitude;");
            source.AppendLine("}");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Boolean))
        {
            source.AppendLine();
            source.AppendLine("static bool _smile_input_boolean(const char *variableName)");
            source.AppendLine("{");
            source.AppendLine($"    unsigned char buffer[{SmileLanguage.MaximumInputLineUtf8Bytes + 1}];");
            source.AppendLine("    size_t length = _smile_read_line(buffer, variableName);");
            source.AppendLine("    size_t first = 0;");
            source.AppendLine("    while (first < length && (buffer[first] == ' ' || buffer[first] == '\\t')) ++first;");
            source.AppendLine("    while (length > first && (buffer[length - 1] == ' ' || buffer[length - 1] == '\\t')) --length;");
            source.AppendLine("    size_t count = length - first;");
            source.AppendLine("    if (count == 4 && (buffer[first] | 32) == 't' && (buffer[first + 1] | 32) == 'r' && (buffer[first + 2] | 32) == 'u' && (buffer[first + 3] | 32) == 'e') return true;");
            source.AppendLine("    if (count == 5 && (buffer[first] | 32) == 'f' && (buffer[first + 1] | 32) == 'a' && (buffer[first + 2] | 32) == 'l' && (buffer[first + 3] | 32) == 's' && (buffer[first + 4] | 32) == 'e') return false;");
            source.AppendLine("    _smile_input_error(5, variableName);");
            source.AppendLine("    return false;");
            source.AppendLine("}");
        }
    }

    private static void AppendArithmetic(
        StringBuilder source,
        BoundProgram program,
        TargetIntegerProfile integers)
    {
        // Checked helpers are part of the generated program's Integer profile.
        // Keeping their type aligned with storage and printf avoids silently
        // widening an ordinary int expression at the helper-call boundary.
        string type = integers.RequiresSigned64Storage ? "int64_t" : "int";
        string minimum = integers.RequiresSigned64Storage ? "INT64_MIN" : "INT_MIN";
        string maximum = integers.RequiresSigned64Storage ? "INT64_MAX" : "INT_MAX";
        int depth = Math.Max(
            1,
            BoundStatementTree.EnumerateExpressions(program)
                .Select(expression => CheckedArithmeticDepth(expression, 0))
                .DefaultIfEmpty(1)
                .Max());
        source.Append("static ").Append(type).Append(" _smile_arithmetic_left[")
            .Append(depth.ToString(CultureInfo.InvariantCulture))
            .AppendLine("];");
        source.Append("static ").Append(type).Append(" _smile_arithmetic_right[")
            .Append(depth.ToString(CultureInfo.InvariantCulture))
            .AppendLine("];");
        source.AppendLine("static void _smile_arithmetic_overflow(void)");
        source.AppendLine("{");
        source.AppendLine("    fputs(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\\n\", stderr);");
        source.AppendLine("    exit(1);");
        source.AppendLine("}");
        source.Append("static ").Append(type).Append(" _smile_add(").Append(type)
            .Append(" left, ").Append(type).AppendLine(" right)");
        source.AppendLine("{");
        source.Append("    if ((right > 0 && left > ").Append(maximum)
            .Append(" - right) || (right < 0 && left < ").Append(minimum)
            .AppendLine(" - right)) _smile_arithmetic_overflow();");
        source.AppendLine("    return left + right;");
        source.AppendLine("}");
        source.Append("static ").Append(type).Append(" _smile_subtract(").Append(type)
            .Append(" left, ").Append(type).AppendLine(" right)");
        source.AppendLine("{");
        source.Append("    if ((right < 0 && left > ").Append(maximum)
            .Append(" + right) || (right > 0 && left < ").Append(minimum)
            .AppendLine(" + right)) _smile_arithmetic_overflow();");
        source.AppendLine("    return left - right;");
        source.AppendLine("}");
        source.Append("static ").Append(type).Append(" _smile_multiply(").Append(type)
            .Append(" left, ").Append(type).AppendLine(" right)");
        source.AppendLine("{");
        source.AppendLine("    if (left == 0 || right == 0) return 0;");
        source.Append("    if ((left == -1 && right == ").Append(minimum)
            .Append(") || (right == -1 && left == ").Append(minimum)
            .AppendLine(")) _smile_arithmetic_overflow();");
        source.Append("    if (left > 0 ? (right > 0 ? left > ").Append(maximum)
            .Append(" / right : right < ").Append(minimum)
            .Append(" / left) : (right > 0 ? left < ").Append(minimum)
            .Append(" / right : left != 0 && right < ").Append(maximum)
            .AppendLine(" / left)) _smile_arithmetic_overflow();");
        source.AppendLine("    return left * right;");
        source.AppendLine("}");
        source.Append("static ").Append(type).Append(" _smile_negate(").Append(type)
            .AppendLine(" value)");
        source.AppendLine("{");
        source.Append("    if (value == ").Append(minimum)
            .AppendLine(") _smile_arithmetic_overflow();");
        source.AppendLine("    return -value;");
        source.AppendLine("}");
        source.Append("static ").Append(type).Append(" _smile_divide(").Append(type)
            .Append(" left, ").Append(type).AppendLine(" right)");
        source.AppendLine("{");
        source.AppendLine("    if (right == 0)");
        source.AppendLine("    {");
        source.AppendLine("        fputs(\"SMILE Runtime Error SMILER1207: Division by zero.\\n\", stderr);");
        source.AppendLine("        exit(1);");
        source.AppendLine("    }");
        source.Append("    if (left == ").Append(minimum)
            .AppendLine(" && right == -1) _smile_arithmetic_overflow();");
        source.AppendLine("    return left / right;");
        source.AppendLine("}");
    }

    private static int CheckedArithmeticDepth(BoundExpression expression, int currentDepth)
    {
        int depth = expression is BoundBinaryExpression
            ? currentDepth + 1
            : currentDepth;
        return expression switch
        {
            BoundUnaryExpression unary => CheckedArithmeticDepth(unary.Operand, depth),
            BoundBinaryExpression nested => Math.Max(
                CheckedArithmeticDepth(nested.Left, depth),
                CheckedArithmeticDepth(nested.Right, depth)),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts
                .OfType<BoundInterpolationExpressionPart>()
                .Select(part => CheckedArithmeticDepth(part.Expression, depth))
                .DefaultIfEmpty(depth)
                .Max(),
            _ => depth
        };
    }
}
