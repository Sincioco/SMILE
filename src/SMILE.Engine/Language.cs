using System.Collections.ObjectModel;
using System.Globalization;

namespace SMILE.Engine;

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message,
    TextSpan Span)
{
    public override string ToString() =>
        $"{Code} {Severity} at line {Span.Line}, column {Span.Column}: {Message}";
}

public readonly record struct TextSpan(
    int Start,
    int Length,
    int Line,
    int Column);

// Syntax nodes describe what the user wrote, before the compiler resolves
// names or decides what an operator means for particular operand types.
public abstract record SyntaxNode(TextSpan Span);

public enum FullLineCommentMarker
{
    Apostrophe
}

// A source-item list retains the learner's authored layout in the same order
// as executable statements. Statements remain a filtered convenience view so
// semantic compiler passes never need to treat layout as executable behavior.
public abstract record SourceItemSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record SmileProgramSyntax : SyntaxNode
{
    public SmileProgramSyntax(
        IReadOnlyList<SourceItemSyntax> SourceItems,
        TextSpan Span)
        : base(Span)
    {
        this.SourceItems = SourceItems;
        Statements = SourceItems.OfType<StatementSyntax>().ToArray();
    }

    public IReadOnlyList<SourceItemSyntax> SourceItems { get; }

    public IReadOnlyList<StatementSyntax> Statements { get; }
}

public abstract record StatementSyntax(TextSpan Span)
    : SourceItemSyntax(Span);

public sealed record FullLineCommentSyntax(
    FullLineCommentMarker Marker,
    string Payload,
    TextSpan Span)
    : SourceItemSyntax(Span);

public sealed record BlankLineSyntax(TextSpan Span)
    : SourceItemSyntax(Span);

public sealed record ConditionalClauseSyntax
{
    public ConditionalClauseSyntax(
        ExpressionSyntax Condition,
        IReadOnlyList<SourceItemSyntax> SourceItems,
        TextSpan Span)
    {
        this.Condition = Condition;
        this.SourceItems = SourceItems;
        this.Span = Span;
        Statements = SourceItems.OfType<StatementSyntax>().ToArray();
    }

    public ExpressionSyntax Condition { get; }

    public IReadOnlyList<SourceItemSyntax> SourceItems { get; }

    public IReadOnlyList<StatementSyntax> Statements { get; }

    public TextSpan Span { get; }
}

public sealed record IfStatementSyntax : StatementSyntax
{
    public IfStatementSyntax(
        IReadOnlyList<ConditionalClauseSyntax> Clauses,
        IReadOnlyList<SourceItemSyntax> ElseSourceItems,
        bool HasElseClause,
        TextSpan Span)
        : base(Span)
    {
        this.Clauses = Clauses;
        this.ElseSourceItems = ElseSourceItems;
        this.HasElseClause = HasElseClause;
        ElseStatements = ElseSourceItems.OfType<StatementSyntax>().ToArray();
    }

    public IReadOnlyList<ConditionalClauseSyntax> Clauses { get; }

    public IReadOnlyList<SourceItemSyntax> ElseSourceItems { get; }

    public IReadOnlyList<StatementSyntax> ElseStatements { get; }

    public bool HasElseClause { get; }
}

public abstract record ExpressionSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record ErrorExpressionSyntax(TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record StringLiteralExpressionSyntax(
    string Value,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record IntegerLiteralExpressionSyntax(
    string Text,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record BooleanLiteralExpressionSyntax(
    bool Value,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record NameExpressionSyntax(
    string Name,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record UnaryExpressionSyntax(
    SyntaxToken OperatorToken,
    ExpressionSyntax Operand,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record BinaryExpressionSyntax(
    ExpressionSyntax Left,
    SyntaxToken OperatorToken,
    ExpressionSyntax Right,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record ParenthesizedExpressionSyntax(
    SyntaxToken OpenParenthesis,
    ExpressionSyntax Expression,
    SyntaxToken CloseParenthesis,
    TextSpan Span)
    : ExpressionSyntax(Span);

// Expected source errors are returned as diagnostics instead of exceptions.
// That lets the CLI and WPF app display friendly messages and keep running.
public sealed record ParseResult(
    SmileProgramSyntax? Program,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success =>
        Program is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public enum SmileType
{
    String,
    Integer,
    Boolean,
    Error
}

public readonly record struct SmileValue
{
    private readonly string? _stringValue;

    private SmileValue(SmileType type, string? stringValue, long integerValue, bool booleanValue)
    {
        Type = type;
        _stringValue = stringValue;
        IntegerValue = integerValue;
        BooleanValue = booleanValue;
    }

    public SmileType Type { get; }

    public string StringValue =>
        Type == SmileType.String
            ? _stringValue ?? string.Empty
            : throw new InvalidOperationException("SMILE value is not a String.");

    public long IntegerValue { get; }

    public bool BooleanValue { get; }

    public static SmileValue FromString(string value) =>
        new(SmileType.String, value, 0, false);

    public static SmileValue FromInteger(long value) =>
        new(SmileType.Integer, null, value, false);

    public static SmileValue FromBoolean(bool value) =>
        new(SmileType.Boolean, null, 0, value);

    public string ToDisplayText() =>
        Type switch
        {
            SmileType.String => StringValue,
            SmileType.Integer => IntegerValue.ToString(CultureInfo.InvariantCulture),
            SmileType.Boolean => BooleanValue ? "True" : "False",
            _ => string.Empty
        };
}

public sealed record VariableSymbol(
    string Name,
    TextSpan DeclarationSpan,
    SmileType Type,
    bool IsConstant = false,
    string? RoutineName = null,
    int ArrayLength = 0,
    bool IsParameter = false)
{
    public bool IsArray => ArrayLength > 0;

    public bool IsGlobal => RoutineName is null;
}

public sealed record RoutineSymbol(
    string Name,
    TextSpan DeclarationSpan,
    RoutineKind Kind,
    IReadOnlyList<VariableSymbol> Parameters,
    SmileType? ReturnType)
{
    public bool IsFunction => Kind is RoutineKind.Function;
}

// Bound nodes describe what the program means after name lookup and type
// checking. Generators consume this layer so no backend has to reparse SMILE.
public abstract record BoundSourceItem;

public sealed record BoundProgram
{
    public BoundProgram(
        IReadOnlyList<BoundSourceItem> SourceItems,
        IReadOnlyList<VariableSymbol> Variables,
        IReadOnlyList<BoundRoutineDeclaration>? Routines = null,
        bool OptionExplicit = false)
    {
        this.SourceItems = SourceItems;
        this.Variables = Variables;
        this.Routines = Routines ?? Array.Empty<BoundRoutineDeclaration>();
        this.OptionExplicit = OptionExplicit;
        Statements = SourceItems.OfType<BoundStatement>().ToArray();
    }

    public IReadOnlyList<BoundSourceItem> SourceItems { get; }

    public IReadOnlyList<BoundStatement> Statements { get; }

    public IReadOnlyList<VariableSymbol> Variables { get; }

    public IReadOnlyList<BoundRoutineDeclaration> Routines { get; }

    public bool OptionExplicit { get; }

    public IEnumerable<VariableSymbol> AllVariables =>
        Variables.Concat(Routines.SelectMany(routine => routine.Locals));

}

public abstract record BoundStatement
    : BoundSourceItem;

public sealed record BoundFullLineComment(
    FullLineCommentMarker OriginalMarker,
    string Payload)
    : BoundSourceItem;

public sealed record BoundBlankLine()
    : BoundSourceItem;

public sealed record BoundSetStatement(
    VariableSymbol Variable,
    BoundExpression Value)
    : BoundStatement;

public sealed record BoundConstStatement(
    VariableSymbol Variable,
    BoundExpression Initializer,
    SmileValue Value)
    : BoundStatement;

public sealed record BoundDimStatement(VariableSymbol Variable)
    : BoundStatement;

public sealed record BoundRoutineDeclaration(
    RoutineSymbol Symbol,
    IReadOnlyList<BoundSourceItem> SourceItems,
    IReadOnlyList<VariableSymbol> Locals)
{
    public IReadOnlyList<BoundStatement> Statements => SourceItems.OfType<BoundStatement>().ToArray();
}

public sealed record BoundArraySetStatement(
    VariableSymbol Array,
    BoundExpression Index,
    BoundExpression Value)
    : BoundStatement;

public sealed record BoundCallStatement(
    RoutineSymbol Routine,
    IReadOnlyList<BoundExpression> Arguments)
    : BoundStatement;

public sealed record BoundReturnStatement(BoundExpression? Value)
    : BoundStatement;

public sealed record BoundSelectCaseClause(
    SmileValue? Value,
    bool IsElse,
    IReadOnlyList<BoundSourceItem> SourceItems)
{
    public IReadOnlyList<BoundStatement> Statements => SourceItems.OfType<BoundStatement>().ToArray();
}

public sealed record BoundSelectStatement(
    BoundExpression Selector,
    IReadOnlyList<BoundSelectCaseClause> Cases)
    : BoundStatement;

public sealed record BoundCorePrintStatement(
    IReadOnlyList<BoundExpression> Values,
    bool SuppressNewLine)
    : BoundStatement;

public sealed record BoundConditionalClause
{
    public BoundConditionalClause(
        BoundExpression Condition,
        IReadOnlyList<BoundSourceItem> SourceItems)
    {
        this.Condition = Condition;
        this.SourceItems = SourceItems;
        Statements = SourceItems.OfType<BoundStatement>().ToArray();
    }

    public BoundExpression Condition { get; }

    public IReadOnlyList<BoundSourceItem> SourceItems { get; }

    public IReadOnlyList<BoundStatement> Statements { get; }
}

public sealed record BoundIfStatement : BoundStatement
{
    public BoundIfStatement(
        IReadOnlyList<BoundConditionalClause> Clauses,
        IReadOnlyList<BoundSourceItem> ElseSourceItems,
        bool HasElseClause)
    {
        this.Clauses = Clauses;
        this.ElseSourceItems = ElseSourceItems;
        this.HasElseClause = HasElseClause;
        ElseStatements = ElseSourceItems.OfType<BoundStatement>().ToArray();
    }

    public IReadOnlyList<BoundConditionalClause> Clauses { get; }

    public IReadOnlyList<BoundSourceItem> ElseSourceItems { get; }

    public IReadOnlyList<BoundStatement> ElseStatements { get; }

    public bool HasElseClause { get; }
}

public sealed record BoundForStatement(
    VariableSymbol Counter,
    bool DeclaresCounter,
    BoundExpression LowerBound,
    BoundExpression UpperBound,
    bool IsDescending,
    IReadOnlyList<BoundSourceItem> SourceItems)
    : BoundStatement
{
    public IReadOnlyList<BoundStatement> Statements => SourceItems.OfType<BoundStatement>().ToArray();
}

public sealed record BoundDoStatement(
    IReadOnlyList<BoundSourceItem> SourceItems,
    BoundExpression? UntilCondition)
    : BoundStatement
{
    public IReadOnlyList<BoundStatement> Statements => SourceItems.OfType<BoundStatement>().ToArray();
}

public enum BoundExitKind
{
    For,
    Do
}

public sealed record BoundExitStatement(BoundExitKind Kind)
    : BoundStatement;

public sealed record BoundEndProgramStatement()
    : BoundStatement;

public abstract record BoundExpression(SmileType Type);

public sealed record BoundErrorExpression()
    : BoundExpression(SmileType.Error);

public sealed record BoundStringLiteralExpression(string Value)
    : BoundExpression(SmileType.String);

public sealed record BoundIntegerLiteralExpression(long Value)
    : BoundExpression(SmileType.Integer);

public sealed record BoundBooleanLiteralExpression(bool Value)
    : BoundExpression(SmileType.Boolean);

public sealed record BoundVariableExpression(VariableSymbol Variable)
    : BoundExpression(Variable.Type);

public sealed record BoundArrayExpression(
    VariableSymbol Array,
    BoundExpression Index)
    : BoundExpression(Array.Type);

public sealed record BoundCallExpression(
    RoutineSymbol Routine,
    IReadOnlyList<BoundExpression> Arguments)
    : BoundExpression(Routine.ReturnType ?? SmileType.Error);

public sealed record BoundUnaryExpression(
    BoundUnaryOperator Operator,
    BoundExpression Operand,
    TextSpan OperatorSpan)
    : BoundExpression(Operator.ResultType);

public sealed record BoundBinaryExpression(
    BoundExpression Left,
    BoundBinaryOperator Operator,
    BoundExpression Right,
    TextSpan OperatorSpan)
    : BoundExpression(Operator.ResultType);

public enum BoundUnaryOperatorKind
{
    Identity,
    Negation,
    LogicalNegation
}

public sealed class BoundUnaryOperator
{
    private static readonly BoundUnaryOperator[] Operators =
    {
        new(SyntaxKind.PlusToken, BoundUnaryOperatorKind.Identity, SmileType.Integer),
        new(SyntaxKind.MinusToken, BoundUnaryOperatorKind.Negation, SmileType.Integer),
        new(SyntaxKind.NotKeyword, BoundUnaryOperatorKind.LogicalNegation, SmileType.Boolean)
    };

    private BoundUnaryOperator(
        SyntaxKind syntaxKind,
        BoundUnaryOperatorKind kind,
        SmileType operandType)
        : this(syntaxKind, kind, operandType, operandType)
    {
    }

    private BoundUnaryOperator(
        SyntaxKind syntaxKind,
        BoundUnaryOperatorKind kind,
        SmileType operandType,
        SmileType resultType)
    {
        SyntaxKind = syntaxKind;
        Kind = kind;
        OperandType = operandType;
        ResultType = resultType;
    }

    public SyntaxKind SyntaxKind { get; }

    public BoundUnaryOperatorKind Kind { get; }

    public SmileType OperandType { get; }

    public SmileType ResultType { get; }

    public static BoundUnaryOperator? Bind(SyntaxKind syntaxKind, SmileType operandType) =>
        Operators.SingleOrDefault(op => op.SyntaxKind == syntaxKind && op.OperandType == operandType);
}

public enum BoundBinaryOperatorKind
{
    Addition,
    Subtraction,
    Multiplication,
    Division,
    Modulo,
    StringConcatenation,
    Equality,
    Inequality,
    Less,
    LessOrEquals,
    Greater,
    GreaterOrEquals,
    LogicalAnd,
    LogicalOr
}

public sealed class BoundBinaryOperator
{
    private static readonly BoundBinaryOperator[] Operators =
    {
        new(SyntaxKind.PlusToken, BoundBinaryOperatorKind.Addition, SmileType.Integer),
        new(SyntaxKind.MinusToken, BoundBinaryOperatorKind.Subtraction, SmileType.Integer),
        new(SyntaxKind.StarToken, BoundBinaryOperatorKind.Multiplication, SmileType.Integer),
        new(SyntaxKind.SlashToken, BoundBinaryOperatorKind.Division, SmileType.Integer),
        new(SyntaxKind.ModKeyword, BoundBinaryOperatorKind.Modulo, SmileType.Integer),
        new(SyntaxKind.PlusToken, BoundBinaryOperatorKind.StringConcatenation, SmileType.String),

        new(SyntaxKind.EqualsToken, BoundBinaryOperatorKind.Equality, SmileType.String, SmileType.Boolean),
        new(SyntaxKind.NotEqualsToken, BoundBinaryOperatorKind.Inequality, SmileType.String, SmileType.Boolean),
        new(SyntaxKind.EqualsToken, BoundBinaryOperatorKind.Equality, SmileType.Integer, SmileType.Boolean),
        new(SyntaxKind.NotEqualsToken, BoundBinaryOperatorKind.Inequality, SmileType.Integer, SmileType.Boolean),
        new(SyntaxKind.EqualsToken, BoundBinaryOperatorKind.Equality, SmileType.Boolean, SmileType.Boolean),
        new(SyntaxKind.NotEqualsToken, BoundBinaryOperatorKind.Inequality, SmileType.Boolean, SmileType.Boolean),

        new(SyntaxKind.LessToken, BoundBinaryOperatorKind.Less, SmileType.Integer, SmileType.Boolean),
        new(SyntaxKind.LessOrEqualsToken, BoundBinaryOperatorKind.LessOrEquals, SmileType.Integer, SmileType.Boolean),
        new(SyntaxKind.GreaterToken, BoundBinaryOperatorKind.Greater, SmileType.Integer, SmileType.Boolean),
        new(SyntaxKind.GreaterOrEqualsToken, BoundBinaryOperatorKind.GreaterOrEquals, SmileType.Integer, SmileType.Boolean),

        new(SyntaxKind.AndKeyword, BoundBinaryOperatorKind.LogicalAnd, SmileType.Boolean),
        new(SyntaxKind.OrKeyword, BoundBinaryOperatorKind.LogicalOr, SmileType.Boolean)
    };

    private BoundBinaryOperator(
        SyntaxKind syntaxKind,
        BoundBinaryOperatorKind kind,
        SmileType operandType)
        : this(syntaxKind, kind, operandType, operandType, operandType)
    {
    }

    private BoundBinaryOperator(
        SyntaxKind syntaxKind,
        BoundBinaryOperatorKind kind,
        SmileType operandType,
        SmileType resultType)
        : this(syntaxKind, kind, operandType, operandType, resultType)
    {
    }

    private BoundBinaryOperator(
        SyntaxKind syntaxKind,
        BoundBinaryOperatorKind kind,
        SmileType leftType,
        SmileType rightType,
        SmileType resultType)
    {
        SyntaxKind = syntaxKind;
        Kind = kind;
        LeftType = leftType;
        RightType = rightType;
        ResultType = resultType;
    }

    public SyntaxKind SyntaxKind { get; }

    public BoundBinaryOperatorKind Kind { get; }

    public SmileType LeftType { get; }

    public SmileType RightType { get; }

    public SmileType ResultType { get; }

    public static BoundBinaryOperator? Bind(
        SyntaxKind syntaxKind,
        SmileType leftType,
        SmileType rightType) =>
        Operators.SingleOrDefault(op =>
            op.SyntaxKind == syntaxKind &&
            op.LeftType == leftType &&
            op.RightType == rightType);
}

public sealed record BindResult(
    BoundProgram? Program,
    IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success =>
        Program is not null &&
        Diagnostics.All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error);
}

public enum StaticEvaluationKind
{
    Known,
    Unknown,
    Invalid
}

public enum SmileArithmeticErrorKind
{
    IntegerOverflow,
    DivisionByZero
}

public readonly record struct SmileArithmeticError(
    SmileArithmeticErrorKind Kind,
    TextSpan Span)
{
    public string CompileCode =>
        Kind is SmileArithmeticErrorKind.IntegerOverflow ? "SMILE1206" : "SMILE1207";

    public string RuntimeCode =>
        Kind is SmileArithmeticErrorKind.IntegerOverflow ? "SMILER1206" : "SMILER1207";

    public string Message =>
        Kind is SmileArithmeticErrorKind.IntegerOverflow
            ? "Number arithmetic overflow."
            : "Division by zero.";
}

// Static evaluation has three semantically different outcomes. Unknown means
// a correctly typed expression depends on runtime data; Invalid means a
// definitely reached source-known operation cannot produce a value. The
// runtime-failure bit prevents an otherwise-known Boolean identity from being
// folded when evaluating its left side can still fail.
public readonly record struct StaticEvaluationResult(
    StaticEvaluationKind Kind,
    SmileValue Value,
    SmileArithmeticError? Error,
    bool MayFailAtRuntime)
{
    public bool IsKnown => Kind is StaticEvaluationKind.Known;

    public bool IsUnknown => Kind is StaticEvaluationKind.Unknown;

    public bool IsInvalid => Kind is StaticEvaluationKind.Invalid;

    public static StaticEvaluationResult Known(
        SmileValue value,
        bool mayFailAtRuntime = false) =>
        new(StaticEvaluationKind.Known, value, null, mayFailAtRuntime);

    public static StaticEvaluationResult Unknown(bool mayFailAtRuntime = false) =>
        new(StaticEvaluationKind.Unknown, default, null, mayFailAtRuntime);

    public static StaticEvaluationResult Invalid(SmileArithmeticError error) =>
        new(StaticEvaluationKind.Invalid, default, error, MayFailAtRuntime: true);
}

public static class BoundExpressionEvaluator
{
    public static StaticEvaluationResult Evaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values) =>
        expression switch
        {
            BoundStringLiteralExpression literal =>
                StaticEvaluationResult.Known(SmileValue.FromString(literal.Value)),
            BoundIntegerLiteralExpression literal =>
                StaticEvaluationResult.Known(SmileValue.FromInteger(literal.Value)),
            BoundBooleanLiteralExpression literal =>
                StaticEvaluationResult.Known(SmileValue.FromBoolean(literal.Value)),
            BoundVariableExpression variable when values.TryGetValue(variable.Variable, out SmileValue value) =>
                StaticEvaluationResult.Known(value),
            BoundVariableExpression => StaticEvaluationResult.Unknown(),
            BoundUnaryExpression unary => EvaluateUnary(unary, values),
            BoundBinaryExpression binary => EvaluateBinary(binary, values),
            _ => StaticEvaluationResult.Unknown()
        };

    // A result that is known only on successful runtime paths is deliberately
    // not returned as a foldable value when reaching it may itself fail.
    public static bool TryEvaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out SmileValue value,
        ICollection<Diagnostic>? diagnostics = null)
    {
        StaticEvaluationResult result = Evaluate(expression, values);
        if (result.IsInvalid && result.Error is SmileArithmeticError error)
        {
            diagnostics?.Add(new Diagnostic(
                error.CompileCode,
                DiagnosticSeverity.Error,
                error.Message,
                error.Span));
        }

        if (result.IsKnown && !result.MayFailAtRuntime)
        {
            value = result.Value;
            return true;
        }

        value = default;
        return false;
    }

    private static StaticEvaluationResult EvaluateUnary(
        BoundUnaryExpression unary,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        StaticEvaluationResult operand = Evaluate(unary.Operand, values);
        if (operand.IsInvalid)
        {
            return operand;
        }

        if (!operand.IsKnown)
        {
            bool mayFail = operand.MayFailAtRuntime ||
                unary.Operator.Kind is BoundUnaryOperatorKind.Negation;
            return StaticEvaluationResult.Unknown(mayFail);
        }

        try
        {
            SmileValue value = unary.Operator.Kind switch
            {
                BoundUnaryOperatorKind.Identity => operand.Value,
                BoundUnaryOperatorKind.Negation =>
                    SmileValue.FromInteger(checked(-operand.Value.IntegerValue)),
                BoundUnaryOperatorKind.LogicalNegation =>
                    SmileValue.FromBoolean(!operand.Value.BooleanValue),
                _ => default
            };
            return StaticEvaluationResult.Known(value, operand.MayFailAtRuntime);
        }
        catch (OverflowException)
        {
            return operand.MayFailAtRuntime
                ? StaticEvaluationResult.Unknown(mayFailAtRuntime: true)
                : StaticEvaluationResult.Invalid(new SmileArithmeticError(
                    SmileArithmeticErrorKind.IntegerOverflow,
                    unary.OperatorSpan));
        }
    }

    private static StaticEvaluationResult EvaluateBinary(
        BoundBinaryExpression binary,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        StaticEvaluationResult left = Evaluate(binary.Left, values);
        if (left.IsInvalid)
        {
            return left;
        }

        if (binary.Operator.Kind is
            BoundBinaryOperatorKind.LogicalAnd or
            BoundBinaryOperatorKind.LogicalOr)
        {
            return EvaluateLogical(binary, left, values);
        }

        StaticEvaluationResult right = Evaluate(binary.Right, values);
        if (right.IsInvalid)
        {
            // Ordinary binary operands are evaluated left to right. A failure
            // in the right operand is compile-time definite only when the left
            // operand itself cannot terminate first at runtime.
            return left.MayFailAtRuntime
                ? StaticEvaluationResult.Unknown(mayFailAtRuntime: true)
                : right;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.Division or BoundBinaryOperatorKind.Modulo &&
            right.IsKnown &&
            right.Value.IntegerValue == 0)
        {
            return left.MayFailAtRuntime || right.MayFailAtRuntime
                ? StaticEvaluationResult.Unknown(mayFailAtRuntime: true)
                : StaticEvaluationResult.Invalid(new SmileArithmeticError(
                    SmileArithmeticErrorKind.DivisionByZero,
                binary.OperatorSpan));
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.Multiplication &&
            ((left.IsKnown && left.Value.IntegerValue == 0) ||
             (right.IsKnown && right.Value.IntegerValue == 0)))
        {
            // Both operands are still evaluated, so retain any earlier runtime
            // failure possibility. On every successful path, however, the
            // multiplication's value is exactly zero.
            return StaticEvaluationResult.Known(
                SmileValue.FromInteger(0),
                left.MayFailAtRuntime || right.MayFailAtRuntime);
        }

        if (!left.IsKnown || !right.IsKnown)
        {
            bool operationMayFail = UnknownIntegerOperationMayFail(
                binary.Operator.Kind,
                left,
                right);
            return StaticEvaluationResult.Unknown(
                left.MayFailAtRuntime || right.MayFailAtRuntime || operationMayFail);
        }

        try
        {
            SmileValue value = ApplyBinary(binary.Operator.Kind, left.Value, right.Value);
            return StaticEvaluationResult.Known(
                value,
                left.MayFailAtRuntime || right.MayFailAtRuntime);
        }
        catch (DivideByZeroException)
        {
            return left.MayFailAtRuntime || right.MayFailAtRuntime
                ? StaticEvaluationResult.Unknown(mayFailAtRuntime: true)
                : StaticEvaluationResult.Invalid(new SmileArithmeticError(
                    SmileArithmeticErrorKind.DivisionByZero,
                    binary.OperatorSpan));
        }
        catch (OverflowException)
        {
            return left.MayFailAtRuntime || right.MayFailAtRuntime
                ? StaticEvaluationResult.Unknown(mayFailAtRuntime: true)
                : StaticEvaluationResult.Invalid(new SmileArithmeticError(
                    SmileArithmeticErrorKind.IntegerOverflow,
                    binary.OperatorSpan));
        }
    }

    private static bool UnknownIntegerOperationMayFail(
        BoundBinaryOperatorKind kind,
        StaticEvaluationResult left,
        StaticEvaluationResult right)
    {
        bool LeftIs(long value) => left.IsKnown && left.Value.IntegerValue == value;
        bool RightIs(long value) => right.IsKnown && right.Value.IntegerValue == value;

        return kind switch
        {
            // These remaining identities cannot overflow. Multiplication by
            // zero is handled above because it also proves the result value.
            BoundBinaryOperatorKind.Addition => !(LeftIs(0) || RightIs(0)),
            BoundBinaryOperatorKind.Subtraction => !RightIs(0),
            BoundBinaryOperatorKind.Multiplication =>
                !(LeftIs(0) || RightIs(0) || LeftIs(1) || RightIs(1)),
            BoundBinaryOperatorKind.Division =>
                !right.IsKnown || right.Value.IntegerValue is 0 or -1,
            BoundBinaryOperatorKind.Modulo =>
                !right.IsKnown || right.Value.IntegerValue is 0 or -1,
            _ => false
        };
    }

    private static StaticEvaluationResult EvaluateLogical(
        BoundBinaryExpression binary,
        StaticEvaluationResult left,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values)
    {
        bool isAnd = binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd;
        if (left.IsKnown)
        {
            bool shortCircuits = isAnd
                ? !left.Value.BooleanValue
                : left.Value.BooleanValue;
            if (shortCircuits)
            {
                return StaticEvaluationResult.Known(
                    SmileValue.FromBoolean(left.Value.BooleanValue),
                    left.MayFailAtRuntime);
            }

            StaticEvaluationResult reachedRight = Evaluate(binary.Right, values);
            if (reachedRight.IsInvalid)
            {
                return left.MayFailAtRuntime
                    ? StaticEvaluationResult.Unknown(mayFailAtRuntime: true)
                    : reachedRight;
            }

            if (reachedRight.IsKnown)
            {
                return StaticEvaluationResult.Known(
                    SmileValue.FromBoolean(reachedRight.Value.BooleanValue),
                    left.MayFailAtRuntime || reachedRight.MayFailAtRuntime);
            }

            return StaticEvaluationResult.Unknown(
                left.MayFailAtRuntime || reachedRight.MayFailAtRuntime);
        }

        // With an unknown left value, the right side is only conditionally
        // reached. Even a source-known failure there is therefore a runtime
        // possibility rather than an unconditional compiler diagnostic.
        StaticEvaluationResult right = Evaluate(binary.Right, values);
        if (right.IsInvalid)
        {
            return StaticEvaluationResult.Unknown(mayFailAtRuntime: true);
        }

        bool mayFail = left.MayFailAtRuntime || right.MayFailAtRuntime;
        if (right.IsKnown &&
            ((isAnd && !right.Value.BooleanValue) ||
             (!isAnd && right.Value.BooleanValue)))
        {
            return StaticEvaluationResult.Known(
                SmileValue.FromBoolean(right.Value.BooleanValue),
                mayFail);
        }

        return StaticEvaluationResult.Unknown(mayFail);
    }

    private static SmileValue ApplyBinary(
        BoundBinaryOperatorKind kind,
        SmileValue left,
        SmileValue right) =>
        kind switch
        {
            BoundBinaryOperatorKind.Addition =>
                SmileValue.FromInteger(checked(left.IntegerValue + right.IntegerValue)),
            BoundBinaryOperatorKind.Subtraction =>
                SmileValue.FromInteger(checked(left.IntegerValue - right.IntegerValue)),
            BoundBinaryOperatorKind.Multiplication =>
                SmileValue.FromInteger(checked(left.IntegerValue * right.IntegerValue)),
            BoundBinaryOperatorKind.Division =>
                SmileValue.FromInteger(checked(left.IntegerValue / right.IntegerValue)),
            BoundBinaryOperatorKind.Modulo =>
                SmileValue.FromInteger(checked(left.IntegerValue % right.IntegerValue)),
            BoundBinaryOperatorKind.StringConcatenation =>
                SmileValue.FromString(left.StringValue + right.StringValue),
            BoundBinaryOperatorKind.Equality =>
                SmileValue.FromBoolean(ValuesEqual(left, right)),
            BoundBinaryOperatorKind.Inequality =>
                SmileValue.FromBoolean(!ValuesEqual(left, right)),
            BoundBinaryOperatorKind.Less =>
                SmileValue.FromBoolean(left.IntegerValue < right.IntegerValue),
            BoundBinaryOperatorKind.LessOrEquals =>
                SmileValue.FromBoolean(left.IntegerValue <= right.IntegerValue),
            BoundBinaryOperatorKind.Greater =>
                SmileValue.FromBoolean(left.IntegerValue > right.IntegerValue),
            BoundBinaryOperatorKind.GreaterOrEquals =>
                SmileValue.FromBoolean(left.IntegerValue >= right.IntegerValue),
            BoundBinaryOperatorKind.LogicalAnd =>
                SmileValue.FromBoolean(left.BooleanValue && right.BooleanValue),
            BoundBinaryOperatorKind.LogicalOr =>
                SmileValue.FromBoolean(left.BooleanValue || right.BooleanValue),
            _ => default
        };

    private static bool ValuesEqual(SmileValue left, SmileValue right) =>
        left.Type switch
        {
            SmileType.String => string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal),
            SmileType.Integer => left.IntegerValue == right.IntegerValue,
            SmileType.Boolean => left.BooleanValue == right.BooleanValue,
            _ => false
        };
}

public enum TargetLanguage
{
    CSharp,
    C,
    MasmX64,
    JavaScript,
    Java,
    Cobol,
    ObjectiveC,
    Swift,
    Python,
    Cpp
}

// Product surfaces share this one active-target policy. All implemented
// destinations are active again, so the active set is the complete catalog.
public static class ActiveTargetLanguages
{
    public static readonly IReadOnlyList<TargetLanguage> All = TargetLanguageInfo.All;

    public static bool IsActive(TargetLanguage language) => All.Contains(language);
}

// Stable IDs are for CLI arguments and saved data. Display names are for users.
public static class TargetLanguageInfo
{
    public static readonly IReadOnlyList<TargetLanguage> All = new ReadOnlyCollection<TargetLanguage>(
        new[]
        {
            TargetLanguage.CSharp,
            TargetLanguage.C,
            TargetLanguage.MasmX64,
            TargetLanguage.JavaScript,
            TargetLanguage.Java,
            TargetLanguage.Cobol,
            TargetLanguage.ObjectiveC,
            TargetLanguage.Swift,
            TargetLanguage.Python,
            TargetLanguage.Cpp
        });

    public static string GetStableId(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp => "csharp",
            TargetLanguage.C => "c",
            TargetLanguage.MasmX64 => "masm-x64",
            TargetLanguage.JavaScript => "javascript",
            TargetLanguage.Java => "java",
            TargetLanguage.Cobol => "cobol",
            TargetLanguage.ObjectiveC => "objective-c",
            TargetLanguage.Swift => "swift",
            TargetLanguage.Python => "python",
            TargetLanguage.Cpp => "cpp",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    public static string GetDisplayName(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp => "C#",
            TargetLanguage.C => "C",
            TargetLanguage.MasmX64 => "Assembly - Windows x64 MASM",
            TargetLanguage.JavaScript => "JavaScript (Node.js)",
            TargetLanguage.Java => "Java",
            TargetLanguage.Cobol => "COBOL",
            TargetLanguage.ObjectiveC => "Objective-C",
            TargetLanguage.Swift => "Swift",
            TargetLanguage.Python => "Python",
            TargetLanguage.Cpp => "C++",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    public static string GetPrimaryFileName(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp => "Program.cs",
            TargetLanguage.C => "Program.c",
            TargetLanguage.MasmX64 => "Program.asm",
            TargetLanguage.JavaScript => "Program.js",
            TargetLanguage.Java => "Program.java",
            TargetLanguage.Cobol => "Program.cob",
            TargetLanguage.ObjectiveC => "Program.m",
            TargetLanguage.Swift => "Program.swift",
            TargetLanguage.Python => "Program.py",
            TargetLanguage.Cpp => "Program.cpp",
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

    public static bool TryParse(string text, out TargetLanguage language)
    {
        foreach (TargetLanguage candidate in All)
        {
            if (string.Equals(text, GetStableId(candidate), StringComparison.OrdinalIgnoreCase))
            {
                language = candidate;
                return true;
            }
        }

        language = default;
        return false;
    }
}
