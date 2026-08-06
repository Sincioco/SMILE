using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class CSharpCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.CSharp;

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
            .OfType<BoundIfStatement>()
            .SelectMany(conditional => conditional.Clauses)
            .Any(clause => GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition));
        var source = new StringBuilder();
        source.AppendLine("using System;");
        if (CSharpGenerationFacts.NeedsInvariantCulture(program) ||
            TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine("using System.Globalization;");
        }

        if (hasInput)
        {
            source.AppendLine("using System.Text;");
        }

        source.AppendLine();
        source.AppendLine("internal static class Program");
        source.AppendLine("{");
        source.AppendLine("    private static void Main()");
        source.AppendLine("    {");

        if (hasInput)
        {
            source.AppendLine("        Console.OutputEncoding = new UTF8Encoding(false);");
        }

        AppendSourceItems(
            source,
            program.SourceItems,
            "        ",
            identifiers,
            integers,
            needsConditionHelper,
            checkedArithmetic);

        source.AppendLine("    }");
        if (needsConditionHelper)
        {
            source.AppendLine();
            source.AppendLine("    // Keep a valid source-constant IF as genuine control flow without CS0162.");
            source.AppendLine("    private static bool _smile_condition(bool value) => value;");
        }

        if (hasInput)
        {
            AppendInputHelpers(source, program);
        }

        if (checkedArithmetic)
        {
            AppendCheckedArithmeticHelpers(source, integers);
        }

        source.AppendLine("}");

        const string project = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";

        return new GeneratedProgram(
            Language,
            new[]
            {
                new GeneratedFile("Program.cs", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true),
                new GeneratedFile("GeneratedProgram.csproj", TextOutput.EnsureOneTrailingNewLine(project), IsPrimary: false)
            });
    }

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            switch (sourceItem)
            {
                case BoundFullLineComment comment:
                    TargetComments.Append(source, TargetLanguage.CSharp, indent, comment.Payload);
                    break;

                case BoundBlankLine:
                    source.AppendLine();
                    break;

                case BoundLetStatement let:
                    string initializer = TargetExpression.CSharp(let.Initializer, identifiers, integers, checkedArithmetic);
                    source.AppendLine($"{indent}{TargetTypes.CSharp(let.Variable.Type, integers)} {identifiers.Get(let.Variable)} = {initializer};");
                    break;

                case BoundSetStatement set:
                    string name = identifiers.Get(set.Variable);
                    string value = TargetExpression.CSharp(set.Value, identifiers, integers, checkedArithmetic);
                    if (set.Value is BoundVariableExpression variable &&
                        ReferenceEquals(variable.Variable, set.Variable))
                    {
                        // Direct self-assignment is valid SMILE, but C# warns
                        // about a plain `value = value` (CS1717). Keep the real
                        // storage update with the smallest type-preserving
                        // identity expression instead of deleting the SET.
                        value = set.Variable.Type switch
                        {
                            SmileType.String => value + " + \"\"",
                            SmileType.Integer => value + " + 0",
                            SmileType.Boolean => value + " || false",
                            _ => value
                        };
                    }

                    source.AppendLine($"{indent}{name} = {value};");
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
                        .Append('(').Append(TargetEscapes.CSharpString(input.Variable.Name))
                        .AppendLine(");");
                    break;

                case BoundPrintStatement print:
                    if (print.IsBlankLine)
                    {
                        source.Append(indent).AppendLine("Console.WriteLine();");
                    }
                    else
                    {
                        source.AppendLine($"{indent}Console.WriteLine({TargetExpression.CSharpDisplay(print.Value, identifiers, integers, checkedArithmetic)});");
                    }

                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        identifiers,
                        integers,
                        hasConditionHelper,
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
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = TargetExpression.CSharp(clause.Condition, identifiers, integers, checkedArithmetic);
            if (GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition))
            {
                condition = $"_smile_condition({condition})";
            }

            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(condition)
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(
                source,
                clause.SourceItems,
                indent + "    ",
                identifiers,
                integers,
                hasConditionHelper,
                checkedArithmetic);
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
                identifiers,
                integers,
                hasConditionHelper,
                checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendInputHelpers(StringBuilder source, BoundProgram program)
    {
        source.AppendLine();
        source.AppendLine("    private static readonly UTF8Encoding _smile_utf8 = new UTF8Encoding(false, true);");
        source.AppendLine("    private static readonly System.IO.Stream _smile_input_stream = _smile_open_input();");
        source.AppendLine("    private static bool _smile_skip_lf;");
        source.AppendLine();
        source.AppendLine("    private static System.IO.Stream _smile_open_input()");
        source.AppendLine("    {");
        source.AppendLine("        Console.InputEncoding = _smile_utf8;");
        source.AppendLine("        return Console.OpenStandardInput();");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    private static int _smile_read_byte(string variableName)");
        source.AppendLine("    {");
        source.AppendLine("        try");
        source.AppendLine("        {");
        source.AppendLine("            return _smile_input_stream.ReadByte();");
        source.AppendLine("        }");
        source.AppendLine("        catch (System.IO.IOException)");
        source.AppendLine("        {");
        source.AppendLine("            _smile_fail($\"SMILE Runtime Error SMILER1506: Input for '{variableName}' could not be read as valid UTF-8 text.\");");
        source.AppendLine("            return -1;");
        source.AppendLine("        }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    private static string _smile_read_line(string variableName)");
        source.AppendLine("    {");
        source.AppendLine($"        byte[] bytes = new byte[{SmileLanguage.MaximumInputLineUtf8Bytes}];");
        source.AppendLine("        int count = 0;");
        source.AppendLine("        bool firstByte = true;");
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
        source.AppendLine("                    _smile_fail($\"SMILE Runtime Error SMILER1501: Input ended before a value was received for '{variableName}'.\");");
        source.AppendLine("                    return string.Empty;");
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
        source.AppendLine($"                _smile_fail($\"SMILE Runtime Error SMILER1502: Input for '{{variableName}}' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.\");");
        source.AppendLine("                return string.Empty;");
        source.AppendLine("            }");
        source.AppendLine();
        source.AppendLine("            bytes[count++] = (byte)next;");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        try");
        source.AppendLine("        {");
        source.AppendLine("            return _smile_utf8.GetString(bytes, 0, count);");
        source.AppendLine("        }");
        source.AppendLine("        catch (DecoderFallbackException)");
        source.AppendLine("        {");
        source.AppendLine("            _smile_fail($\"SMILE Runtime Error SMILER1506: Input for '{variableName}' could not be read as valid UTF-8 text.\");");
        source.AppendLine("            return string.Empty;");
        source.AppendLine("        }");
        source.AppendLine("    }");

        if (TargetRuntimeFacts.HasInput(program, SmileType.String))
        {
            source.AppendLine();
            source.AppendLine("    private static string _smile_input_string(string variableName) =>");
            source.AppendLine("        _smile_read_line(variableName);");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine();
            source.AppendLine("    private static long _smile_input_integer(string variableName)");
            source.AppendLine("    {");
            source.AppendLine("        string text = _smile_read_line(variableName).Trim(' ', '\\t');");
            source.AppendLine("        int digitStart = text.Length > 0 && (text[0] == '+' || text[0] == '-') ? 1 : 0;");
            source.AppendLine("        if (digitStart == text.Length || text.AsSpan(digitStart).IndexOfAnyExceptInRange('0', '9') >= 0)");
            source.AppendLine("        {");
            source.AppendLine("            _smile_fail($\"SMILE Runtime Error SMILER1503: Input for '{variableName}' is not a valid Integer.\");");
            source.AppendLine("            return 0;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))");
            source.AppendLine("        {");
            source.AppendLine("            _smile_fail($\"SMILE Runtime Error SMILER1504: Input for '{variableName}' is outside the signed 64-bit Integer range.\");");
            source.AppendLine("            return 0;");
            source.AppendLine("        }");
            source.AppendLine();
            source.AppendLine("        return value;");
            source.AppendLine("    }");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Boolean))
        {
            source.AppendLine();
            source.AppendLine("    private static bool _smile_input_boolean(string variableName)");
            source.AppendLine("    {");
            source.AppendLine("        string text = _smile_read_line(variableName).Trim(' ', '\\t');");
            source.AppendLine("        if (text.Equals(\"TRUE\", StringComparison.OrdinalIgnoreCase)) return true;");
            source.AppendLine("        if (text.Equals(\"FALSE\", StringComparison.OrdinalIgnoreCase)) return false;");
            source.AppendLine("        _smile_fail($\"SMILE Runtime Error SMILER1505: Input for '{variableName}' must be TRUE or FALSE.\");");
            source.AppendLine("        return false;");
            source.AppendLine("    }");
        }

        source.AppendLine();
        source.AppendLine("    private static void _smile_fail(string message)");
        source.AppendLine("    {");
        source.AppendLine("        Console.Error.WriteLine(message);");
        source.AppendLine("        Environment.Exit(1);");
        source.AppendLine("    }");
    }

    private static void AppendCheckedArithmeticHelpers(
        StringBuilder source,
        TargetIntegerProfile integers)
    {
        string type = integers.RequiresSigned64Storage ? "long" : "int";
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_add({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(left + right); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_subtract({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(left - right); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_multiply({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(left * right); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_negate({type} value)");
        source.AppendLine("    {");
        source.AppendLine("        try { return checked(-value); }");
        source.AppendLine("        catch (OverflowException) { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine($"    private static {type} _smile_divide({type} left, {type} right)");
        source.AppendLine("    {");
        source.AppendLine("        if (right == 0) { _smile_fail(\"SMILE Runtime Error SMILER1207: Division by zero.\"); return 0; }");
        source.AppendLine($"        if (left == {type}.MinValue && right == -1) {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\"); return 0; }}");
        source.AppendLine("        return left / right;");
        source.AppendLine("    }");
    }
}

internal static class CSharpGenerationFacts
{
    public static bool NeedsInvariantCulture(BoundProgram program) =>
        BoundStatementTree.Enumerate(program).Any(statement => statement switch
        {
            BoundLetStatement let => NeedsInvariantCulture(let.Initializer, displayContext: false),
            BoundSetStatement set => NeedsInvariantCulture(set.Value, displayContext: false),
            BoundPrintStatement print => !print.IsBlankLine && NeedsInvariantCulture(print.Value, displayContext: true),
            BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                NeedsInvariantCulture(clause.Condition, displayContext: false)),
            _ => false
        });

    private static bool NeedsInvariantCulture(BoundExpression expression, bool displayContext)
    {
        // C# only needs CultureInfo when a SMILE Integer is converted to text.
        // Its storage type is selected once from the complete bound program.
        if (displayContext && expression.Type is SmileType.Integer)
        {
            return true;
        }

        return expression switch
        {
            BoundUnaryExpression unary => NeedsInvariantCulture(unary.Operand, displayContext: false),
            BoundBinaryExpression binary => NeedsInvariantCulture(binary.Left, displayContext: false) ||
                NeedsInvariantCulture(binary.Right, displayContext: false),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart interpolation &&
                NeedsInvariantCulture(interpolation.Expression, displayContext: true)),
            _ => false
        };
    }
}
