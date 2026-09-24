using System.Globalization;

namespace SMILE.Engine;

// This is the sole source-language binder. It performs deliberate declaration
// and body passes so every evaluator and target receives the same symbols,
// scopes, exact scalar types, call order, and control-flow tree.
internal sealed partial class Binder
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
        BindEnums();

        foreach (string name in _constantSyntax.Keys.ToArray())
        {
            ResolveConstant(name);
        }

        BindRecords();
        DeclareGlobalDimensions(arrays: false);
        DeclareGlobalDimensions(arrays: true);
        BuildRoutineSignatures();
        BuildRecordMemberSignatures();

        IReadOnlyList<BoundSourceItem> topLevel = BindItems(syntax.SourceItems, directProgramLevel: true);
        foreach (RoutineDeclarationSyntax declaration in _routineSyntax.Values.OrderBy(item => item.Span.Start))
        {
            BindRoutine(declaration);
        }
        foreach (var member in _recordRoutineSyntax) BindRoutine(member.Key, member.Value);

        return new BindResult(
            new BoundProgram(
                topLevel,
                _globals.Values.OrderBy(symbol => symbol.DeclarationSpan.Start).ToArray(),
                _boundRoutines,
                _optionExplicit) { EnumTypes = _enums.Values.ToArray(), RecordTypes = _orderedRecords, ClassTypes = _classes.Values.ToArray() },
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
                case ClassDeclarationSyntax reference:
                    if (ReserveProgramName(reference.Name, reference.NameSpan))
                        _classes.Add(reference.Name, new ClassTypeSymbol(reference));
                    break;
                case RecordDeclarationSyntax record:
                    if (ReserveProgramName(record.Name, record.NameSpan))
                        _records.Add(record.Name, new RecordTypeSymbol(record));
                    break;
                case EnumDeclarationSyntax enumeration:
                    if (ReserveProgramName(enumeration.Name, enumeration.NameSpan))
                        _enums.Add(enumeration.Name, new EnumTypeSymbol(enumeration));
                    break;
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
            case WithStatementSyntax block:
                InventoryGlobalDimsInItems(block.SourceItems);
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
            IReadOnlyList<int> dimensions = dim.IsArray ? ResolveArrayDimensions(dim) : Array.Empty<int>();
            _globals[dim.Name] = new VariableSymbol(
                dim.Name,
                dim.NameSpan,
                ResolveVariableType(dim, dimensions),
                IsConstant: false,
                RoutineName: null,
                ArrayLength: dimensions.Count > 0 ? dimensions[0] : 0,
                ArraySecondLength: dimensions.Count > 1 ? dimensions[1] : 0);
        }
    }

    private void BuildRoutineSignatures()
    {
        foreach (RoutineDeclarationSyntax declaration in _routineSyntax.Values.OrderBy(item => item.Span.Start))
            _routineSymbols.Add(declaration.Name, BuildRoutineSignature(declaration));
    }

    private RoutineSymbol BuildRoutineSignature(RoutineDeclarationSyntax declaration, InstanceTypeSymbol? owner = null,
        bool isPrivate = false, InstanceMemberRoutineKind memberKind = InstanceMemberRoutineKind.Method, SmileType? setterType = null)
    {
        string scopeName = owner is null ? declaration.Name : owner.Name + "." + declaration.Name + "." + memberKind;
        var parameters = new List<VariableSymbol>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool sawOptional = false;
        foreach (ParameterSyntax parameter in declaration.Parameters)
        {
            if (!names.Add(parameter.Name))
            {
                Report("SMILE2119", $"Parameter '{parameter.Name}' is already declared in routine '{declaration.Name}'.", parameter.NameSpan);
                continue;
            }

            if (sawOptional && !parameter.IsOptional)
            {
                Report("SMILE2160", "Required parameters must precede Optional parameters.", parameter.Span);
            }
            sawOptional |= parameter.IsOptional;
            SmileType parameterType = ResolveType(parameter.DeclaredType);
            SmileValue? defaultValue = BindParameterDefault(parameter, parameterType);
            parameters.Add(new VariableSymbol(
                parameter.Name,
                parameter.NameSpan,
                parameterType,
                IsConstant: false,
                RoutineName: scopeName,
                ArrayLength: 0,
                IsParameter: true,
                DefaultValue: defaultValue,
                IsByRef: parameter.IsByRef));
        }

        return new RoutineSymbol(
            declaration.Name,
            declaration.NameSpan,
            declaration.Kind,
            parameters,
            declaration.ReturnType is null ? null : ResolveType(declaration.ReturnType))
        {
            Owner = owner, IsPrivate = isPrivate, MemberKind = memberKind,
            Receiver = owner is null ? null : new VariableSymbol("Me", declaration.NameSpan, owner, RoutineName: scopeName, IsParameter: true, IsByRef: owner is RecordTypeSymbol) { IsReceiver = true },
            SetterValue = setterType is null ? null : new VariableSymbol("Value", declaration.NameSpan, setterType, RoutineName: scopeName, IsParameter: true) { IsSetterValue = true }
        };
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
        if (initializer.Type is { Kind: SmileTypeKind.Error } || !evaluation.IsKnown || evaluation.MayFailAtRuntime)
        {
            if (evaluation.IsInvalid && evaluation.Error is SmileArithmeticError error)
            {
                Report(error.CompileCode, error.Message, error.Span);
            }
            else if (initializer.Type is not { Kind: SmileTypeKind.Error })
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

    private IReadOnlyList<int> ResolveArrayDimensions(DimStatementSyntax syntax)
    {
        if (!syntax.IsArray)
        {
            return Array.Empty<int>();
        }

        if (syntax.ArraySizes.Count is < 1 or > 2)
        {
            Report("SMILE2010", "Arrays require one or two dimensions.", syntax.Span);
            return new[] { 1 };
        }

        var dimensions = new List<int>(syntax.ArraySizes.Count);
        long total = 1;
        foreach (ExpressionSyntax sizeSyntax in syntax.ArraySizes)
        {
            BoundExpression size = BindExpression(sizeSyntax, constantsOnly: true);
            RequireNumber(size, sizeSyntax.Span, "array dimension");
            StaticEvaluationResult evaluation = BoundExpressionEvaluator.Evaluate(size, _constantValues);
            if (evaluation.IsInvalid && evaluation.Error is SmileArithmeticError error)
            {
                Report(error.CompileCode, error.Message, error.Span);
                dimensions.Add(1);
                continue;
            }

            if (!evaluation.IsKnown || evaluation.MayFailAtRuntime || size.Type is not { Kind: SmileTypeKind.Integer })
            {
                if (size.Type is not { Kind: SmileTypeKind.Error })
                {
                    Report("SMILE2120", "An array dimension must be a compile-time Number expression.", sizeSyntax.Span);
                }

                dimensions.Add(1);
                continue;
            }

            long value = evaluation.Value.IntegerValue;
            if (value <= 0)
            {
                Report("SMILE2121", "An array dimension must be positive.", sizeSyntax.Span);
                value = 1;
            }
            else if (value > int.MaxValue)
            {
                Report("SMILE2122", "The array dimension is too large for compiler-managed storage.", sizeSyntax.Span);
                value = 1;
            }

            if (total > int.MaxValue / value)
            {
                Report("SMILE2122", "Total array storage is too large for compiler-managed storage.", syntax.Span);
                value = 1;
            }

            total *= value;
            dimensions.Add((int)value);
        }

        return dimensions;
    }

    private void BindRoutine(RoutineDeclarationSyntax declaration, RoutineSymbol? routine = null)
    {
        if (routine is null && !_routineSymbols.TryGetValue(declaration.Name, out routine))
        {
            return;
        }

        _currentRoutine = routine;
        _locals = new Dictionary<string, VariableSymbol>(StringComparer.OrdinalIgnoreCase);
        foreach (VariableSymbol parameter in routine.ExecutionParameters)
        {
            _locals[parameter.Name] = parameter;
        }

        InventoryLocalDimensions(declaration.SourceItems, routine.ScopeName);
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

                    IReadOnlyList<int> dimensions = dim.IsArray
                        ? ResolveArrayDimensions(dim)
                        : Array.Empty<int>();
                    _locals.Add(dim.Name, new VariableSymbol(
                        dim.Name,
                        dim.NameSpan,
                        ResolveVariableType(dim, dimensions),
                        IsConstant: false,
                        RoutineName: routineName,
                        ArrayLength: dimensions.Count > 0 ? dimensions[0] : 0,
                        ArraySecondLength: dimensions.Count > 1 ? dimensions[1] : 0));
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
                case WithStatementSyntax block:
                    InventoryLocalDimensions(block.SourceItems, routineName);
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
                EnumDeclarationSyntax when directProgramLevel => null,
                EnumDeclarationSyntax enumeration => BindNestedEnum(enumeration),
                RecordDeclarationSyntax when directProgramLevel => null,
                RecordDeclarationSyntax record => BindNestedRecord(record),
                ClassDeclarationSyntax when directProgramLevel => null,
                ClassDeclarationSyntax reference => BindNestedRecord(reference),
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
        MemberAssignmentStatementSyntax assignment => BindMemberAssignment(assignment),
        DataLoadStatementSyntax load => BindDataLoad(load),
        DataSaveStatementSyntax save => BindDataSave(save),
        CoreArrayAssignmentStatementSyntax assignment => BindArrayAssignment(assignment),
        DimStatementSyntax dim => BindDim(dim),
        ConstStatementSyntax constant => directProgramLevel ? BindConst(constant) : BindLocalConst(constant),
        CallStatementSyntax call => BindCallStatement(call),
        MemberCallStatementSyntax call => BindMemberCallStatement(call),
        ReturnStatementSyntax returnStatement => BindReturn(returnStatement),
        SelectStatementSyntax select => BindSelect(select),
        CorePrintStatementSyntax print => BindPrint(print),
        TextFileLoadStatementSyntax load => BindTextFileLoad(load),
        NumberLoadStatementSyntax load => BindNumberLoad(load),
        NumberSaveStatementSyntax save => BindNumberSave(save),
        GetKeyStatementSyntax getKey => BindGetKey(getKey),
        ClearScreenStatementSyntax => new BoundClearScreenStatement(),
        MoveCursorStatementSyntax moveCursor => BindMoveCursor(moveCursor),
        TextColorStatementSyntax textColor => new BoundTextColorStatement(
            textColor.Foreground,
            textColor.Background,
            textColor.IsDefault),
        WaitStatementSyntax wait => BindWait(wait),
        RandomStatementSyntax random => BindRandom(random),
        IfStatementSyntax conditional => BindIf(conditional),
        ForStatementSyntax loop => BindFor(loop),
        DoStatementSyntax loop => BindDo(loop),
        WithStatementSyntax block => BindWith(block),
        ExitStatementSyntax exit => BindExit(exit),
        EndProgramStatementSyntax => new BoundEndProgramStatement(),
        _ => null
    };

    private BoundStatement BindMoveCursor(MoveCursorStatementSyntax syntax)
    {
        BoundExpression column = BindExpression(syntax.Column);
        BoundExpression row = BindExpression(syntax.Row);
        RequireNumber(column, syntax.Column.Span, "Move Cursor To column");
        RequireNumber(row, syntax.Row.Span, "Move Cursor To row");
        return new BoundMoveCursorStatement(column, row);
    }

    private BoundStatement BindAssignment(CoreAssignmentStatementSyntax syntax)
    {
        BoundExpression value = BindExpression(syntax.Value);
        VariableSymbol variable = ResolveAssignmentTarget(syntax.Name, syntax.NameSpan, value.Type);
        value = CoerceReference(value, variable.Type);
        if (variable.IsArray)
        {
            Report("SMILE2127", $"Array '{variable.Name}' requires an index.", syntax.NameSpan);
        }
        else if (variable.IsConstant)
        {
            Report("SMILE2105", $"Constant '{variable.Name}' cannot be assigned.", syntax.NameSpan);
        }
        else if (value.Type is not { Kind: SmileTypeKind.Error } && variable.Type != value.Type)
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

        if (inferredType == SmileType.Nothing)
            Report("SMILE3454", "Nothing cannot infer a variable type; declare a scalar As Class variable first.", span);

        SmileType type = inferredType is { Kind: SmileTypeKind.Error } ? SmileType.Integer : inferredType;
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
        IReadOnlyList<BoundExpression> indices = BindArrayIndices(array, syntax.Indices, syntax.Span);
        BoundExpression value = BindExpression(syntax.Value);
        if (value.Type is not { Kind: SmileTypeKind.Error } && array.Type != value.Type)
        {
            Report("SMILE2130", $"Cannot assign {DisplayType(value.Type)} to {DisplayType(array.Type)} array '{array.Name}'.", syntax.Value.Span);
        }

        return new BoundArraySetStatement(array, indices, value);
    }

    private BoundStatement BindGetKey(GetKeyStatementSyntax syntax)
    {
        VariableSymbol target = ResolveAssignmentTarget(syntax.Name, syntax.NameSpan, SmileType.Integer);
        ValidateWritableNumberTarget(target, syntax.NameSpan, "Get Key");
        return new BoundGetKeyStatement(target);
    }

    private BoundStatement BindWait(WaitStatementSyntax syntax)
    {
        BoundExpression duration = BindExpression(syntax.Duration);
        RequireNumber(duration, syntax.Duration.Span, "Wait duration");
        return new BoundWaitStatement(duration);
    }

    private BoundStatement BindRandom(RandomStatementSyntax syntax)
    {
        BoundExpression lower = BindExpression(syntax.LowerBound);
        BoundExpression upper = BindExpression(syntax.UpperBound);
        RequireNumber(lower, syntax.LowerBound.Span, "Random lower bound");
        RequireNumber(upper, syntax.UpperBound.Span, "Random upper bound");
        VariableSymbol target = ResolveAssignmentTarget(syntax.Name, syntax.NameSpan, SmileType.Integer);
        ValidateWritableNumberTarget(target, syntax.NameSpan, "Random");
        return new BoundRandomStatement(target, lower, upper);
    }

    private void ValidateWritableNumberTarget(VariableSymbol target, TextSpan span, string context)
    {
        if (target.IsConstant || target.IsArray)
        {
            Report("SMILE2150", $"{context} requires a writable scalar Number variable.", span);
        }
        else if (target.Type is not { Kind: SmileTypeKind.Integer } and not { Kind: SmileTypeKind.Error })
        {
            Report("SMILE2151", $"{context} target must have type Number.", span);
        }
    }

    private BoundStatement? BindDim(DimStatementSyntax syntax)
    {
        VariableSymbol? variable = LookupVariable(syntax.Name, syntax.NameSpan, reportUnknown: false, checkFutureLocal: false);
        return variable is null ? null : syntax.Initializer is null ? new BoundDimStatement(variable)
            : new BoundSetStatement(variable, BindNew(syntax.Initializer, constantsOnly: false));
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
        BoundArguments arguments = BindCallArguments(routine, syntax.Arguments, syntax.NameSpan);
        if (routine.IsFunction)
        {
            Report("SMILE2131", $"Function '{routine.Name}' must be used as an expression, not with Call.", syntax.NameSpan);
        }

        return new BoundCallStatement(routine, arguments.Values, arguments.ParameterOrder);
    }

    private BoundStatement BindReturn(ReturnStatementSyntax syntax)
    {
        BoundExpression? value = syntax.Value is null ? null : BindExpression(syntax.Value);
        if (value is not null && _currentRoutine?.ReturnType is { } returnType) value = CoerceReference(value, returnType);
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
                 value.Type is not { Kind: SmileTypeKind.Error } && value.Type != _currentRoutine.ReturnType)
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
        if (selector.Type is InstanceTypeSymbol || selector.Type == SmileType.Nothing) Report("SMILE3409", "Select Case cannot compare whole records or class references.", syntax.Selector.Span);
        var clauses = new List<BoundSelectCaseClause>();
        var seen = new HashSet<SmileValue>();
        bool sawElse = false;
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
                if (selector.Type is not { Kind: SmileTypeKind.Error } && value.Type is not { Kind: SmileTypeKind.Error } && selector.Type != value.Type)
                {
                    Report("SMILE2139", "A Case value must have exactly the selector's scalar type.", clause.Value!.Span);
                }

                StaticEvaluationResult evaluation = BoundExpressionEvaluator.Evaluate(value, _constantValues);
                if (!evaluation.IsKnown || evaluation.MayFailAtRuntime)
                {
                    if (value.Type is not { Kind: SmileTypeKind.Error })
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

    private BoundStatement BindPrint(CorePrintStatementSyntax syntax)
    {
        BoundExpression[] values = syntax.Values.Select(value => BindExpression(value)).ToArray();
        for (int index = 0; index < values.Length; index++)
            if (values[index].Type is EnumTypeSymbol or InstanceTypeSymbol || values[index].Type == SmileType.Nothing)
                Report("SMILE3424", "Print accepts scalar Text, Number, Double or Boolean values.", syntax.Values[index].Span);
        return new BoundCorePrintStatement(values, syntax.SuppressNewLine);
    }

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
        else if (counter.Type is not { Kind: SmileTypeKind.Integer })
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
            case NewExpressionSyntax creation:
                return BindNew(creation, constantsOnly);
            case NothingExpressionSyntax:
                return new BoundNothingExpression(SmileType.Nothing);
            case IdentityExpressionSyntax identity:
                return BindIdentity(identity, constantsOnly);
            case ErrorExpressionSyntax:
                return new BoundErrorExpression();
            case StringLiteralExpressionSyntax literal:
                return new BoundStringLiteralExpression(literal.Value);
            case DoubleLiteralExpressionSyntax literal:
                return DoubleSemantics.TryParse(literal.Text, out double floating) ? new BoundDoubleLiteralExpression(floating) : new BoundErrorExpression();
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
            case WithReceiverExpressionSyntax receiver:
                return BindWithReceiver(receiver);
            case MeExpressionSyntax instance:
                return BindMe(instance);
            case MemberInvocationExpressionSyntax invocation:
                return BindMemberInvocation(invocation);
            case MemberAccessExpressionSyntax member:
                return BindMember(member, constantsOnly);
            case IndexedMemberExpressionSyntax indexed:
                return BindRecordField(indexed.Member, indexed.Indices, constantsOnly);
            case ArrayAccessExpressionSyntax array:
                if (constantsOnly)
                {
                    Report("SMILE2111", "Constant expressions cannot read array elements.", array.Span);
                    return new BoundErrorExpression();
                }

                VariableSymbol arraySymbol = ResolveArray(array.Name, array.NameSpan);
                IReadOnlyList<BoundExpression> indices = BindArrayIndices(arraySymbol, array.Indices, array.Span);
                return new BoundArrayExpression(arraySymbol, indices);
            case CallExpressionSyntax call:
                if (TryBindIntrinsic(call, constantsOnly, out BoundExpression? intrinsic))
                {
                    return intrinsic!;
                }

                if (constantsOnly)
                {
                    Report("SMILE2111", "Constant expressions cannot invoke functions.", call.Span);
                    return new BoundErrorExpression();
                }

                RoutineSymbol routine = ResolveRoutine(call.Name, call.NameSpan, expectedFunction: true);
                BoundArguments arguments = BindCallArguments(routine, call.Arguments, call.NameSpan);
                if (!routine.IsFunction)
                {
                    Report("SMILE2142", $"Sub '{routine.Name}' cannot be used as an expression.", call.NameSpan);
                    return new BoundErrorExpression();
                }

                return new BoundCallExpression(routine, arguments.Values, arguments.ParameterOrder);
            case UnaryExpressionSyntax unary:
                BoundExpression operand = BindExpression(unary.Operand, constantsOnly);
                BoundUnaryOperator? unaryOperator = BoundUnaryOperator.Bind(unary.OperatorToken.Kind, operand.Type);
                if (unaryOperator is null)
                {
                    if (operand.Type is not { Kind: SmileTypeKind.Error })
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
                    if (left.Type is not { Kind: SmileTypeKind.Error } && right.Type is not { Kind: SmileTypeKind.Error })
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

    private IReadOnlyList<BoundExpression> BindArrayIndices(
        VariableSymbol array,
        IReadOnlyList<ExpressionSyntax> syntax,
        TextSpan span) => BindArrayIndices(array.Name, array.ArrayDimensions, syntax, span);

    private IReadOnlyList<BoundExpression> BindArrayIndices(string name, IReadOnlyList<int> dimensions,
        IReadOnlyList<ExpressionSyntax> syntax, TextSpan span)
    {
        BoundExpression[] indices = syntax.Select(index => BindExpression(index)).ToArray();
        if (indices.Length != dimensions.Count)
        {
            Report("SMILE2152", $"Array '{name}' requires {dimensions.Count} index value(s).", span);
        }

        int count = Math.Min(indices.Length, dimensions.Count);
        for (int position = 0; position < indices.Length; position++)
        {
            ExpressionSyntax indexSyntax = syntax[position];
            BoundExpression index = indices[position];
            RequireNumber(index, indexSyntax.Span, "array index");
            if (position >= count || index.Type is not { Kind: SmileTypeKind.Integer })
            {
                continue;
            }

            StaticEvaluationResult evaluation = BoundExpressionEvaluator.Evaluate(index, _constantValues);
            int length = dimensions[position];
            if (evaluation.IsKnown && !evaluation.MayFailAtRuntime)
            {
                long value = evaluation.Value.IntegerValue;
                if (value < 0 || value >= length)
                {
                    Report(
                        "SMILE2146",
                        $"Array index {value} for dimension {position + 1} is outside the valid range 0 through {length - 1} for '{name}'.",
                        indexSyntax.Span);
                }
            }
        }

        return indices;
    }

    private bool TryBindIntrinsic(
        CallExpressionSyntax syntax,
        bool constantsOnly,
        out BoundExpression? expression)
    {
        if (_routineSymbols.ContainsKey(syntax.Name)) { expression = null; return false; }
        BoundIntrinsicKind? kind = syntax.Name.ToUpperInvariant() switch
        {
            "TODOUBLE" => BoundIntrinsicKind.ToDouble,
            "TONUMBER" => BoundIntrinsicKind.ToNumber,
            "CLAMP" => BoundIntrinsicKind.Clamp,
            "SQRT" => BoundIntrinsicKind.Sqrt,
            "SIN" => BoundIntrinsicKind.Sin,
            "COS" => BoundIntrinsicKind.Cos,
            "ATAN2" => BoundIntrinsicKind.Atan2,
            "FLOOR" => BoundIntrinsicKind.Floor,
            "CEILING" => BoundIntrinsicKind.Ceiling,
            "TRUNCATE" => BoundIntrinsicKind.Truncate,
            "ROUND" => BoundIntrinsicKind.Round,
            "TEXT_FROM_DOUBLE" => BoundIntrinsicKind.TextFromDouble,
            "TEXT_TO_DOUBLE" => BoundIntrinsicKind.TextToDouble,
            "TIMER" => BoundIntrinsicKind.Timer,
            "ABS" => BoundIntrinsicKind.Abs,
            "MIN" => BoundIntrinsicKind.Min,
            "MAX" => BoundIntrinsicKind.Max,
            "TEXT_LENGTH" => BoundIntrinsicKind.TextLength,
            "TEXT_CODE_AT" => BoundIntrinsicKind.TextCodeAt,
            "TEXT_SLICE" => BoundIntrinsicKind.TextSlice,
            _ => null
        };
        if (kind is null)
        {
            expression = null;
            return false;
        }

        if (syntax.Arguments.Any(argument => argument is NamedArgumentExpressionSyntax))
        {
            Report("SMILE2165", "Built-in functions accept positional arguments only.", syntax.Span);
            expression = new BoundErrorExpression();
            return true;
        }

        if (DoubleSemantics.IsIntrinsic(kind.Value) || kind is BoundIntrinsicKind.Abs or BoundIntrinsicKind.Min or BoundIntrinsicKind.Max &&
            syntax.Arguments.Count > 0 && BindExpression(syntax.Arguments[0], constantsOnly).Type is { Kind: SmileTypeKind.Double })
        {
            expression = BindDoubleIntrinsic(syntax, kind.Value, constantsOnly);
            return true;
        }

        int expected = kind switch
        {
            BoundIntrinsicKind.Timer => 0,
            BoundIntrinsicKind.Abs or BoundIntrinsicKind.TextLength => 1,
            BoundIntrinsicKind.TextSlice => 3,
            _ => 2
        };
        int diagnosticCount = _diagnostics.Count;
        BoundExpression[] arguments = syntax.Arguments.Select(argument => BindExpression(argument, constantsOnly)).ToArray();
        if (arguments.Length != expected)
        {
            Report("SMILE2153", $"Built-in function '{syntax.Name}' expects {expected} argument(s), but received {arguments.Length}.", syntax.NameSpan);
        }

        foreach ((BoundExpression argument, int index) in arguments.Select((value, index) => (value, index)))
        {
            TextSpan argumentSpan = index < syntax.Arguments.Count ? syntax.Arguments[index].Span : syntax.NameSpan;
            if (index == 0 && kind is BoundIntrinsicKind.TextLength or BoundIntrinsicKind.TextCodeAt or BoundIntrinsicKind.TextSlice)
            {
                if (argument.Type is not ({ Kind: SmileTypeKind.String } or { Kind: SmileTypeKind.Error }))
                {
                    Report("SMILE2154", $"{syntax.Name} requires Text as its first argument.", argumentSpan);
                }
            }
            else
            {
                RequireNumber(argument, argumentSpan, $"{syntax.Name} argument");
            }
        }

        if (constantsOnly && kind is BoundIntrinsicKind.Timer or BoundIntrinsicKind.TextLength or BoundIntrinsicKind.TextCodeAt or BoundIntrinsicKind.TextSlice)
        {
            Report("SMILE2111", $"Constant expressions cannot call {syntax.Name}().", syntax.Span);
            expression = new BoundErrorExpression();
            return true;
        }

        expression = _diagnostics.Count == diagnosticCount
            ? new BoundIntrinsicExpression(kind.Value, arguments)
            : new BoundErrorExpression();
        return true;
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

    private static VariableSymbol ErrorVariable(string name, TextSpan span, SmileType type) =>
        new(name, span, type is { Kind: SmileTypeKind.Error } ? SmileType.Integer : type);

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
        BoundWithStatement block => ItemsDefinitelyExit(block.SourceItems),
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
        if (expression.Type is not { Kind: SmileTypeKind.Boolean } and not { Kind: SmileTypeKind.Error })
        {
            Report("SMILE2115", $"{context} must have type Boolean.", span);
        }
    }

    private void RequireNumber(BoundExpression expression, TextSpan span, string context)
    {
        if (expression.Type is not { Kind: SmileTypeKind.Integer } and not { Kind: SmileTypeKind.Error })
        {
            Report("SMILE2116", $"{context} must have type Number.", span);
        }
    }

    private void Report(string code, string message, TextSpan span) =>
        _diagnostics.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, span));

    private static string DisplayType(SmileType type) => type switch
    {
        { Kind: SmileTypeKind.Double } => "Double",
        { Kind: SmileTypeKind.Integer } => "Number",
        { Kind: SmileTypeKind.Boolean } => "Boolean",
        { Kind: SmileTypeKind.String } => "Text",
        _ => type.Name
    };
}
