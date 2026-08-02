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

// Tokens are the small pieces the lexer recognizes. The parser consumes these
// tokens and builds a syntax tree from them.
public enum SyntaxKind
{
    BadToken,
    EndOfFileToken,
    NewLineToken,
    PrintKeyword,
    StringLiteralToken
}

// The syntax tree is deliberately language-neutral. Target generators should
// understand SMILE statements, not the syntax of another generated language.
public abstract record SyntaxNode(TextSpan Span);

public sealed record SmileProgramSyntax(
    IReadOnlyList<StatementSyntax> Statements,
    TextSpan Span)
    : SyntaxNode(Span);

public abstract record StatementSyntax(TextSpan Span)
    : SyntaxNode(Span);

public sealed record PrintStatementSyntax(
    string Text,
    TextSpan Span)
    : StatementSyntax(Span);

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

internal sealed record SyntaxToken(
    SyntaxKind Kind,
    string Text,
    string? Value,
    TextSpan Span,
    bool HasError);
