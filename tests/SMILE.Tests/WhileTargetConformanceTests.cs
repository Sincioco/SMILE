using System.Text;
using System.Text.RegularExpressions;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class WhileTargetConformanceTests
{
    private const string RequireAllTargetsEnvironmentVariable = "SMILE_REQUIRE_ALL_TARGETS";
    private const string RequireJavaEnvironmentVariable = "SMILE_REQUIRE_JAVA";
    private const string RequireZeroTargetWarningsEnvironmentVariable =
        "SMILE_REQUIRE_ZERO_TARGET_WARNINGS";

    private const string AcceptanceSource = """
REM SMILE v0.8.0 WHILE acceptance program

LET Count = 0
LET Total = 0

PRINT Enter a positive count:
INPUT Count

WHILE Count > 0
    SET Total = Total + Count
    PRINT Count={Count}, Total={Total}
    SET Count = Count - 1
END WHILE

PRINT Done. Total={Total}
""";

    private const string KnownFalseVariableSource = """
LET Ready = FALSE

WHILE Ready = TRUE
    PRINT unreachable body
END WHILE

PRINT Known-false variable loop complete.
""";

    private const string FiniteCorpusSource = """
REM zero iterations retain a genuine source loop
LET ZeroCount = 0

WHILE ZeroCount > 0
    PRINT zero-iteration body must remain
END WHILE

PRINT Zero iterations complete.

LET OneCount = 0

WHILE OneCount < 1
    SET OneCount = OneCount + 1
END WHILE

PRINT One={OneCount}

LET TenCount = 0

WHILE TenCount < 10
    SET TenCount = TenCount + 1
END WHILE

PRINT Ten={TenCount}

LET Row = 1
LET ColumnValue = 1

WHILE Row <= 2
    SET ColumnValue = 1

    WHILE ColumnValue <= 2
        PRINT Cell={Row},{ColumnValue}
        SET ColumnValue = ColumnValue + 1
    END WHILE

    SET Row = Row + 1
END WHILE

LET KeepGoing = TRUE
LET BooleanIterations = 0

WHILE KeepGoing = TRUE
    INPUT KeepGoing

    IF KeepGoing = TRUE THEN
        SET BooleanIterations = BooleanIterations + 1
    ELSE
        PRINT Boolean loop stopped.
    END IF
END WHILE

PRINT Boolean iterations={BooleanIterations}

LET Choice = 0
LET ChoiceTotal = 0

WHILE Choice < 3
    IF Choice = 1 THEN
        SET ChoiceTotal = ChoiceTotal + 10
    ELSE
        SET ChoiceTotal = ChoiceTotal + 1
    END IF

    SET Choice = Choice + 1
END WHILE

PRINT IF mutation={ChoiceTotal}

LET Negative = -3

WHILE Negative < 0
    PRINT Negative={Negative}
    SET Negative = Negative + 1
END WHILE

LET Dividend = -7
LET Divisor = 2
LET Quotient = 0

WHILE Divisor > 0
    SET Quotient = Dividend / Divisor
    SET Divisor = 0
END WHILE

PRINT Quotient={Quotient}

LET Text = ""
LET ReadText = TRUE
LET TextMatches = FALSE

WHILE ReadText = TRUE
    INPUT Text
    SET TextMatches = Text = "A B"
    PRINT Text=[{Text}], Match={TextMatches}
    SET ReadText = FALSE
END WHILE

// Learner names that collide with Java and COBOL WHILE support.
LET _smile_condition = 0
LET WHILE_CONDITION_0 = 0
LET WHILE_EXIT_0 = 0
LET column = 0

WHILE column < 1
    SET _smile_condition = _smile_condition + 1
    SET WHILE_CONDITION_0 = WHILE_CONDITION_0 + 2
    SET WHILE_EXIT_0 = WHILE_EXIT_0 + 3
    SET column = column + 1
END WHILE

PRINT Collisions={_smile_condition},{WHILE_CONDITION_0},{WHILE_EXIT_0},{column}
""";

    private const string FiniteCorpusInput = "TRUE\nFALSE\nA B\n";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void All_targets_emit_genuine_deterministic_WHILE_control_flow()
    {
        const string source = """
LET Count = 0

WHILE Count < 2
    // learner loop body marker

    PRINT Loop value={Count}
    SET Count = Count + 1
END WHILE
""";

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            GeneratedProgram first = Generate(source, language);
            GeneratedProgram second = Generate(source, language);

            AssertGeneratedProgramsEqual(first, second, language);
            AssertGenuineLoopStructure(first.PrimaryFile.Content, language, expectedLoops: 1);
            Assert.HasCount(
                1,
                Regex.Matches(first.PrimaryFile.Content, "learner loop body marker")
                    .Cast<Match>(),
                $"{language} duplicated the learner comment while lowering WHILE.");
            Assert.HasCount(
                1,
                Regex.Matches(first.PrimaryFile.Content, "Loop value=").Cast<Match>(),
                $"{language} duplicated or unrolled the learner loop body.");
            StringAssert.Contains(
                first.PrimaryFile.Content,
                "Count",
                $"{language} stopped reading current loop-carried storage.");
        }
    }

    [TestMethod]
    public void Nested_known_false_and_empty_WHILE_blocks_remain_authentic()
    {
        const string nestedSource = """
LET Outer = 0
LET Inner = 0

WHILE Outer < 2
    SET Inner = 0

    WHILE Inner < 2
        PRINT nested body marker
        SET Inner = Inner + 1
    END WHILE

    SET Outer = Outer + 1
END WHILE
""";
        const string knownFalseSource = """
LET Ready = FALSE

WHILE Ready = TRUE
    PRINT known-false body marker
END WHILE
""";
        const string emptySource = """
LET Ready = FALSE

WHILE Ready = TRUE
    // layout-only loop marker

END WHILE
""";

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            GeneratedProgram nested = Generate(nestedSource, language);
            AssertGenuineLoopStructure(nested.PrimaryFile.Content, language, expectedLoops: 2);
            Assert.HasCount(
                1,
                Regex.Matches(nested.PrimaryFile.Content, "nested body marker").Cast<Match>(),
                $"{language} duplicated or unrolled a nested body.");

            GeneratedProgram knownFalse = Generate(knownFalseSource, language);
            AssertGenuineLoopStructure(knownFalse.PrimaryFile.Content, language, expectedLoops: 1);
            StringAssert.Contains(
                knownFalse.PrimaryFile.Content,
                "known-false body marker",
                $"{language} deleted a source loop whose first condition is known false.");

            GeneratedProgram empty = Generate(emptySource, language);
            AssertGenuineLoopStructure(empty.PrimaryFile.Content, language, expectedLoops: 1);
            Assert.HasCount(
                1,
                Regex.Matches(empty.PrimaryFile.Content, "layout-only loop marker").Cast<Match>(),
                $"{language} duplicated the layout-only loop comment.");

            if (language is TargetLanguage.Python)
            {
                StringAssert.Contains(empty.PrimaryFile.Content, "pass");
            }
            else if (language is TargetLanguage.Cobol)
            {
                StringAssert.Contains(empty.PrimaryFile.Content, "CONTINUE");
            }
        }
    }

    [TestMethod]
    public void Active_WHILE_generation_keeps_INPUT_checked_arithmetic_and_collision_names_safe()
    {
        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            GeneratedProgram generated = Generate(FiniteCorpusSource, language);
            string text = string.Join("\n", generated.Files.Select(file => file.Content));

            foreach (string marker in CheckedArithmeticMarkers(language))
            {
                StringAssert.Contains(
                    text,
                    marker,
                    $"{language} omitted loop-carried checked arithmetic marker '{marker}'.");
            }

            foreach (string marker in InputMarkers(language))
            {
                StringAssert.Contains(
                    text,
                    marker,
                    $"{language} omitted loop-body INPUT marker '{marker}'.");
            }
        }

        string masm = Generate(FiniteCorpusSource, TargetLanguage.MasmX64).PrimaryFile.Content;
        StringAssert.Contains(masm, "_smile__smile_condition");
        StringAssert.Contains(masm, "_smile_WHILE_CONDITION_0");
        StringAssert.Contains(masm, "_smile_WHILE_EXIT_0");
        StringAssert.Contains(masm, "_smile_column");
    }

    [TestMethod]
    public async Task Installed_targets_match_the_normative_WHILE_acceptance_program()
    {
        const string expectedOutput =
            "Enter a positive count:\n" +
            "Count=3, Total=3\n" +
            "Count=2, Total=5\n" +
            "Count=1, Total=6\n" +
            "Done. Total=6\n";

        EvaluationResult reference = _evaluator.Evaluate(AcceptanceSource, "3\n");
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));
        Assert.AreEqual(expectedOutput, reference.StandardOutput);
        Assert.AreEqual(string.Empty, reference.StandardError);
        Assert.AreEqual(0, reference.ExitCode);

        await AssertInstalledTargetsMatchAsync(
            AcceptanceSource,
            "3\n",
            () => BuildRunOptions.Scripted("3\n"));
    }

    [TestMethod]
    public async Task Installed_targets_match_the_finite_WHILE_runtime_corpus()
    {
        await AssertInstalledTargetsMatchAsync(
            FiniteCorpusSource,
            FiniteCorpusInput,
            () => BuildRunOptions.ScriptedBytes(Encoding.UTF8.GetBytes(FiniteCorpusInput)));
    }

    [TestMethod]
    public async Task Installed_targets_keep_known_false_variable_WHILE_warning_free()
    {
        await AssertInstalledTargetsMatchAsync(
            KnownFalseVariableSource,
            string.Empty,
            () => BuildRunOptions.Default);
    }

    [TestMethod]
    [TestCategory("HistoricalExactInput")]
    public async Task Installed_targets_preserve_reached_loop_carried_overflow()
    {
        const string source = """
LET Value = 0
LET Running = TRUE

INPUT Value

WHILE Running = TRUE
    PRINT Before overflow.
    SET Value = Value + 1
    SET Running = FALSE
END WHILE

PRINT unreachable
""";

        await AssertInstalledTargetsMatchAsync(
            source,
            "9223372036854775807\n",
            () => BuildRunOptions.Scripted("9223372036854775807\n"));
    }

    [TestMethod]
    public async Task Installed_targets_preserve_reached_loop_carried_division_by_zero()
    {
        const string source = """
LET Divisor = 1
LET Running = TRUE

WHILE Running = TRUE
    INPUT Divisor
    PRINT Before division.
    PRINT {10 / Divisor}
    SET Running = FALSE
END WHILE

PRINT unreachable
""";

        await AssertInstalledTargetsMatchAsync(
            source,
            "0\n",
            () => BuildRunOptions.Scripted("0\n"));
    }

    [TestMethod]
    public async Task Installed_targets_suppress_an_unreachable_WHILE_right_operand_failure()
    {
        const string source = """
LET Divisor = 0

WHILE FALSE = TRUE AND (1 / Divisor = 0)
    PRINT unreachable short-circuit body
END WHILE

PRINT Short circuit remained safe.
""";

        foreach (TargetLanguage language in ActiveTargetLanguages.All)
        {
            StringAssert.Contains(
                Generate(source, language).PrimaryFile.Content,
                "unreachable short-circuit body",
                $"{language} deleted the source WHILE body to suppress the unreachable failure.");
        }

        await AssertInstalledTargetsMatchAsync(
            source,
            string.Empty,
            () => BuildRunOptions.Default);
    }

    [TestMethod]
    [TestCategory("HistoricalExactInput")]
    public async Task Installed_targets_keep_constant_true_WHILE_warning_safe_until_INPUT_failure()
    {
        const string source = """
LET Value = 0

WHILE TRUE = TRUE
    INPUT Value
END WHILE
""";

        await AssertInstalledTargetsMatchAsync(
            source,
            string.Empty,
            () => BuildRunOptions.Scripted(string.Empty));
    }

    [TestMethod]
    public async Task Generated_infinite_WHILE_is_cancelled_with_its_captured_process()
    {
        const string source = """
WHILE TRUE = TRUE
END WHILE
""";

        GeneratedProgram generated = Generate(source, TargetLanguage.JavaScript);
        AssertGenuineLoopStructure(
            generated.PrimaryFile.Content,
            TargetLanguage.JavaScript,
            expectedLoops: 1);

        IToolchain toolchain = _toolchains.Get(TargetLanguage.JavaScript);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            if (EnvironmentFlagIsEnabled(RequireAllTargetsEnvironmentVariable))
            {
                Assert.Fail(
                    $"{RequireAllTargetsEnvironmentVariable}=1 requires JavaScript: {status.Message}");
            }

            TestContext.WriteLine($"JavaScript infinite-loop cancellation unavailable: {status.Message}");
            return;
        }

        // Leave enough setup time for antivirus/file-system variability so
        // cancellation reaches the running child process rather than the
        // temporary-source write that precedes it.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        BuildRunResult result = await toolchain.BuildAndRunAsync(
            generated,
            cancellation.Token);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Cancelled, FormatBuildAndErrorOutput(result));
        Assert.IsFalse(result.TimedOut, FormatBuildAndErrorOutput(result));
        Assert.IsLessThan(
            TimeSpan.FromSeconds(5),
            result.Duration,
            "The captured infinite target did not terminate promptly after cancellation.");
    }

    private async Task AssertInstalledTargetsMatchAsync(
        string source,
        string evaluatorInput,
        Func<BuildRunOptions> options)
    {
        EvaluationResult expected = _evaluator.Evaluate(source, evaluatorInput);
        Assert.HasCount(
            0,
            expected.Diagnostics,
            JoinDiagnostics(expected.Diagnostics));

        bool requireAllTargets = EnvironmentFlagIsEnabled(RequireAllTargetsEnvironmentVariable);
        bool requireJava = EnvironmentFlagIsEnabled(RequireJavaEnvironmentVariable);
        bool requireZeroWarnings = EnvironmentFlagIsEnabled(
            RequireZeroTargetWarningsEnvironmentVariable);
        IReadOnlyList<TargetLanguage> targetLanguages = requireAllTargets
            ? TargetLanguageInfo.All
            : requireJava
                ? ActiveTargetLanguages.All.Append(TargetLanguage.Java).Distinct().ToArray()
                : ActiveTargetLanguages.All;
        var failures = new List<string>();
        int executed = 0;

        TestContext.WriteLine(
            $"{RequireAllTargetsEnvironmentVariable}={(requireAllTargets ? "1" : "0")}, " +
            $"{RequireJavaEnvironmentVariable}={(requireJava ? "1" : "0")}, " +
            $"{RequireZeroTargetWarningsEnvironmentVariable}={(requireZeroWarnings ? "1" : "0")}");

        foreach (TargetLanguage language in targetLanguages)
        {
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            if (!status.IsAvailable)
            {
                TestContext.WriteLine($"{language}: unavailable - {status.Message}");
                if (requireAllTargets ||
                    (requireJava && language is TargetLanguage.Java) ||
                    (requireZeroWarnings && language is TargetLanguage.CSharp))
                {
                    failures.Add($"{language}: required toolchain unavailable - {status.Message}");
                }

                continue;
            }

            GeneratedProgram generated = Generate(source, language);
            BuildRunResult actual = await toolchain.BuildAndRunAsync(
                generated,
                CancellationToken.None,
                options());
            string compilerOutput = FormatBuildAndErrorOutput(actual);
            string expectedOutput = WithTargetPhysicalNewLines(
                expected.StandardOutput,
                language,
                isStandardError: false);
            string expectedError = WithTargetPhysicalNewLines(
                expected.StandardError,
                language,
                isStandardError: true);

            if (!string.Equals(actual.Stage, "Running", StringComparison.Ordinal))
            {
                failures.Add(
                    $"{language}: generated WHILE program did not reach its runtime stage." +
                    Environment.NewLine + compilerOutput);
            }

            if (actual.Success != expected.Success || actual.ExitCode != expected.ExitCode)
            {
                failures.Add(
                    $"{language}: expected success={expected.Success}, exit={expected.ExitCode}; " +
                    $"actual success={actual.Success}, exit={actual.ExitCode}." +
                    Environment.NewLine + compilerOutput);
            }

            if (!string.Equals(expectedOutput, actual.StandardOutput, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{language}: WHILE stdout differed byte-for-byte." +
                    Environment.NewLine + $"Expected: {Visible(expectedOutput)}" +
                    Environment.NewLine + $"Actual:   {Visible(actual.StandardOutput)}");
            }

            if (!string.Equals(expectedError, actual.StandardError, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{language}: WHILE stderr differed byte-for-byte." +
                    Environment.NewLine + $"Expected: {Visible(expectedError)}" +
                    Environment.NewLine + $"Actual:   {Visible(actual.StandardError)}");
            }

            if (GeneratedTargetWarningDetector.ContainsCompilerWarning(language, actual.BuildOutput))
            {
                failures.Add(
                    $"{language}: generated WHILE target emitted a compiler warning." +
                    Environment.NewLine + actual.BuildOutput);
            }

            if (language is TargetLanguage.JavaScript or TargetLanguage.Python &&
                !string.IsNullOrWhiteSpace(actual.BuildOutput))
            {
                failures.Add(
                    $"{language}: interpreted target unexpectedly reported compile-stage output." +
                    Environment.NewLine + actual.BuildOutput);
            }

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
            Assert.Inconclusive("No target toolchain is installed.");
        }
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static void AssertGenuineLoopStructure(
        string generated,
        TargetLanguage language,
        int expectedLoops)
    {
        string pattern = language switch
        {
            TargetLanguage.CSharp or
            TargetLanguage.C or
            TargetLanguage.Cpp or
            TargetLanguage.JavaScript or
            TargetLanguage.Java or
            TargetLanguage.ObjectiveC => @"(?m)^\s*while\s*\(",
            TargetLanguage.Swift => @"(?m)^\s*while\s+.+\s+\{",
            TargetLanguage.Python => @"(?m)^\s*while\s+.+:",
            TargetLanguage.Cobol => @"(?m)^\s*PERFORM UNTIL SMILE-WHILE-EXIT-\d+ = 1",
            TargetLanguage.MasmX64 => @"(?m)^smilewhileHead\d+:",
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

        Assert.HasCount(
            expectedLoops,
            Regex.Matches(generated, pattern).Cast<Match>(),
            $"{language} did not emit exactly {expectedLoops} genuine learner WHILE loop(s)." +
            Environment.NewLine + generated);

        if (language is TargetLanguage.MasmX64)
        {
            Assert.HasCount(
                expectedLoops,
                Regex.Matches(generated, @"(?m)^smilewhileEnd\d+:").Cast<Match>(),
                generated);
            Assert.HasCount(
                expectedLoops,
                Regex.Matches(generated, @"(?m)^\s*jz smilewhileEnd\d+\r?$").Cast<Match>(),
                generated);
            Assert.HasCount(
                expectedLoops,
                Regex.Matches(generated, @"(?m)^\s*jmp smilewhileHead\d+\r?$").Cast<Match>(),
                generated);
        }
    }

    private static void AssertGeneratedProgramsEqual(
        GeneratedProgram expected,
        GeneratedProgram actual,
        TargetLanguage language)
    {
        Assert.HasCount(expected.Files.Count, actual.Files, language.ToString());
        for (int index = 0; index < expected.Files.Count; index++)
        {
            Assert.AreEqual(expected.Files[index].RelativePath, actual.Files[index].RelativePath);
            Assert.AreEqual(
                expected.Files[index].Content,
                actual.Files[index].Content,
                $"{language} WHILE generation was not byte-for-byte deterministic.");
        }
    }

    private static IReadOnlyList<string> CheckedArithmeticMarkers(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp or
            TargetLanguage.C or
            TargetLanguage.Cpp or
            TargetLanguage.JavaScript or
            TargetLanguage.Java or
            TargetLanguage.ObjectiveC or
            TargetLanguage.Swift or
            TargetLanguage.Python => ["_smile_add", "SMILER1206"],
            TargetLanguage.Cobol => ["smile_checked_add", "SMILER1206"],
            TargetLanguage.MasmX64 => ["add eax, r10d", "jo smileArithmeticOverflow", "SMILER1206"],
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    private static IReadOnlyList<string> InputMarkers(TargetLanguage language) =>
        language switch
        {
            TargetLanguage.CSharp => ["Console.ReadLine()", "bool.Parse"],
            TargetLanguage.C => ["scanf(", "fgets(", "strcmp("],
            TargetLanguage.Cpp => ["std::cin.get()", "_smile_input_boolean"],
            TargetLanguage.JavaScript => ["fs.readSync", "_smile_input_boolean"],
            TargetLanguage.Java => ["System.in.read()", "_smile_input_boolean"],
            TargetLanguage.Cobol => ["CALL \"smile_input_", "static int smile_read_line"],
            TargetLanguage.ObjectiveC => ["fgetc(stdin)", "_smile_input_boolean"],
            TargetLanguage.Swift => ["FileHandle.standardInput.read", "_smile_input_boolean"],
            TargetLanguage.Python => ["sys.stdin.buffer.read", "_smile_input_boolean"],
            TargetLanguage.MasmX64 => ["extern scanf:proc", "call scanf", "call _stricmp"],
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

    private static bool EnvironmentFlagIsEnabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private static string FormatBuildAndErrorOutput(BuildRunResult result) =>
        string.Join(
            Environment.NewLine,
            new[] { result.BuildOutput, result.StandardError }
                .Where(output => !string.IsNullOrWhiteSpace(output)));

    private static string WithTargetPhysicalNewLines(
        string logicalText,
        TargetLanguage language,
        bool isStandardError)
    {
        Assert.DoesNotContain(
            '\r',
            logicalText,
            "A WHILE physical-line expectation must not normalize source data.");
        string newLine = (language, isStandardError) switch
        {
            (TargetLanguage.JavaScript, _) => "\n",
            (TargetLanguage.Swift, true) => "\n",
            _ => "\r\n"
        };
        return logicalText.Replace("\n", newLine, StringComparison.Ordinal);
    }

    private static string Visible(string text) =>
        "\"" + text
            .Replace("\0", "<NUL>", StringComparison.Ordinal)
            .Replace("\t", "<TAB>", StringComparison.Ordinal)
            .Replace("\r", "<CR>", StringComparison.Ordinal)
            .Replace("\n", "<LF>", StringComparison.Ordinal) + "\"";

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
