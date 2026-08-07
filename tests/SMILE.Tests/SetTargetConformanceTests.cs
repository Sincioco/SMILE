using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class SetTargetConformanceTests
{
    private const string AcceptanceSource = """
LET Counter = 0
LET Name = ""
LET Ready = FALSE

SET Name ="
S
 I
  N
"

PRINT Counter={Counter}, Ready={Ready}
PRINT Name:
PRINT {Name}

SET Counter = Counter + 1
SET Name = "Louiery"
SET Ready = TRUE

PRINT Counter={Counter}, Name={Name}, Ready={Ready}

SET Counter = Counter + 2
LET Message = $"{Name} finished with {Counter}."
PRINT {Message}
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void All_ten_generators_emit_real_SET_updates()
    {
        const string source = """
LET Counter = 0
LET Stable = 9
SET Counter = Counter + 1
PRINT {Counter}
""";

        string csharp = Generate(source, TargetLanguage.CSharp);
        StringAssert.Contains(csharp, "int Counter = 0;");
        StringAssert.Contains(csharp, "Counter = Counter + 1;");

        string c = Generate(source, TargetLanguage.C);
        StringAssert.Contains(c, "int Counter = 0;");
        StringAssert.Contains(c, "Counter = Counter + 1;");

        string masm = Generate(source, TargetLanguage.MasmX64);
        StringAssert.Contains(masm, "set2Value BYTE \"1\"");
        StringAssert.Contains(masm, "lea rax, set2Value");
        StringAssert.Contains(masm, "mov QWORD PTR [variable0Ptr], rax");
        StringAssert.Contains(masm, "mov DWORD PTR [variable0Length], set2ValueLength");

        string javascript = Generate(source, TargetLanguage.JavaScript);
        StringAssert.Contains(javascript, "let Counter = 0;");
        StringAssert.Contains(javascript, "Counter = Counter + 1;");

        string java = Generate(source, TargetLanguage.Java);
        StringAssert.Contains(java, "int Counter = 0;");
        StringAssert.Contains(java, "Counter = Counter + 1;");

        string cobol = Generate(source, TargetLanguage.Cobol);
        StringAssert.Contains(cobol, "MOVE \"1\" TO Counter.");
        StringAssert.Contains(cobol, "MOVE 1 TO SMILE-SET-LENGTH-0.");

        string objectiveC = Generate(source, TargetLanguage.ObjectiveC);
        StringAssert.Contains(objectiveC, "int Counter = 0;");
        StringAssert.Contains(objectiveC, "Counter = Counter + 1;");

        string swift = Generate(source, TargetLanguage.Swift);
        StringAssert.Contains(swift, "var Counter: Int = 0");
        StringAssert.Contains(swift, "let Stable: Int = 9");
        StringAssert.Contains(swift, "Counter = Counter + 1");

        string python = Generate(source, TargetLanguage.Python);
        StringAssert.Contains(python, "Counter = 0");
        StringAssert.Contains(python, "Counter = Counter + 1");

        string cpp = Generate(source, TargetLanguage.Cpp);
        StringAssert.Contains(cpp, "int Counter = 0;");
        StringAssert.Contains(cpp, "Counter = Counter + 1;");
    }

    [TestMethod]
    public void Swift_preserves_direct_self_assignment_with_real_type_safe_storage_updates()
    {
        const string source = """
LET Text = "SMILE"
LET Number = 1
LET Flag = TRUE
SET Text = Text
SET Number = Number
SET Flag = Flag
PRINT {Text}
PRINT {Number}
PRINT {Flag}
""";

        string swift = Generate(source, TargetLanguage.Swift);

        StringAssert.Contains(swift, "Text = Text + \"\"");
        StringAssert.Contains(swift, "Number = Number + 0");
        StringAssert.Contains(swift, "Flag = Flag || false");
        Assert.IsFalse(swift.Contains("Text = Text\n", StringComparison.Ordinal));
        Assert.IsFalse(swift.Contains("Number = Number\n", StringComparison.Ordinal));
        Assert.IsFalse(swift.Contains("Flag = Flag\n", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Block_values_reach_every_target_only_as_normalized_ordinary_Strings()
    {
        const string source = """
LET Name = ""
SET Name ="
S
 I
  N
"
PRINT {Name}
""";

        var generated = TargetLanguageInfo.All.ToDictionary(
            language => language,
            language => NormalizePhysicalNewlines(Generate(source, language)));

        StringAssert.Contains(generated[TargetLanguage.CSharp], "Name = \"\"\"");
        StringAssert.Contains(generated[TargetLanguage.C], "Name =\n        \"S\\n\"");
        StringAssert.Contains(generated[TargetLanguage.JavaScript], "Name = `S\n I\n  N`;");
        StringAssert.Contains(generated[TargetLanguage.Java], "Name = \"\"\"");
        StringAssert.Contains(generated[TargetLanguage.ObjectiveC], "Name =\n        \"S\\n\"");
        StringAssert.Contains(generated[TargetLanguage.Swift], "Name = \"\"\"");
        StringAssert.Contains(generated[TargetLanguage.Python], "Name = \"\"\"S\n I\n  N\"\"\"");
        StringAssert.Contains(generated[TargetLanguage.Cpp], "Name = R\"SMILE(S\n I\n  N)SMILE\";");
        StringAssert.Contains(generated[TargetLanguage.Cobol], "MOVE X\"530A20490A20204E\" TO Name.");
        StringAssert.Contains(generated[TargetLanguage.MasmX64], "set1Value BYTE \"S\", 10, \" I\", 10, \"  N\"");

        foreach (string targetSource in generated.Values)
        {
            Assert.IsFalse(targetSource.Contains("SET Name =\"", StringComparison.Ordinal));
            Assert.IsFalse(targetSource.Contains("structural margin", StringComparison.OrdinalIgnoreCase));
        }
    }

    [TestMethod]
    public void SET_generation_is_byte_deterministic_for_every_generated_file()
    {
        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            GeneratedProgram first = GenerateProgram(AcceptanceSource, language);
            GeneratedProgram second = GenerateProgram(AcceptanceSource, language);

            CollectionAssert.AreEqual(
                first.Files.Select(file => file.RelativePath).ToArray(),
                second.Files.Select(file => file.RelativePath).ToArray(),
                language.ToString());
            CollectionAssert.AreEqual(
                first.Files.Select(file => file.Content).ToArray(),
                second.Files.Select(file => file.Content).ToArray(),
                language.ToString());
        }
    }

    [TestMethod]
    public void SET_values_and_intermediates_select_the_whole_program_Integer_profile()
    {
        const string wideValue = "LET Value = 1\nSET Value = 5000000000\nPRINT {Value}";
        AssertWideIntegerProfile(wideValue);

        const string wideIntermediate = "LET Value = 1\nSET Value = 50000 * 50000\nPRINT {Value}";
        AssertWideIntegerProfile(wideIntermediate);

        const string bigInteger = "LET Value = 1\nSET Value = 3100000000 * 3000000\nPRINT {Value}";
        string javascript = Generate(bigInteger, TargetLanguage.JavaScript);
        StringAssert.Contains(javascript, "let Value = 1n;");
        StringAssert.Contains(javascript, "Value = 3100000000n * 3000000n;");
    }

    [TestMethod]
    public void C_family_SET_tracks_exact_length_when_any_assigned_String_contains_NUL()
    {
        const string source = """
LET Data = "ABC"
SET Data = "A\0B"
PRINT {Data}
SET Data = "XYZ"
PRINT {Data}
""";

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language);
            StringAssert.Contains(generated, "size_t smileString0Length = 3;");
            Assert.AreEqual(3, CountOccurrences(generated, "smileString0Length = 3;"));
            StringAssert.Contains(generated, "Data = \"A\\000B\";");
            StringAssert.Contains(generated, "Data = \"XYZ\";");
            StringAssert.Contains(generated, "fwrite(Data, 1, smileString0Length, stdout);");
            Assert.IsFalse(generated.Contains("smilePrintBytes", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public async Task Installed_targets_preserve_NUL_to_NUL_SET_and_equality_after_SET()
    {
        const string source = """
LET Data = "A\0B"
PRINT {Data}
SET Data = "A\0C"
PRINT {Data}
PRINT {Data = "A\0B"}
""";

        EvaluationResult reference = _evaluator.Evaluate(source);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));
        Assert.AreEqual("A\0B\nA\0C\nFALSE\n", NormalizePhysicalNewlines(reference.Output));

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language);
            StringAssert.Contains(generated, "#include <string.h>");
            StringAssert.Contains(generated, "memcmp(Data, \"A\\000B\"");
            Assert.IsFalse(generated.Contains("#include <stdbool.h>", StringComparison.Ordinal));
        }

        await AssertInstalledTargetsMatchEvaluator(source);
    }

    [TestMethod]
    public async Task Installed_targets_run_the_v050_acceptance_program_against_the_reference_evaluator()
    {
        await AssertInstalledTargetsMatchEvaluator(AcceptanceSource);
    }

    [TestMethod]
    public async Task Installed_targets_preserve_exact_block_boundaries_whitespace_quotes_and_NUL()
    {
        const string source = """
LET Value = ""
SET Value ="
S
 I
  N
"
PRINT {Value}
    SET Value ="
    S
     I
      N
    "
PRINT {Value}
SET Value ="
First

Third
"
PRINT {Value}
SET Value ="

Leading
"
PRINT {Value}
SET Value ="
Trailing

"
PRINT {Value}
SET Value ="
Space 
Tab	
He said "Hello".
A\0B
"
PRINT {Value}
SET Value = "ordinary"
PRINT {Value}
""";

        await AssertInstalledTargetsMatchEvaluator(source);
    }

    private async Task AssertInstalledTargetsMatchEvaluator(string source)
    {
        EvaluationResult reference = _evaluator.Evaluate(source);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));

        ToolchainRegistry toolchains = ToolchainRegistry.CreateDefault();
        int executed = 0;
        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            IToolchain toolchain = toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            if (!status.IsAvailable)
            {
                TestContext.WriteLine($"{language}: unavailable - {status.Message}");
                continue;
            }

            BuildRunResult result = await toolchain.BuildAndRunAsync(
                GenerateProgram(source, language),
                CancellationToken.None);
            Assert.IsTrue(
                result.Success,
                $"{language}{Environment.NewLine}{result.BuildOutput}{Environment.NewLine}{result.StandardError}");
            Assert.AreEqual(
                NormalizePhysicalNewlines(reference.Output),
                NormalizePhysicalNewlines(result.StandardOutput),
                language.ToString());
            TestContext.WriteLine($"{language}: passed exact reference-output comparison");
            executed++;
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No target toolchains are installed.");
        }
    }

    private void AssertWideIntegerProfile(string source)
    {
        StringAssert.Contains(Generate(source, TargetLanguage.CSharp), "long Value");
        StringAssert.Contains(Generate(source, TargetLanguage.C), "int64_t Value");
        StringAssert.Contains(Generate(source, TargetLanguage.Java), "long Value");
        StringAssert.Contains(Generate(source, TargetLanguage.ObjectiveC), "int64_t Value");
        StringAssert.Contains(Generate(source, TargetLanguage.Swift), "Int64");
        StringAssert.Contains(Generate(source, TargetLanguage.Cpp), "std::int64_t Value");
    }

    private string Generate(string source, TargetLanguage language) =>
        GenerateProgram(source, language).PrimaryFile.Content;

    private GeneratedProgram GenerateProgram(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static string NormalizePhysicalNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
