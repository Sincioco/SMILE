using System.IO;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class PythonTargetTests
{
    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void Python_metadata_is_appended_without_reordering_existing_targets()
    {
        CollectionAssert.AreEqual(
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
            },
            TargetLanguageInfo.All.ToArray());

        Assert.AreEqual("python", TargetLanguageInfo.GetStableId(TargetLanguage.Python));
        Assert.AreEqual("Python", TargetLanguageInfo.GetDisplayName(TargetLanguage.Python));
        Assert.AreEqual("Program.py", TargetLanguageInfo.GetPrimaryFileName(TargetLanguage.Python));
        Assert.IsTrue(TargetLanguageInfo.TryParse("PYTHON", out TargetLanguage parsed));
        Assert.AreEqual(TargetLanguage.Python, parsed);
    }

    [TestMethod]
    public void Python_generator_emits_direct_top_level_PRINT_script()
    {
        GeneratedProgram program = Generate("PRINT \"Hello World\"");

        Assert.AreEqual(
            Lines("print(\"Hello World\")"),
            program.PrimaryFile.Content);
        AssertHasNoProgramWrapper(program.PrimaryFile.Content);
    }

    [TestMethod]
    public void Python_generator_emits_minimal_top_level_string_script()
    {
        GeneratedProgram program = Generate("""
LET Name = "Sin"
PRINT {Name}
PRINT Name
PRINT
""");

        Assert.AreEqual("Program.py", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "Name = \"Sin\"",
                "print(Name)",
                "print(\"Name\")",
                "print()"),
            program.PrimaryFile.Content);
        AssertHasNoProgramWrapper(program.PrimaryFile.Content);
        Assert.IsFalse(program.PrimaryFile.Content.Contains("_smile_text", StringComparison.Ordinal));
        Assert.IsFalse(program.PrimaryFile.Content.Contains("_smile_div", StringComparison.Ordinal));
        Assert.IsFalse(program.PrimaryFile.Content.Contains("import ", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Python_generator_preserves_typed_expression_intent_and_display_rules()
    {
        string python = Generate("""
LET Name = "Sin"
LET FullName = Name + " Cioco"
LET Age = 49
LET Adult = Age >= 18
LET Quotient = -7 / 2
LET Same = Name = "Sin"
LET Safe = FALSE AND (1 / 0 = 0)
LET Message = $"{{Name}} {FullName}: Age={Age}, Adult={Adult}"

PRINT {Message}
PRINT {Quotient}
PRINT {Same}
PRINT {Safe}
""").PrimaryFile.Content;

        StringAssert.Contains(python, "def _smile_text(value: object) -> str:");
        StringAssert.Contains(python, "return \"TRUE\" if value else \"FALSE\"");
        StringAssert.Contains(python, "def _smile_div(left: int, right: int) -> int:");
        StringAssert.Contains(python, "quotient = abs(left) // abs(right)");
        StringAssert.Contains(python, "FullName = Name + \" Cioco\"");
        StringAssert.Contains(python, "Adult = Age >= 18");
        StringAssert.Contains(python, "Quotient = _smile_div(-7, 2)");
        StringAssert.Contains(python, "Same = Name == \"Sin\"");
        StringAssert.Contains(python, "Safe = False");
        StringAssert.Contains(
            python,
            "Message = f\"{{Name}} {FullName}: Age={_smile_text(Age)}, Adult={_smile_text(Adult)}\"");
        StringAssert.Contains(python, "print(_smile_text(Quotient))");
        StringAssert.Contains(python, "print(_smile_text(Same))");
        Assert.IsFalse(python.Contains(" / ", StringComparison.Ordinal));
        Assert.IsFalse(python.Contains("import ", StringComparison.Ordinal));
        AssertHasNoProgramWrapper(python);
    }

    [TestMethod]
    public void Python_generator_emits_helpers_only_when_the_program_uses_them()
    {
        string integerOnly = Generate("LET Age = 49").PrimaryFile.Content;
        Assert.IsFalse(integerOnly.Contains("_smile_text", StringComparison.Ordinal));
        Assert.IsFalse(integerOnly.Contains("_smile_div", StringComparison.Ordinal));

        string divisionOnly = Generate("LET Half = 8 / 2").PrimaryFile.Content;
        StringAssert.Contains(divisionOnly, "def _smile_div(left: int, right: int) -> int:");
        Assert.IsFalse(divisionOnly.Contains("_smile_text", StringComparison.Ordinal));

        string displayOnly = Generate("PRINT {49}").PrimaryFile.Content;
        StringAssert.Contains(displayOnly, "def _smile_text(value: object) -> str:");
        Assert.IsFalse(displayOnly.Contains("_smile_div", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Python_generator_places_required_helpers_before_unindented_learner_statements()
    {
        string python = Generate("LET Ready = TRUE\nPRINT {Ready}").PrimaryFile.Content;

        Assert.AreEqual(
            Lines(
                "def _smile_text(value: object) -> str:",
                "    if isinstance(value, bool):",
                "        return \"TRUE\" if value else \"FALSE\"",
                "",
                "    return str(value)",
                "",
                "",
                "Ready = True",
                "print(_smile_text(Ready))"),
            python);
        AssertHasNoProgramWrapper(python);
    }

    [TestMethod]
    public void Python_generator_never_wraps_representative_program_shapes()
    {
        string[] sources =
        {
            "PRINT \"Hello\"",
            "LET Name = \"Sin\"\nPRINT {Name}",
            "LET Ready = TRUE\nPRINT {Ready}",
            "LET Name = \"\"\nINPUT Name\nPRINT {Name}",
            "LET Age = 49\nIF Age >= 18 THEN\n    PRINT \"Adult\"\nEND IF",
            "LET Ready = FALSE\nWHILE Ready = TRUE\n    PRINT \"Waiting\"\nEND WHILE"
        };

        foreach (string source in sources)
        {
            AssertHasNoProgramWrapper(Generate(source).PrimaryFile.Content);
        }

        string input = NormalizeLineEndings(
            Generate("LET Name = \"\"\nINPUT Name\nPRINT {Name}").PrimaryFile.Content);
        StringAssert.Contains(
            input,
            "\n\nName = \"\"\nName = _smile_input_string(\"Name\")\nprint(Name)\n");
        Assert.IsFalse(input.Contains("\n    Name = \"\"", StringComparison.Ordinal), input);
    }

    [TestMethod]
    public void Python_IF_and_WHILE_use_only_normal_suite_indentation()
    {
        string conditional = Generate("""
LET Age = 49

IF Age >= 18 THEN
    PRINT "Adult"
ELSE
    PRINT "Minor"
END IF
""").PrimaryFile.Content;

        Assert.AreEqual(
            Lines(
                "Age = 49",
                "",
                "if Age >= 18:",
                "    print(\"Adult\")",
                "else:",
                "    print(\"Minor\")"),
            conditional);

        string loop = NormalizeLineEndings(Generate("""
LET Counter = 0

WHILE Counter < 3
    PRINT {Counter}
    SET Counter = Counter + 1
END WHILE
""").PrimaryFile.Content);

        StringAssert.Contains(
            loop,
            "Counter = 0\n\nwhile Counter < 3:\n" +
            "    print(_smile_text(Counter))\n" +
            "    Counter = _smile_add(Counter, 1)\n");
        Assert.IsFalse(loop.Contains("\n    while Counter < 3:", StringComparison.Ordinal), loop);
        AssertHasNoProgramWrapper(conditional);
        AssertHasNoProgramWrapper(loop);
    }

    [TestMethod]
    public void Python_empty_comment_only_and_layout_only_programs_need_no_program_level_pass()
    {
        string empty = Generate(string.Empty).PrimaryFile.Content;
        Assert.AreEqual(Environment.NewLine, empty);
        Assert.IsFalse(empty.Contains("pass", StringComparison.Ordinal));
        AssertHasNoProgramWrapper(empty);

        string commentOnly = Generate("REM Greeting").PrimaryFile.Content;
        Assert.AreEqual(Lines("# Greeting"), commentOnly);
        Assert.IsFalse(commentOnly.Contains("pass", StringComparison.Ordinal));
        AssertHasNoProgramWrapper(commentOnly);

        string laidOut = Generate("\nREM Greeting\n\nPRINT \"Hello World\"\n").PrimaryFile.Content;
        Assert.AreEqual(
            Lines(
                "",
                "# Greeting",
                "",
                "print(\"Hello World\")"),
            laidOut);
        Assert.IsFalse(laidOut.Contains("    # Greeting", StringComparison.Ordinal));
        AssertHasNoProgramWrapper(laidOut);
    }

    [TestMethod]
    public void Python_310_generation_folds_string_literal_f_string_holes_safely()
    {
        string python = Generate("""
PRINT Value: {"A\n" + "B"}
""").PrimaryFile.Content;

        StringAssert.Contains(python, "print(\"Value: A\\nB\")");
        Assert.IsFalse(python.Contains("f\"", StringComparison.Ordinal));
        Assert.IsFalse(python.Contains("_smile_text", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Python_precedence_and_parentheses_preserve_the_bound_tree()
    {
        string python = Generate("""
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 - (3 - 1)
LET D = NOT TRUE OR TRUE
LET E = TRUE OR FALSE AND FALSE
LET F = TRUE = (FALSE = FALSE)
LET G = NOT (49 = 49)
""").PrimaryFile.Content;

        StringAssert.Contains(python, "A = 2 + 3 * 4");
        StringAssert.Contains(python, "B = (2 + 3) * 4");
        StringAssert.Contains(python, "C = 10 - (3 - 1)");
        StringAssert.Contains(python, "D = True");
        StringAssert.Contains(python, "E = True");
        StringAssert.Contains(python, "F = True == (False == False)");
        StringAssert.Contains(python, "G = not (49 == 49)");
    }

    [TestMethod]
    public void Python_identifier_mapping_covers_keywords_soft_keywords_and_runtime_names()
    {
        string python = Generate("""
LET class = "class"
LET match = "match"
LET main = "main"
LET __name__ = "__name__"
LET sys = "sys"
LET str = "str"
LET isinstance = "isinstance"
LET _smile_text = "_smile_text"
LET _smile_div = "_smile_div"
LET _ = "underscore"

PRINT {class}
PRINT {match}
PRINT {main}
PRINT {__name__}
PRINT {sys}
PRINT {str}
PRINT {isinstance}
PRINT {_smile_text}
PRINT {_smile_div}
PRINT {_}
""").PrimaryFile.Content;

        StringAssert.Contains(python, "_smile_class = \"class\"");
        StringAssert.Contains(python, "_smile_match = \"match\"");
        StringAssert.Contains(python, "main = \"main\"");
        StringAssert.Contains(python, "print(main)");
        StringAssert.Contains(python, "_smile___name__ = \"__name__\"");
        StringAssert.Contains(python, "_smile_sys = \"sys\"");
        StringAssert.Contains(python, "_smile_str = \"str\"");
        StringAssert.Contains(python, "_smile_isinstance = \"isinstance\"");
        StringAssert.Contains(python, "_smile__smile_text = \"_smile_text\"");
        StringAssert.Contains(python, "_smile__smile_div = \"_smile_div\"");
        StringAssert.Contains(python, "_ = \"underscore\"");
        StringAssert.Contains(python, "print(_smile__smile_text)");
        AssertHasNoProgramWrapper(python);
    }

    [TestMethod]
    public void Python_generation_is_byte_for_byte_deterministic()
    {
        const string source = """
LET Name = "Sin"
LET Age = 49
LET Message = $"Hello {Name}; age={Age}"
PRINT {Message}
""";

        string first = Generate(source).PrimaryFile.Content;
        string second = Generate(source).PrimaryFile.Content;

        Assert.AreEqual(first, second);
        Assert.IsTrue(first.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.IsFalse(first.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Installed_python_matches_the_reference_evaluator_for_python_hardening_cases()
    {
        IToolchain toolchain = ToolchainRegistry.CreateDefault().Get(TargetLanguage.Python);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        const string source = """
LET A = 7 / 2
LET B = -7 / 2
LET C = 7 / -2
LET D = -7 / -2
LET SafeAnd = FALSE AND (1 / 0 = 0)
LET SafeOr = TRUE OR (1 / 0 = 0)
LET Same = "Sin" = "Sin"
LET Different = "Sin" <> "sin"
LET class = "class"
LET Message = $"{{Python}} A={A}, Safe={SafeAnd}"

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
PRINT {SafeAnd}
PRINT {SafeOr}
PRINT {Same}
PRINT {Different}
PRINT {class}
PRINT {Message}
""";
        EvaluationResult evaluation = _evaluator.Evaluate(source);
        Assert.IsTrue(evaluation.Success, string.Join(Environment.NewLine, evaluation.Diagnostics));

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            Generate(source),
            CancellationToken.None,
            new BuildRunOptions(CreatePauseLauncher: true));

        Assert.IsTrue(result.Success, result.BuildOutput + Environment.NewLine + result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(
            NormalizeLineEndings(evaluation.Output),
            NormalizeLineEndings(result.StandardOutput));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.PauseLauncherPath));
        string launcher = await File.ReadAllTextAsync(result.PauseLauncherPath!);
        StringAssert.Contains(launcher, "-B Program.py");
    }

    private GeneratedProgram Generate(string source)
    {
        TranspileResult result = _transpiler.Transpile(source, TargetLanguage.Python);
        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    private static void AssertHasNoProgramWrapper(string python)
    {
        Assert.IsFalse(python.Contains("def main() -> None:", StringComparison.Ordinal), python);
        Assert.IsFalse(python.Contains("if __name__ == \"__main__\":", StringComparison.Ordinal), python);
        Assert.IsFalse(python.Contains("\n    main()", StringComparison.Ordinal), python);
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
