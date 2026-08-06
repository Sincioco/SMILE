using System.IO;
using System.Text;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class RuntimeStorageReadinessTests
{
    private const string RequireAllTargetsEnvironmentVariable = "SMILE_REQUIRE_ALL_TARGETS";

    private const string RuntimeAuthenticitySource = """
LET Text = "One"
LET Number = 1
LET Flag = FALSE

PRINT {Text}
PRINT {Number}
PRINT {Flag}

SET Text = "Two"
SET Number = 2
SET Flag = TRUE

PRINT {Text}
PRINT {Number}
PRINT {Flag}
""";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_direct_NUL_free_variable_PRINT_reads_storage_before_and_after_SET(
        TargetLanguage language)
    {
        const string source = """
LET Data = "First"
PRINT {Data}
SET Data = "Second"
PRINT {Data}
""";

        string generated = Generate(source, language).PrimaryFile.Content;

        StringAssert.Contains(generated, "Data = \"Second\";");
        Assert.AreEqual(
            2,
            CountOccurrences(generated, "printf(\"%s\\n\", Data);"),
            generated);
        Assert.IsFalse(generated.Contains("printf(\"First\\n\")", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("printf(\"Second\\n\")", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("smilePrintBytes", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_direct_length_aware_variable_PRINT_reads_pointer_and_current_length(
        TargetLanguage language)
    {
        const string source = """
LET Data = "ABC"
PRINT {Data}
SET Data = "A\0B"
PRINT {Data}
SET Data = "XYZ"
PRINT {Data}
""";

        string generated = Generate(source, language).PrimaryFile.Content;

        StringAssert.Contains(generated, "size_t smileString0Length = 3;");
        StringAssert.Contains(generated, "Data = \"A\\000B\";");
        StringAssert.Contains(generated, "Data = \"XYZ\";");
        Assert.AreEqual(
            3,
            CountOccurrences(generated, "smileString0Length = 3;"),
            generated);
        Assert.AreEqual(
            3,
            CountOccurrences(generated, "fwrite(Data, 1, smileString0Length, stdout);"),
            generated);
        Assert.AreEqual(3, CountOccurrences(generated, "fputc('\\n', stdout);"), generated);
        Assert.IsFalse(generated.Contains("smilePrintBytes", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_direct_Block_String_PRINT_reads_the_variable(TargetLanguage language)
    {
        const string source = """
LET Message = ""

SET Message ="
A
 B
"

PRINT {Message}
""";

        string generated = Generate(source, language).PrimaryFile.Content;

        StringAssert.Contains(generated, "Message = \"A\\n B\";");
        StringAssert.Contains(generated, "printf(\"%s\\n\", Message);");
        Assert.IsFalse(generated.Contains("printf(\"A\\n B\\n\")", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public void C_family_direct_String_equality_reads_current_storage_and_lengths(
        TargetLanguage language)
    {
        const string source = """
LET Left = "A\0B"
LET Right = "A\0B"

PRINT {Left = Right}
PRINT {Left <> Right}

SET Right = "A\0C"

PRINT {Left = Right}
PRINT {Left <> Right}
PRINT {Left = "A\0B"}
PRINT {"A\0B" = Left}
PRINT {Left <> "A\0C"}
PRINT {"A\0C" <> Left}

SET Right = "A\0B\0"

PRINT {Left = Right}
PRINT {Left <> Right}
""";

        string generated = Generate(source, language).PrimaryFile.Content;

        StringAssert.Contains(generated, "#include <string.h>");
        StringAssert.Contains(generated, "smileString0Length");
        StringAssert.Contains(generated, "smileString1Length");
        StringAssert.Contains(generated, "memcmp(Left, Right");
        StringAssert.Contains(generated, "memcmp(Left, \"A\\000B\"");
        StringAssert.Contains(generated, "memcmp(\"A\\000B\", Left");
        StringAssert.Contains(generated, "memcmp(Left, \"A\\000C\"");
        StringAssert.Contains(generated, "memcmp(\"A\\000C\", Left");
        StringAssert.Contains(generated, " == 0");
        StringAssert.Contains(generated, " != 0");
        Assert.IsGreaterThanOrEqualTo(10, CountOccurrences(generated, "memcmp("), generated);
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public async Task Installed_C_family_target_runs_exact_storage_equality(
        TargetLanguage language)
    {
        const string source = """
LET Left = "A\0B"
LET Right = "A\0B"

PRINT {Left = Right}
PRINT {Left <> Right}

SET Right = "A\0C"

PRINT {Left = Right}
PRINT {Left <> Right}
PRINT {Left = "A\0B"}
PRINT {"A\0B" = Left}
PRINT {Left <> "A\0C"}
PRINT {"A\0C" <> Left}

SET Right = "A\0B\0"

PRINT {Left = Right}
PRINT {Left <> Right}
""";

        await AssertInstalledTargetMatchesEvaluatorExactly(source, language);
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public async Task Installed_C_family_target_preserves_exact_storage_bytes(
        TargetLanguage language)
    {
        const string blockSource = """
LET Text = ""

SET Text ="
A
 B
"

PRINT {Text}
""";
        const string trailingSpaceSource =
            "LET Text = \"\"\n\nSET Text =\"\nA \nB\n\"\n\nPRINT {Text}";
        (string Source, string ExpectedHex)[] cases =
        {
            ("LET Data = \"ABC\"\nSET Data = \"A\\0B\"\nPRINT {Data}", "4100420A"),
            (blockSource, "410A20420A"),
            ("LET Text = \"X\"\nSET Text = \"\"\nPRINT {Text}", "0A"),
            (trailingSpaceSource, "41200A420A")
        };

        foreach ((string source, string expectedHex) in cases)
        {
            await AssertInstalledTargetMatchesEvaluatorExactly(source, language, expectedHex);
        }
    }

    [TestMethod]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.ObjectiveC)]
    public async Task Installed_C_family_maps_every_emitted_runtime_facility_name(
        TargetLanguage language)
    {
        const string source = """
LET bool = TRUE
LET size_t = "A\0B"
LET int64_t = 5000000000
LET fwrite = "A\0B"
LET fputc = "line"
LET memcmp = "A\0B"
LET strlen = "ABC"
LET strcmp = "same"
LET printf = "print"
LET stdout = "out"
LET main = "main"

PRINT {bool}
PRINT {size_t}
PRINT {int64_t}
PRINT {fwrite}
PRINT {fputc}
PRINT {memcmp = "A\0B"}
PRINT {strlen = "A\0B"}
PRINT {strcmp = "same"}
PRINT {printf}
PRINT {stdout}
PRINT {main}
""";

        string generated = Generate(source, language).PrimaryFile.Content;

        StringAssert.Contains(generated, "bool _smile_bool = true;");
        StringAssert.Contains(generated, "int64_t _smile_int64_t = INT64_C(5000000000);");
        StringAssert.Contains(generated, "fwrite(_smile_fwrite, 1,");
        StringAssert.Contains(generated, "memcmp(_smile_memcmp, \"A\\000B\"");
        StringAssert.Contains(generated, "strlen(_smile_strlen)");
        StringAssert.Contains(generated, "strcmp(_smile_strcmp, \"same\")");
        StringAssert.Contains(generated, "printf(\"%s\\n\", _smile_printf);");
        StringAssert.Contains(generated, "_smile_size_t");
        StringAssert.Contains(generated, "_smile_fputc");
        StringAssert.Contains(generated, "_smile_stdout");
        StringAssert.Contains(generated, "_smile_main");

        await AssertInstalledTargetMatchesEvaluatorExactly(source, language);
    }

    [TestMethod]
    public void COBOL_direct_mutable_variable_PRINT_reads_storage_and_logical_length()
    {
        const string source = """
LET Name = "First"
PRINT {Name}
SET Name = "Second"
PRINT {Name}
""";

        string generated = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;

        StringAssert.Contains(generated, "01 SMILE-SET-LENGTH-0 PIC 9(9) COMP-5 VALUE 5.");
        StringAssert.Contains(generated, "MOVE \"Second\" TO Name.");
        StringAssert.Contains(generated, "MOVE 6 TO SMILE-SET-LENGTH-0.");
        Assert.AreEqual(
            2,
            CountOccurrences(generated, "Name(1:SMILE-SET-LENGTH-0)"),
            generated);
        Assert.IsFalse(generated.Contains("DISPLAY \"First\".", StringComparison.Ordinal));
        Assert.IsFalse(generated.Contains("DISPLAY \"Second\".", StringComparison.Ordinal));
    }

    [TestMethod]
    public void COBOL_empty_mutable_String_PRINT_uses_runtime_length_and_exact_newline()
    {
        const string source = """
LET Text = "X"
SET Text = ""
PRINT {Text}
""";

        string generated = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;

        StringAssert.Contains(generated, "MOVE SPACES TO SMILE-Text.");
        StringAssert.Contains(generated, "MOVE 0 TO SMILE-SET-LENGTH-0.");
        StringAssert.Contains(generated, "IF SMILE-SET-LENGTH-0");
        StringAssert.Contains(generated, "DISPLAY X\"0A\" WITH NO ADVANCING");
        StringAssert.Contains(
            generated,
            "DISPLAY SMILE-Text WITH NO ADVANCING");
        Assert.IsFalse(
            generated.Contains("DISPLAY SMILE-Text(1:", StringComparison.Ordinal),
            "A one-byte PIC X should avoid warning-prone reference modification.");
        StringAssert.Contains(generated, "END-IF.");
    }

    [TestMethod]
    public void COBOL_Block_String_PRINT_reads_normalized_storage_and_logical_length()
    {
        const string source = """
LET Message = ""

SET Message ="
A
 B
"

PRINT {Message}
""";

        string generated = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;

        StringAssert.Contains(generated, "MOVE X\"410A2042\" TO SMILE-Message.");
        StringAssert.Contains(generated, "MOVE 4 TO SMILE-SET-LENGTH-0.");
        StringAssert.Contains(
            generated,
            "DISPLAY SMILE-Message(1:SMILE-SET-LENGTH-0) WITH NO ADVANCING");
        Assert.IsFalse(generated.Contains("DISPLAY X\"410A2042\".", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Installed_COBOL_target_preserves_empty_Block_and_trailing_space_storage_bytes()
    {
        const string blockSource = """
LET Text = ""

SET Text ="
A
 B
"

PRINT {Text}
""";
        const string trailingSpaceSource =
            "LET Text = \"\"\n\nSET Text =\"\nA \nB\n\"\n\nPRINT {Text}";
        (string Source, string ExpectedHex)[] cases =
        {
            ("LET Text = \"X\"\nSET Text = \"\"\nPRINT {Text}", "0A"),
            (blockSource, "410A20420A"),
            (trailingSpaceSource, "41200A420A")
        };

        foreach ((string source, string expectedHex) in cases)
        {
            await AssertInstalledTargetMatchesEvaluatorExactly(
                source,
                TargetLanguage.Cobol,
                expectedHex);
        }
    }

    [TestMethod]
    public void MASM_direct_variable_PRINT_reads_runtime_pointer_and_length_after_SET()
    {
        string generated = Generate(RuntimeAuthenticitySource, TargetLanguage.MasmX64).PrimaryFile.Content;

        for (int variableIndex = 0; variableIndex < 3; variableIndex++)
        {
            Assert.AreEqual(
                2,
                CountOccurrences(
                    generated,
                    $"mov rdx, QWORD PTR [variable{variableIndex}Ptr]"),
                generated);
            Assert.AreEqual(
                2,
                CountOccurrences(
                    generated,
                    $"mov r8d, DWORD PTR [variable{variableIndex}Length]"),
                generated);
        }

        StringAssert.Contains(generated, "lea rax, set6Value");
        StringAssert.Contains(generated, "mov QWORD PTR [variable0Ptr], rax");
        StringAssert.Contains(generated, "mov DWORD PTR [variable0Length], set6ValueLength");
    }

    [TestMethod]
    public async Task Installed_targets_run_the_runtime_authenticity_program_against_the_evaluator()
    {
        await AssertAvailableTargetsMatchEvaluator(
            RuntimeAuthenticitySource,
            "runtime-authenticity program");
    }

    [TestMethod]
    public async Task Available_targets_run_the_deployed_language_reference_against_the_evaluator()
    {
        string languagePath = Path.Combine(AppContext.BaseDirectory, "language.smile");
        Assert.IsTrue(File.Exists(languagePath), languagePath);
        string source = await File.ReadAllTextAsync(languagePath, Encoding.UTF8);

        await AssertAvailableTargetsMatchEvaluator(
            source,
            "deployed language.smile",
            InputTestData.CanonicalScriptedInput);
    }

    private async Task AssertAvailableTargetsMatchEvaluator(
        string source,
        string programName,
        string? scriptedInput = null)
    {
        EvaluationResult reference = scriptedInput is null
            ? _evaluator.Evaluate(source)
            : _evaluator.Evaluate(source, scriptedInput);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));
        string expected = NormalizePhysicalNewlines(reference.Output);
        var failures = new List<string>();
        int executed = 0;
        bool requireAllTargets = string.Equals(
            Environment.GetEnvironmentVariable(RequireAllTargetsEnvironmentVariable),
            "1",
            StringComparison.Ordinal);

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            if (!status.IsAvailable)
            {
                TestContext.WriteLine($"{language}: unavailable - {status.Message}");
                if (requireAllTargets)
                {
                    failures.Add($"{language}: required toolchain unavailable - {status.Message}");
                }

                continue;
            }

            BuildRunResult result = await toolchain.BuildAndRunAsync(
                Generate(source, language),
                CancellationToken.None,
                scriptedInput is null ? null : BuildRunOptions.Scripted(scriptedInput));

            if (!result.Success || result.ExitCode != 0)
            {
                failures.Add(
                    $"{language}: build/run failed.{Environment.NewLine}" +
                    result.BuildOutput + Environment.NewLine + result.StandardError);
                TestContext.WriteLine($"{language}: {programName} failed to build or run");
                continue;
            }

            string actual = NormalizePhysicalNewlines(result.StandardOutput);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                failures.Add($"{language}: stdout differed from SmileEvaluator.");
                TestContext.WriteLine($"{language}: {programName} stdout differed");
                continue;
            }

            TestContext.WriteLine($"{language}: {programName} matched SmileEvaluator");
            executed++;
        }

        if (failures.Count > 0)
        {
            Assert.Fail(string.Join(Environment.NewLine + Environment.NewLine, failures));
        }

        if (requireAllTargets)
        {
            Assert.AreEqual(
                TargetLanguageInfo.All.Count,
                executed,
                $"{RequireAllTargetsEnvironmentVariable}=1 requires every target to execute.");
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No target toolchains are installed.");
        }
    }

    private async Task AssertInstalledTargetMatchesEvaluatorExactly(
        string source,
        TargetLanguage language,
        string? requiredHex = null)
    {
        IToolchain toolchain = _toolchains.Get(language);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            Assert.Inconclusive(status.Message);
        }

        EvaluationResult expected = _evaluator.Evaluate(source);
        Assert.IsTrue(expected.Success, JoinDiagnostics(expected.Diagnostics));
        BuildRunResult actual = await toolchain.BuildAndRunAsync(
            Generate(source, language),
            CancellationToken.None);

        Assert.IsTrue(actual.Success, actual.BuildOutput + Environment.NewLine + actual.StandardError);
        Assert.AreEqual(0, actual.ExitCode);

        string referenceHex = ToNormalizedUtf8Hex(expected.Output);
        string actualHex = ToNormalizedUtf8Hex(actual.StandardOutput);
        if (requiredHex is not null)
        {
            Assert.AreEqual(requiredHex, referenceHex, "The reference evaluator bytes changed.");
        }

        Assert.AreEqual(referenceHex, actualHex, $"Exact stdout differed for {language}.");
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static string ToNormalizedUtf8Hex(string text) =>
        Convert.ToHexString(Encoding.UTF8.GetBytes(NormalizePhysicalNewlines(text)));

    private static string NormalizePhysicalNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
