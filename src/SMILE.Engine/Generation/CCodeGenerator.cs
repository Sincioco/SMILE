using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class CCodeGenerator : ICodeGenerator
{
    internal sealed record RuntimeStringBuffer(string Name, int Capacity);

    public TargetLanguage Language => TargetLanguage.C;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers =
            CreateRuntimeStringBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers =
            CreateRuntimeExpressionBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths =
            CreateExactStringLengthNames(
                program,
                identifiers,
                analysis,
                runtimeStringBuffers.Keys.Select(statement => statement switch
                {
                    BoundLetStatement let => let.Variable,
                    BoundSetStatement set => set.Variable,
                    _ => throw new InvalidOperationException("Unexpected C runtime String statement.")
                }));
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (exactStringLengths.Count > 0 ||
            CGenerationFacts.NeedsStringComparison(analysis))
        {
            source.AppendLine("#include <string.h>");
        }

        source.AppendLine();
        source.AppendLine("int main(void)");
        source.AppendLine("{");

        foreach (RuntimeStringBuffer buffer in runtimeExpressionBuffers.Values)
        {
            source.Append("    static char ").Append(buffer.Name).Append('[')
                .Append((buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture))
                .AppendLine("] = { 0 };");
            source.Append("    size_t ").Append(buffer.Name).AppendLine("Used = 0;");
        }

        if (runtimeExpressionBuffers.Count > 0)
        {
            source.AppendLine();
        }

        bool emittedDeclaration = runtimeExpressionBuffers.Count > 0;
        bool emittedExecutable = false;
        bool emittedBodyStatement = false;
        AppendStatements(
            source,
            program.Statements,
            "    ",
            analysis,
            identifiers,
            integers,
            exactStringLengths,
            runtimeStringBuffers,
            runtimeExpressionBuffers,
            ref emittedDeclaration,
            ref emittedExecutable,
            ref emittedBodyStatement);

        if (emittedBodyStatement)
        {
            source.AppendLine();
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.c", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        foreach (BoundStatement statement in statements)
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    if (let.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        if (let.Initializer is BoundVariableExpression letDirectSource)
                        {
                            source.AppendLine(
                                $"{indent}const char *{identifiers.Get(let.Variable)} = {identifiers.Get(letDirectSource.Variable)};");
                            if (exactStringLengths.TryGetValue(let.Variable, out string? directLetLength))
                            {
                                string sourceLength = exactStringLengths.TryGetValue(
                                    letDirectSource.Variable,
                                    out string? exactSourceLength)
                                    ? exactSourceLength
                                    : $"strlen({identifiers.Get(letDirectSource.Variable)})";
                                source.AppendLine($"{indent}size_t {directLetLength} = {sourceLength};");
                            }
                        }
                        else
                        {
                            RuntimeStringBuffer buffer = runtimeStringBuffers[let];
                            source.AppendLine(
                                $"{indent}static char {buffer.Name}[{buffer.Capacity + 1}] = {{ 0 }};");
                            source.AppendLine(
                                $"{indent}const char *{identifiers.Get(let.Variable)} = {buffer.Name};");
                            source.AppendLine(
                                $"{indent}size_t {exactStringLengths[let.Variable]} = 0;");
                            AppendCRuntimeStringAssignment(
                                source,
                                indent,
                                let.Variable,
                                let.Initializer,
                                buffer,
                                identifiers,
                                integers,
                                exactStringLengths,
                                runtimeExpressionBuffers,
                                declareBuffer: false);
                        }
                    }
                    else
                    {
                        SmileValue letValue = let.Variable.Type is SmileType.String
                            ? facts.Value.Value
                            : default;
                        string initializer = let.Variable.Type is SmileType.String
                            ? TargetExpression.CConstant(letValue, integers)
                            : TargetExpression.C(
                                let.Initializer,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                        if (exactStringLengths.TryGetValue(let.Variable, out string? letLengthName))
                        {
                            source.AppendLine($"{indent}size_t {letLengthName} = {Utf8ByteLength(letValue)};");
                        }
                    }

                    emittedDeclaration = true;
                    emittedBodyStatement = true;
                    break;

                case BoundSetStatement set:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    if (set.Variable.Type is SmileType.String &&
                        !facts.Value.IsKnown &&
                        set.Value is BoundVariableExpression directSource)
                    {
                        AppendCDirectStringCopy(
                            source,
                            indent,
                            set.Variable,
                            directSource.Variable,
                            identifiers,
                            exactStringLengths);
                    }
                    else if (set.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        AppendCRuntimeStringAssignment(
                            source,
                            indent,
                            set.Variable,
                            set.Value,
                            runtimeStringBuffers[set],
                            identifiers,
                            integers,
                            exactStringLengths,
                            runtimeExpressionBuffers,
                            declareBuffer: true);
                    }
                    else
                    {
                        SmileValue setValue = facts.Value.IsKnown
                            ? facts.Value.Value
                            : default;
                        string value = set.Variable.Type is SmileType.String
                            ? TargetExpression.CConstant(setValue, integers)
                            : TargetExpression.C(
                                set.Value,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {value};");
                        if (exactStringLengths.TryGetValue(set.Variable, out string? setLengthName))
                        {
                            source.AppendLine($"{indent}{setLengthName} = {Utf8ByteLength(setValue)};");
                        }
                    }

                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundPrintStatement print:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendCPrint(
                        source,
                        indent,
                        print,
                        identifiers,
                        integers,
                        facts.Value.IsKnown,
                        GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                        exactStringLengths,
                        runtimeExpressionBuffers);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundIfStatement conditional:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendIfStatement(
                        source,
                        conditional,
                        indent,
                        analysis,
                        identifiers,
                        integers,
                        exactStringLengths,
                        runtimeStringBuffers,
                        runtimeExpressionBuffers,
                        ref emittedDeclaration,
                        ref emittedExecutable,
                        ref emittedBodyStatement);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;
            }
        }
    }

    private static void AppendIfStatement(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            BoundConditionalClauseAnalysis clauseFacts = analysis.GetClauseFacts(clause);
            source.Append(indent)
                .Append(clauseIndex == 0 ? "if (" : "else if (")
                .Append(TargetExpression.C(
                    clause.Condition,
                    identifiers,
                    integers,
                    GeneratorConditionFacts.KnownValues(clauseFacts.ValuesBefore),
                    exactStringLengths,
                    runtimeExpressionBuffers))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                clause.Statements,
                indent + "    ",
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
            source.Append(indent).AppendLine("}");
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("else");
            source.Append(indent).AppendLine("{");
            AppendStatements(
                source,
                conditional.ElseStatements,
                indent + "    ",
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendCPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool valueIsKnown,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers)
    {
        if (TryAppendDirectStringVariablePrint(
            source,
            indent,
            print,
            identifiers,
            exactStringLengths))
        {
            return;
        }

        if (!valueIsKnown && TryAppendRuntimeStringSegments(
                source,
                indent,
                print,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers,
                TargetLanguage.C))
        {
            return;
        }

        if (valueIsKnown && TryAppendExactNulStringPrint(source, indent, print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.C(
                expression,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers),
            integers.RequiresSigned64Storage);
        AppendPrintfCall(source, indent, plan);
    }

    internal static bool TryAppendRuntimeStringSegments(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        TargetLanguage language)
    {
        if (print.IsBlankLine ||
            print.Value.Type is not SmileType.String ||
            !RuntimeTextPlan.CanFlatten(print.Value))
        {
            return false;
        }

        IReadOnlyList<RuntimeTextSegment> segments = RuntimeTextPlan.Flatten(print.Value);
        bool needsExactStreaming = segments.Any(segment => segment switch
        {
            RuntimeLiteralTextSegment literal => literal.Text.Contains('\0', StringComparison.Ordinal),
            RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable } =>
                variable.Variable.Type is SmileType.String &&
                exactStringLengths.ContainsKey(variable.Variable),
            _ => false
        });
        if (!needsExactStreaming)
        {
            return false;
        }

        foreach (RuntimeTextSegment segment in segments)
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal:
                    int byteLength = Encoding.UTF8.GetByteCount(literal.Text);
                    if (byteLength > 0)
                    {
                        source.Append(indent).Append("fwrite(")
                            .Append(TargetEscapes.CString(literal.Text))
                            .Append(", 1, ")
                            .Append(byteLength.ToString(CultureInfo.InvariantCulture))
                            .AppendLine(", stdout);");
                    }

                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }
                    when variable.Variable.Type is SmileType.String:
                    string name = identifiers.Get(variable.Variable);
                    if (exactStringLengths.TryGetValue(variable.Variable, out string? lengthName))
                    {
                        source.Append(indent).Append("fwrite(").Append(name).Append(", 1, ")
                            .Append(lengthName).AppendLine(", stdout);");
                    }
                    else
                    {
                        source.Append(indent).Append("fputs(").Append(name).AppendLine(", stdout);");
                    }

                    break;

                case RuntimeExpressionTextSegment expression:
                    string rendered = language is TargetLanguage.ObjectiveC
                        ? TargetExpression.ObjectiveC(
                            expression.Expression,
                            identifiers,
                            integers,
                            values,
                            exactStringLengths,
                            runtimeExpressionBuffers)
                        : TargetExpression.C(
                            expression.Expression,
                            identifiers,
                            integers,
                            values,
                            exactStringLengths,
                            runtimeExpressionBuffers);
                    CPrintfPlan typedPlan = CPrintfPlan.FromPrint(
                        new BoundPrintStatement(expression.Expression, IsBlankLine: false),
                        _ => rendered,
                        integers.RequiresSigned64Storage);
                    // Remove the newline owned by the complete PRINT; each
                    // live segment is emitted without advancing here.
                    AppendPrintfCall(
                        source,
                        indent,
                        typedPlan with
                        {
                            FormatText = typedPlan.FormatText[..^1]
                        });
                    break;
            }
        }

        source.Append(indent).AppendLine("fputc('\\n', stdout);");
        return true;
    }

    internal static bool TryAppendDirectStringVariablePrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        if (print.IsBlankLine ||
            print.Value is not BoundVariableExpression variable ||
            variable.Variable.Type is not SmileType.String)
        {
            return false;
        }

        string name = identifiers.Get(variable.Variable);
        if (exactStringLengths.TryGetValue(variable.Variable, out string? lengthName))
        {
            // Exact mutable Strings are pointer-plus-length values in C. Read
            // both pieces of current target storage instead of re-emitting the
            // statement's statically known bytes as an unrelated print literal.
            source.Append(indent).Append("fwrite(").Append(name).Append(", 1, ")
                .Append(lengthName).AppendLine(", stdout);");
            source.Append(indent).AppendLine("fputc('\\n', stdout);");
        }
        else
        {
            source.Append(indent).Append("printf(\"%s\\n\", ").Append(name).AppendLine(");");
        }

        return true;
    }

    internal static void AppendPrintfCall(StringBuilder source, string indent, CPrintfPlan plan)
    {
        source.Append(indent);
        source.Append("printf(");
        source.Append(TargetEscapes.CPrintfFormatString(plan.FormatText));

        foreach (string argument in plan.Arguments)
        {
            source.Append(", ");
            source.Append(argument);
        }

        source.AppendLine(");");
    }

    internal static bool TryAppendExactNulStringPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        if (print.IsBlankLine ||
            !GeneratorValueFacts.TryGetNulContainingString(print.Value, values, out string value))
        {
            return false;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(value);

        // A tiny nested scope lets every exact PRINT reuse the same readable
        // compiler-owned name without colliding with a SMILE variable in the
        // surrounding main function. The byte array avoids C's NUL-terminated
        // String convention and makes the UTF-8 length explicit to fwrite.
        source.Append(indent).AppendLine("{");
        source.Append(indent).Append("    static const unsigned char smilePrintBytes[] = { ");
        source.Append(string.Join(", ", bytes.Select(value => value.ToString(CultureInfo.InvariantCulture))));
        source.AppendLine(" };");
        source.Append(indent).Append("    fwrite(smilePrintBytes, 1, ");
        source.Append(bytes.Length.ToString(CultureInfo.InvariantCulture));
        source.AppendLine(", stdout);");
        source.Append(indent).AppendLine("    fputc('\\n', stdout);");
        source.Append(indent).AppendLine("}");
        return true;
    }

    internal static IReadOnlyDictionary<VariableSymbol, string> CreateExactStringLengthNames(
        BoundProgram program,
        TargetIdentifierMap identifiers,
        BoundProgramAnalysis analysis,
        IEnumerable<VariableSymbol>? additionalVariables = null)
    {
        var names = new Dictionary<VariableSymbol, string>();
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<VariableSymbol> additional = additionalVariables?.ToHashSet() ??
            new HashSet<VariableSymbol>();

        for (int index = 0; index < program.Variables.Count; index++)
        {
            VariableSymbol variable = program.Variables[index];
            if (variable.Type is not SmileType.String ||
                !analysis.AssignedValuesMayContainNul(variable) &&
                !additional.Contains(variable))
            {
                continue;
            }

            string preferred = $"smileString{index}Length";
            string name = preferred;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = preferred + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            names.Add(variable, name);
        }

        return names;
    }

    internal static IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer>
        CreateRuntimeStringBuffers(
            BoundProgram program,
            TargetIdentifierMap identifiers,
            BoundProgramAnalysis analysis)
    {
        var needsBuffer = new List<(BoundStatement Statement, VariableSymbol Variable)>();
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement { Variable.Type: SmileType.String } let when
                    !facts.Value.IsKnown && let.Initializer is not BoundVariableExpression:
                    needsBuffer.Add((let, let.Variable));
                    break;

                case BoundSetStatement { Variable.Type: SmileType.String } set when
                    !facts.Value.IsKnown && set.Value is not BoundVariableExpression:
                    needsBuffer.Add((set, set.Variable));
                    break;
            }
        }

        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);
        var buffers = new Dictionary<BoundStatement, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        for (int index = 0; index < needsBuffer.Count; index++)
        {
            (BoundStatement statement, VariableSymbol variable) = needsBuffer[index];

            string preferred = $"smileString{index}Buffer";
            string name = preferred;
            int suffix = 2;
            while (used.Contains(name) || used.Contains(name + "Used"))
            {
                name = preferred + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            used.Add(name);
            used.Add(name + "Used");

            buffers.Add(
                statement,
                new RuntimeStringBuffer(
                    name,
                    Math.Max(1, analysis.MaximumAssignedUtf8ByteLength(variable))));
        }

        return buffers;
    }

    internal static IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer>
        CreateRuntimeExpressionBuffers(
            BoundProgram program,
            TargetIdentifierMap identifiers,
            BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundExpression, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);

        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    Collect(let.Initializer, facts.ValuesBefore);
                    break;

                case BoundSetStatement set:
                    Collect(set.Value, facts.ValuesBefore);
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    Collect(print.Value, facts.ValuesBefore);
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        Collect(
                            clause.Condition,
                            analysis.GetClauseFacts(clause).ValuesBefore);
                    }

                    break;
            }
        }

        return buffers;

        void Collect(
            BoundExpression expression,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> valuesBefore)
        {
            if (expression is BoundBinaryExpression comparison &&
                comparison.Left.Type is SmileType.String &&
                comparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality)
            {
                Add(comparison.Left, valuesBefore);
                Add(comparison.Right, valuesBefore);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Collect(unary.Operand, valuesBefore);
                    break;

                case BoundBinaryExpression binary:
                    Collect(binary.Left, valuesBefore);
                    Collect(binary.Right, valuesBefore);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Collect(hole.Expression, valuesBefore);
                    }

                    break;
            }
        }

        void Add(
            BoundExpression operand,
            IReadOnlyDictionary<VariableSymbol, AnalyzedValue> valuesBefore)
        {
            if (operand is BoundVariableExpression or BoundStringLiteralExpression ||
                buffers.ContainsKey(operand) ||
                GeneratorConditionFacts.TryEvaluateFromAnalyzedValues(
                    operand,
                    valuesBefore,
                    out SmileValue knownOperand) &&
                knownOperand.Type is SmileType.String &&
                !knownOperand.StringValue.Contains('\0', StringComparison.Ordinal))
            {
                return;
            }

            string preferred = $"smileExpression{buffers.Count}Buffer";
            string name = preferred;
            int suffix = 2;
            while (used.Contains(name) || used.Contains(name + "Used"))
            {
                name = preferred + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            used.Add(name);
            used.Add(name + "Used");
            buffers.Add(
                operand,
                new RuntimeStringBuffer(
                    name,
                    Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    internal static void AppendCRuntimeStringAssignment(
        StringBuilder source,
        string indent,
        VariableSymbol destination,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        bool declareBuffer)
    {
        string workLength = buffer.Name + "Used";
        source.Append(indent).AppendLine("{");
        if (declareBuffer)
        {
            source.Append(indent).Append("    static char ").Append(buffer.Name).Append('[')
                .Append((buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture))
                .AppendLine("] = { 0 };");
        }

        source.Append(indent).Append("    size_t ").Append(workLength).AppendLine(" = 0;");
        AppendCRuntimeTextSegments(
            source,
            indent + "    ",
            expression,
            buffer,
            workLength,
            identifiers,
            integers,
            exactStringLengths,
            runtimeExpressionBuffers);
        source.Append(indent).Append("    ").Append(buffer.Name).Append('[')
            .Append(workLength).AppendLine("] = '\\0';");
        source.Append(indent).Append("    ").Append(identifiers.Get(destination))
            .Append(" = ").Append(buffer.Name).AppendLine(";");
        source.Append(indent).Append("    ").Append(exactStringLengths[destination])
            .Append(" = ").Append(workLength).AppendLine(";");
        source.Append(indent).AppendLine("}");
    }

    private static void AppendCRuntimeTextSegments(
        StringBuilder source,
        string indent,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        string workLength,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers)
    {
        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal:
                    int literalLength = Encoding.UTF8.GetByteCount(literal.Text);
                    if (literalLength == 0)
                    {
                        break;
                    }

                    source.Append(indent).Append("memcpy(").Append(buffer.Name).Append(" + ")
                        .Append(workLength).Append(", ").Append(TargetEscapes.CString(literal.Text))
                        .Append(", ").Append(literalLength.ToString(CultureInfo.InvariantCulture))
                        .AppendLine(");");
                    source.Append(indent).Append(workLength).Append(" += ")
                        .Append(literalLength.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }
                    when variable.Variable.Type is SmileType.String:
                    string variableName = identifiers.Get(variable.Variable);
                    string variableLength = exactStringLengths.TryGetValue(
                        variable.Variable,
                        out string? exactLength)
                        ? exactLength
                        : $"strlen({variableName})";
                    source.Append(indent).Append("memcpy(").Append(buffer.Name).Append(" + ")
                        .Append(workLength).Append(", ").Append(variableName).Append(", ")
                        .Append(variableLength).AppendLine(");");
                    source.Append(indent).Append(workLength).Append(" += ")
                        .Append(variableLength).AppendLine(";");
                    break;

                case RuntimeExpressionTextSegment typed when
                    typed.Expression.Type is SmileType.Integer:
                    string integer = TargetExpression.C(
                        typed.Expression,
                        identifiers,
                        integers,
                        new Dictionary<VariableSymbol, SmileValue>(),
                        exactStringLengths,
                        runtimeExpressionBuffers);
                    string integerFormat = integers.RequiresSigned64Storage ? "%lld" : "%d";
                    string integerArgument = integers.RequiresSigned64Storage
                        ? $"(long long)({integer})"
                        : integer;
                    source.Append(indent).Append(workLength).Append(" += (size_t)snprintf(")
                        .Append(buffer.Name).Append(" + ").Append(workLength).Append(", ")
                        .Append((buffer.Capacity + 1).ToString(CultureInfo.InvariantCulture))
                        .Append(" - ").Append(workLength).Append(", \"")
                        .Append(integerFormat).Append("\", ").Append(integerArgument)
                        .AppendLine(");");
                    break;

                case RuntimeExpressionTextSegment typed when
                    typed.Expression.Type is SmileType.Boolean:
                    string boolean = TargetExpression.C(
                        typed.Expression,
                        identifiers,
                        integers,
                        new Dictionary<VariableSymbol, SmileValue>(),
                        exactStringLengths,
                        runtimeExpressionBuffers);
                    source.Append(indent).Append("if (").Append(boolean).AppendLine(")");
                    source.Append(indent).AppendLine("{");
                    AppendCFixedRuntimeText(source, indent + "    ", buffer, workLength, "TRUE");
                    source.Append(indent).AppendLine("}");
                    source.Append(indent).AppendLine("else");
                    source.Append(indent).AppendLine("{");
                    AppendCFixedRuntimeText(source, indent + "    ", buffer, workLength, "FALSE");
                    source.Append(indent).AppendLine("}");
                    break;
            }
        }
    }

    private static void AppendCFixedRuntimeText(
        StringBuilder source,
        string indent,
        RuntimeStringBuffer buffer,
        string workLength,
        string text)
    {
        source.Append(indent).Append("memcpy(").Append(buffer.Name).Append(" + ")
            .Append(workLength).Append(", \"").Append(text).Append("\", ")
            .Append(text.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(");");
        source.Append(indent).Append(workLength).Append(" += ")
            .Append(text.Length.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
    }

    internal static void AppendCDirectStringCopy(
        StringBuilder source,
        string indent,
        VariableSymbol destination,
        VariableSymbol sourceVariable,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        string destinationName = identifiers.Get(destination);
        string sourceName = identifiers.Get(sourceVariable);
        source.AppendLine($"{indent}{destinationName} = {sourceName};");
        if (!exactStringLengths.TryGetValue(destination, out string? destinationLength))
        {
            return;
        }

        string sourceLength = exactStringLengths.TryGetValue(sourceVariable, out string? exactSourceLength)
            ? exactSourceLength
            : $"strlen({sourceName})";
        source.AppendLine($"{indent}{destinationLength} = {sourceLength};");
    }

    internal static int Utf8ByteLength(SmileValue value) =>
        Encoding.UTF8.GetByteCount(value.StringValue);

}
