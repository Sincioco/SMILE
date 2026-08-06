using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class CobolCodeGenerator : ICodeGenerator
{
    private const string RuntimePointerName = "SMILE-RUNTIME-POINTER";
    private const string RuntimeIntegerName = "SMILE-RUNTIME-INTEGER";
    private const string RuntimeIntegerTextName = "SMILE-RUNTIME-INTEGER-TEXT";
    private const string RuntimeConditionName = "SMILE-RUNTIME-CONDITION";
    private const string RuntimeStatusName = "SMILE-RUNTIME-STATUS";

    private sealed record RuntimeStringBuffer(
        BoundExpression Expression,
        string ValueName,
        string LengthName,
        int Capacity);

    private sealed record CobolRuntimePlan(
        IReadOnlyDictionary<VariableSymbol, string> InputFunctions,
        bool NeedsCheckedIntegerArithmetic,
        int IntegerScratchCount)
    {
        public bool HasInput => InputFunctions.Count > 0;

        public string IntegerScratch(int index) =>
            index == 0 ? RuntimeIntegerName : $"{RuntimeIntegerName}-{index + 1}";
    }

    public TargetLanguage Language => TargetLanguage.Cobol;

    public GeneratedProgram Generate(BoundProgram program)
    {
        TargetIdentifierMap identifiers = TargetIdentifierMap.Create(program, Language);
        BoundProgramAnalysis analysis = BoundProgramAnalysis.Create(program);
        var source = new StringBuilder();
        BoundLetStatement[] lets = program.Statements.OfType<BoundLetStatement>().ToArray();
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths =
            CreateLogicalLengthNames(program, identifiers, analysis);
        IReadOnlyDictionary<VariableSymbol, int> storageLengths =
            CreateStorageLengths(program, analysis);
        BoundConditionalClause[] clauses = analysis.EnumerateStatements()
            .OfType<BoundIfStatement>()
            .SelectMany(conditional => conditional.Clauses)
            .ToArray();
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers =
            CreateRuntimeStringBuffers(program, analysis);
        CobolRuntimePlan runtime = CreateRuntimePlan(program);
        bool needsRuntimeFacilities =
            NeedsRuntimeFacilities(analysis, runtimeStringBuffers) ||
            runtime.NeedsCheckedIntegerArithmetic;

        source.AppendLine(">>SOURCE FORMAT IS FREE");
        source.AppendLine("IDENTIFICATION DIVISION.");
        source.AppendLine("PROGRAM-ID. Program.");

        if (lets.Length > 0 || clauses.Length > 0)
        {
            source.AppendLine();
            source.AppendLine("DATA DIVISION.");
            source.AppendLine("WORKING-STORAGE SECTION.");
            source.AppendLine("*> SMILE LET values are stored before PROCEDURE DIVISION.");

            foreach (BoundLetStatement let in lets)
            {
                BoundStatementAnalysis facts = analysis.GetStatementFacts(let);
                AppendCobolLet(
                    source,
                    let,
                    facts.Value,
                    identifiers,
                    storageLengths,
                    logicalLengths);
            }

            foreach (BoundConditionalClause clause in clauses)
            {
                source.Append("01 ")
                    .Append(ConditionName(analysis.GetClauseFacts(clause).Ordinal))
                    .AppendLine(" PIC 9 COMP-5 VALUE 0.");
            }

            foreach (RuntimeStringBuffer buffer in runtimeStringBuffers.Values)
            {
                string picture = buffer.Capacity == 1
                    ? "PIC X"
                    : $"PIC X({buffer.Capacity})";
                source.Append("01 ").Append(buffer.ValueName).Append(' ')
                    .Append(picture).AppendLine(" VALUE SPACES.");
                source.Append("01 ").Append(buffer.LengthName)
                    .AppendLine(" PIC 9(9) COMP-5 VALUE 0.");
            }

            if (needsRuntimeFacilities)
            {
                source.Append("01 ").Append(RuntimePointerName)
                    .AppendLine(" PIC 9(9) COMP-5 VALUE 1.");
                int integerScratchCount = runtime.NeedsCheckedIntegerArithmetic
                    ? Math.Max(1, runtime.IntegerScratchCount)
                    : 1;
                for (int index = 0; index < integerScratchCount; index++)
                {
                    // S9(18) COMP-5 is GnuCOBOL's native eight-byte signed
                    // binary field and therefore preserves the complete Int64
                    // range even though its decimal PICTURE has 18 positions.
                    source.Append("01 ").Append(runtime.IntegerScratch(index))
                        .AppendLine(" PIC S9(18) COMP-5 VALUE 0.");
                }

                source.Append("01 ").Append(RuntimeIntegerTextName)
                    .AppendLine(" PIC -(19)9 VALUE ZERO.");
                source.Append("01 ").Append(RuntimeConditionName)
                    .AppendLine(" PIC 9 COMP-5 VALUE 0.");
            }

            if (runtime.HasInput || runtime.NeedsCheckedIntegerArithmetic)
            {
                source.Append("01 ").Append(RuntimeStatusName)
                    .AppendLine(" PIC S9(9) COMP-5 VALUE 0.");
            }
        }

        source.AppendLine();
        source.AppendLine("PROCEDURE DIVISION.");
        source.AppendLine("*> SMILE PRINT reads current storage when it directly names a variable.");
        AppendSourceItems(
            source,
            program.SourceItems,
            "    ",
            analysis,
            identifiers,
            logicalLengths,
            storageLengths,
            runtimeStringBuffers,
            runtime,
            insideConditional: false);

        source.AppendLine("    STOP RUN.");

        var files = new List<GeneratedFile>
        {
            new(
                "Program.cob",
                TextOutput.EnsureOneTrailingNewLine(source.ToString()),
                IsPrimary: true)
        };
        if (runtime.HasInput)
        {
            files.Add(new GeneratedFile(
                "SmileRuntime.c",
                TextOutput.EnsureOneTrailingNewLine(
                    GenerateCobolRuntimeCompanion(program, runtime, storageLengths)),
                IsPrimary: false));
        }

        return new GeneratedProgram(Language, files);
    }

    private static void AppendCobolLet(
        StringBuilder source,
        BoundLetStatement let,
        AnalyzedValue analyzedValue,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths)
    {
        string name = identifiers.Get(let.Variable);
        string text = analyzedValue.IsKnown
            ? analyzedValue.Value.ToDisplayText()
            : string.Empty;
        int storageLength = Math.Max(1, storageLengths[let.Variable]);
        string picture = storageLength == 1 ? "PIC X" : $"PIC X({storageLength})";
        string storageValue = text.Length == 0
            ? storageLength == 1 ? "SPACE" : "SPACES"
            : TargetEscapes.CobolString(text);
        source.AppendLine($"01 {name} {picture} VALUE {storageValue}.");

        if (logicalLengths.TryGetValue(let.Variable, out string? lengthName))
        {
            source.AppendLine(
                $"01 {lengthName} PIC 9(9) COMP-5 VALUE {TargetEscapes.CobolByteLength(text)}.");
        }
    }

    private static void AppendCobolSet(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        bool valueIsKnown,
        BoundSetStatement set,
        SmileValue knownValue,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        CobolRuntimePlan runtime)
    {
        string terminator = terminateSentence ? "." : string.Empty;
        if (!valueIsKnown &&
            set.Value is BoundVariableExpression directSource &&
            !ReferenceEquals(set.Variable, directSource.Variable))
        {
            string sourceName = identifiers.Get(directSource.Variable);
            source.AppendLine(
                $"{indent}MOVE {sourceName} TO {identifiers.Get(set.Variable)}{terminator}");
            string sourceLength = logicalLengths[directSource.Variable];
            source.AppendLine(
                $"{indent}MOVE {sourceLength} TO {logicalLengths[set.Variable]}{terminator}");
            return;
        }

        if (!valueIsKnown)
        {
            AppendCobolRuntimeAssignment(
                source,
                indent,
                terminator,
                set.Variable,
                set.Value,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers,
                runtime);
            return;
        }

        string text = knownValue.ToDisplayText();
        string storageValue = text.Length == 0 ? "SPACES" : TargetEscapes.CobolString(text);
        source.AppendLine($"{indent}MOVE {storageValue} TO {identifiers.Get(set.Variable)}{terminator}");
        source.AppendLine(
            $"{indent}MOVE {TargetEscapes.CobolByteLength(text)} TO {logicalLengths[set.Variable]}{terminator}");
    }

    private static void AppendCobolRuntimeAssignment(
        StringBuilder source,
        string indent,
        string terminator,
        VariableSymbol destination,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        CobolRuntimePlan runtime)
    {
        switch (destination.Type)
        {
            case SmileType.String:
                RuntimeStringBuffer stringBuffer = runtimeStringBuffers[expression];
                AppendCobolRuntimeStringMaterialization(
                    source,
                    indent,
                    terminator,
                    stringBuffer.ValueName,
                    stringBuffer.LengthName,
                    expression,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers,
                    runtime,
                    RuntimeConditionName);
                source.Append(indent).Append("MOVE ").Append(stringBuffer.ValueName)
                    .Append(" TO ").Append(identifiers.Get(destination)).AppendLine(terminator);
                source.Append(indent).Append("MOVE ").Append(stringBuffer.LengthName)
                    .Append(" TO ").Append(logicalLengths[destination]).AppendLine(terminator);
                return;

            case SmileType.Integer when TryRenderCobolIntegerExpression(
                expression,
                identifiers,
                out string integer,
                out _):
                if (runtime.NeedsCheckedIntegerArithmetic &&
                    TargetRuntimeFacts.ContainsIntegerArithmetic(expression))
                {
                    AppendCobolIntegerEvaluation(
                        source,
                        indent,
                        expression,
                        scratchIndex: 0,
                        identifiers,
                        runtime);
                }
                else
                {
                    source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                        .Append(" = ").Append(integer).AppendLine(terminator);
                }

                source.Append(indent).Append("MOVE ").Append(RuntimeIntegerName)
                    .Append(" TO ").Append(RuntimeIntegerTextName).AppendLine(terminator);
                source.Append(indent).Append("MOVE FUNCTION TRIM(")
                    .Append(RuntimeIntegerTextName).Append(") TO ")
                    .Append(identifiers.Get(destination)).AppendLine(terminator);
                source.Append(indent).Append("MOVE FUNCTION LENGTH(FUNCTION TRIM(")
                    .Append(RuntimeIntegerTextName).Append(")) TO ")
                    .Append(logicalLengths[destination]).AppendLine(terminator);
                return;

            case SmileType.Boolean:
                AppendCobolConditionEvaluation(
                    source,
                    indent,
                    RuntimeConditionName,
                    expression,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers,
                    runtime);
                source.Append(indent).Append("IF ").Append(RuntimeConditionName).AppendLine(" = 1");
                AppendCobolFixedAssignment(
                    source,
                    indent + "    ",
                    destination,
                    "TRUE",
                    identifiers,
                    logicalLengths);
                source.Append(indent).AppendLine("ELSE");
                AppendCobolFixedAssignment(
                    source,
                    indent + "    ",
                    destination,
                    "FALSE",
                    identifiers,
                    logicalLengths);
                source.Append(indent).Append("END-IF").AppendLine(terminator);
                return;
        }

        throw new InvalidOperationException(
            $"COBOL cannot lower runtime {destination.Type} assignment expression.");
    }

    private static void AppendCobolFixedAssignment(
        StringBuilder source,
        string indent,
        VariableSymbol destination,
        string text,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths)
    {
        source.Append(indent).Append("MOVE ").Append(TargetEscapes.CobolString(text))
            .Append(" TO ").AppendLine(identifiers.Get(destination));
        source.Append(indent).Append("MOVE ").Append(Encoding.UTF8.GetByteCount(text))
            .Append(" TO ").AppendLine(logicalLengths[destination]);
    }

    private static void AppendCobolRuntimeStringMaterialization(
        StringBuilder source,
        string indent,
        string terminator,
        string destinationName,
        string destinationLength,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        CobolRuntimePlan runtime,
        string conditionName)
    {
        source.Append(indent).Append("MOVE SPACES TO ").Append(destinationName)
            .AppendLine(terminator);
        source.Append(indent).Append("MOVE 1 TO ").Append(RuntimePointerName)
            .AppendLine(terminator);

        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal when literal.Text.Length > 0:
                    AppendCobolStringInto(
                        source,
                        indent,
                        TargetEscapes.CobolString(literal.Text),
                        destinationName);
                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }
                    when variable.Variable.Type is SmileType.String:
                    string variableName = identifiers.Get(variable.Variable);
                    string variableLength = logicalLengths[variable.Variable];
                    source.Append(indent).Append("IF ").Append(variableLength).AppendLine(" > 0");
                    string variableSlice = storageLengths[variable.Variable] == 1
                        ? variableName
                        : $"{variableName}(1:{variableLength})";
                    AppendCobolStringInto(
                        source,
                        indent + "    ",
                        variableSlice,
                        destinationName);
                    source.Append(indent).AppendLine("END-IF");
                    break;

                case RuntimeExpressionTextSegment typed when
                    typed.Expression.Type is SmileType.Integer &&
                    TryRenderCobolIntegerExpression(
                        typed.Expression,
                        identifiers,
                        out string integer,
                        out _):
                    if (runtime.NeedsCheckedIntegerArithmetic &&
                        TargetRuntimeFacts.ContainsIntegerArithmetic(typed.Expression))
                    {
                        AppendCobolIntegerEvaluation(
                            source,
                            indent,
                            typed.Expression,
                            scratchIndex: 0,
                            identifiers,
                            runtime);
                    }
                    else
                    {
                        source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                            .Append(" = ").AppendLine(integer);
                    }

                    source.Append(indent).Append("MOVE ").Append(RuntimeIntegerName)
                        .Append(" TO ").AppendLine(RuntimeIntegerTextName);
                    AppendCobolStringInto(
                        source,
                        indent,
                        $"FUNCTION TRIM({RuntimeIntegerTextName})",
                        destinationName);
                    break;

                case RuntimeExpressionTextSegment typed when typed.Expression.Type is SmileType.Boolean:
                    // A nested Boolean comparison can materialize its own
                    // String operands and therefore reuse the shared STRING
                    // pointer. This buffer's length field is not final until
                    // the end, so it safely preserves the outer cursor here.
                    source.Append(indent).Append("MOVE ").Append(RuntimePointerName)
                        .Append(" TO ").AppendLine(destinationLength);
                    AppendCobolConditionEvaluation(
                        source,
                        indent,
                        conditionName,
                        typed.Expression,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers,
                        runtime);
                    source.Append(indent).Append("MOVE ").Append(destinationLength)
                        .Append(" TO ").AppendLine(RuntimePointerName);
                    source.Append(indent).Append("IF ").Append(conditionName).AppendLine(" = 1");
                    AppendCobolStringInto(source, indent + "    ", "\"TRUE\"", destinationName);
                    source.Append(indent).AppendLine("ELSE");
                    AppendCobolStringInto(source, indent + "    ", "\"FALSE\"", destinationName);
                    source.Append(indent).AppendLine("END-IF");
                    break;
            }
        }

        source.Append(indent).Append("COMPUTE ").Append(destinationLength)
            .Append(" = ").Append(RuntimePointerName).Append(" - 1")
            .AppendLine(terminator);
    }

    private static void AppendCobolStringInto(
        StringBuilder source,
        string indent,
        string value,
        string destinationName)
    {
        source.Append(indent).Append("STRING ").Append(value)
            .Append(" DELIMITED BY SIZE INTO ").Append(destinationName)
            .Append(" WITH POINTER ").Append(RuntimePointerName)
            .AppendLine(" END-STRING");
    }

    private static void AppendCobolPrint(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        bool valueIsKnown,
        BoundPrintStatement print,
        SmileValue knownValue,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        CobolRuntimePlan runtime)
    {
        if (!print.IsBlankLine && print.Value is BoundVariableExpression directVariable)
        {
            AppendCobolDirectVariablePrint(
                source,
                indent,
                terminateSentence,
                directVariable.Variable,
                identifiers,
                logicalLengths,
                storageLengths);
            return;
        }

        if (!print.IsBlankLine && !valueIsKnown)
        {
            AppendCobolRuntimePrint(
                source,
                indent,
                terminateSentence,
                print.Value,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers,
                runtime);
            return;
        }

        string text = print.IsBlankLine
            ? string.Empty
            : knownValue.ToDisplayText();
        if (text.Length == 0)
        {
            // DISPLAY "" emits one space in GnuCOBOL. A no-advancing line-feed
            // emits exactly the blank line SMILE PRINT requires.
            source.Append(indent).Append("DISPLAY X\"0A\" WITH NO ADVANCING")
                .AppendLine(terminateSentence ? "." : string.Empty);
            return;
        }

        source.Append(indent).Append("DISPLAY ");
        source.Append(TargetEscapes.CobolString(text));
        source.AppendLine(terminateSentence ? "." : string.Empty);
    }

    private static void AppendCobolRuntimePrint(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        CobolRuntimePlan runtime)
    {
        RuntimeStringBuffer buffer = runtimeStringBuffers[expression];
        // SMILE evaluates the complete PRINT value before writing any bytes.
        // Materializing first prevents a later interpolation overflow or
        // division failure from leaking an earlier literal/segment to stdout.
        AppendCobolRuntimeStringMaterialization(
            source,
            indent,
            terminator: string.Empty,
            buffer.ValueName,
            buffer.LengthName,
            expression,
            identifiers,
            logicalLengths,
            storageLengths,
            runtimeStringBuffers,
            runtime,
            RuntimeConditionName);
        source.Append(indent).Append("IF ").Append(buffer.LengthName).AppendLine(" > 0");
        source.Append(indent).Append("    DISPLAY ").Append(buffer.ValueName);
        if (buffer.Capacity > 1)
        {
            source.Append("(1:").Append(buffer.LengthName).Append(')');
        }

        source.AppendLine(" WITH NO ADVANCING");
        source.Append(indent).AppendLine("END-IF");

        source.Append(indent).Append("DISPLAY X\"0A\" WITH NO ADVANCING")
            .AppendLine(terminateSentence ? "." : string.Empty);
    }

    private static void AppendCobolInput(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        BoundInputStatement input,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        CobolRuntimePlan runtime)
    {
        string terminator = terminateSentence ? "." : string.Empty;
        string variableName = identifiers.Get(input.Variable);
        string logicalLength = logicalLengths[input.Variable];
        source.Append(indent).Append("CALL ")
            .Append(TargetEscapes.CobolString(runtime.InputFunctions[input.Variable]))
            .AppendLine(" USING");
        source.Append(indent).Append("    BY REFERENCE ").AppendLine(variableName);
        source.Append(indent).Append("    BY REFERENCE ").AppendLine(logicalLength);
        source.Append(indent).Append("    RETURNING ").AppendLine(RuntimeStatusName);
        source.Append(indent).Append("IF ").Append(RuntimeStatusName).AppendLine(" NOT = 0");
        source.Append(indent).Append("    EVALUATE ").AppendLine(RuntimeStatusName);
        AppendCobolInputError(
            source,
            indent + "        ",
            1,
            $"SMILE Runtime Error SMILER1501: Input ended before a value was received for '{input.Variable.Name}'.");
        AppendCobolInputError(
            source,
            indent + "        ",
            2,
            $"SMILE Runtime Error SMILER1502: Input for '{input.Variable.Name}' exceeds the {SmileLanguage.MaximumInputLineUtf8Bytes}-byte UTF-8 limit.");
        AppendCobolInputError(
            source,
            indent + "        ",
            3,
            $"SMILE Runtime Error SMILER1503: Input for '{input.Variable.Name}' is not a valid Integer.");
        AppendCobolInputError(
            source,
            indent + "        ",
            4,
            $"SMILE Runtime Error SMILER1504: Input for '{input.Variable.Name}' is outside the signed 64-bit Integer range.");
        AppendCobolInputError(
            source,
            indent + "        ",
            5,
            $"SMILE Runtime Error SMILER1505: Input for '{input.Variable.Name}' must be TRUE or FALSE.");
        source.Append(indent).AppendLine("        WHEN OTHER");
        source.Append(indent).Append("            DISPLAY ")
            .Append(TargetEscapes.CobolString(
                $"SMILE Runtime Error SMILER1506: Input for '{input.Variable.Name}' could not be read as valid UTF-8 text."))
            .AppendLine(" UPON STDERR");
        source.Append(indent).AppendLine("    END-EVALUATE");
        source.Append(indent).AppendLine("    MOVE 1 TO RETURN-CODE");
        source.Append(indent).AppendLine("    GOBACK");
        source.Append(indent).Append("END-IF").AppendLine(terminator);
    }

    private static void AppendCobolInputError(
        StringBuilder source,
        string indent,
        int status,
        string message)
    {
        source.Append(indent).Append("WHEN ").AppendLine(status.ToString(CultureInfo.InvariantCulture));
        source.Append(indent).Append("    DISPLAY ").Append(TargetEscapes.CobolString(message))
            .AppendLine(" UPON STDERR");
    }

    private static void AppendSourceItems(
        StringBuilder source,
        IReadOnlyList<BoundSourceItem> sourceItems,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        CobolRuntimePlan runtime,
        bool insideConditional)
    {
        foreach (BoundSourceItem sourceItem in sourceItems)
        {
            if (sourceItem is BoundFullLineComment comment)
            {
                // COBOL declarations live in WORKING-STORAGE, but learner
                // layout belongs exactly once in the source-order PROCEDURE
                // stream nearest the executable form of the program.
                TargetComments.Append(source, TargetLanguage.Cobol, indent, comment.Payload);
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
                case BoundLetStatement let when !facts.Value.IsKnown:
                    AppendCobolRuntimeAssignment(
                        source,
                        indent,
                        insideConditional ? string.Empty : ".",
                        let.Variable,
                        let.Initializer,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers,
                        runtime);
                    break;

                case BoundSetStatement set:
                    AppendCobolSet(
                        source,
                        indent,
                        terminateSentence: !insideConditional,
                        valueIsKnown: facts.Value.IsKnown,
                        set,
                        facts.Value.Value,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers,
                        runtime);
                    break;

                case BoundInputStatement input:
                    AppendCobolInput(
                        source,
                        indent,
                        terminateSentence: !insideConditional,
                        input,
                        identifiers,
                        logicalLengths,
                        runtime);
                    break;

                case BoundPrintStatement print:
                    AppendCobolPrint(
                        source,
                        indent,
                        terminateSentence: !insideConditional,
                        valueIsKnown: facts.Value.IsKnown,
                        print,
                        facts.Value.Value,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers,
                        runtime);
                    break;

                case BoundIfStatement conditional:
                    AppendCobolIf(
                        source,
                        conditional,
                        indent,
                        analysis,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers,
                        runtime,
                        terminateSentence: !insideConditional);
                    break;
            }
        }
    }

    private static void AppendCobolIf(
        StringBuilder source,
        BoundIfStatement conditional,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        CobolRuntimePlan runtime,
        bool terminateSentence)
    {
        for (int clauseIndex = 0; clauseIndex < conditional.Clauses.Count; clauseIndex++)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            BoundConditionalClauseAnalysis clauseFacts = analysis.GetClauseFacts(clause);
            string conditionName = ConditionName(clauseFacts.Ordinal);

            if (clauseIndex > 0)
            {
                source.Append(indent).AppendLine("ELSE");
                indent += "    ";
            }

            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                clause.Condition,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers,
                runtime);
            source.Append(indent).Append("IF ").Append(conditionName).AppendLine(" = 1");
            AppendSourceItems(
                source,
                clause.SourceItems,
                indent + "    ",
                analysis,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers,
                runtime,
                insideConditional: true);
            if (clause.Statements.Count == 0)
            {
                source.Append(indent).AppendLine("    CONTINUE");
            }
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("ELSE");
            AppendSourceItems(
                source,
                conditional.ElseSourceItems,
                indent + "    ",
                analysis,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeStringBuffers,
                runtime,
                insideConditional: true);
            if (conditional.ElseStatements.Count == 0)
            {
                source.Append(indent).AppendLine("    CONTINUE");
            }
        }

        for (int clauseIndex = conditional.Clauses.Count - 1; clauseIndex >= 0; clauseIndex--)
        {
            bool closesCompleteStatement = clauseIndex == 0 && terminateSentence;
            source.Append(indent).Append("END-IF")
                .AppendLine(closesCompleteStatement ? "." : string.Empty);
            if (clauseIndex > 0)
            {
                indent = indent[..^4];
            }
        }
    }

    private static void AppendCobolDirectVariablePrint(
        StringBuilder source,
        string indent,
        bool terminateSentence,
        VariableSymbol variable,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths)
    {
        string terminator = terminateSentence ? "." : string.Empty;
        string name = identifiers.Get(variable);
        string lengthName = logicalLengths[variable];
        // Preserve the established exact empty-String path. Only the final
        // END-IF receives a period at top level; inside a SMILE IF, even
        // that period is suppressed so it cannot close the outer scope.
        source.Append(indent).Append("IF ").Append(lengthName).AppendLine(" = 0");
        source.Append(indent).AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING");
        source.Append(indent).AppendLine("ELSE");
        source.Append(indent).Append("    DISPLAY ").Append(name);
        if (storageLengths[variable] > 1)
        {
            source.Append("(1:").Append(lengthName).Append(')');
        }

        source.AppendLine(" WITH NO ADVANCING");
        source.Append(indent).AppendLine("    DISPLAY X\"0A\" WITH NO ADVANCING");
        source.Append(indent).Append("END-IF").AppendLine(terminator);
    }

    private static bool ContainsLiveVariable(BoundExpression expression) =>
        CanEmitLiveSegments(expression) && expression switch
        {
            BoundVariableExpression => true,
            BoundBinaryExpression binary =>
                ContainsLiveVariable(binary.Left) || ContainsLiveVariable(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole && ContainsLiveVariable(hole.Expression)),
            _ => false
        };

    private static bool CanEmitLiveSegments(BoundExpression expression) =>
        expression switch
        {
            BoundStringLiteralExpression => true,
            BoundVariableExpression => true,
            BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary =>
                CanEmitLiveSegments(binary.Left) && CanEmitLiveSegments(binary.Right),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.All(part => part switch
            {
                BoundInterpolatedTextPart => true,
                BoundInterpolationExpressionPart hole => CanEmitLiveSegments(hole.Expression),
                _ => false
            }),
            _ => false
        };

    private readonly record struct CobolStringConditionOperand(
        string Value,
        string Length,
        bool ReadsStorage);

    private static void AppendCobolConditionEvaluation(
        StringBuilder source,
        string indent,
        string conditionName,
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        CobolRuntimePlan runtime)
    {
        if (expression is BoundUnaryExpression
            {
                Operator.Kind: BoundUnaryOperatorKind.LogicalNegation
            } unary)
        {
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                unary.Operand,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers,
                runtime);
            source.Append(indent).Append("IF ").Append(conditionName).AppendLine(" = 1");
            source.Append(indent).Append("    MOVE 0 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("ELSE");
            source.Append(indent).Append("    MOVE 1 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("END-IF");
            return;
        }

        if (expression is BoundBinaryExpression booleanComparison &&
            booleanComparison.Left.Type is SmileType.Boolean &&
            booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality)
        {
            string leftScratch = runtimeBuffers[expression].LengthName;
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                booleanComparison.Left,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers,
                runtime);
            source.Append(indent).Append("MOVE ").Append(conditionName)
                .Append(" TO ").AppendLine(leftScratch);
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                booleanComparison.Right,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers,
                runtime);
            string comparisonOperator = booleanComparison.Operator.Kind is
                BoundBinaryOperatorKind.Equality ? " = " : " NOT = ";
            source.Append(indent).Append("IF ").Append(leftScratch)
                .Append(comparisonOperator).AppendLine(conditionName);
            source.Append(indent).Append("    MOVE 1 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("ELSE");
            source.Append(indent).Append("    MOVE 0 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("END-IF");
            return;
        }

        if (expression is BoundBinaryExpression logical &&
            logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                BoundBinaryOperatorKind.LogicalOr)
        {
            AppendCobolConditionEvaluation(
                source,
                indent,
                conditionName,
                logical.Left,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers,
                runtime);
            string test = logical.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                ? " = 1"
                : " = 0";
            source.Append(indent).Append("IF ").Append(conditionName).AppendLine(test);
            AppendCobolConditionEvaluation(
                source,
                indent + "    ",
                conditionName,
                logical.Right,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers,
                runtime);
            source.Append(indent).AppendLine("END-IF");
            return;
        }

        if (runtime.NeedsCheckedIntegerArithmetic &&
            expression is BoundBinaryExpression integerComparison &&
            integerComparison.Left.Type is SmileType.Integer &&
            integerComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                BoundBinaryOperatorKind.Inequality or
                BoundBinaryOperatorKind.Less or
                BoundBinaryOperatorKind.LessOrEquals or
                BoundBinaryOperatorKind.Greater or
                BoundBinaryOperatorKind.GreaterOrEquals &&
            (TargetRuntimeFacts.ContainsIntegerArithmetic(integerComparison.Left) ||
             TargetRuntimeFacts.ContainsIntegerArithmetic(integerComparison.Right)))
        {
            AppendCobolIntegerEvaluation(
                source,
                indent,
                integerComparison.Left,
                scratchIndex: 0,
                identifiers,
                runtime);
            AppendCobolIntegerEvaluation(
                source,
                indent,
                integerComparison.Right,
                scratchIndex: 1,
                identifiers,
                runtime);
            string comparisonOperator = integerComparison.Operator.Kind switch
            {
                BoundBinaryOperatorKind.Equality => "=",
                BoundBinaryOperatorKind.Inequality => "NOT =",
                BoundBinaryOperatorKind.Less => "<",
                BoundBinaryOperatorKind.LessOrEquals => "<=",
                BoundBinaryOperatorKind.Greater => ">",
                BoundBinaryOperatorKind.GreaterOrEquals => ">=",
                _ => throw new InvalidOperationException("Unsupported COBOL Integer comparison.")
            };
            source.Append(indent).Append("IF ").Append(runtime.IntegerScratch(0))
                .Append(' ').Append(comparisonOperator).Append(' ')
                .AppendLine(runtime.IntegerScratch(1));
            source.Append(indent).Append("    MOVE 1 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("ELSE");
            source.Append(indent).Append("    MOVE 0 TO ").AppendLine(conditionName);
            source.Append(indent).AppendLine("END-IF");
            return;
        }

        foreach (RuntimeStringBuffer buffer in runtimeBuffers.Values.Where(buffer =>
                     buffer.Expression.Type is SmileType.String &&
                     ContainsExpression(expression, buffer.Expression)))
        {
            AppendCobolRuntimeStringMaterialization(
                source,
                indent,
                string.Empty,
                buffer.ValueName,
                buffer.LengthName,
                buffer.Expression,
                identifiers,
                logicalLengths,
                storageLengths,
                runtimeBuffers,
                runtime,
                conditionName);
        }

        int runtimeBufferIndex = 0;
        if (!TryRenderCobolCondition(
                expression,
                identifiers,
                logicalLengths,
                runtimeBuffers,
                ref runtimeBufferIndex,
                out string rendered))
        {
            throw new InvalidOperationException(
                "COBOL could not render a planned runtime condition.");
        }

        source.Append(indent).Append("IF ").AppendLine(rendered);
        source.Append(indent).Append("    MOVE 1 TO ").AppendLine(conditionName);
        source.Append(indent).AppendLine("ELSE");
        source.Append(indent).Append("    MOVE 0 TO ").AppendLine(conditionName);
        source.Append(indent).AppendLine("END-IF");
    }

    private static bool ContainsExpression(
        BoundExpression root,
        BoundExpression candidate)
    {
        if (ReferenceEquals(root, candidate))
        {
            return true;
        }

        return root switch
        {
            BoundUnaryExpression unary => ContainsExpression(unary.Operand, candidate),
            BoundBinaryExpression binary =>
                ContainsExpression(binary.Left, candidate) ||
                ContainsExpression(binary.Right, candidate),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts.Any(part =>
                part is BoundInterpolationExpressionPart hole &&
                ContainsExpression(hole.Expression, candidate)),
            _ => false
        };
    }

    private static bool TryRenderCobolCondition(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        ref int runtimeBufferIndex,
        out string rendered)
    {
        switch (expression)
        {
            case BoundBooleanLiteralExpression literal:
                rendered = literal.Value ? "1 = 1" : "1 = 0";
                return true;

            case BoundVariableExpression { Variable.Type: SmileType.Boolean } variable:
                rendered = identifiers.Get(variable.Variable) + " = \"TRUE\"";
                return true;

            case BoundUnaryExpression { Operator.Kind: BoundUnaryOperatorKind.LogicalNegation } unary
                when TryRenderCobolCondition(
                    unary.Operand,
                    identifiers,
                    logicalLengths,
                    runtimeBuffers,
                    ref runtimeBufferIndex,
                    out string operand):
                rendered = $"NOT ({operand})";
                return true;

            case BoundBinaryExpression binary when
                binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or
                    BoundBinaryOperatorKind.LogicalOr:
                if (TryRenderCobolCondition(
                        binary.Left,
                        identifiers,
                        logicalLengths,
                        runtimeBuffers,
                        ref runtimeBufferIndex,
                        out string left) &&
                    TryRenderCobolCondition(
                        binary.Right,
                        identifiers,
                        logicalLengths,
                        runtimeBuffers,
                        ref runtimeBufferIndex,
                        out string right))
                {
                    string logicalOperator = binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                        ? "AND"
                        : "OR";
                    rendered = $"({left}) {logicalOperator} ({right})";
                    return true;
                }

                break;

            case BoundBinaryExpression binary when TryRenderCobolDirectComparison(
                binary,
                identifiers,
                logicalLengths,
                runtimeBuffers,
                ref runtimeBufferIndex,
                out rendered):
                return true;
        }

        rendered = string.Empty;
        return false;
    }

    private static bool TryRenderCobolDirectComparison(
        BoundBinaryExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        ref int runtimeBufferIndex,
        out string rendered)
    {
        bool isEquality = expression.Operator.Kind is BoundBinaryOperatorKind.Equality;
        bool isInequality = expression.Operator.Kind is BoundBinaryOperatorKind.Inequality;
        if (expression.Left.Type is SmileType.String)
        {
            if ((!isEquality && !isInequality) ||
                !TryGetCobolStringConditionOperand(
                    expression.Left,
                    identifiers,
                    logicalLengths,
                    runtimeBuffers,
                    ref runtimeBufferIndex,
                    out CobolStringConditionOperand left) ||
                !TryGetCobolStringConditionOperand(
                    expression.Right,
                    identifiers,
                    logicalLengths,
                    runtimeBuffers,
                    ref runtimeBufferIndex,
                    out CobolStringConditionOperand right))
            {
                rendered = string.Empty;
                return false;
            }

            string equality;
            if (!left.ReadsStorage && left.Length == "0")
            {
                equality = $"{right.Length} = 0";
            }
            else if (!right.ReadsStorage && right.Length == "0")
            {
                equality = $"{left.Length} = 0";
            }
            else
            {
                equality = $"({left.Length} = {right.Length} AND {left.Value} = {right.Value})";
            }

            rendered = isEquality ? equality : $"NOT ({equality})";
            return true;
        }

        if (!TryGetCobolScalarConditionOperand(expression.Left, identifiers, out string scalarLeft, out _) ||
            !TryGetCobolScalarConditionOperand(expression.Right, identifiers, out string scalarRight, out _))
        {
            rendered = string.Empty;
            return false;
        }

        string comparisonOperator = expression.Operator.Kind switch
        {
            BoundBinaryOperatorKind.Equality => "=",
            BoundBinaryOperatorKind.Inequality => "NOT =",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            _ => string.Empty
        };
        if (comparisonOperator.Length == 0)
        {
            rendered = string.Empty;
            return false;
        }

        rendered = $"{scalarLeft} {comparisonOperator} {scalarRight}";
        return true;
    }

    private static bool TryGetCobolStringConditionOperand(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers,
        ref int runtimeBufferIndex,
        out CobolStringConditionOperand operand)
    {
        switch (expression)
        {
            case BoundVariableExpression variable when
                logicalLengths.TryGetValue(variable.Variable, out string? lengthName):
                operand = new CobolStringConditionOperand(
                    identifiers.Get(variable.Variable),
                    lengthName,
                    ReadsStorage: true);
                return true;

            case BoundStringLiteralExpression literal:
                operand = new CobolStringConditionOperand(
                    TargetEscapes.CobolString(literal.Value),
                    TargetEscapes.CobolByteLength(literal.Value).ToString(CultureInfo.InvariantCulture),
                    ReadsStorage: false);
                return true;

            default:
                if (runtimeBuffers.TryGetValue(expression, out RuntimeStringBuffer? buffer))
                {
                    runtimeBufferIndex++;
                    operand = new CobolStringConditionOperand(
                        buffer.ValueName,
                        buffer.LengthName,
                        ReadsStorage: true);
                    return true;
                }

                operand = default;
                return false;
        }
    }

    private static bool TryGetCobolScalarConditionOperand(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        out string operand,
        out bool readsStorage)
    {
        if (expression.Type is SmileType.Integer &&
            TryRenderCobolIntegerExpression(
                expression,
                identifiers,
                out operand,
                out readsStorage))
        {
            return true;
        }

        switch (expression)
        {
            case BoundVariableExpression { Variable.Type: SmileType.Boolean } variable:
                operand = identifiers.Get(variable.Variable);
                readsStorage = true;
                return true;

            case BoundBooleanLiteralExpression boolean:
                operand = TargetEscapes.CobolString(boolean.Value ? "TRUE" : "FALSE");
                readsStorage = false;
                return true;

            default:
                operand = string.Empty;
                readsStorage = false;
                return false;
        }
    }

    private static void AppendCobolIntegerEvaluation(
        StringBuilder source,
        string indent,
        BoundExpression expression,
        int scratchIndex,
        TargetIdentifierMap identifiers,
        CobolRuntimePlan runtime)
    {
        string destination = runtime.IntegerScratch(scratchIndex);
        switch (expression)
        {
            case BoundIntegerLiteralExpression literal:
                source.Append(indent).Append("MOVE ")
                    .Append(literal.Value.ToString(CultureInfo.InvariantCulture))
                    .Append(" TO ").AppendLine(destination);
                return;

            case BoundVariableExpression { Variable.Type: SmileType.Integer } variable:
                source.Append(indent).Append("MOVE FUNCTION NUMVAL(")
                    .Append(identifiers.Get(variable.Variable)).Append(") TO ")
                    .AppendLine(destination);
                return;

            case BoundUnaryExpression
                {
                    Operator.Kind: BoundUnaryOperatorKind.Identity
                } identity:
                AppendCobolIntegerEvaluation(
                    source,
                    indent,
                    identity.Operand,
                    scratchIndex,
                    identifiers,
                    runtime);
                return;

            case BoundUnaryExpression
                {
                    Operator.Kind: BoundUnaryOperatorKind.Negation
                } negation:
                AppendCobolIntegerEvaluation(
                    source,
                    indent,
                    negation.Operand,
                    scratchIndex,
                    identifiers,
                    runtime);
                source.Append(indent).AppendLine("CALL \"smile_checked_negate\" USING");
                source.Append(indent).Append("    BY VALUE ").AppendLine(destination);
                source.Append(indent).Append("    BY REFERENCE ").AppendLine(destination);
                source.Append(indent).Append("    RETURNING ").AppendLine(RuntimeStatusName);
                AppendCobolArithmeticErrorCheck(source, indent);
                return;

            case BoundBinaryExpression binary when
                binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division:
                AppendCobolIntegerEvaluation(
                    source,
                    indent,
                    binary.Left,
                    scratchIndex,
                    identifiers,
                    runtime);
                AppendCobolIntegerEvaluation(
                    source,
                    indent,
                    binary.Right,
                    scratchIndex + 1,
                    identifiers,
                    runtime);
                string helper = binary.Operator.Kind switch
                {
                    BoundBinaryOperatorKind.Addition => "smile_checked_add",
                    BoundBinaryOperatorKind.Subtraction => "smile_checked_subtract",
                    BoundBinaryOperatorKind.Multiplication => "smile_checked_multiply",
                    BoundBinaryOperatorKind.Division => "smile_checked_divide",
                    _ => throw new InvalidOperationException("Unsupported COBOL Integer operation.")
                };
                source.Append(indent).Append("CALL ").Append(TargetEscapes.CobolString(helper))
                    .AppendLine(" USING");
                source.Append(indent).Append("    BY VALUE ").AppendLine(destination);
                source.Append(indent).Append("    BY VALUE ")
                    .AppendLine(runtime.IntegerScratch(scratchIndex + 1));
                source.Append(indent).Append("    BY REFERENCE ").AppendLine(destination);
                source.Append(indent).Append("    RETURNING ").AppendLine(RuntimeStatusName);
                AppendCobolArithmeticErrorCheck(source, indent);
                return;

            default:
                throw new InvalidOperationException(
                    "COBOL could not materialize a checked Integer expression.");
        }
    }

    private static void AppendCobolArithmeticErrorCheck(
        StringBuilder source,
        string indent)
    {
        source.Append(indent).Append("IF ").Append(RuntimeStatusName).AppendLine(" NOT = 0");
        source.Append(indent).Append("    IF ").Append(RuntimeStatusName).AppendLine(" = 7");
        source.Append(indent).AppendLine(
            "        DISPLAY \"SMILE Runtime Error SMILER1207: Division by zero.\" UPON STDERR");
        source.Append(indent).AppendLine("    ELSE");
        source.Append(indent).AppendLine(
            "        DISPLAY \"SMILE Runtime Error SMILER1206: Integer arithmetic overflow.\" UPON STDERR");
        source.Append(indent).AppendLine("    END-IF");
        source.Append(indent).AppendLine("    MOVE 1 TO RETURN-CODE");
        source.Append(indent).AppendLine("    GOBACK");
        source.Append(indent).AppendLine("END-IF");
    }

    private static bool TryRenderCobolIntegerExpression(
        BoundExpression expression,
        TargetIdentifierMap identifiers,
        out string rendered,
        out bool readsStorage)
    {
        switch (expression)
        {
            case BoundVariableExpression { Variable.Type: SmileType.Integer } variable:
                rendered = $"FUNCTION NUMVAL({identifiers.Get(variable.Variable)})";
                readsStorage = true;
                return true;

            case BoundIntegerLiteralExpression integer:
                rendered = integer.Value.ToString(CultureInfo.InvariantCulture);
                readsStorage = false;
                return true;

            case BoundUnaryExpression unary when
                unary.Operator.Kind is BoundUnaryOperatorKind.Identity or
                    BoundUnaryOperatorKind.Negation:
                if (!TryRenderCobolIntegerExpression(
                        unary.Operand,
                        identifiers,
                        out string unaryOperand,
                        out readsStorage))
                {
                    rendered = string.Empty;
                    return false;
                }

                rendered = unary.Operator.Kind is BoundUnaryOperatorKind.Negation
                    ? $"(-({unaryOperand}))"
                    : $"({unaryOperand})";
                return true;

            case BoundBinaryExpression binary when
                binary.Operator.Kind is BoundBinaryOperatorKind.Addition or
                    BoundBinaryOperatorKind.Subtraction or
                    BoundBinaryOperatorKind.Multiplication or
                    BoundBinaryOperatorKind.Division:
                if (!TryRenderCobolIntegerExpression(
                        binary.Left,
                        identifiers,
                        out string left,
                        out bool leftReadsStorage) ||
                    !TryRenderCobolIntegerExpression(
                        binary.Right,
                        identifiers,
                        out string right,
                        out bool rightReadsStorage))
                {
                    rendered = string.Empty;
                    readsStorage = false;
                    return false;
                }

                readsStorage = leftReadsStorage || rightReadsStorage;
                if (binary.Operator.Kind is BoundBinaryOperatorKind.Division)
                {
                    // INTEGER-PART truncates toward zero, matching SMILE's
                    // signed Integer division instead of COBOL decimal math.
                    rendered = $"FUNCTION INTEGER-PART(({left}) / ({right}))";
                    return true;
                }

                string arithmeticOperator = binary.Operator.Kind switch
                {
                    BoundBinaryOperatorKind.Addition => "+",
                    BoundBinaryOperatorKind.Subtraction => "-",
                    BoundBinaryOperatorKind.Multiplication => "*",
                    _ => throw new InvalidOperationException("Unsupported COBOL Integer operator.")
                };
                rendered = $"(({left}) {arithmeticOperator} ({right}))";
                return true;

            default:
                rendered = string.Empty;
                readsStorage = false;
                return false;
        }
    }

    private static IReadOnlyDictionary<VariableSymbol, string> CreateLogicalLengthNames(
        BoundProgram program,
        TargetIdentifierMap identifiers,
        BoundProgramAnalysis analysis)
    {
        var names = new Dictionary<VariableSymbol, string>();
        var used = program.Variables
            .Select(identifiers.Get)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int index = 0; index < program.Variables.Count; index++)
        {
            VariableSymbol variable = program.Variables[index];
            // COBOL stores every SMILE value in an alphanumeric field. A
            // logical length beside every field keeps direct LET/SET copies,
            // exact empty Strings, embedded NUL, and runtime-formatted scalar
            // values uniform after branch merges.
            string preferred = $"SMILE-SET-LENGTH-{index}";
            string name = preferred;
            int suffix = 2;
            while (!used.Add(name))
            {
                name = preferred + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            names.Add(variable, name);
        }

        return names;
    }

    private static IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer>
        CreateRuntimeStringBuffers(
            BoundProgram program,
            BoundProgramAnalysis analysis)
    {
        var buffers = new Dictionary<BoundExpression, RuntimeStringBuffer>(
            ReferenceEqualityComparer.Instance);
        foreach (BoundStatement statement in analysis.EnumerateStatements())
        {
            BoundStatementAnalysis facts = analysis.GetStatementFacts(statement);
            switch (statement)
            {
                case BoundLetStatement { Variable.Type: SmileType.String } let when
                    !facts.Value.IsKnown:
                    Add(let.Initializer, $"SMILE-STATEMENT-{facts.Ordinal}-STRING");
                    Collect(let.Initializer);
                    break;

                case BoundSetStatement { Variable.Type: SmileType.String } set when
                    !facts.Value.IsKnown:
                    Add(set.Value, $"SMILE-STATEMENT-{facts.Ordinal}-STRING");
                    Collect(set.Value);
                    break;

                case BoundLetStatement let:
                    Collect(let.Initializer);
                    break;

                case BoundSetStatement set:
                    Collect(set.Value);
                    break;

                case BoundPrintStatement { IsBlankLine: false } print:
                    if (!facts.Value.IsKnown && print.Value is not BoundVariableExpression)
                    {
                        Add(print.Value, $"SMILE-STATEMENT-{facts.Ordinal}-STRING");
                    }

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
            if (expression is BoundBinaryExpression booleanComparison &&
                booleanComparison.Left.Type is SmileType.Boolean &&
                booleanComparison.Operator.Kind is BoundBinaryOperatorKind.Equality or
                    BoundBinaryOperatorKind.Inequality)
            {
                Add(booleanComparison);
            }

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

        void Add(BoundExpression operand, string? preferredName = null)
        {
            if (buffers.ContainsKey(operand) ||
                (preferredName is null &&
                 operand is BoundVariableExpression or BoundStringLiteralExpression))
            {
                return;
            }

            string valueName = preferredName ?? $"SMILE-EXPRESSION-{buffers.Count}-STRING";
            buffers.Add(operand, new RuntimeStringBuffer(
                operand,
                valueName,
                valueName + "-LENGTH",
                Math.Max(1, analysis.MaximumExpressionDisplayUtf8ByteLength(operand))));
        }
    }

    private static CobolRuntimePlan CreateRuntimePlan(BoundProgram program)
    {
        IReadOnlyList<BoundInputStatement> inputs = TargetRuntimeFacts.Inputs(program);
        var inputFunctions = new Dictionary<VariableSymbol, string>();
        foreach (VariableSymbol variable in program.Variables)
        {
            if (inputs.Any(input => ReferenceEquals(input.Variable, variable)))
            {
                inputFunctions.Add(variable, $"smile_input_{inputFunctions.Count}");
            }
        }

        bool needsCheckedIntegerArithmetic =
            TargetRuntimeFacts.NeedsCheckedIntegerArithmetic(program);
        int integerScratchCount = needsCheckedIntegerArithmetic
            ? BoundStatementTree.EnumerateExpressions(program)
                .Select(RequiredIntegerScratchCount)
                .DefaultIfEmpty(1)
                .Max()
            : 0;
        return new CobolRuntimePlan(
            inputFunctions,
            needsCheckedIntegerArithmetic,
            Math.Max(1, integerScratchCount));
    }

    private static int RequiredIntegerScratchCount(BoundExpression expression) =>
        expression switch
        {
            BoundIntegerLiteralExpression => 1,
            BoundVariableExpression { Variable.Type: SmileType.Integer } => 1,
            BoundUnaryExpression unary => RequiredIntegerScratchCount(unary.Operand),
            BoundBinaryExpression binary => Math.Max(
                RequiredIntegerScratchCount(binary.Left),
                1 + RequiredIntegerScratchCount(binary.Right)),
            BoundInterpolatedStringExpression interpolated => interpolated.Parts
                .OfType<BoundInterpolationExpressionPart>()
                .Select(part => RequiredIntegerScratchCount(part.Expression))
                .DefaultIfEmpty(0)
                .Max(),
            _ => 0
        };

    private static string GenerateCobolRuntimeCompanion(
        BoundProgram program,
        CobolRuntimePlan runtime,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths)
    {
        var source = new StringBuilder();
        source.AppendLine("#include <limits.h>");
        source.AppendLine("#include <stdint.h>");
        source.AppendLine("#include <stdio.h>");
        source.AppendLine("#include <string.h>");
        source.AppendLine("#ifdef _WIN32");
        source.AppendLine("#include <fcntl.h>");
        source.AppendLine("#include <io.h>");
        source.AppendLine("#endif");
        source.AppendLine();
        source.AppendLine($"#define SMILE_MAX_INPUT_BYTES {SmileLanguage.MaximumInputLineUtf8Bytes}");
        source.AppendLine();
        source.AppendLine("static int smile_skip_line_feed = 0;");
        source.AppendLine("static int smile_input_prepared = 0;");
        source.AppendLine("static int smile_input_prepare_status = 0;");
        source.AppendLine();
        source.AppendLine("static void smile_prepare_input(void)");
        source.AppendLine("{");
        source.AppendLine("#ifdef _WIN32");
        source.AppendLine("    if (!smile_input_prepared)");
        source.AppendLine("    {");
        source.AppendLine("        if (_setmode(_fileno(stdin), _O_BINARY) == -1)");
        source.AppendLine("            smile_input_prepare_status = 6;");
        source.AppendLine("        smile_input_prepared = 1;");
        source.AppendLine("    }");
        source.AppendLine("#else");
        source.AppendLine("    smile_input_prepared = 1;");
        source.AppendLine("#endif");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static int smile_read_byte(void)");
        source.AppendLine("{");
        source.AppendLine("    int value = fgetc(stdin);");
        source.AppendLine("    if (smile_skip_line_feed)");
        source.AppendLine("    {");
        source.AppendLine("        smile_skip_line_feed = 0;");
        source.AppendLine("        if (value == '\\n') value = fgetc(stdin);");
        source.AppendLine("    }");
        source.AppendLine("    return value;");
        source.AppendLine("}");
        source.AppendLine();
        AppendCobolUtf8Validator(source);
        source.AppendLine();
        source.AppendLine("static int smile_read_line(unsigned char *buffer, size_t *length)");
        source.AppendLine("{");
        source.AppendLine("    size_t count = 0;");
        source.AppendLine("    smile_prepare_input();");
        source.AppendLine("    if (smile_input_prepare_status != 0) return smile_input_prepare_status;");
        source.AppendLine("    for (;;)");
        source.AppendLine("    {");
        source.AppendLine("        int value = smile_read_byte();");
        source.AppendLine("        if (value == EOF)");
        source.AppendLine("        {");
        source.AppendLine("            if (ferror(stdin)) return 6;");
        source.AppendLine("            if (count == 0) return 1;");
        source.AppendLine("            break;");
        source.AppendLine("        }");
        source.AppendLine("        if (value == '\\n') break;");
        source.AppendLine("        if (value == '\\r')");
        source.AppendLine("        {");
        source.AppendLine("            smile_skip_line_feed = 1;");
        source.AppendLine("            break;");
        source.AppendLine("        }");
        source.AppendLine("        if (count == SMILE_MAX_INPUT_BYTES) return 2;");
        source.AppendLine("        buffer[count++] = (unsigned char)value;");
        source.AppendLine("    }");
        source.AppendLine("    if (!smile_valid_utf8(buffer, count)) return 6;");
        source.AppendLine("    *length = count;");
        source.AppendLine("    return 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("static void smile_store(char *destination, size_t capacity,");
        source.AppendLine("    uint32_t *logical_length, const unsigned char *value, size_t length)");
        source.AppendLine("{");
        source.AppendLine("    memset(destination, ' ', capacity);");
        source.AppendLine("    if (length > 0) memcpy(destination, value, length);");
        source.AppendLine("    *logical_length = (uint32_t)length;");
        source.AppendLine("}");

        bool hasIntegerInput = runtime.InputFunctions.Keys.Any(variable =>
            variable.Type is SmileType.Integer);
        bool hasBooleanInput = runtime.InputFunctions.Keys.Any(variable =>
            variable.Type is SmileType.Boolean);
        if (hasIntegerInput || hasBooleanInput)
        {
            source.AppendLine();
            source.AppendLine("static void smile_trim_ascii(const unsigned char *value, size_t length,");
            source.AppendLine("    size_t *start, size_t *end)");
            source.AppendLine("{");
            source.AppendLine("    size_t first = 0;");
            source.AppendLine("    size_t last = length;");
            source.AppendLine("    while (first < last && (value[first] == ' ' || value[first] == '\\t')) first++;");
            source.AppendLine("    while (last > first && (value[last - 1] == ' ' || value[last - 1] == '\\t')) last--;");
            source.AppendLine("    *start = first;");
            source.AppendLine("    *end = last;");
            source.AppendLine("}");
        }

        if (hasIntegerInput)
        {
            source.AppendLine();
            AppendCobolIntegerInputParser(source);
        }

        if (hasBooleanInput)
        {
            source.AppendLine();
            AppendCobolBooleanInputParser(source);
        }

        foreach (VariableSymbol variable in program.Variables.Where(runtime.InputFunctions.ContainsKey))
        {
            source.AppendLine();
            AppendCobolInputFunction(
                source,
                runtime.InputFunctions[variable],
                variable.Type,
                storageLengths[variable]);
        }

        if (runtime.NeedsCheckedIntegerArithmetic)
        {
            source.AppendLine();
            AppendCobolCheckedArithmeticFunctions(source);
        }

        return source.ToString();
    }

    private static void AppendCobolUtf8Validator(StringBuilder source)
    {
        source.AppendLine("static int smile_valid_utf8(const unsigned char *value, size_t length)");
        source.AppendLine("{");
        source.AppendLine("    size_t index = 0;");
        source.AppendLine("    while (index < length)");
        source.AppendLine("    {");
        source.AppendLine("        unsigned char first = value[index++];");
        source.AppendLine("        if (first <= 0x7F) continue;");
        source.AppendLine("        if (first >= 0xC2 && first <= 0xDF)");
        source.AppendLine("        {");
        source.AppendLine("            if (index >= length || (value[index++] & 0xC0) != 0x80) return 0;");
        source.AppendLine("            continue;");
        source.AppendLine("        }");
        source.AppendLine("        if (first >= 0xE0 && first <= 0xEF)");
        source.AppendLine("        {");
        source.AppendLine("            if (index + 1 >= length) return 0;");
        source.AppendLine("            unsigned char second = value[index++];");
        source.AppendLine("            unsigned char third = value[index++];");
        source.AppendLine("            if ((second & 0xC0) != 0x80 || (third & 0xC0) != 0x80) return 0;");
        source.AppendLine("            if (first == 0xE0 && second < 0xA0) return 0;");
        source.AppendLine("            if (first == 0xED && second > 0x9F) return 0;");
        source.AppendLine("            continue;");
        source.AppendLine("        }");
        source.AppendLine("        if (first >= 0xF0 && first <= 0xF4)");
        source.AppendLine("        {");
        source.AppendLine("            if (index + 2 >= length) return 0;");
        source.AppendLine("            unsigned char second = value[index++];");
        source.AppendLine("            unsigned char third = value[index++];");
        source.AppendLine("            unsigned char fourth = value[index++];");
        source.AppendLine("            if ((second & 0xC0) != 0x80 || (third & 0xC0) != 0x80 ||");
        source.AppendLine("                (fourth & 0xC0) != 0x80) return 0;");
        source.AppendLine("            if (first == 0xF0 && second < 0x90) return 0;");
        source.AppendLine("            if (first == 0xF4 && second > 0x8F) return 0;");
        source.AppendLine("            continue;");
        source.AppendLine("        }");
        source.AppendLine("        return 0;");
        source.AppendLine("    }");
        source.AppendLine("    return 1;");
        source.AppendLine("}");
    }

    private static void AppendCobolIntegerInputParser(StringBuilder source)
    {
        source.AppendLine("static int smile_parse_integer(const unsigned char *value, size_t length,");
        source.AppendLine("    int64_t *result)");
        source.AppendLine("{");
        source.AppendLine("    size_t start;");
        source.AppendLine("    size_t end;");
        source.AppendLine("    size_t index;");
        source.AppendLine("    int negative = 0;");
        source.AppendLine("    uint64_t magnitude = 0;");
        source.AppendLine("    uint64_t limit;");
        source.AppendLine("    smile_trim_ascii(value, length, &start, &end);");
        source.AppendLine("    if (start == end) return 3;");
        source.AppendLine("    index = start;");
        source.AppendLine("    if (value[index] == '+' || value[index] == '-')");
        source.AppendLine("    {");
        source.AppendLine("        negative = value[index] == '-';");
        source.AppendLine("        index++;");
        source.AppendLine("    }");
        source.AppendLine("    if (index == end) return 3;");
        source.AppendLine("    for (size_t grammar = index; grammar < end; grammar++)");
        source.AppendLine("        if (value[grammar] < '0' || value[grammar] > '9') return 3;");
        source.AppendLine("    limit = negative ? UINT64_C(9223372036854775808) : UINT64_C(9223372036854775807);");
        source.AppendLine("    for (; index < end; index++)");
        source.AppendLine("    {");
        source.AppendLine("        unsigned digit = (unsigned)(value[index] - '0');");
        source.AppendLine("        if (magnitude > (limit - digit) / 10) return 4;");
        source.AppendLine("        magnitude = magnitude * 10 + digit;");
        source.AppendLine("    }");
        source.AppendLine("    if (negative)");
        source.AppendLine("        *result = magnitude == UINT64_C(9223372036854775808)");
        source.AppendLine("            ? INT64_MIN : -(int64_t)magnitude;");
        source.AppendLine("    else");
        source.AppendLine("        *result = (int64_t)magnitude;");
        source.AppendLine("    return 0;");
        source.AppendLine("}");
    }

    private static void AppendCobolBooleanInputParser(StringBuilder source)
    {
        source.AppendLine("static int smile_ascii_equal(const unsigned char *value, size_t start,");
        source.AppendLine("    size_t end, const char *expected)");
        source.AppendLine("{");
        source.AppendLine("    size_t expected_length = strlen(expected);");
        source.AppendLine("    if (end - start != expected_length) return 0;");
        source.AppendLine("    for (size_t index = 0; index < expected_length; index++)");
        source.AppendLine("    {");
        source.AppendLine("        unsigned char actual = value[start + index];");
        source.AppendLine("        if (actual >= 'a' && actual <= 'z') actual = (unsigned char)(actual - 'a' + 'A');");
        source.AppendLine("        if (actual != (unsigned char)expected[index]) return 0;");
        source.AppendLine("    }");
        source.AppendLine("    return 1;");
        source.AppendLine("}");
    }

    private static void AppendCobolInputFunction(
        StringBuilder source,
        string functionName,
        SmileType type,
        int storageLength)
    {
        source.Append("int ").Append(functionName)
            .AppendLine("(char *destination, uint32_t *logical_length)");
        source.AppendLine("{");
        source.AppendLine("    unsigned char line[SMILE_MAX_INPUT_BYTES];");
        source.AppendLine("    size_t length = 0;");
        source.AppendLine("    int status = smile_read_line(line, &length);");
        source.AppendLine("    if (status != 0) return status;");
        switch (type)
        {
            case SmileType.String:
                source.Append("    smile_store(destination, ").Append(storageLength)
                    .AppendLine(", logical_length, line, length);");
                break;

            case SmileType.Integer:
                source.AppendLine("    int64_t value;");
                source.AppendLine("    char formatted[21];");
                source.AppendLine("    status = smile_parse_integer(line, length, &value);");
                source.AppendLine("    if (status != 0) return status;");
                source.AppendLine("    int formatted_length = snprintf(formatted, sizeof(formatted), \"%lld\",");
                source.AppendLine("        (long long)value);");
                source.Append("    smile_store(destination, ").Append(storageLength)
                    .AppendLine(", logical_length, (const unsigned char *)formatted,");
                source.AppendLine("        (size_t)formatted_length);");
                break;

            case SmileType.Boolean:
                source.AppendLine("    size_t start;");
                source.AppendLine("    size_t end;");
                source.AppendLine("    smile_trim_ascii(line, length, &start, &end);");
                source.AppendLine("    if (smile_ascii_equal(line, start, end, \"TRUE\"))");
                source.Append("        smile_store(destination, ").Append(storageLength)
                    .AppendLine(", logical_length, (const unsigned char *)\"TRUE\", 4);");
                source.AppendLine("    else if (smile_ascii_equal(line, start, end, \"FALSE\"))");
                source.Append("        smile_store(destination, ").Append(storageLength)
                    .AppendLine(", logical_length, (const unsigned char *)\"FALSE\", 5);");
                source.AppendLine("    else");
                source.AppendLine("        return 5;");
                break;
        }

        source.AppendLine("    return 0;");
        source.AppendLine("}");
    }

    private static void AppendCobolCheckedArithmeticFunctions(StringBuilder source)
    {
        source.AppendLine("int smile_checked_add(int64_t left, int64_t right, int64_t *result)");
        source.AppendLine("{");
        source.AppendLine("    return __builtin_add_overflow(left, right, result) ? 6 : 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("int smile_checked_subtract(int64_t left, int64_t right, int64_t *result)");
        source.AppendLine("{");
        source.AppendLine("    return __builtin_sub_overflow(left, right, result) ? 6 : 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("int smile_checked_multiply(int64_t left, int64_t right, int64_t *result)");
        source.AppendLine("{");
        source.AppendLine("    return __builtin_mul_overflow(left, right, result) ? 6 : 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("int smile_checked_negate(int64_t value, int64_t *result)");
        source.AppendLine("{");
        source.AppendLine("    if (value == INT64_MIN) return 6;");
        source.AppendLine("    *result = -value;");
        source.AppendLine("    return 0;");
        source.AppendLine("}");
        source.AppendLine();
        source.AppendLine("int smile_checked_divide(int64_t left, int64_t right, int64_t *result)");
        source.AppendLine("{");
        source.AppendLine("    if (right == 0) return 7;");
        source.AppendLine("    if (left == INT64_MIN && right == -1) return 6;");
        source.AppendLine("    *result = left / right;");
        source.AppendLine("    return 0;");
        source.AppendLine("}");
    }

    private static bool NeedsRuntimeFacilities(
        BoundProgramAnalysis analysis,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
    {
        if (runtimeStringBuffers.Count > 0)
        {
            return true;
        }

        return analysis.EnumerateStatements().Any(statement =>
            statement is BoundLetStatement or BoundSetStatement or BoundPrintStatement &&
            !analysis.GetStatementFacts(statement).Value.IsKnown);
    }

    private static IReadOnlyDictionary<VariableSymbol, int> CreateStorageLengths(
        BoundProgram program,
        BoundProgramAnalysis analysis)
    {
        bool hasConditionalControlFlow = analysis.EnumerateStatements()
            .Any(statement => statement is BoundIfStatement);
        var inputVariables = TargetRuntimeFacts.Inputs(program)
            .Select(input => input.Variable)
            .ToHashSet();

        return program.Variables.ToDictionary(
            variable => variable,
            variable =>
            {
                int assignedLength = analysis.MaximumAssignedUtf8ByteLength(variable);
                if (inputVariables.Contains(variable))
                {
                    int requiredInputLength = variable.Type switch
                    {
                        SmileType.String => SmileLanguage.MaximumInputLineUtf8Bytes,
                        SmileType.Integer => 20,
                        SmileType.Boolean => 5,
                        _ => 1
                    };
                    assignedLength = Math.Max(assignedLength, requiredInputLength);
                }

                if (hasConditionalControlFlow && assignedLength <= 1)
                {
                    // GnuCOBOL warns when a one-byte display field uses
                    // variable-length reference modification. IF makes
                    // runtime value paths possible, so one spare byte keeps
                    // the established exact-length spelling warning-free.
                    return 2;
                }

                return assignedLength;
            });
    }

    private static IEnumerable<VariableSymbol> EnumerateConditionVariables(BoundExpression expression)
    {
        switch (expression)
        {
            case BoundVariableExpression variable:
                yield return variable.Variable;
                break;

            case BoundUnaryExpression unary:
                foreach (VariableSymbol nested in EnumerateConditionVariables(unary.Operand))
                {
                    yield return nested;
                }

                break;

            case BoundBinaryExpression binary:
                foreach (VariableSymbol nested in EnumerateConditionVariables(binary.Left))
                {
                    yield return nested;
                }

                foreach (VariableSymbol nested in EnumerateConditionVariables(binary.Right))
                {
                    yield return nested;
                }

                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolationExpressionPart hole in
                    interpolated.Parts.OfType<BoundInterpolationExpressionPart>())
                {
                    foreach (VariableSymbol nested in EnumerateConditionVariables(hole.Expression))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }

    private static string ConditionName(int ordinal) => $"SMILE-IF-CONDITION-{ordinal}";
}
