using System.Globalization;

namespace SMILE.Engine;

internal sealed class Binder
{
    private static readonly ulong MinIntegerMagnitude = (ulong)long.MaxValue + 1UL;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VariableSymbol> _declaredVariables = new();
    private readonly Dictionary<VariableSymbol, SmileValue> _knownValues = new();
    private bool _topLevelCanContinue = true;
    private bool _topLevelDefinitelyContinues = true;

    public BindResult Bind(SmileProgramSyntax program)
    {
        IReadOnlyList<BoundSourceItem> sourceItems = BindSourceItems(
            program.SourceItems,
            appendExecution: true,
            isIfBody: false,
            isWhileBody: false);

        return new BindResult(
            new BoundProgram(sourceItems, _declaredVariables.ToArray()),
            _diagnostics);
    }

    private IReadOnlyList<BoundSourceItem> BindSourceItems(
        IReadOnlyList<SourceItemSyntax> sourceItems,
        bool appendExecution,
        bool isIfBody,
        bool isWhileBody)
    {
        var boundItems = new List<BoundSourceItem>(sourceItems.Count);
        foreach (SourceItemSyntax sourceItem in sourceItems)
        {
            switch (sourceItem)
            {
                case FullLineCommentSyntax comment:
                    boundItems.Add(new BoundFullLineComment(comment.Marker, comment.Payload));
                    break;

                case BlankLineSyntax:
                    boundItems.Add(new BoundBlankLine());
                    break;

                case StatementSyntax statement:
                    BoundStatement? bound = BindStatement(
                        statement,
                        appendExecution,
                        isIfBody,
                        isWhileBody);
                    if (bound is not null)
                    {
                        boundItems.Add(bound);
                    }

                    break;
            }
        }

        return boundItems;
    }

    private BoundStatement? BindStatement(
        StatementSyntax statement,
        bool appendExecution,
        bool isIfBody,
        bool isWhileBody) =>
        statement switch
        {
            LetStatementSyntax let when isWhileBody => RejectWhileLet(let),
            LetStatementSyntax let when isIfBody => RejectBranchLet(let),
            LetStatementSyntax let => BindLetStatement(let, appendExecution),
            SetStatementSyntax set => BindSetStatement(set, appendExecution),
            InputStatementSyntax input => BindInputStatement(input, appendExecution),
            PrintStatementSyntax print => BindPrintStatement(print, appendExecution),
            IfStatementSyntax conditional => BindIfStatement(
                conditional,
                appendExecution,
                isWhileBody),
            WhileStatementSyntax loop => BindWhileStatement(
                loop,
                appendExecution,
                isIfBody),
            _ => null
        };

    private BoundStatement? RejectBranchLet(LetStatementSyntax syntax)
    {
        _diagnostics.Add(new Diagnostic(
            "SMILE1414",
            DiagnosticSeverity.Error,
            "LET is not permitted inside IF v1.0.",
            syntax.Span));
        return null;
    }

    private BoundStatement? RejectWhileLet(LetStatementSyntax syntax)
    {
        _diagnostics.Add(new Diagnostic(
            "SMILE1610",
            DiagnosticSeverity.Error,
            "LET is not permitted inside WHILE v1.0.",
            syntax.Span with { Length = "LET".Length }));
        return null;
    }

    private BoundStatement? BindLetStatement(
        LetStatementSyntax syntax,
        bool appendExecution)
    {
        if (_variables.ContainsKey(syntax.Name))
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1107",
                DiagnosticSeverity.Error,
                $"Variable '{syntax.Name}' is already declared.",
                syntax.NameSpan));
            return null;
        }

        // A declaration is intentionally absent while its initializer binds.
        // That single ordering rule gives us declaration-before-use and makes
        // self-reference naturally become the normal undefined-variable error.
        int diagnosticCountBeforeInitializer = _diagnostics.Count;
        BoundExpression initializer = BindExpression(syntax.Initializer);
        if (initializer.Type is SmileType.Error ||
            _diagnostics.Count != diagnosticCountBeforeInitializer)
        {
            return null;
        }

        var symbol = new VariableSymbol(syntax.Name, syntax.NameSpan, initializer.Type);
        var statement = new BoundLetStatement(symbol, initializer);
        if (appendExecution && !TryApplyTopLevelStatement(statement))
        {
            return null;
        }

        _variables.Add(syntax.Name, symbol);
        _declaredVariables.Add(symbol);
        return statement;
    }

    private BoundStatement? BindSetStatement(
        SetStatementSyntax syntax,
        bool appendExecution)
    {
        if (!_variables.TryGetValue(syntax.Name, out VariableSymbol? variable))
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1304",
                DiagnosticSeverity.Error,
                $"SET target variable '{syntax.Name}' is undefined.",
                syntax.NameSpan));
            return null;
        }

        int diagnosticCountBeforeValue = _diagnostics.Count;
        BoundExpression value = BindExpression(syntax.Value);
        if (value.Type is SmileType.Error ||
            _diagnostics.Count != diagnosticCountBeforeValue)
        {
            return null;
        }

        if (value.Type != variable.Type)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1305",
                DiagnosticSeverity.Error,
                $"SET value type '{value.Type}' does not match variable '{syntax.Name}' of type '{variable.Type}'.",
                syntax.Value.Span));
            return null;
        }

        var statement = new BoundSetStatement(variable, value);
        return !appendExecution || TryApplyTopLevelStatement(statement)
            ? statement
            : null;
    }

    private BoundStatement? BindInputStatement(
        InputStatementSyntax syntax,
        bool appendExecution)
    {
        if (!_variables.TryGetValue(syntax.Name, out VariableSymbol? variable))
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1505",
                DiagnosticSeverity.Error,
                $"INPUT target variable '{syntax.Name}' is undefined.",
                syntax.NameSpan));
            return null;
        }

        var statement = new BoundInputStatement(variable);
        return !appendExecution || TryApplyTopLevelStatement(statement)
            ? statement
            : null;
    }

    private BoundStatement? BindPrintStatement(
        PrintStatementSyntax syntax,
        bool appendExecution)
    {
        int diagnosticCountBeforeValue = _diagnostics.Count;
        BoundExpression value = BindExpression(syntax.Value);
        if (value.Type is SmileType.Error ||
            _diagnostics.Count != diagnosticCountBeforeValue)
        {
            return null;
        }

        var statement = new BoundPrintStatement(value, syntax.IsBlankLine);
        return !appendExecution || TryApplyTopLevelStatement(statement)
            ? statement
            : null;
    }

    private BoundStatement? BindIfStatement(
        IfStatementSyntax syntax,
        bool appendExecution,
        bool isWhileBody)
    {
        int diagnosticsBefore = _diagnostics.Count;
        var clauses = new List<BoundConditionalClause>(syntax.Clauses.Count);

        foreach (ConditionalClauseSyntax clause in syntax.Clauses)
        {
            BoundExpression condition = BindControlFlowCondition(
                clause.Condition,
                IfConditionDiagnostics);

            clauses.Add(new BoundConditionalClause(
                condition,
                BindIfBody(clause.SourceItems, isWhileBody)));
        }

        IReadOnlyList<BoundSourceItem> elseSourceItems = BindIfBody(
            syntax.ElseSourceItems,
            isWhileBody);
        if (_diagnostics.Count != diagnosticsBefore ||
            clauses.Any(clause => clause.Condition.Type is SmileType.Error))
        {
            return null;
        }

        var statement = new BoundIfStatement(
            clauses,
            elseSourceItems,
            syntax.HasElseClause);
        return !appendExecution || TryApplyTopLevelStatement(statement)
            ? statement
            : null;
    }

    private BoundStatement? BindWhileStatement(
        WhileStatementSyntax syntax,
        bool appendExecution,
        bool isIfBody)
    {
        int diagnosticsBefore = _diagnostics.Count;
        BoundExpression condition = BindControlFlowCondition(
            syntax.Condition,
            WhileConditionDiagnostics);

        IReadOnlyList<BoundSourceItem> sourceItems = BindSourceItems(
            syntax.SourceItems,
            appendExecution: false,
            isIfBody: isIfBody,
            isWhileBody: true);
        if (_diagnostics.Count != diagnosticsBefore ||
            condition.Type is SmileType.Error)
        {
            return null;
        }

        var statement = new BoundWhileStatement(
            condition,
            sourceItems,
            syntax.KeywordSpan);
        return !appendExecution || TryApplyTopLevelStatement(statement)
            ? statement
            : null;
    }

    private bool TryApplyTopLevelStatement(BoundStatement statement)
    {
        // Binding and type checking continue through unreachable source so the
        // learner still receives structural diagnostics. Static evaluation is
        // skipped once every possible runtime path has already terminated;
        // later arithmetic is then not definitely evaluated.
        if (!_topLevelCanContinue)
        {
            return true;
        }

        bool succeeded = TryApplyStaticStatement(
            statement,
            _knownValues,
            reportInvalid: _topLevelDefinitelyContinues,
            out bool canContinue,
            out bool definitelyContinues);
        if (succeeded)
        {
            _topLevelCanContinue = canContinue;
            _topLevelDefinitelyContinues &= definitelyContinues;
        }

        return succeeded;
    }

    private bool TryApplyStaticStatement(
        BoundStatement statement,
        Dictionary<VariableSymbol, SmileValue> knownValues,
        bool reportInvalid,
        out bool canContinue,
        out bool definitelyContinues)
    {
        switch (statement)
        {
            case BoundLetStatement let:
                return TryApplyStaticAssignment(
                    let.Variable,
                    let.Initializer,
                    knownValues,
                    reportInvalid,
                    out canContinue,
                    out definitelyContinues);

            case BoundSetStatement set:
                return TryApplyStaticAssignment(
                    set.Variable,
                    set.Value,
                    knownValues,
                    reportInvalid,
                    out canContinue,
                    out definitelyContinues);

            case BoundInputStatement input:
                knownValues.Remove(input.Variable);
                canContinue = true;
                definitelyContinues = true;
                return true;

            case BoundPrintStatement print:
                StaticEvaluationResult printed = BoundExpressionEvaluator.Evaluate(
                    print.Value,
                    knownValues);
                return HandleStaticResult(
                    printed,
                    reportInvalid,
                    out canContinue,
                    out definitelyContinues);

            case BoundIfStatement conditional:
                return TryApplyStaticIf(
                    conditional,
                    knownValues,
                    reportInvalid,
                    out canContinue,
                    out definitelyContinues);

            case BoundWhileStatement loop:
                return TryApplyStaticWhile(
                    loop,
                    knownValues,
                    reportInvalid,
                    out canContinue,
                    out definitelyContinues);

            default:
                canContinue = true;
                definitelyContinues = true;
                return true;
        }
    }

    private bool TryApplyStaticWhile(
        BoundWhileStatement loop,
        Dictionary<VariableSymbol, SmileValue> knownValues,
        bool reportInvalid,
        out bool canContinue,
        out bool definitelyContinues)
    {
        StaticEvaluationResult condition = BoundExpressionEvaluator.Evaluate(
            loop.Condition,
            knownValues);
        if (!HandleStaticResult(
                condition,
                reportInvalid,
                out canContinue,
                out definitelyContinues))
        {
            return false;
        }

        if (!canContinue)
        {
            return true;
        }

        if (condition.IsKnown &&
            !condition.MayFailAtRuntime &&
            !condition.Value.BooleanValue)
        {
            // The condition is evaluated, but the body is unreachable. Binding
            // already validated the complete body independently of execution.
            return true;
        }

        // One abstract body transfer is enough for binding-time reachability:
        // it can report a source-known failure in a definitely reached first
        // iteration, but it never re-evaluates the back edge or invents a trip
        // count. BoundProgramAnalysis owns the full fixed-point calculation.
        var bodyValues = new Dictionary<VariableSymbol, SmileValue>(knownValues);
        bool bodyIsDefinitelyReached =
            reportInvalid &&
            condition.IsKnown &&
            condition.Value.BooleanValue &&
            !condition.MayFailAtRuntime;
        if (!TryApplyStaticStatementList(
                loop.Statements,
                bodyValues,
                bodyIsDefinitelyReached,
                out bool bodyCanContinue,
                out _))
        {
            canContinue = false;
            definitelyContinues = false;
            return false;
        }

        if (bodyIsDefinitelyReached && !bodyCanContinue)
        {
            canContinue = false;
            definitelyContinues = false;
            return true;
        }

        foreach (VariableSymbol variable in EnumerateMutatedVariables(loop.Statements))
        {
            if (!knownValues.TryGetValue(variable, out SmileValue incoming) ||
                !bodyValues.TryGetValue(variable, out SmileValue afterBody) ||
                incoming != afterBody)
            {
                knownValues.Remove(variable);
            }
        }

        // A loop may execute zero times, fail, run forever, or eventually
        // leave. Its successful exit is possible, but not guaranteed, unless
        // the known-false special case above proved that the body is skipped.
        canContinue = true;
        definitelyContinues = false;
        return true;
    }

    private static IEnumerable<VariableSymbol> EnumerateMutatedVariables(
        IReadOnlyList<BoundStatement> statements)
    {
        foreach (BoundStatement statement in statements)
        {
            switch (statement)
            {
                case BoundSetStatement set:
                    yield return set.Variable;
                    break;

                case BoundInputStatement input:
                    yield return input.Variable;
                    break;

                case BoundIfStatement conditional:
                    foreach (BoundConditionalClause clause in conditional.Clauses)
                    {
                        foreach (VariableSymbol variable in EnumerateMutatedVariables(clause.Statements))
                        {
                            yield return variable;
                        }
                    }

                    foreach (VariableSymbol variable in EnumerateMutatedVariables(
                                 conditional.ElseStatements))
                    {
                        yield return variable;
                    }

                    break;

                case BoundWhileStatement nested:
                    foreach (VariableSymbol variable in EnumerateMutatedVariables(nested.Statements))
                    {
                        yield return variable;
                    }

                    break;
            }
        }
    }

    private bool TryApplyStaticAssignment(
        VariableSymbol variable,
        BoundExpression expression,
        Dictionary<VariableSymbol, SmileValue> knownValues,
        bool reportInvalid,
        out bool canContinue,
        out bool definitelyContinues)
    {
        StaticEvaluationResult result = BoundExpressionEvaluator.Evaluate(
            expression,
            knownValues);
        if (!HandleStaticResult(
                result,
                reportInvalid,
                out canContinue,
                out definitelyContinues))
        {
            return false;
        }

        if (!canContinue)
        {
            return true;
        }

        if (result.IsKnown)
        {
            knownValues[variable] = result.Value;
        }
        else
        {
            knownValues.Remove(variable);
        }

        return true;
    }

    private bool HandleStaticResult(
        StaticEvaluationResult result,
        bool reportInvalid,
        out bool canContinue,
        out bool definitelyContinues)
    {
        if (!result.IsInvalid)
        {
            canContinue = true;
            // A value can be Known on every successful runtime path while its
            // evaluation can still fail first (for example, an INPUT-dependent
            // division hidden behind a Boolean identity). Later source-known
            // errors are therefore diagnostics only when this expression is
            // also guaranteed to complete.
            definitelyContinues = !result.MayFailAtRuntime;
            return true;
        }

        canContinue = false;
        definitelyContinues = false;
        if (!reportInvalid)
        {
            return true;
        }

        SmileArithmeticError error = result.Error!.Value;
        _diagnostics.Add(new Diagnostic(
            error.CompileCode,
            DiagnosticSeverity.Error,
            error.Message,
            error.Span));
        return false;
    }

    private bool TryApplyStaticIf(
        BoundIfStatement conditional,
        Dictionary<VariableSymbol, SmileValue> knownValues,
        bool reportInvalid,
        out bool canContinue,
        out bool definitelyContinues)
    {
        var outgoing = new List<Dictionary<VariableSymbol, SmileValue>>();
        bool remainingPathIsPossible = true;
        bool remainingPathIsDefinite = reportInvalid;
        definitelyContinues = true;

        foreach (BoundConditionalClause clause in conditional.Clauses)
        {
            if (!remainingPathIsPossible)
            {
                break;
            }

            StaticEvaluationResult condition = BoundExpressionEvaluator.Evaluate(
                clause.Condition,
                knownValues);
            if (condition.IsInvalid)
            {
                if (remainingPathIsDefinite)
                {
                    canContinue = false;
                    definitelyContinues = false;
                    return HandleStaticResult(
                        condition,
                        reportInvalid: true,
                        out _,
                        out _);
                }

                // This remaining path terminates at runtime if earlier clauses
                // all failed. It cannot reach a later clause or the merge.
                remainingPathIsPossible = false;
                definitelyContinues = false;
                break;
            }

            if (condition.MayFailAtRuntime)
            {
                // The condition's value describes successful evaluations only.
                // A runtime arithmetic failure can prevent both its selected
                // body and every later clause from being reached.
                definitelyContinues = false;
                remainingPathIsDefinite = false;
            }

            if (condition.IsKnown)
            {
                if (!condition.Value.BooleanValue)
                {
                    continue;
                }

                var selectedValues = new Dictionary<VariableSymbol, SmileValue>(knownValues);
                if (!TryApplyStaticStatementList(
                        clause.Statements,
                        selectedValues,
                        remainingPathIsDefinite,
                        out bool selectedContinues,
                        out bool selectedDefinitelyContinues))
                {
                    canContinue = false;
                    definitelyContinues = false;
                    return false;
                }

                if (selectedContinues)
                {
                    outgoing.Add(selectedValues);
                }

                definitelyContinues &= selectedContinues && selectedDefinitelyContinues;

                remainingPathIsPossible = false;
                break;
            }

            var branchValues = new Dictionary<VariableSymbol, SmileValue>(knownValues);
            if (!TryApplyStaticStatementList(
                    clause.Statements,
                    branchValues,
                    reportInvalid: false,
                    out bool branchContinues,
                    out bool branchDefinitelyContinues))
            {
                canContinue = false;
                definitelyContinues = false;
                return false;
            }

            if (branchContinues)
            {
                outgoing.Add(branchValues);
            }

            definitelyContinues &= branchContinues && branchDefinitelyContinues;

            // A runtime-unknown condition makes both its selected body and the
            // later-clause path conditional from this point onward.
            remainingPathIsDefinite = false;
        }

        if (remainingPathIsPossible)
        {
            if (conditional.HasElseClause)
            {
                var elseValues = new Dictionary<VariableSymbol, SmileValue>(knownValues);
                if (!TryApplyStaticStatementList(
                        conditional.ElseStatements,
                        elseValues,
                        remainingPathIsDefinite,
                        out bool elseContinues,
                        out bool elseDefinitelyContinues))
                {
                    canContinue = false;
                    definitelyContinues = false;
                    return false;
                }

                if (elseContinues)
                {
                    outgoing.Add(elseValues);
                }

                definitelyContinues &= elseContinues && elseDefinitelyContinues;
            }
            else
            {
                outgoing.Add(new Dictionary<VariableSymbol, SmileValue>(knownValues));
            }
        }

        canContinue = outgoing.Count > 0;
        definitelyContinues &= canContinue;
        if (canContinue)
        {
            MergeKnownValues(knownValues, outgoing);
        }

        return true;
    }

    private bool TryApplyStaticStatementList(
        IReadOnlyList<BoundStatement> statements,
        Dictionary<VariableSymbol, SmileValue> knownValues,
        bool reportInvalid,
        out bool canContinue,
        out bool definitelyContinues)
    {
        definitelyContinues = true;
        foreach (BoundStatement statement in statements)
        {
            if (!TryApplyStaticStatement(
                    statement,
                    knownValues,
                    reportInvalid && definitelyContinues,
                    out canContinue,
                    out bool statementDefinitelyContinues))
            {
                definitelyContinues = false;
                return false;
            }

            if (!canContinue)
            {
                definitelyContinues = false;
                return true;
            }

            definitelyContinues &= statementDefinitelyContinues;
        }

        canContinue = true;
        return true;
    }

    private static void MergeKnownValues(
        Dictionary<VariableSymbol, SmileValue> destination,
        IReadOnlyList<Dictionary<VariableSymbol, SmileValue>> outgoing)
    {
        VariableSymbol[] variables = outgoing
            .SelectMany(environment => environment.Keys)
            .Distinct()
            .ToArray();
        destination.Clear();

        foreach (VariableSymbol variable in variables)
        {
            if (!outgoing[0].TryGetValue(variable, out SmileValue first))
            {
                continue;
            }

            if (outgoing.Skip(1).All(environment =>
                    environment.TryGetValue(variable, out SmileValue candidate) &&
                    candidate == first))
            {
                destination.Add(variable, first);
            }
        }
    }

    private IReadOnlyList<BoundSourceItem> BindIfBody(
        IReadOnlyList<SourceItemSyntax> sourceItems,
        bool isWhileBody) =>
        BindSourceItems(
            sourceItems,
            appendExecution: false,
            isIfBody: true,
            isWhileBody: isWhileBody);

    private void ValidateCondition(
        ExpressionSyntax expression,
        ConditionDiagnosticProfile diagnostics)
    {
        switch (expression)
        {
            case ErrorExpressionSyntax:
                return;

            case ParenthesizedExpressionSyntax parenthesized:
                ValidateCondition(parenthesized.Expression, diagnostics);
                return;

            case UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.NotKeyword } unary:
                ValidateCondition(unary.Operand, diagnostics);
                return;

            case BinaryExpressionSyntax binary
                when binary.OperatorToken.Kind is SyntaxKind.AndKeyword or SyntaxKind.OrKeyword:
                ValidateCondition(binary.Left, diagnostics);
                ValidateCondition(binary.Right, diagnostics);
                return;

            case BinaryExpressionSyntax binary when IsComparison(binary.OperatorToken.Kind):
                if (ContainsInvocation(binary.Left) || ContainsInvocation(binary.Right))
                {
                    _diagnostics.Add(new Diagnostic(
                        diagnostics.InvocationCode,
                        DiagnosticSeverity.Error,
                        diagnostics.InvocationMessage,
                        binary.Span));
                }

                return;

            default:
                if (ContainsInvocation(expression))
                {
                    _diagnostics.Add(new Diagnostic(
                        diagnostics.InvocationCode,
                        DiagnosticSeverity.Error,
                        diagnostics.InvocationMessage,
                        expression.Span));
                }

                _diagnostics.Add(new Diagnostic(
                    diagnostics.ExplicitComparisonCode,
                    DiagnosticSeverity.Error,
                    diagnostics.ExplicitComparisonMessage,
                    expression.Span));
                return;
        }
    }

    private BoundExpression BindControlFlowCondition(
        ExpressionSyntax syntax,
        ConditionDiagnosticProfile diagnostics)
    {
        // Structural validation deliberately sees the unsimplified syntax tree
        // so a Boolean identity can never hide an implicit condition leaf.
        ValidateCondition(syntax, diagnostics);
        BoundExpression condition = BindExpression(syntax);
        if (condition.Type is not (SmileType.Boolean or SmileType.Error))
        {
            _diagnostics.Add(new Diagnostic(
                diagnostics.TypeCode,
                DiagnosticSeverity.Error,
                diagnostics.TypeMessage,
                syntax.Span));
        }

        return condition;
    }

    private static readonly ConditionDiagnosticProfile IfConditionDiagnostics = new(
        ExplicitComparisonCode: "SMILE1402",
        ExplicitComparisonMessage: "Every atomic IF condition must be an explicit comparison.",
        TypeCode: "SMILE1403",
        TypeMessage: "The complete IF condition must have type Boolean.",
        InvocationCode: "SMILE1404",
        InvocationMessage: "An IF condition cannot invoke a function or procedure.");

    private static readonly ConditionDiagnosticProfile WhileConditionDiagnostics = new(
        ExplicitComparisonCode: "SMILE1603",
        ExplicitComparisonMessage: "Every atomic WHILE condition must be an explicit comparison.",
        TypeCode: "SMILE1604",
        TypeMessage: "The complete WHILE condition must have type Boolean.",
        InvocationCode: "SMILE1605",
        InvocationMessage: "A WHILE condition cannot invoke a function or procedure.");

    private readonly record struct ConditionDiagnosticProfile(
        string ExplicitComparisonCode,
        string ExplicitComparisonMessage,
        string TypeCode,
        string TypeMessage,
        string InvocationCode,
        string InvocationMessage);

    private static bool IsComparison(SyntaxKind kind) =>
        kind is SyntaxKind.EqualsToken or
            SyntaxKind.NotEqualsToken or
            SyntaxKind.LessToken or
            SyntaxKind.LessOrEqualsToken or
            SyntaxKind.GreaterToken or
            SyntaxKind.GreaterOrEqualsToken;

    private static bool ContainsInvocation(ExpressionSyntax expression) =>
        expression switch
        {
            ErrorExpressionSyntax => false,
            StringLiteralExpressionSyntax => false,
            BlockStringLiteralExpressionSyntax => false,
            IntegerLiteralExpressionSyntax => false,
            BooleanLiteralExpressionSyntax => false,
            NameExpressionSyntax => false,
            UnaryExpressionSyntax unary => ContainsInvocation(unary.Operand),
            BinaryExpressionSyntax binary =>
                ContainsInvocation(binary.Left) || ContainsInvocation(binary.Right),
            ParenthesizedExpressionSyntax parenthesized =>
                ContainsInvocation(parenthesized.Expression),
            InterpolatedStringExpressionSyntax interpolated =>
                interpolated.Parts
                    .OfType<InterpolationExpressionPartSyntax>()
                    .Any(part => ContainsInvocation(part.Expression)),

            // IF conditions permanently fail closed for future callable or
            // otherwise unknown value-expression nodes. A future function
            // feature must deliberately prove that condition evaluation is
            // call-free instead of inheriting accidental acceptance here.
            _ => true
        };

    private BoundExpression BindExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ErrorExpressionSyntax => new BoundErrorExpression(),
            StringLiteralExpressionSyntax literal => new BoundStringLiteralExpression(literal.Value),
            BlockStringLiteralExpressionSyntax literal => new BoundStringLiteralExpression(literal.Value),
            IntegerLiteralExpressionSyntax literal => BindIntegerLiteral(literal),
            BooleanLiteralExpressionSyntax literal => new BoundBooleanLiteralExpression(literal.Value),
            NameExpressionSyntax name => BindNameExpression(name),
            UnaryExpressionSyntax unary => BindUnaryExpression(unary),
            BinaryExpressionSyntax binary => BindBinaryExpression(binary),
            ParenthesizedExpressionSyntax parenthesized => BindExpression(parenthesized.Expression),
            InterpolatedStringExpressionSyntax interpolated => BindInterpolatedString(interpolated),
            _ => new BoundErrorExpression()
        };

    private BoundExpression BindIntegerLiteral(IntegerLiteralExpressionSyntax syntax)
    {
        if (TryParseIntegerMagnitude(syntax.Text, out ulong magnitude) &&
            magnitude <= long.MaxValue)
        {
            return new BoundIntegerLiteralExpression((long)magnitude);
        }

        _diagnostics.Add(new Diagnostic(
            "SMILE1202",
            DiagnosticSeverity.Error,
            "Integer literal is outside the signed 64-bit range.",
            syntax.Span));
        return new BoundErrorExpression();
    }

    private BoundExpression BindNameExpression(NameExpressionSyntax syntax)
    {
        if (_variables.TryGetValue(syntax.Name, out VariableSymbol? variable))
        {
            return new BoundVariableExpression(variable);
        }

        _diagnostics.Add(new Diagnostic(
            "SMILE1106",
            DiagnosticSeverity.Error,
            $"Undefined variable '{syntax.Name}'.",
            syntax.Span));
        return new BoundErrorExpression();
    }

    private BoundExpression BindUnaryExpression(UnaryExpressionSyntax syntax)
    {
        if (syntax.OperatorToken.Kind is SyntaxKind.MinusToken &&
            syntax.Operand is IntegerLiteralExpressionSyntax literal &&
            TryParseIntegerMagnitude(literal.Text, out ulong magnitude) &&
            magnitude == MinIntegerMagnitude)
        {
            return new BoundIntegerLiteralExpression(long.MinValue);
        }

        BoundExpression operand = BindExpression(syntax.Operand);
        if (operand.Type is SmileType.Error)
        {
            return new BoundErrorExpression();
        }

        BoundUnaryOperator? op = BoundUnaryOperator.Bind(syntax.OperatorToken.Kind, operand.Type);
        if (op is null)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1203",
                DiagnosticSeverity.Error,
                $"Unary operator '{syntax.OperatorToken.Text}' is not defined for type '{operand.Type}'.",
                syntax.OperatorToken.Span));
            return new BoundErrorExpression();
        }

        return new BoundUnaryExpression(op, operand, syntax.OperatorToken.Span);
    }

    private BoundExpression BindBinaryExpression(BinaryExpressionSyntax syntax)
    {
        BoundExpression left = BindExpression(syntax.Left);
        BoundExpression right = BindExpression(syntax.Right);
        if (left.Type is SmileType.Error || right.Type is SmileType.Error)
        {
            return new BoundErrorExpression();
        }

        BoundBinaryOperator? op = BoundBinaryOperator.Bind(syntax.OperatorToken.Kind, left.Type, right.Type);
        if (op is null)
        {
            _diagnostics.Add(new Diagnostic(
                "SMILE1204",
                DiagnosticSeverity.Error,
                $"Binary operator '{syntax.OperatorToken.Text}' is not defined for types '{left.Type}' and '{right.Type}'.",
                syntax.OperatorToken.Span));
            return new BoundErrorExpression();
        }

        return new BoundBinaryExpression(left, op, right, syntax.OperatorToken.Span);
    }

    private BoundExpression BindInterpolatedString(InterpolatedStringExpressionSyntax syntax)
    {
        var parts = new List<BoundInterpolatedPart>();
        foreach (InterpolatedPartSyntax part in syntax.Parts)
        {
            switch (part)
            {
                case InterpolatedTextPartSyntax text:
                    parts.Add(new BoundInterpolatedTextPart(text.Text));
                    break;

                case InterpolationExpressionPartSyntax expression:
                    parts.Add(new BoundInterpolationExpressionPart(BindExpression(expression.Expression)));
                    break;
            }
        }

        return new BoundInterpolatedStringExpression(parts);
    }

    private static bool TryParseIntegerMagnitude(string text, out ulong magnitude) =>
        ulong.TryParse(
            text,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out magnitude);
}
