using System.Text;

namespace SMILE.Engine;

internal sealed class SwiftCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.Swift;

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
        IReadOnlySet<VariableSymbol> mutatedVariables = BoundStatementTree.Enumerate(program)
            .Select(statement => statement switch
            {
                BoundSetStatement set => set.Variable,
                BoundInputStatement input => input.Variable,
                _ => null
            })
            .Where(variable => variable is not null)
            .Select(variable => variable!)
            .ToHashSet();
        bool needsConditionHelper = BoundStatementTree.Enumerate(program).Any(statement =>
            statement switch
            {
                BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition)),
                BoundWhileStatement loop =>
                    GeneratorConditionFacts.RequiresWarningSafeWrapper(loop.Condition),
                _ => false
            });

        if (hasInput || checkedArithmetic)
        {
            source.AppendLine("import Foundation");
            source.AppendLine();
        }

        if (hasInput)
        {
            AppendInputHelpers(source, program);
            source.AppendLine();
            source.AppendLine();
        }
        else if (checkedArithmetic)
        {
            AppendFailureHelper(source);
            source.AppendLine();
            source.AppendLine();
        }

        if (checkedArithmetic)
        {
            AppendCheckedArithmeticHelpers(source, integers);
            source.AppendLine();
            source.AppendLine();
        }

        if (needsConditionHelper)
        {
            source.AppendLine("// Keep valid source-constant control flow genuine without warnings.");
            source.AppendLine("@inline(never)");
            source.AppendLine("func _smile_condition(_ value: Bool) -> Bool {");
            source.AppendLine("    value");
            source.AppendLine("}");
            source.AppendLine();
        }

        AppendSourceItems(
            source,
            program.SourceItems,
            string.Empty,
            identifiers,
            integers,
            mutatedVariables,
            needsConditionHelper,
            checkedArithmetic);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.swift", TextOutput.EnsureOneTrailingNewLinePreservingExistingLineEndings(source.ToString()), IsPrimary: true) });
    }

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            switch (sourceItem)
            {
                case BoundFullLineComment comment:
                    TargetComments.Append(source, TargetLanguage.Swift, indent, comment.Payload);
                    break;

                case BoundBlankLine:
                    source.AppendLine();
                    break;

                case BoundLetStatement let:
                    string initializer = WriteDirectExpression(
                        let.Initializer,
                        indent,
                        identifiers,
                        integers,
                        checkedArithmetic);
                    string declaration = mutatedVariables.Contains(let.Variable) ? "var" : "let";
                    source.AppendLine($"{indent}{declaration} {identifiers.Get(let.Variable)}: {TargetTypes.Swift(let.Variable.Type, integers)} = {initializer}");
                    break;

                case BoundSetStatement set:
                    string name = identifiers.Get(set.Variable);
                    string value = WriteDirectExpression(
                        set.Value,
                        indent,
                        identifiers,
                        integers,
                        checkedArithmetic);
                    if (set.Value is BoundVariableExpression variable &&
                        variable.Variable == set.Variable)
                    {
                        // Swift rejects a plain `value = value` as a compile-time
                        // error even though direct self-assignment is valid SMILE.
                        // Keep the required target storage update with the
                        // smallest type-preserving identity expression.
                        value = set.Variable.Type switch
                        {
                            SmileType.String => value + " + \"\"",
                            SmileType.Integer => value + " + 0",
                            SmileType.Boolean => value + " || false",
                            _ => value
                        };
                    }

                    source.AppendLine($"{indent}{name} = {value}");
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
                        .Append('(').Append(TargetEscapes.SwiftString(input.Variable.Name))
                        .AppendLine(")");
                    break;

                case BoundPrintStatement print:
                    source.Append(indent).AppendLine(print.IsBlankLine
                        ? "print()"
                        : $"print({WriteDirectDisplayExpression(print.Value, indent, identifiers, integers, checkedArithmetic)})");
                    break;

                case BoundIfStatement conditional:
                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        identifiers,
                        integers,
                        mutatedVariables,
                        hasConditionHelper,
                        checkedArithmetic);
                    break;

                case BoundWhileStatement loop:
                    AppendWhileStatement(
                        source,
                        loop,
                        indent,
                        identifiers,
                        integers,
                        mutatedVariables,
                        hasConditionHelper,
                        checkedArithmetic);
                    break;
            }
        }
    }

    private static string WriteDirectExpression(
        BoundExpression expression,
        string structuralIndent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedArithmetic) =>
        expression is BoundStringLiteralExpression literal &&
        TargetMultilineLiterals.TrySwift(literal.Value, structuralIndent, out string multiline)
            ? multiline
            : TargetExpression.Swift(expression, identifiers, integers, checkedArithmetic);

    private static string WriteDirectDisplayExpression(
        BoundExpression expression,
        string structuralIndent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool checkedArithmetic) =>
        expression is BoundStringLiteralExpression literal &&
        TargetMultilineLiterals.TrySwift(literal.Value, structuralIndent, out string multiline)
            ? multiline
            : TargetExpression.SwiftDisplay(expression, identifiers, integers, checkedArithmetic);

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = TargetExpression.Swift(clause.Condition, identifiers, integers, checkedArithmetic);
            if (GeneratorConditionFacts.RequiresWarningSafeWrapper(clause.Condition))
            {
                condition = $"_smile_condition({condition})";
            }

            source.Append(indent)
                .Append(clauseIndex == 0 ? "if " : "else if ")
                .Append(condition)
                .AppendLine(" {");
            AppendSourceItems(
                source,
                clause.SourceItems,
                indent + "    ",
                identifiers,
                integers,
                mutatedVariables,
                hasConditionHelper,
                checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else {");
            AppendSourceItems(
                source,
                conditional.ElseSourceItems,
                indent + "    ",
                identifiers,
                integers,
                mutatedVariables,
                hasConditionHelper,
                checkedArithmetic);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendWhileStatement(
        StringBuilder source,
        BoundWhileStatement loop,
        string indent,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlySet<VariableSymbol> mutatedVariables,
        bool hasConditionHelper,
        bool checkedArithmetic)
    {
        string condition = TargetExpression.Swift(
            loop.Condition,
            identifiers,
            integers,
            checkedArithmetic);
        if (GeneratorConditionFacts.RequiresWarningSafeWrapper(loop.Condition))
        {
            condition = $"_smile_condition({condition})";
        }

        source.Append(indent).Append("while ").Append(condition).AppendLine(" {");
        AppendSourceItems(
            source,
            loop.SourceItems,
            indent + "    ",
            identifiers,
            integers,
            mutatedVariables,
            hasConditionHelper,
            checkedArithmetic);
        source.Append(indent).AppendLine("}");
    }

    private static void AppendInputHelpers(StringBuilder source, BoundProgram program)
    {
        source.AppendLine("var _smile_skip_lf = false");
        source.AppendLine();
        source.AppendLine("func _smile_fail(_ message: String) -> Never {");
        source.AppendLine("    FileHandle.standardError.write((message + \"\\n\").data(using: .utf8)!)");
        source.AppendLine("    exit(1)");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("func _smile_next_byte() throws -> UInt8? {");
        source.AppendLine("    return try FileHandle.standardInput.read(upToCount: 1)?.first");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("func _smile_read_line(_ variableName: String) -> String {");
        source.AppendLine("    var bytes: [UInt8] = []");
        source.AppendLine("    var firstByte = true");
        source.AppendLine("    do {");
        source.AppendLine("        while true {");
        source.AppendLine("            guard let value = try _smile_next_byte() else {");
        source.AppendLine("                if bytes.isEmpty { _smile_fail(\"SMILE Runtime Error SMILER1501: Input ended before a value was received for '\\(variableName)'.\") }");
        source.AppendLine("                break");
        source.AppendLine("            }");
        source.AppendLine("            if firstByte {");
        source.AppendLine("                firstByte = false");
        source.AppendLine("                if _smile_skip_lf {");
        source.AppendLine("                    _smile_skip_lf = false");
        source.AppendLine("                    if value == 10 { continue }");
        source.AppendLine("                }");
        source.AppendLine("            }");
        source.AppendLine("            if value == 10 { break }");
        source.AppendLine("            if value == 13 {");
        source.AppendLine("                _smile_skip_lf = true");
        source.AppendLine("                break");
        source.AppendLine("            }");
        source.AppendLine("            bytes.append(value)");
        source.AppendLine($"            if bytes.count > {SmileLanguage.MaximumInputLineUtf8Bytes} {{ _smile_fail(\"SMILE Runtime Error SMILER1502: Input for '\\(variableName)' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.\") }}");
        source.AppendLine("        }");
        source.AppendLine("    } catch {");
        source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1506: Input for '\\(variableName)' could not be read as valid UTF-8 text.\")");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    guard String(data: Data(bytes), encoding: .utf8) != nil else {");
        source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1506: Input for '\\(variableName)' could not be read as valid UTF-8 text.\")");
        source.AppendLine("    }");
        source.AppendLine("    return String(decoding: bytes, as: UTF8.self)");
        source.AppendLine("}");

        if (TargetRuntimeFacts.HasInput(program, SmileType.String))
        {
            source.AppendLine();
            source.AppendLine("func _smile_input_string(_ variableName: String) -> String {");
            source.AppendLine("    _smile_read_line(variableName)");
            source.AppendLine("}");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Integer))
        {
            source.AppendLine();
        source.AppendLine("func _smile_input_integer(_ variableName: String) -> Int64 {");
        source.AppendLine("    let text = _smile_read_line(variableName).trimmingCharacters(in: CharacterSet(charactersIn: \" \\t\"))");
        source.AppendLine("    let bytes = Array(text.utf8)");
        source.AppendLine("    let digits = bytes.first == 43 || bytes.first == 45 ? bytes.dropFirst() : bytes[...]");
        source.AppendLine("    guard !digits.isEmpty && digits.allSatisfy({ $0 >= 48 && $0 <= 57 }) else {");
            source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1503: Input for '\\(variableName)' is not a valid Integer.\")");
            source.AppendLine("    }");
            source.AppendLine("    guard let value = Int64(text) else {");
            source.AppendLine("        _smile_fail(\"SMILE Runtime Error SMILER1504: Input for '\\(variableName)' is outside the signed 64-bit Integer range.\")");
            source.AppendLine("    }");
            source.AppendLine("    return value");
            source.AppendLine("}");
        }

        if (TargetRuntimeFacts.HasInput(program, SmileType.Boolean))
        {
            source.AppendLine();
            source.AppendLine("func _smile_ascii_equals(_ text: String, _ expected: [UInt8]) -> Bool {");
            source.AppendLine("    let bytes = Array(text.utf8)");
            source.AppendLine("    guard bytes.count == expected.count else { return false }");
            source.AppendLine("    for index in bytes.indices {");
            source.AppendLine("        let actual = bytes[index]");
            source.AppendLine("        let upper = expected[index]");
            source.AppendLine("        if actual != upper && actual != upper + 32 { return false }");
            source.AppendLine("    }");
            source.AppendLine("    return true");
            source.AppendLine("}");
            source.AppendLine();
            source.AppendLine("func _smile_input_boolean(_ variableName: String) -> Bool {");
            source.AppendLine("    let text = _smile_read_line(variableName).trimmingCharacters(in: CharacterSet(charactersIn: \" \\t\"))");
            source.AppendLine("    if _smile_ascii_equals(text, [84, 82, 85, 69]) { return true }");
            source.AppendLine("    if _smile_ascii_equals(text, [70, 65, 76, 83, 69]) { return false }");
            source.AppendLine("    _smile_fail(\"SMILE Runtime Error SMILER1505: Input for '\\(variableName)' must be TRUE or FALSE.\")");
            source.AppendLine("}");
        }
    }

    private static void AppendFailureHelper(StringBuilder source)
    {
        source.AppendLine("func _smile_fail(_ message: String) -> Never {");
        source.AppendLine("    FileHandle.standardError.write((message + \"\\n\").data(using: .utf8)!)");
        source.AppendLine("    exit(1)");
        source.AppendLine("}");
    }

    private static void AppendCheckedArithmeticHelpers(StringBuilder source, TargetIntegerProfile integers)
    {
        string type = integers.RequiresSigned64Storage ? "Int64" : "Int";
        source.AppendLine($"func _smile_add(_ left: {type}, _ right: {type}) -> {type} {{");
        source.AppendLine("    let result = left.addingReportingOverflow(right)");
        source.AppendLine("    if result.overflow { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\") }");
        source.AppendLine("    return result.partialValue");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"func _smile_subtract(_ left: {type}, _ right: {type}) -> {type} {{");
        source.AppendLine("    let result = left.subtractingReportingOverflow(right)");
        source.AppendLine("    if result.overflow { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\") }");
        source.AppendLine("    return result.partialValue");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"func _smile_multiply(_ left: {type}, _ right: {type}) -> {type} {{");
        source.AppendLine("    let result = left.multipliedReportingOverflow(by: right)");
        source.AppendLine("    if result.overflow { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\") }");
        source.AppendLine("    return result.partialValue");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"func _smile_negate(_ value: {type}) -> {type} {{");
        source.AppendLine("    let result = Int64(0).subtractingReportingOverflow(value)");
        source.AppendLine("    if result.overflow { _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\") }");
        source.AppendLine("    return result.partialValue");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine($"func _smile_divide(_ left: {type}, _ right: {type}) -> {type} {{");
        source.AppendLine("    if right == 0 { _smile_fail(\"SMILE Runtime Error SMILER1207: Division by zero.\") }");
        source.AppendLine($"    if left == {type}.min && right == -1 {{ _smile_fail(\"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\") }}");
        source.AppendLine("    return left / right");
        source.AppendLine("}");
    }
}
