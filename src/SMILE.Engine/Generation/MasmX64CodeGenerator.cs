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
        // Ordinary learner programs take the native CRT path first. The older
        // exact-byte path remains only as a compatibility fallback for advanced
        // String expressions that still need dedicated lowering while the
        // strategic reset continues to simplify those exceptional cases.
        if (MasmX64NativeGeneration.TryGenerate(program, out GeneratedProgram? nativeProgram))
        {
            return nativeProgram!;
        }

        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        BoundInputStatement[] inputs = TargetRuntimeFacts.Inputs(program).ToArray();
        bool hasInput = inputs.Length > 0;
        bool checkedArithmetic = NeedsMasmRuntimeArithmetic(analysis);
        BoundPrintStatement[] prints = analysis.EnumerateStatements()
            .OfType<BoundPrintStatement>()
            .ToArray();
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes = lets
            .Select((let, index) => (let.Variable, index))
            .ToDictionary(item => item.Variable, item => item.index);
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers =
            CreateMasmStatementBuffers(analysis);
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>>
            conditionBuffers = CreateMasmConditionBuffers(analysis);
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers =
            CreateMasmBooleanStringBuffers(analysis);
        bool needsIntegerFormatter = NeedsMasmIntegerFormatter(
            analysis,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers);
        needsIntegerFormatter |= inputs.Any(input => input.Variable.Type is SmileType.Integer);
        bool needsBooleanText = NeedsMasmBooleanText(
            analysis,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers);
        needsBooleanText |= inputs.Any(input => input.Variable.Type is SmileType.Boolean);
        var source = new StringBuilder();

        AppendMasmLine(source, "option casemap:none", "Keep symbol names case-sensitive.");
        source.AppendLine();

        if (prints.Length > 0 || hasInput || checkedArithmetic)
        {
            AppendMasmLine(source, "EXTERN GetStdHandle:PROC", "Windows API: get standard console handles.");
            AppendMasmLine(source, "EXTERN WriteFile:PROC", "Windows API: write bytes to the console.");
        }

        if (hasInput)
        {
            AppendMasmLine(source, "EXTERN ReadFile:PROC", "Windows API: read exact redirected or console input bytes.");
            AppendMasmLine(source, "EXTERN GetLastError:PROC", "Distinguish a closed redirected pipe from a real read failure.");
            AppendMasmLine(source, "EXTERN SetConsoleCP:PROC", "Use UTF-8 for interactive console input.");
        }

        AppendMasmLine(source, "EXTERN ExitProcess:PROC", "Windows API: terminate the process.");
        source.AppendLine();

        AppendMasmData(
            source,
            analysis,
            variableIndexes,
            prints.Length,
            inputs,
            checkedArithmetic,
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
            hasInput,
            checkedArithmetic,
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
        IReadOnlyList<BoundInputStatement> inputs,
        bool checkedArithmetic,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        bool needsIntegerFormatter,
        bool needsBooleanText)
    {
        if (variableIndexes.Count == 0 &&
            printCount == 0 &&
            inputs.Count == 0 &&
            !checkedArithmetic &&
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

        if (inputs.Count > 0)
        {
            AppendMasmLine(source, "STD_INPUT_HANDLE EQU -10", "Magic value for the standard input handle.");
        }

        if (inputs.Count > 0 || checkedArithmetic)
        {
            AppendMasmLine(source, "STD_ERROR_HANDLE EQU -12", "Magic value for the standard error handle.");
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
                        int ordinal = analysis.GetClauseFacts(clause).Ordinal;
                        AppendMasmConditionData(
                            source,
                            clause.Condition,
                            IfConditionPrefix(ordinal),
                            conditionBuffers[clause.Condition],
                            ref comparisonIndex);
                    }

                    break;

                case BoundWhileStatement loop:
                    int whileComparisonIndex = 0;
                    int whileOrdinal = analysis.GetWhileOrdinal(loop);
                    AppendMasmConditionData(
                        source,
                        loop.Condition,
                        WhileConditionPrefix(whileOrdinal),
                        conditionBuffers[loop.Condition],
                        ref whileComparisonIndex);
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

        if (inputs.Count > 0)
        {
            AppendMasmInputData(source, inputs, analysis);
        }

        if (checkedArithmetic)
        {
            AppendMasmStringData(
                source,
                "smileRuntimeOverflowMessage",
                "SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\n",
                "Exact SMILE Integer overflow diagnostic.",
                "Length of the overflow diagnostic.");
            AppendMasmStringData(
                source,
                "smileRuntimeDivisionByZeroMessage",
                "SMILE Runtime Error SMILER1207: Division by zero.\n",
                "Exact SMILE division-by-zero diagnostic.",
                "Length of the division-by-zero diagnostic.");
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
        }

        if (inputs.Count > 0)
        {
            AppendMasmLine(source, "stdinHandle QWORD ?", "Cached standard input handle.");
        }

        if (inputs.Count > 0 || checkedArithmetic)
        {
            AppendMasmLine(source, "stderrHandle QWORD ?", "Cached standard error handle.");
        }

        if (printCount > 0 || inputs.Count > 0 || checkedArithmetic)
        {
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

    private static void AppendMasmInputData(
        StringBuilder source,
        IReadOnlyList<BoundInputStatement> inputs,
        BoundProgramAnalysis analysis)
    {
        AppendMasmLine(
            source,
            $"smileInputLineBuffer BYTE {SmileLanguage.MaximumInputLineUtf8Bytes} DUP (?)",
            "One strict UTF-8 physical INPUT line before conversion.");
        AppendMasmLine(source, "smileInputLength DWORD 0", "Current INPUT line byte length.");
        AppendMasmLine(source, "smileInputByte BYTE 0", "One byte returned by ReadFile.");
        AppendMasmLine(source, "smileInputSkipLf BYTE 0", "Whether the next INPUT may begin with CR's paired LF.");
        AppendMasmLine(source, "smileInputFirstByte BYTE 0", "Whether this INPUT is classifying its first byte.");
        AppendMasmLine(source, "smileInputBytesRead DWORD 0", "ReadFile byte count for INPUT.");

        if (inputs.Any(input => input.Variable.Type is SmileType.Integer))
        {
            AppendMasmLine(source, "smileInputInteger QWORD 0", "Converted signed 64-bit INPUT value.");
            AppendMasmLine(source, "smileInputNegative BYTE 0", "Integer parser sign flag.");
        }

        if (inputs.Any(input => input.Variable.Type is SmileType.Boolean))
        {
            AppendMasmLine(source, "smileInputBoolean BYTE 0", "Converted Boolean INPUT value.");
        }

        foreach (BoundInputStatement input in inputs)
        {
            int ordinal = analysis.GetStatementFacts(input).Ordinal;
            if (input.Variable.Type is SmileType.String)
            {
                AppendMasmLine(
                    source,
                    $"{InputValueLabel(ordinal)} BYTE {SmileLanguage.MaximumInputLineUtf8Bytes} DUP (?)",
                    $"Stable exact String storage for INPUT {input.Variable.Name}.");
            }
            else if (input.Variable.Type is SmileType.Integer)
            {
                AppendMasmLine(
                    source,
                    $"{InputValueLabel(ordinal)} BYTE 21 DUP (?)",
                    $"Stable canonical Integer text for INPUT {input.Variable.Name}.");
            }

            foreach (int code in InputErrorCodes(input.Variable.Type))
            {
                AppendMasmStringData(
                    source,
                    InputErrorLabel(ordinal, code),
                    InputErrorText(code, input.Variable.Name),
                    $"Exact SMILER15{code:00} diagnostic for INPUT {input.Variable.Name}.",
                    "Length of this INPUT diagnostic.");
            }
        }
    }

    private static IEnumerable<int> InputErrorCodes(SmileType type)
    {
        yield return 1;
        yield return 2;
        if (type is SmileType.Integer)
        {
            yield return 3;
            yield return 4;
        }
        else if (type is SmileType.Boolean)
        {
            yield return 5;
        }

        yield return 6;
    }

    private static string InputErrorText(int code, string variableName) => code switch
    {
        1 => $"SMILE Runtime Error SMILER1501: Input ended before a value was received for '{variableName}'.\n",
        2 => $"SMILE Runtime Error SMILER1502: Input for '{variableName}' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.\n",
        3 => $"SMILE Runtime Error SMILER1503: Input for '{variableName}' is not a valid Integer.\n",
        4 => $"SMILE Runtime Error SMILER1504: Input for '{variableName}' is outside the signed 64-bit Integer range.\n",
        5 => $"SMILE Runtime Error SMILER1505: Input for '{variableName}' must be TRUE or FALSE.\n",
        _ => $"SMILE Runtime Error SMILER1506: Input for '{variableName}' could not be read as valid UTF-8 text.\n"
    };

    private static void AppendMasmConditionData(
        StringBuilder source,
        BoundExpression expression,
        string conditionPrefix,
        IReadOnlyList<RuntimeStringBuffer> runtimeBuffers,
        ref int comparisonIndex)
    {
        if (expression is BoundUnaryExpression unary)
        {
            AppendMasmConditionData(
                source,
                unary.Operand,
                conditionPrefix,
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
                conditionPrefix,
                runtimeBuffers,
                ref comparisonIndex);
            AppendMasmConditionData(
                source,
                binary.Right,
                conditionPrefix,
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
            runtimeBuffers,
            MasmConditionOperandLabel(conditionPrefix, currentComparison, "Left"));
        AppendMasmConditionOperandData(
            source,
            binary.Right,
            runtimeBuffers,
            MasmConditionOperandLabel(conditionPrefix, currentComparison, "Right"));
    }

    private static void AppendMasmConditionOperandData(
        StringBuilder source,
        BoundExpression expression,
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
        bool hasInput,
        bool checkedArithmetic,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
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

        if (hasInput)
        {
            source.AppendLine();
            AppendMasmLine(source, "    mov ecx, 65001", "Use UTF-8 for interactive Windows console input.");
            AppendMasmLine(source, "    call SetConsoleCP", "Redirected byte streams are unaffected.");
            AppendMasmLine(source, "    mov ecx, STD_INPUT_HANDLE", "Ask Windows for stdin.");
            AppendMasmLine(source, "    call GetStdHandle", "RAX receives the stdin handle.");
            AppendMasmLine(source, "    mov QWORD PTR [stdinHandle], rax", "Cache stdin for every INPUT.");
        }

        if (hasInput || checkedArithmetic)
        {
            AppendMasmLine(source, "    mov ecx, STD_ERROR_HANDLE", "Ask Windows for stderr.");
            AppendMasmLine(source, "    call GetStdHandle", "RAX receives the stderr handle.");
            AppendMasmLine(source, "    mov QWORD PTR [stderrHandle], rax", "Cache stderr for exact runtime diagnostics.");
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

        if (checkedArithmetic)
        {
            AppendMasmLine(source, "smileRuntimeOverflow:", "Reached checked signed Integer overflow.");
            AppendMasmLine(source, "    lea rdx, smileRuntimeOverflowMessage", "Address of exact SMILER1206 text.");
            AppendMasmLine(source, "    mov r8d, smileRuntimeOverflowMessageLength", "Length of exact SMILER1206 text.");
            AppendMasmLine(source, "    call smileFail", "Write stderr and terminate with exit code 1.");
            AppendMasmLine(source, "smileRuntimeDivisionByZero:", "Reached checked signed Integer division by zero.");
            AppendMasmLine(source, "    lea rdx, smileRuntimeDivisionByZeroMessage", "Address of exact SMILER1207 text.");
            AppendMasmLine(source, "    mov r8d, smileRuntimeDivisionByZeroMessageLength", "Length of exact SMILER1207 text.");
            AppendMasmLine(source, "    call smileFail", "Write stderr and terminate with exit code 1.");
        }

        AppendMasmLine(source, "main ENDP", "End of the main procedure.");
        if (needsIntegerFormatter)
        {
            source.AppendLine();
            AppendMasmIntegerFormatter(source);
        }

        if (hasInput)
        {
            source.AppendLine();
            AppendMasmInputProcedures(source, TargetRuntimeFacts.Inputs(program));
        }

        if (hasInput || checkedArithmetic)
        {
            source.AppendLine();
            AppendMasmFailureProcedure(source);
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
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
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

                case BoundInputStatement input:
                    AppendMasmInputStatement(
                        source,
                        input,
                        facts.Ordinal,
                        variableIndexes);
                    break;

                case BoundPrintStatement print:
                    AppendMasmPrint(
                        source,
                        print,
                        facts,
                        printIndex,
                        variableIndexes,
                        $"print{printIndex}",
                        statementBuffers,
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

                case BoundWhileStatement loop:
                    AppendMasmWhile(
                        source,
                        loop,
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
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
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
            foreach (RuntimeStringBuffer buffer in conditionBuffers[clause.Condition])
            {
                runtimeBufferMap.Add(buffer.Expression, buffer);
            }

            AppendMasmCondition(
                source,
                clause.Condition,
                IfConditionPrefix(clauseFacts.Ordinal),
                clauseFacts.ValuesBefore,
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

    private static void AppendMasmWhile(
        StringBuilder source,
        BoundWhileStatement loop,
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> booleanStringBuffers,
        ref int printIndex)
    {
        BoundWhileStatementAnalysis loopFacts = analysis.GetWhileFacts(loop);
        int ordinal = loopFacts.Ordinal;
        string conditionLabel = WhileConditionLabel(ordinal);
        string bodyLabel = WhileBodyLabel(ordinal);
        string endLabel = WhileEndLabel(ordinal);
        string conditionPrefix = WhileConditionPrefix(ordinal);

        source.AppendLine();
        AppendMasmLine(source, $"; WHILE #{ordinal + 1}", "Re-evaluate the condition before every body iteration.");
        AppendMasmLine(source, $"{conditionLabel}:", "WHILE condition and back-edge target.");

        int comparisonIndex = 0;
        int partIndex = 0;
        var runtimeBufferMap = new Dictionary<BoundExpression, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        foreach (RuntimeStringBuffer buffer in conditionBuffers[loop.Condition])
        {
            runtimeBufferMap.Add(buffer.Expression, buffer);
        }

        AppendMasmCondition(
            source,
            loop.Condition,
            conditionPrefix,
            loopFacts.ValuesAtHead,
            variableIndexes,
            runtimeBufferMap,
            booleanStringBuffers,
            ref comparisonIndex,
            ref partIndex);
        AppendMasmLine(source, "    test eax, eax", "Zero exits this pre-test loop.");
        AppendMasmLine(source, $"    jz {endLabel}", "Skip the body when the condition is false.");
        AppendMasmLine(source, $"{bodyLabel}:", "Execute the complete learner-authored body.");
        AppendMasmSourceItems(
            source,
            loop.SourceItems,
            analysis,
            variableIndexes,
            statementBuffers,
            conditionBuffers,
            booleanStringBuffers,
            ref printIndex);
        AppendMasmLine(source, $"    jmp {conditionLabel}", "Re-evaluate current storage after this iteration.");
        AppendMasmLine(source, $"{endLabel}:", "Continue after the complete WHILE.");
    }

    private static void AppendMasmCondition(
        StringBuilder source,
        BoundExpression expression,
        string conditionPrefix,
        IReadOnlyDictionary<VariableSymbol, AnalyzedValue> valuesAtCondition,
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
                conditionPrefix,
                valuesAtCondition,
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
                conditionPrefix,
                valuesAtCondition,
                variableIndexes,
                runtimeBuffers,
                booleanStringBuffers,
                ref comparisonIndex,
                ref partIndex);
            string endLabel = MasmConditionPartLabel(
                conditionPrefix,
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
                conditionPrefix,
                valuesAtCondition,
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
                conditionPrefix,
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
                            $"{conditionPrefix}Part{partIndex}",
                        booleanStringBuffers,
                        ref partIndex);
                }
            }

            AppendMasmDirectEquality(
                source,
                comparison,
                conditionPrefix,
                variableIndexes,
                runtimeBuffers,
                comparisonIndex++,
                ref partIndex);
            return;
        }

        if (!GeneratorConditionFacts.TryEvaluateFromAnalyzedValues(
                expression,
                valuesAtCondition,
                out SmileValue provenCondition))
        {
            throw new InvalidOperationException(
                "MASM requires runtime lowering for an abstract-unknown control-flow condition.");
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
                    AppendMasmLine(source, "    jo smileRuntimeOverflow", "Int64.MinValue cannot be negated.");
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
                        AppendMasmLine(source, "    jo smileRuntimeOverflow", "Report signed addition overflow.");
                        break;

                    case BoundBinaryOperatorKind.Subtraction:
                        AppendMasmLine(source, "    sub rax, r9", "Apply SMILE signed Integer subtraction.");
                        AppendMasmLine(source, "    jo smileRuntimeOverflow", "Report signed subtraction overflow.");
                        break;

                    case BoundBinaryOperatorKind.Multiplication:
                        AppendMasmLine(source, "    imul rax, r9", "Apply SMILE signed Integer multiplication.");
                        AppendMasmLine(source, "    jo smileRuntimeOverflow", "Report signed multiplication overflow.");
                        break;

                    case BoundBinaryOperatorKind.Division:
                        AppendMasmLine(source, "    test r9, r9", "Reject a zero divisor before IDIV.");
                        AppendMasmLine(source, "    jz smileRuntimeDivisionByZero", "Report SMILER1207 without a CPU trap.");
                        AppendMasmLine(source, "    mov r10, 08000000000000000h", "Int64.MinValue overflow sentinel.");
                        AppendMasmLine(source, "    cmp rax, r10", "Only Int64.MinValue divided by -1 overflows.");
                        AppendMasmLine(source, "    jne @F", "All other nonzero divisors are safe.");
                        AppendMasmLine(source, "    cmp r9, -1", "Inspect the one overflowing divisor.");
                        AppendMasmLine(source, "    je smileRuntimeOverflow", "Report SMILER1206 without a CPU trap.");
                        AppendMasmLine(source, "@@:", "The signed division is safe to execute.");
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
        string conditionPrefix,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        int comparisonIndex,
        ref int partIndex)
    {
        string loopLabel = MasmConditionPartLabel(conditionPrefix, partIndex++, "Compare");
        string differentLabel = MasmConditionPartLabel(conditionPrefix, partIndex++, "Different");
        string doneLabel = MasmConditionPartLabel(conditionPrefix, partIndex++, "Done");

        AppendMasmLoadConditionOperand(
            source,
            expression.Left,
            variableIndexes,
            runtimeBuffers,
            MasmConditionOperandLabel(conditionPrefix, comparisonIndex, "Left"),
            "r10",
            "ecx");
        AppendMasmLoadConditionOperand(
            source,
            expression.Right,
            variableIndexes,
            runtimeBuffers,
            MasmConditionOperandLabel(conditionPrefix, comparisonIndex, "Right"),
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
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
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
        else if (statementBuffers.TryGetValue(print, out RuntimeStringBuffer? buffer))
        {
            int partIndex = 0;
            AppendMasmRuntimeTextMaterialization(
                source,
                print.Value,
                buffer,
                variableIndexes,
                labelPrefix,
                booleanStringBuffers,
                ref partIndex);
            AppendMasmLine(source, $"    lea rax, {buffer.Label}", "Address of the complete PRINT value.");
            AppendMasmLine(
                source,
                $"    mov edx, DWORD PTR [{buffer.Label}Length]",
                "Length of the complete PRINT value.");
            AppendMasmWriteBuffer(source, "rax", "edx", "atomic runtime PRINT text");
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
            AppendMasmLine(source, "    mov rcx, rax", "Format the value that was evaluated exactly once.");
            AppendMasmLine(source, $"    call {IntegerFormatProcedure}", "Return canonical decimal text in RAX/EDX.");
            AppendMasmLine(source, "    mov rsi, rax", "Copy from the shared Integer formatter buffer.");
            AppendMasmLine(source, $"    lea rdi, {buffer.Label}", "Use stable storage owned by this assignment.");
            AppendMasmLine(source, "    mov ecx, edx", "Copy the exact canonical decimal byte length.");
            AppendMasmLine(source, "    rep movsb", "Keep direct variable PRINT synchronized with numeric storage.");
            AppendMasmLine(source, $"    lea rax, {buffer.Label}", $"Address of current {destination.Name} display text.");
            AppendMasmLine(
                source,
                $"    mov QWORD PTR [{VariablePointerLabel(destinationIndex)}], rax",
                "Store the current Integer display pointer.");
            AppendMasmLine(
                source,
                $"    mov DWORD PTR [{VariableLengthLabel(destinationIndex)}], edx",
                "Store the current Integer display length.");
            return;
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

    private static void AppendMasmInputStatement(
        StringBuilder source,
        BoundInputStatement input,
        int ordinal,
        IReadOnlyDictionary<VariableSymbol, int> variableIndexes)
    {
        int variableIndex = variableIndexes[input.Variable];
        string prefix = $"input{ordinal}";
        string convertedLabel = prefix + "Converted";
        string doneLabel = prefix + "Done";

        source.AppendLine();
        AppendMasmLine(source, $"; INPUT {input.Variable.Name}", "Read one physical UTF-8 line, then convert atomically.");
        AppendMasmLine(source, "    call smileReadInputLine", "EAX is 0 or the SMILER15xx suffix code.");
        AppendMasmLine(source, "    test eax, eax", "Was the physical line read successfully?");
        AppendMasmLine(source, $"    jz {prefix}LineReady", "Only a complete valid UTF-8 line may be converted.");
        AppendMasmLine(source, "    cmp eax, 1", "Distinguish immediate EOF.");
        AppendMasmLine(source, $"    je {prefix}Error1", "Report SMILER1501.");
        AppendMasmLine(source, "    cmp eax, 2", "Distinguish an over-limit line.");
        AppendMasmLine(source, $"    je {prefix}Error2", "Report SMILER1502.");
        AppendMasmLine(source, $"    jmp {prefix}Error6", "Every other read/decoding failure is SMILER1506.");
        AppendMasmLine(source, $"{prefix}LineReady:", "The exact line bytes are available.");

        switch (input.Variable.Type)
        {
            case SmileType.String:
                AppendMasmLine(source, "    lea rsi, smileInputLineBuffer", "Copy from the shared line buffer.");
                AppendMasmLine(source, $"    lea rdi, {InputValueLabel(ordinal)}", "Use stable storage owned by this INPUT.");
                AppendMasmLine(source, "    mov ecx, DWORD PTR [smileInputLength]", "Copy the exact logical byte count.");
                AppendMasmLine(source, "    rep movsb", "Embedded NUL and every other valid UTF-8 byte are data.");
                AppendMasmLine(source, $"    lea rax, {InputValueLabel(ordinal)}", "Address of the new String value.");
                AppendMasmLine(source, $"    mov QWORD PTR [{VariablePointerLabel(variableIndex)}], rax", "Commit the String pointer after successful reading.");
                AppendMasmLine(source, "    mov eax, DWORD PTR [smileInputLength]", "Read the exact String byte length.");
                AppendMasmLine(source, $"    mov DWORD PTR [{VariableLengthLabel(variableIndex)}], eax", "Commit the String length atomically with its pointer.");
                break;

            case SmileType.Integer:
                AppendMasmLine(source, "    call smileParseInputInteger", "Validate canonical SMILE Integer input.");
                AppendMasmLine(source, "    test eax, eax", "Was Integer conversion successful?");
                AppendMasmLine(source, $"    jz {convertedLabel}", "Commit only a fully converted Int64 value.");
                AppendMasmLine(source, "    cmp eax, 3", "Distinguish malformed text from range overflow.");
                AppendMasmLine(source, $"    je {prefix}Error3", "Report SMILER1503.");
                AppendMasmLine(source, $"    jmp {prefix}Error4", "The remaining parse failure is SMILER1504.");
                AppendMasmLine(source, $"{convertedLabel}:", "The signed 64-bit INPUT value is ready.");
                AppendMasmLine(source, "    mov rax, QWORD PTR [smileInputInteger]", "Read the converted value.");
                AppendMasmLine(source, $"    mov QWORD PTR [{VariableIntegerLabel(variableIndex)}], rax", "Commit current Integer storage.");
                AppendMasmLine(source, "    mov rcx, rax", "Format canonical decimal display text.");
                AppendMasmLine(source, $"    call {IntegerFormatProcedure}", "Return canonical decimal pointer and length.");
                AppendMasmLine(source, "    mov rsi, rax", "Copy formatter output before another value can reuse it.");
                AppendMasmLine(source, $"    lea rdi, {InputValueLabel(ordinal)}", "Use stable storage owned by this INPUT.");
                AppendMasmLine(source, "    mov ecx, edx", "Copy exactly the canonical decimal bytes.");
                AppendMasmLine(source, "    rep movsb", "Preserve direct variable PRINT storage.");
                AppendMasmLine(source, $"    lea rax, {InputValueLabel(ordinal)}", "Address of canonical Integer text.");
                AppendMasmLine(source, $"    mov QWORD PTR [{VariablePointerLabel(variableIndex)}], rax", "Commit the Integer display pointer.");
                AppendMasmLine(source, $"    mov DWORD PTR [{VariableLengthLabel(variableIndex)}], edx", "Commit the Integer display length.");
                break;

            case SmileType.Boolean:
                AppendMasmLine(source, "    call smileParseInputBoolean", "Accept only TRUE or FALSE, ordinal-ignore-case.");
                AppendMasmLine(source, "    test eax, eax", "Was Boolean conversion successful?");
                AppendMasmLine(source, $"    jnz {prefix}Error5", "Report SMILER1505 without changing storage.");
                AppendMasmLine(source, "    mov al, BYTE PTR [smileInputBoolean]", "Read the normalized Boolean value.");
                AppendMasmLine(source, $"    mov BYTE PTR [{VariableBooleanLabel(variableIndex)}], al", "Commit current Boolean storage.");
                AppendMasmLine(source, "    test al, al", "Choose canonical display text.");
                AppendMasmLine(source, $"    jz {prefix}BooleanFalse", "Zero selects FALSE.");
                AppendMasmLine(source, "    lea rax, smileBooleanTrue", "Address of canonical TRUE text.");
                AppendMasmLine(source, "    mov edx, smileBooleanTrueLength", "Length of TRUE text.");
                AppendMasmLine(source, $"    jmp {prefix}BooleanReady", "Skip the FALSE selection.");
                AppendMasmLine(source, $"{prefix}BooleanFalse:", "Select canonical FALSE text.");
                AppendMasmLine(source, "    lea rax, smileBooleanFalse", "Address of canonical FALSE text.");
                AppendMasmLine(source, "    mov edx, smileBooleanFalseLength", "Length of FALSE text.");
                AppendMasmLine(source, $"{prefix}BooleanReady:", "Boolean pointer and length are ready.");
                AppendMasmLine(source, $"    mov QWORD PTR [{VariablePointerLabel(variableIndex)}], rax", "Commit the Boolean display pointer.");
                AppendMasmLine(source, $"    mov DWORD PTR [{VariableLengthLabel(variableIndex)}], edx", "Commit the Boolean display length.");
                break;
        }

        AppendMasmLine(source, $"    jmp {doneLabel}", "Skip statement-local fatal error labels.");
        foreach (int code in InputErrorCodes(input.Variable.Type))
        {
            string label = InputErrorLabel(ordinal, code);
            AppendMasmLine(source, $"{prefix}Error{code}:", $"Prepare exact SMILER15{code:00} text.");
            AppendMasmLine(source, $"    lea rdx, {label}", "Runtime diagnostic address.");
            AppendMasmLine(source, $"    mov r8d, {label}Length", "Runtime diagnostic byte length.");
            AppendMasmLine(source, "    call smileFail", "Write stderr and terminate with exit code 1.");
        }

        AppendMasmLine(source, $"{doneLabel}:", "INPUT completed successfully.");
    }

    private static void AppendMasmInputProcedures(
        StringBuilder source,
        IReadOnlyList<BoundInputStatement> inputs)
    {
        AppendMasmReadByteProcedure(source);
        source.AppendLine();
        AppendMasmUtf8ValidationProcedure(source);
        source.AppendLine();
        AppendMasmReadLineProcedure(source);
        if (inputs.Any(input => input.Variable.Type is SmileType.Integer))
        {
            source.AppendLine();
            AppendMasmIntegerInputProcedure(source);
        }

        if (inputs.Any(input => input.Variable.Type is SmileType.Boolean))
        {
            source.AppendLine();
            AppendMasmBooleanInputProcedure(source);
        }
    }

    private static void AppendMasmReadByteProcedure(StringBuilder source)
    {
        AppendMasmLine(source, "smileReadInputByte PROC", "Return 0..255, -1 for EOF, or -2 for read failure.");
        AppendMasmLine(source, "    sub rsp, 28h", "Reserve Win64 shadow space and align the stack.");
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stdinHandle]", "Read from cached stdin.");
        AppendMasmLine(source, "    lea rdx, smileInputByte", "ReadFile destination byte.");
        AppendMasmLine(source, "    mov r8d, 1", "Request exactly one byte for deterministic line boundaries.");
        AppendMasmLine(source, "    lea r9, smileInputBytesRead", "Receive the number of bytes read.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "No overlapped I/O.");
        AppendMasmLine(source, "    call ReadFile", "Read redirected bytes or UTF-8 console bytes.");
        AppendMasmLine(source, "    test eax, eax", "Did the Windows read operation succeed?");
        AppendMasmLine(source, "    jz smileReadInputByteCheckFailure", "A closed pipe is EOF; other failures are SMILER1506.");
        AppendMasmLine(source, "    cmp DWORD PTR [smileInputBytesRead], 0", "A successful zero-byte read is EOF.");
        AppendMasmLine(source, "    je smileReadInputByteEof", "Return the EOF sentinel.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [smileInputByte]", "Return the exact unsigned byte.");
        AppendMasmLine(source, "    add rsp, 28h", "Release procedure stack space.");
        AppendMasmLine(source, "    ret", "Return one input byte.");
        AppendMasmLine(source, "smileReadInputByteCheckFailure:", "Classify the failed Windows read.");
        AppendMasmLine(source, "    call GetLastError", "ReadFile reports redirected-pipe EOF as ERROR_BROKEN_PIPE.");
        AppendMasmLine(source, "    cmp eax, 109", "ERROR_BROKEN_PIPE means the scripted writer closed normally.");
        AppendMasmLine(source, "    je smileReadInputByteEof", "Treat a closed stdin pipe as EOF.");
        AppendMasmLine(source, "    jmp smileReadInputByteFailure", "Every other failure is SMILER1506.");
        AppendMasmLine(source, "smileReadInputByteEof:", "Return the EOF sentinel.");
        AppendMasmLine(source, "    mov eax, -1", "-1 cannot collide with an unsigned byte.");
        AppendMasmLine(source, "    add rsp, 28h", "Release procedure stack space.");
        AppendMasmLine(source, "    ret", "Return EOF.");
        AppendMasmLine(source, "smileReadInputByteFailure:", "Return the read-failure sentinel.");
        AppendMasmLine(source, "    mov eax, -2", "-2 means SMILER1506.");
        AppendMasmLine(source, "    add rsp, 28h", "Release procedure stack space.");
        AppendMasmLine(source, "    ret", "Return read failure.");
        AppendMasmLine(source, "smileReadInputByte ENDP", "End one-byte reader.");
    }

    private static void AppendMasmReadLineProcedure(StringBuilder source)
    {
        AppendMasmLine(source, "smileReadInputLine PROC", "Read LF, CRLF, standalone CR, or a final nonempty EOF line.");
        AppendMasmLine(source, "    push rsi", "Preserve the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    sub rsp, 20h", "Reserve Win64 shadow space while keeping calls aligned.");
        AppendMasmLine(source, "    lea rsi, smileInputLineBuffer", "Use register-relative indexing for large-address-safe code.");
        AppendMasmLine(source, "    mov DWORD PTR [smileInputLength], 0", "Start a new physical line.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputFirstByte], 1", "Only the first byte may complete a prior CRLF ending.");
        AppendMasmLine(source, "smileReadInputLineLoop:", "Read until one SMILE line boundary.");
        AppendMasmLine(source, "    call smileReadInputByte", "Return a byte or a negative sentinel.");
        AppendMasmLine(source, "    cmp eax, -2", "Did the byte read fail?");
        AppendMasmLine(source, "    je smileReadInputLineReadFailure", "Report SMILER1506.");
        AppendMasmLine(source, "    cmp eax, -1", "Did stdin end?");
        AppendMasmLine(source, "    je smileReadInputLineEof", "Only a nonempty final line succeeds at EOF.");
        AppendMasmLine(source, "    cmp BYTE PTR [smileInputFirstByte], 0", "Can this byte be a deferred CRLF line feed?");
        AppendMasmLine(source, "    je smileReadInputLineHaveByte", "Only the first byte needs the deferred check.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputFirstByte], 0", "All later bytes belong to this line.");
        AppendMasmLine(source, "    cmp BYTE PTR [smileInputSkipLf], 0", "Did the prior INPUT stop immediately at CR?");
        AppendMasmLine(source, "    je smileReadInputLineHaveByte", "There is no possible paired LF to skip.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputSkipLf], 0", "Consume the one-byte deferred decision.");
        AppendMasmLine(source, "    cmp al, 10", "LF paired with the prior CR is not this line's value.");
        AppendMasmLine(source, "    je smileReadInputLineLoop", "Read this INPUT's actual first byte.");
        AppendMasmLine(source, "smileReadInputLineHaveByte:", "Classify this exact input byte.");
        AppendMasmLine(source, "    cmp al, 10", "LF terminates the physical line.");
        AppendMasmLine(source, "    je smileReadInputLineValidate", "Do not include LF in the value.");
        AppendMasmLine(source, "    cmp al, 13", "CR ends this INPUT without reading into the next line.");
        AppendMasmLine(source, "    je smileReadInputLineCarriageReturn", "Defer a possible paired LF to the next INPUT.");
        AppendMasmLine(source, "    mov ecx, DWORD PTR [smileInputLength]", "Current line length before storing this byte.");
        AppendMasmLine(source, $"    cmp ecx, {SmileLanguage.MaximumInputLineUtf8Bytes}", "Enforce the pre-trim UTF-8 byte limit.");
        AppendMasmLine(source, "    jae smileReadInputLineTooLong", "A 4097th data byte is SMILER1502.");
        AppendMasmLine(source, "    mov BYTE PTR [rsi + rcx], al", "Preserve the byte exactly, including NUL.");
        AppendMasmLine(source, "    inc ecx", "Advance the exact byte count.");
        AppendMasmLine(source, "    mov DWORD PTR [smileInputLength], ecx", "Publish the updated line length.");
        AppendMasmLine(source, "    jmp smileReadInputLineLoop", "Continue this physical line.");
        AppendMasmLine(source, "smileReadInputLineCarriageReturn:", "Finish now so later input failures cannot affect this INPUT.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputSkipLf], 1", "The next INPUT will discard one leading LF if present.");
        AppendMasmLine(source, "    jmp smileReadInputLineValidate", "The CR completes this physical line.");
        AppendMasmLine(source, "smileReadInputLineEof:", "Handle EOF without a line-ending byte.");
        AppendMasmLine(source, "    cmp DWORD PTR [smileInputLength], 0", "A final nonempty physical line is valid.");
        AppendMasmLine(source, "    jne smileReadInputLineValidate", "Validate and return the final value.");
        AppendMasmLine(source, "    mov eax, 1", "Immediate EOF maps to SMILER1501.");
        AppendMasmLine(source, "    jmp smileReadInputLineReturn", "Return the INPUT status.");
        AppendMasmLine(source, "smileReadInputLineValidate:", "Validate the complete raw line as strict UTF-8.");
        AppendMasmLine(source, "    call smileValidateInputUtf8", "EAX is one only for scalar, shortest-form UTF-8.");
        AppendMasmLine(source, "    test eax, eax", "Was the byte sequence valid UTF-8?");
        AppendMasmLine(source, "    jz smileReadInputLineInvalidUtf8", "Malformed input is SMILER1506.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means the line is ready for conversion.");
        AppendMasmLine(source, "    jmp smileReadInputLineReturn", "Return success.");
        AppendMasmLine(source, "smileReadInputLineTooLong:", "Return the over-limit status.");
        AppendMasmLine(source, "    mov eax, 2", "SMILER1502 suffix code.");
        AppendMasmLine(source, "    jmp smileReadInputLineReturn", "Return the INPUT status.");
        AppendMasmLine(source, "smileReadInputLineReadFailure:", "Return strict read failure.");
        AppendMasmLine(source, "smileReadInputLineInvalidUtf8:", "Return strict decoding failure.");
        AppendMasmLine(source, "    mov eax, 6", "SMILER1506 suffix code.");
        AppendMasmLine(source, "smileReadInputLineReturn:", "Return one deterministic status code.");
        AppendMasmLine(source, "    add rsp, 20h", "Release procedure shadow space.");
        AppendMasmLine(source, "    pop rsi", "Restore the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    ret", "Return to this INPUT statement.");
        AppendMasmLine(source, "smileReadInputLine ENDP", "End physical line reader.");
    }

    private static void AppendMasmUtf8ValidationProcedure(StringBuilder source)
    {
        AppendMasmLine(source, "smileValidateInputUtf8 PROC", "Validate strict Unicode scalar UTF-8 without decoding the bytes.");
        AppendMasmLine(source, "    lea r10, smileInputLineBuffer", "Address of the current raw INPUT line.");
        AppendMasmLine(source, "    xor ecx, ecx", "Start at byte index zero.");
        AppendMasmLine(source, "    mov edx, DWORD PTR [smileInputLength]", "Exact number of bytes to validate.");
        AppendMasmLine(source, "smileUtf8Next:", "Validate one Unicode scalar encoding.");
        AppendMasmLine(source, "    cmp ecx, edx", "Have all bytes been consumed?");
        AppendMasmLine(source, "    jae smileUtf8Valid", "The complete line is valid.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [r10 + rcx]", "Read the leading byte.");
        AppendMasmLine(source, "    cmp eax, 07Fh", "ASCII, including NUL, is a complete scalar.");
        AppendMasmLine(source, "    jbe smileUtf8One", "Advance one byte.");
        AppendMasmLine(source, "    cmp eax, 0C2h", "C0/C1 and stray continuation bytes are invalid.");
        AppendMasmLine(source, "    jb smileUtf8Invalid", "Reject overlong or isolated bytes.");
        AppendMasmLine(source, "    cmp eax, 0DFh", "C2..DF begin a two-byte scalar.");
        AppendMasmLine(source, "    jbe smileUtf8Two", "Validate one continuation byte.");
        AppendMasmLine(source, "    cmp eax, 0E0h", "E0 needs an A0..BF second byte.");
        AppendMasmLine(source, "    je smileUtf8ThreeE0", "Reject three-byte overlong encodings.");
        AppendMasmLine(source, "    cmp eax, 0EDh", "ED needs an 80..9F second byte.");
        AppendMasmLine(source, "    je smileUtf8ThreeEd", "Reject UTF-16 surrogate scalars.");
        AppendMasmLine(source, "    cmp eax, 0E1h", "E1..EF otherwise use ordinary continuations.");
        AppendMasmLine(source, "    jb smileUtf8Invalid", "Reject impossible leading bytes.");
        AppendMasmLine(source, "    cmp eax, 0EFh", "End of the three-byte leading range.");
        AppendMasmLine(source, "    jbe smileUtf8Three", "Validate two continuation bytes.");
        AppendMasmLine(source, "    cmp eax, 0F0h", "F0 needs a 90..BF second byte.");
        AppendMasmLine(source, "    je smileUtf8FourF0", "Reject four-byte overlong encodings.");
        AppendMasmLine(source, "    cmp eax, 0F4h", "F4 needs an 80..8F second byte.");
        AppendMasmLine(source, "    je smileUtf8FourF4", "Reject values above U+10FFFF.");
        AppendMasmLine(source, "    cmp eax, 0F1h", "F1..F3 use ordinary continuations.");
        AppendMasmLine(source, "    jb smileUtf8Invalid", "Reject impossible leading bytes.");
        AppendMasmLine(source, "    cmp eax, 0F3h", "End of valid ordinary four-byte leaders.");
        AppendMasmLine(source, "    jbe smileUtf8Four", "Validate three continuation bytes.");
        AppendMasmLine(source, "    jmp smileUtf8Invalid", "F5..FF are not Unicode scalar UTF-8.");

        AppendMasmLine(source, "smileUtf8One:", "Advance past one ASCII byte.");
        AppendMasmLine(source, "    inc ecx", "Consume the scalar.");
        AppendMasmLine(source, "    jmp smileUtf8Next", "Validate the next scalar.");

        AppendMasmLine(source, "smileUtf8Two:", "Validate a two-byte scalar.");
        AppendMasmLine(source, "    lea r11d, [ecx + 1]", "Index of its continuation byte.");
        AppendMasmLine(source, "    cmp r11d, edx", "Is that byte present?");
        AppendMasmLine(source, "    jae smileUtf8Invalid", "Reject a truncated sequence.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 1]", "Read the continuation byte.");
        AppendMasmLine(source, "    and r8d, 0C0h", "Continuation bytes begin with binary 10.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Check the continuation prefix.");
        AppendMasmLine(source, "    jne smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    add ecx, 2", "Consume the complete scalar.");
        AppendMasmLine(source, "    jmp smileUtf8Next", "Validate the next scalar.");

        AppendMasmLine(source, "smileUtf8ThreeE0:", "Validate E0's constrained second byte.");
        AppendMasmLine(source, "    lea r11d, [ecx + 2]", "Index of the last required byte.");
        AppendMasmLine(source, "    cmp r11d, edx", "Are both continuation bytes present?");
        AppendMasmLine(source, "    jae smileUtf8Invalid", "Reject a truncated sequence.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 1]", "Read E0's second byte.");
        AppendMasmLine(source, "    cmp r8d, 0A0h", "Shortest-form E0 starts at A0.");
        AppendMasmLine(source, "    jb smileUtf8Invalid", "Reject an overlong sequence.");
        AppendMasmLine(source, "    cmp r8d, 0BFh", "Continuation bytes end at BF.");
        AppendMasmLine(source, "    ja smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    jmp smileUtf8ThreeLast", "Validate the final continuation.");

        AppendMasmLine(source, "smileUtf8ThreeEd:", "Validate ED's constrained second byte.");
        AppendMasmLine(source, "    lea r11d, [ecx + 2]", "Index of the last required byte.");
        AppendMasmLine(source, "    cmp r11d, edx", "Are both continuation bytes present?");
        AppendMasmLine(source, "    jae smileUtf8Invalid", "Reject a truncated sequence.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 1]", "Read ED's second byte.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Continuation bytes start at 80.");
        AppendMasmLine(source, "    jb smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    cmp r8d, 09Fh", "ED must stay below surrogate encodings.");
        AppendMasmLine(source, "    ja smileUtf8Invalid", "Reject UTF-16 surrogate values.");
        AppendMasmLine(source, "    jmp smileUtf8ThreeLast", "Validate the final continuation.");

        AppendMasmLine(source, "smileUtf8Three:", "Validate an ordinary three-byte scalar.");
        AppendMasmLine(source, "    lea r11d, [ecx + 2]", "Index of the last required byte.");
        AppendMasmLine(source, "    cmp r11d, edx", "Are both continuation bytes present?");
        AppendMasmLine(source, "    jae smileUtf8Invalid", "Reject a truncated sequence.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 1]", "Read the second byte.");
        AppendMasmLine(source, "    and r8d, 0C0h", "Check its continuation prefix.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Continuation bytes begin with binary 10.");
        AppendMasmLine(source, "    jne smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "smileUtf8ThreeLast:", "Validate the third byte.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 2]", "Read the final continuation.");
        AppendMasmLine(source, "    and r8d, 0C0h", "Check its continuation prefix.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Continuation bytes begin with binary 10.");
        AppendMasmLine(source, "    jne smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    add ecx, 3", "Consume the complete scalar.");
        AppendMasmLine(source, "    jmp smileUtf8Next", "Validate the next scalar.");

        AppendMasmLine(source, "smileUtf8FourF0:", "Validate F0's constrained second byte.");
        AppendMasmLine(source, "    lea r11d, [ecx + 3]", "Index of the last required byte.");
        AppendMasmLine(source, "    cmp r11d, edx", "Are all continuation bytes present?");
        AppendMasmLine(source, "    jae smileUtf8Invalid", "Reject a truncated sequence.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 1]", "Read F0's second byte.");
        AppendMasmLine(source, "    cmp r8d, 090h", "Shortest-form F0 starts at 90.");
        AppendMasmLine(source, "    jb smileUtf8Invalid", "Reject an overlong sequence.");
        AppendMasmLine(source, "    cmp r8d, 0BFh", "Continuation bytes end at BF.");
        AppendMasmLine(source, "    ja smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    jmp smileUtf8FourLast", "Validate the remaining continuations.");

        AppendMasmLine(source, "smileUtf8FourF4:", "Validate F4's constrained second byte.");
        AppendMasmLine(source, "    lea r11d, [ecx + 3]", "Index of the last required byte.");
        AppendMasmLine(source, "    cmp r11d, edx", "Are all continuation bytes present?");
        AppendMasmLine(source, "    jae smileUtf8Invalid", "Reject a truncated sequence.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 1]", "Read F4's second byte.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Continuation bytes start at 80.");
        AppendMasmLine(source, "    jb smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    cmp r8d, 08Fh", "F4 must stay at or below U+10FFFF.");
        AppendMasmLine(source, "    ja smileUtf8Invalid", "Reject values above Unicode's limit.");
        AppendMasmLine(source, "    jmp smileUtf8FourLast", "Validate the remaining continuations.");

        AppendMasmLine(source, "smileUtf8Four:", "Validate an ordinary four-byte scalar.");
        AppendMasmLine(source, "    lea r11d, [ecx + 3]", "Index of the last required byte.");
        AppendMasmLine(source, "    cmp r11d, edx", "Are all continuation bytes present?");
        AppendMasmLine(source, "    jae smileUtf8Invalid", "Reject a truncated sequence.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 1]", "Read the second byte.");
        AppendMasmLine(source, "    and r8d, 0C0h", "Check its continuation prefix.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Continuation bytes begin with binary 10.");
        AppendMasmLine(source, "    jne smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "smileUtf8FourLast:", "Validate the third and fourth bytes.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 2]", "Read the third byte.");
        AppendMasmLine(source, "    and r8d, 0C0h", "Check its continuation prefix.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Continuation bytes begin with binary 10.");
        AppendMasmLine(source, "    jne smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    movzx r8d, BYTE PTR [r10 + rcx + 3]", "Read the fourth byte.");
        AppendMasmLine(source, "    and r8d, 0C0h", "Check its continuation prefix.");
        AppendMasmLine(source, "    cmp r8d, 080h", "Continuation bytes begin with binary 10.");
        AppendMasmLine(source, "    jne smileUtf8Invalid", "Reject malformed continuation.");
        AppendMasmLine(source, "    add ecx, 4", "Consume the complete scalar.");
        AppendMasmLine(source, "    jmp smileUtf8Next", "Validate the next scalar.");

        AppendMasmLine(source, "smileUtf8Valid:", "Return valid.");
        AppendMasmLine(source, "    mov eax, 1", "One means valid strict UTF-8.");
        AppendMasmLine(source, "    ret", "Return to the physical line reader.");
        AppendMasmLine(source, "smileUtf8Invalid:", "Return invalid.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means malformed UTF-8.");
        AppendMasmLine(source, "    ret", "Return to the physical line reader.");
        AppendMasmLine(source, "smileValidateInputUtf8 ENDP", "End strict UTF-8 validator.");
    }

    private static void AppendMasmIntegerInputProcedure(StringBuilder source)
    {
        AppendMasmLine(source, "smileParseInputInteger PROC", "Parse ASCII-space/tab-trimmed [+-]?[0-9]+ into Int64.");
        AppendMasmLine(source, "    push rsi", "Preserve the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    lea rsi, smileInputLineBuffer", "Use register-relative indexing for large-address-safe code.");
        AppendMasmLine(source, "    xor r10d, r10d", "Start index after leading trim.");
        AppendMasmLine(source, "    mov r11d, DWORD PTR [smileInputLength]", "End index before trailing trim.");
        AppendMasmLine(source, "smileInputIntegerTrimStart:", "Trim only ASCII space and tab.");
        AppendMasmLine(source, "    cmp r10d, r11d", "Is any text left?");
        AppendMasmLine(source, "    jae smileInputIntegerMalformed", "Whitespace-only input is malformed.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10]", "Inspect the next leading byte.");
        AppendMasmLine(source, "    cmp al, ' '", "ASCII space is trim whitespace.");
        AppendMasmLine(source, "    je smileInputIntegerAdvanceStart", "Skip it.");
        AppendMasmLine(source, "    cmp al, 9", "ASCII tab is trim whitespace.");
        AppendMasmLine(source, "    jne smileInputIntegerTrimEnd", "No more leading trim.");
        AppendMasmLine(source, "smileInputIntegerAdvanceStart:", "Advance leading trim.");
        AppendMasmLine(source, "    inc r10d", "Skip one byte.");
        AppendMasmLine(source, "    jmp smileInputIntegerTrimStart", "Continue leading trim.");
        AppendMasmLine(source, "smileInputIntegerTrimEnd:", "Trim only ASCII space and tab at the end.");
        AppendMasmLine(source, "    cmp r11d, r10d", "Is any text left?");
        AppendMasmLine(source, "    jbe smileInputIntegerMalformed", "Whitespace-only input is malformed.");
        AppendMasmLine(source, "    mov eax, r11d", "Address the last untrimmed byte.");
        AppendMasmLine(source, "    dec eax", "Convert exclusive end to an index.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + rax]", "Inspect trailing whitespace.");
        AppendMasmLine(source, "    cmp al, ' '", "ASCII space is trim whitespace.");
        AppendMasmLine(source, "    je smileInputIntegerRetreatEnd", "Skip it.");
        AppendMasmLine(source, "    cmp al, 9", "ASCII tab is trim whitespace.");
        AppendMasmLine(source, "    jne smileInputIntegerSign", "The token boundaries are ready.");
        AppendMasmLine(source, "smileInputIntegerRetreatEnd:", "Retreat trailing trim.");
        AppendMasmLine(source, "    dec r11d", "Drop one byte.");
        AppendMasmLine(source, "    jmp smileInputIntegerTrimEnd", "Continue trailing trim.");
        AppendMasmLine(source, "smileInputIntegerSign:", "Recognize one optional leading sign.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputNegative], 0", "Default to nonnegative.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10]", "Inspect the first token byte.");
        AppendMasmLine(source, "    cmp al, '+'", "A leading plus is allowed.");
        AppendMasmLine(source, "    je smileInputIntegerSkipSign", "Consume the sign.");
        AppendMasmLine(source, "    cmp al, '-'", "A leading minus is allowed.");
        AppendMasmLine(source, "    jne smileInputIntegerDigits", "Otherwise the first byte must be a digit.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputNegative], 1", "Remember a negative magnitude.");
        AppendMasmLine(source, "smileInputIntegerSkipSign:", "Consume the optional sign.");
        AppendMasmLine(source, "    inc r10d", "Move to the first required digit.");
        AppendMasmLine(source, "    cmp r10d, r11d", "A sign alone is invalid.");
        AppendMasmLine(source, "    jae smileInputIntegerMalformed", "Require at least one digit.");
        AppendMasmLine(source, "smileInputIntegerDigits:", "Accumulate an unsigned magnitude with explicit range checks.");
        AppendMasmLine(source, "    xor r8d, r8d", "Magnitude starts at zero.");
        AppendMasmLine(source, "smileInputIntegerDigitLoop:", "Consume every remaining ASCII digit.");
        AppendMasmLine(source, "    cmp r10d, r11d", "Have all digits been consumed?");
        AppendMasmLine(source, "    jae smileInputIntegerComplete", "Apply the sign and commit the parsed value.");
        AppendMasmLine(source, "    movzx edx, BYTE PTR [rsi + r10]", "Read the next token byte.");
        AppendMasmLine(source, "    cmp dl, '0'", "Digits begin at ASCII zero.");
        AppendMasmLine(source, "    jb smileInputIntegerMalformed", "Reject non-digits.");
        AppendMasmLine(source, "    cmp dl, '9'", "Digits end at ASCII nine.");
        AppendMasmLine(source, "    ja smileInputIntegerMalformed", "Reject non-digits.");
        AppendMasmLine(source, "    sub edx, '0'", "Convert this byte to a numeric digit.");
        AppendMasmLine(source, "    mov rax, 0CCCCCCCCCCCCCCCh", "Int64 magnitude limit divided by ten.");
        AppendMasmLine(source, "    cmp r8, rax", "Would another decimal digit exceed the quotient?");
        AppendMasmLine(source, "    ja smileInputIntegerRange", "The token is numeric but outside Int64.");
        AppendMasmLine(source, "    jb smileInputIntegerAccumulate", "There is room for any decimal digit.");
        AppendMasmLine(source, "    mov r9d, 7", "Positive Int64 permits a final digit of seven.");
        AppendMasmLine(source, "    cmp BYTE PTR [smileInputNegative], 0", "Negative Int64 has one extra magnitude value.");
        AppendMasmLine(source, "    je smileInputIntegerCheckLast", "Keep the positive bound.");
        AppendMasmLine(source, "    mov r9d, 8", "Int64.MinValue permits a final digit of eight.");
        AppendMasmLine(source, "smileInputIntegerCheckLast:", "Check the boundary token's final digit.");
        AppendMasmLine(source, "    cmp edx, r9d", "Is the final magnitude digit within range?");
        AppendMasmLine(source, "    ja smileInputIntegerRange", "The token is numeric but outside Int64.");
        AppendMasmLine(source, "smileInputIntegerAccumulate:", "Append one decimal digit.");
        AppendMasmLine(source, "    imul r8, r8, 10", "Shift the magnitude one decimal place.");
        AppendMasmLine(source, "    add r8, rdx", "Append this digit.");
        AppendMasmLine(source, "    inc r10d", "Advance to the next byte.");
        AppendMasmLine(source, "    jmp smileInputIntegerDigitLoop", "Continue the token.");
        AppendMasmLine(source, "smileInputIntegerComplete:", "Apply the optional sign.");
        AppendMasmLine(source, "    cmp BYTE PTR [smileInputNegative], 0", "Was a minus sign present?");
        AppendMasmLine(source, "    je smileInputIntegerStore", "A positive magnitude is ready.");
        AppendMasmLine(source, "    neg r8", "Two's-complement negation also represents Int64.MinValue.");
        AppendMasmLine(source, "smileInputIntegerStore:", "Publish the converted value only after full validation.");
        AppendMasmLine(source, "    mov QWORD PTR [smileInputInteger], r8", "Atomic conversion scratch storage.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means conversion success.");
        AppendMasmLine(source, "    pop rsi", "Restore the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    ret", "Return to INPUT.");
        AppendMasmLine(source, "smileInputIntegerMalformed:", "Return malformed Integer status.");
        AppendMasmLine(source, "    mov eax, 3", "SMILER1503 suffix code.");
        AppendMasmLine(source, "    pop rsi", "Restore the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    ret", "Return to INPUT.");
        AppendMasmLine(source, "smileInputIntegerRange:", "Return Integer range status.");
        AppendMasmLine(source, "    mov eax, 4", "SMILER1504 suffix code.");
        AppendMasmLine(source, "    pop rsi", "Restore the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    ret", "Return to INPUT.");
        AppendMasmLine(source, "smileParseInputInteger ENDP", "End strict Integer parser.");
    }

    private static void AppendMasmBooleanInputProcedure(StringBuilder source)
    {
        AppendMasmLine(source, "smileParseInputBoolean PROC", "Parse ASCII-space/tab-trimmed TRUE or FALSE.");
        AppendMasmLine(source, "    push rsi", "Preserve the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    lea rsi, smileInputLineBuffer", "Use register-relative indexing for large-address-safe code.");
        AppendMasmLine(source, "    xor r10d, r10d", "Start index after leading trim.");
        AppendMasmLine(source, "    mov r11d, DWORD PTR [smileInputLength]", "End index before trailing trim.");
        AppendMasmLine(source, "smileInputBooleanTrimStart:", "Trim only ASCII space and tab.");
        AppendMasmLine(source, "    cmp r10d, r11d", "Is any text left?");
        AppendMasmLine(source, "    jae smileInputBooleanMalformed", "Whitespace-only input is invalid Boolean text.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10]", "Inspect leading whitespace.");
        AppendMasmLine(source, "    cmp al, ' '", "ASCII space is trim whitespace.");
        AppendMasmLine(source, "    je smileInputBooleanAdvanceStart", "Skip it.");
        AppendMasmLine(source, "    cmp al, 9", "ASCII tab is trim whitespace.");
        AppendMasmLine(source, "    jne smileInputBooleanTrimEnd", "No more leading trim.");
        AppendMasmLine(source, "smileInputBooleanAdvanceStart:", "Advance leading trim.");
        AppendMasmLine(source, "    inc r10d", "Skip one byte.");
        AppendMasmLine(source, "    jmp smileInputBooleanTrimStart", "Continue leading trim.");
        AppendMasmLine(source, "smileInputBooleanTrimEnd:", "Trim only ASCII space and tab at the end.");
        AppendMasmLine(source, "    cmp r11d, r10d", "Is any text left?");
        AppendMasmLine(source, "    jbe smileInputBooleanMalformed", "Whitespace-only input is invalid Boolean text.");
        AppendMasmLine(source, "    mov eax, r11d", "Address the last untrimmed byte.");
        AppendMasmLine(source, "    dec eax", "Convert exclusive end to an index.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + rax]", "Inspect trailing whitespace.");
        AppendMasmLine(source, "    cmp al, ' '", "ASCII space is trim whitespace.");
        AppendMasmLine(source, "    je smileInputBooleanRetreatEnd", "Skip it.");
        AppendMasmLine(source, "    cmp al, 9", "ASCII tab is trim whitespace.");
        AppendMasmLine(source, "    jne smileInputBooleanToken", "The token boundaries are ready.");
        AppendMasmLine(source, "smileInputBooleanRetreatEnd:", "Retreat trailing trim.");
        AppendMasmLine(source, "    dec r11d", "Drop one byte.");
        AppendMasmLine(source, "    jmp smileInputBooleanTrimEnd", "Continue trailing trim.");
        AppendMasmLine(source, "smileInputBooleanToken:", "Compare ordinal-ignore-case ASCII token bytes.");
        AppendMasmLine(source, "    mov eax, r11d", "Compute the trimmed token length.");
        AppendMasmLine(source, "    sub eax, r10d", "EAX is the token byte count.");
        AppendMasmLine(source, "    cmp eax, 4", "TRUE has four bytes.");
        AppendMasmLine(source, "    je smileInputBooleanTrue", "Compare TRUE.");
        AppendMasmLine(source, "    cmp eax, 5", "FALSE has five bytes.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "No other Boolean spelling is valid.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10]", "Compare F case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII uppercase to lowercase.");
        AppendMasmLine(source, "    cmp al, 'f'", "Expected F.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10 + 1]", "Compare A case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 'a'", "Expected A.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10 + 2]", "Compare L case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 'l'", "Expected L.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10 + 3]", "Compare S case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 's'", "Expected S.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10 + 4]", "Compare E case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 'e'", "Expected E.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputBoolean], 0", "Publish FALSE after complete validation.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means conversion success.");
        AppendMasmLine(source, "    pop rsi", "Restore the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    ret", "Return to INPUT.");
        AppendMasmLine(source, "smileInputBooleanTrue:", "Compare TRUE's four bytes.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10]", "Compare T case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 't'", "Expected T.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10 + 1]", "Compare R case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 'r'", "Expected R.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10 + 2]", "Compare U case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 'u'", "Expected U.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    movzx eax, BYTE PTR [rsi + r10 + 3]", "Compare E case-insensitively.");
        AppendMasmLine(source, "    or al, 20h", "Fold ASCII case.");
        AppendMasmLine(source, "    cmp al, 'e'", "Expected E.");
        AppendMasmLine(source, "    jne smileInputBooleanMalformed", "Reject a different byte.");
        AppendMasmLine(source, "    mov BYTE PTR [smileInputBoolean], 1", "Publish TRUE after complete validation.");
        AppendMasmLine(source, "    xor eax, eax", "Zero means conversion success.");
        AppendMasmLine(source, "    pop rsi", "Restore the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    ret", "Return to INPUT.");
        AppendMasmLine(source, "smileInputBooleanMalformed:", "Return invalid Boolean status.");
        AppendMasmLine(source, "    mov eax, 5", "SMILER1505 suffix code.");
        AppendMasmLine(source, "    pop rsi", "Restore the nonvolatile input-buffer base register.");
        AppendMasmLine(source, "    ret", "Return to INPUT.");
        AppendMasmLine(source, "smileParseInputBoolean ENDP", "End strict Boolean parser.");
    }

    private static void AppendMasmFailureProcedure(StringBuilder source)
    {
        AppendMasmLine(source, "smileFail PROC", "Write one exact runtime diagnostic to stderr, then exit 1.");
        AppendMasmLine(source, "    sub rsp, 28h", "Reserve Win64 shadow space and align the stack.");
        AppendMasmLine(source, "    mov rcx, QWORD PTR [stderrHandle]", "WriteFile arg 1: cached stderr handle.");
        AppendMasmLine(source, "    lea r9, bytesWritten", "WriteFile arg 4: receive bytes written.");
        AppendMasmLine(source, "    mov QWORD PTR [rsp + 20h], 0", "WriteFile arg 5: no overlapped I/O.");
        AppendMasmLine(source, "    call WriteFile", "Emit the exact UTF-8 error line.");
        AppendMasmLine(source, "    mov ecx, 1", "ExitProcess arg 1: runtime failure exit code.");
        AppendMasmLine(source, "    call ExitProcess", "Stop immediately; stdout already produced remains intact.");
        AppendMasmLine(source, "    int 3", "ExitProcess does not return.");
        AppendMasmLine(source, "smileFail ENDP", "End fatal runtime reporter.");
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
                BoundPrintStatement print when
                    !print.IsBlankLine &&
                    !facts.Value.IsKnown &&
                    print.Value is not BoundVariableExpression =>
                    print.Value,
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

    private static IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>>
        CreateMasmConditionBuffers(BoundProgramAnalysis analysis)
    {
        var plans = new Dictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            if (statement is BoundIfStatement conditional)
            {
                foreach (BoundConditionalClause clause in conditional.Clauses)
                {
                    Add(
                        clause.Condition,
                        IfConditionPrefix(analysis.GetClauseFacts(clause).Ordinal));
                }
            }
            else if (statement is BoundWhileStatement loop)
            {
                Add(loop.Condition, WhileConditionPrefix(analysis.GetWhileOrdinal(loop)));
            }
        }

        return plans;

        void Add(BoundExpression condition, string conditionPrefix)
        {
            var buffers = new List<RuntimeStringBuffer>();
            CollectMasmConditionBuffers(condition, conditionPrefix, analysis, buffers);
            plans.Add(condition, buffers);
        }
    }

    private static void CollectMasmConditionBuffers(
        BoundExpression expression,
        string conditionPrefix,
        BoundProgramAnalysis analysis,
        List<RuntimeStringBuffer> buffers)
    {
        if (expression is BoundUnaryExpression unary)
        {
            CollectMasmConditionBuffers(unary.Operand, conditionPrefix, analysis, buffers);
            return;
        }

        if (expression is not BoundBinaryExpression binary)
        {
            return;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            CollectMasmConditionBuffers(binary.Left, conditionPrefix, analysis, buffers);
            CollectMasmConditionBuffers(binary.Right, conditionPrefix, analysis, buffers);
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

            string label = $"{conditionPrefix}Runtime{buffers.Count}";
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

                case BoundWhileStatement loop:
                    Collect(loop.Condition);
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
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
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

    private static bool NeedsMasmRuntimeArithmetic(BoundProgramAnalysis analysis)
    {
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            bool emitsArithmetic = statement switch
            {
                BoundLetStatement let when !facts.Value.IsKnown =>
                    TargetRuntimeFacts.ContainsIntegerArithmetic(let.Initializer),
                BoundSetStatement set when !facts.Value.IsKnown =>
                    TargetRuntimeFacts.ContainsIntegerArithmetic(set.Value),
                BoundPrintStatement { IsBlankLine: false } print when !facts.Value.IsKnown =>
                    TargetRuntimeFacts.ContainsIntegerArithmetic(print.Value),
                BoundIfStatement conditional => conditional.Clauses.Any(clause =>
                    TargetRuntimeFacts.ContainsIntegerArithmetic(clause.Condition)),
                BoundWhileStatement loop =>
                    TargetRuntimeFacts.ContainsIntegerArithmetic(loop.Condition),
                _ => false
            };

            if (emitsArithmetic)
            {
                return true;
            }
        }

        return false;
    }

    private static bool NeedsMasmBooleanText(
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<BoundStatement, RuntimeStringBuffer> statementBuffers,
        IReadOnlyDictionary<BoundExpression, IReadOnlyList<RuntimeStringBuffer>> conditionBuffers,
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

    private static string InputValueLabel(int statementIndex) => $"input{statementIndex}Value";

    private static string InputErrorLabel(int statementIndex, int code) =>
        $"input{statementIndex}Error{code}Message";

    private static string PrintLiteralLabel(int printIndex, int segmentIndex) =>
        $"print{printIndex}Segment{segmentIndex}";

    private static string IfClauseLabel(int ifOrdinal, int clauseIndex) =>
        $"if{ifOrdinal}Clause{clauseIndex}";

    private static string IfElseLabel(int ifOrdinal) => $"if{ifOrdinal}Else";

    private static string IfEndLabel(int ifOrdinal) => $"if{ifOrdinal}End";

    private static string IfConditionPrefix(int clauseOrdinal) =>
        $"ifCondition{clauseOrdinal}";

    private static string WhileConditionPrefix(int whileOrdinal) =>
        $"while{whileOrdinal}ConditionValue";

    private static string WhileConditionLabel(int whileOrdinal) =>
        $"while{whileOrdinal}Condition";

    private static string WhileBodyLabel(int whileOrdinal) =>
        $"while{whileOrdinal}Body";

    private static string WhileEndLabel(int whileOrdinal) =>
        $"while{whileOrdinal}End";

    private static string MasmConditionOperandLabel(
        string conditionPrefix,
        int comparisonIndex,
        string side) =>
        $"{conditionPrefix}Comparison{comparisonIndex}{side}";

    private static string MasmConditionPartLabel(
        string conditionPrefix,
        int partIndex,
        string purpose) =>
        $"{conditionPrefix}Part{partIndex}{purpose}";

}
