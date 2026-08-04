using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

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

public sealed record SmileProgramSyntax(
    IReadOnlyList<StatementSyntax> Statements,
    TextSpan Span)
    : SyntaxNode(Span);

public abstract record StatementSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record PrintStatementSyntax(
    ExpressionSyntax Value,
    TextSpan Span,
    bool IsBlankLine = false)
    : StatementSyntax(Span);

public sealed record LetStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Initializer,
    TextSpan Span)
    : StatementSyntax(Span);

public sealed record SetStatementSyntax(
    string Name,
    TextSpan NameSpan,
    ExpressionSyntax Value,
    TextSpan Span)
    : StatementSyntax(Span);

public abstract record ExpressionSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record ErrorExpressionSyntax(TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record StringLiteralExpressionSyntax(
    string Value,
    TextSpan Span)
    : ExpressionSyntax(Span);

// The dedicated syntax form lets the parser enforce SET-only placement while
// the binder still lowers the already-normalized value to the one canonical
// bound String literal used by every target.
public sealed record BlockStringLiteralExpressionSyntax(
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

public sealed record InterpolatedStringExpressionSyntax(
    IReadOnlyList<InterpolatedPartSyntax> Parts,
    TextSpan Span)
    : ExpressionSyntax(Span);

public abstract record InterpolatedPartSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record InterpolatedTextPartSyntax(
    string Text,
    TextSpan Span)
    : InterpolatedPartSyntax(Span);

public sealed record InterpolationExpressionPartSyntax(
    ExpressionSyntax Expression,
    TextSpan Span)
    : InterpolatedPartSyntax(Span);

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
            SmileType.Boolean => BooleanValue ? "TRUE" : "FALSE",
            _ => string.Empty
        };
}

public sealed record VariableSymbol(
    string Name,
    TextSpan DeclarationSpan,
    SmileType Type);

// Bound nodes describe what the program means after name lookup and type
// checking. Generators consume this layer so no backend has to reparse SMILE.
public sealed record BoundProgram(
    IReadOnlyList<BoundStatement> Statements,
    IReadOnlyList<VariableSymbol> Variables);

public abstract record BoundStatement;

public sealed record BoundLetStatement(
    VariableSymbol Variable,
    BoundExpression Initializer)
    : BoundStatement;

public sealed record BoundSetStatement(
    VariableSymbol Variable,
    BoundExpression Value)
    : BoundStatement;

public sealed record BoundPrintStatement(
    BoundExpression Value,
    bool IsBlankLine = false)
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

public sealed record BoundInterpolatedStringExpression(
    IReadOnlyList<BoundInterpolatedPart> Parts)
    : BoundExpression(SmileType.String);

public abstract record BoundInterpolatedPart;

public sealed record BoundInterpolatedTextPart(string Text)
    : BoundInterpolatedPart;

public sealed record BoundInterpolationExpressionPart(BoundExpression Expression)
    : BoundInterpolatedPart;

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

public abstract record PrintSegment;

public sealed record LiteralPrintSegment(string Text)
    : PrintSegment;

public sealed record VariablePrintSegment(VariableSymbol Variable)
    : PrintSegment;

public static class BoundStringExpression
{
    public static IReadOnlyList<PrintSegment> FlattenForOutput(BoundExpression expression)
    {
        var segments = new List<PrintSegment>();
        Append(expression, segments);
        return segments;
    }

    public static IReadOnlyList<PrintSegment> Flatten(BoundExpression expression) =>
        FlattenForOutput(expression);

    private static void Append(BoundExpression expression, List<PrintSegment> segments)
    {
        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                if (literal.Value.Length > 0)
                {
                    segments.Add(new LiteralPrintSegment(literal.Value));
                }

                break;

            case BoundVariableExpression variable:
                segments.Add(new VariablePrintSegment(variable.Variable));
                break;

            case BoundBinaryExpression { Operator.Kind: BoundBinaryOperatorKind.StringConcatenation } binary:
                Append(binary.Left, segments);
                Append(binary.Right, segments);
                break;

            case BoundInterpolatedStringExpression interpolated:
                foreach (BoundInterpolatedPart part in interpolated.Parts)
                {
                    switch (part)
                    {
                        case BoundInterpolatedTextPart textPart:
                            if (textPart.Text.Length > 0)
                            {
                                segments.Add(new LiteralPrintSegment(textPart.Text));
                            }

                            break;

                        case BoundInterpolationExpressionPart expressionPart:
                            Append(expressionPart.Expression, segments);
                            break;
                    }
                }

                break;
        }
    }
}

public static class BoundExpressionEvaluator
{
    public static bool TryEvaluate(
        BoundExpression expression,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out SmileValue value,
        ICollection<Diagnostic>? diagnostics = null)
    {
        switch (expression)
        {
            case BoundStringLiteralExpression literal:
                value = SmileValue.FromString(literal.Value);
                return true;

            case BoundIntegerLiteralExpression literal:
                value = SmileValue.FromInteger(literal.Value);
                return true;

            case BoundBooleanLiteralExpression literal:
                value = SmileValue.FromBoolean(literal.Value);
                return true;

            case BoundVariableExpression variable:
                return values.TryGetValue(variable.Variable, out value);

            case BoundUnaryExpression unary:
                return TryEvaluateUnary(unary, values, out value, diagnostics);

            case BoundBinaryExpression binary:
                return TryEvaluateBinary(binary, values, out value, diagnostics);

            case BoundInterpolatedStringExpression interpolated:
                return TryEvaluateInterpolatedString(interpolated, values, out value, diagnostics);

            default:
                value = default;
                return false;
        }
    }

    private static bool TryEvaluateUnary(
        BoundUnaryExpression unary,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out SmileValue value,
        ICollection<Diagnostic>? diagnostics)
    {
        if (!TryEvaluate(unary.Operand, values, out SmileValue operand, diagnostics))
        {
            value = default;
            return false;
        }

        try
        {
            value = unary.Operator.Kind switch
            {
                BoundUnaryOperatorKind.Identity => operand,
                BoundUnaryOperatorKind.Negation => SmileValue.FromInteger(checked(-operand.IntegerValue)),
                BoundUnaryOperatorKind.LogicalNegation => SmileValue.FromBoolean(!operand.BooleanValue),
                _ => default
            };
            return true;
        }
        catch (OverflowException)
        {
            AddDiagnostic(
                diagnostics,
                "SMILE1206",
                "Integer arithmetic overflow.",
                unary.OperatorSpan);
            value = default;
            return false;
        }
    }

    private static bool TryEvaluateBinary(
        BoundBinaryExpression binary,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out SmileValue value,
        ICollection<Diagnostic>? diagnostics)
    {
        if (!TryEvaluate(binary.Left, values, out SmileValue left, diagnostics))
        {
            value = default;
            return false;
        }

        // Logical operands are evaluated left to right. The binder has already
        // resolved and type-checked both sides, but an unreachable right side
        // must not produce evaluation-time failures such as division by zero.
        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd && !left.BooleanValue)
        {
            value = SmileValue.FromBoolean(false);
            return true;
        }

        if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalOr && left.BooleanValue)
        {
            value = SmileValue.FromBoolean(true);
            return true;
        }

        if (!TryEvaluate(binary.Right, values, out SmileValue right, diagnostics))
        {
            value = default;
            return false;
        }

        try
        {
            if (binary.Operator.Kind is BoundBinaryOperatorKind.Division &&
                right.IntegerValue == 0)
            {
                AddDiagnostic(
                    diagnostics,
                    "SMILE1207",
                    "Division by zero.",
                    binary.OperatorSpan);
                value = default;
                return false;
            }

            value = binary.Operator.Kind switch
            {
                BoundBinaryOperatorKind.Addition => SmileValue.FromInteger(checked(left.IntegerValue + right.IntegerValue)),
                BoundBinaryOperatorKind.Subtraction => SmileValue.FromInteger(checked(left.IntegerValue - right.IntegerValue)),
                BoundBinaryOperatorKind.Multiplication => SmileValue.FromInteger(checked(left.IntegerValue * right.IntegerValue)),
                BoundBinaryOperatorKind.Division => SmileValue.FromInteger(checked(left.IntegerValue / right.IntegerValue)),
                BoundBinaryOperatorKind.StringConcatenation => SmileValue.FromString(left.StringValue + right.StringValue),
                BoundBinaryOperatorKind.Equality => SmileValue.FromBoolean(ValuesEqual(left, right)),
                BoundBinaryOperatorKind.Inequality => SmileValue.FromBoolean(!ValuesEqual(left, right)),
                BoundBinaryOperatorKind.Less => SmileValue.FromBoolean(left.IntegerValue < right.IntegerValue),
                BoundBinaryOperatorKind.LessOrEquals => SmileValue.FromBoolean(left.IntegerValue <= right.IntegerValue),
                BoundBinaryOperatorKind.Greater => SmileValue.FromBoolean(left.IntegerValue > right.IntegerValue),
                BoundBinaryOperatorKind.GreaterOrEquals => SmileValue.FromBoolean(left.IntegerValue >= right.IntegerValue),
                BoundBinaryOperatorKind.LogicalAnd => SmileValue.FromBoolean(left.BooleanValue && right.BooleanValue),
                BoundBinaryOperatorKind.LogicalOr => SmileValue.FromBoolean(left.BooleanValue || right.BooleanValue),
                _ => default
            };
            return value.Type is not SmileType.Error;
        }
        catch (OverflowException)
        {
            AddDiagnostic(
                diagnostics,
                "SMILE1206",
                "Integer arithmetic overflow.",
                binary.OperatorSpan);
            value = default;
            return false;
        }
    }

    private static bool TryEvaluateInterpolatedString(
        BoundInterpolatedStringExpression interpolated,
        IReadOnlyDictionary<VariableSymbol, SmileValue> values,
        out SmileValue value,
        ICollection<Diagnostic>? diagnostics)
    {
        var builder = new StringBuilder();
        foreach (BoundInterpolatedPart part in interpolated.Parts)
        {
            switch (part)
            {
                case BoundInterpolatedTextPart text:
                    builder.Append(text.Text);
                    break;

                case BoundInterpolationExpressionPart interpolation:
                    if (!TryEvaluate(interpolation.Expression, values, out SmileValue partValue, diagnostics))
                    {
                        value = default;
                        return false;
                    }

                    builder.Append(partValue.ToDisplayText());
                    break;
            }
        }

        value = SmileValue.FromString(builder.ToString());
        return true;
    }

    private static bool ValuesEqual(SmileValue left, SmileValue right) =>
        left.Type switch
        {
            SmileType.String => string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal),
            SmileType.Integer => left.IntegerValue == right.IntegerValue,
            SmileType.Boolean => left.BooleanValue == right.BooleanValue,
            _ => false
        };

    private static void AddDiagnostic(
        ICollection<Diagnostic>? diagnostics,
        string code,
        string message,
        TextSpan span)
    {
        diagnostics?.Add(new Diagnostic(code, DiagnosticSeverity.Error, message, span));
    }
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
            TargetLanguage.JavaScript => "JavaScript",
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
