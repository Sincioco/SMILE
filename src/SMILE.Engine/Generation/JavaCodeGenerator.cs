using System.Text;

namespace SMILE.Engine;

internal sealed class JavaCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Java;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        bool hasInput = TargetRuntimeFacts.HasInput(program);
        bool checkedArithmetic = TargetRuntimeFacts.NeedsCheckedIntegerArithmetic(program);
        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer) || checkedArithmetic)
        {
            integers = new TargetIntegerProfile(RequiresSigned64Storage: true, RequiresJavaScriptBigInt: true);
        }
        bool needsConditionHelper = BoundStatementTree.Enumerate(program)
            .OfType<BoundWhileStatement>()
            .Any(loop => GeneratorConditionFacts.RequiresWarningSafeWrapper(loop.Condition));
        var source = new StringBuilder();
        if (hasInput)
        {
            source.AppendLine("import java.io.IOException;");
            source.AppendLine("import java.nio.ByteBuffer;");
            source.AppendLine("import java.nio.charset.CharacterCodingException;");
            source.AppendLine("import java.nio.charset.CodingErrorAction;");
            source.AppendLine("import java.nio.charset.StandardCharsets;");
            source.AppendLine();
        }

        source.AppendLine("public final class Program");
        source.AppendLine("{");
        if (hasInput)
        {
            source.AppendLine("    private static boolean _smile_skip_lf;");
            source.AppendLine();
        }

        source.AppendLine("    public static void main(String[] args)");
        source.AppendLine("    {");
        if (hasInput)
        {
            source.AppendLine("        System.setOut(new java.io.PrintStream(System.out, true, StandardCharsets.UTF_8));");
            source.AppendLine("        System.setErr(new java.io.PrintStream(System.err, true, StandardCharsets.UTF_8));");
            source.AppendLine();
        }

        AppendSourceItems(source, program.SourceItems, "        ", identifiers, integers, checkedArithmetic);

        source.AppendLine("    }");
        if (needsConditionHelper)
        {
            source.AppendLine();
            source.AppendLine("    // Keep source-constant WHILE conditions genuine and reachable to javac.");
            source.AppendLine("    private static boolean _smile_condition(boolean value) { return value; }");
        }

        if (hasInput)
        {
            AppendInputHelpers(source, program);
        }
        else if (checkedArithmetic)
        {
            AppendFailureHelper(source);
        }

        if (checkedArithmetic)
        {
            AppendCheckedArithmeticHelpers(source, integers);
        }

        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.java", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedArithmetic)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            switch (sourceItem)
            {
                case BoundFullLineComment comment:
                    TargetComments.Append(source, TargetLanguage.Java, indent, comment.Payload);
                    break;

                case BoundBlankLine:
                    source.AppendLine();
                    break;

                case BoundLetStatement let:
                    string initializer = TargetExpression.Java(let.Initializer, identifiers, integers, checkedArithmetic);
                    source.AppendLine($"{indent}{TargetTypes.Java(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {TargetExpression.Java(set.Value, identifiers, integers, checkedArithmetic)};");
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
                        .Append('(').Append(TargetEscapes.JavaString(input.Variable.Name))
                        .AppendLine(");");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "System.out.println();"
                        : $"System.out.println({TargetExpression.JavaDisplay(print.Value, identifiers, integers, checkedArithmetic)});");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(source, conditional, indent, identifiers, integers, checkedArithmetic);
                    break;

                case BoundWhileStatement loop:
                    AppendWhileStatement(
                        source,
                        loop,
                        indent,
                        identifiers,
                        integers,
                        checkedArithmetic);
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedArithmetic)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(TargetExpression.Java(clause.Condition, identifiers, integers, checkedArithmetic))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(source, clause.SourceItems, indent + "    ", identifiers, integers, checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(source, conditional.ElseSourceItems, indent + "    ", identifiers, integers, checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendWhileStatement(
        StringBuilder source,
        BoundWhileStatement loop,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedArithmetic)
    {
        string condition = TargetExpression.Java(
            loop.Condition,
            identifiers,
            integers,
            checkedArithmetic);
        if (GeneratorConditionFacts.RequiresWarningSafeWrapper(loop.Condition))
        {
            // A javac constant-false WHILE body is an error, and a
            // constant-true loop can make following source unreachable. A
            // normal method call preserves runtime truth without either issue.
            condition = $"_smile_condition({condition})";
        }

        source.Append(indent).Append("while (").Append(condition).AppendLine(")");
        source.Append(indent).AppendLine("{");
        AppendSourceItems(
            source,
            loop.SourceItems,
            indent + "    ",
            identifiers,
            integers,
            checkedArithmetic);
        source.Append(indent).AppendLine("}");
    }

    private static void AppendInputHelpers(StringBuilder source, BoundProgram program)
    {
        source.AppendLine();
        source.AppendLine("    private static int _smile_read_byte(String variableName)");
        source.AppendLine("    {");
        source.AppendLine("        try");
        source.AppendLine("        {");
        source.AppendLine("            return System.in.read();");
        source.AppendLine("        }");
        source.AppendLine("        catch (IOException exception)");
        source.AppendLine("        {");
        source.AppendLine("            _smile_fail(\"SMILE Runtime Error SMILER1506: Input for '\" + variableName + \"' could not be read as valid UTF-8 text.\");");
        source.AppendLine("            return -1;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    private static String _smile_read_line(String variableName)");
        source.AppendLine("    {");
        source.AppendLine($"        byte[] bytes = new byte[{SmileLanguage.MaximumInputLineUtf8Bytes}];");
        source.AppendLine("        int count = 0;");
        source.AppendLine("        boolean firstByte = true;");
        source.AppendLine("        while (true)");
        source.AppendLine("        {");
        source.AppendLine("            int next = _smile_read_byte(variableName);");
        source.AppendLine("            if (firstByte)");
        source.AppendLine("            {");
        source.AppendLine("                firstByte = false;");
        source.AppendLine("                if (_smile_skip_lf)");
        source.AppendLine("                {");
        source.AppendLine("                    _smile_skip_lf = false;");
        source.AppendLine("                    if (next == '\\n') continue;");
        source.AppendLine("                }");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            if (next < 0)");
        source.AppendLine("            {");
        source.AppendLine("                if (count == 0)");
        source.AppendLine("                {");
        source.AppendLine("                    _smile_fail(\"SMILE Runtime Error SMILER1501: Input ended before a value was received for '\" + variableName + \"'.\");");
        source.AppendLine("                    return \"\";");
        source.AppendLine("                }");
        source.AppendLine();
        source.AppendLine("                break;");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            if (next == '\\n') break;");
        source.AppendLine("            if (next == '\\r')");
        source.AppendLine("            {");
        source.AppendLine("                _smile_skip_lf = true;");
        source.AppendLine("                break;");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine($"            if (count == {SmileLanguage.MaximumInputLineUtf8Bytes})");
        source.AppendLine("            {");
        source.AppendLine($"                _smile_fail(\"SMILE Runtime Error SMILER1502: Input for '\" + variableName + \"' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.\");");
        source.AppendLine("                return \"\";");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            bytes[count++] = (byte)next;");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        try");
        source.AppendLine("        {");
        source.AppendLine("            return StandardCharsets.UTF_8.newDecoder()");
        source.AppendLine("                .onMalformedInput(CodingErrorAction.REPORT)");
        source.AppendLine("                .onUnmappableCharacter(CodingErrorAction.REPORT)");
        source.AppendLine("                .decode(ByteBuffer.wrap(bytes, 0, count)).toString();");
        source.AppendLine("        }");
        source.AppendLine("        catch (CharacterCodingException exception)");
        source.AppendLine("        {");
        source.AppendLine("            _smile_fail(\"SMILE Runtime Error SMILER1506: Input for '\" + variableName + \"' could not be read as valid UTF-8 text.\");");
        source.AppendLine("            return \"\";");
        source.AppendLine("        }");
        source.AppendLine("    }");

        if (TargetRuntimeFacts.HasInput(program, SmileType.String))
        {
            source.AppendLine();
            source.AppendLine("    private static String _smile_input_string(String variableName)");
            source.AppendLine("    {");
            source.AppendLine("        return _smile_read_line(variableName);");
            source.AppendLine("    }");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine();
            source.AppendLine("    private static long _smile_input_integer(String variableName)");
            source.AppendLine("    {");
            source.AppendLine("        String text = _smile_read_line(variableName).replaceAll(\"^[ \\t]+|[ \\t]+$\", \"\");");
            source.AppendLine("        if (!text.matches(\"[+-]?[0-9]+\"))");
            source.AppendLine("        {");
            source.AppendLine("            _smile_fail(\"SMILE Runtime Error SMILER1503: Input for '\" + variableName + \"' is not a valid Integer.\");");
            source.AppendLine("            return 0;");
            source.AppendLine("        }");
            source.AppendLine("        try");
            source.AppendLine("        {");
            source.AppendLine("            return Long.parseLong(text);");
            source.AppendLine("        }");
            source.AppendLine("        catch (NumberFormatException exception)");
            source.AppendLine("        {");
            source.AppendLine("            _smile_fail(\"SMILE Runtime Error SMILER1504: Input for '\" + variableName + \"' is outside the signed 64-bit Integer range.\");");
            source.AppendLine("            return 0;");
            source.AppendLine("        }");
            source.AppendLine("    }");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Boolean))
        {
            source.AppendLine();
            source.AppendLine("    private static boolean _smile_ascii_equals(String text, String expected)");
            source.AppendLine("    {");
            source.AppendLine("        if (text.length() != expected.length()) return false;");
            source.AppendLine("        for (int index = 0; index < text.length(); index++)");
            source.AppendLine("        {");
            source.AppendLine("            char actual = text.charAt(index);");
            source.AppendLine("            char upper = expected.charAt(index);");
            source.AppendLine("            if (actual != upper && actual != upper + ('a' - 'A')) return false;");
            source.AppendLine("        }");
            source.AppendLine("        return true;");
            source.AppendLine("    }");
            source.AppendLine();
            source.AppendLine("    private static boolean _smile_input_boolean(String variableName)");
            source.AppendLine("    {");
            source.AppendLine("        String text = _smile_read_line(variableName).replaceAll(\"^[ \\t]+|[ \\t]+$\", \"\");");
            source.AppendLine("        if (_smile_ascii_equals(text, \"TRUE\")) return true;");
            source.AppendLine("        if (_smile_ascii_equals(text, \"FALSE\")) return false;");
            source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1505: Input for '\" + variableName + \"' must be TRUE or FALSE.\");");
            source.AppendLine("        return false;");
            source.AppendLine("    }");
        }

        AppendFailureHelper(source);
    }

    private static void AppendFailureHelper(StringBuilder source)
    {
        source.AppendLine();
        source.AppendLine("    private static void _smile_fail(String message)");
        source.AppendLine("    {");
        source.AppendLine("        System.err.println(message);");
        source.AppendLine("        System.exit(1);");
        source.AppendLine("    }");
    }

    private static void AppendCheckedArithmeticHelpers(StringBuilder source, TargetIntegerProfile integers)
    {
        string type = integers.RequiresSigned64Storage ? "long" : "int";
        string wrapper = integers.RequiresSigned64Storage ? "Long" : "Integer";
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_add({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine($"        try {{ return Math.addExact(left, right); }} catch (ArithmeticException exception) {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }}");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_subtract({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine($"        try {{ return Math.subtractExact(left, right); }} catch (ArithmeticException exception) {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }}");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_multiply({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine($"        try {{ return Math.multiplyExact(left, right); }} catch (ArithmeticException exception) {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }}");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_negate({type} value)");
        source.AppendLine("    {");
        source.AppendLine($"        try {{ return Math.negateExact(value); }} catch (ArithmeticException exception) {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }}");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_divide({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        if (right == 0) { _smile_fail(\"SMILE Runtime Error SMILER1207: Division by zero.\"); return 0; }");
        source.AppendLine($"        if (left == {wrapper}.MIN_VALUE && right == -1) {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }}");
        source.AppendLine("        return left / right;");
        source.AppendLine("    }");
    }
}
