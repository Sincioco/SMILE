using System.Globalization;

namespace SMILE.Engine;

// This is the sole source-language binder. It performs deliberate declaration
// and body passes so every evaluator and target receives the same symbols,
// scopes, exact scalar types, call order, and control-flow tree.
internal sealed class Binder
{
    private readonly Dictionary<string, TextSpan> _programDeclarations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DimStatementSyntax> _globalDimSyntax = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConstStatementSyntax> _constantSyntax = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BoundConstStatement> _resolvedConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resolvingConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RoutineDeclarationSyntax> _routineSyntax = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RoutineSymbol> _routineSymbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, VariableSymbol> _globals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VariableSymbol, SmileValue> _constantValues = new();
    private readonly List<BoundRoutineDeclaration> _boundRoutines = new();
    private readonly List<Diagnostic> _diagnostics = new();

    private Dictionary<string, VariableSymbol>? _locals;
    private RoutineSymbol? _currentRoutine;
    private bool _optionExplicit;
    private int _forDepth;
    private int _doDepth;

    public BindResult Bind(SmileProgramSyntax syntax)
    {
        ValidateOptionExplicit(syntax.SourceItems);
        InventoryProgramDeclarations(syntax.SourceItems);

        foreach (string name in _constantSyntax.Keys.ToArray())
        {
            ResolveConstant(name);
        }

        DeclareGlobalDimensions(arrays: false);
        DeclareGlobalDimensions(arrays: true);
        BuildRoutineSignatures();

        IReadOnlyList<BoundSourceItem> topLevel = BindItems(syntax.SourceItems, directProgramLevel: true);
        foreach (RoutineDeclarationSyntax declaration in _routineSyntax.Values.OrderBy(item => item.Span.Start))
        {
            BindRoutine(declaration);
        }

        return new BindResult(
            new BoundProgram(
                topLevel,
                _globals.Values.OrderBy(symbol => symbol.DeclarationSpan.Start).ToArray(),
                _boundRoutines,
                _optionExplicit),
            _diagnostics);
    }

    private void ValidateOptionExplicit(IReadOnlyList<SourceItemSyntax> items)
    {
        SourceItemSyntax? firstMeaningful = items.FirstOrDefault(item => item is not BlankLineSyntax and not FullLineCommentSyntax);
        int count = 0;
        foreach (OptionExplicitStatementSyntax option in items.OfType<OptionExplicitStatementSyntax>())
        {
            count++;
            _optionExplicit = true;
            if (!ReferenceEquals(option, firstMeaningful))
            {
                Report("SMILE2117", "Option Explicit must be the first nonblank, noncomment source item.", option.Span);
            }

            if (count > 1)
            {
                Report("SMILE2118", "Option Explicit may appear at most once.", option.Span);
            }
        }
    }

    private void InventoryProgramDeclarations(IReadOnlyList<SourceItemSyntax> items)
    {
        foreach (SourceItemSyntax item in items)
        {
            switch (item)
            {
                case ConstStatementSyntax constant:
                    if (ReserveProgramName(constant.Name, constant.NameSpan))
                    {
                        _constantSyntax.Add(constant.Name, constant);
                    }

                    break;
                case DimStatementSyntax dim:
                    InventoryGlobalDim(dim);
                    break;
                case RoutineDeclarationSyntax routine:
                    if (ReserveProgramName(routine.Name, routine.NameSpan))
                    {
                        _routineSyntax.Add(routine.Name, routine);
                    }

                    break;
                case StatementSyntax statement:
                    InventoryGlobalDimsInStatement(statement);
                    break;
            }
        }
    }

    private void InventoryGlobalDimsInStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case IfStatementSyntax conditional:
                foreach (ConditionalClauseSyntax clause in conditional.Clauses)
                {
                    InventoryGlobalDimsInItems(clause.SourceItems);
                }

                InventoryGlobalDimsInItems(conditional.ElseSourceItems);
                break;
            case ForStatementSyntax loop:
                InventoryGlobalDimsInItems(loop.SourceItems);
                break;
            case DoStatementSyntax loop:
                InventoryGlobalDimsInItems(loop.SourceItems);
                break;
            case SelectStatementSyntax select:
                foreach (SelectCaseClauseSyntax clause in select.Cases)
                {
                    InventoryGlobalDimsInItems(clause.SourceItems);
                }

                break;
        }
    }

    private void InventoryGlobalDimsInItems(IReadOnlyList<SourceItemSyntax> items)
    {
        foreach (SourceItemSyntax item in items)
        {
            if (item is DimStatementSyntax dim)
            {
                InventoryGlobalDim(dim);
            }
            else if (item is StatementSyntax statement and not RoutineDeclarationSyntax)
            {
                InventoryGlobalDimsInStatement(statement);
            }
        }
    }

    private void InventoryGlobalDim(DimStatementSyntax dim)
    {
        if (ReserveProgramName(dim.Name, dim.NameSpan))
        {
            _globalDimSyntax.Add(dim.Name, dim);
        }
    }

    private bool ReserveProgramName(string name, TextSpan span)
    {
        if (_programDeclarations.ContainsKey(name))
        {
            Report("SMILE2101", $"'{name}' is already declared in the program namespace.", span);
            return false;
        }

        _programDeclarations.Add(name, span);
        return true;
    }

    private void DeclareGlobalDimensions(bool arrays)
    {
        foreach (DimStatementSyntax dim in _globalDimSyntax.Values
                     .Where(item => item.IsArray == arrays)
                     .OrderBy(item => item.Span.Start))
        {
            int length = dim.IsArray ? ResolveArrayLength(dim) : 0;
            _globals[dim.Name] = new VariableSymbol(
                dim.Name,
                dim.NameSpan,
                dim.DeclaredType,
                IsConstant: false,
                RoutineName: null,
                ArrayLength: length);
        }
    }

    private void BuildRoutineSignatures()
    {
        foreach (RoutineDeclarationSyntax declaration in _routineSyntax.Values.OrderBy(item => item.Span.Start))
        {
            var parameters = new List<VariableSymbol>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ParameterSyntax parameter in declaration.Parameters)
            {
                if (!names.Add(parameter.Name))
                {
                    Report("SMILE2119", $"Parameter '{parameter.Name}' is already declared in routine '{declaration.Name}'.", parameter.NameSpan);
                    continue;
                }

                parameters.Add(new VariableSymbol(
                    parameter.Name,
                    parameter.NameSpan,
                    parameter.DeclaredType,
                    IsConstant: false,
                    RoutineName: declaration.Name,
                    ArrayLength: 0,
                    IsParameter: true));
            }

            _routineSymbols.Add(declaration.Name, new RoutineSymbol(
                declaration.Name,
                declaration.NameSpan,
                declaration.Kind,
                parameters,
                declaration.ReturnType));
        }
    }

    private BoundConstStatement? ResolveConstant(string name)
    {
        if (_resolvedConstants.TryGetValue(name, out BoundConstStatement? resolved))
        {
            return resolved;
        }

        if (!_constantSyntax.TryGetValue(name, out ConstStatementSyntax? syntax))
        {
            return null;
        }

        if (!_resolvingConstants.Add(name))
        {
            Report("SMILE2103", $"Constant '{name}' is part of a circular definition.", syntax.NameSpan);
            return null;
        }

        BoundExpression initializer = BindExpression(syntax.Initializer, constantsOnly: true);
        StaticEvaluationResult evaluation = BoundExpressionEvaluator.Evaluate(initializer, _constantValues);
        _resolvingConstants.Remove(name);
        if (initializer.Type is SmileType.Error || !evaluation.IsKnown || evaluation.MayFailAtRuntime)
        {
            if (evaluation.IsInvalid && evaluation.Error is SmileArithmeticError error)
            {
                Report(error.CompileCode, error.Message, error.Span);
            }
            else if (initializer.Type is not SmileType.Error)
            {
                Report("SMILE2104", $"Constant '{name}' requires a compile-time scalar value.", syntax.Initializer.Span);
            }

            return null;
        }

        var symbol = new VariableSymbol(name, syntax.NameSpan, initializer.Type, IsConstant: true);
        _globals[name] = symbol;
        var statement = new BoundConstStatement(symbol, initializer, evaluation.Value);
        _resolvedConstants[name] = statement;
        _constantValues[symbol] = evaluation.Value;
        return statement;
    }

    private int ResolveArrayLength(DimStatementSyntax syntax)
    {
        if (syntax.ArraySize is null)
        {
            return 0;
        }

        BoundExpression size = BindExpression(syntax.ArraySize, constantsOnly: true);
        RequireNumber(size, syntax.ArraySize.Span, "array dimension");
        StaticEvaluationResult evaluation = BoundExpressionEvaluator.Evaluate(size, _constantValues);
        if (evaluation.IsInvalid && evaluation.Error is SmileArithmeticError error)
        {
            Report(error.CompileCode, error.Message, error.Span);
            return 1;
        }

        if (!evaluation.IsKnown || evaluation.MayFailAtRuntime || size.Type is not SmileType.Integer)
        {
            if (size.Type is not SmileType.Error)
            {
                Report("SMILE2120", "An array dimension must be a compile-time Number expression.", syntax.ArraySize.Span);
            }

            return 1;
        }

        long value = evaluation.Value.IntegerValue;
        if (value <= 0)
        {
            Report("SMILE2121", "An array dimension must be positive.", syntax.ArraySize.Span);
            return 1;
        }

        if (value > int.MaxValue)
        {
            Report("SMILE2122", "The array dimension is too large for compiler-managed storage.", syntax.ArraySize.Span);
            return 1;
        }

        return (int)value;
    }

    private void BindRoutine(RoutineDeclarationSyntax declaration)
    {
        if (!_routineSymbols.TryGetValue(declaration.Name, out RoutineSymbol? routine))
        {
            return;
        }

        _currentRoutine = routine;
        _locals = new Dictionary<string, VariableSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (VariableSymbol parameter in routine.Parameters)
        {
            _locals[parameter.Name] = parameter;
        }

        InventoryLocalDimensions(declaration.SourceItems, routine.Name);
        _forDepth = 0;
        _doDepth = 0;
        IReadOnlyList<BoundSourceItem> body = BindItems(declaration.SourceItems, directProgramLevel: false);

        if (routine.IsFunction && !ItemsDefinitelyExit(body))
        {
            Report("SMILE2123", $"Function '{routine.Name}' does not return a value on every reachable normal path.", declaration.NameSpan);
        }

        _boundRoutines.Add(new BoundRoutineDeclaration(
            routine,
            body,
            _locals.Values.OrderBy(symbol => symbol.DeclarationSpan.Start).ToArray()));
        _locals = null;
        _currentRoutine = null;
    }

    private void InventoryLocalDimensions(IReadOnlyList<SourceItemSyntax> items, string routineName)
    {
        foreach (SourceItemSyntax item in items)
        {
            switch (item)
            {
                case DimStatementSyntax dim:
                    if (_locals!.ContainsKey(dim.Name))
                    {
                        Report("SMILE2124", $"Local '{dim.Name}' is already declared in routine '{routineName}'.", dim.NameSpan);
                        break;
                    }

                    _locals.Add(dim.Name, new VariableSymbol(
                        dim.Name,
                        dim.NameSpan,
                        dim.DeclaredType,
                        IsConstant: false,
                        RoutineName: routineName,
                        ArrayLength: dim.IsArray ? ResolveArrayLength(dim) : 0));
                    break;
                case IfStatementSyntax conditional:
                    foreach (ConditionalClauseSyntax clause in conditional.Clauses)
                    {
                        InventoryLocalDimensions(clause.SourceItems, routineName);
                    }

                    InventoryLocalDimensions(conditional.ElseSourceItems, routineName);
                    break;
                case ForStatementSyntax loop:
                    InventoryLocalDimensions(loop.SourceItems, routineName);
                    break;
                case DoStatementSyntax loop:
                    InventoryLocalDimensions(loop.SourceItems, routineName);
                    break;
                case SelectStatementSyntax select:
                    foreach (SelectCaseClauseSyntax clause in select.Cases)
                    {
                        InventoryLocalDimensions(clause.SourceItems, routineName);
                    }

                    break;
            }
        }
    }

    private IReadOnlyList<BoundSourceItem> BindItems(IReadOnlyList<SourceItemSyntax> items, bool directProgramLevel)
    {
        var result = new List<BoundSourceItem>();
        foreach (SourceItemSyntax item in items)
        {
            BoundSourceItem? bound = item switch
            {
                BlankLineSyntax => new BoundBlankLine(),
                FullLineCommentSyntax comment => new BoundFullLineComment(comment.Marker, comment.Payload),
                RoutineDeclarationSyntax routine when directProgramLevel => null,
                RoutineDeclarationSyntax routine => BindNestedRoutine(routine),
                OptionExplicitStatementSyntax option when directProgramLevel => null,
                OptionExplicitStatementSyntax option => BindMisplacedOption(option),
                StatementSyntax statement => BindStatement(statement, directProgramLevel),
                _ => null
            };
            if (bound is not null)
            {
                result.Add(bound);
            }
        }

        return result;
    }

    private BoundStatement? BindNestedRoutine(RoutineDeclarationSyntax syntax)
    {
        Report("SMILE2125", "Routines must be declared directly at program level and cannot be nested.", syntax.Span);
        return null;
    }

    private BoundStatement? BindMisplacedOption(OptionExplicitStatementSyntax syntax)
    {
        Report("SMILE2126", "Option Explicit is valid only as the first program-level directive.", syntax.Span);
        return null;
    }

    private BoundStatement? BindStatement(StatementSyntax statement, bool directProgramLevel) => statement switch
    {
        CoreAssignmentStatementSyntax assignment => BindAssignment(assignment),
        CoreArrayAssignmentStatementSyntax assignment => BindArrayAssignment(assignment),
        DimStatementSyntax dim => BindDim(dim),
        ConstStatementSyntax constant => directProgramLevel ? BindConst(constant) : BindLocalConst(constant),
        CallStatementSyntax call => BindCallStatement(call),
        ReturnStatementSyntax returnStatement => BindReturn(returnStatement),
        SelectStatementSyntax select => BindSelect(select),
        CorePrintStatementSyntax print => BindPrint(print),
        IfStatementSyntax conditional => BindIf(conditional),
        ForStatementSyntax loop => BindFor(loop),
        DoStatementSyntax loop => BindDo(loop),
        ExitStatementSyntax exit => BindExit(exit),
        EndProgramStatementSyntax => new BoundEndProgramStatement(),
        _ => null
    };

    private BoundStatement BindAssignment(CoreAssignmentStatementSyntax syntax)
    {
        BoundExpression value = BindExpression(syntax.Value);
        VariableSymbol variable = ResolveAssignmentTarget(syntax.Name, syntax.NameSpan, value.Type);
        if (variable.IsArray)
        {
            Report("SMILE2127", $"Array '{variable.Name}' requires an index.", syntax.NameSpan);
        }
        else if (variable.IsConstant)
        {
            Report("SMILE2105", $"Constant '{variable.Name}' cannot be assigned.", syntax.NameSpan);
        }
        else if (value.Type is not SmileType.Error && variable.Type != value.Type)
        {
            Report(
                "SMILE2106",
                $"Cannot assign {DisplayType(value.Type)} to {DisplayType(variable.Type)} variable '{variable.Name}'.",
                syntax.Value.Span);
        }

        return new BoundSetStatement(variable, value);
    }

    private VariableSymbol ResolveAssignmentTarget(string name, TextSpan span, SmileType inferredType)
    {
        VariableSymbol? existing = LookupVariable(name, span, reportUnknown: false);
        if (existing is not null)
        {
            return existing;
        }

        if (_programDeclarations.ContainsKey(name))
        {
            Report("SMILE2128", $"'{name}' names a routine and cannot be used as a variable.", span);
            return ErrorVariable(name, span, inferredType);
        }

        if (_optionExplicit)
        {
            Report("SMILE2129", $"Variable '{name}' must be declared because Option Explicit is enabled.", span);
            return ErrorVariable(name, span, inferredType);
        }

        SmileType type = inferredType is SmileType.Error ? SmileType.Integer : inferredType;
        var variable = new VariableSymbol(
            name,
            span,
            type,
            IsConstant: false,
            RoutineName: _currentRoutine?.Name);
        if (_currentRoutine is null)
        {
            _globals[name] = variable;
            _programDeclarations[name] = span;
        }
        else
        {
            _locals![name] = variable;
        }

        return variable;
    }

    private BoundStatement BindArrayAssignment(CoreArrayAssignmentStatementSyntax syntax)
    {
        VariableSymbol array = ResolveArray(syntax.Name, syntax.NameSpan);
        BoundExpression index = BindExpression(syntax.Index);
        ValidateIndex(array, index, syntax.Index.Span);
        BoundExpression value = BindExpression(syntax.Value);
        if (value.Type is not SmileType.Error && array.Type != value.Type)
        {
            Report("SMILE2130", $"Cannot assign {DisplayType(value.Type)} to {DisplayType(array.Type)} array '{array.Name}'.", syntax.Value.Span);
        }

        return new BoundArraySetStatement(array, index, value);
    }

    private BoundStatement? BindDim(DimStatementSyntax syntax)
    {
        VariableSymbol? variable = LookupVariable(syntax.Name, syntax.NameSpan, reportUnknown: false, checkFutureLocal: false);
        return variable is null ? null : new BoundDimStatement(variable);
    }

    private BoundStatement? BindConst(ConstStatementSyntax syntax) => ResolveConstant(syntax.Name);

    private BoundStatement? BindLocalConst(ConstStatementSyntax syntax)
    {
        Report("SMILE2102", "Const declarations are allowed only directly at program level.", syntax.Span);
        return null;
    }

    private BoundStatement BindCallStatement(CallStatementSyntax syntax)
    {
        RoutineSymbol routine = ResolveRoutine(syntax.Name, syntax.NameSpan, expectedFunction: false);
        IReadOnlyList<BoundExpression> arguments = BindCallArguments(routine, syntax.Arguments, syntax.NameSpan);
        if (routine.IsFunction)
        {
            Report("SMILE2131", $"Function '{routine.Name}' must be used as an expression, not with Call.", syntax.NameSpan);
        }

        return new BoundCallStatement(routine, arguments);
    }

    private BoundStatement BindReturn(ReturnStatementSyntax syntax)
    {
        BoundExpression? value = syntax.Value is null ? null : BindExpression(syntax.Value);
        if (_currentRoutine is null)
        {
            Report("SMILE2132", "Return is valid only inside a Sub or Function.", syntax.Span);
        }
        else if (!_currentRoutine.IsFunction && value is not null)
        {
            Report("SMILE2133", $"Sub '{_currentRoutine.Name}' cannot return a value.", syntax.Span);
        }
        else if (_currentRoutine.IsFunction && value is null)
        {
            Report("SMILE2134", $"Function '{_currentRoutine.Name}' must return a value.", syntax.Span);
        }
        else if (_currentRoutine.IsFunction && value is not null &&
                 value.Type is not SmileType.Error && value.Type != _currentRoutine.ReturnType)
        {
            Report(
                "SMILE2135",
                $"Function '{_currentRoutine.Name}' must return {DisplayType(_currentRoutine.ReturnType ?? SmileType.Error)}, not {DisplayType(value.Type)}.",
                syntax.Value!.Span);
        }

        return new BoundReturnStatement(value);
    }

    private BoundStatement BindSelect(SelectStatementSyntax syntax)
    {
        BoundExpression selector = BindExpression(syntax.Selector);
        var clauses = new List<BoundSelectCaseClause>();
        var seen = new HashSet<SmileValue>();
        bool sawElse = false;
        if (syntax.Cases.Count == 0)
        {
            Report("SMILE2136", "Select Case requires at least one Case clause.", syntax.Span);
        }

        for (int index = 0; index < syntax.Cases.Count; index++)
        {
            SelectCaseClauseSyntax clause = syntax.Cases[index];
            SmileValue? caseValue = null;
            if (clause.IsElse)
            {
                if (sawElse)
                {
                    Report("SMILE2137", "Select Case may contain only one Case Else.", clause.Span);
                }

                sawElse = true;
                if (index != syntax.Cases.Count - 1)
                {
                    Report("SMILE2138", "Case Else must be the last Case clause.", clause.Span);
                }
            }
            else
            {
                if (sawElse)
                {
                    Report("SMILE2138", "No Case clause may follow Case Else.", clause.Span);
                }

                BoundExpression value = BindExpression(clause.Value!, constantsOnly: true);
                if (selector.Type is not SmileType.Error && value.Type is not SmileType.Error && selector.Type != value.Type)
                {
                    Report("SMILE2139", "A Case value must have exactly the selector's scalar type.", clause.Value!.Span);
                }

                StaticEvaluationResult evaluation = BoundExpressionEvaluator.Evaluate(value, _constantValues);
                if (!evaluation.IsKnown || evaluation.MayFailAtRuntime)
                {
                    if (value.Type is not SmileType.Error)
                    {
                        Report("SMILE2140", "A Case value must be a compile-time scalar expression.", clause.Value!.Span);
                    }
                }
                else
                {
                    caseValue = evaluation.Value;
                    if (!seen.Add(evaluation.Value))
                    {
                        Report("SMILE2141", "Duplicate Case value.", clause.Value!.Span);
                    }
                }
            }

            clauses.Add(new BoundSelectCaseClause(
                caseValue,
                clause.IsElse,
                BindItems(clause.SourceItems, directProgramLevel: false)));
        }

        return new BoundSelectStatement(selector, clauses);
    }

    private BoundStatement BindPrint(CorePrintStatementSyntax syntax) =>
        new BoundCorePrintStatement(
            syntax.Values.Select(value => BindExpression(value)).ToArray(),
            syntax.SuppressNewLine);

    private BoundStatement BindIf(IfStatementSyntax syntax)
    {
        var clauses = new List<BoundConditionalClause>();
        foreach (ConditionalClauseSyntax clause in syntax.Clauses)
        {
            BoundExpression condition = BindExpression(clause.Condition);
            RequireBoolean(condition, clause.Condition.Span, "IF condition");
            clauses.Add(new BoundConditionalClause(
                condition,
                BindItems(clause.SourceItems, directProgramLevel: false)));
        }

        return new BoundIfStatement(
            clauses,
            BindItems(syntax.ElseSourceItems, directProgramLevel: false),
            syntax.HasElseClause);
    }

    private BoundStatement BindFor(ForStatementSyntax syntax)
    {
        BoundExpression lower = BindExpression(syntax.LowerBound);
        BoundExpression upper = BindExpression(syntax.UpperBound);
        RequireNumber(lower, syntax.LowerBound.Span, "FOR lower bound");
        RequireNumber(upper, syntax.UpperBound.Span, "FOR upper bound");

        VariableSymbol? counter = LookupVariable(syntax.CounterName, syntax.CounterSpan, reportUnknown: false);
        bool declares = counter is null;
        if (counter is null)
        {
            if (_optionExplicit)
            {
                Report("SMILE2129", $"FOR counter '{syntax.CounterName}' must be declared because Option Explicit is enabled.", syntax.CounterSpan);
                counter = ErrorVariable(syntax.CounterName, syntax.CounterSpan, SmileType.Integer);
            }
            else if (_programDeclarations.ContainsKey(syntax.CounterName))
            {
                Report("SMILE2128", $"'{syntax.CounterName}' names a routine and cannot be used as a FOR counter.", syntax.CounterSpan);
                counter = ErrorVariable(syntax.CounterName, syntax.CounterSpan, SmileType.Integer);
            }
            else
            {
                counter = new VariableSymbol(
                    syntax.CounterName,
                    syntax.CounterSpan,
                    SmileType.Integer,
                    RoutineName: _currentRoutine?.Name);
                if (_currentRoutine is null)
                {
                    _globals[syntax.CounterName] = counter;
                    _programDeclarations[syntax.CounterName] = syntax.CounterSpan;
                }
                else
                {
                    _locals![syntax.CounterName] = counter;
                }
            }
        }
        else if (counter.IsConstant || counter.IsArray)
        {
            Report("SMILE2107", "A FOR counter must be a writable scalar variable.", syntax.CounterSpan);
        }
        else if (counter.Type is not SmileType.Integer)
        {
            Report("SMILE2108", "A FOR counter must have type Number.", syntax.CounterSpan);
        }

        _forDepth++;
        IReadOnlyList<BoundSourceItem> body = BindItems(syntax.SourceItems, directProgramLevel: false);
        _forDepth--;
        return new BoundForStatement(counter, declares, lower, upper, syntax.IsDescending, body);
    }

    private BoundStatement BindDo(DoStatementSyntax syntax)
    {
        _doDepth++;
        IReadOnlyList<BoundSourceItem> body = BindItems(syntax.SourceItems, directProgramLevel: false);
        _doDepth--;
        BoundExpression? condition = syntax.UntilCondition is null ? null : BindExpression(syntax.UntilCondition);
        if (condition is not null)
        {
            RequireBoolean(condition, syntax.UntilCondition!.Span, "LOOP UNTIL condition");
        }

        return new BoundDoStatement(body, condition);
    }

    private BoundStatement BindExit(ExitStatementSyntax syntax)
    {
        if (syntax.Kind is ExitStatementKind.For && _forDepth == 0)
        {
            Report("SMILE2109", "'Exit For' is valid only inside a FOR loop in the current routine.", syntax.Span);
        }
        else if (syntax.Kind is ExitStatementKind.Do && _doDepth == 0)
        {
            Report("SMILE2110", "'Exit Do' is valid only inside a DO loop in the current routine.", syntax.Span);
        }

        return new BoundExitStatement(
            syntax.Kind is ExitStatementKind.For ? BoundExitKind.For : BoundExitKind.Do);
    }

    private BoundExpression BindExpression(ExpressionSyntax syntax, bool constantsOnly = false)
    {
        switch (syntax)
        {
            case ErrorExpressionSyntax:
                return new BoundErrorExpression();
            case StringLiteralExpressionSyntax literal:
                return new BoundStringLiteralExpression(literal.Value);
            case IntegerLiteralExpressionSyntax literal:
                return long.TryParse(literal.Text, NumberStyles.None, CultureInfo.InvariantCulture, out long number)
                    ? new BoundIntegerLiteralExpression(number)
                    : new BoundErrorExpression();
            case BooleanLiteralExpressionSyntax literal:
                return new BoundBooleanLiteralExpression(literal.Value);
            case ParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression, constantsOnly);
            case NameExpressionSyntax name:
                return BindName(name, constantsOnly);
            case ArrayAccessExpressionSyntax array:
                if (constantsOnly)
                {
                    Report("SMILE2111", "Constant expressions cannot read array elements.", array.Span);
                    return new BoundErrorExpression();
                }

                VariableSymbol arraySymbol = ResolveArray(array.Name, array.NameSpan);
                BoundExpression index = BindExpression(array.Index);
                ValidateIndex(arraySymbol, index, array.Index.Span);
                return new BoundArrayExpression(arraySymbol, index);
            case CallExpressionSyntax call:
                if (constantsOnly)
                {
                    Report("SMILE2111", "Constant expressions cannot invoke functions.", call.Span);
                    return new BoundErrorExpression();
                }

                RoutineSymbol routine = ResolveRoutine(call.Name, call.NameSpan, expectedFunction: true);
                IReadOnlyList<BoundExpression> arguments = BindCallArguments(routine, call.Arguments, call.NameSpan);
                if (!routine.IsFunction)
                {
                    Report("SMILE2142", $"Sub '{routine.Name}' cannot be used as an expression.", call.NameSpan);
                    return new BoundErrorExpression();
                }

                return new BoundCallExpression(routine, arguments);
            case UnaryExpressionSyntax unary:
                BoundExpression operand = BindExpression(unary.Operand, constantsOnly);
                BoundUnaryOperator? unaryOperator = BoundUnaryOperator.Bind(unary.OperatorToken.Kind, operand.Type);
                if (unaryOperator is null)
                {
                    if (operand.Type is not SmileType.Error)
                    {
                        Report("SMILE2113", $"Operator '{unary.OperatorToken.Text}' is not defined for {DisplayType(operand.Type)}.", unary.OperatorToken.Span);
                    }

                    return new BoundErrorExpression();
                }

                return new BoundUnaryExpression(unaryOperator, operand, unary.OperatorToken.Span);
            case BinaryExpressionSyntax binary:
                BoundExpression left = BindExpression(binary.Left, constantsOnly);
                BoundExpression right = BindExpression(binary.Right, constantsOnly);
                BoundBinaryOperator? binaryOperator = BoundBinaryOperator.Bind(binary.OperatorToken.Kind, left.Type, right.Type);
                if (binaryOperator is null)
                {
                    if (left.Type is not SmileType.Error && right.Type is not SmileType.Error)
                    {
                        Report(
                            "SMILE2114",
                            $"Operator '{binary.OperatorToken.Text}' is not defined for {DisplayType(left.Type)} and {DisplayType(right.Type)}.",
                            binary.OperatorToken.Span);
                    }

                    return new BoundErrorExpression();
                }

                return new BoundBinaryExpression(left, binaryOperator, right, binary.OperatorToken.Span);
            default:
                return new BoundErrorExpression();
        }
    }

    private BoundExpression BindName(NameExpressionSyntax syntax, bool constantsOnly)
    {
        if (_constantSyntax.ContainsKey(syntax.Name) && !_globals.ContainsKey(syntax.Name))
        {
            ResolveConstant(syntax.Name);
        }

        VariableSymbol? variable = LookupVariable(syntax.Name, syntax.Span, reportUnknown: false);
        if (variable is null || constantsOnly && !variable.IsConstant)
        {
            Report(
                constantsOnly ? "SMILE2111" : "SMILE2112",
                constantsOnly
                    ? $"Constant expression cannot reference non-constant '{syntax.Name}'."
                    : $"Variable '{syntax.Name}' is used before its first assignment or declaration.",
                syntax.Span);
            return new BoundErrorExpression();
        }

        if (variable.IsArray)
        {
            Report("SMILE2127", $"Array '{variable.Name}' requires an index.", syntax.Span);
            return new BoundErrorExpression();
        }

        return new BoundVariableExpression(variable);
    }

    private VariableSymbol? LookupVariable(
        string name,
        TextSpan useSpan,
        bool reportUnknown,
        bool checkFutureLocal = true)
    {
        if (_locals is not null && _locals.TryGetValue(name, out VariableSymbol? local))
        {
            if (checkFutureLocal && !local.IsParameter && useSpan.Start < local.DeclarationSpan.Start)
            {
                Report("SMILE2143", $"Local '{name}' is used before its Dim declaration.", useSpan);
            }

            return local;
        }

        if (_globals.TryGetValue(name, out VariableSymbol? global))
        {
            return global;
        }

        if (reportUnknown)
        {
            Report("SMILE2112", $"Variable '{name}' is not declared.", useSpan);
        }

        return null;
    }

    private VariableSymbol ResolveArray(string name, TextSpan span)
    {
        VariableSymbol? symbol = LookupVariable(name, span, reportUnknown: false);
        if (symbol is null)
        {
            Report("SMILE2144", $"Array '{name}' is not declared.", span);
            return new VariableSymbol(name, span, SmileType.Integer, RoutineName: _currentRoutine?.Name, ArrayLength: 1);
        }

        if (!symbol.IsArray)
        {
            Report("SMILE2145", $"Scalar variable '{name}' cannot be indexed as an array.", span);
            return new VariableSymbol(name, span, symbol.Type, RoutineName: symbol.RoutineName, ArrayLength: 1);
        }

        return symbol;
    }

    private void ValidateIndex(VariableSymbol array, BoundExpression index, TextSpan span)
    {
        RequireNumber(index, span, "array index");
        if (index.Type is not SmileType.Integer)
        {
            return;
        }

        StaticEvaluationResult evaluation = BoundExpressionEvaluator.Evaluate(index, _constantValues);
        if (evaluation.IsKnown && !evaluation.MayFailAtRuntime)
        {
            long value = evaluation.Value.IntegerValue;
            if (value < 0 || value >= array.ArrayLength)
            {
                Report("SMILE2146", $"Array index {value} is outside the valid range 0 through {array.ArrayLength - 1} for '{array.Name}'.", span);
            }
        }
    }

    private RoutineSymbol ResolveRoutine(string name, TextSpan span, bool expectedFunction)
    {
        if (_routineSymbols.TryGetValue(name, out RoutineSymbol? routine))
        {
            return routine;
        }

        Report("SMILE2147", $"Routine '{name}' is not declared.", span);
        return new RoutineSymbol(
            name,
            span,
            expectedFunction ? RoutineKind.Function : RoutineKind.Sub,
            Array.Empty<VariableSymbol>(),
            expectedFunction ? SmileType.Error : null);
    }

    private IReadOnlyList<BoundExpression> BindCallArguments(
        RoutineSymbol routine,
        IReadOnlyList<ExpressionSyntax> arguments,
        TextSpan callSpan)
    {
        BoundExpression[] bound = arguments.Select(argument => BindExpression(argument)).ToArray();
        if (bound.Length != routine.Parameters.Count)
        {
            Report(
                "SMILE2148",
                $"Routine '{routine.Name}' expects {routine.Parameters.Count} argument(s), but received {bound.Length}.",
                callSpan);
        }

        int count = Math.Min(bound.Length, routine.Parameters.Count);
        for (int index = 0; index < count; index++)
        {
            if (bound[index].Type is not SmileType.Error && bound[index].Type != routine.Parameters[index].Type)
            {
                Report(
                    "SMILE2149",
                    $"Argument {index + 1} for '{routine.Name}' must be {DisplayType(routine.Parameters[index].Type)}, not {DisplayType(bound[index].Type)}.",
                    arguments[index].Span);
            }
        }

        return bound;
    }

    private static VariableSymbol ErrorVariable(string name, TextSpan span, SmileType type) =>
        new(name, span, type is SmileType.Error ? SmileType.Integer : type);

    private static bool ItemsDefinitelyExit(IReadOnlyList<BoundSourceItem> items)
    {
        foreach (BoundStatement statement in items.OfType<BoundStatement>())
        {
            if (StatementDefinitelyExits(statement))
            {
                return true;
            }
        }

        return false;
    }

    private static bool StatementDefinitelyExits(BoundStatement statement) => statement switch
    {
        BoundReturnStatement => true,
        BoundEndProgramStatement => true,
        BoundIfStatement conditional =>
            conditional.HasElseClause &&
            conditional.Clauses.All(clause => ItemsDefinitelyExit(clause.SourceItems)) &&
            ItemsDefinitelyExit(conditional.ElseSourceItems),
        BoundSelectStatement select =>
            select.Cases.Any(clause => clause.IsElse) &&
            select.Cases.All(clause => ItemsDefinitelyExit(clause.SourceItems)),
        _ => false
    };

    private void RequireBoolean(BoundExpression expression, TextSpan span, string context)
    {
        if (expression.Type is not SmileType.Boolean and not SmileType.Error)
        {
            Report("SMILE2115", $"{context} must have type Boolean.", span);
        }
    }

    private void RequireNumber(BoundExpression expression, TextSpan span, string context)
    {
        if (expression.Type is not SmileType.Integer and not SmileType.Error)
        {
            Report("SMILE2116", $"{context} must have type Number.", span);
        }
    }

    private void Report(string code, string message, TextSpan span) =>
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, span));

    private static string DisplayType(SmileType type) => type switch
    {
        SmileType.Integer => "Number",
        SmileType.Boolean => "Boolean",
        SmileType.String => "Text",
        _ => "Error"
    };
}
