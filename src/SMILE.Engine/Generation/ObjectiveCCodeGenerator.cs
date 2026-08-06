using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class ObjectiveCCodeGenerator : ICodeGenerator
{
    public TargetLanguage Language => TargetLanguage.ObjectiveC;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        TargetIntegerProfile integers = TargetIntegerProfile.Analyze(program, analysis);
        IReadOnlyDictionary<BoundStatement, CCodeGenerator.RuntimeStringBuffer> runtimeStringBuffers =
            CCodeGenerator.CreateRuntimeStringBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers =
            CCodeGenerator.CreateRuntimeExpressionBuffers(program, identifiers, analysis);
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths =
            CCodeGenerator.CreateExactStringLengthNames(
                program,
                identifiers,
                analysis,
                runtimeStringBuffers.Keys.Select(statement => statement switch
                {
                    BoundLetStatement let => let.Variable,
                    BoundSetStatement set => set.Variable,
                    _ => throw new InvalidOperationException("Unexpected Objective-C runtime String statement.")
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

        foreach (CCodeGenerator.RuntimeStringBuffer buffer in runtimeExpressionBuffers.Values)
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
            new[] { new GeneratedFile("Program.m", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundStatement, CCodeGenerator.RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers,
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
                    // The Windows-local Objective-C toolchain uses Clang/MSYS2
                    // without Foundation. C-compatible console types keep this
                    // target easy to build while still compiling as Objective-C.
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
                            CCodeGenerator.RuntimeStringBuffer buffer = runtimeStringBuffers[let];
                            source.AppendLine(
                                $"{indent}static char {buffer.Name}[{buffer.Capacity + 1}] = {{ 0 }};");
                            source.AppendLine(
                                $"{indent}const char *{identifiers.Get(let.Variable)} = {buffer.Name};");
                            source.AppendLine(
                                $"{indent}size_t {exactStringLengths[let.Variable]} = 0;");
                            CCodeGenerator.AppendCRuntimeStringAssignment(
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
                            : TargetExpression.ObjectiveC(
                                let.Initializer,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{TargetTypes.CDeclaration(let.Variable.Type, identifiers.Get(let.Variable), integers)} = {initializer};");
                        if (exactStringLengths.TryGetValue(let.Variable, out string? letLengthName))
                        {
                            source.AppendLine($"{indent}size_t {letLengthName} = {CCodeGenerator.Utf8ByteLength(letValue)};");
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
                        CCodeGenerator.AppendCDirectStringCopy(
                            source,
                            indent,
                            set.Variable,
                            directSource.Variable,
                            identifiers,
                            exactStringLengths);
                    }
                    else if (set.Variable.Type is SmileType.String && !facts.Value.IsKnown)
                    {
                        CCodeGenerator.AppendCRuntimeStringAssignment(
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
                            : TargetExpression.ObjectiveC(
                                set.Value,
                                identifiers,
                                integers,
                                GeneratorConditionFacts.KnownValues(facts.ValuesBefore),
                                exactStringLengths,
                                runtimeExpressionBuffers);
                        source.AppendLine($"{indent}{identifiers.Get(set.Variable)} = {value};");
                        if (exactStringLengths.TryGetValue(set.Variable, out string? setLengthName))
                        {
                            source.AppendLine($"{indent}{setLengthName} = {CCodeGenerator.Utf8ByteLength(setValue)};");
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

                    AppendObjectiveCPrint(
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
        IReadOnlyDictionary<BoundStatement, CCodeGenerator.RuntimeStringBuffer> runtimeStringBuffers,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers,
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
                .Append(TargetExpression.ObjectiveC(
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

    private static void AppendObjectiveCPrint(
        StringBuilder source,
        string indent,
        BoundPrintStatement print,
        TargetIdentifierMap identifiers,
        TargetIntegerProfile integers,
        bool valueIsKnown,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        IReadOnlyDictionary<VariableSymbol, string> exactStringLengths,
        IReadOnlyDictionary<BoundExpression, CCodeGenerator.RuntimeStringBuffer> runtimeExpressionBuffers)
    {
        if (CCodeGenerator.TryAppendDirectStringVariablePrint(
            source,
            indent,
            print,
            identifiers,
            exactStringLengths))
        {
            return;
        }

        if (!valueIsKnown && CCodeGenerator.TryAppendRuntimeStringSegments(
                source,
                indent,
                print,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers,
                TargetLanguage.ObjectiveC))
        {
            return;
        }

        if (valueIsKnown && CCodeGenerator.TryAppendExactNulStringPrint(source, indent, print, values))
        {
            return;
        }

        CPrintfPlan plan = CPrintfPlan.FromPrint(
            print,
            expression => TargetExpression.ObjectiveC(
                expression,
                identifiers,
                integers,
                values,
                exactStringLengths,
                runtimeExpressionBuffers),
            integers.RequiresSigned64Storage);
        CCodeGenerator.AppendPrintfCall(source, indent, plan);
    }
}
