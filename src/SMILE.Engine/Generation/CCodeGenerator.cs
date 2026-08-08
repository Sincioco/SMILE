using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class CCodeGenerator : ICodeGenerator
{
    // This is a C-target implementation choice, not a SMILE language limit.
    // A small fixed buffer keeps beginner output readable and conventional.
    private const int InputStringBufferSize = 256;

    internal sealed record RuntimeStringBuffer(string Name, int Capacity);

    public TargetLanguage Language => TargetLanguage.C;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        bool hasStringInput = TargetRuntimeFacts.HasInput(program, SmileType.String);
        bool hasBooleanInput = TargetRuntimeFacts.HasInput(program, SmileType.Boolean);
        bool checkedArithmetic = TargetRuntimeFacts.NeedsCheckedIntegerArithmetic(program);
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
                    }),
                nativeInputIsNulTerminated: true);
        var source = new StringBuilder();
        source.AppendLine("#include <stdio.h>");
        if (integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <stdint.h>");
        }

        if (checkedArithmetic && !integers.RequiresSigned64Storage)
        {
            source.AppendLine("#include <limits.h>");
        }

        if (CGenerationFacts.NeedsBooleanHeader(program))
        {
            source.AppendLine("#include <stdbool.h>");
        }

        if (exactStringLengths.Count > 0 ||
            CGenerationFacts.NeedsStringComparison(analysis) ||
            hasStringInput ||
            hasBooleanInput)
        {
            source.AppendLine("#include <string.h>");
        }

        if (hasBooleanInput)
        {
            source.AppendLine("#include <ctype.h>");
        }

        if (checkedArithmetic)
        {
            source.AppendLine("#include <stdlib.h>");
        }

        source.AppendLine();
        CGeneratedRuntime.Append(
            source,
            program,
            integers,
            checkedArithmetic,
            includeInput: false);
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
        AppendSourceItems(
            source,
            program.SourceItems,
            "    ",
            analysis,
            identifiers,
            integers,
            exactStringLengths,
            runtimeStringBuffers,
            runtimeExpressionBuffers,
            checkedArithmetic,
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

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        bool checkedArithmetic,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            if (sourceItem is BoundFullLineComment comment)
            {
                TargetComments.Append(source, TargetLanguage.C, indent, comment.Payload);
                continue;
            }

            if (sourceItem is BoundBlankLine)
            {
                source.AppendLine();
                continue;
            }

            var statement = (BoundStatement)sourceItem;
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    if (let.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        // SMILE Strings have value semantics. Copy an Unknown
                        // initializer into storage owned by the new variable;
                        // pointing at a mutable INPUT buffer would let a later
                        // INPUT silently change this LET without an assignment.
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
                            declareBuffer: false,
                            checkedArithmetic);
                    }
                    else
                    {
                        SmileValue letValue = let.Variable.Type is SmileType.String
                            ? facts.Value.Value
                            : default;
                        if (let.Variable.Type is SmileType.String)
                        {
                            AppendCStringAssignment(
                                source,
                                indent,
                                TargetTypes.CDeclaration(
                                    let.Variable.Type,
                                    identifiers.Get(let.Variable),
                                    integers),
                                letValue.StringValue);
                        }
                        else
                        {
                            string initializer = TargetExpression.C(
                                let.Initializer,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers,
                                checkedArithmetic);
                            source.AppendLine($"{indent}{TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                        }

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
                        set.Value is BoundVariableExpression directSource &&
                        ReferenceEquals(directSource.Variable, set.Variable))
                    {
                        // Preserve direct self-assignment as a real storage
                        // update without copying a buffer onto itself.
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
                                declareBuffer: true,
                                checkedArithmetic);
                    }
                    else
                    {
                        SmileValue setValue = facts.Value.IsKnown
                            ? facts.Value.Value
                            : default;
                        if (set.Variable.Type is SmileType.String)
                        {
                            AppendCStringAssignment(
                                source,
                                indent,
                                identifiers.Get(set.Variable),
                                setValue.StringValue);
                        }
                        else
                        {
                            string value = TargetExpression.C(
                                set.Value,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers,
                                checkedArithmetic);
                            source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {value};");
                        }

                        if (exactStringLengths.TryGetValue(set.Variable, out string? setLengthName))
                        {
                            source.AppendLine($"{indent}{setLengthName} = {Utf8ByteLength(setValue)};");
                        }
                    }

                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundInputStatement input:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendNativeInputStatement(
                        source,
                        indent,
                        input,
                        facts.Ordinal,
                        identifiers,
                        integers,
                        exactStringLengths);
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
                        runtimeExpressionBuffers,
                        checkedArithmetic);
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
                        checkedArithmetic,
                        ref emittedDeclaration,
                        ref emittedExecutable,
                        ref emittedBodyStatement);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;

                case BoundWhileStatement loop:
                    if (!emittedExecutable && emittedDeclaration)
                    {
                        source.AppendLine();
                    }

                    AppendWhileStatement(
                        source,
                        loop,
                        indent,
                        analysis,
                        identifiers,
                        integers,
                        exactStringLengths,
                        runtimeStringBuffers,
                        runtimeExpressionBuffers,
                        checkedArithmetic,
                        ref emittedDeclaration,
                        ref emittedExecutable,
                        ref emittedBodyStatement);
                    emittedExecutable = true;
                    emittedBodyStatement = true;
                    break;
            }
        }
    }

    private static void AppendNativeInputStatement(
        StringBuilder source,
        string indent,
        BoundInputStatement input,
        int ordinal,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths)
    {
        string name = identifiers.Get(input.Variable);
        switch (input.Variable.Type)
        {
            case SmileType.String:
            {
                string buffer = $"smileInput{ordinal}Buffer";
                source.Append(indent).AppendLine("{");
                source.Append(indent).Append("    static char ").Append(buffer).Append('[')
                    .Append(InputStringBufferSize.ToString(CultureInfo.InvariantCulture))
                    .AppendLine("];");
                source.Append(indent).Append("    if (fgets(").Append(buffer).Append(", sizeof ")
                    .Append(buffer).AppendLine(", stdin) == NULL) return 1;");
                source.Append(indent).Append("    ").Append(buffer).Append("[strcspn(")
                    .Append(buffer).AppendLine(", \"\\r\\n\")] = '\\0';");
                source.Append(indent).Append("    ").Append(name).Append(" = ")
                    .Append(buffer).AppendLine(";");
                if (exactStringLengths.TryGetValue(input.Variable, out string? lengthName))
                {
                    source.Append(indent).Append("    ").Append(lengthName).Append(" = strlen(")
                        .Append(name).AppendLine(");");
                }

                source.Append(indent).AppendLine("}");
                break;
            }

            case SmileType.Integer:
                if (integers.RequiresSigned64Storage)
                {
                    string inputValue = $"smileInput{ordinal}Value";
                    source.Append(indent).AppendLine("{");
                    source.Append(indent).Append("    long long ").Append(inputValue).AppendLine(";");
                    source.Append(indent).Append("    if (scanf(\"%lld%*[\\r\\n]\", &").Append(inputValue)
                        .AppendLine(") != 1) return 1;");
                    source.Append(indent).Append("    ").Append(name).Append(" = (int64_t)")
                        .Append(inputValue).AppendLine(";");
                    source.Append(indent).AppendLine("}");
                }
                else
                {
                    source.Append(indent).Append("if (scanf(\"%d%*[\\r\\n]\", &").Append(name)
                        .AppendLine(") != 1) return 1;");
                }

                break;

            case SmileType.Boolean:
            {
                string buffer = $"smileInput{ordinal}Buffer";
                string index = $"smileInput{ordinal}Index";
                source.Append(indent).AppendLine("{");
                source.Append(indent).Append("    char ").Append(buffer).AppendLine("[6];");
                source.Append(indent).Append("    if (scanf(\"%5s%*[\\r\\n]\", ").Append(buffer)
                    .AppendLine(") != 1) return 1;");
                source.Append(indent).Append("    for (size_t ").Append(index).Append(" = 0; ")
                    .Append(buffer).Append('[').Append(index).Append("] != '\\0'; ++")
                    .Append(index).AppendLine(")");
                source.Append(indent).Append("        ").Append(buffer).Append('[').Append(index)
                    .Append("] = (char)toupper((unsigned char)").Append(buffer).Append('[')
                    .Append(index).AppendLine("]);");
                source.Append(indent).Append("    if (strcmp(").Append(buffer)
                    .Append(", \"TRUE\") == 0) ").Append(name).AppendLine(" = true;");
                source.Append(indent).Append("    else if (strcmp(").Append(buffer)
                    .Append(", \"FALSE\") == 0) ").Append(name).AppendLine(" = false;");
                source.Append(indent).AppendLine("    else return 1;");
                source.Append(indent).AppendLine("}");
                break;
            }

            default:
                throw new InvalidOperationException("Unsupported C INPUT target type.");
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
        bool checkedArithmetic,
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
                    runtimeExpressionBuffers,
                    checkedArithmetic))
                .AppendLine(")");
            source.Append(indent).AppendLine("{");
            AppendSourceItems(
                source,
                clause.SourceItems,
                indent + "    ",
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                checkedArithmetic,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
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
                analysis,
                identifiers,
                integers,
                exactStringLengths,
                runtimeStringBuffers,
                runtimeExpressionBuffers,
                checkedArithmetic,
                ref emittedDeclaration,
                ref emittedExecutable,
                ref emittedBodyStatement);
            source.Append(indent).AppendLine("}");
        }
    }

    private static void AppendWhileStatement(
        StringBuilder source,
        BoundWhileStatement loop,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        bool checkedArithmetic,
        ref bool emittedDeclaration,
        ref bool emittedExecutable,
        ref bool emittedBodyStatement)
    {
        BoundWhileStatementAnalysis loopFacts = analysis.GetWhileFacts(loop);
        source.Append(indent).Append("while (")
            .Append(TargetExpression.C(
                loop.Condition,
                identifiers,
                integers,
                GeneratorConditionFacts.KnownValues(loopFacts.ValuesAtHead),
                exactStringLengths,
                runtimeExpressionBuffers,
                checkedArithmetic))
            .AppendLine(")");
        source.Append(indent).AppendLine("{");
        AppendSourceItems(
            source,
            loop.SourceItems,
            indent + "    ",
            analysis,
            identifiers,
            integers,
            exactStringLengths,
            runtimeStringBuffers,
            runtimeExpressionBuffers,
            checkedArithmetic,
            ref emittedDeclaration,
            ref emittedExecutable,
            ref emittedBodyStatement);
        source.Append(indent).AppendLine("}");
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
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        bool checkedArithmetic)
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

        if (!valueIsKnown && TryAppendAtomicRuntimeStringPrint(
                source,
                indent,
                print,
                identifiers,
                integers,
                exactStringLengths,
                runtimeExpressionBuffers,
                checkedArithmetic))
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
                TargetLanguage.C,
                checkedArithmetic,
                forceSequential: checkedArithmetic))
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
                runtimeExpressionBuffers,
                checkedArithmetic),
            integers.RequiresSigned64Storage);
        AppendPrintfCall(source, indent, plan);
    }

    internal static bool TryAppendAtomicRuntimeStringPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        bool checkedArithmetic)
    {
        if (print.IsBlankLine ||
            print.Value.Type is not SmileType.String ||
            !runtimeExpressionBuffers.TryGetValue(print.Value, out RuntimeStringBuffer? buffer))
        {
            return false;
        }

        string workLength = buffer.Name + "Used";
        source.Append(indent).Append(workLength).AppendLine(" = 0;");
        AppendCRuntimeTextSegments(
            source,
            indent,
            print.Value,
            buffer,
            workLength,
            identifiers,
            integers,
            exactStringLengths,
            runtimeExpressionBuffers,
            checkedArithmetic);
        source.Append(indent).Append(buffer.Name).Append('[')
            .Append(workLength).AppendLine("] = '\\0';");
        source.Append(indent).Append("fwrite(").Append(buffer.Name).Append(", 1, ")
            .Append(workLength).AppendLine(", stdout);");
        source.Append(indent).AppendLine("fputc('\\n', stdout);");
        return true;
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
        TargetLanguage language,
        bool checkedArithmetic = false,
        bool forceSequential = false)
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
        if (!needsExactStreaming && !forceSequential)
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
                            runtimeExpressionBuffers,
                            checkedArithmetic)
                        : TargetExpression.C(
                            expression.Expression,
                            identifiers,
                            integers,
                        values,
                        exactStringLengths,
                        runtimeExpressionBuffers,
                        checkedArithmetic);
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
        // C concatenates adjacent ordinary literals during translation. Split
        // a genuinely multiline PRINT format at its logical LF boundaries so
        // learners can see the same lines in generated source without relying
        // on a non-standard quote-spanning literal. A normal one-line PRINT
        // owns one final LF and keeps the established compact form.
        if (plan.FormatText.Count(value => value == '\n') > 1)
        {
            source.Append(indent).AppendLine("printf(");
            AppendCStringFragments(
                source,
                indent + "    ",
                plan.FormatText,
                suffix: plan.Arguments.Count == 0 ? ");" : ",");

            for (int index = 0; index < plan.Arguments.Count; index++)
            {
                source.Append(indent).Append("    ").Append(plan.Arguments[index])
                    .AppendLine(index == plan.Arguments.Count - 1 ? ");" : ",");
            }

            return;
        }

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

    /// <summary>
    /// Emits one C assignment while giving semantic multiline String values a
    /// conventional adjacent-literal representation. Every embedded LF stays
    /// an explicit <c>\n</c>, so generated-file line endings cannot alter the
    /// value compiled by C or by the Foundation-free Objective-C backend.
    /// </summary>
    internal static void AppendCStringAssignment(
        StringBuilder source,
        string indent,
        string destination,
        string value)
    {
        if (!value.Contains('\n', StringComparison.Ordinal))
        {
            source.Append(indent).Append(destination).Append(" = ")
                .Append(TargetEscapes.CString(value)).AppendLine(";");
            return;
        }

        source.Append(indent).Append(destination).AppendLine(" =");
        AppendCStringFragments(source, indent + "    ", value, suffix: ";");
    }

    private static void AppendCStringFragments(
        StringBuilder source,
        string indent,
        string value,
        string suffix)
    {
        IReadOnlyList<string> fragments = CreateCStringFragments(value);
        for (int index = 0; index < fragments.Count; index++)
        {
            source.Append(indent).Append(TargetEscapes.CString(fragments[index]));
            if (index == fragments.Count - 1)
            {
                source.Append(suffix);
            }

            source.AppendLine();
        }
    }

    private static IReadOnlyList<string> CreateCStringFragments(string value)
    {
        var fragments = new List<string>();
        int fragmentStart = 0;

        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] != '\n')
            {
                continue;
            }

            // Keep the explicit LF on the preceding physical fragment. This
            // mirrors the learner's line boundaries and avoids a redundant
            // empty literal when the semantic value ends in LF.
            fragments.Add(value[fragmentStart..(index + 1)]);
            fragmentStart = index + 1;
        }

        if (fragmentStart < value.Length || fragments.Count == 0)
        {
            fragments.Add(value[fragmentStart..]);
        }

        return fragments;
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
        IEnumerable<VariableSymbol>? additionalVariables = null,
        bool nativeInputIsNulTerminated = false)
    {
        var names = new Dictionary<VariableSymbol, string>();
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.Ordinal);
        IReadOnlySet<VariableSymbol> additional = additionalVariables?.ToHashSet() ??
            new HashSet<VariableSymbol>();
        var sourceNulVariables = analysis.AssignedValues
            .Where(pair => pair.Value.Any(value =>
                value.Type is SmileType.String &&
                value.StringValue.Contains('\0', StringComparison.Ordinal)))
            .Select(pair => pair.Key)
            .ToHashSet();

        bool ContainsSourceNul(BoundExpression expression) =>
            expression switch
            {
                BoundStringLiteralExpression literal =>
                    literal.Value.Contains('\0', StringComparison.Ordinal),
                BoundVariableExpression variable => sourceNulVariables.Contains(variable.Variable),
                BoundUnaryExpression unary => ContainsSourceNul(unary.Operand),
                BoundBinaryExpression binary =>
                    ContainsSourceNul(binary.Left) || ContainsSourceNul(binary.Right),
                BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                    part switch
                    {
                        BoundInterpolatedTextPart text =>
                            text.Text.Contains('\0', StringComparison.Ordinal),
                        BoundInterpolationExpressionPart hole => ContainsSourceNul(hole.Expression),
                        _ => false
                    }),
                _ => false
            };

        if (nativeInputIsNulTerminated)
        {
            (VariableSymbol Variable, BoundExpression Value)[] assignments =
                BoundStatementTree.Enumerate(program)
                    .Select(statement => statement switch
                    {
                        BoundLetStatement let => (let.Variable, let.Initializer),
                        BoundSetStatement set => (set.Variable, set.Value),
                        _ => default
                    })
                    .Where(assignment => assignment.Variable is not null)
                    .ToArray()!;
            bool changed;
            do
            {
                changed = false;
                foreach ((VariableSymbol variable, BoundExpression value) in assignments)
                {
                    if (variable.Type is SmileType.String &&
                        !sourceNulVariables.Contains(variable) &&
                        ContainsSourceNul(value))
                    {
                        sourceNulVariables.Add(variable);
                        changed = true;
                    }
                }
            }
            while (changed);
        }

        for (int index = 0; index < program.Variables.Count; index++)
        {
            VariableSymbol variable = program.Variables[index];
            bool mayNeedExactLength = analysis.AssignedValuesMayContainNul(variable) &&
                (!nativeInputIsNulTerminated || sourceNulVariables.Contains(variable));
            if (variable.Type is not SmileType.String ||
                !mayNeedExactLength &&
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
                    !facts.Value.IsKnown:
                    needsBuffer.Add((let, let.Variable));
                    break;

                case BoundSetStatement { Variable.Type: SmileType.String } set when
                    !facts.Value.IsKnown &&
                    (set.Value is not BoundVariableExpression directSource ||
                     !ReferenceEquals(directSource.Variable, set.Variable)):
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
                    if (!facts.Value.IsKnown &&
                        TargetRuntimeFacts.ContainsIntegerArithmetic(print.Value))
                    {
                        Add(print.Value, facts.ValuesBefore);
                    }

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

                case BoundWhileStatement loop:
                    Collect(
                        loop.Condition,
                        analysis.GetWhileFacts(loop).ValuesAtHead);
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
        bool declareBuffer,
        bool checkedArithmetic = false)
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
            runtimeExpressionBuffers,
            checkedArithmetic);
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
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeExpressionBuffers,
        bool checkedArithmetic)
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
                        runtimeExpressionBuffers,
                        checkedArithmetic);
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
                        runtimeExpressionBuffers,
                        checkedArithmetic);
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
