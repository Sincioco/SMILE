using System.Text;

namespace SMILE.Engine;

internal sealed class JavaScriptCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.JavaScript;

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
        var source = new StringBuilder();

        if (hasInput)
        {
            AppendInputHelpers(source, program);
            source.AppendLine();
        }

        if (checkedArithmetic)
        {
            AppendCheckedArithmeticHelpers(source);
            source.AppendLine();
        }

        AppendSourceItems(source, program.SourceItems, string.Empty, identifiers, integers, checkedArithmetic);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.js", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
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
                    TargetComments.Append(source, TargetLanguage.JavaScript, indent, comment.Payload);
                    break;

                case BoundBlankLine:
                    source.AppendLine();
                    break;

                case BoundLetStatement let:
                    source.AppendLine($"{indent}let {identifiers.Get(let.Variable)} = {TargetExpression.JavaScript(let.Initializer, identifiers, integers, checkedArithmetic)};");
                    break;

                case BoundSetStatement set:
                    source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {TargetExpression.JavaScript(set.Value, identifiers, integers, checkedArithmetic)};");
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
                        .Append('(').Append(TargetEscapes.JavaScriptString(input.Variable.Name))
                        .AppendLine(");");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "console.log();"
                        : $"console.log({TargetExpression.JavaScriptDisplay(print.Value, identifiers, integers, checkedArithmetic)});");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(source, conditional, indent, identifiers, integers, checkedArithmetic);
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
                .Append(TargetExpression.JavaScript(clause.Condition, identifiers, integers, checkedArithmetic))
                .AppendLine(") {");
            AppendSourceItems(source, clause.SourceItems, indent + "    ", identifiers, integers, checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else {");
            AppendSourceItems(source, conditional.ElseSourceItems, indent + "    ", identifiers, integers, checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendInputHelpers(StringBuilder source, BoundProgram program)
    {
        source.AppendLine("const fs = require(\"fs\");");
        source.AppendLine("const _smile_input_byte = Buffer.allocUnsafe(1);");
        // WHATWG's ignoreBOM option is named from the decoder's perspective:
        // true means do not consume the BOM, which preserves SMILE String data.
        source.AppendLine("const _smile_decoder = new TextDecoder(\"utf-8\", { fatal: true, ignoreBOM: true });");
        source.AppendLine("let _smile_skip_lf = false;");
        source.AppendLine();
        source.AppendLine("function _smile_next_byte() {");
        source.AppendLine("    return fs.readSync(0, _smile_input_byte, 0, 1, null) === 0 ? -1 : _smile_input_byte[0];");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("function _smile_read_line(variableName) {");
        source.AppendLine("    const bytes = [];");
        source.AppendLine("    let firstByte = true;");
        source.AppendLine("    try {");
        source.AppendLine("        while (true) {");
        source.AppendLine("            const value = _smile_next_byte();");
        source.AppendLine("            if (firstByte) {");
        source.AppendLine("                firstByte = false;");
        source.AppendLine("                if (_smile_skip_lf) {");
        source.AppendLine("                    _smile_skip_lf = false;");
        source.AppendLine("                    if (value === 10) continue;");
        source.AppendLine("                }");
        source.AppendLine("            }");
        source.AppendLine("            if (value < 0) {");
        source.AppendLine("                if (bytes.length === 0) _smile_fail(`SMILE Runtime Error SMILER1501: Input ended before a value was received for '${variableName}'.`);");
        source.AppendLine("                break;");
        source.AppendLine("            }");
        source.AppendLine("            if (value === 10) break;");
        source.AppendLine("            if (value === 13) {");
        source.AppendLine("                _smile_skip_lf = true;");
        source.AppendLine("                break;");
        source.AppendLine("            }");
        source.AppendLine("            bytes.push(value);");
        source.AppendLine($"            if (bytes.length > {SmileLanguage.MaximumInputLineUtf8Bytes}) _smile_fail(`SMILE Runtime Error SMILER1502: Input for '${{variableName}}' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.`);");
        source.AppendLine("        }");
        source.AppendLine("        return _smile_decoder.decode(Uint8Array.from(bytes));");
        source.AppendLine("    } catch (error) {");
        source.AppendLine("        if (error && error._smileRuntimeError) throw error;");
        source.AppendLine("        _smile_fail(`SMILE Runtime Error SMILER1506: Input for '${variableName}' could not be read as valid UTF-8 text.`);");
        source.AppendLine("    }");
        source.AppendLine("}");

        if (TargetRuntimeFacts.HasInput(program, SmileType.String))
        {
            source.AppendLine();
            source.AppendLine("function _smile_input_string(variableName) { return _smile_read_line(variableName); }");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine();
            source.AppendLine("function _smile_input_integer(variableName) {");
            source.AppendLine("    const text = _smile_read_line(variableName).replace(/^[ \\t]+|[ \\t]+$/g, \"\");");
            source.AppendLine("    if (!/^[+-]?[0-9]+$/.test(text)) _smile_fail(`SMILE Runtime Error SMILER1503: Input for '${variableName}' is not a valid Integer.`);");
            source.AppendLine("    const value = BigInt(text);");
            source.AppendLine("    if (value < -9223372036854775808n || value > 9223372036854775807n) _smile_fail(`SMILE Runtime Error SMILER1504: Input for '${variableName}' is outside the signed 64-bit Integer range.`);");
            source.AppendLine("    return value;");
            source.AppendLine("}");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Boolean))
        {
            source.AppendLine();
            source.AppendLine("function _smile_input_boolean(variableName) {");
            source.AppendLine("    const text = _smile_read_line(variableName).replace(/^[ \\t]+|[ \\t]+$/g, \"\");");
            source.AppendLine("    if (/^[Tt][Rr][Uu][Ee]$/.test(text)) return true;");
            source.AppendLine("    if (/^[Ff][Aa][Ll][Ss][Ee]$/.test(text)) return false;");
            source.AppendLine("    _smile_fail(`SMILE Runtime Error SMILER1505: Input for '${variableName}' must be TRUE or FALSE.`);");
            source.AppendLine("}");
        }

        source.AppendLine();
        source.AppendLine("function _smile_fail(message) {");
        source.AppendLine("    console.error(message);");
        source.AppendLine("    const error = new Error(message);");
        source.AppendLine("    error._smileRuntimeError = true;");
        source.AppendLine("    process.exitCode = 1;");
        source.AppendLine("    throw error;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("process.on(\"uncaughtException\", error => {");
        source.AppendLine("    if (error && error._smileRuntimeError) return;");
        source.AppendLine("    throw error;");
        source.AppendLine("});");
    }

    private static void AppendCheckedArithmeticHelpers(StringBuilder source)
    {
        source.AppendLine("function _smile_checked(value) {");
        source.AppendLine("    if (value < -9223372036854775808n || value > 9223372036854775807n) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    return value;");
        source.AppendLine("}");
        source.AppendLine("function _smile_add(left, right) { return _smile_checked(left + right); }");
        source.AppendLine("function _smile_subtract(left, right) { return _smile_checked(left - right); }");
        source.AppendLine("function _smile_multiply(left, right) { return _smile_checked(left * right); }");
        source.AppendLine("function _smile_negate(value) { return _smile_checked(-value); }");
        source.AppendLine("function _smile_divide(left, right) {");
        source.AppendLine("    if (right === 0n) _smile_fail(\"SMILE Runtime Error SMILER1207: Division by zero.\");");
        source.AppendLine("    if (left === -9223372036854775808n && right === -1n) _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\");");
        source.AppendLine("    return left / right;");
        source.AppendLine("}");
    }
}
