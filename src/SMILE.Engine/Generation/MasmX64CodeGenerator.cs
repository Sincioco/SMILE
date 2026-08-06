using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class MasmX64CodeGenerator : ICodeGenerator
{
    private const string IntegerFormatBufferLabel = "smileIntegerFormatBuffer";
    private const string IntegerFormatProcedure = "smileFormatInteger";

    private sealed record RuntimeStringBuffer(
        BoundExpression Expression,
        string Label,
        int Capacity);

    public TargetLanguage Language => TargetLanguage.MasmX64;

    public GeneratedProgram Generate(BoundProgram program)
    {
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        BoundPrintStatement[] prints = analysis.EnumerateStatements()
            .OfType<BoundPrintStatement>()
            .ToArray();
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes = lets
            .Select((let, index) => (let.Variable, index))
            .ToDictionary(item => item.Variable, item => item.index);
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers =
            CreateMasmStatementBuffers(analysis);
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>>
            conditionBuffers = CreateMasmConditionBuffers(analysis);
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers =
            CreateMasmBooleanStringBuffers(analysis);
        bool needsIntegerFormatter = NeedsMasmIntegerFormatter(
            analysis,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers);
        bool needsBooleanText = NeedsMasmBooleanText(
            analysis,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers);
        var source = new StringBuilder();

        AppendMasmLine(source, "option casemap:none", "Keep symbol names case-sensitive.");
        source.AppendLine();

        if (prints.Length > 0)
        {
            AppendMasmLine(source, "EXTERN GetStdHandle:PROC", "Windows API: get standard console handles.");
            AppendMasmLine(source, "EXTERN WriteFile:PROC", "Windows API: write bytes to the console.");
        }

        AppendMasmLine(source, "EXTERN ExitProcess:PROC", "Windows API: terminate the process.");
        source.AppendLine();

        AppendMasmData(
            source,
            analysis,
            variableIndexes,
            prints.Length,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers,
            needsIntegerFormatter,
            needsBooleanText);
        AppendMasmCode(
            source,
            program,
            analysis,
            variableIndexes,
            prints.Length,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers,
            needsIntegerFormatter);

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.asm", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
    }

    private static void AppendMasmData(
        StringBuilder source,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        int printCount,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        bool needsIntegerFormatter,
        bool needsBooleanText)
    {
        if (variableIndexes.Count == 0 &&
            printCount == 0 &&
            statementBuffers.Count == 0 &&
            conditionBuffers.Values.All(buffers => buffers.Count == 0) &&
            booleanStringBuffers.Count == 0 &&
            !needsIntegerFormatter &&
            !needsBooleanText)
        {
            return;
        }

        AppendMasmLine(source, ".data", "Static bytes and variables live here.");

        if (printCount > 0)
        {
            AppendMasmLine(source, "STD_OUTPUT_HANDLE EQU -11", "Magic value for the console output handle.");
        }

        int printIndex = 0;
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement let:
                    int variableIndex = variableIndexes[let.Variable];
                    string valueLabel = VariableValueLabel(variableIndex);
                    string initialText = facts.Value.IsKnown
                        ? facts.Value.Value.ToDisplayText()
                        : string.Empty;
                    AppendMasmStringData(
                        source,
                        valueLabel,
                        initialText,
                        $"LET {let.Variable.Name} initial text.",
                        "Length of the variable's current text.");
                    AppendMasmLine(source, $"{VariablePointerLabel(variableIndex)} QWORD ?", $"Runtime pointer for {let.Variable.Name}.");
                    AppendMasmLine(source, $"{VariableLengthLabel(variableIndex)} DWORD ?", $"Runtime length for {let.Variable.Name}.");
                    if (let.Variable.Type is SmileType.Integer)
                    {
                        AppendMasmLine(
                            source,
                            $"{VariableIntegerLabel(variableIndex)} QWORD ?",
                            $"Runtime signed Integer value for {let.Variable.Name} conditions.");
                    }
                    else if (let.Variable.Type is SmileType.Boolean)
                    {
                        AppendMasmLine(
                            source,
                            $"{VariableBooleanLabel(variableIndex)} BYTE ?",
                            $"Runtime Boolean value for {let.Variable.Name} expressions.");
                    }

                    break;

                case BoundSetStatement set:
                    if (!facts.Value.IsKnown)
                    {
                        // Runtime lowering below reads current storage or
                        // materializes the complete expression on its reached
                        // path. Never bake the selected concrete branch here.
                        break;
                    }

                    string setText = facts.Value.Value.ToDisplayText();
                    AppendMasmStringData(
                        source,
                        SetValueLabel(facts.Ordinal),
                        setText,
                        $"SET {set.Variable.Name} assigned text.",
                        "Length of this assigned value.");
                    break;

                case BoundPrintStatement print:
                    AppendMasmPrintData(source, print, facts, printIndex);

                    printIndex++;
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        int comparisonIndex = 0;
                        AppendMasmConditionData(
                            source,
                            clause.Condition,
                            analysis.GetClauseFacts(clause),
                            conditionBuffers[clause],
                            ref comparisonIndex);
                    }

                    break;
            }
        }

        foreach (RuntimeStringBuffer buffer in statementBuffers.Values
                     .Concat(conditionBuffers.Values.SelectMany(value => value))
                     .Concat(booleanStringBuffers.Values))
        {
            AppendMasmLine(
                source,
                $"{buffer.Label} BYTE {buffer.Capacity} DUP (?)",
                "Stable runtime text storage for one source expression.");
            AppendMasmLine(
                source,
                $"{buffer.Label}Length DWORD ?",
                "Logical UTF-8 byte length of this runtime text.");
        }

        if (needsIntegerFormatter)
        {
            AppendMasmLine(
                source,
                $"{IntegerFormatBufferLabel} BYTE 21 DUP (?)",
                "Temporary signed Int64 decimal text (sign plus 19 digits).");
        }

        if (needsBooleanText)
        {
            AppendMasmStringData(
                source,
                "smileBooleanTrue",
                "TRUE",
                "Canonical runtime Boolean true text.",
                "Length of canonical true text.");
            AppendMasmStringData(
                source,
                "smileBooleanFalse",
                "FALSE",
                "Canonical runtime Boolean false text.",
                "Length of canonical false text.");
        }

        if (printCount > 0)
        {
            AppendMasmLine(source, "newline BYTE 13, 10", "SMILE PRINT appends CR/LF on Windows.");
            AppendMasmLine(source, "newlineLength EQU $ - newline", "Length of the newline bytes.");
            AppendMasmLine(source, "stdoutHandle QWORD ?", "Cached standard output handle.");
            AppendMasmLine(source, "bytesWritten DWORD ?", "WriteFile stores how many bytes it wrote.");
        }

        source.AppendLine();
    }

    private static void AppendMasmPrintData(
        StringBuilder source,
        BoundPrintStatement print,
        BoundStatementAnalysis facts,
        int printIndex)
    {
        if (!print.IsBlankLine && print.Value is BoundVariableExpression)
        {
            return;
        }

        if (print.IsBlankLine || facts.Value.IsKnown)
        {
            string text = print.IsBlankLine
                ? string.Empty
                : facts.Value.Value.ToDisplayText();
            AppendMasmStringData(
                source,
                PrintLiteralLabel(printIndex, 0),
                text,
                $"PRINT #{printIndex + 1} canonical text.",
                "Length of this print text.");
            return;
        }

        IReadOnlyList<RuntimeTextSegment> segments = RuntimeTextPlan.Flatten(print.Value);
        for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            if (segments[segmentIndex] is not RuntimeLiteralTextSegment { Text.Length: > 0 } literal)
            {
                continue;
            }

            AppendMasmStringData(
                source,
                PrintLiteralLabel(printIndex, segmentIndex),
                literal.Text,
                $"PRINT #{printIndex + 1} literal segment.",
                "Length of this print segment.");
        }
    }

    private static void AppendMasmStringData(
        StringBuilder source,
        string label,
        string value,
        string valueComment,
        string lengthComment)
    {
        AppendMasmLine(source, $"{label} BYTE {TargetEscapes.MasmByteInitializers(value)}", valueComment);

        // MASM needs at least one byte after a BYTE label, so the empty string
        // uses a 0 placeholder for storage. The logical SMILE string length is
        // still zero; otherwise WriteFile would emit an invisible NUL byte.
        string lengthExpression = Encoding.UTF8.GetByteCount(value) == 0
            ? "0"
            : $"$ - {label}";
        AppendMasmLine(source, $"{label}Length EQU {lengthExpression}", lengthComment);
    }

    private static void AppendMasmConditionData(
        StringBuilder source,
        BoundExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyList<RuntimeStringBuffer> runtimeBuffers,
        ref int comparisonIndex)
    {
        if (expression is BoundUnaryExpression unary)
        {
            AppendMasmConditionData(
                source,
                unary.Operand,
                clauseFacts,
                runtimeBuffers,
                ref comparisonIndex);
            return;
        }

        if (expression is not BoundBinaryExpression binary)
        {
            return;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            AppendMasmConditionData(
                source,
                binary.Left,
                clauseFacts,
                runtimeBuffers,
                ref comparisonIndex);
            AppendMasmConditionData(
                source,
                binary.Right,
                clauseFacts,
                runtimeBuffers,
                ref comparisonIndex);
            return;
        }

        if (!CanEmitMasmDirectEquality(binary))
        {
            return;
        }

        int currentComparison = comparisonIndex++;
        AppendMasmConditionOperandData(
            source,
            binary.Left,
            clauseFacts,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, currentComparison, "Left"));
        AppendMasmConditionOperandData(
            source,
            binary.Right,
            clauseFacts,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, currentComparison, "Right"));
    }

    private static void AppendMasmConditionOperandData(
        StringBuilder source,
        BoundExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyList<RuntimeStringBuffer> runtimeBuffers,
        string label)
    {
        if (expression is BoundVariableExpression ||
            runtimeBuffers.Any(buffer => ReferenceEquals(buffer.Expression, expression)))
        {
            return;
        }

        string text = expression switch
        {
            BoundStringLiteralExpression literal => literal.Value,
            BoundIntegerLiteralExpression literal =>
                literal.Value.ToString(CultureInfo.InvariantCulture),
            BoundBooleanLiteralExpression literal => literal.Value ? "TRUE" : "FALSE",
            _ => throw new InvalidOperationException(
                "A static MASM IF operand must be a bound literal.")
        };
        AppendMasmStringData(
            source,
            label,
            text,
            "Static operand for a runtime IF comparison.",
            "Length of this IF operand.");
    }

    private static void AppendMasmCode(
        StringBuilder source,
        BoundProgram program,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        int printCount,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        bool needsIntegerFormatter)
    {
        AppendMasmLine(source, ".code", "CPU instructions live here.");
        AppendMasmLine(source, "main PROC", "Program entry point.");
        AppendMasmLine(source, "    sub rsp, 28h", "Reserve Win64 shadow space and align the stack.");

        if (printCount > 0)
        {
            source.AppendLine();
            AppendMasmLine(source, "    mov ecx, STD_OUTPUT_HANDLE", "Ask Windows for stdout.");
            AppendMasmLine(source, "    call GetStdHandle", "RAX receives the stdout handle.");
            AppendMasmLine(source, "    mov QWORD PTR [stdoutHandle], rax", "Cache stdout for every PRINT segment.");
        }

        int printIndex = 0;
        AppendMasmSourceItems(
            source,
            program.SourceItems,
            analysis,
            variableIndexes,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers,
            ref printIndex);

        source.AppendLine();
        AppendMasmLine(source, "    xor ecx, ecx", "ExitProcess arg 1: process exit code 0.");
        AppendMasmLine(source, "    call ExitProcess", "End the program.");
        AppendMasmLine(source, "main ENDP", "End of the main procedure.");
        if (needsIntegerFormatter)
        {
            source.AppendLine();
            AppendMasmIntegerFormatter(source);
        }

        source.AppendLine();
        source.AppendLine("END");
    }

    private static void AppendMasmSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int printIndex)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            if (sourceItem is BoundFullLineComment comment)
            {
                // User layout belongs in the instruction stream. Static
                // storage remains generator-owned so comments are never
                // duplicated into .data.
                TargetComments.Append(source, TargetLanguage.MasmX64, "    ", comment.Payload);
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
                    if (!facts.Value.IsKnown &&
                        let.Initializer is BoundVariableExpression directLetSource)
                    {
                        AppendMasmStorageCopy(
                            source,
                            variableIndexes[let.Variable],
                            variableIndexes[directLetSource.Variable],
                            let.Variable.Type,
                            let.Variable.Name,
                            directLetSource.Variable.Name);
                    }
                    else if (!facts.Value.IsKnown)
                    {
                        AppendMasmRuntimeAssignment(
                            source,
                            let.Variable,
                            let.Initializer,
                            statementBuffers[let],
                            variableIndexes,
                            $"let{facts.Ordinal}",
                            booleanStringBuffers);
                    }
                    else
                    {
                        AppendMasmStorageUpdate(
                            source,
                            variableIndexes[let.Variable],
                            let.Variable,
                            facts.Value.Value,
                            VariableValueLabel(variableIndexes[let.Variable]),
                            $"Address of LET {let.Variable.Name} text.");
                    }

                    break;

                case BoundSetStatement set:
                    if (!facts.Value.IsKnown && set.Value is BoundVariableExpression directSource)
                    {
                        AppendMasmStorageCopy(
                            source,
                            variableIndexes[set.Variable],
                            variableIndexes[directSource.Variable],
                            set.Variable.Type,
                            set.Variable.Name,
                            directSource.Variable.Name);
                    }
                    else if (!facts.Value.IsKnown)
                    {
                        AppendMasmRuntimeAssignment(
                            source,
                            set.Variable,
                            set.Value,
                            statementBuffers[set],
                            variableIndexes,
                            $"set{facts.Ordinal}",
                            booleanStringBuffers);
                    }
                    else
                    {
                        AppendMasmStorageUpdate(
                            source,
                            variableIndexes[set.Variable],
                            set.Variable,
                            facts.Value.Value,
                            SetValueLabel(facts.Ordinal),
                            $"Address of SET {set.Variable.Name} text.");
                    }

                    break;

                case BoundPrintStatement print:
                    AppendMasmPrint(
                        source,
                        print,
                        facts,
                        printIndex,
                        variableIndexes,
                        $"print{printIndex}",
                        booleanStringBuffers);
                    printIndex++;
                    break;

                case BoundIfStatement conditional:
                    AppendMasmIf(
                        source,
                        conditional,
                        analysis,
                        variableIndexes,
                        statementBuffers,
                        conditionBuffers,
                        booleanStringBuffers,
                        ref printIndex);
                    break;
            }
        }
    }

    private static void AppendMasmIf(
        StringBuilder source,
        BoundIfStatement conditional,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int printIndex)
    {
        int ifOrdinal = analysis.GetIfOrdinal(conditional);
        string endLabel = IfEndLabel(ifOrdinal);

        source.AppendLine();
        AppendMasmLine(source, $"; IF #{ifOrdinal + 1}", "Evaluate clauses in source order.");
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            BoundConditionalClauseAnalysis clauseFacts = analysis.GetClauseFacts(clause);
            if (clauseIndex > 0)
            {
                AppendMasmLine(source, $"{IfClauseLabel(ifOrdinal, clauseIndex)}:", "Next ELSE IF clause.");
            }

            string falseLabel = clauseIndex + 1 < conditional.Clauses.Count
                ? IfClauseLabel(ifOrdinal, clauseIndex + 1)
                : conditional.HasElseClause
                    ? IfElseLabel(ifOrdinal)
                    : endLabel;
            int comparisonIndex = 0;
            int partIndex = 0;
            var runtimeBufferMap = new Dictionary<BoundExpression, RuntimeStringBuffer>(
                ReferenceEqualityComparer.Instance);
            foreach (RuntimeStringBuffer buffer in conditionBuffers[clause])
            {
                runtimeBufferMap.Add(buffer.Expression, buffer);
            }

            AppendMasmCondition(
                source,
                clause.Condition,
                clauseFacts,
                variableIndexes,
                runtimeBufferMap,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            AppendMasmLine(source, "    test eax, eax", "Zero means this clause did not match.");
            AppendMasmLine(source, $"    jz {falseLabel}", "Continue with the next clause or ELSE.");
            AppendMasmSourceItems(
                source,
                clause.SourceItems,
                analysis,
                variableIndexes,
                statementBuffers,
                conditionBuffers,
                booleanStringBuffers,
                ref printIndex);
            AppendMasmLine(source, $"    jmp {endLabel}", "Only one IF branch executes.");
        }

        if (conditional.HasElseClause)
        {
            AppendMasmLine(source, $"{IfElseLabel(ifOrdinal)}:", "Final ELSE branch.");
            AppendMasmSourceItems(
                source,
                conditional.ElseSourceItems,
                analysis,
                variableIndexes,
                statementBuffers,
                conditionBuffers,
                booleanStringBuffers,
                ref printIndex);
        }

        AppendMasmLine(source, $"{endLabel}:", "Continue after the complete IF.");
    }

    private static void AppendMasmCondition(
        StringBuilder source,
        BoundExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int comparisonIndex,
        ref int partIndex)
    {
        if (expression is BoundUnaryExpression
            {
                Operator.Kind: BoundUnaryOperatorKind.LogicalNegation
            } unary)
        {
            AppendMasmCondition(
                source,
                unary.Operand,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            AppendMasmLine(source, "    xor eax, 1", "Invert the normalized Boolean condition result.");
            return;
        }

        if (expression is BoundBinaryExpression binary &&
            binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr)
        {
            AppendMasmCondition(
                source,
                binary.Left,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            string endLabel = MasmConditionPartLabel(
                clauseFacts.Ordinal,
                partIndex++,
                binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd ? "AndEnd" : "OrEnd");
            AppendMasmLine(source, "    test eax, eax", "Honor SMILE's left-to-right short circuit.");
            AppendMasmLine(
                source,
                binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                    ? $"    jz {endLabel}"
                    : $"    jnz {endLabel}",
                "Skip the unreachable right condition.");
            AppendMasmCondition(
                source,
                binary.Right,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            AppendMasmLine(source, $"{endLabel}:", "Complete this logical condition.");
            return;
        }

        if (expression is BoundBinaryExpression integerComparison &&
            CanEmitMasmDirectIntegerComparison(integerComparison))
        {
            AppendMasmDirectIntegerComparison(
                source,
                integerComparison,
                variableIndexes);
            return;
        }

        if (expression is BoundBinaryExpression booleanComparison &&
            booleanComparison.Left.Type is SmileType.Boolean &&
            booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality)
        {
            AppendMasmBooleanExpression(
                source,
                booleanComparison,
                variableIndexes,
                $"ifCondition{clauseFacts.Ordinal}",
                booleanStringBuffers,
                ref partIndex);
            return;
        }

        if (expression is BoundBinaryExpression comparison &&
            CanEmitMasmDirectEquality(comparison))
        {
            foreach (BoundExpression operand in new[] { comparison.Left, comparison.Right })
            {
                if (runtimeBuffers.TryGetValue(operand, out RuntimeStringBuffer? buffer))
                {
                    AppendMasmRuntimeTextMaterialization(
                        source,
                        operand,
                        buffer,
                        variableIndexes,
                        $"ifCondition{clauseFacts.Ordinal}Part{partIndex}",
                        booleanStringBuffers,
                        ref partIndex);
                }
            }

            AppendMasmDirectEquality(
                source,
                comparison,
                clauseFacts,
                variableIndexes,
                runtimeBuffers,
                comparisonIndex++,
                ref partIndex);
            return;
        }

        if (!GeneratorConditionFacts.TryEvaluateFromAnalyzedValues(
                expression,
                clauseFacts.ValuesBefore,
                out SmileValue provenCondition))
        {
            throw new InvalidOperationException(
                "MASM requires runtime lowering for an abstract-unknown IF condition.");
        }

        AppendMasmLine(
            source,
            $"    mov eax, {(provenCondition.BooleanValue ? 1 : 0)}",
            "Materialize an unsupported proven condition without deleting its branch.");
    }

    private static bool CanEmitMasmDirectEquality(BoundBinaryExpression expression) =>
        (expression.Operator.Kind is BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality) &&
        expression.Left.Type is not SmileType.Integer &&
        IsMasmDirectConditionOperand(expression.Left) &&
        IsMasmDirectConditionOperand(expression.Right) &&
        (ContainsVariableRead(expression.Left) || ContainsVariableRead(expression.Right));

    private static bool IsMasmDirectConditionOperand(BoundExpression expression) =>
        expression.Type is SmileType.String
            ? RuntimeTextPlan.CanFlatten(expression)
            : expression is BoundVariableExpression or
            BoundStringLiteralExpression or
            BoundIntegerLiteralExpression or
            BoundBooleanLiteralExpression;

    private static bool ContainsVariableRead(BoundExpression expression) =>
        expression switch
        {
            BoundVariableExpression => true,
            BoundUnaryExpression unary => ContainsVariableRead(unary.Operand),
            BoundBinaryExpression binary =>
                ContainsVariableRead(binary.Left) || ContainsVariableRead(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                ContainsVariableRead(hole.Expression)),
            _ => false
        };

    private static bool CanEmitMasmDirectIntegerComparison(BoundBinaryExpression expression) =>
        expression.Left.Type is SmileType.Integer &&
        (expression.Operator.Kind is BoundBinaryOperatorKind.Equality or
            BoundBinaryOperatorKind.Inequality or
            BoundBinaryOperatorKind.Less or
            BoundBinaryOperatorKind.LessOrEquals or
            BoundBinaryOperatorKind.Greater or
            BoundBinaryOperatorKind.GreaterOrEquals) &&
        CanEmitMasmIntegerExpression(expression.Left) &&
        CanEmitMasmIntegerExpression(expression.Right);

    private static bool CanEmitMasmIntegerExpression(BoundExpression expression) =>
        expression switch
        {
            BoundVariableExpression { Variable.Type: SmileType.Integer } => true,
            BoundIntegerLiteralExpression => true,
            BoundUnaryExpression unary when
                unary.Operator.Kind is BoundUnaryOperatorKind.Identity or
                    BoundUnaryOperatorKind.Negation =>
                CanEmitMasmIntegerExpression(unary.Operand),
            BoundBinaryExpression binary when
                binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division =>
                CanEmitMasmIntegerExpression(binary.Left) &&
                CanEmitMasmIntegerExpression(binary.Right),
            _ => false
        };

    private static void AppendMasmDirectIntegerComparison(
        StringBuilder source,
        BoundBinaryExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes)
    {
        AppendMasmIntegerExpression(source, expression.Left, variableIndexes);
        AppendMasmLine(source, "    push rax", "Preserve the left signed Integer operand.");
        AppendMasmIntegerExpression(source, expression.Right, variableIndexes);
        AppendMasmLine(source, "    mov r9, rax", "Keep the right signed Integer operand.");
        AppendMasmLine(source, "    pop rax", "Restore the left signed Integer operand.");
        AppendMasmLine(source, "    cmp rax, r9", "Compare current signed Integer values.");
        string setInstruction = expression.Operator.Kind switch
        {
            BoundBinaryOperatorKind.Equality => "sete al",
            BoundBinaryOperatorKind.Inequality => "setne al",
            BoundBinaryOperatorKind.Less => "setl al",
            BoundBinaryOperatorKind.LessOrEquals => "setle al",
            BoundBinaryOperatorKind.Greater => "setg al",
            BoundBinaryOperatorKind.GreaterOrEquals => "setge al",
            _ => throw new InvalidOperationException("Unsupported MASM Integer comparison.")
        };
        AppendMasmLine(source, $"    {setInstruction}", "Materialize the signed comparison result.");
        AppendMasmLine(source, "    movzx eax, al", "Normalize the comparison result to zero or one.");
    }

    private static void AppendMasmIntegerExpression(
        StringBuilder source,
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes)
    {
        switch (expression)
        {
            case BoundVariableExpression variable:
                AppendMasmLine(
                    source,
                    $"    mov rax, QWORD PTR [{VariableIntegerLabel(variableIndexes[variable.Variable])}]",
                    $"Read current signed Integer storage for {variable.Variable.Name}.");
                return;

            case BoundIntegerLiteralExpression literal:
                AppendMasmLine(
                    source,
                    $"    mov rax, {MasmIntegerImmediate(literal.Value)}",
                    "Materialize this signed Integer literal.");
                return;

            case BoundUnaryExpression unary:
                AppendMasmIntegerExpression(source, unary.Operand, variableIndexes);
                if (unary.Operator.Kind is BoundUnaryOperatorKind.Negation)
                {
                    AppendMasmLine(source, "    neg rax", "Apply SMILE signed Integer negation.");
                }

                return;

            case BoundBinaryExpression binary:
                AppendMasmIntegerExpression(source, binary.Left, variableIndexes);
                AppendMasmLine(source, "    push rax", "Preserve the left arithmetic operand.");
                AppendMasmIntegerExpression(source, binary.Right, variableIndexes);
                AppendMasmLine(source, "    mov r9, rax", "Keep the right arithmetic operand.");
                AppendMasmLine(source, "    pop rax", "Restore the left arithmetic operand.");
                switch (binary.Operator.Kind)
                {
                    case BoundBinaryOperatorKind.Addition:
                        AppendMasmLine(source, "    add rax, r9", "Apply SMILE signed Integer addition.");
                        break;

                    case BoundBinaryOperatorKind.Subtraction:
                        AppendMasmLine(source, "    sub rax, r9", "Apply SMILE signed Integer subtraction.");
                        break;

                    case BoundBinaryOperatorKind.Multiplication:
                        AppendMasmLine(source, "    imul rax, r9", "Apply SMILE signed Integer multiplication.");
                        break;

                    case BoundBinaryOperatorKind.Division:
                        AppendMasmLine(source, "    cqo", "Extend the signed dividend into RDX:RAX.");
                        AppendMasmLine(source, "    idiv r9", "Apply truncating signed Integer division.");
                        break;
                }

                return;
        }
    }

    private static void AppendMasmDirectEquality(
        StringBuilder source,
        BoundBinaryExpression expression,
        BoundConditionalClauseAnalysis clauseFacts,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        int comparisonIndex,
        ref int partIndex)
    {
        string loopLabel = MasmConditionPartLabel(clauseFacts.Ordinal, partIndex++, "Compare");
        string differentLabel = MasmConditionPartLabel(clauseFacts.Ordinal, partIndex++, "Different");
        string doneLabel = MasmConditionPartLabel(clauseFacts.Ordinal, partIndex++, "Done");

        AppendMasmLoadConditionOperand(
            source,
            expression.Left,
            variableIndexes,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, comparisonIndex, "Left"),
            "r10",
            "ecx");
        AppendMasmLoadConditionOperand(
            source,
            expression.Right,
            variableIndexes,
            runtimeBuffers,
            MasmConditionOperandLabel(clauseFacts.Ordinal, comparisonIndex, "Right"),
            "r11",
            "edx");
        AppendMasmLine(source, "    mov eax, 1", "Assume equal until a length or byte differs.");
        AppendMasmLine(source, "    cmp ecx, edx", "Exact SMILE values must have equal logical lengths.");
        AppendMasmLine(source, $"    jne {differentLabel}", "Different lengths cannot be equal.");
        AppendMasmLine(source, "    test ecx, ecx", "Empty values are equal when both lengths are zero.");
        AppendMasmLine(source, $"    jz {doneLabel}", "No bytes remain to compare.");
        AppendMasmLine(source, $"{loopLabel}:", "Compare current target storage one byte at a time.");
        AppendMasmLine(source, "    mov r8b, BYTE PTR [r10]", "Read the next left byte.");
        AppendMasmLine(source, "    cmp r8b, BYTE PTR [r11]", "Compare it to the next right byte.");
        AppendMasmLine(source, $"    jne {differentLabel}", "A differing byte makes the values unequal.");
        AppendMasmLine(source, "    inc r10", "Advance the left pointer.");
        AppendMasmLine(source, "    inc r11", "Advance the right pointer.");
        AppendMasmLine(source, "    dec ecx", "Count down the shared logical length.");
        AppendMasmLine(source, $"    jnz {loopLabel}", "Continue until every byte matches.");
        AppendMasmLine(source, $"    jmp {doneLabel}", "The complete values are equal.");
        AppendMasmLine(source, $"{differentLabel}:", "Normalize inequality to Boolean zero.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means the values differ.");
        AppendMasmLine(source, $"{doneLabel}:", "EAX now contains exact equality.");
        if (expression.Operator.Kind is BoundBinaryOperatorKind.Inequality)
        {
            AppendMasmLine(source, "    xor eax, 1", "Invert equality for SMILE's <> comparison.");
        }
    }

    private static void AppendMasmLoadConditionOperand(
        StringBuilder source,
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        string staticLabel,
        string pointerRegister,
        string lengthRegister)
    {
        if (expression is BoundVariableExpression variable)
        {
            int variableIndex = variableIndexes[variable.Variable];
            AppendMasmLine(
                source,
                $"    mov {pointerRegister}, QWORD PTR [{VariablePointerLabel(variableIndex)}]",
                $"Read current {variable.Variable.Name} storage for this IF.");
            AppendMasmLine(
                source,
                $"    mov {lengthRegister}, DWORD PTR [{VariableLengthLabel(variableIndex)}]",
                $"Read current {variable.Variable.Name} logical length.");
            return;
        }

        if (runtimeBuffers.TryGetValue(expression, out RuntimeStringBuffer? buffer))
        {
            AppendMasmLine(
                source,
                $"    lea {pointerRegister}, {buffer.Label}",
                "Address of the runtime-composed IF operand.");
            AppendMasmLine(
                source,
                $"    mov {lengthRegister}, DWORD PTR [{buffer.Label}Length]",
                "Length of the runtime-composed IF operand.");
            return;
        }

        AppendMasmLine(source, $"    lea {pointerRegister}, {staticLabel}", "Address of the static IF operand.");
        AppendMasmLine(source, $"    mov {lengthRegister}, {staticLabel}Length", "Length of the static IF operand.");
    }

    private static void AppendMasmPrint(
        StringBuilder source,
        BoundPrintStatement print,
        BoundStatementAnalysis facts,
        int printIndex,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        source.AppendLine();
        AppendMasmLine(source, $"; PRINT #{printIndex + 1}", "Write each expression segment, then newline.");

        if (!print.IsBlankLine && print.Value is BoundVariableExpression directVariable)
        {
            AppendMasmWriteVariable(
                source,
                directVariable.Variable.Name,
                variableIndexes[directVariable.Variable]);
        }
        else if (print.IsBlankLine || facts.Value.IsKnown)
        {
            AppendMasmWriteLiteral(source, PrintLiteralLabel(printIndex, 0));
        }
        else
        {
            IReadOnlyList<RuntimeTextSegment> segments = RuntimeTextPlan.Flatten(print.Value);
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                switch (segments[segmentIndex])
                {
                    case RuntimeLiteralTextSegment { Text.Length: > 0 }:
                        AppendMasmWriteLiteral(source, PrintLiteralLabel(printIndex, segmentIndex));
                        break;

                    case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }:
                        AppendMasmWriteVariable(
                            source,
                            variable.Variable.Name,
                            variableIndexes[variable.Variable]);
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Integer:
                        AppendMasmIntegerExpression(source, runtime.Expression, variableIndexes);
                        AppendMasmLine(source, "    mov rcx, rax", "Format this runtime PRINT Integer.");
                        AppendMasmLine(
                            source,
                            $"    call {IntegerFormatProcedure}",
                            "Return decimal pointer and byte length.");
                        AppendMasmWriteBuffer(source, "rax", "edx", "runtime Integer text");
                        break;

                    case RuntimeExpressionTextSegment runtime when
                        runtime.Expression.Type is SmileType.Boolean:
                        int booleanPartIndex = 0;
                        AppendMasmBooleanExpression(
                            source,
                            runtime.Expression,
                            variableIndexes,
                            labelPrefix + "Segment" +
                                segmentIndex.ToString(CultureInfo.InvariantCulture),
                            booleanStringBuffers,
                            ref booleanPartIndex);
                        string falseLabel = labelPrefix + "BooleanFalse" +
                            segmentIndex.ToString(CultureInfo.InvariantCulture);
                        string readyLabel = labelPrefix + "BooleanReady" +
                            segmentIndex.ToString(CultureInfo.InvariantCulture);
                        AppendMasmLine(source, "    test eax, eax", "Choose canonical Boolean PRINT text.");
                        AppendMasmLine(source, $"    jz {falseLabel}", "Zero selects FALSE.");
                        AppendMasmLine(source, "    lea rax, smileBooleanTrue", "Address of TRUE text.");
                        AppendMasmLine(source, "    mov edx, smileBooleanTrueLength", "Length of TRUE text.");
                        AppendMasmLine(source, $"    jmp {readyLabel}", "Skip the FALSE selection.");
                        AppendMasmLine(source, $"{falseLabel}:", "Select canonical FALSE text.");
                        AppendMasmLine(source, "    lea rax, smileBooleanFalse", "Address of FALSE text.");
                        AppendMasmLine(source, "    mov edx, smileBooleanFalseLength", "Length of FALSE text.");
                        AppendMasmLine(source, $"{readyLabel}:", "Boolean PRINT text is ready.");
                        AppendMasmWriteBuffer(source, "rax", "edx", "runtime Boolean text");
                        break;
                }
            }
        }

        AppendMasmWriteLiteral(source, "newline");
    }

    private static bool CanEmitLivePrintSegments(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundVariableExpression => true,
            BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary =>
                CanEmitLivePrintSegments(binary.Left) && CanEmitLivePrintSegments(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.All(part => part switch
            {
                BoundInterpolatedTextPart => true,
                BoundInterpolationExpressionPart hole => CanEmitLivePrintSegments(hole.Expression),
                _ => false
            }),
            _ => false
        };

    private static void AppendMasmStorageUpdate(
        StringBuilder source,
        int variableIndex,
        VariableSymbol variable,
        SmileValue assignedValue,
        string valueLabel,
        string addressComment)
    {
        source.AppendLine();
        AppendMasmLine(source, $"    lea rax, {valueLabel}", addressComment);
        AppendMasmLine(source, $"    mov QWORD PTR [{VariablePointerLabel(variableIndex)}], rax", "Store the runtime string pointer.");
        AppendMasmLine(source, $"    mov DWORD PTR [{VariableLengthLabel(variableIndex)}], {valueLabel}Length", "Store the runtime string length.");
        if (variable.Type is SmileType.Integer)
        {
            AppendMasmLine(
                source,
                $"    mov rax, {MasmIntegerImmediate(assignedValue.IntegerValue)}",
                "Materialize the signed Integer value for runtime comparisons.");
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariableIntegerLabel(variableIndex)}], rax",
                "Update the runtime signed Integer storage.");
        }
        else if (variable.Type is SmileType.Boolean)
        {
            AppendMasmLine(
                source,
                $"    mov BYTE PTR [{VariableBooleanLabel(variableIndex)}], {(assignedValue.BooleanValue ? 1 : 0)}",
                "Update the runtime Boolean storage.");
        }
    }

    private static void AppendMasmStorageCopy(
        StringBuilder source,
        int destinationIndex,
        int sourceIndex,
        SmileType variableType,
        string destinationName,
        string sourceName)
    {
        source.AppendLine();
        AppendMasmLine(
            source,
            $"    mov rax, QWORD PTR [{VariablePointerLabel(sourceIndex)}]",
            $"Read current {sourceName} pointer for SET {destinationName}.");
        AppendMasmLine(
            source,
            $"    mov QWORD PTR [{VariablePointerLabel(destinationIndex)}], rax",
            $"Store the copied pointer in {destinationName}.");
        AppendMasmLine(
            source,
            $"    mov eax, DWORD PTR [{VariableLengthLabel(sourceIndex)}]",
            $"Read current {sourceName} logical length.");
        AppendMasmLine(
            source,
            $"    mov DWORD PTR [{VariableLengthLabel(destinationIndex)}], eax",
            $"Store the copied logical length in {destinationName}.");
        if (variableType is SmileType.Integer)
        {
            AppendMasmLine(
                source,
                $"    mov rax, QWORD PTR [{VariableIntegerLabel(sourceIndex)}]",
                $"Read current {sourceName} signed Integer storage.");
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariableIntegerLabel(destinationIndex)}], rax",
                $"Store the copied signed Integer in {destinationName}.");
        }
        else if (variableType is SmileType.Boolean)
        {
            AppendMasmLine(
                source,
                $"    mov al, BYTE PTR [{VariableBooleanLabel(sourceIndex)}]",
                $"Read current {sourceName} Boolean storage.");
            AppendMasmLine(
                source,
                $"    mov BYTE PTR [{VariableBooleanLabel(destinationIndex)}], al",
                $"Store the copied Boolean in {destinationName}.");
        }
    }

    private static void AppendMasmRuntimeAssignment(
        StringBuilder source,
        VariableSymbol destination,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        int destinationIndex = variableIndexes[destination];
        if (destination.Type is SmileType.Integer)
        {
            AppendMasmIntegerExpression(source, expression, variableIndexes);
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariableIntegerLabel(destinationIndex)}], rax",
                $"Update current signed Integer storage for {destination.Name}.");
        }
        else if (destination.Type is SmileType.Boolean)
        {
            int booleanPartIndex = 0;
            AppendMasmBooleanExpression(
                source,
                expression,
                variableIndexes,
                labelPrefix,
                booleanStringBuffers,
                ref booleanPartIndex);
            AppendMasmLine(
                source,
                $"    mov BYTE PTR [{VariableBooleanLabel(destinationIndex)}], al",
                $"Update current Boolean storage for {destination.Name}.");
            string falseLabel = labelPrefix + "BooleanFalse";
            string doneLabel = labelPrefix + "BooleanDone";
            AppendMasmLine(source, "    test eax, eax", "Choose canonical Boolean display text.");
            AppendMasmLine(source, $"    jz {falseLabel}", "Zero selects FALSE.");
            AppendMasmLine(source, "    lea rax, smileBooleanTrue", "Address of canonical TRUE text.");
            AppendMasmLine(source, "    mov edx, smileBooleanTrueLength", "Length of TRUE text.");
            AppendMasmLine(source, $"    jmp {doneLabel}", "Skip the FALSE selection.");
            AppendMasmLine(source, $"{falseLabel}:", "Select canonical FALSE text.");
            AppendMasmLine(source, "    lea rax, smileBooleanFalse", "Address of canonical FALSE text.");
            AppendMasmLine(source, "    mov edx, smileBooleanFalseLength", "Length of FALSE text.");
            AppendMasmLine(source, $"{doneLabel}:", "Boolean pointer and length are ready.");
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariablePointerLabel(destinationIndex)}], rax",
                "Store the runtime Boolean text pointer.");
            AppendMasmLine(
                source,
                $"    mov DWORD PTR [{VariableLengthLabel(destinationIndex)}], edx",
                "Store the runtime Boolean text length.");
            return;
        }

        int partIndex = 0;
        AppendMasmRuntimeTextMaterialization(
            source,
            expression,
            buffer,
            variableIndexes,
            labelPrefix,
            booleanStringBuffers,
            ref partIndex);
        AppendMasmLine(
            source,
            $"    lea rax, {buffer.Label}",
            $"Address of runtime-composed {destination.Name} text.");
        AppendMasmLine(
            source,
            $"    mov QWORD PTR [{VariablePointerLabel(destinationIndex)}], rax",
            "Store the runtime text pointer.");
        AppendMasmLine(
            source,
            $"    mov eax, DWORD PTR [{buffer.Label}Length]",
            "Read the runtime text length.");
        AppendMasmLine(
            source,
            $"    mov DWORD PTR [{VariableLengthLabel(destinationIndex)}], eax",
            "Store the runtime text length.");
    }

    private static void AppendMasmBooleanExpression(
        StringBuilder source,
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int partIndex)
    {
        switch (expression)
        {
            case BoundBooleanLiteralExpression literal:
                AppendMasmLine(
                    source,
                    $"    mov eax, {(literal.Value ? 1 : 0)}",
                    "Materialize this Boolean literal.");
                return;

            case BoundVariableExpression { Variable.Type: SmileType.Boolean } variable:
                AppendMasmLine(
                    source,
                    $"    movzx eax, BYTE PTR [{VariableBooleanLabel(variableIndexes[variable.Variable])}]",
                    $"Read current Boolean storage for {variable.Variable.Name}.");
                return;

            case BoundUnaryExpression
                {
                    Operator.Kind: BoundUnaryOperatorKind.LogicalNegation
                } unary:
                AppendMasmBooleanExpression(
                    source,
                    unary.Operand,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, "    xor eax, 1", "Invert the normalized Boolean result.");
                return;

            case BoundBinaryExpression logical when
                logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                    BoundBinaryOperatorKind.LogicalOr:
                AppendMasmBooleanExpression(
                    source,
                    logical.Left,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                string shortCircuitLabel = labelPrefix + "BooleanPart" +
                    partIndex++.ToString(CultureInfo.InvariantCulture);
                AppendMasmLine(source, "    test eax, eax", "Honor left-to-right Boolean short circuit.");
                AppendMasmLine(
                    source,
                    logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                        ? $"    jz {shortCircuitLabel}"
                        : $"    jnz {shortCircuitLabel}",
                    "Skip the unreachable Boolean operand.");
                AppendMasmBooleanExpression(
                    source,
                    logical.Right,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, $"{shortCircuitLabel}:", "Complete this Boolean expression.");
                return;

            case BoundBinaryExpression integerComparison when
                CanEmitMasmDirectIntegerComparison(integerComparison):
                AppendMasmDirectIntegerComparison(source, integerComparison, variableIndexes);
                return;

            case BoundBinaryExpression stringComparison when
                stringComparison.Left.Type is SmileType.String &&
                stringComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality:
                RuntimeStringBuffer leftBuffer = booleanStringBuffers[stringComparison.Left];
                RuntimeStringBuffer rightBuffer = booleanStringBuffers[stringComparison.Right];
                AppendMasmRuntimeTextMaterialization(
                    source,
                    stringComparison.Left,
                    leftBuffer,
                    variableIndexes,
                    labelPrefix + "StringLeft",
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmRuntimeTextMaterialization(
                    source,
                    stringComparison.Right,
                    rightBuffer,
                    variableIndexes,
                    labelPrefix + "StringRight",
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmRuntimeBufferEquality(
                    source,
                    leftBuffer,
                    rightBuffer,
                    stringComparison.Operator.Kind is BoundBinaryOperatorKind.Inequality,
                    labelPrefix,
                    ref partIndex);
                return;

            case BoundBinaryExpression booleanComparison when
                booleanComparison.Left.Type is SmileType.Boolean &&
                booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality:
                AppendMasmBooleanExpression(
                    source,
                    booleanComparison.Left,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, "    push rax", "Preserve the left Boolean operand.");
                AppendMasmBooleanExpression(
                    source,
                    booleanComparison.Right,
                    variableIndexes,
                    labelPrefix,
                    booleanStringBuffers,
                    ref partIndex);
                AppendMasmLine(source, "    mov r9d, eax", "Keep the right Boolean operand.");
                AppendMasmLine(source, "    pop rax", "Restore the left Boolean operand.");
                AppendMasmLine(source, "    cmp eax, r9d", "Compare normalized Boolean values.");
                AppendMasmLine(
                    source,
                    booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality
                        ? "    sete al"
                        : "    setne al",
                    "Materialize Boolean equality.");
                AppendMasmLine(source, "    movzx eax, al", "Normalize the Boolean comparison.");
                return;
        }

        throw new InvalidOperationException(
            "MASM could not lower an abstract-unknown Boolean expression.");
    }

    private static void AppendMasmRuntimeTextMaterialization(
        StringBuilder source,
        BoundExpression expression,
        RuntimeStringBuffer buffer,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        string labelPrefix,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int partIndex)
    {
        source.AppendLine();
        AppendMasmLine(source, $"    lea r10, {buffer.Label}", "Start this runtime text buffer.");
        AppendMasmLine(source, "    xor r8d, r8d", "Current logical byte length is zero.");

        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal:
                    foreach (byte value in Encoding.UTF8.GetBytes(literal.Text))
                    {
                        AppendMasmLine(
                            source,
                            $"    mov BYTE PTR [r10], {value.ToString(CultureInfo.InvariantCulture)}",
                            "Append one compiler-known UTF-8 byte.");
                        AppendMasmLine(source, "    inc r10", "Advance the runtime text destination.");
                        AppendMasmLine(source, "    inc r8d", "Count the appended byte.");
                    }

                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }:
                    int variableIndex = variableIndexes[variable.Variable];
                    AppendMasmLine(
                        source,
                        $"    mov r11, QWORD PTR [{VariablePointerLabel(variableIndex)}]",
                        $"Read current {variable.Variable.Name} text storage.");
                    AppendMasmLine(
                        source,
                        $"    mov ecx, DWORD PTR [{VariableLengthLabel(variableIndex)}]",
                        $"Read current {variable.Variable.Name} logical length.");
                    AppendMasmCopyBytes(
                        source,
                        labelPrefix,
                        ref partIndex);
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Integer:
                    AppendMasmLine(source, "    push r10", "Preserve the runtime text destination.");
                    AppendMasmLine(source, "    push r8", "Preserve the accumulated text length.");
                    AppendMasmIntegerExpression(source, runtime.Expression, variableIndexes);
                    AppendMasmLine(source, "    mov rcx, rax", "Format this signed Integer value.");
                    AppendMasmLine(source, $"    call {IntegerFormatProcedure}", "Return decimal text in RAX/EDX.");
                    AppendMasmLine(source, "    mov r11, rax", "Use the formatted text as the copy source.");
                    AppendMasmLine(source, "    mov ecx, edx", "Use its exact decimal byte length.");
                    AppendMasmLine(source, "    pop r8", "Restore the accumulated text length.");
                    AppendMasmLine(source, "    pop r10", "Restore the runtime text destination.");
                    AppendMasmCopyBytes(
                        source,
                        labelPrefix,
                        ref partIndex);
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Boolean:
                    AppendMasmLine(source, "    push r10", "Preserve the runtime text destination.");
                    AppendMasmLine(source, "    push r8", "Preserve the accumulated text length.");
                    AppendMasmBooleanExpression(
                        source,
                        runtime.Expression,
                        variableIndexes,
                        labelPrefix + "Boolean",
                        booleanStringBuffers,
                        ref partIndex);
                    string falseLabel = labelPrefix + "BooleanText" +
                        partIndex++.ToString(CultureInfo.InvariantCulture) + "False";
                    string readyLabel = labelPrefix + "BooleanText" +
                        partIndex++.ToString(CultureInfo.InvariantCulture) + "Ready";
                    AppendMasmLine(source, "    test eax, eax", "Choose canonical Boolean text.");
                    AppendMasmLine(source, $"    jz {falseLabel}", "Zero selects FALSE.");
                    AppendMasmLine(source, "    lea r11, smileBooleanTrue", "Address of TRUE text.");
                    AppendMasmLine(source, "    mov ecx, smileBooleanTrueLength", "Length of TRUE text.");
                    AppendMasmLine(source, $"    jmp {readyLabel}", "Skip the FALSE selection.");
                    AppendMasmLine(source, $"{falseLabel}:", "Select canonical FALSE text.");
                    AppendMasmLine(source, "    lea r11, smileBooleanFalse", "Address of FALSE text.");
                    AppendMasmLine(source, "    mov ecx, smileBooleanFalseLength", "Length of FALSE text.");
                    AppendMasmLine(source, $"{readyLabel}:", "Boolean text source is ready.");
                    AppendMasmLine(source, "    pop r8", "Restore the accumulated text length.");
                    AppendMasmLine(source, "    pop r10", "Restore the runtime text destination.");
                    AppendMasmCopyBytes(source, labelPrefix, ref partIndex);
                    break;
            }
        }

        AppendMasmLine(
            source,
            $"    mov DWORD PTR [{buffer.Label}Length], r8d",
            "Store the complete runtime text length.");
    }

    private static void AppendMasmRuntimeBufferEquality(
        StringBuilder source,
        RuntimeStringBuffer left,
        RuntimeStringBuffer right,
        bool invert,
        string labelPrefix,
        ref int partIndex)
    {
        string labelBase = labelPrefix + "StringCompare" +
            partIndex++.ToString(CultureInfo.InvariantCulture);
        string loopLabel = labelBase + "Loop";
        string differentLabel = labelBase + "Different";
        string doneLabel = labelBase + "Done";
        AppendMasmLine(source, $"    lea r10, {left.Label}", "Address of the left runtime String.");
        AppendMasmLine(source, $"    lea r11, {right.Label}", "Address of the right runtime String.");
        AppendMasmLine(source, $"    mov ecx, DWORD PTR [{left.Label}Length]", "Left logical byte length.");
        AppendMasmLine(source, $"    mov edx, DWORD PTR [{right.Label}Length]", "Right logical byte length.");
        AppendMasmLine(source, "    mov eax, 1", "Assume the complete String values are equal.");
        AppendMasmLine(source, "    cmp ecx, edx", "Exact Strings must have equal lengths.");
        AppendMasmLine(source, $"    jne {differentLabel}", "Different lengths cannot be equal.");
        AppendMasmLine(source, "    test ecx, ecx", "Empty Strings with equal lengths are equal.");
        AppendMasmLine(source, $"    jz {doneLabel}", "No bytes remain to compare.");
        AppendMasmLine(source, $"{loopLabel}:", "Compare the next exact UTF-8 byte.");
        AppendMasmLine(source, "    mov r8b, BYTE PTR [r10]", "Read the left byte.");
        AppendMasmLine(source, "    cmp r8b, BYTE PTR [r11]", "Compare the right byte.");
        AppendMasmLine(source, $"    jne {differentLabel}", "A differing byte makes the Strings unequal.");
        AppendMasmLine(source, "    inc r10", "Advance the left pointer.");
        AppendMasmLine(source, "    inc r11", "Advance the right pointer.");
        AppendMasmLine(source, "    dec ecx", "Count down the shared byte length.");
        AppendMasmLine(source, $"    jnz {loopLabel}", "Continue through every byte.");
        AppendMasmLine(source, $"    jmp {doneLabel}", "The complete Strings are equal.");
        AppendMasmLine(source, $"{differentLabel}:", "Materialize String inequality.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means unequal.");
        AppendMasmLine(source, $"{doneLabel}:", "String equality is normalized in EAX.");
        if (invert)
        {
            AppendMasmLine(source, "    xor eax, 1", "Invert equality for SMILE inequality.");
        }
    }

    private static void AppendMasmCopyBytes(
        StringBuilder source,
        string labelPrefix,
        ref int partIndex)
    {
        string loopLabel = $"{labelPrefix}Copy{partIndex}Loop";
        string doneLabel = $"{labelPrefix}Copy{partIndex}Done";
        partIndex++;
        AppendMasmLine(source, "    mov edx, ecx", "Preserve this segment's logical length.");
        AppendMasmLine(source, "    test ecx, ecx", "Skip an exact empty segment.");
        AppendMasmLine(source, $"    jz {doneLabel}", "No bytes need copying.");
        AppendMasmLine(source, $"{loopLabel}:", "Copy current runtime text byte by byte.");
        AppendMasmLine(source, "    mov al, BYTE PTR [r11]", "Read the next source byte.");
        AppendMasmLine(source, "    mov BYTE PTR [r10], al", "Append it to the destination buffer.");
        AppendMasmLine(source, "    inc r11", "Advance the source pointer.");
        AppendMasmLine(source, "    inc r10", "Advance the destination pointer.");
        AppendMasmLine(source, "    dec ecx", "Count down this segment.");
        AppendMasmLine(source, $"    jnz {loopLabel}", "Continue until every byte is copied.");
        AppendMasmLine(source, "    add r8d, edx", "Add this segment to the complete length.");
        AppendMasmLine(source, $"{doneLabel}:", "Continue with the next runtime text segment.");
    }

    private static void AppendMasmIntegerFormatter(StringBuilder source)
    {
        AppendMasmLine(source, $"{IntegerFormatProcedure} PROC", "Format RCX as exact signed Int64 decimal text.");
        AppendMasmLine(source, $"    lea r10, {IntegerFormatBufferLabel} + 21", "Build digits backward from the buffer end.");
        AppendMasmLine(source, "    mov rax, rcx", "Copy the signed input value.");
        AppendMasmLine(source, "    xor r11d, r11d", "Remember whether a minus sign is required.");
        AppendMasmLine(source, "    test rax, rax", "Inspect the signed input.");
        AppendMasmLine(source, "    jge smileFormatIntegerMagnitude", "A nonnegative value is already a magnitude.");
        AppendMasmLine(source, "    mov r11d, 1", "Record the negative sign.");
        AppendMasmLine(source, "    neg rax", "Use the unsigned magnitude; Int64.MinValue remains representable as bits.");
        AppendMasmLine(source, "smileFormatIntegerMagnitude:", "Convert at least one decimal digit.");
        AppendMasmLine(source, "    mov r8d, 10", "Decimal divisor.");
        AppendMasmLine(source, "smileFormatIntegerDigit:", "Extract the next least-significant digit.");
        AppendMasmLine(source, "    xor edx, edx", "Clear the high unsigned dividend.");
        AppendMasmLine(source, "    div r8", "RAX becomes quotient; RDX is the digit.");
        AppendMasmLine(source, "    add dl, '0'", "Convert the digit to ASCII.");
        AppendMasmLine(source, "    dec r10", "Reserve one byte before the current text.");
        AppendMasmLine(source, "    mov BYTE PTR [r10], dl", "Store this decimal digit.");
        AppendMasmLine(source, "    test rax, rax", "More quotient digits remain?");
        AppendMasmLine(source, "    jnz smileFormatIntegerDigit", "Continue until the quotient is zero.");
        AppendMasmLine(source, "    test r11d, r11d", "Does the value need a sign?");
        AppendMasmLine(source, "    jz smileFormatIntegerReady", "Positive and zero values are complete.");
        AppendMasmLine(source, "    dec r10", "Reserve the leading sign byte.");
        AppendMasmLine(source, "    mov BYTE PTR [r10], '-'", "Prepend the minus sign.");
        AppendMasmLine(source, "smileFormatIntegerReady:", "Return pointer and exact length.");
        AppendMasmLine(source, $"    lea rax, {IntegerFormatBufferLabel} + 21", "Point one byte past the formatted text.");
        AppendMasmLine(source, "    sub rax, r10", "Compute the formatted byte length.");
        AppendMasmLine(source, "    mov edx, eax", "Return length in EDX.");
        AppendMasmLine(source, "    mov rax, r10", "Return text pointer in RAX.");
        AppendMasmLine(source, "    ret", "Return to generated code.");
        AppendMasmLine(source, $"{IntegerFormatProcedure} ENDP", "End signed Integer formatter.");
    }

    private static IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer>
        CreateMasmStatementBuffers(BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundStatement, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            BoundExpression? expression = statement switch
            {
                BoundLetStatement let when
                    !facts.Value.IsKnown && let.Initializer is not BoundVariableExpression =>
                    let.Initializer,
                BoundSetStatement set when
                    !facts.Value.IsKnown && set.Value is not BoundVariableExpression =>
                    set.Value,
                _ => null
            };
            if (expression is null)
            {
                continue;
            }

            string label = $"runtimeStatement{facts.Ordinal}Value";
            buffers.Add(
                statement,
                new RuntimeStringBuffer(
                    expression,
                    label,
                    Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(expression))));
        }

        return buffers;
    }

    private static IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>>
        CreateMasmConditionBuffers(BoundProgramAnalysis analysis)
    {
        var plans = new Dictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundIfStatement conditional in analysis.EnumerateStatements().OfType<BoundIfStatement>())
        {
            foreach (BoundConditionalClause clause in conditional.Clauses)
            {
                int ordinal = analysis.GetClauseFacts(clause).Ordinal;
                var buffers = new List<RuntimeStringBuffer>();
                CollectMasmConditionBuffers(clause.Condition, ordinal, analysis, buffers);
                plans.Add(clause, buffers);
            }
        }

        return plans;
    }

    private static void CollectMasmConditionBuffers(
        BoundExpression expression,
        int clauseOrdinal,
        BoundProgramAnalysis analysis,
        List<RuntimeStringBuffer> buffers)
    {
        if (expression is BoundUnaryExpression unary)
        {
            CollectMasmConditionBuffers(unary.Operand, clauseOrdinal, analysis, buffers);
            return;
        }

        if (expression is not BoundBinaryExpression binary)
        {
            return;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            CollectMasmConditionBuffers(binary.Left, clauseOrdinal, analysis, buffers);
            CollectMasmConditionBuffers(binary.Right, clauseOrdinal, analysis, buffers);
            return;
        }

        if (binary.Left.Type is not SmileType.String ||
            binary.Operator.Kind is not (BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality))
        {
            return;
        }

        Add(binary.Left);
        Add(binary.Right);

        void Add(BoundExpression operand)
        {
            if (operand is BoundVariableExpression or BoundStringLiteralExpression)
            {
                return;
            }

            string label = $"ifCondition{clauseOrdinal}Runtime{buffers.Count}";
            buffers.Add(new RuntimeStringBuffer(
                operand,
                label,
                Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    private static IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer>
        CreateMasmBooleanStringBuffers(BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundExpression, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            switch (statement)
            {
                case BoundLetStatement let:
                    Collect(let.Initializer);
                    break;

                case BoundSetStatement set:
                    Collect(set.Value);
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    Collect(print.Value);
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        Collect(clause.Condition);
                    }

                    break;
            }
        }

        return buffers;

        void Collect(BoundExpression expression)
        {
            if (expression is BoundBinaryExpression comparison &&
                comparison.Left.Type is SmileType.String &&
                comparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality)
            {
                Add(comparison.Left);
                Add(comparison.Right);
            }

            switch (expression)
            {
                case BoundUnaryExpression unary:
                    Collect(unary.Operand);
                    break;

                case BoundBinaryExpression binary:
                    Collect(binary.Left);
                    Collect(binary.Right);
                    break;

                case BoundInterpolatedStringExpression interpolated:
                    foreach (BoundInterpolationExpressionPart hole in
                        interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                    {
                        Collect(hole.Expression);
                    }

                    break;
            }
        }

        void Add(BoundExpression operand)
        {
            if (buffers.ContainsKey(operand))
            {
                return;
            }

            string label = $"runtimeBooleanString{buffers.Count}";
            buffers.Add(
                operand,
                new RuntimeStringBuffer(
                    operand,
                    label,
                    Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    private static bool NeedsMasmIntegerFormatter(
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        if (statementBuffers.Values
                .Concat(conditionBuffers.Values.SelectMany(value => value))
                .Concat(booleanStringBuffers.Values)
                .Any(buffer => RuntimeTextPlan.Flatten(buffer.Expression).Any(segment =>
                    segment is RuntimeExpressionTextSegment runtime &&
                    runtime.Expression.Type is SmileType.Integer &&
                    runtime.Expression is not BoundVariableExpression)))
        {
            return true;
        }

        return analysis.EnumerateStatements().Any(statement =>
            statement is BoundPrintStatement print &&
            !analysis.GetStatementFacts(statement).Value.IsKnown &&
            RuntimeTextPlan.Flatten(print.Value).Any(segment =>
                segment is RuntimeExpressionTextSegment runtime &&
                runtime.Expression.Type is SmileType.Integer &&
                runtime.Expression is not BoundVariableExpression));
    }

    private static bool NeedsMasmBooleanText(
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundConditionalClause, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers)
    {
        if (statementBuffers.Values
                .Concat(conditionBuffers.Values.SelectMany(value => value))
                .Concat(booleanStringBuffers.Values)
                .Any(buffer => RuntimeTextPlan.Flatten(buffer.Expression).Any(segment =>
                    segment is RuntimeExpressionTextSegment runtime &&
                    runtime.Expression.Type is SmileType.Boolean)))
        {
            return true;
        }

        return analysis.EnumerateStatements().Any(statement =>
            statement is BoundPrintStatement print &&
            !analysis.GetStatementFacts(statement).Value.IsKnown &&
            RuntimeTextPlan.Flatten(print.Value).Any(segment =>
                segment is RuntimeExpressionTextSegment runtime &&
                runtime.Expression.Type is SmileType.Boolean));
    }

    private static void AppendMasmWriteLiteral(StringBuilder source, string label)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    lea rdx, {label}", "WriteFile arg 2: address of literal bytes.");
        AppendMasmLine(source, $"    mov r8d, {label}Length", "WriteFile arg 3: byte count.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this literal segment.");
    }

    private static void AppendMasmWriteVariable(StringBuilder source, string name, int variableIndex)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    mov rdx, QWORD PTR [{VariablePointerLabel(variableIndex)}]", $"WriteFile arg 2: {name} pointer.");
        AppendMasmLine(source, $"    mov r8d, DWORD PTR [{VariableLengthLabel(variableIndex)}]", $"WriteFile arg 3: {name} length.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this variable segment.");
    }

    private static void AppendMasmWriteBuffer(
        StringBuilder source,
        string pointerRegister,
        string lengthRegister,
        string description)
    {
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdoutHandle]", "WriteFile arg 1: stdout handle.");
        AppendMasmLine(source, $"    mov r8d, {lengthRegister}", $"WriteFile arg 3: {description} length.");
        AppendMasmLine(source, $"    mov rdx, {pointerRegister}", $"WriteFile arg 2: {description} pointer.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: address for bytes-written result.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5 on stack: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit this runtime segment.");
    }

    private static void AppendMasmLine(StringBuilder source, string code, string? comment = null)
    {
        if (comment is null)
        {
            source.AppendLine(code);
            return;
        }

        const int commentColumn = 48;
        int padding = Math.Max(1, commentColumn - code.Length);
        source.AppendLine(code + new string(' ', padding) + "; " + comment);
    }

    private static string VariableValueLabel(int index) => $"variable{index}Value";

    private static string VariablePointerLabel(int index) => $"variable{index}Ptr";

    private static string VariableLengthLabel(int index) => $"variable{index}Length";

    private static string VariableIntegerLabel(int index) => $"variable{index}Integer";

    private static string VariableBooleanLabel(int index) => $"variable{index}Boolean";

    private static string MasmIntegerImmediate(long value) =>
        "0" + unchecked((ulong)value).ToString("X16", CultureInfo.InvariantCulture) + "h";

    private static string SetValueLabel(int statementIndex) => $"set{statementIndex}Value";

    private static string PrintLiteralLabel(int printIndex, int segmentIndex) =>
        $"print{printIndex}Segment{segmentIndex}";

    private static string IfClauseLabel(int ifOrdinal, int clauseIndex) =>
        $"if{ifOrdinal}Clause{clauseIndex}";

    private static string IfElseLabel(int ifOrdinal) => $"if{ifOrdinal}Else";

    private static string IfEndLabel(int ifOrdinal) => $"if{ifOrdinal}End";

    private static string MasmConditionOperandLabel(
        int clauseOrdinal,
        int comparisonIndex,
        string side) =>
        $"ifCondition{clauseOrdinal}Comparison{comparisonIndex}{side}";

    private static string MasmConditionPartLabel(
        int clauseOrdinal,
        int partIndex,
        string purpose) =>
        $"ifCondition{clauseOrdinal}Part{partIndex}{purpose}";

}
