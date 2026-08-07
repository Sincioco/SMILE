using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class IdiomaticMultilineStringTargetTests
{
    private const string CanonicalSource = """
LET MultilineText = "
    Hello World!
    This is SMILE!
        How are you?
"

PRINT {MultilineText}
""";

    private const string RecursivePlacementSource = """
LET Message = ""
LET Route = 1
LET Outer = 0
LET Inner = 0
LET Nested = 0

IF Route = 0 THEN
    PRINT First branch
ELSE IF Route = 1 THEN
    SET Message = "
ElseIf
Value
"
ELSE
    SET Message = "
Else
Value
"
END IF

WHILE Outer < 1
    WHILE Inner < 1
        SET Message = "
Nested
While
"
        SET Inner = Inner + 1
    END WHILE
    SET Outer = Outer + 1
END WHILE

IF Route = 1 THEN
    WHILE Nested < 1
        SET Message = "
While
InsideIf
"
        SET Nested = Nested + 1
    END WHILE
END IF
""";

    private const string DelimiterCollisionSource = """
LET CSharpQuotes = "\"\"\"\nTail"
LET JavaScriptData = "`${value}\\path\nTail"
LET SwiftData = "\\#(Name) \"\"\"#\nTail"
LET PythonData = "\"\"\" and '''\nTail"
LET CppData = ")SMILE\"\nTail"

PRINT {CSharpQuotes}
PRINT {JavaScriptData}
PRINT {SwiftData}
PRINT {PythonData}
PRINT {CppData}
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Canonical_safe_value_uses_each_high_level_targets_native_multiline_form()
    {
        string csharp = Generate(CanonicalSource, TargetLanguage.CSharp);
        StringAssert.Contains(csharp, "string MultilineText = \"\"\"\n            Hello World!\n            This is SMILE!\n                How are you?\n        \"\"\";");

        string javascript = Generate(CanonicalSource, TargetLanguage.JavaScript);
        StringAssert.Contains(javascript, "let MultilineText = `    Hello World!\n    This is SMILE!\n        How are you?`;");

        string java = Generate(CanonicalSource, TargetLanguage.Java);
        StringAssert.Contains(java, "String MultilineText = \"\"\"\n            Hello World!\n            This is SMILE!\n                How are you?\\\n        \"\"\";");

        string swift = Generate(CanonicalSource, TargetLanguage.Swift);
        StringAssert.Contains(swift, "let MultilineText: String = \"\"\"\n    Hello World!\n    This is SMILE!\n        How are you?\n\"\"\"");

        string python = Generate(CanonicalSource, TargetLanguage.Python);
        StringAssert.Contains(python, "MultilineText = \"\"\"    Hello World!\n    This is SMILE!\n        How are you?\"\"\"");

        string cpp = Generate(CanonicalSource, TargetLanguage.Cpp);
        StringAssert.Contains(cpp, "std::string MultilineText = R\"SMILE(    Hello World!\n    This is SMILE!\n        How are you?)SMILE\";");
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp, "Message = \"\"\"")]
    [DataRow(TargetLanguage.JavaScript, "Message = `Hello")]
    [DataRow(TargetLanguage.Java, "Message = \"\"\"")]
    [DataRow(TargetLanguage.Swift, "Message = \"\"\"")]
    [DataRow(TargetLanguage.Python, "Message = \"\"\"Hello")]
    [DataRow(TargetLanguage.Cpp, "Message = R\"SMILE(Hello")]
    public void Direct_set_inside_if_inside_while_uses_the_same_renderer(
        TargetLanguage language,
        string expectedAssignment)
    {
        const string source = """
LET Message = ""
LET Count = 0
LET Ready = TRUE

WHILE Count < 2
    IF Ready = TRUE THEN
        SET Message = "
Hello
World
"
    END IF

    SET Count = Count + 1
END WHILE

PRINT {Message}
""";

        string generated = Generate(source, language);

        StringAssert.Contains(generated, expectedAssignment);
        StringAssert.Contains(generated, "Hello\n");
        StringAssert.Contains(generated, "World");
    }

    [TestMethod]
    public void Direct_set_uses_the_native_renderer_in_every_recursive_structured_placement()
    {
        foreach ((TargetLanguage Language, string AssignmentStart) target in new[]
                 {
                     (TargetLanguage.CSharp, "Message = \"\"\""),
                     (TargetLanguage.JavaScript, "Message = `"),
                     (TargetLanguage.Java, "Message = \"\"\""),
                     (TargetLanguage.Swift, "Message = \"\"\""),
                     (TargetLanguage.Python, "Message = \"\"\""),
                     (TargetLanguage.Cpp, "Message = R\"SMILE(")
                 })
        {
            string generated = Generate(RecursivePlacementSource, target.Language);

            Assert.AreEqual(
                4,
                CountOccurrences(generated, target.AssignmentStart),
                $"{target.Language} did not render every ELSE IF, ELSE, nested WHILE, and WHILE-inside-IF SET.");
            StringAssert.Contains(generated, "ElseIf");
            StringAssert.Contains(generated, "Else");
            StringAssert.Contains(generated, "Nested");
            StringAssert.Contains(generated, "InsideIf");
        }
    }

    [TestMethod]
    public void Unsafe_controls_use_exact_escaped_or_adjacent_fallbacks()
    {
        const string source = """
LET Value = "A\0B\nC"

PRINT {Value}
""";

        string csharp = Generate(source, TargetLanguage.CSharp);
        StringAssert.Contains(csharp, "string Value = \"A\\0B\\nC\";");
        Assert.IsFalse(csharp.Contains("string Value = \"\"\"", StringComparison.Ordinal));

        string javascript = Generate(source, TargetLanguage.JavaScript);
        StringAssert.Contains(javascript, "let Value = \"A\\u0000B\\nC\";");
        Assert.IsFalse(javascript.Contains("let Value = `", StringComparison.Ordinal));

        string java = Generate(source, TargetLanguage.Java);
        StringAssert.Contains(java, "String Value = \"A\\000B\\n\"");
        StringAssert.Contains(java, "+ \"C\";");
        Assert.IsFalse(java.Contains("String Value = \"\"\"", StringComparison.Ordinal));

        string swift = Generate(source, TargetLanguage.Swift);
        StringAssert.Contains(swift, "let Value: String = \"A\\0B\\nC\"");
        Assert.IsFalse(swift.Contains("let Value: String = \"\"\"", StringComparison.Ordinal));

        string python = Generate(source, TargetLanguage.Python);
        StringAssert.Contains(python, "Value = (\n        \"A\\x00B\\n\"");
        StringAssert.Contains(python, "        \"C\"\n    )");

        string cpp = Generate(source, TargetLanguage.Cpp);
        StringAssert.Contains(cpp, "std::string Value = std::string{\"A\\000B\\nC\", 5};");
        Assert.IsFalse(cpp.Contains("std::string Value = R\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Target_significant_delimiters_are_escaped_or_selected_deterministically()
    {
        string csharp = Generate(DelimiterCollisionSource, TargetLanguage.CSharp);
        StringAssert.Contains(csharp, "string CSharpQuotes = \"\"\"\"");

        string javascript = Generate(DelimiterCollisionSource, TargetLanguage.JavaScript);
        StringAssert.Contains(javascript, "let JavaScriptData = `\\`\\${value}\\\\path\nTail`;");

        string swift = Generate(DelimiterCollisionSource, TargetLanguage.Swift);
        StringAssert.Contains(swift, "let SwiftData: String = ##\"\"\"");
        StringAssert.Contains(swift, "\"\"\"##");

        string python = Generate(DelimiterCollisionSource, TargetLanguage.Python);
        StringAssert.Contains(python, "PythonData = (");

        string cpp = Generate(DelimiterCollisionSource, TargetLanguage.Cpp);
        StringAssert.Contains(cpp, "std::string CppData = R\"SMILE1(");
        StringAssert.Contains(cpp, ")SMILE1\";");
    }

    [TestMethod]
    public void Explicit_concatenation_and_interpolation_keep_their_expression_shapes()
    {
        const string source = """
LET Name = "Sin"
LET Direct = "First\nSecond"
LET Concatenated = Direct + "\nTail"
LET Interpolated = $"Hello {Name}\nAgain"
""";

        foreach (TargetLanguage language in new[]
                 {
                     TargetLanguage.CSharp,
                     TargetLanguage.JavaScript,
                     TargetLanguage.Java,
                     TargetLanguage.Swift,
                     TargetLanguage.Python,
                     TargetLanguage.Cpp
                 })
        {
            string generated = Generate(source, language);
            StringAssert.Contains(generated, "Direct");
            StringAssert.Contains(generated, "Concatenated");
            StringAssert.Contains(generated, "Interpolated");
            StringAssert.Contains(generated, "Tail");
            StringAssert.Contains(generated, "Name");
        }
    }

    [TestMethod]
    public async Task Installed_high_level_targets_preserve_multiline_and_fallback_values_at_runtime()
    {
        string[] sources =
        {
            """
LET Value = "First\nSecond"
PRINT {Value}
""",
            """
LET Value = "\n  First \n\tSecond\t\n\n"
PRINT {Value}
""",
            """
LET Value = "A\0B\nC\b\f\rD"
PRINT {Value}
""",
            DelimiterCollisionSource
        };
        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();
        int executed = 0;

        foreach (string source in sources)
        {
            EvaluationResult expected = _evaluator.Evaluate(source);
            Assert.IsTrue(expected.Success, string.Join(Environment.NewLine, expected.Diagnostics));

            foreach (TargetLanguage language in new[]
                     {
                         TargetLanguage.CSharp,
                         TargetLanguage.JavaScript,
                         TargetLanguage.Java,
                         TargetLanguage.Swift,
                         TargetLanguage.Python,
                         TargetLanguage.Cpp
                     })
            {
                IToolchain toolchain = toolchains.Get(language);
                ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
                if (!status.IsAvailable)
                {
                    continue;
                }

                TranspileResult transpiled = _transpiler.Transpile(source, language);
                Assert.IsTrue(transpiled.Success, JoinDiagnostics(transpiled));
                BuildRunResult result = await toolchain.BuildAndRunAsync(
                    transpiled.GeneratedProgram!,
                    CancellationToken.None);

                Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
                Assert.AreEqual(string.Empty, result.StandardError, language.ToString());
                Assert.IsFalse(
                    GeneratedTargetWarningDetector.ContainsCompilerWarning(language, result.BuildOutput),
                    result.BuildOutput);
                // Windows text-mode runtimes may translate every stdout LF to
                // CRLF. Normalize only that established physical transport
                // form; do not trim or discard any other significant value.
                Assert.AreEqual(
                    NormalizeTransportLineEndings(expected.Output),
                    NormalizeTransportLineEndings(result.StandardOutput),
                    $"{language} changed the multiline value: {Visible(result.StandardOutput)}");
                executed++;
            }
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No high-level target toolchain is installed.");
        }
    }

    private string Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result));
        return Normalize(result.GeneratedProgram!.PrimaryFile.Content);
    }

    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string NormalizeTransportLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int position = 0;
        while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }

        return count;
    }

    private static string JoinDiagnostics(TranspileResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message));

    private static string Visible(string value) =>
        value.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\0", "\\0", StringComparison.Ordinal);
}
