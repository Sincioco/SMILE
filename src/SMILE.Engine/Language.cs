using System.Collections.ObjectModel;

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
// names. Keeping this layer source-shaped makes diagnostics easier to place.
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

public abstract record ExpressionSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record StringLiteralExpressionSyntax(
    string Value,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record NameExpressionSyntax(
    string Name,
    TextSpan Span)
    : ExpressionSyntax(Span);

public sealed record ConcatenationExpressionSyntax(
    ExpressionSyntax Left,
    ExpressionSyntax Right,
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
    String
}

public sealed record VariableSymbol(
    string Name,
    TextSpan DeclarationSpan,
    SmileType Type);

// Bound nodes describe what the program means after name lookup. Generators
// consume this layer so no backend has to reparse SMILE source text.
public sealed record BoundProgram(
    IReadOnlyList<BoundStatement> Statements,
    IReadOnlyList<VariableSymbol> Variables);

public abstract record BoundStatement;

public sealed record BoundLetStatement(
    VariableSymbol Variable,
    BoundExpression Initializer)
    : BoundStatement;

public sealed record BoundPrintStatement(
    BoundExpression Value,
    bool IsBlankLine = false)
    : BoundStatement;

public abstract record BoundExpression(SmileType Type);

public sealed record BoundStringLiteralExpression(string Value)
    : BoundExpression(SmileType.String);

public sealed record BoundVariableExpression(VariableSymbol Variable)
    : BoundExpression(SmileType.String);

public sealed record BoundConcatenationExpression(
    BoundExpression Left,
    BoundExpression Right)
    : BoundExpression(SmileType.String);

public sealed record BoundInterpolatedStringExpression(
    IReadOnlyList<BoundInterpolatedPart> Parts)
    : BoundExpression(SmileType.String);

public abstract record BoundInterpolatedPart;

public sealed record BoundInterpolatedTextPart(string Text)
    : BoundInterpolatedPart;

public sealed record BoundInterpolationExpressionPart(BoundExpression Expression)
    : BoundInterpolatedPart;

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
    // Some low-level targets do not have a convenient string-expression syntax.
    // They lower a bound expression into "write this literal, then this
    // variable" output segments at the last responsible moment. High-level
    // targets should keep using the expression tree so interpolation and
    // concatenation intent remains visible in generated educational code.
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

            case BoundConcatenationExpression concatenation:
                Append(concatenation.Left, segments);
                Append(concatenation.Right, segments);
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

public enum TargetLanguage
{
    CSharp,
    C,
    MasmX64,
    JavaScript,
    Java,
    ObjectiveC,
    Swift
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
            TargetLanguage.ObjectiveC,
            TargetLanguage.Swift
        });

    public static string GetStableId(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp => "csharp",
            TargetLanguage.C => "c",
            TargetLanguage.MasmX64 => "masm-x64",
            TargetLanguage.JavaScript => "javascript",
            TargetLanguage.Java => "java",
            TargetLanguage.ObjectiveC => "objective-c",
            TargetLanguage.Swift => "swift",
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
            TargetLanguage.ObjectiveC => "Objective-C",
            TargetLanguage.Swift => "Swift",
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
            TargetLanguage.ObjectiveC => "Program.m",
            TargetLanguage.Swift => "Program.swift",
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
