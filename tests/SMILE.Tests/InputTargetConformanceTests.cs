using System.IO;
using System.Text;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class InputTargetConformanceTests
{
    private const string RequireAllTargetsEnvironmentVariable = "SMILE_REQUIRE_ALL_TARGETS";
    private const string RequireJavaEnvironmentVariable = "SMILE_REQUIRE_JAVA";
    private const string RequireZeroTargetWarningsEnvironmentVariable =
        "SMILE_REQUIRE_ZERO_TARGET_WARNINGS";

    private static readonly TargetLanguage[] Targets =
    [
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
    ];

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void All_targets_emit_INPUT_support_only_when_required_and_deterministically()
    {
        const string source = """
LET Name = ""
LET Age = 0
LET Ready = FALSE

REM preserved INPUT lowering marker
INPUT Name

INPUT Age
INPUT Ready
PRINT [{Name}]
PRINT {Age}
PRINT {Ready}
""";

        foreach (TargetLanguage language in Targets)
        {
            GeneratedProgram first = Generate(source, language);
            GeneratedProgram second = Generate(source, language);

            Assert.IsTrue(first.RequiresStandardInput, language.ToString());
            CollectionAssert.AreEqual(first.Files.ToArray(), second.Files.ToArray(), language.ToString());
            AssertInputLowering(first, language);

            GeneratedProgram noInput = Generate("LET Value = 1\nPRINT {Value}", language);
            Assert.IsFalse(noInput.RequiresStandardInput, language.ToString());
            Assert.IsFalse(
                noInput.Files.Any(file => file.Content.Contains("SMILER1501", StringComparison.Ordinal)),
                language.ToString());
        }
    }

    [TestMethod]
    public void Cpp_INPUT_headers_follow_the_facilities_each_variable_type_uses()
    {
        string stringInput = Generate(
            "LET Value = \"\"\nINPUT Value",
            TargetLanguage.Cpp).PrimaryFile.Content;
        string booleanInput = Generate(
            "LET Value = FALSE\nINPUT Value",
            TargetLanguage.Cpp).PrimaryFile.Content;
        string integerInput = Generate(
            "LET Value = 0\nINPUT Value",
            TargetLanguage.Cpp).PrimaryFile.Content;

        foreach (string generated in new[] { stringInput, booleanInput })
        {
            Assert.AreEqual(-1, generated.IndexOf("#include <charconv>", StringComparison.Ordinal));
            Assert.AreEqual(-1, generated.IndexOf("#include <system_error>", StringComparison.Ordinal));
            Assert.AreEqual(-1, generated.IndexOf("#include <cstdint>", StringComparison.Ordinal));
        }

        StringAssert.Contains(integerInput, "#include <charconv>");
        StringAssert.Contains(integerInput, "#include <system_error>");
        StringAssert.Contains(integerInput, "#include <cstdint>");
    }

    [TestMethod]
    public async Task Installed_targets_match_evaluator_for_normative_INPUT_acceptance()
    {
        const string source = """
REM SMILE v0.7.0 INPUT acceptance program

LET Name = ""
LET Age = 0
LET Ready = FALSE

PRINT Enter your name:
INPUT Name

PRINT Enter your age:
INPUT Age

PRINT Enter TRUE or FALSE:
INPUT Ready

PRINT Name=[{Name}]

IF Age >= 18 THEN
    PRINT Age group=Adult
ELSE
    PRINT Age group=Minor
END IF

IF Ready = TRUE THEN
    PRINT Ready=TRUE
ELSE
    PRINT Ready=FALSE
END IF
""";

        EvaluationResult expected = _evaluator.Evaluate(source, InputTestData.CanonicalScriptedInput);
        Assert.IsTrue(expected.Success);
        Assert.AreEqual(0, expected.ExitCode);
        Assert.AreEqual(
            "Enter your name:\nEnter your age:\nEnter TRUE or FALSE:\n" +
            "Name=[  Sin  ]\nAge group=Adult\nReady=TRUE\n",
            expected.StandardOutput);
        Assert.AreEqual(string.Empty, expected.StandardError);

        await AssertInstalledTargetsMatchAsync(source, InputTestData.CanonicalScriptedInput);
    }

    [TestMethod]
    public async Task Installed_targets_preserve_BOM_NUL_whitespace_line_endings_and_final_EOF_line()
    {
        const string source = """
LET First = ""
LET Second = ""
LET Third = ""
LET Fourth = ""
INPUT First
PRINT [{First}]
INPUT Second
PRINT [{Second}]
INPUT Third
PRINT [{Third}]
INPUT Fourth
PRINT [{Fourth}]
""";
        const string input = "\uFEFF\uFEFFAlpha\r\n A\u001AB\t\0尾 \rThird\nLast";

        await AssertInstalledTargetsMatchBytesAsync(
            source,
            input,
            new System.Text.UTF8Encoding(false, true).GetBytes(input));
    }

    [TestMethod]
    public async Task Installed_targets_enforce_INPUT_limit_and_conversion_errors()
    {
        const string emoji = "\U0001F642";
        string acceptedEmojiLine = string.Concat(Enumerable.Repeat(emoji, 1024));
        string rejectedEmojiLine = acceptedEmojiLine + emoji;
        string paddedInteger = new string(' ', 2048) + "0" + new string('\t', 2048);
        string paddedBoolean = new string(' ', 2046) + "TRUE" + new string('\t', 2047);

        Assert.AreEqual(
            SmileLanguage.MaximumInputLineUtf8Bytes,
            Encoding.UTF8.GetByteCount(acceptedEmojiLine));
        Assert.AreEqual(
            SmileLanguage.MaximumInputLineUtf8Bytes + 4,
            Encoding.UTF8.GetByteCount(rejectedEmojiLine));
        Assert.AreEqual(
            SmileLanguage.MaximumInputLineUtf8Bytes + 1,
            Encoding.UTF8.GetByteCount(paddedInteger));
        Assert.AreEqual(
            SmileLanguage.MaximumInputLineUtf8Bytes + 1,
            Encoding.UTF8.GetByteCount(paddedBoolean));

        (string Source, string Input)[] cases =
        [
            (
                "LET Value = \"\"\nINPUT Value\nPRINT {Value}",
                new string('a', SmileLanguage.MaximumInputLineUtf8Bytes) + "\n"),
            (
                "LET Value = \"\"\nINPUT Value\nPRINT {Value}",
                new string('a', SmileLanguage.MaximumInputLineUtf8Bytes + 1) + "\n"),
            // One run proves the byte-accurate boundary in both directions:
            // the 1024-emoji line must succeed before the 1025-emoji line fails.
            (
                "LET Accepted = \"\"\nLET Rejected = \"\"\n" +
                "INPUT Accepted\nPRINT Accepted boundary\nINPUT Rejected",
                acceptedEmojiLine + "\n" + rejectedEmojiLine + "\n"),
            // The shared byte limit is checked before Integer and Boolean trim.
            // Each 4097-byte line would otherwise become a valid value.
            ("LET Value = 0\nINPUT Value", paddedInteger + "\n"),
            ("LET Value = FALSE\nINPUT Value", paddedBoolean + "\n"),
            ("LET Value = \"Before\"\nINPUT Value", string.Empty),
            ("LET Value = 0\nINPUT Value", "+\n"),
            ("LET Value = 0\nINPUT Value", "0\u0301\n"),
            ("LET Value = 0\nINPUT Value", "9223372036854775808\n"),
            ("LET Value = FALSE\nINPUT Value", "YES\n"),
            // U+017F LONG S participates in broad Unicode uppercasing, but
            // SMILE Boolean INPUT is ordinal ASCII TRUE/FALSE only.
            ("LET Value = FALSE\nINPUT Value", "fal\u017Fe\n")
        ];

        foreach ((string source, string input) in cases)
        {
            await AssertInstalledTargetsMatchAsync(source, input);
        }
    }

    [TestMethod]
    public async Task Installed_targets_match_normative_invalid_Integer_and_EOF_runs()
    {
        const string invalidIntegerSource = """
LET Age = 0
PRINT Before
INPUT Age
PRINT After
""";
        EvaluationResult invalidInteger = _evaluator.Evaluate(invalidIntegerSource, "hello\n");
        Assert.IsFalse(invalidInteger.Success);
        Assert.AreEqual(1, invalidInteger.ExitCode);
        Assert.AreEqual("Before\n", invalidInteger.StandardOutput);
        Assert.AreEqual(
            "SMILE Runtime Error SMILER1503: Input for 'Age' is not a valid Integer.\n",
            invalidInteger.StandardError);
        await AssertInstalledTargetsMatchAsync(invalidIntegerSource, "hello\n");

        const string eofSource = """
LET Name = ""
INPUT Name
""";
        EvaluationResult eof = _evaluator.Evaluate(eofSource, string.Empty);
        Assert.IsFalse(eof.Success);
        Assert.AreEqual(1, eof.ExitCode);
        Assert.AreEqual(string.Empty, eof.StandardOutput);
        Assert.AreEqual(
            "SMILE Runtime Error SMILER1501: Input ended before a value was received for 'Name'.\n",
            eof.StandardError);
        await AssertInstalledTargetsMatchAsync(eofSource, string.Empty);
    }

    [TestMethod]
    public async Task Installed_targets_match_all_checked_Integer_success_and_failure_cases()
    {
        const string success = """
LET Left = 0
LET Right = 0
INPUT Left
INPUT Right
LET Sum = Left + Right
LET Difference = Left - Right
LET Product = Left * Right
LET Quotient = Left / Right
LET Negative = -Left
PRINT {Sum}
PRINT {Difference}
PRINT {Product}
PRINT {Quotient}
PRINT {Negative}
""";
        await AssertInstalledTargetsMatchAsync(success, "7\n-3\n");

        (string Source, string Input)[] failures =
        [
            ("LET Value = 0\nINPUT Value\nLET Result = Value + 1\nPRINT {Result}", "9223372036854775807\n"),
            ("LET Value = 0\nINPUT Value\nLET Result = Value - 1\nPRINT {Result}", "-9223372036854775808\n"),
            ("LET Value = 0\nINPUT Value\nLET Result = Value * 2\nPRINT {Result}", "9223372036854775807\n"),
            ("LET Value = 0\nINPUT Value\nLET Result = -Value\nPRINT {Result}", "-9223372036854775808\n"),
            ("LET Value = 1\nINPUT Value\nLET Result = 7 / Value\nPRINT {Result}", "0\n"),
            ("LET Value = 1\nINPUT Value\nLET Result = -9223372036854775808 / Value\nPRINT {Result}", "-1\n")
        ];

        foreach ((string source, string input) in failures)
        {
            await AssertInstalledTargetsMatchAsync(source, input);
        }
    }

    [TestMethod]
    public async Task Installed_targets_preserve_short_circuit_branch_and_left_to_right_failure_order()
    {
        const string controlFlow = """
LET Check = FALSE
LET Result = 0
INPUT Check
LET SafeAnd = Check = TRUE AND (1 / 0 = 0)
LET SafeOr = Check = FALSE OR (1 / 0 = 0)
IF Check = TRUE THEN
    SET Result = 1 / 0
ELSE
    SET Result = 42
END IF
PRINT {SafeAnd}
PRINT {SafeOr}
PRINT {Result}
""";
        await AssertInstalledTargetsMatchAsync(controlFlow, "FALSE\n");
        await AssertInstalledTargetsMatchAsync(controlFlow, "TRUE\n");

        const string arithmeticOrder = """
LET X = 1
INPUT X
LET Result = (1 / X) + (9223372036854775807 + (X + 1))
PRINT {Result}
""";
        await AssertInstalledTargetsMatchAsync(arithmeticOrder, "0\n");

        const string interpolationOrder = """
LET X = 1
INPUT X
PRINT {(1 / X)}{9223372036854775807 + (X + 1)}
""";
        await AssertInstalledTargetsMatchAsync(interpolationOrder, "0\n");

        const string atomicPrint = """
LET X = 1
INPUT X
PRINT Before
PRINT Prefix{(1 / X)}|{9223372036854775807 + (X + 1)}
PRINT After
""";
        await AssertInstalledTargetsMatchAsync(atomicPrint, "1\n");
    }

    [TestMethod]
    public async Task All_targets_preserve_and_execute_INPUT_only_and_comment_only_IF_bodies()
    {
        const string source = """
LET ChoiceOne = 0
LET ChoiceTwo = 0
LET ChoiceThree = 0
LET First = ""
LET Second = ""
LET Third = ""
LET Nested = ""
LET Spare = ""

INPUT ChoiceOne
INPUT ChoiceTwo
INPUT ChoiceThree

IF ChoiceOne = 1 THEN
    INPUT First
ELSE IF ChoiceOne = 2 THEN
    REM layout comment-only body
ELSE
    INPUT Spare
END IF

IF ChoiceTwo = 1 THEN
    // layout second comment-only body
ELSE IF ChoiceTwo = 2 THEN
    INPUT Second
ELSE
    INPUT Spare
END IF

IF ChoiceThree = 1 THEN
    INPUT Spare
ELSE IF ChoiceThree = 2 THEN
    INPUT Spare
ELSE
    INPUT Third
    IF ChoiceThree = 3 THEN
        -- layout before nested INPUT

        INPUT Nested

        # layout after nested INPUT
    END IF
END IF

PRINT [{First}]
PRINT [{Second}]
PRINT [{Third}]
PRINT [{Nested}]
""";
        const string input = "1\n2\n3\nAlpha\nBeta\nGamma\nDelta\n";

        EvaluationResult expected = _evaluator.Evaluate(source, input);
        Assert.IsTrue(expected.Success, expected.StandardError);
        Assert.AreEqual("[Alpha]\n[Beta]\n[Gamma]\n[Delta]\n", expected.StandardOutput);

        foreach (TargetLanguage language in Targets)
        {
            AssertInputBranchLayout(Generate(source, language), language);
        }

        await AssertInstalledTargetsMatchAsync(source, input);
    }

    [TestMethod]
    public async Task Installed_targets_reject_raw_malformed_UTF8_on_the_reached_INPUT_only()
    {
        const string firstSource = """
LET Value = ""
INPUT Value
PRINT After
""";
        await AssertInstalledTargetsMatchRawAsync(
            firstSource,
            [0xC3, 0x28, 0x0A],
            expectedOutput: string.Empty,
            expectedError:
                "SMILE Runtime Error SMILER1506: Input for 'Value' could not be read as valid UTF-8 text.\n");

        const string laterSource = """
LET First = ""
LET Second = ""
INPUT First
PRINT {First}
INPUT Second
PRINT {Second}
""";
        await AssertInstalledTargetsMatchRawAsync(
            laterSource,
            [0x46, 0x69, 0x72, 0x73, 0x74, 0x0D, 0xC3, 0x28, 0x0A],
            expectedOutput: "First\n",
            expectedError:
                "SMILE Runtime Error SMILER1506: Input for 'Second' could not be read as valid UTF-8 text.\n");
    }

    [TestMethod]
    public async Task Installed_targets_compile_and_run_with_generator_helper_name_collisions()
    {
        const string source = """
LET _smile_input_integer = 0
LET _smile_input_string = "Before"
LET _smile_input_boolean = FALSE
LET _smile_checked = 0
LET fs = 0
LET smileInput0Buffer = "Before"
LET smileInputLength = "Before"
LET _smile_skip_lf = FALSE
LET sys = 0
LET Uint8Array = 0
LET Error = 0
LET SystemExit = 0
LET bytearray = 0
LET len = 0
LET any = 0
LET all = 0
LET zip = 0
LET OSError = 0
LET UnicodeError = 0
LET stdin = 0
LET EOF = 0
LET exit = 0
LET Array = 0
LET CharacterSet = 0
LET Int64 = 0
LET UInt8 = 0
LET UTF8 = 0
LET Bool = 0
LET Never = 0
INPUT _smile_input_integer
INPUT _smile_input_string
INPUT _smile_input_boolean
PRINT {_smile_input_integer}
PRINT [{_smile_input_string}]
PRINT {_smile_input_boolean}
PRINT {_smile_checked}
PRINT {fs}
PRINT [{smileInput0Buffer}]
PRINT [{smileInputLength}]
PRINT {_smile_skip_lf}
PRINT {sys}
PRINT {Uint8Array}
PRINT {Error}
PRINT {SystemExit}
PRINT {bytearray}
PRINT {len}
PRINT {any}
PRINT {all}
PRINT {zip}
PRINT {OSError}
PRINT {UnicodeError}
PRINT {stdin}
PRINT {EOF}
PRINT {exit}
PRINT {Array}
PRINT {CharacterSet}
PRINT {Int64}
PRINT {UInt8}
PRINT {UTF8}
PRINT {Bool}
PRINT {Never}
""";

        await AssertInstalledTargetsMatchAsync(source, "49\nSin\nTRUE\n");
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult transpiled = _transpiler.Transpile(source, language);
        Assert.IsTrue(
            transpiled.Success,
            language + Environment.NewLine +
            string.Join(Environment.NewLine, transpiled.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return transpiled.GeneratedProgram!;
    }

    private async Task AssertInstalledTargetsMatchAsync(string source, string input)
    {
        EvaluationResult expected = _evaluator.Evaluate(source, input);
        Assert.HasCount(
            0,
            expected.Diagnostics,
            string.Join(Environment.NewLine, expected.Diagnostics.Select(diagnostic => diagnostic.Message)));

        await AssertInstalledTargetsAsync(
            source,
            () => BuildRunOptions.Scripted(input),
            expected.Success,
            expected.ExitCode,
            expected.StandardOutput,
            expected.StandardError);
    }

    private async Task AssertInstalledTargetsMatchRawAsync(
        string source,
        byte[] input,
        string expectedOutput,
        string expectedError)
    {
        using var inputStream = new MemoryStream(input, writable: false);
        EvaluationResult expected = _evaluator.Evaluate(source, inputStream);
        Assert.HasCount(
            0,
            expected.Diagnostics,
            string.Join(Environment.NewLine, expected.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.IsFalse(expected.Success);
        Assert.AreEqual(1, expected.ExitCode);
        Assert.AreEqual(expectedOutput, expected.StandardOutput);
        Assert.AreEqual(expectedError, expected.StandardError);

        await AssertInstalledTargetsAsync(
            source,
            () => BuildRunOptions.ScriptedBytes(input),
            expected.Success,
            expected.ExitCode,
            expected.StandardOutput,
            expected.StandardError);
    }

    private async Task AssertInstalledTargetsMatchBytesAsync(
        string source,
        string evaluatorInput,
        byte[] targetInput)
    {
        EvaluationResult expected = _evaluator.Evaluate(source, evaluatorInput);
        Assert.HasCount(
            0,
            expected.Diagnostics,
            string.Join(Environment.NewLine, expected.Diagnostics.Select(diagnostic => diagnostic.Message)));

        await AssertInstalledTargetsAsync(
            source,
            () => BuildRunOptions.ScriptedBytes(targetInput),
            expected.Success,
            expected.ExitCode,
            expected.StandardOutput,
            expected.StandardError);
    }

    private async Task AssertInstalledTargetsAsync(
        string source,
        Func<BuildRunOptions> options,
        bool expectedSuccess,
        int expectedExitCode,
        string expectedOutput,
        string expectedError)
    {
        bool requireAllTargets = EnvironmentFlagIsEnabled(RequireAllTargetsEnvironmentVariable);
        bool requireJava = EnvironmentFlagIsEnabled(RequireJavaEnvironmentVariable);
        bool requireZeroWarnings = EnvironmentFlagIsEnabled(
            RequireZeroTargetWarningsEnvironmentVariable);
        var failures = new List<string>();
        int executed = 0;

        TestContext.WriteLine(
            $"{RequireAllTargetsEnvironmentVariable}={(requireAllTargets ? "1" : "0")}, " +
            $"{RequireJavaEnvironmentVariable}={(requireJava ? "1" : "0")}, " +
            $"{RequireZeroTargetWarningsEnvironmentVariable}={(requireZeroWarnings ? "1" : "0")}");

        foreach (TargetLanguage language in Targets)
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

            BuildRunResult actual = await toolchain.BuildAndRunAsync(
                Generate(source, language),
                CancellationToken.None,
                options());

            string compilerOutput = string.Join(
                Environment.NewLine,
                new[] { actual.BuildOutput }
                    .Where(output => !string.IsNullOrWhiteSpace(output)));
            string targetExpectedOutput = WithTargetPhysicalNewLines(
                expectedOutput,
                language,
                isStandardError: false);
            string targetExpectedError = WithTargetPhysicalNewLines(
                expectedError,
                language,
                isStandardError: true);

            if (!string.Equals(actual.Stage, "Running", StringComparison.Ordinal))
            {
                failures.Add($"{language}: generated program did not reach its runtime stage." +
                    Environment.NewLine + compilerOutput + Environment.NewLine + actual.StandardError);
            }

            if (actual.ExitCode != expectedExitCode || actual.Success != expectedSuccess)
            {
                failures.Add(
                    $"{language}: expected success={expectedSuccess}, exit={expectedExitCode}; " +
                    $"actual success={actual.Success}, exit={actual.ExitCode}." +
                    Environment.NewLine + compilerOutput + Environment.NewLine + actual.StandardError);
            }

            if (!string.Equals(targetExpectedOutput, actual.StandardOutput, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{language}: stdout differed byte-for-byte after applying only its " +
                    $"established physical line ending.{Environment.NewLine}" +
                    $"Expected: {Visible(targetExpectedOutput)}{Environment.NewLine}" +
                    $"Actual:   {Visible(actual.StandardOutput)}");
            }

            if (!string.Equals(targetExpectedError, actual.StandardError, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{language}: stderr differed byte-for-byte after applying only its " +
                    $"established physical line ending.{Environment.NewLine}" +
                    $"Expected: {Visible(targetExpectedError)}{Environment.NewLine}" +
                    $"Actual:   {Visible(actual.StandardError)}");
            }

            if (GeneratedTargetWarningDetector.ContainsCompilerWarning(language, actual.BuildOutput))
            {
                failures.Add($"{language}: generated target emitted a compiler warning." +
                    Environment.NewLine + compilerOutput);
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
                Targets.Length,
                executed,
                $"{RequireAllTargetsEnvironmentVariable}=1 requires every target to execute.");
        }

        if (executed == 0)
        {
            Assert.Inconclusive("No target toolchain is installed.");
        }
    }

    private static void AssertInputLowering(GeneratedProgram program, TargetLanguage language)
    {
        string generated = string.Join("\n", program.Files.Select(file => file.Content));
        string[] markers = language switch
        {
            TargetLanguage.CSharp => ["_smile_read_byte", "_smile_input_integer", "_smile_skip_lf"],
            TargetLanguage.C => ["fgetc(stdin)", "_smile_input_integer", "skipLf = true"],
            TargetLanguage.MasmX64 =>
                ["smileReadInputLine PROC", "call smileReadInputLine", "smileInputSkipLf"],
            TargetLanguage.JavaScript => ["fs.readSync", "_smile_input_integer", "_smile_skip_lf"],
            TargetLanguage.Java => ["System.in.read()", "_smile_input_integer", "_smile_skip_lf"],
            TargetLanguage.Cobol => ["CALL \"smile_input_", "static int smile_read_line"],
            TargetLanguage.ObjectiveC => ["fgetc(stdin)", "_smile_input_integer", "skipLf = true"],
            TargetLanguage.Swift =>
                ["FileHandle.standardInput.read", "_smile_input_integer", "_smile_skip_lf"],
            TargetLanguage.Python =>
                ["sys.stdin.buffer.read", "_smile_input_integer", "_smile_skip_lf"],
            TargetLanguage.Cpp => ["std::cin.get()", "_smile_input_integer", "_smile_skip_lf"],
            _ => throw new ArgumentOutOfRangeException(nameof(language))
        };

        foreach (string marker in markers)
        {
            StringAssert.Contains(generated, marker, $"{language} omitted INPUT marker '{marker}'.");
        }

        if (language is not TargetLanguage.Cobol)
        {
            Assert.AreEqual(
                -1,
                generated.IndexOf("_smile_pending_byte", StringComparison.Ordinal),
                $"{language} retained pending-byte CR look-ahead state.");
            Assert.AreEqual(
                -1,
                generated.IndexOf("smileInputPendingByte", StringComparison.Ordinal),
                $"{language} retained MASM pending-byte CR look-ahead state.");
        }
    }

    private static void AssertInputBranchLayout(
        GeneratedProgram program,
        TargetLanguage language)
    {
        string[] lines = program.PrimaryFile.Content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        string marker = language switch
        {
            TargetLanguage.Python => "#",
            TargetLanguage.Cobol => "*>",
            TargetLanguage.MasmX64 => ";",
            _ => "//"
        };
        int firstCommentOnly = SingleGeneratedLineIndex(lines, "layout comment-only body");
        int secondCommentOnly = SingleGeneratedLineIndex(lines, "layout second comment-only body");
        int beforeNestedInput = SingleGeneratedLineIndex(lines, "layout before nested INPUT");
        int afterNestedInput = SingleGeneratedLineIndex(lines, "layout after nested INPUT");

        Assert.AreEqual(
            marker + " layout comment-only body",
            lines[firstCommentOnly].TrimStart(),
            language.ToString());
        Assert.AreEqual(
            marker + " layout second comment-only body",
            lines[secondCommentOnly].TrimStart(),
            language.ToString());
        Assert.AreEqual(
            marker + " layout before nested INPUT",
            lines[beforeNestedInput].TrimStart(),
            language.ToString());
        Assert.AreEqual(
            marker + " layout after nested INPUT",
            lines[afterNestedInput].TrimStart(),
            language.ToString());
        Assert.AreEqual(
            string.Empty,
            lines[beforeNestedInput + 1],
            $"{language} lost the blank line before nested INPUT.");
        Assert.AreEqual(
            string.Empty,
            lines[afterNestedInput - 1],
            $"{language} lost the blank line after nested INPUT.");
    }

    private static int SingleGeneratedLineIndex(
        IReadOnlyList<string> lines,
        string payload)
    {
        int[] indices = lines
            .Select((line, index) => (line, index))
            .Where(pair => pair.line.Contains(payload, StringComparison.Ordinal))
            .Select(pair => pair.index)
            .ToArray();
        Assert.HasCount(1, indices, $"Expected one generated line containing '{payload}'.");
        return indices[0];
    }

    private static bool EnvironmentFlagIsEnabled(string name) =>
        string.Equals(Environment.GetEnvironmentVariable(name), "1", StringComparison.Ordinal);

    private static string WithTargetPhysicalNewLines(
        string logicalText,
        TargetLanguage language,
        bool isStandardError)
    {
        // These focused scenarios never PRINT embedded CR/LF as data. Convert
        // only evaluator-authored logical terminators to each Windows runtime's
        // established bytes, then compare the resulting strings ordinally.
        Assert.DoesNotContain(
            '\r',
            logicalText,
            "A physical-line expectation must not normalize source data.");
        string newLine = (language, isStandardError) switch
        {
            (TargetLanguage.JavaScript, _) => "\n",
            (TargetLanguage.MasmX64, true) => "\n",
            (TargetLanguage.Swift, true) => "\n",
            _ => "\r\n"
        };
        return logicalText.Replace("\n", newLine, StringComparison.Ordinal);
    }

    private static string Visible(string text) =>
        "\"" + text
            .Replace("\0", "␀", StringComparison.Ordinal)
            .Replace("\u001A", "␚", StringComparison.Ordinal)
            .Replace("\t", "␉", StringComparison.Ordinal)
            .Replace("\r", "␍", StringComparison.Ordinal)
            .Replace("\n", "␊", StringComparison.Ordinal) + "\"";
}
