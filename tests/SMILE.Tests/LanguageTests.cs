using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class LanguageTests
{
    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    [DataRow("PRINT Hello", "Hello")]
    [DataRow("Print Hello", "Hello")]
    [DataRow("print Hello", "Hello")]
    [DataRow("pRiNt Hello", "Hello")]
    [DataRow("PRINT \"Hello\"", "Hello")]
    [DataRow("PRINT    Hello", "Hello")]
    [DataRow("PRINT\tHello", "Hello")]
    [DataRow("PRINT \u201cHello\u201d", "Hello")]
    public void Parser_accepts_keyword_casing_and_print_forms(string source, string expectedText)
    {
        PrintStatementSyntax statement = ParseSinglePrint(source);

        var literal = (StringLiteralExpressionSyntax)statement.Value;
        Assert.AreEqual(expectedText, literal.Value);
    }

    [TestMethod]
    [DataRow("PRINT", "")]
    [DataRow("PRINT    ", "")]
    public void Parser_treats_blank_print_as_empty_string(string source, string expectedText)
    {
        PrintStatementSyntax statement = ParseSinglePrint(source);

        var literal = (StringLiteralExpressionSyntax)statement.Value;
        Assert.IsTrue(statement.IsBlankLine);
        Assert.AreEqual(expectedText, literal.Value);
    }

    [TestMethod]
    public void Parser_keeps_blank_print_distinct_from_quoted_empty_string()
    {
        PrintStatementSyntax statement = ParseSinglePrint("PRINT \"\"");

        var literal = (StringLiteralExpressionSyntax)statement.Value;
        Assert.IsFalse(statement.IsBlankLine);
        Assert.AreEqual(string.Empty, literal.Value);
    }

    [TestMethod]
    public void Raw_print_with_interpolation_has_text_expression_text_parts()
    {
        PrintStatementSyntax statement = ParseSinglePrint("PRINT Hello {Name}!");

        var interpolated = (InterpolatedStringExpressionSyntax)statement.Value;
        Assert.HasCount(3, interpolated.Parts);
        Assert.AreEqual("Hello ", ((InterpolatedTextPartSyntax)interpolated.Parts[0]).Text);
        Assert.AreEqual("Name", ((NameExpressionSyntax)((InterpolationExpressionPartSyntax)interpolated.Parts[1]).Expression).Name);
        Assert.AreEqual("!", ((InterpolatedTextPartSyntax)interpolated.Parts[2]).Text);
    }

    [TestMethod]
    public void Interpolated_quoted_print_uses_the_same_interpolated_syntax_shape()
    {
        PrintStatementSyntax statement = ParseSinglePrint("PRINT $\"Hello {Name}!\"");

        var interpolated = (InterpolatedStringExpressionSyntax)statement.Value;
        Assert.HasCount(3, interpolated.Parts);
        Assert.AreEqual("Hello ", ((InterpolatedTextPartSyntax)interpolated.Parts[0]).Text);
        Assert.AreEqual("Name", ((NameExpressionSyntax)((InterpolationExpressionPartSyntax)interpolated.Parts[1]).Expression).Name);
        Assert.AreEqual("!", ((InterpolatedTextPartSyntax)interpolated.Parts[2]).Text);
    }

    [TestMethod]
    public void Quoted_expression_with_plus_is_left_associative_concatenation()
    {
        PrintStatementSyntax statement = ParseSinglePrint("PRINT \"Hello \" + Name + \"!\"");

        var outer = (BinaryExpressionSyntax)statement.Value;
        Assert.IsInstanceOfType(outer.Left, typeof(BinaryExpressionSyntax));
        Assert.AreEqual("!", ((StringLiteralExpressionSyntax)outer.Right).Value);
    }

    [TestMethod]
    public void Binder_resolves_identifiers_case_insensitively_and_preserves_declaration_spelling()
    {
        BindResult result = _transpiler.Bind("""
LET CustomerName = "Sin"
PRINT {customername}
PRINT {CUSTOMERNAME}
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        VariableSymbol variable = result.Program!.Variables.Single();
        Assert.AreEqual("CustomerName", variable.Name);

        var firstPrint = (BoundPrintStatement)result.Program.Statements[1];
        var firstVariable = (BoundVariableExpression)firstPrint.Value;
        Assert.AreSame(variable, firstVariable.Variable);
    }

    [TestMethod]
    public void Binder_preserves_expression_intent_before_optional_output_flattening()
    {
        BindResult raw = _transpiler.Bind("""
LET Name = "Sin"
PRINT Hello {Name}!
""");
        BindResult quoted = _transpiler.Bind("""
LET Name = "Sin"
PRINT $"Hello {Name}!"
""");
        BindResult concatenated = _transpiler.Bind("""
LET Name = "Sin"
PRINT "Hello " + Name + "!"
""");

        Assert.IsInstanceOfType(GetPrintExpression(raw), typeof(BoundInterpolatedStringExpression));
        Assert.IsInstanceOfType(GetPrintExpression(quoted), typeof(BoundInterpolatedStringExpression));
        Assert.IsInstanceOfType(GetPrintExpression(concatenated), typeof(BoundBinaryExpression));

        AssertSegments(raw, "Hello ", "Name", "!");
        AssertSegments(quoted, "Hello ", "Name", "!");
        AssertSegments(concatenated, "Hello ", "Name", "!");
    }

    [TestMethod]
    public void Binder_keeps_bare_words_literal_and_braced_names_evaluated()
    {
        BindResult result = _transpiler.Bind("""
LET Name = "Sin"
PRINT Name
PRINT {Name}
""");

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));

        var literalPrint = (BoundPrintStatement)result.Program!.Statements[1];
        var evaluatedPrint = (BoundPrintStatement)result.Program.Statements[2];

        AssertSegments(literalPrint.Value, "Name");
        AssertSegments(evaluatedPrint.Value, "Name");
        Assert.IsInstanceOfType(BoundStringExpression.FlattenForOutput(literalPrint.Value).Single(), typeof(LiteralPrintSegment));
        Assert.IsInstanceOfType(BoundStringExpression.FlattenForOutput(evaluatedPrint.Value).Single(), typeof(VariablePrintSegment));
    }

    [TestMethod]
    [DataRow("PRINT Use {{Name}}")]
    [DataRow("PRINT $\"Use {{Name}}\"")]
    public void Binder_preserves_literal_braces_in_template_forms(string source)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var print = (BoundPrintStatement)result.Program!.Statements.Single();
        AssertSegments(print.Value, "Use {Name}");
    }

    [TestMethod]
    [DataRow("PRINT\"Hello\"", "SMILE1101", 1, 6)]
    [DataRow("PRINT$\"Hello\"", "SMILE1101", 1, 6)]
    [DataRow("PRINT Hello PRINT World", "SMILE1102", 1, 13)]
    [DataRow("print Hello PrInT World", "SMILE1102", 1, 13)]
    [DataRow("PRINT \"Hello\"; PRINT \"World\"", "SMILE1102", 1, 16)]
    [DataRow("PRINT Use PRINT to display text.", "SMILE1102", 1, 11)]
    [DataRow("PRINT Hello {", "SMILE1103", 1, 13)]
    [DataRow("PRINT Hello {}", "SMILE1105", 1, 13)]
    [DataRow("PRINT Hello {Name", "SMILE1103", 1, 13)]
    [DataRow("PRINT Hello Name}", "SMILE1104", 1, 17)]
    [DataRow("PRINT $\"Hello {Name\"", "SMILE1103", 1, 15)]
    [DataRow("PRINT $\"Hello }\"", "SMILE1104", 1, 15)]
    [DataRow("PRINT \"Hello\" +", "SMILE1201", 1, 16)]
    [DataRow("PRINT \"A\"; \"B\"", "SMILE1109", 1, 10)]
    [DataRow("PRINT \"Hello\" \"World\"", "SMILE1111", 1, 15)]
    [DataRow("PRONT \"Typo\"", "SMILE1001", 1, 1)]
    [DataRow("@", "SMILE1005", 1, 1)]
    public void Parser_reports_friendly_diagnostics_without_throwing(
        string source,
        string expectedCode,
        int expectedLine,
        int expectedColumn)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.First(d => d.Code == expectedCode);
        Assert.AreEqual(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.AreEqual(expectedLine, diagnostic.Span.Line);
        Assert.AreEqual(expectedColumn, diagnostic.Span.Column);
    }

    [TestMethod]
    [DataRow("PRINT \"Use PRINT to display text.\"")]
    [DataRow("PRINT Reprint this report.")]
    [DataRow("PRINT PRINTABLE text.")]
    [DataRow("PRINT Use \"PRINT\" as the command name.")]
    [DataRow("PRINT Use {\"PRINT\"} as the command name.")]
    [DataRow("PRINT A; B; C")]
    public void Parser_allows_print_keyword_text_when_it_is_not_a_second_statement(string source)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
    }

    [TestMethod]
    [DataRow("PRINT Hello {MissingName}!", "SMILE1106", 1, 14)]
    [DataRow("PRINT \"Hello\" + MissingName", "SMILE1106", 1, 17)]
    [DataRow("LET Name = \"Sin\"\nLET NAME = \"Joy\"", "SMILE1107", 2, 5)]
    public void Binder_reports_semantic_diagnostics_without_throwing(
        string source,
        string expectedCode,
        int expectedLine,
        int expectedColumn)
    {
        BindResult result = _transpiler.Bind(source);

        Assert.IsFalse(result.Success);
        Diagnostic diagnostic = result.Diagnostics.Single(d => d.Code == expectedCode);
        Assert.AreEqual(expectedLine, diagnostic.Span.Line);
        Assert.AreEqual(expectedColumn, diagnostic.Span.Column);
    }

    [TestMethod]
    public void Transpile_many_reports_the_same_diagnostics_for_each_target()
    {
        IReadOnlyList<TranspileResult> results = _transpiler.TranspileMany(
            "PRINT Hello {MissingName}!",
            TargetLanguageInfo.All);

        Assert.HasCount(TargetLanguageInfo.All.Count, results);
        Assert.IsTrue(results.All(result => !result.Success));
        Assert.IsTrue(results.All(result => result.GeneratedProgram is null));
        Assert.IsTrue(results.All(result => result.Diagnostics.Any(diagnostic => diagnostic.Code == "SMILE1106")));
    }

    [TestMethod]
    [DataRow("csharp", TargetLanguage.CSharp, "C#", "Program.cs")]
    [DataRow("c", TargetLanguage.C, "C", "Program.c")]
    [DataRow("masm-x64", TargetLanguage.MasmX64, "Assembly - Windows x64 MASM", "Program.asm")]
    [DataRow("javascript", TargetLanguage.JavaScript, "JavaScript", "Program.js")]
    [DataRow("java", TargetLanguage.Java, "Java", "Program.java")]
    [DataRow("cobol", TargetLanguage.Cobol, "COBOL", "Program.cob")]
    [DataRow("objective-c", TargetLanguage.ObjectiveC, "Objective-C", "Program.m")]
    [DataRow("swift", TargetLanguage.Swift, "Swift", "Program.swift")]
    [DataRow("python", TargetLanguage.Python, "Python", "Program.py")]
    [DataRow("cpp", TargetLanguage.Cpp, "C++", "Program.cpp")]
    public void Target_language_metadata_is_stable(
        string stableId,
        TargetLanguage language,
        string displayName,
        string primaryFileName)
    {
        Assert.IsTrue(TargetLanguageInfo.TryParse(stableId, out TargetLanguage parsed));
        Assert.AreEqual(language, parsed);
        Assert.AreEqual(stableId, TargetLanguageInfo.GetStableId(language));
        Assert.AreEqual(displayName, TargetLanguageInfo.GetDisplayName(language));
        Assert.AreEqual(primaryFileName, TargetLanguageInfo.GetPrimaryFileName(language));
    }

    private PrintStatementSyntax ParseSinglePrint(string source)
    {
        ParseResult result = _transpiler.Parse(source);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return (PrintStatementSyntax)result.Program!.Statements.Single();
    }

    private static void AssertSegments(BindResult result, params string[] expected)
    {
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        AssertSegments(GetPrintExpression(result), expected);
    }

    private static void AssertSegments(BoundExpression expression, params string[] expected)
    {
        string[] actual = BoundStringExpression.FlattenForOutput(expression)
            .Select(segment => segment switch
            {
                LiteralPrintSegment literal => literal.Text,
                VariablePrintSegment variable => variable.Variable.Name,
                _ => string.Empty
            })
            .ToArray();

        CollectionAssert.AreEqual(expected, actual);
    }

    private static BoundExpression GetPrintExpression(BindResult result)
    {
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var print = (BoundPrintStatement)result.Program!.Statements[1];
        return print.Value;
    }
}
