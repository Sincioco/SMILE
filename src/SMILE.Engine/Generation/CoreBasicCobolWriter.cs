using System.Globalization;
using System.Text;

namespace SMILE.Engine;

// GnuCOBOL contained programs provide native call frames: LINKAGE carries
// parameters, LOCAL-STORAGE is recreated for recursion, and parent GLOBAL
// storage remains visible to every routine.
internal sealed class CobolWriter
{
    private const int TextCapacity = 4096;
    private readonly BoundProgram _program;
    private readonly TargetIdentifierMap _identifiers;
    private readonly IReadOnlyDictionary<VariableSymbol, SmileValue> _constants;
    private readonly StringBuilder _builder = new();

    public CobolWriter(BoundProgram program)
    {
        _program = program;
        _identifiers = TargetIdentifierMap.Create(program, TargetLanguage.Cobol);
        _constants = StructuredStatements(program.SourceItems)
            .OfType<BoundConstStatement>()
            .ToDictionary(statement => statement.Variable, statement => statement.Value);
    }

    public string Write()
    {
        ProcedurePlan main = new ProcedureEmitter(this, _program.SourceItems, null).Write();
        ProcedurePlan[] routines = _program.Routines
            .Select(routine => new ProcedureEmitter(this, routine.SourceItems, routine).Write())
            .ToArray();

        Line("       IDENTIFICATION DIVISION.");
        Line("       PROGRAM-ID. Program.");
        Line("       DATA DIVISION.");
        Line("       WORKING-STORAGE SECTION.");
        WriteStateDefinition(linkage: false);
        WritePlanStorage(main);
        Line("       PROCEDURE DIVISION.");
        AppendBody(main.Body);
        Line("           MOVE 0 TO RETURN-CODE.");
        Line("           STOP RUN.");
        Line("       END PROGRAM Program.");
        Line();

        for (int index = 0; index < _program.Routines.Count; index++)
        {
            WriteRoutine(_program.Routines[index], routines[index]);
            Line();
        }

        return _builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private void WriteRoutine(BoundRoutineDeclaration routine, ProcedurePlan plan)
    {
        RoutineSymbol symbol = routine.Symbol;
        Line("       IDENTIFICATION DIVISION.");
        Line($"       PROGRAM-ID. {RoutineName(symbol)} IS RECURSIVE.");
        Line("       DATA DIVISION.");

        VariableSymbol[] locals = routine.Locals.Where(variable => !variable.IsParameter).ToArray();
        if (locals.Length > 0 || plan.Temporaries.Count > 0 || plan.NeedsDisplayNumber)
        {
            Line("       LOCAL-STORAGE SECTION.");
            foreach (VariableSymbol local in locals)
            {
                WriteDataDeclaration(local);
            }

            WritePlanStorage(plan);
        }

        Line("       LINKAGE SECTION.");
        WriteStateDefinition(linkage: true);
        foreach (VariableSymbol parameter in symbol.Parameters)
        {
            Line($"       01 {Name(parameter)} {Picture(parameter.Type)}.");
            if (parameter.Type is SmileType.String)
            {
                Line($"       01 {LengthName(parameter)} PIC S9(18) COMP-5.");
            }
        }

        if (symbol.IsFunction)
        {
            Line($"       01 SMILE-RETURN-VALUE {Picture(symbol.ReturnType ?? SmileType.Integer)}.");
            if (symbol.ReturnType is SmileType.String)
            {
                Line("       01 SMILE-RETURN-LENGTH PIC S9(18) COMP-5.");
            }
        }

        var usingItems = new List<string> { "BY REFERENCE SMILE-STATE" };
        foreach (VariableSymbol parameter in symbol.Parameters)
        {
            usingItems.Add($"BY REFERENCE {Name(parameter)}");
            if (parameter.Type is SmileType.String)
            {
                usingItems.Add($"BY REFERENCE {LengthName(parameter)}");
            }
        }
        if (symbol.IsFunction)
        {
            usingItems.Add("BY REFERENCE SMILE-RETURN-VALUE");
            if (symbol.ReturnType is SmileType.String)
            {
                usingItems.Add("BY REFERENCE SMILE-RETURN-LENGTH");
            }
        }

        Line("       PROCEDURE DIVISION USING");
        for (int index = 0; index < usingItems.Count; index++)
        {
            string terminator = index + 1 == usingItems.Count ? "." : string.Empty;
            Line($"           {usingItems[index]}{terminator}");
        }
        AppendBody(plan.Body);
        Line("           GOBACK.");
        Line($"       END PROGRAM {RoutineName(symbol)}.");
    }

    private void WritePlanStorage(ProcedurePlan plan)
    {
        foreach (Temporary temporary in plan.Temporaries)
        {
            Line($"       01 {temporary.Name} {Picture(temporary.Type)} {DefaultClause(temporary.Type)}.");
            if (temporary.Type is SmileType.String)
            {
                Line($"       01 {LengthName(temporary)} PIC S9(18) COMP-5 VALUE 0.");
            }
        }

        if (plan.NeedsDisplayNumber)
        {
            Line("       01 SMILE-DISPLAY-NUMBER PIC -(17)9.");
        }
    }

    private void WriteStateDefinition(bool linkage)
    {
        VariableSymbol[] globals = _program.Variables.Where(item => !item.IsConstant).ToArray();
        if (globals.Length == 0)
        {
            Line($"       01 SMILE-STATE PIC X{(linkage ? string.Empty : " VALUE SPACE")}.");
            return;
        }

        Line("       01 SMILE-STATE.");
        foreach (VariableSymbol variable in globals)
        {
            string valueClause = linkage ? string.Empty : " " + DefaultClause(variable.Type);
            if (variable.IsArray)
            {
                if (variable.ArrayRank == 1)
                {
                    Line($"          05 {ArrayElementName(variable)} {Picture(variable.Type)}{valueClause} OCCURS {variable.ArrayLength} TIMES.");
                    if (variable.Type is SmileType.String)
                    {
                        Line($"          05 {ArrayLengthElementName(variable)} PIC S9(18) COMP-5{(linkage ? string.Empty : " VALUE 0")} OCCURS {variable.ArrayLength} TIMES.");
                    }
                }
                else
                {
                    Line($"          05 {Name(variable)}-ROW OCCURS {variable.ArrayLength} TIMES.");
                    Line($"             10 {ArrayElementName(variable)} {Picture(variable.Type)}{valueClause} OCCURS {variable.ArraySecondLength} TIMES.");
                    if (variable.Type is SmileType.String)
                    {
                        Line($"          05 {Name(variable)}-LENGTH-ROW OCCURS {variable.ArrayLength} TIMES.");
                        Line($"             10 {ArrayLengthElementName(variable)} PIC S9(18) COMP-5{(linkage ? string.Empty : " VALUE 0")} OCCURS {variable.ArraySecondLength} TIMES.");
                    }
                }
            }
            else
            {
                Line($"          05 {Name(variable)} {Picture(variable.Type)}{valueClause}.");
                if (variable.Type is SmileType.String)
                {
                    Line($"          05 {LengthName(variable)} PIC S9(18) COMP-5{(linkage ? string.Empty : " VALUE 0")}.");
                }
            }
        }
    }

    private void WriteDataDeclaration(VariableSymbol variable)
    {
        string name = Name(variable);
        if (variable.IsArray)
        {
            Line($"       01 {name}.");
            if (variable.ArrayRank == 1)
            {
                Line($"          05 {ArrayElementName(variable)} {Picture(variable.Type)} {DefaultClause(variable.Type)} OCCURS {variable.ArrayLength} TIMES.");
                if (variable.Type is SmileType.String)
                {
                    Line($"          05 {ArrayLengthElementName(variable)} PIC S9(18) COMP-5 VALUE 0 OCCURS {variable.ArrayLength} TIMES.");
                }
            }
            else
            {
                Line($"          05 {name}-ROW OCCURS {variable.ArrayLength} TIMES.");
                Line($"             10 {ArrayElementName(variable)} {Picture(variable.Type)} {DefaultClause(variable.Type)} OCCURS {variable.ArraySecondLength} TIMES.");
                if (variable.Type is SmileType.String)
                {
                    Line($"          05 {name}-LENGTH-ROW OCCURS {variable.ArrayLength} TIMES.");
                    Line($"             10 {ArrayLengthElementName(variable)} PIC S9(18) COMP-5 VALUE 0 OCCURS {variable.ArraySecondLength} TIMES.");
                }
            }
        }
        else
        {
            Line($"       01 {name} {Picture(variable.Type)} {DefaultClause(variable.Type)}.");
            if (variable.Type is SmileType.String)
            {
                Line($"       01 {LengthName(variable)} PIC S9(18) COMP-5 VALUE 0.");
            }
        }
    }

    private static string Picture(SmileType type) => type switch
    {
        SmileType.Integer => "PIC S9(18) COMP-5",
        SmileType.Boolean => "PIC 9 COMP-5",
        _ => $"PIC X({TextCapacity})"
    };

    private static string DefaultClause(SmileType type) => type is SmileType.String
        ? "VALUE SPACES"
        : "VALUE 0";

    private void AppendBody(string body)
    {
        _builder.Append(body);
    }

    private void Line(string text = "") => _builder.AppendLine(text);

    private string Name(VariableSymbol variable) => _identifiers.Get(variable);

    private string RoutineName(RoutineSymbol routine) => _identifiers.Get(routine);

    private string ArrayElementName(VariableSymbol array) => Name(array) + "-ITEM";

    private string LengthName(VariableSymbol variable) => Name(variable) + "-LENGTH";

    private string ArrayLengthElementName(VariableSymbol array) => Name(array) + "-LENGTH-ITEM";

    private static string LengthName(Temporary temporary) => temporary.Name + "-LENGTH";

    private string Literal(SmileValue value) => value.Type switch
    {
        SmileType.Integer => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
        SmileType.Boolean => value.BooleanValue ? "1" : "0",
        _ => TargetEscapes.CobolString(value.StringValue)
    };

    private string ExpressionName(VariableSymbol variable) => variable.IsConstant && _constants.TryGetValue(variable, out SmileValue value)
        ? Literal(value)
        : Name(variable);

    private bool IsEmptyTextConstant(VariableSymbol variable) =>
        variable.IsConstant &&
        _constants.TryGetValue(variable, out SmileValue value) &&
        value.Type is SmileType.String &&
        value.StringValue.Length == 0;

    private static IEnumerable<BoundStatement> StructuredStatements(IReadOnlyList<BoundSourceItem> items)
    {
        foreach (BoundSourceItem item in items)
        {
            if (item is not BoundStatement statement)
            {
                continue;
            }

            yield return statement;
            switch (statement)
            {
                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        foreach (BoundStatement nested in StructuredStatements(clause.SourceItems)) yield return nested;
                    }

                    foreach (BoundStatement nested in StructuredStatements(conditional.ElseSourceItems)) yield return nested;
                    break;
                case BoundForStatement loop:
                    foreach (BoundStatement nested in StructuredStatements(loop.SourceItems)) yield return nested;
                    break;
                case BoundDoStatement loop:
                    foreach (BoundStatement nested in StructuredStatements(loop.SourceItems)) yield return nested;
                    break;
                case BoundSelectStatement select:
                    foreach (BoundSelectCaseClause clause in select.Cases)
                    {
                        foreach (BoundStatement nested in StructuredStatements(clause.SourceItems)) yield return nested;
                    }

                    break;
            }
        }
    }

    private sealed record Temporary(string Name, SmileType Type);

    private sealed record PreparedArrayElement(string Value, string? Length);

    private sealed record ProcedurePlan(
        string Body,
        IReadOnlyList<Temporary> Temporaries,
        bool NeedsDisplayNumber);

    private sealed record LoopFrame(BoundExitKind Kind, Temporary ExitFlag);

    private sealed class ProcedureEmitter
    {
        private readonly CobolWriter _owner;
        private readonly IReadOnlyList<BoundSourceItem> _items;
        private readonly BoundRoutineDeclaration? _routine;
        private readonly StringBuilder _body = new();
        private readonly List<Temporary> _temporaries = new();
        private readonly List<LoopFrame> _loops = new();
        private readonly Dictionary<BoundExpression, string> _preparedTextLengths = new();
        private int _tempId;
        private int _loopId;
        private bool _needsDisplayNumber;

        public ProcedureEmitter(
            CobolWriter owner,
            IReadOnlyList<BoundSourceItem> items,
            BoundRoutineDeclaration? routine)
        {
            _owner = owner;
            _items = items;
            _routine = routine;
        }

        public ProcedurePlan Write()
        {
            WriteItems(_items, 0);
            return new ProcedurePlan(_body.ToString(), _temporaries, _needsDisplayNumber);
        }

        private bool WriteItems(IReadOnlyList<BoundSourceItem> items, int indent)
        {
            foreach (BoundSourceItem item in items)
            {
                switch (item)
                {
                    case BoundBlankLine:
                        Line();
                        break;
                    case BoundFullLineComment comment:
                        Line(indent, "*>" + comment.Payload);
                        break;
                    case BoundDimStatement or BoundConstStatement:
                        break;
                    case BoundSetStatement set:
                    {
                        string value = PrepareExpression(set.Value, indent);
                        Assign(
                            _owner.Name(set.Variable),
                            set.Variable.Type,
                            set.Value,
                            value,
                            indent,
                            set.Variable.Type is SmileType.String ? _owner.LengthName(set.Variable) : null);
                        break;
                    }
                    case BoundArraySetStatement set:
                    {
                        PreparedArrayElement target = PrepareArrayElement(set.Array, set.Indices, indent);
                        string value = PrepareExpression(set.Value, indent);
                        Assign(target.Value, set.Array.Type, set.Value, value, indent, target.Length);
                        break;
                    }
                    case BoundGetKeyStatement getKey:
                        Line(indent, "CALL \"smile_get_key_cobol\" RETURNING " + _owner.Name(getKey.Target));
                        break;
                    case BoundClearScreenStatement:
                        Line(indent, "CALL \"smile_clear_screen_cobol\"");
                        break;
                    case BoundWaitStatement wait:
                    {
                        string duration = PrepareExpression(wait.Duration, indent);
                        Temporary captured = NewTemporary(SmileType.Integer);
                        Assign(captured.Name, SmileType.Integer, wait.Duration, duration, indent);
                        Line(indent, $"CALL \"smile_wait_cobol\" USING BY REFERENCE {captured.Name}");
                        break;
                    }
                    case BoundRandomStatement random:
                    {
                        string lower = PrepareExpression(random.LowerBound, indent);
                        Temporary capturedLower = NewTemporary(SmileType.Integer);
                        Assign(capturedLower.Name, SmileType.Integer, random.LowerBound, lower, indent);
                        string upper = PrepareExpression(random.UpperBound, indent);
                        Temporary capturedUpper = NewTemporary(SmileType.Integer);
                        Assign(capturedUpper.Name, SmileType.Integer, random.UpperBound, upper, indent);
                        Line(indent, $"CALL \"smile_random_cobol\" USING BY REFERENCE {capturedLower.Name} BY REFERENCE {capturedUpper.Name} RETURNING {_owner.Name(random.Target)}");
                        break;
                    }
                    case BoundCallStatement call:
                        EmitCall(call.Routine, call.Arguments, indent, resultTarget: null);
                        break;
                    case BoundReturnStatement returnStatement:
                        if (returnStatement.Value is not null)
                        {
                            string value = PrepareExpression(returnStatement.Value, indent);
                            Assign(
                                "SMILE-RETURN-VALUE",
                                returnStatement.Value.Type,
                                returnStatement.Value,
                                value,
                                indent,
                                returnStatement.Value.Type is SmileType.String ? "SMILE-RETURN-LENGTH" : null);
                        }

                        Line(indent, "GOBACK");
                        return true;
                    case BoundCorePrintStatement print:
                        WritePrint(print, indent);
                        break;
                    case BoundIfStatement conditional:
                        WriteIf(conditional, 0, indent);
                        break;
                    case BoundSelectStatement select:
                        WriteSelect(select, indent);
                        break;
                    case BoundForStatement loop:
                        WriteFor(loop, indent);
                        break;
                    case BoundDoStatement loop:
                        WriteDo(loop, indent);
                        break;
                    case BoundExitStatement exit:
                        WriteExit(exit, indent);
                        return true;
                    case BoundEndProgramStatement:
                        Line(indent, "MOVE 0 TO RETURN-CODE");
                        Line(indent, "STOP RUN");
                        return true;
                }
            }

            return false;
        }

        private void WritePrint(BoundCorePrintStatement print, int indent)
        {
            foreach (BoundExpression expression in print.Values)
            {
                string value = PrepareExpression(expression, indent);
                switch (expression.Type)
                {
                    case SmileType.Integer:
                        _needsDisplayNumber = true;
                        Temporary displayValue = NewTemporary(SmileType.Integer);
                        Assign(displayValue.Name, SmileType.Integer, expression, value, indent);
                        Line(indent, $"MOVE {displayValue.Name} TO SMILE-DISPLAY-NUMBER");
                        Line(indent, "DISPLAY FUNCTION TRIM(SMILE-DISPLAY-NUMBER) WITH NO ADVANCING");
                        break;
                    case SmileType.Boolean:
                        Line(indent, $"IF {Condition(expression, value)}");
                        Line(indent + 1, "DISPLAY \"True\" WITH NO ADVANCING");
                        Line(indent, "ELSE");
                        Line(indent + 1, "DISPLAY \"False\" WITH NO ADVANCING");
                        Line(indent, "END-IF");
                        break;
                    default:
                        if (IsEmptyTextExpression(expression))
                        {
                            break;
                        }

                        if (expression is BoundStringLiteralExpression or
                            BoundVariableExpression { Variable.IsConstant: true })
                        {
                            Line(indent, $"DISPLAY {value} WITH NO ADVANCING");
                        }
                        else
                        {
                            string length = TextLength(expression, value);
                            Line(indent, $"IF {length} > 0");
                            Line(indent + 1, $"DISPLAY {value}(1:{length}) WITH NO ADVANCING");
                            Line(indent, "END-IF");
                        }
                        break;
                }
            }

            if (!print.SuppressNewLine)
            {
                Line(indent, "DISPLAY X\"0A\" WITH NO ADVANCING");
            }
        }

        private void WriteIf(BoundIfStatement conditional, int clauseIndex, int indent)
        {
            if (clauseIndex >= conditional.Clauses.Count)
            {
                if (conditional.HasElseClause)
                {
                    WriteItems(conditional.ElseSourceItems, indent);
                }

                return;
            }

            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = PrepareExpression(clause.Condition, indent);
            Line(indent, $"IF {Condition(clause.Condition, condition)}");
            WriteItems(clause.SourceItems, indent + 1);
            if (clauseIndex + 1 < conditional.Clauses.Count || conditional.HasElseClause)
            {
                Line(indent, "ELSE");
                WriteIf(conditional, clauseIndex + 1, indent + 1);
            }

            Line(indent, "END-IF");
        }

        private void WriteSelect(BoundSelectStatement select, int indent)
        {
            string selector = PrepareExpression(select.Selector, indent);
            Temporary captured = NewTemporary(select.Selector.Type);
            Assign(
                captured.Name,
                select.Selector.Type,
                select.Selector,
                selector,
                indent,
                select.Selector.Type is SmileType.String ? LengthName(captured) : null);

            if (select.Selector.Type is SmileType.String)
            {
                WriteTextSelectCases(select.Cases, 0, captured, indent);
                return;
            }

            Line(indent, $"EVALUATE {captured.Name}");
            foreach (BoundSelectCaseClause clause in select.Cases)
            {
                Line(indent, clause.IsElse
                    ? "WHEN OTHER"
                    : $"WHEN {_owner.Literal(clause.Value!.Value)}");
                WriteItems(clause.SourceItems, indent + 1);
            }

            Line(indent, "END-EVALUATE");
        }

        private void WriteTextSelectCases(
            IReadOnlyList<BoundSelectCaseClause> clauses,
            int clauseIndex,
            Temporary selector,
            int indent)
        {
            if (clauseIndex >= clauses.Count)
            {
                return;
            }

            BoundSelectCaseClause clause = clauses[clauseIndex];
            if (clause.IsElse)
            {
                WriteItems(clause.SourceItems, indent);
                return;
            }

            SmileValue value = clause.Value!.Value;
            if (value.StringValue.Length == 0)
            {
                Line(indent, $"IF {LengthName(selector)} = 0");
                WriteItems(clause.SourceItems, indent + 1);
                if (clauseIndex + 1 < clauses.Count)
                {
                    Line(indent, "ELSE");
                    WriteTextSelectCases(clauses, clauseIndex + 1, selector, indent + 1);
                }
                Line(indent, "END-IF");
                return;
            }

            string caseValue = _owner.Literal(value);
            Temporary equal = NewTemporary(SmileType.Boolean);
            Line(indent, $"MOVE 0 TO {equal.Name}");
            Line(indent, $"IF {LengthName(selector)} = FUNCTION LENGTH({caseValue})");
            Line(indent + 1, $"IF {LengthName(selector)} = 0");
            Line(indent + 2, $"MOVE 1 TO {equal.Name}");
            Line(indent + 1, "ELSE");
            Line(indent + 2, $"IF {selector.Name}(1:{LengthName(selector)}) = {caseValue}");
            Line(indent + 3, $"MOVE 1 TO {equal.Name}");
            Line(indent + 2, "END-IF");
            Line(indent + 1, "END-IF");
            Line(indent, "END-IF");
            Line(indent, $"IF {equal.Name} = 1");
            WriteItems(clause.SourceItems, indent + 1);
            if (clauseIndex + 1 < clauses.Count)
            {
                Line(indent, "ELSE");
                WriteTextSelectCases(clauses, clauseIndex + 1, selector, indent + 1);
            }
            Line(indent, "END-IF");
        }

        private void WriteFor(BoundForStatement loop, int indent)
        {
            string lower = PrepareExpression(loop.LowerBound, indent);
            Temporary lowerTemp = NewTemporary(SmileType.Integer);
            Assign(lowerTemp.Name, SmileType.Integer, loop.LowerBound, lower, indent);
            string upper = PrepareExpression(loop.UpperBound, indent);
            Temporary upperTemp = NewTemporary(SmileType.Integer);
            Assign(upperTemp.Name, SmileType.Integer, loop.UpperBound, upper, indent);

            _loopId++;
            Temporary exitFlag = NewTemporary(SmileType.Boolean);
            Line(indent, $"MOVE 0 TO {exitFlag.Name}");
            _loops.Add(new LoopFrame(BoundExitKind.For, exitFlag));
            string counter = _owner.Name(loop.Counter);
            Line(indent, $"PERFORM VARYING {counter} FROM {lowerTemp.Name} BY {(loop.IsDescending ? "-1" : "1")} UNTIL {counter} {(loop.IsDescending ? "<" : ">")} {upperTemp.Name}");
            if (!loop.SourceItems.OfType<BoundStatement>().Any())
            {
                Line(indent + 1, "CONTINUE");
            }
            else
            {
                WriteItems(loop.SourceItems, indent + 1);
            }
            Line(indent, "END-PERFORM");
            _loops.RemoveAt(_loops.Count - 1);
            PropagateOuterExit(indent);
        }

        private void WriteDo(BoundDoStatement loop, int indent)
        {
            _loopId++;
            Temporary exitFlag = NewTemporary(SmileType.Boolean);
            Line(indent, $"MOVE 0 TO {exitFlag.Name}");
            _loops.Add(new LoopFrame(BoundExitKind.Do, exitFlag));
            bool inlineCondition = loop.UntilCondition is not null && CanInlineCondition(loop.UntilCondition);
            string? renderedCondition = inlineCondition
                ? PrepareExpression(loop.UntilCondition!, indent)
                : null;
            Line(indent, inlineCondition
                ? $"PERFORM WITH TEST AFTER UNTIL {Condition(loop.UntilCondition!, renderedCondition!)}"
                : "PERFORM UNTIL 1 = 0");
            if (!loop.SourceItems.OfType<BoundStatement>().Any())
            {
                Line(indent + 1, "CONTINUE");
            }
            else
            {
                WriteItems(loop.SourceItems, indent + 1);
            }

            if (loop.UntilCondition is not null && !inlineCondition)
            {
                string condition = PrepareExpression(loop.UntilCondition, indent + 1);
                Line(indent + 1, $"IF {Condition(loop.UntilCondition, condition)}");
                Line(indent + 2, "EXIT PERFORM");
                Line(indent + 1, "END-IF");
            }

            Line(indent, "END-PERFORM");
            _loops.RemoveAt(_loops.Count - 1);
            PropagateOuterExit(indent);
        }

        private void WriteExit(BoundExitStatement exit, int indent)
        {
            LoopFrame? target = _loops.LastOrDefault(loop => loop.Kind == exit.Kind);
            if (target is null)
            {
                return;
            }

            Line(indent, $"MOVE 1 TO {target.ExitFlag.Name}");
            Line(indent, "EXIT PERFORM");
        }

        private void PropagateOuterExit(int indent)
        {
            if (_loops.Count == 0)
            {
                return;
            }

            foreach (LoopFrame outer in _loops)
            {
                Line(indent, $"IF {outer.ExitFlag.Name} = 1");
                Line(indent + 1, "EXIT PERFORM");
                Line(indent, "END-IF");
            }
        }

        private static bool CanInlineCondition(BoundExpression expression) => expression switch
        {
            BoundStringLiteralExpression or BoundIntegerLiteralExpression or BoundBooleanLiteralExpression or BoundVariableExpression => true,
            BoundUnaryExpression unary => CanInlineCondition(unary.Operand),
            BoundBinaryExpression binary when binary.Operator.Kind is
                BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr => false,
            BoundBinaryExpression binary => CanInlineCondition(binary.Left) && CanInlineCondition(binary.Right),
            _ => false
        };

        private string PrepareExpression(BoundExpression expression, int indent)
        {
            switch (expression)
            {
                case BoundStringLiteralExpression text:
                    return TargetEscapes.CobolString(text.Value);
                case BoundIntegerLiteralExpression number:
                    return number.Value.ToString(CultureInfo.InvariantCulture);
                case BoundBooleanLiteralExpression boolean:
                    return boolean.Value ? "1" : "0";
                case BoundVariableExpression variable:
                    return _owner.ExpressionName(variable.Variable);
                case BoundArrayExpression array:
                {
                    PreparedArrayElement prepared = PrepareArrayElement(array.Array, array.Indices, indent);
                    if (prepared.Length is not null)
                    {
                        _preparedTextLengths[array] = prepared.Length;
                    }
                    return prepared.Value;
                }
                case BoundCallExpression call:
                {
                    Temporary result = NewTemporary(call.Type);
                    EmitCall(call.Routine, call.Arguments, indent, result.Name);
                    if (call.Type is SmileType.String)
                    {
                        _preparedTextLengths[call] = LengthName(result);
                    }
                    return result.Name;
                }
                case BoundIntrinsicExpression intrinsic:
                    return PrepareIntrinsic(intrinsic, indent);
                case BoundUnaryExpression unary:
                {
                    string operand = PrepareExpression(unary.Operand, indent);
                    return unary.Operator.Kind switch
                    {
                        BoundUnaryOperatorKind.Identity => operand,
                        BoundUnaryOperatorKind.Negation => $"(-{operand})",
                        BoundUnaryOperatorKind.LogicalNegation => $"(NOT {Condition(unary.Operand, operand)})",
                        _ => operand
                    };
                }
                case BoundBinaryExpression binary:
                {
                    string left = PrepareExpression(binary.Left, indent);
                    if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr)
                    {
                        Temporary result = NewTemporary(SmileType.Boolean);
                        Assign(result.Name, SmileType.Boolean, binary.Left, left, indent);
                        Line(indent, binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                            ? $"IF {result.Name} = 1"
                            : $"IF {result.Name} = 0");
                        string conditionalRight = PrepareExpression(binary.Right, indent + 1);
                        Assign(result.Name, SmileType.Boolean, binary.Right, conditionalRight, indent + 1);
                        Line(indent, "END-IF");
                        return result.Name;
                    }

                    string right = PrepareExpression(binary.Right, indent);
                    if (binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
                    {
                        return PrepareStringConcatenation(binary, left, right, indent);
                    }

                    if (binary.Left.Type is SmileType.String && binary.Operator.Kind is
                        BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
                    {
                        return PrepareStringComparison(binary, left, right, indent);
                    }

                    if (binary.Operator.Kind is BoundBinaryOperatorKind.Modulo)
                    {
                        return $"FUNCTION MOD({left}, {right})";
                    }

                    string op = binary.Operator.Kind switch
                    {
                        BoundBinaryOperatorKind.Addition => "+",
                        BoundBinaryOperatorKind.Subtraction => "-",
                        BoundBinaryOperatorKind.Multiplication => "*",
                        BoundBinaryOperatorKind.Division => "/",
                        BoundBinaryOperatorKind.Equality => "=",
                        BoundBinaryOperatorKind.Inequality => "NOT =",
                        BoundBinaryOperatorKind.Less => "<",
                        BoundBinaryOperatorKind.LessOrEquals => "<=",
                        BoundBinaryOperatorKind.Greater => ">",
                        BoundBinaryOperatorKind.GreaterOrEquals => ">=",
                        BoundBinaryOperatorKind.LogicalAnd => "AND",
                        BoundBinaryOperatorKind.LogicalOr => "OR",
                        _ => "="
                    };
                    string leftText = binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr
                        ? Condition(binary.Left, left)
                        : left;
                    string rightText = binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr
                        ? Condition(binary.Right, right)
                        : right;
                    return $"({leftText} {op} {rightText})";
                }
                default:
                    return "0";
            }
        }

        private PreparedArrayElement PrepareArrayElement(
            VariableSymbol array,
            IReadOnlyList<BoundExpression> indexExpressions,
            int indent)
        {
            var checkedIndices = new List<Temporary>(indexExpressions.Count);
            for (int dimension = 0; dimension < indexExpressions.Count; dimension++)
            {
                BoundExpression indexExpression = indexExpressions[dimension];
                string index = PrepareExpression(indexExpression, indent);
                Temporary checkedIndex = NewTemporary(SmileType.Integer);
                Assign(checkedIndex.Name, SmileType.Integer, indexExpression, index, indent);
                checkedIndices.Add(checkedIndex);
            }

            for (int dimension = 0; dimension < checkedIndices.Count; dimension++)
            {
                Temporary checkedIndex = checkedIndices[dimension];
                int length = dimension == 0 ? array.ArrayLength : array.ArraySecondLength;
                Line(indent, $"IF {checkedIndex.Name} < 0 OR {checkedIndex.Name} >= {length}");
                Line(indent + 1, $"DISPLAY \"SMILE Runtime Error SMILER1210: Array index is outside the bounds of '{array.Name}'.\" UPON STDERR");
                Line(indent + 1, "STOP RUN RETURNING 1");
                Line(indent, "END-IF");
            }

            foreach (Temporary checkedIndex in checkedIndices)
            {
                Line(indent, $"ADD 1 TO {checkedIndex.Name}");
            }
            string indices = string.Join(", ", checkedIndices.Select(item => item.Name));
            return new PreparedArrayElement(
                $"{_owner.ArrayElementName(array)}({indices})",
                array.Type is SmileType.String
                    ? $"{_owner.ArrayLengthElementName(array)}({indices})"
                    : null);
        }

        private string PrepareStringConcatenation(
            BoundBinaryExpression binary,
            string left,
            string right,
            int indent)
        {
            Temporary capturedLeft = NewTemporary(SmileType.String);
            Assign(capturedLeft.Name, SmileType.String, binary.Left, left, indent, LengthName(capturedLeft));
            Temporary capturedRight = NewTemporary(SmileType.String);
            Assign(capturedRight.Name, SmileType.String, binary.Right, right, indent, LengthName(capturedRight));

            Temporary result = NewTemporary(SmileType.String);
            Temporary leftCopyLength = NewTemporary(SmileType.Integer);
            Temporary rightCopyLength = NewTemporary(SmileType.Integer);
            Temporary remainingLength = NewTemporary(SmileType.Integer);
            Temporary rightOffset = NewTemporary(SmileType.Integer);

            Line(indent, $"MOVE SPACES TO {result.Name}");
            Line(indent, $"COMPUTE {leftCopyLength.Name} = {LengthName(capturedLeft)}");
            Line(indent, $"IF {leftCopyLength.Name} > {TextCapacity}");
            Line(indent + 1, $"MOVE {TextCapacity} TO {leftCopyLength.Name}");
            Line(indent, "END-IF");
            Line(indent, $"IF {leftCopyLength.Name} > 0");
            Line(indent + 1, $"MOVE {capturedLeft.Name}(1:{leftCopyLength.Name}) TO {result.Name}(1:{leftCopyLength.Name})");
            Line(indent, "END-IF");

            Line(indent, $"COMPUTE {remainingLength.Name} = {TextCapacity} - {leftCopyLength.Name}");
            Line(indent, $"COMPUTE {rightCopyLength.Name} = {LengthName(capturedRight)}");
            Line(indent, $"IF {rightCopyLength.Name} > {remainingLength.Name}");
            Line(indent + 1, $"MOVE {remainingLength.Name} TO {rightCopyLength.Name}");
            Line(indent, "END-IF");
            Line(indent, $"IF {rightCopyLength.Name} > 0");
            Line(indent + 1, $"COMPUTE {rightOffset.Name} = {leftCopyLength.Name} + 1");
            Line(indent + 1, $"MOVE {capturedRight.Name}(1:{rightCopyLength.Name}) TO {result.Name}({rightOffset.Name}:{rightCopyLength.Name})");
            Line(indent, "END-IF");
            Line(indent, $"COMPUTE {LengthName(result)} = {leftCopyLength.Name} + {rightCopyLength.Name}");

            _preparedTextLengths[binary] = LengthName(result);
            return result.Name;
        }

        private string PrepareStringComparison(
            BoundBinaryExpression binary,
            string left,
            string right,
            int indent)
        {
            Temporary capturedLeft = NewTemporary(SmileType.String);
            Assign(capturedLeft.Name, SmileType.String, binary.Left, left, indent, LengthName(capturedLeft));
            Temporary capturedRight = NewTemporary(SmileType.String);
            Assign(capturedRight.Name, SmileType.String, binary.Right, right, indent, LengthName(capturedRight));
            Temporary equal = NewTemporary(SmileType.Boolean);

            Line(indent, $"MOVE 0 TO {equal.Name}");
            Line(indent, $"IF {LengthName(capturedLeft)} = {LengthName(capturedRight)}");
            Line(indent + 1, $"IF {LengthName(capturedLeft)} = 0");
            Line(indent + 2, $"MOVE 1 TO {equal.Name}");
            Line(indent + 1, "ELSE");
            Line(indent + 2, $"IF {capturedLeft.Name}(1:{LengthName(capturedLeft)}) = {capturedRight.Name}(1:{LengthName(capturedRight)})");
            Line(indent + 3, $"MOVE 1 TO {equal.Name}");
            Line(indent + 2, "END-IF");
            Line(indent + 1, "END-IF");
            Line(indent, "END-IF");

            return binary.Operator.Kind is BoundBinaryOperatorKind.Equality
                ? $"({equal.Name} = 1)"
                : $"({equal.Name} = 0)";
        }

        private string PrepareIntrinsic(BoundIntrinsicExpression intrinsic, int indent)
        {
            var arguments = new List<Temporary>(intrinsic.Arguments.Count);
            foreach (BoundExpression argument in intrinsic.Arguments)
            {
                string value = PrepareExpression(argument, indent);
                Temporary captured = NewTemporary(SmileType.Integer);
                Assign(captured.Name, SmileType.Integer, argument, value, indent);
                arguments.Add(captured);
            }

            Temporary result = NewTemporary(SmileType.Integer);
            string helper = intrinsic.Kind switch
            {
                BoundIntrinsicKind.Timer => "smile_timer_cobol",
                BoundIntrinsicKind.Abs => "smile_abs_cobol",
                BoundIntrinsicKind.Min => "smile_min_cobol",
                BoundIntrinsicKind.Max => "smile_max_cobol",
                _ => "smile_timer_cobol"
            };
            string usingClause = arguments.Count == 0
                ? string.Empty
                : " USING " + string.Join(" ", arguments.Select(item => $"BY REFERENCE {item.Name}"));
            Line(indent, $"CALL \"{helper}\"{usingClause} RETURNING {result.Name}");
            return result.Name;
        }

        private void EmitCall(
            RoutineSymbol routine,
            IReadOnlyList<BoundExpression> arguments,
            int indent,
            string? resultTarget)
        {
            var captured = new List<(VariableSymbol Parameter, Temporary Value)>();
            for (int index = 0; index < arguments.Count; index++)
            {
                BoundExpression argument = arguments[index];
                string expression = PrepareExpression(argument, indent);
                Temporary temporary = NewTemporary(argument.Type);
                Assign(
                    temporary.Name,
                    argument.Type,
                    argument,
                    expression,
                    indent,
                    argument.Type is SmileType.String ? LengthName(temporary) : null);
                captured.Add((routine.Parameters[index], temporary));
            }

            var usingItems = new List<string> { "BY REFERENCE SMILE-STATE" };
            foreach ((VariableSymbol parameter, Temporary value) in captured)
            {
                usingItems.Add($"BY REFERENCE {value.Name}");
                if (parameter.Type is SmileType.String)
                {
                    usingItems.Add($"BY REFERENCE {LengthName(value)}");
                }
            }
            if (resultTarget is not null)
            {
                usingItems.Add($"BY REFERENCE {resultTarget}");
                if (routine.ReturnType is SmileType.String)
                {
                    usingItems.Add($"BY REFERENCE {resultTarget}-LENGTH");
                }
            }

            Line(indent, $"CALL \"{_owner.RoutineName(routine)}\" USING");
            foreach (string usingItem in usingItems)
            {
                Line(indent + 1, usingItem);
            }
        }

        private void Assign(
            string target,
            SmileType type,
            BoundExpression sourceExpression,
            string expression,
            int indent,
            string? textTargetLength = null)
        {
            switch (type)
            {
                case SmileType.Boolean:
                    Line(indent, $"IF {Condition(sourceExpression, expression)}");
                    Line(indent + 1, $"MOVE 1 TO {target}");
                    Line(indent, "ELSE");
                    Line(indent + 1, $"MOVE 0 TO {target}");
                    Line(indent, "END-IF");
                    break;
                case SmileType.Integer:
                    Line(indent, $"COMPUTE {target} = {expression}");
                    break;
                default:
                    if (textTargetLength is null)
                    {
                        throw new InvalidOperationException("COBOL Text assignments require logical-length storage.");
                    }

                    if (IsEmptyTextExpression(sourceExpression))
                    {
                        Line(indent, $"MOVE SPACES TO {target}");
                        Line(indent, $"MOVE 0 TO {textTargetLength}");
                        break;
                    }

                    Line(indent, $"MOVE {expression} TO {target}");
                    Line(indent, $"COMPUTE {textTargetLength} = {TextLength(sourceExpression, expression)}");
                    Line(indent, $"IF {textTargetLength} > {TextCapacity}");
                    Line(indent + 1, $"MOVE {TextCapacity} TO {textTargetLength}");
                    Line(indent, "END-IF");
                    break;
            }
        }

        private string TextLength(BoundExpression expression, string rendered)
        {
            if (_preparedTextLengths.TryGetValue(expression, out string? preparedLength))
            {
                return preparedLength;
            }

            return expression switch
            {
                BoundStringLiteralExpression { Value.Length: 0 } => "0",
                BoundStringLiteralExpression => $"FUNCTION LENGTH({rendered})",
                BoundVariableExpression variable when _owner.IsEmptyTextConstant(variable.Variable) => "0",
                BoundVariableExpression { Variable.IsConstant: true } => $"FUNCTION LENGTH({rendered})",
                BoundVariableExpression variable => _owner.LengthName(variable.Variable),
                _ => throw new InvalidOperationException(
                    $"COBOL Text expression '{expression.GetType().Name}' has no logical length.")
            };
        }

        private bool IsEmptyTextExpression(BoundExpression expression) => expression switch
        {
            BoundStringLiteralExpression { Value.Length: 0 } => true,
            BoundVariableExpression variable => _owner.IsEmptyTextConstant(variable.Variable),
            _ => false
        };

        private static string Condition(BoundExpression expression, string rendered) => expression switch
        {
            BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr } =>
                $"{rendered} = 1",
            BoundBinaryExpression { Type: SmileType.Boolean } => rendered,
            BoundUnaryExpression { Operator.Kind: BoundUnaryOperatorKind.LogicalNegation } => rendered,
            BoundBooleanLiteralExpression boolean => boolean.Value ? "1 = 1" : "1 = 0",
            _ => $"{rendered} = 1"
        };

        private Temporary NewTemporary(SmileType type)
        {
            var temporary = new Temporary($"SMILE-TEMP-{++_tempId}", type);
            _temporaries.Add(temporary);
            return temporary;
        }

        private void Line(int indent = 0, string text = "")
        {
            if (text.Length == 0)
            {
                _body.AppendLine();
                return;
            }

            _body.Append("           ");
            _body.Append(' ', indent * 4);
            _body.AppendLine(text);
        }
    }
}
