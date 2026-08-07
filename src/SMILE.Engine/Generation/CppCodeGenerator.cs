using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class CppCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Cpp;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        bool hasInput = TargetRuntimeFacts.HasInput(program);
        bool hasIntegerInput = TargetRuntimeFacts.HasInput(program, SmileType.Integer);
        bool checkedArithmetic = TargetRuntimeFacts.NeedsCheckedIntegerArithmetic(program);
        if (hasIntegerInput || checkedArithmetic)
        {
            integers = new TargetIntegerProfile(RequiresSigned64Storage: true, RequiresJavaScriptBigInt: true);
        }
        var expressions = new CppExpressionWriter(identifiers, integers, checkedArithmetic);
        var source = new StringBuilder();

        bool needsIostream = hasInput || checkedArithmetic || BoundStatementTree.Enumerate(program)
            .Any(statement => statement is BoundPrintStatement);
        bool needsString = CppGenerationFacts.NeedsStringHeader(program) || hasInput || checkedArithmetic;

        if (needsIostream)
        {
            source.AppendLine("#include <iostream>");
        }

        if (needsString)
        {
            source.AppendLine("#include <string>");
        }

        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <cstdint>");
        }

        if (hasInput || checkedArithmetic)
        {
            source.AppendLine("#include <cstdlib>");
        }

        if (hasInput)
        {
            if (hasIntegerInput)
            {
                source.AppendLine("#include <charconv>");
                source.AppendLine("#include <system_error>");
            }

            source.AppendLine("#ifdef _WIN32");
            source.AppendLine("#include <fcntl.h>");
            source.AppendLine("#include <io.h>");
            source.AppendLine("#endif");
        }

        if (needsIostream || needsString || integers.RequiresSigned64Storage || hasInput || checkedArithmetic)
        {
            source.AppendLine();
        }

        if (hasInput)
        {
            AppendInputHelpers(source, program);
            source.AppendLine();
        }
        else if (checkedArithmetic)
        {
            AppendFailureHelper(source);
            source.AppendLine();
        }

        if (checkedArithmetic)
        {
            AppendCheckedArithmeticHelpers(source);
            source.AppendLine();
        }

        source.AppendLine("int main()");
        source.AppendLine("{");

        bool emittedDeclaration = false;
        bool emittedExecutable = false;
        AppendSourceItems(
            source,
            program.SourceItems,
            "    ",
            expressions,
            identifiers,
            integers,
            ref emittedDeclaration,
            ref emittedExecutable);

        if (program.Statements.Count > 0)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[]
            {
                new GeneratedFile(
                    "Program.cpp",
                    TextOutput.EnsureOneTrailingNewLine(source.ToString()),
                    IsPrimary: true)
            });
    }

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        CppExpressionWriter expressions,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            switch (sourceItem)
            {
                case BoundFullLineComment comment:
                    TargetComments.Append(source, TargetLanguage.Cpp, indent, comment.Payload);
                    break;

                case BoundBlankLine:
                    source.AppendLine();
                    break;

                case BoundLetStatement let:
                    source.AppendLine(
                        $"{indent}{TargetTypes.Cpp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {expressions.Write(let.Initializer)};");
                    emittedDeclaration = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {expressions.Write(set.Value)};");
                    emittedExecutable = true;
                    break;

                case BoundInputStatement input:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    source.Append(indent).Append(identifiers.Get(input.Variable)).Append(" = ")
                        .Append(input.Variable.Type switch
                        {
                            SmileType.String => "_smile_input_string",
                            SmileType.Integer => "_smile_input_integer",
                            SmileType.Boolean => "_smile_input_boolean",
                            _ => throw new InvalidOperationException("Unsupported INPUT target type.")
                        })
                        .Append('(').Append(TargetEscapes.CString(input.Variable.Name))
                        .AppendLine(");");
                    emittedExecutable = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendPrint(source, indent, print, expressions);
                    emittedExecutable = true;
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        expressions,
                        identifiers,
                        integers,
                        ref emittedDeclaration,
                        ref emittedExecutable);
                    emittedExecutable = true;
                    break;

                case BoundWhileStatement loop:
                    AppendWhileStatement(
                        source,
                        loop,
                        indent,
                        expressions,
                        identifiers,
                        integers,
                        ref emittedDeclaration,
                        ref emittedExecutable);
                    emittedExecutable = true;
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        CppExpressionWriter expressions,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(expressions.Write(clause.Condition))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(
                source,
                clause.SourceItems,
                indent + "    ",
                expressions,
                identifiers,
                integers,
                ref emittedDeclaration,
                ref emittedExecutable);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(
                source,
                conditional.ElseSourceItems,
                indent + "    ",
                expressions,
                identifiers,
                integers,
                ref emittedDeclaration,
                ref emittedExecutable);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendWhileStatement(
        StringBuilder source,
        BoundWhileStatement loop,
        string indent,
        CppExpressionWriter expressions,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable)
    {
        source.Append(indent).Append("while (")
            .Append(expressions.Write(loop.Condition)).AppendLine(")");
        source.Append(indent).AppendLine("{");
        AppendSourceItems(
            source,
            loop.SourceItems,
            indent + "    ",
            expressions,
            identifiers,
            integers,
            ref emittedDeclaration,
            ref emittedExecutable);
        source.Append(indent).AppendLine("}");
    }

    private static void AppendPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        CppExpressionWriter expressions)
    {
        source.Append(indent).Append("std::cout");

        if (print.IsBlankLine)
        {
            source.AppendLine(" << '\\n';");
            return;
        }

        if (print.Value is BoundInterpolatedStringExpression &&
            TargetRuntimeFacts.ContainsIntegerArithmetic(print.Value))
        {
            // Build the complete value before the first stream insertion. If a
            // later hole fails, this PRINT remains atomic just like the evaluator.
            source.Append(" << ");
            source.Append(expressions.Write(print.Value));
        }
        else if (print.Value is BoundInterpolatedStringExpression interpolated)
        {
            bool emittedPart = false;
            foreach (BoundInterpolatedPart part in interpolated.Parts)
            {
                string? text = part switch
                {
                    BoundInterpolatedTextPart literal when literal.Text.Length > 0 =>
                        expressions.WriteStringLiteral(literal.Text),
                    BoundInterpolationExpressionPart hole => expressions.WriteForStream(hole.Expression),
                    _ => null
                };

                if (text is not null)
                {
                    source.Append(" << ");
                    source.Append(text);
                    emittedPart = true;
                }
            }

            if (!emittedPart)
            {
                source.Append(" << \"\"");
            }
        }
        else
        {
            source.Append(" << ");
            source.Append(expressions.WriteForStream(print.Value));
        }

        source.AppendLine(" << '\\n';");
    }

    private static void AppendInputHelpers(StringBuilder source, BoundProgram program)
    {
        AppendFailureHelper(source);
        source.AppendLine();
        source.AppendLine("bool _smile_skip_lf = false;");
        source.AppendLine();
        source.AppendLine("int _smile_next_byte()");
        source.AppendLine("{");
        source.AppendLine("    return std::cin.get();");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("bool _smile_valid_utf8(const std::string& text)");
        source.AppendLine("{");
        source.AppendLine("    const auto* bytes = reinterpret_cast<const unsigned char*>(text.data());");
        source.AppendLine("    std::size_t index = 0;");
        source.AppendLine("    while (index < text.size())");
        source.AppendLine("    {");
        source.AppendLine("        const unsigned char first = bytes[index++];");
        source.AppendLine("        if (first <= 0x7f) continue;");
        source.AppendLine("        int continuationCount;");
        source.AppendLine("        unsigned int scalar;");
        source.AppendLine("        unsigned int minimum;");
        source.AppendLine("        if ((first & 0xe0) == 0xc0) { continuationCount = 1; scalar = first & 0x1f; minimum = 0x80; }");
        source.AppendLine("        else if ((first & 0xf0) == 0xe0) { continuationCount = 2; scalar = first & 0x0f; minimum = 0x800; }");
        source.AppendLine("        else if ((first & 0xf8) == 0xf0) { continuationCount = 3; scalar = first & 0x07; minimum = 0x10000; }");
        source.AppendLine("        else return false;");
        source.AppendLine("        if (index + static_cast<std::size_t>(continuationCount) > text.size()) return false;");
        source.AppendLine("        for (int count = 0; count < continuationCount; ++count)");
        source.AppendLine("        {");
        source.AppendLine("            const unsigned char next = bytes[index++];");
        source.AppendLine("            if ((next & 0xc0) != 0x80) return false;");
        source.AppendLine("            scalar = (scalar << 6) | (next & 0x3f);");
        source.AppendLine("        }");
        source.AppendLine("        if (scalar < minimum || scalar > 0x10ffff || (scalar >= 0xd800 && scalar <= 0xdfff)) return false;");
        source.AppendLine("    }");
        source.AppendLine("    return true;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("std::string _smile_read_line(const std::string& variableName)");
        source.AppendLine("{");
        source.AppendLine("#ifdef _WIN32");
        source.AppendLine("    static bool binaryInputConfigured = false;");
        source.AppendLine("    if (!binaryInputConfigured)");
        source.AppendLine("    {");
        source.AppendLine("        if (_setmode(_fileno(stdin), _O_BINARY) == -1) _smile_fail(\"SMILE Runtime Error SMILER1506: Input for '\" + variableName + \"' could not be read as valid UTF-8 text.\");");
        source.AppendLine("        binaryInputConfigured = true;");
        source.AppendLine("    }");
        source.AppendLine("#endif");
        source.AppendLine("    std::string value;");
        source.AppendLine("    bool firstByte = true;");
        source.AppendLine("    while (true)");
        source.AppendLine("    {");
        source.AppendLine("        const int next = _smile_next_byte();");
        source.AppendLine("        if (firstByte)");
        source.AppendLine("        {");
        source.AppendLine("            firstByte = false;");
        source.AppendLine("            if (_smile_skip_lf)");
        source.AppendLine("            {");
        source.AppendLine("                _smile_skip_lf = false;");
        source.AppendLine("                if (next == '\\n') continue;");
        source.AppendLine("            }");
        source.AppendLine("        }");
        source.AppendLine("        if (next == std::char_traits<char>::eof())");
        source.AppendLine("        {");
        source.AppendLine("            if (std::cin.bad()) _smile_fail(\"SMILE Runtime Error SMILER1506: Input for '\" + variableName + \"' could not be read as valid UTF-8 text.\");");
        source.AppendLine("            if (value.empty()) _smile_fail(\"SMILE Runtime Error SMILER1501: Input ended before a value was received for '\" + variableName + \"'.\");");
        source.AppendLine("            break;");
        source.AppendLine("        }");
        source.AppendLine("        if (next == '\\n') break;");
        source.AppendLine("        if (next == '\\r')");
        source.AppendLine("        {");
        source.AppendLine("            _smile_skip_lf = true;");
        source.AppendLine("            break;");
        source.AppendLine("        }");
        source.AppendLine("        value.push_back(static_cast<char>(next));");
        source.AppendLine($"        if (value.size() > {SmileLanguage.MaximumInputLineUtf8Bytes}) _smile_fail(\"SMILE Runtime Error SMILER1502: Input for '\" + variableName + \"' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.\");");
        source.AppendLine("    }");
        source.AppendLine("    if (!_smile_valid_utf8(value)) _smile_fail(\"SMILE Runtime Error SMILER1506: Input for '\" + variableName + \"' could not be read as valid UTF-8 text.\");");
        source.AppendLine("    return value;");
        source.AppendLine("}");

        if (TargetRuntimeFacts.HasInput(program, SmileType.String))
        {
            source.AppendLine();
            source.AppendLine("std::string _smile_input_string(const std::string& variableName)");
            source.AppendLine("{");
            source.AppendLine("    return _smile_read_line(variableName);");
            source.AppendLine("}");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine();
            source.AppendLine("std::int64_t _smile_input_integer(const std::string& variableName)");
            source.AppendLine("{");
            source.AppendLine("    std::string text = _smile_read_line(variableName);");
            source.AppendLine("    const std::size_t first = text.find_first_not_of(\" \\t\");");
            source.AppendLine("    const std::size_t last = text.find_last_not_of(\" \\t\");");
            source.AppendLine("    text = first == std::string::npos ? std::string{} : text.substr(first, last - first + 1);");
            source.AppendLine("    std::size_t digitStart = !text.empty() && (text[0] == '+' || text[0] == '-') ? 1 : 0;");
            source.AppendLine("    bool valid = digitStart < text.size();");
            source.AppendLine("    for (std::size_t index = digitStart; valid && index < text.size(); ++index) valid = text[index] >= '0' && text[index] <= '9';");
            source.AppendLine("    if (!valid) _smile_fail(\"SMILE Runtime Error SMILER1503: Input for '\" + variableName + \"' is not a valid Integer.\");");
            source.AppendLine("    std::int64_t value = 0;");
            source.AppendLine("    const char* begin = text.data();");
            source.AppendLine("    if (*begin == '+') ++begin;");
            source.AppendLine("    const auto result = std::from_chars(begin, text.data() + text.size(), value, 10);");
            source.AppendLine("    if (result.ec == std::errc::result_out_of_range) _smile_fail(\"SMILE Runtime Error SMILER1504: Input for '\" + variableName + \"' is outside the signed 64-bit Integer range.\");");
            source.AppendLine("    if (result.ec != std::errc{} || result.ptr != text.data() + text.size()) _smile_fail(\"SMILE Runtime Error SMILER1503: Input for '\" + variableName + \"' is not a valid Integer.\");");
            source.AppendLine("    return value;");
            source.AppendLine("}");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Boolean))
        {
            source.AppendLine();
            source.AppendLine("bool _smile_input_boolean(const std::string& variableName)");
            source.AppendLine("{");
            source.AppendLine("    std::string text = _smile_read_line(variableName);");
            source.AppendLine("    const std::size_t first = text.find_first_not_of(\" \\t\");");
            source.AppendLine("    const std::size_t last = text.find_last_not_of(\" \\t\");");
            source.AppendLine("    text = first == std::string::npos ? std::string{} : text.substr(first, last - first + 1);");
            source.AppendLine("    for (char& value : text) if (value >= 'a' && value <= 'z') value = static_cast<char>(value - 'a' + 'A');");
            source.AppendLine("    if (text == \"TRUE\") return true;");
            source.AppendLine("    if (text == \"FALSE\") return false;");
            source.AppendLine("    _smile_fail(\"SMILE Runtime Error SMILER1505: Input for '\" + variableName + \"' must be TRUE or FALSE.\");");
            source.AppendLine("}");
        }
    }

    private static void AppendFailureHelper(StringBuilder source)
    {
        source.AppendLine("[[noreturn]] void _smile_fail(const std::string& message)");
        source.AppendLine("{");
        source.AppendLine("    std::cout.flush();");
        source.AppendLine("    std::cerr << message << '\\n';");
        source.AppendLine("    std::exit(1);");
        source.AppendLine("}");
    }

    private static void AppendCheckedArithmeticHelpers(StringBuilder source)
    {
        source.AppendLine("std::int64_t _smile_add(std::int64_t left, std::int64_t right)");
        source.AppendLine("{");
        source.AppendLine("    if ((right > 0 && left > INT64_MAX - right) || (right < 0 && left < INT64_MIN - right)) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    return left + right;");
        source.AppendLine("}");
        source.AppendLine("std::int64_t _smile_subtract(std::int64_t left, std::int64_t right)");
        source.AppendLine("{");
        source.AppendLine("    if ((right < 0 && left > INT64_MAX + right) || (right > 0 && left < INT64_MIN + right)) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    return left - right;");
        source.AppendLine("}");
        source.AppendLine("std::int64_t _smile_multiply(std::int64_t left, std::int64_t right)");
        source.AppendLine("{");
        source.AppendLine("    if (left == 0 || right == 0) return 0;");
        source.AppendLine("    if ((left == -1 && right == INT64_MIN) || (right == -1 && left == INT64_MIN)) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    if (left > 0 ? (right > 0 ? left > INT64_MAX / right : right < INT64_MIN / left) : (right > 0 ? left < INT64_MIN / right : left != 0 && right < INT64_MAX / left)) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    return left * right;");
        source.AppendLine("}");
        source.AppendLine("std::int64_t _smile_negate(std::int64_t value)");
        source.AppendLine("{");
        source.AppendLine("    if (value == INT64_MIN) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    return -value;");
        source.AppendLine("}");
        source.AppendLine("std::int64_t _smile_divide(std::int64_t left, std::int64_t right)");
        source.AppendLine("{");
        source.AppendLine("    if (right == 0) _smile_fail(\"SMILE Runtime Error SMILER1207: Division by zero.\");");
        source.AppendLine("    if (left == INT64_MIN && right == -1) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    return left / right;");
        source.AppendLine("}");
    }
}

internal static class CppGenerationFacts
{
    public static bool NeedsStringHeader(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let =>
                let.Variable.Type is SmileType.String || ContainsStringFacility(let.Initializer),
            BoundSetStatement set => ContainsStringFacility(set.Value),
            BoundPrintStatement print when !print.IsBlankLine =>
                ContainsDirectStreamStringFacility(print.Value),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                ContainsStringFacility(clause.Condition)),
            BoundWhileStatement loop => ContainsStringFacility(loop.Condition),
            _ => false
        });

    private static bool ContainsDirectStreamStringFacility(BoundExpression expression) =>
        expression is BoundInterpolatedStringExpression interpolated
            ? interpolated.Parts.Any(part => part switch
            {
                BoundInterpolatedTextPart text => text.Text.Contains('\0', StringComparison.Ordinal),
                BoundInterpolationExpressionPart hole => ContainsStringFacility(hole.Expression),
                _ => false
            })
            : ContainsStringFacility(expression);

    private static bool ContainsStringFacility(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            BoundVariableExpression variable => variable.Variable.Type is SmileType.String,
            BoundUnaryExpression unary => ContainsStringFacility(unary.Operand),
            BoundBinaryExpression binary =>
                binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation ||
                (binary.Left.Type is SmileType.String &&
                    binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) ||
                ContainsStringFacility(binary.Left) ||
                ContainsStringFacility(binary.Right),
            BoundInterpolatedStringExpression => true,
            _ => false
        };
}

internal sealed class CppExpressionWriter
{
    private readonly TargetIdentifierMap _identifiers;
    private readonly TargetIntegerProfile _integers;
    private readonly bool _checkedRuntimeArithmetic;

    public CppExpressionWriter(
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedRuntimeArithmetic = false)
    {
        _identifiers = identifiers;
        _integers = integers;
        _checkedRuntimeArithmetic = checkedRuntimeArithmetic;
    }

    public string Write(BoundExpression expression) =>
        WriteExpression(expression, parentPrecedence: 0, isRightChild: false, parentOperator: null);

    public string WriteForStream(BoundExpression expression) =>
        expression.Type is SmileType.Boolean
            ? $"({Write(expression)} ? \"TRUE\" : \"FALSE\")"
            : Write(expression);

    public string WriteStringLiteral(string value) =>
        value.Contains('\0', StringComparison.Ordinal)
            ? $"std::string{{{TargetEscapes.CString(value)}, {Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture)}}}"
            : TargetEscapes.CString(value);

    private string WriteExpression(
        BoundExpression expression,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator) =>
        expression switch
        {
            BoundStringLiteralExpression literal => WriteStringLiteral(literal.Value),
            BoundIntegerLiteralExpression literal => IntegerLiteral(literal.Value),
            BoundBooleanLiteralExpression literal => literal.Value ? "true" : "false",
            BoundVariableExpression variable => _identifiers.Get(variable.Variable),
            BoundUnaryExpression unary => WriteUnary(unary, parentPrecedence),
            BoundBinaryExpression binary => WriteBinary(binary, parentPrecedence, isRightChild, parentOperator),
            BoundInterpolatedStringExpression interpolated => WriteInterpolatedString(interpolated),
            _ => "std::string{}"
        };

    private string WriteUnary(BoundUnaryExpression expression, int parentPrecedence)
    {
        if (_checkedRuntimeArithmetic &&
            expression.Operator.Kind is BoundUnaryOperatorKind.Negation &&
            expression.Operand.Type is SmileType.Integer)
        {
            string call = "_smile_negate(" +
                WriteExpression(expression.Operand, 0, isRightChild: false, parentOperator: null) +
                ")";
            return parentPrecedence > 7 ? "(" + call + ")" : call;
        }

        const int precedence = 7;
        string op = expression.Operator.Kind switch
        {
            BoundUnaryOperatorKind.Identity => "+",
            BoundUnaryOperatorKind.Negation => "-",
            BoundUnaryOperatorKind.LogicalNegation => "!",
            _ => string.Empty
        };
        string operand = WriteExpression(expression.Operand, precedence, isRightChild: true, parentOperator: null);
        string text = op + operand;
        return precedence < parentPrecedence ? "(" + text + ")" : text;
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
                BoundBinaryOperatorKind.Multiplication or
                BoundBinaryOperatorKind.Division)
        {
            string helper = expression.Operator.Kind switch
            {
                BoundBinaryOperatorKind.Addition => "_smile_add",
                BoundBinaryOperatorKind.Subtraction => "_smile_subtract",
                BoundBinaryOperatorKind.Multiplication => "_smile_multiply",
                BoundBinaryOperatorKind.Division => "_smile_divide",
                _ => throw new InvalidOperationException("Unsupported checked C++ Integer operator.")
            };
            string leftValue = WriteExpression(
                expression.Left,
                0,
                isRightChild: false,
                parentOperator: null);
            string rightValue = WriteExpression(
                expression.Right,
                0,
                isRightChild: false,
                parentOperator: null);
            // C++ does not promise which function argument is evaluated first.
            // A tiny immediately-invoked lambda turns the two operand reads
            // into sequenced statements so the first reached SMILE runtime
            // failure is always the source-left failure.
            string call = "([&]() { const std::int64_t _smile_arithmetic_left_value = " +
                leftValue + "; const std::int64_t _smile_arithmetic_right_value = " +
                rightValue + "; return " + helper + "(_smile_arithmetic_left_value, " +
                "_smile_arithmetic_right_value); }())";
            return parentPrecedence > 7 ? "(" + call + ")" : call;
        }

        if (_checkedRuntimeArithmetic &&
            expression.Operator.Kind is not (
                BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr) &&
            (ContainsCheckedArithmetic(expression.Left) || ContainsCheckedArithmetic(expression.Right)))
        {
            string valueType = expression.Left.Type switch
            {
                SmileType.Integer => "std::int64_t",
                SmileType.Boolean => "bool",
                SmileType.String => "std::string",
                _ => "auto"
            };
            string sequencedLeft = WriteExpression(
                expression.Left,
                0,
                isRightChild: false,
                parentOperator: null);
            string sequencedRight = WriteExpression(
                expression.Right,
                0,
                isRightChild: false,
                parentOperator: null);
            string sequenced = "([&]() { const " + valueType +
                " _smile_expression_left_value = " + sequencedLeft + "; const " +
                valueType + " _smile_expression_right_value = " + sequencedRight +
                "; return _smile_expression_left_value " +
                OperatorText(expression.Operator.Kind) +
                " _smile_expression_right_value; }())";
            return parentPrecedence > 7 ? "(" + sequenced + ")" : sequenced;
        }

        int precedence = Precedence(expression.Operator.Kind);
        string left = WriteExpression(expression.Left, precedence, isRightChild: false, expression.Operator.Kind);
        string right = WriteExpression(expression.Right, precedence, isRightChild: true, expression.Operator.Kind);

        if (expression.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation &&
            !ProducesOwnedString(expression.Left))
        {
            // Two C++ string literals cannot be added because both decay to
            // pointers. Starting the chain with an owned std::string keeps the
            // source natural while making every legal SMILE concatenation valid.
            left = "std::string{" + left + "}";
        }

        if (expression.Left.Type is SmileType.String &&
            expression.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality &&
            !ProducesOwnedString(expression.Left))
        {
            // std::string equality is length-aware, including embedded NUL.
            // Owning a literal left operand also avoids pointer comparison.
            left = "std::string{" + left + "}";
        }

        string text = left + " " + OperatorText(expression.Operator.Kind) + " " + right;
        return NeedsParentheses(precedence, parentPrecedence, isRightChild, parentOperator)
            ? "(" + text + ")"
            : text;
    }

    private string WriteInterpolatedString(BoundInterpolatedStringExpression expression)
    {
        var segments = new List<(string Text, bool IsOwned)>();

        foreach (BoundInterpolatedPart part in expression.Parts)
        {
            switch (part)
            {
                case BoundInterpolatedTextPart text when text.Text.Length > 0:
                    segments.Add((WriteStringLiteral(text.Text), text.Text.Contains('\0', StringComparison.Ordinal)));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.String:
                    segments.Add((Write(hole.Expression), ProducesOwnedString(hole.Expression)));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.Integer:
                    segments.Add(($"std::to_string({Write(hole.Expression)})", true));
                    break;

                case BoundInterpolationExpressionPart hole when hole.Expression.Type is SmileType.Boolean:
                    segments.Add(($"({Write(hole.Expression)} ? \"TRUE\" : \"FALSE\")", false));
                    break;
            }
        }

        if (segments.Count == 0)
        {
            return "std::string{}";
        }

        if (_checkedRuntimeArithmetic && expression.Parts
                .OfType<BoundInterpolationExpressionPart>()
                .Any(part => ContainsCheckedArithmetic(part.Expression)))
        {
            return "([&]() { std::string _smile_text_result{}; " +
                string.Concat(segments.Select(segment =>
                    "_smile_text_result += " + segment.Text + "; ")) +
                "return _smile_text_result; }())";
        }

        if (!segments[0].IsOwned)
        {
            segments[0] = ("std::string{" + segments[0].Text + "}", true);
        }

        return string.Join(" + ", segments.Select(segment => segment.Text));
    }

    private string IntegerLiteral(long value)
    {
        if (!_integers.RequiresSigned64Storage)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (value == long.MinValue)
        {
            return "INT64_MIN";
        }

        return value < 0
            ? "-INT64_C(" + (-value).ToString(CultureInfo.InvariantCulture) + ")"
            : "INT64_C(" + value.ToString(CultureInfo.InvariantCulture) + ")";
    }

    private static bool ProducesOwnedString(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression literal => literal.Value.Contains('\0', StringComparison.Ordinal),
            BoundVariableExpression variable => variable.Variable.Type is SmileType.String,
            BoundBinaryExpression binary => binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation,
            BoundInterpolatedStringExpression => true,
            _ => false
        };

    private static bool ContainsCheckedArithmetic(BoundExpression expression) =>
        expression switch
        {
            BoundUnaryExpression unary =>
                (unary.Operator.Kind is BoundUnaryOperatorKind.Negation &&
                 unary.Operand.Type is SmileType.Integer) ||
                ContainsCheckedArithmetic(unary.Operand),
            BoundBinaryExpression binary =>
                (binary.Left.Type is SmileType.Integer &&
                 binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                     BoundBinaryOperatorKind.Subtraction or
                     BoundBinaryOperatorKind.Multiplication or
                     BoundBinaryOperatorKind.Division) ||
                ContainsCheckedArithmetic(binary.Left) ||
                ContainsCheckedArithmetic(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts
                .OfType<BoundInterpolationExpressionPart>()
                .Any(part => ContainsCheckedArithmetic(part.Expression)),
            _ => false
        };

    private static string OperatorText(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.StringConcatenation => "+",
            BoundBinaryOperatorKind.Subtraction => "-",
            BoundBinaryOperatorKind.Multiplication => "*",
            BoundBinaryOperatorKind.Division => "/",
            BoundBinaryOperatorKind.Equality => "==",
            BoundBinaryOperatorKind.Inequality => "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => "&&",
            BoundBinaryOperatorKind.LogicalOr => "||",
            _ => string.Empty
        };

    private static int Precedence(BoundBinaryOperatorKind kind) =>
        kind switch
        {
            BoundBinaryOperatorKind.Multiplication or BoundBinaryOperatorKind.Division => 6,
            BoundBinaryOperatorKind.Addition or BoundBinaryOperatorKind.Subtraction or
                BoundBinaryOperatorKind.StringConcatenation => 5,
            BoundBinaryOperatorKind.Less or BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or BoundBinaryOperatorKind.GreaterOrEquals => 4,
            BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality => 3,
            BoundBinaryOperatorKind.LogicalAnd => 2,
            BoundBinaryOperatorKind.LogicalOr => 1,
            _ => 0
        };

    private static bool NeedsParentheses(
        int precedence,
        int parentPrecedence,
        bool isRightChild,
        BoundBinaryOperatorKind? parentOperator)
    {
        if (precedence < parentPrecedence)
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
}
