using System.Globalization;

namespace SMILE.Engine;

// This is the sole source-language binder. Every evaluator and target backend
// receives the same case-insensitive names, exact types, and control-flow tree.
internal sealed class Binder
{
    private readonly Dictionary<string, VariableSymbol> _symbols = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ConstStatementSyntax> _constantSyntax = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BoundConstStatement> _resolvedConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resolvingConstants = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VariableSymbol, SmileValue> _constantValues = new();
    private readonly List<Diagnostic> _diagnostics = new();
    private int _forDepth;
    private int _doDepth;

    public BindResult Bind(SmileProgramSyntax syntax)
    {
        CollectExplicitDeclarations(syntax.SourceItems, topLevel: true);
        foreach (string name in _constantSyntax.Keys.ToArray())
        {
            ResolveConstant(name);
        }

        IReadOnlyList<BoundSourceItem> items = BindItems(syntax.SourceItems, topLevel: true);
        return new BindResult(
            new BoundProgram(items, _symbols.Values.ToArray()),
            _diagnostics);
    }

    private void CollectExplicitDeclarations(IReadOnlyList<SourceItemSyntax> items, bool topLevel)
    {
        foreach (SourceItemSyntax item in items)
        {
            switch (item)
            {
                case DimStatementSyntax dim:
                    DeclareExplicit(dim.Name, dim.NameSpan, dim.DeclaredType, isConstant: false);
                    break;
                case ConstStatementSyntax constant when topLevel:
                    if (_constantSyntax.ContainsKey(constant.Name) || _symbols.ContainsKey(constant.Name))
                    {
                        Report("SMILE2101", $"'{constant.Name}' is already declared.", constant.NameSpan);
                    }
                    else
                    {
                        _constantSyntax.Add(constant.Name, constant);
                    }

                    break;
                case ConstStatementSyntax constant:
                    Report("SMILE2102", "Const declarations are allowed only at program level.", constant.Span);
                    break;
                case IfStatementSyntax conditional:
                    foreach (ConditionalClauseSyntax clause in conditional.Clauses)
                    {
                        CollectExplicitDeclarations(clause.SourceItems, topLevel: false);
                    }

                    CollectExplicitDeclarations(conditional.ElseSourceItems, topLevel: false);
                    break;
                case ForStatementSyntax loop:
                    CollectExplicitDeclarations(loop.SourceItems, topLevel: false);
                    break;
                case DoStatementSyntax loop:
                    CollectExplicitDeclarations(loop.SourceItems, topLevel: false);
                    break;
            }
        }
    }

    private void DeclareExplicit(string name, TextSpan span, SmileType type, bool isConstant)
    {
        if (_symbols.ContainsKey(name) || _constantSyntax.ContainsKey(name))
        {
            Report("SMILE2101", $"'{name}' is already declared.", span);
            return;
        }

        _symbols.Add(name, new VariableSymbol(name, span, type, isConstant));
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
            if (!evaluation.IsInvalid)
            {
                Report("SMILE2104", $"Constant '{name}' requires a compile-time scalar value.", syntax.Initializer.Span);
            }
            else if (evaluation.Error is SmileArithmeticError error)
            {
                Report(error.CompileCode, error.Message, error.Span);
            }

            return null;
        }

        var symbol = new VariableSymbol(name, syntax.NameSpan, initializer.Type, IsConstant: true);
        _symbols[name] = symbol;
        var statement = new BoundConstStatement(symbol, initializer, evaluation.Value);
        _resolvedConstants[name] = statement;
        _constantValues[symbol] = evaluation.Value;
        return statement;
    }

    private IReadOnlyList<BoundSourceItem> BindItems(IReadOnlyList<SourceItemSyntax> items, bool topLevel)
    {
        var result = new List<BoundSourceItem>();
        foreach (SourceItemSyntax item in items)
        {
            BoundSourceItem? bound = item switch
            {
                BlankLineSyntax => new BoundBlankLine(),
                FullLineCommentSyntax comment => new BoundFullLineComment(comment.Marker, comment.Payload),
                StatementSyntax statement => BindStatement(statement, topLevel),
                _ => null
            };
            if (bound is not null)
            {
                result.Add(bound);
            }
        }

        return result;
    }

    private BoundStatement? BindStatement(StatementSyntax statement, bool topLevel) => statement switch
    {
        CoreAssignmentStatementSyntax assignment => BindAssignment(assignment),
        DimStatementSyntax dim => BindDim(dim),
        ConstStatementSyntax constant => topLevel ? BindConst(constant) : null,
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
        if (!_symbols.TryGetValue(syntax.Name, out VariableSymbol? variable))
        {
            variable = new VariableSymbol(syntax.Name, syntax.NameSpan, value.Type);
            _symbols.Add(syntax.Name, variable);
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

    private BoundStatement? BindDim(DimStatementSyntax syntax) =>
        _symbols.TryGetValue(syntax.Name, out VariableSymbol? variable)
            ? new BoundDimStatement(variable)
            : null;

    private BoundStatement? BindConst(ConstStatementSyntax syntax) => ResolveConstant(syntax.Name);

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
                BindItems(clause.SourceItems, topLevel: false)));
        }

        return new BoundIfStatement(
            clauses,
            BindItems(syntax.ElseSourceItems, topLevel: false),
            syntax.HasElseClause);
    }

    private BoundStatement BindFor(ForStatementSyntax syntax)
    {
        BoundExpression lower = BindExpression(syntax.LowerBound);
        BoundExpression upper = BindExpression(syntax.UpperBound);
        RequireNumber(lower, syntax.LowerBound.Span, "FOR lower bound");
        RequireNumber(upper, syntax.UpperBound.Span, "FOR upper bound");

        bool declares = !_symbols.TryGetValue(syntax.CounterName, out VariableSymbol? counter);
        if (declares)
        {
            counter = new VariableSymbol(syntax.CounterName, syntax.CounterSpan, SmileType.Integer);
            _symbols.Add(syntax.CounterName, counter);
        }
        else if (counter!.IsConstant)
        {
            Report("SMILE2107", "A FOR counter must be writable.", syntax.CounterSpan);
        }
        else if (counter.Type is not SmileType.Integer)
        {
            Report("SMILE2108", "A FOR counter must have type Number.", syntax.CounterSpan);
        }

        _forDepth++;
        IReadOnlyList<BoundSourceItem> body = BindItems(syntax.SourceItems, topLevel: false);
        _forDepth--;
        return new BoundForStatement(counter!, declares, lower, upper, syntax.IsDescending, body);
    }

    private BoundStatement BindDo(DoStatementSyntax syntax)
    {
        _doDepth++;
        IReadOnlyList<BoundSourceItem> body = BindItems(syntax.SourceItems, topLevel: false);
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
            Report("SMILE2109", "'Exit For' is valid only inside a FOR loop.", syntax.Span);
        }
        else if (syntax.Kind is ExitStatementKind.Do && _doDepth == 0)
        {
            Report("SMILE2110", "'Exit Do' is valid only inside a DO loop.", syntax.Span);
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
                if (long.TryParse(literal.Text, NumberStyles.None, CultureInfo.InvariantCulture, out long number))
                {
                    return new BoundIntegerLiteralExpression(number);
                }

                return new BoundErrorExpression();
            case BooleanLiteralExpressionSyntax literal:
                return new BoundBooleanLiteralExpression(literal.Value);
            case ParenthesizedExpressionSyntax parenthesized:
                return BindExpression(parenthesized.Expression, constantsOnly);
            case NameExpressionSyntax name:
                if (_constantSyntax.ContainsKey(name.Name) && !_symbols.ContainsKey(name.Name))
                {
                    ResolveConstant(name.Name);
                }

                if (_symbols.TryGetValue(name.Name, out VariableSymbol? variable) &&
                    (!constantsOnly || variable.IsConstant))
                {
                    return new BoundVariableExpression(variable);
                }

                Report(
                    constantsOnly ? "SMILE2111" : "SMILE2112",
                    constantsOnly
                        ? $"Constant expression cannot reference non-constant '{name.Name}'."
                        : $"Variable '{name.Name}' is used before its first assignment or declaration.",
                    name.Span);
                return new BoundErrorExpression();
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
