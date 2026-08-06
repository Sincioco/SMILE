using System.Globalization;

namespace SMILE.Engine;

internal sealed class Binder
{
    private static readonly ulong MinIntegerMagnitude = (ulong)long.MaxValue + 1UL;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly Dictionary<string, VariableSymbol> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<VariableSymbol> _declaredVariables = new();
    private readonly BoundProgramExecutionTraceBuilder _execution = new();

    public BindResult Bind(SmileProgramSyntax program)
    {
        var statements = new List<BoundStatement>();

        foreach (StatementSyntax statement in program.Statements)
        {
            BoundStatement? bound = BindStatement(
                statement,
                appendExecution: true,
                isIfBody: false);
            if (bound is not null)
            {
                statements.Add(bound);
            }
        }

        return new BindResult(
            new BoundProgram(statements, _declaredVariables.ToArray()),
            _diagnostics);
    }

    private BoundStatement? BindStatement(
        StatementSyntax statement,
        bool appendExecution,
        bool isIfBody) =>
        statement switch
        {
            LetStatementSyntax let when isIfBody => RejectBranchLet(let),
            LetStatementSyntax let => BindLetStatement(let, appendExecution),
            SetStatementSyntax set => BindSetStatement(set, appendExecution),
            PrintStatementSyntax print => BindPrintStatement(print, appendExecution),
            IfStatementSyntax conditional => BindIfStatement(conditional, appendExecution),
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
        if (appendExecution && !_execution.TryAppend(statement, _diagnostics))
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
        return !appendExecution || _execution.TryAppend(statement, _diagnostics)
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
        return !appendExecution || _execution.TryAppend(statement, _diagnostics)
            ? statement
            : null;
    }

    private BoundStatement? BindIfStatement(
        IfStatementSyntax syntax,
        bool appendExecution)
    {
        int diagnosticsBefore = _diagnostics.Count;
        var clauses = new List<BoundConditionalClause>(syntax.Clauses.Count);

        foreach (ConditionalClauseSyntax clause in syntax.Clauses)
        {
            ValidateIfCondition(clause.Condition);
            BoundExpression condition = BindExpression(clause.Condition);
            if (condition.Type is not (SmileType.Boolean or SmileType.Error))
            {
                _diagnostics.Add(new Diagnostic(
                    "SMILE1403",
                    DiagnosticSeverity.Error,
                    "The complete IF condition must have type Boolean.",
                    clause.Condition.Span));
            }

            clauses.Add(new BoundConditionalClause(
                condition,
                BindIfBody(clause.Statements)));
        }

        IReadOnlyList<BoundStatement> elseStatements = BindIfBody(syntax.ElseStatements);
        if (_diagnostics.Count != diagnosticsBefore ||
            clauses.Any(clause => clause.Condition.Type is SmileType.Error))
        {
            return null;
        }

        var statement = new BoundIfStatement(
            clauses,
            elseStatements,
            syntax.HasElseClause);
        return !appendExecution || _execution.TryAppend(statement, _diagnostics)
            ? statement
            : null;
    }

    private IReadOnlyList<BoundStatement> BindIfBody(
        IReadOnlyList<StatementSyntax> statements)
    {
        var boundStatements = new List<BoundStatement>(statements.Count);
        foreach (StatementSyntax statement in statements)
        {
            BoundStatement? bound = BindStatement(
                statement,
                appendExecution: false,
                isIfBody: true);
            if (bound is not null)
            {
                boundStatements.Add(bound);
            }
        }

        return boundStatements;
    }

    private void ValidateIfCondition(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ErrorExpressionSyntax:
                return;

            case ParenthesizedExpressionSyntax parenthesized:
                ValidateIfCondition(parenthesized.Expression);
                return;

            case UnaryExpressionSyntax { OperatorToken.Kind: SyntaxKind.NotKeyword } unary:
                ValidateIfCondition(unary.Operand);
                return;

            case BinaryExpressionSyntax binary
                when binary.OperatorToken.Kind is SyntaxKind.AndKeyword or SyntaxKind.OrKeyword:
                ValidateIfCondition(binary.Left);
                ValidateIfCondition(binary.Right);
                return;

            case BinaryExpressionSyntax binary when IsComparison(binary.OperatorToken.Kind):
                if (ContainsInvocation(binary.Left) || ContainsInvocation(binary.Right))
                {
                    _diagnostics.Add(new Diagnostic(
                        "SMILE1404",
                        DiagnosticSeverity.Error,
                        "An IF condition cannot invoke a function or procedure.",
                        binary.Span));
                }

                return;

            default:
                if (ContainsInvocation(expression))
                {
                    _diagnostics.Add(new Diagnostic(
                        "SMILE1404",
                        DiagnosticSeverity.Error,
                        "An IF condition cannot invoke a function or procedure.",
                        expression.Span));
                }

                _diagnostics.Add(new Diagnostic(
                    "SMILE1402",
                    DiagnosticSeverity.Error,
                    "Every atomic IF condition must be an explicit comparison.",
                    expression.Span));
                return;
        }
    }

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
