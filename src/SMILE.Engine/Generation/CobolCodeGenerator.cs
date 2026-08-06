using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal sealed class CobolCodeGenerator : ICodeGenerator
{
    private const string RuntimePointerName = "SMILE-RUNTIME-POINTER";
    private const string RuntimeIntegerName = "SMILE-RUNTIME-INTEGER";
    private const string RuntimeIntegerTextName = "SMILE-RUNTIME-INTEGER-TEXT";
    private const string RuntimeConditionName = "SMILE-RUNTIME-CONDITION";

    private sealed record RuntimeStringBuffer(
        BoundExpression Expression,
        string ValueName,
        string LengthName,
        int Capacity);

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
        bool needsRuntimeFacilities = NeedsRuntimeFacilities(analysis, runtimeStringBuffers);

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
                source.Append("01 ").Append(RuntimeIntegerName)
                    .AppendLine(" PIC S9(18) COMP-5 VALUE 0.");
                source.Append("01 ").Append(RuntimeIntegerTextName)
                    .AppendLine(" PIC -(19)9 VALUE ZERO.");
                source.Append("01 ").Append(RuntimeConditionName)
                    .AppendLine(" PIC 9 COMP-5 VALUE 0.");
            }
        }

        source.AppendLine();
        source.AppendLine("PROCEDURE DIVISION.");
        source.AppendLine("*> SMILE PRINT reads current storage when it directly names a variable.");
        AppendStatements(
            source,
            program.Statements,
            "    ",
            analysis,
            identifiers,
            logicalLengths,
            storageLengths,
            runtimeStringBuffers,
            insideConditional: false);

        source.AppendLine("    STOP RUN.");

        return new GeneratedProgram(
            Language,
            new[] { new GeneratedFile("Program.cob", TextOutput.EnsureOneTrailingNewLine(source.ToString()), IsPrimary: true) });
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
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
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
                runtimeStringBuffers);
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
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
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
                source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                    .Append(" = ").Append(integer).AppendLine(terminator);
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
                    runtimeStringBuffers);
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
                    string variableSlice = $"{variableName}(1:{variableLength})";
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
                    source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                        .Append(" = ").AppendLine(integer);
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
                        runtimeStringBuffers);
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
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
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
                runtimeStringBuffers);
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
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers)
    {
        foreach (RuntimeTextSegment segment in RuntimeTextPlan.Flatten(expression))
        {
            switch (segment)
            {
                case RuntimeLiteralTextSegment literal when literal.Text.Length > 0:
                    source.Append(indent).Append("DISPLAY ")
                        .Append(TargetEscapes.CobolString(literal.Text))
                        .AppendLine(" WITH NO ADVANCING");
                    break;

                case RuntimeExpressionTextSegment { Expression: BoundVariableExpression variable }:
                    AppendCobolVariableSegment(
                        source,
                        indent,
                        variable.Variable,
                        identifiers,
                        logicalLengths,
                        storageLengths);
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Integer &&
                    TryRenderCobolIntegerExpression(
                        runtime.Expression,
                        identifiers,
                        out string integer,
                        out _):
                    source.Append(indent).Append("COMPUTE ").Append(RuntimeIntegerName)
                        .Append(" = ").AppendLine(integer);
                    source.Append(indent).Append("MOVE ").Append(RuntimeIntegerName)
                        .Append(" TO ").AppendLine(RuntimeIntegerTextName);
                    source.Append(indent).Append("DISPLAY FUNCTION TRIM(")
                        .Append(RuntimeIntegerTextName).AppendLine(") WITH NO ADVANCING");
                    break;

                case RuntimeExpressionTextSegment runtime when
                    runtime.Expression.Type is SmileType.Boolean:
                    AppendCobolConditionEvaluation(
                        source,
                        indent,
                        RuntimeConditionName,
                        runtime.Expression,
                        identifiers,
                        logicalLengths,
                        storageLengths,
                        runtimeStringBuffers);
                    source.Append(indent).Append("IF ").Append(RuntimeConditionName).AppendLine(" = 1");
                    source.Append(indent).AppendLine("    DISPLAY \"TRUE\" WITH NO ADVANCING");
                    source.Append(indent).AppendLine("ELSE");
                    source.Append(indent).AppendLine("    DISPLAY \"FALSE\" WITH NO ADVANCING");
                    source.Append(indent).AppendLine("END-IF");
                    break;
            }
        }

        source.Append(indent).Append("DISPLAY X\"0A\" WITH NO ADVANCING")
            .AppendLine(terminateSentence ? "." : string.Empty);
    }

    private static void AppendStatements(
        StringBuilder source,
        IReadOnlyList<BoundStatement> statements,
        string indent,
        BoundProgramAnalysis analysis,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths,
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeStringBuffers,
        bool insideConditional)
    {
        foreach (BoundStatement statement in statements)
        {
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
                        runtimeStringBuffers);
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
                        runtimeStringBuffers);
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
                        runtimeStringBuffers);
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
                runtimeStringBuffers);
            source.Append(indent).Append("IF ").Append(conditionName).AppendLine(" = 1");
            if (clause.Statements.Count == 0)
            {
                source.Append(indent).AppendLine("    CONTINUE");
            }
            else
            {
                AppendStatements(
                    source,
                    clause.Statements,
                    indent + "    ",
                    analysis,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers,
                    insideConditional: true);
            }
        }

        if (conditional.HasElseClause)
        {
            source.Append(indent).AppendLine("ELSE");
            if (conditional.ElseStatements.Count == 0)
            {
                source.Append(indent).AppendLine("    CONTINUE");
            }
            else
            {
                AppendStatements(
                    source,
                    conditional.ElseStatements,
                    indent + "    ",
                    analysis,
                    identifiers,
                    logicalLengths,
                    storageLengths,
                    runtimeStringBuffers,
                    insideConditional: true);
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

    private static void AppendCobolVariableSegment(
        StringBuilder source,
        string indent,
        VariableSymbol variable,
        TargetIdentifierMap identifiers,
        IReadOnlyDictionary<VariableSymbol, string> logicalLengths,
        IReadOnlyDictionary<VariableSymbol, int> storageLengths)
    {
        string name = identifiers.Get(variable);
        string lengthName = logicalLengths[variable];

        source.Append(indent).Append("IF ").Append(lengthName).AppendLine(" > 0");
        source.Append(indent).Append("    DISPLAY ").Append(name)
            .Append("(1:").Append(lengthName).AppendLine(") WITH NO ADVANCING");
        source.Append(indent).AppendLine("END-IF");
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
        source.Append(indent).Append("    DISPLAY ").Append(name)
            .Append("(1:").Append(lengthName).AppendLine(") WITH NO ADVANCING");
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
        IReadOnlyDictionary<BoundExpression, RuntimeStringBuffer> runtimeBuffers)
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
                runtimeBuffers);
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
                runtimeBuffers);
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
                runtimeBuffers);
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
                runtimeBuffers);
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
                runtimeBuffers);
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

        return program.Variables.ToDictionary(
            variable => variable,
            variable =>
            {
                int assignedLength = analysis.MaximumAssignedUtf8ByteLength(variable);
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
