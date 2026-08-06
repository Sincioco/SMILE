using System.Text.RegularExpressions;
using SMILE.Engine;
using SMILE.Toolchains;

namespace SMILE.Tests;

[TestClass]
public sealed class IfTargetConformanceTests
{
    private const string RequireAllTargetsEnvironmentVariable = "SMILE_REQUIRE_ALL_TARGETS";
    private const string RequireJavaEnvironmentVariable = "SMILE_REQUIRE_JAVA";
    private const string RequireZeroTargetWarningsEnvironmentVariable =
        "SMILE_REQUIRE_ZERO_TARGET_WARNINGS";

    private const string StructuralSource = """
LET Choice = 2

IF Choice = 1 THEN
    PRINT Initial branch marker
ELSE IF Choice = 2 THEN
    PRINT First else-if marker
ELSE IF Choice = 3 THEN
    PRINT Second else-if marker
ELSE
    PRINT Final else marker
END IF
""";

    private const string AcceptanceSource = """
LET Score = 85
LET Ready = TRUE
LET Grade = ""
LET Message = ""

IF Score >= 90 AND Ready = TRUE THEN
    SET Grade = "A"
ELSE IF Score >= 80 AND Ready = TRUE THEN
    SET Grade = "B"
ELSE IF Score >= 70 AND Ready = TRUE THEN
    SET Grade = "C"
ELSE
    SET Grade = "Below C"
END IF

IF Grade = "B" THEN
    SET Message ="
Grade B
Ready for the next lesson.
"
ELSE
    SET Message = "Unexpected grade"
END IF

PRINT Grade={Grade}
PRINT {Message}
""";

    private const string ExpectedAcceptanceOutput =
        "Grade=B\nGrade B\nReady for the next lesson.\n";

    private const string StorageConditionSource = """
LET State = "initial"
LET Copy = ""
LET Number = 1
LET _smile_condition = 1

IF _smile_condition = 1 OR TRUE = TRUE THEN
ELSE
END IF

IF _smile_condition = 1 AND FALSE = TRUE THEN
ELSE
END IF

IF 1 = 2 THEN
    SET State = "never selected"
    SET Number = 9
ELSE
    SET State = "A\0B"
    SET Number = 2
END IF

SET Copy = State

IF Copy = "A\0B" AND (Number * 3) / 2 = 3 AND _smile_condition = 1 THEN
    PRINT {Copy}
ELSE
    PRINT wrong branch
END IF
""";

    private const string ExpectedStorageConditionOutput = "A\0B\n";

    private const string LowLevelRuntimeSource = """
LET ChooseFirst = TRUE
LET Source = ""
LET NulValue = ""
LET Composite = ""
LET Number = 0
LET Result = 0

IF ChooseFirst = TRUE THEN
    SET Source = "A"
    SET NulValue = "N\0A"
    SET Number = 1
ELSE
    SET Source = "B"
    SET NulValue = "N\0B"
    SET Number = 2
END IF

LET DirectCopy = Source
SET Composite = Source + "!"
LET Snapshot = Composite
SET Composite = Source + "?"
SET Result = Number + 1
LET NumberCopy = Number

PRINT Direct={DirectCopy}
PRINT Snapshot={Snapshot}
PRINT Composite={Composite}
PRINT Scalar={Result + 1}|{NumberCopy}

IF NulValue + "!" = "N\0A!" THEN
    PRINT Composite NUL={NulValue + "!"}
ELSE
    PRINT wrong composite branch
END IF

PRINT $"Unsafe={Source + "!"}"
""";

    private const string ExpectedLowLevelRuntimeOutput =
        "Direct=A\n" +
        "Snapshot=A!\n" +
        "Composite=A?\n" +
        "Scalar=3|1\n" +
        "Composite NUL=N\0A!\n" +
        "Unsafe=A!\n";

    private const string LowLevelBooleanRuntimeSource = """
LET ChooseFirst = TRUE
LET BranchText = ""
LET BranchNumber = 0
LET Zero = 0
LET Flag = FALSE
LET Comparison = FALSE
LET NestedSet = FALSE
LET BooleanText = ""

IF ChooseFirst = TRUE THEN
    SET BranchText = "A"
    SET BranchNumber = 1
    SET Flag = TRUE
ELSE
    SET BranchText = "B"
    SET BranchNumber = 2
    SET Flag = FALSE
END IF

SET BranchText = BranchText
SET BranchNumber = BranchNumber
SET Flag = Flag

LET FlagCopy = Flag
LET LetComparison = BranchText + "!" = "A!"
LET Negated = NOT Flag
LET BooleanEquality = Flag = (BranchText = "A")
LET NestedEquality = (BranchText = "A") = (BranchNumber = 1)
SET Comparison = BranchText + "?" = "A?"
SET NestedSet = (BranchText = "A") = (BranchNumber = 1)
SET BooleanText = $"Wrapped={BranchText + "!" = "A!"}:tail"

PRINT Flag={Flag}|{FlagCopy}
PRINT Boolean LET={LetComparison}
PRINT Negated={Negated}
PRINT Boolean equality={BooleanEquality}
PRINT Nested equality={NestedEquality}
PRINT Boolean SET={Comparison}
PRINT Nested SET={NestedSet}
PRINT $"Boolean hole={BranchText + "!" = "A!"}"
PRINT {BooleanText}

IF (BranchText = "A") = (BranchNumber = 1) THEN
    PRINT nested Boolean IF selected
ELSE
    PRINT wrong nested Boolean IF branch
END IF

IF TRUE = TRUE THEN
    PRINT pure literal Boolean condition
ELSE
    PRINT wrong pure literal branch
END IF

IF BranchNumber = 1 OR 1 / Zero = 0 THEN
    PRINT scalar OR short-circuited
ELSE
    PRINT wrong scalar OR branch
END IF

IF BranchNumber = 2 AND 1 / Zero = 0 THEN
    PRINT wrong scalar AND branch
ELSE
    PRINT scalar AND short-circuited
END IF

IF BranchNumber = 2 THEN
    PRINT wrong false IF without ELSE body
END IF

PRINT continued after false IF without ELSE
""";

    private const string ExpectedLowLevelBooleanRuntimeOutput =
        "Flag=TRUE|TRUE\n" +
        "Boolean LET=TRUE\n" +
        "Negated=FALSE\n" +
        "Boolean equality=TRUE\n" +
        "Nested equality=TRUE\n" +
        "Boolean SET=TRUE\n" +
        "Nested SET=TRUE\n" +
        "Boolean hole=TRUE\n" +
        "Wrapped=TRUE:tail\n" +
        "nested Boolean IF selected\n" +
        "pure literal Boolean condition\n" +
        "scalar OR short-circuited\n" +
        "scalar AND short-circuited\n" +
        "continued after false IF without ELSE\n";

    private readonly SmileTranspiler _transpiler = new();
    private readonly SmileEvaluator _evaluator = new();
    private readonly ToolchainRegistry _toolchains = ToolchainRegistry.CreateDefault();

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void All_ten_generators_preserve_complete_IF_structure_and_every_branch_body()
    {
        Assert.HasCount(10, TargetLanguageInfo.All);

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string generated = Generate(StructuralSource, language).PrimaryFile.Content;

            foreach (string marker in new[]
            {
                "Initial branch marker",
                "First else-if marker",
                "Second else-if marker",
                "Final else marker"
            })
            {
                StringAssert.Contains(
                    generated,
                    marker,
                    $"{language} deleted the source body containing '{marker}'.");
            }

            switch (language)
            {
                case TargetLanguage.CSharp:
                case TargetLanguage.C:
                case TargetLanguage.JavaScript:
                case TargetLanguage.Java:
                case TargetLanguage.ObjectiveC:
                case TargetLanguage.Cpp:
                    StringAssert.Contains(generated, "if (");
                    StringAssert.Contains(generated, "else if (");
                    StringAssert.Contains(generated, "else");
                    break;

                case TargetLanguage.Swift:
                    StringAssert.Contains(generated, "if ");
                    StringAssert.Contains(generated, "else if ");
                    StringAssert.Contains(generated, "else");
                    break;

                case TargetLanguage.Python:
                    StringAssert.Contains(generated, "if ");
                    StringAssert.Contains(generated, "elif ");
                    StringAssert.Contains(generated, "else:");
                    break;

                case TargetLanguage.Cobol:
                    Assert.IsTrue(
                        Regex.IsMatch(generated, @"(?m)^\s*IF\b"),
                        generated);
                    Assert.IsTrue(
                        Regex.IsMatch(generated, @"(?m)^\s*ELSE\b"),
                        generated);
                    StringAssert.Contains(generated, "END-IF");
                    break;

                case TargetLanguage.MasmX64:
                    StringAssert.Contains(generated, "test eax, eax");
                    Assert.IsTrue(
                        Regex.IsMatch(generated, @"(?m)^\s*jz\s+\S+"),
                        generated);
                    Assert.IsTrue(
                        Regex.IsMatch(generated, @"(?m)^\s*jmp\s+\S+"),
                        generated);
                    break;

                default:
                    Assert.Fail($"No IF structural assertion exists for {language}.");
                    break;
            }
        }
    }

    [TestMethod]
    public void All_ten_generators_preserve_each_official_acceptance_Grade_assignment()
    {
        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string generated = Generate(AcceptanceSource, language).PrimaryFile.Content;
            foreach (string grade in new[] { "A", "B", "C", "Below C" })
            {
                if (language is TargetLanguage.Cobol)
                {
                    StringAssert.Contains(generated, $"MOVE \"{grade}\" TO Grade");
                    continue;
                }

                if (language is TargetLanguage.MasmX64)
                {
                    string escaped = Regex.Escape($"\"{grade}\"");
                    StringAssert.Matches(
                        generated,
                        new Regex(
                            $@"set\d+Value BYTE {escaped}\s+; SET Grade assigned text\.",
                            RegexOptions.CultureInvariant));
                    continue;
                }

                StringAssert.Contains(generated, $"Grade = \"{grade}\"");
            }
        }
    }

    [TestMethod]
    public async Task C_family_and_Cobol_runtime_helpers_cannot_collide_with_student_identifiers()
    {
        const string source = """
LET IF_CONDITION_0 = 1
LET RUNTIME_POINTER = 2
LET SET_LENGTH_0 = 3
LET STATEMENT_5_STRING = ""
LET EXPRESSION_0_STRING = ""
LET fputs = ""
LET memcpy = "M"
LET snprintf = "S"
LET NulValue = ""
LET ChooseFirst = TRUE

IF ChooseFirst = TRUE THEN
    SET fputs = "A"
    SET NulValue = "N\0A"
ELSE
    SET fputs = "B"
    SET NulValue = "N\0B"
END IF

SET STATEMENT_5_STRING = $"{fputs}{IF_CONDITION_0}"

IF fputs + snprintf = "AS" THEN
    PRINT mapped={memcpy}{fputs}{NulValue}
ELSE
    PRINT wrong mapped branch
END IF
""";

        foreach (TargetLanguage language in new[]
        {
            TargetLanguage.C,
            TargetLanguage.ObjectiveC
        })
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            foreach (string mapped in new[]
            {
                "_smile_fputs",
                "_smile_memcpy",
                "_smile_snprintf"
            })
            {
                StringAssert.Contains(generated, mapped);
            }

            StringAssert.Contains(generated, "fputs(");
            StringAssert.Contains(generated, "memcpy(");
            StringAssert.Contains(generated, "snprintf(");
        }

        string cobol = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;
        foreach (string mapped in new[]
        {
            "SMILE-VAR-IF-CONDITION-0",
            "SMILE-VAR-RUNTIME-POINTER",
            "SMILE-VAR-SET-LENGTH-0",
            "SMILE-VAR-STATEMENT-5-STRING",
            "SMILE-VAR-EXPRESSION-0-STRING"
        })
        {
            StringAssert.Contains(cobol, mapped);
        }

        StringAssert.Contains(cobol, "SMILE-RUNTIME-POINTER");
        StringAssert.Contains(cobol, "SMILE-STATEMENT-");
        StringAssert.Contains(cobol, "SMILE-EXPRESSION-");

        EvaluationResult reference = _evaluator.Evaluate(source);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));
        foreach (TargetLanguage language in new[]
        {
            TargetLanguage.C,
            TargetLanguage.ObjectiveC,
            TargetLanguage.Cobol
        })
        {
            IToolchain toolchain = _toolchains.Get(language);
            ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
            if (!status.IsAvailable)
            {
                TestContext.WriteLine($"{language}: collision runtime unavailable - {status.Message}");
                continue;
            }

            BuildRunResult result = await toolchain.BuildAndRunAsync(
                Generate(source, language),
                CancellationToken.None);
            string compilerOutput = FormatBuildAndErrorOutput(result);
            Assert.IsFalse(
                GeneratedTargetWarningDetector.ContainsCompilerWarning(language, compilerOutput),
                $"{language} collision program emitted a warning.{Environment.NewLine}{compilerOutput}");
            Assert.IsTrue(
                result.Success && result.ExitCode == 0,
                $"{language} collision program failed.{Environment.NewLine}{compilerOutput}");
            Assert.AreEqual(
                NormalizePhysicalNewlines(reference.Output),
                NormalizePhysicalNewlines(result.StandardOutput),
                $"{language} collision program stdout differed from SmileEvaluator.");
        }
    }

    [TestMethod]
    public void Nested_IF_generation_and_MASM_labels_are_byte_deterministic()
    {
        const string source = """
LET A = 1
LET B = 2
LET C = 3

IF A = 1 THEN
    PRINT Outer initial marker
    IF B = 2 THEN
        PRINT First nested initial marker
    ELSE
        PRINT First nested else marker
    END IF
ELSE IF A = 2 THEN
    IF C = 3 THEN
        PRINT Second nested initial marker
    ELSE
        PRINT Second nested else marker
    END IF
ELSE
    PRINT Outer else marker
END IF
""";

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            GeneratedProgram first = Generate(source, language);
            GeneratedProgram second = Generate(source, language);

            CollectionAssert.AreEqual(
                first.Files.Select(file => file.RelativePath).ToArray(),
                second.Files.Select(file => file.RelativePath).ToArray(),
                $"{language} generated a different file set.");
            CollectionAssert.AreEqual(
                first.Files.Select(file => file.Content).ToArray(),
                second.Files.Select(file => file.Content).ToArray(),
                $"{language} output was not byte deterministic.");
        }

        string masm = Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content;
        MatchCollection labelMatches = Regex.Matches(
            masm,
            @"(?im)^\s*(if(?<id>\d+)(?:Clause\d+|Else|End)):\s*(?:;.*)?$");
        string[] labels = labelMatches
            .Select(match => match.Groups[1].Value)
            .ToArray();
        string[] ifIds = labelMatches
            .Select(match => match.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.IsGreaterThanOrEqualTo(6, labels.Length, masm);
        Assert.AreEqual(
            labels.Length,
            labels.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            $"Nested MASM IF labels collided.{Environment.NewLine}{masm}");
        Assert.IsGreaterThanOrEqualTo(
            3,
            ifIds.Length,
            $"Each nested BoundIfStatement needs a distinct deterministic label id.{Environment.NewLine}{masm}");
    }

    [TestMethod]
    public void Conditions_and_unselected_nested_branches_select_the_whole_program_Integer_profile()
    {
        const string source = """
LET Selector = 0
LET Value = 1

IF Selector = 0 THEN
    SET Value = 2
ELSE IF Selector = 5000000000 THEN
    SET Value = 3
ELSE
    IF Selector = 2 THEN
        SET Value = 3100000000 * 3000000
    ELSE
        SET Value = -9223372036854775808
    END IF
END IF

PRINT {Value}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "long Selector = 0L;");
        StringAssert.Contains(csharp, "long Value = 1L;");
        StringAssert.Contains(csharp, "5000000000L");

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            StringAssert.Contains(generated, "int64_t Selector = INT64_C(0);");
            StringAssert.Contains(generated, "int64_t Value = INT64_C(1);");
            StringAssert.Contains(generated, "INT64_C(5000000000)");
        }

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "long Selector = 0L;");
        StringAssert.Contains(java, "long Value = 1L;");
        StringAssert.Contains(java, "5000000000L");

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "let Selector = 0n;");
        StringAssert.Contains(javascript, "let Value = 1n;");
        StringAssert.Contains(javascript, "3100000000n * 3000000n");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "Selector: Int64 = 0");
        StringAssert.Contains(swift, "Value: Int64 = 1");
        StringAssert.Contains(swift, "5000000000");

        string cpp = Generate(source, TargetLanguage.Cpp).PrimaryFile.Content;
        StringAssert.Contains(cpp, "std::int64_t Selector = INT64_C(0);");
        StringAssert.Contains(cpp, "std::int64_t Value = INT64_C(1);");
        StringAssert.Contains(cpp, "INT64_C(5000000000)");
    }

    [TestMethod]
    public void Merged_branch_values_promote_later_Integer_intermediates_across_targets()
    {
        const string source = """
LET Ready = TRUE
LET Source = 0
LET Result = 0

IF Ready = TRUE THEN
    SET Source = 1
ELSE
    SET Source = 2000000000
END IF

SET Result = Source * 2
PRINT {Result}
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "long Source = 0L;");
        StringAssert.Contains(csharp, "long Result = 0L;");
        StringAssert.Contains(csharp, "Result = Source * 2L;");

        string java = Generate(source, TargetLanguage.Java).PrimaryFile.Content;
        StringAssert.Contains(java, "long Source = 0L;");
        StringAssert.Contains(java, "long Result = 0L;");
        StringAssert.Contains(java, "Result = Source * 2L;");

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            StringAssert.Contains(generated, "int64_t Source = INT64_C(0);");
            StringAssert.Contains(generated, "int64_t Result = INT64_C(0);");
            StringAssert.Contains(generated, "Result = Source * INT64_C(2);");
        }

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "Source: Int64 = 0");
        StringAssert.Contains(swift, "Result: Int64 = 0");
        StringAssert.Contains(swift, "Result = Source * 2");

        string cpp = Generate(source, TargetLanguage.Cpp).PrimaryFile.Content;
        StringAssert.Contains(cpp, "std::int64_t Source = INT64_C(0);");
        StringAssert.Contains(cpp, "std::int64_t Result = INT64_C(0);");
        StringAssert.Contains(cpp, "Result = Source * INT64_C(2);");

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "Result = Source * 2;");
        Assert.IsFalse(javascript.Contains("2n", StringComparison.Ordinal), javascript);
    }

    [TestMethod]
    public void NUL_and_maximum_String_planning_inspect_unselected_nested_IF_branches()
    {
        const string source = """
LET UsePlain = TRUE
LET Payload = "ABC"

IF UsePlain = TRUE THEN
    SET Payload = "XYZ"
ELSE
    IF UsePlain = FALSE THEN
        SET Payload = "A\0B-LONG"
    ELSE
        SET Payload = ""
    END IF
END IF

PRINT {Payload}
""";

        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(source, language).PrimaryFile.Content;
            Match lengthDeclaration = Regex.Match(
                generated,
                @"size_t\s+(?<name>smileString\d+Length)\s*=\s*3;");

            Assert.IsTrue(
                lengthDeclaration.Success,
                $"{language} did not plan exact String length storage.{Environment.NewLine}{generated}");
            string lengthName = lengthDeclaration.Groups["name"].Value;
            StringAssert.Contains(generated, "Payload = \"A\\000B-LONG\";");
            StringAssert.Contains(generated, $"{lengthName} = 8;");
            StringAssert.Contains(generated, $"fwrite(Payload, 1, {lengthName}, stdout);");
        }

        string cobol = Generate(source, TargetLanguage.Cobol).PrimaryFile.Content;
        Assert.IsTrue(
            Regex.IsMatch(cobol, @"(?m)^01\s+Payload\s+PIC X\(8\)(?:\s|$)"),
            cobol);
        StringAssert.Contains(cobol, "MOVE X\"4100422D4C4F4E47\" TO Payload");
    }

    [TestMethod]
    public void Swift_marks_variables_mutated_only_inside_nested_IF_branches_as_var()
    {
        const string source = """
LET Selector = 0
LET MutableValue = 1
LET StableValue = 9

IF Selector = 1 THEN
    SET MutableValue = 2
ELSE
    IF Selector = 2 THEN
        SET MutableValue = 3
    ELSE
        SET MutableValue = 4
    END IF
END IF

PRINT {MutableValue}
PRINT {StableValue}
""";

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;

        StringAssert.Contains(swift, "var MutableValue: Int = 1");
        StringAssert.Contains(swift, "let StableValue: Int = 9");
        Assert.IsFalse(swift.Contains("let MutableValue:", StringComparison.Ordinal), swift);
    }

    [TestMethod]
    public void Constant_conditions_are_warning_safe_and_helper_names_cannot_collide()
    {
        string csharp = Generate(StorageConditionSource, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, "if (_smile_condition(1 == 2))");
        StringAssert.Contains(
            csharp,
            "if (_smile_condition(_smile__smile_condition == 1 || true == true))");
        StringAssert.Contains(
            csharp,
            "if (_smile_condition(_smile__smile_condition == 1 && false == true))");
        StringAssert.Contains(csharp, "int _smile__smile_condition = 1;");
        StringAssert.Contains(csharp, "private static bool _smile_condition(bool value)");

        string swift = Generate(StorageConditionSource, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "@inline(never)");
        StringAssert.Contains(swift, "if _smile_condition(1 == 2)");
        StringAssert.Contains(
            swift,
            "if _smile_condition(_smile__smile_condition == 1 || true == true)");
        StringAssert.Contains(
            swift,
            "if _smile_condition(_smile__smile_condition == 1 && false == true)");
        StringAssert.Contains(swift, "let _smile__smile_condition: Int = 1");
        StringAssert.Contains(swift, "func _smile_condition(_ value: Bool) -> Bool");
    }

    [TestMethod]
    public void COBOL_and_MASM_supported_post_merge_comparisons_read_current_storage()
    {
        foreach (TargetLanguage language in new[] { TargetLanguage.C, TargetLanguage.ObjectiveC })
        {
            string generated = Generate(StorageConditionSource, language).PrimaryFile.Content;
            StringAssert.Contains(generated, "Copy = State;");
            StringAssert.Contains(generated, "smileString1Length = smileString0Length;");
            StringAssert.Contains(generated, "fwrite(Copy, 1, smileString1Length, stdout);");
        }

        string cobol = Generate(StorageConditionSource, TargetLanguage.Cobol).PrimaryFile.Content;
        StringAssert.Contains(cobol, "01 SMILE-Copy PIC X(14) VALUE SPACES.");
        StringAssert.Contains(cobol, "MOVE State TO SMILE-Copy.");
        StringAssert.Contains(cobol, "MOVE SMILE-SET-LENGTH-0 TO SMILE-SET-LENGTH-1.");
        StringAssert.Contains(cobol, "MOVE 3 TO SMILE-SET-LENGTH-0");
        StringAssert.Contains(cobol, "SMILE-Copy = X\"410042\"");
        StringAssert.Contains(cobol, "FUNCTION INTEGER-PART");
        StringAssert.Contains(cobol, "FUNCTION NUMVAL(SMILE-Number)");
        StringAssert.Contains(cobol, "IF SMILE-IF-CONDITION-3 = 1");

        string masm = Generate(StorageConditionSource, TargetLanguage.MasmX64).PrimaryFile.Content;
        StringAssert.Contains(masm, "mov r10, QWORD PTR [variable1Ptr]");
        StringAssert.Contains(masm, "mov rax, QWORD PTR [variable0Ptr]");
        StringAssert.Contains(masm, "mov QWORD PTR [variable1Ptr], rax");
        StringAssert.Contains(masm, "mov eax, DWORD PTR [variable0Length]");
        StringAssert.Contains(masm, "mov DWORD PTR [variable1Length], eax");
        StringAssert.Contains(masm, "mov rax, QWORD PTR [variable2Integer]");
        StringAssert.Contains(masm, "mov rax, QWORD PTR [variable3Integer]");
        StringAssert.Contains(masm, "imul rax, r9");
        StringAssert.Contains(masm, "idiv r9");
        StringAssert.Contains(masm, "smileRuntimeOverflowMessage BYTE");
        StringAssert.Contains(masm, "smileRuntimeDivisionByZeroMessage BYTE");
        StringAssert.Contains(masm, "smileRuntimeOverflow:");
        StringAssert.Contains(masm, "smileRuntimeDivisionByZero:");
        StringAssert.Contains(masm, "smileFail PROC");
        StringAssert.Contains(masm, "sete al");
        StringAssert.Contains(masm, "cmp r8b, BYTE PTR [r11]");
        StringAssert.Contains(masm, "ifCondition3Comparison0Right BYTE \"A\", 0, \"B\"");
        Assert.AreEqual(
            -1,
            masm.IndexOf("smileReadInputByte PROC", StringComparison.Ordinal),
            "Runtime IF arithmetic must not pull INPUT readers into a no-INPUT program.");
        Assert.AreEqual(
            -1,
            masm.IndexOf("stdinHandle", StringComparison.Ordinal),
            "Runtime IF arithmetic must not allocate INPUT state.");

        string minimalMasm = Generate(
            "LET Value = 1\nPRINT {Value}",
            TargetLanguage.MasmX64).PrimaryFile.Content;
        Assert.AreEqual(
            -1,
            minimalMasm.IndexOf("smileRuntimeOverflowMessage", StringComparison.Ordinal),
            "A source-known no-INPUT program does not need runtime arithmetic errors.");
        Assert.AreEqual(
            -1,
            minimalMasm.IndexOf("smileReadInputByte PROC", StringComparison.Ordinal),
            "A source-known no-INPUT program does not need INPUT readers.");
    }

    [TestMethod]
    public async Task MASM_emits_data_for_a_no_variable_no_PRINT_runtime_condition()
    {
        const string source = """
IF $"{1}" = "1" THEN
ELSE
END IF
""";

        GeneratedProgram program = Generate(source, TargetLanguage.MasmX64);
        string masm = program.PrimaryFile.Content;
        StringAssert.Contains(masm, ".data");
        StringAssert.Contains(masm, "ifCondition0Runtime0");
        StringAssert.Contains(masm, "smileIntegerFormatBuffer");
        StringAssert.Contains(masm, "smileFormatInteger PROC");

        IToolchain toolchain = _toolchains.Get(TargetLanguage.MasmX64);
        ToolchainStatus status = await toolchain.DetectAsync(CancellationToken.None);
        if (!status.IsAvailable)
        {
            TestContext.WriteLine($"MasmX64: no-variable condition unavailable - {status.Message}");
            return;
        }

        BuildRunResult result = await toolchain.BuildAndRunAsync(
            program,
            CancellationToken.None);
        string compilerOutput = FormatBuildAndErrorOutput(result);
        Assert.IsFalse(
            GeneratedTargetWarningDetector.ContainsCompilerWarning(
                TargetLanguage.MasmX64,
                compilerOutput),
            compilerOutput);
        Assert.IsTrue(result.Success && result.ExitCode == 0, compilerOutput);
        Assert.AreEqual(string.Empty, result.StandardOutput);
    }

    [TestMethod]
    public async Task Available_targets_run_the_official_IF_acceptance_program_against_SmileEvaluator()
    {
        await AssertAvailableTargetsMatchEvaluator(
            AcceptanceSource,
            ExpectedAcceptanceOutput,
            "official IF acceptance",
            new[] { "Below C", "Unexpected grade" });
    }

    [TestMethod]
    public async Task Available_targets_keep_literal_IF_warning_free_and_read_post_merge_NUL_storage()
    {
        await AssertAvailableTargetsMatchEvaluator(
            StorageConditionSource,
            ExpectedStorageConditionOutput,
            "literal and post-merge storage IF",
            new[] { "never selected", "wrong branch" });
    }

    [TestMethod]
    public async Task Available_targets_lower_post_IF_unknown_values_without_aliasing_or_text_loss()
    {
        string python = Generate(LowLevelRuntimeSource, TargetLanguage.Python).PrimaryFile.Content;
        StringAssert.Contains(python, "print(\"Unsafe=\" + Source + \"!\")");
        Assert.IsFalse(
            python.Contains("print(\"Unsafe=A!\")", StringComparison.Ordinal),
            python);

        await AssertAvailableTargetsMatchEvaluator(
            LowLevelRuntimeSource,
            ExpectedLowLevelRuntimeOutput,
            "post-IF low-level runtime values",
            new[] { "wrong composite branch" });
    }

    [TestMethod]
    public async Task Available_targets_lower_runtime_Boolean_values_and_scalar_short_circuit()
    {
        await AssertAvailableTargetsMatchEvaluator(
            LowLevelBooleanRuntimeSource,
            ExpectedLowLevelBooleanRuntimeOutput,
            "post-IF Boolean values and scalar short circuit",
            new[]
            {
                "wrong scalar OR branch",
                "wrong scalar AND branch",
                "wrong nested Boolean IF branch",
                "wrong pure literal branch",
                "wrong false IF without ELSE body"
            });
    }

    [TestMethod]
    public void All_generators_preserve_unreachable_division_without_evaluating_it()
    {
        const string source = """
LET Ready = TRUE
LET Zero = 0
LET Source = 0
LET Message = ""

IF Ready = TRUE THEN
    PRINT selected assignment path
ELSE
    SET Message = $"{1 / Zero}"
END IF

IF Ready = TRUE THEN
    PRINT selected output path
ELSE
    PRINT $"{1 / Zero}{Source}"
END IF

IF Ready = TRUE THEN
    PRINT selected clause path
ELSE IF $"{1 / Zero}" = "0" THEN
    PRINT unreachable clause body
ELSE
    PRINT unselected fallback body
END IF
""";

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            string generated = Generate(source, language).PrimaryFile.Content;

            foreach (string bodyMarker in new[]
            {
                "selected assignment path",
                "selected output path",
                "selected clause path",
                "unreachable clause body",
                "unselected fallback body"
            })
            {
                StringAssert.Contains(
                    generated,
                    bodyMarker,
                    $"{language} deleted the IF body containing '{bodyMarker}'.");
            }
        }
    }

    private async Task AssertAvailableTargetsMatchEvaluator(
        string source,
        string expectedOutput,
        string scenario,
        IReadOnlyList<string> requiredBodyValues)
    {
        EvaluationResult reference = _evaluator.Evaluate(source);
        Assert.IsTrue(reference.Success, JoinDiagnostics(reference.Diagnostics));
        string expected = NormalizePhysicalNewlines(reference.Output);
        Assert.AreEqual(expectedOutput, expected, $"The {scenario} evaluator output changed.");

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

        foreach (TargetLanguage language in TargetLanguageInfo.All)
        {
            GeneratedProgram generated = Generate(source, language);
            foreach (string requiredBodyValue in requiredBodyValues)
            {
                if (!generated.PrimaryFile.Content.Contains(requiredBodyValue, StringComparison.Ordinal))
                {
                    failures.Add(
                        $"{language}: {scenario} deleted the source body value '{requiredBodyValue}'.");
                }
            }

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

            BuildRunResult result = await toolchain.BuildAndRunAsync(
                generated,
                CancellationToken.None);
            int failureCountBeforeTarget = failures.Count;
            string compilerOutput = FormatBuildAndErrorOutput(result);

            if (GeneratedTargetWarningDetector.ContainsCompilerWarning(language, compilerOutput))
            {
                failures.Add(
                    $"{language}: generated {scenario} target emitted a compiler warning." +
                    Environment.NewLine + compilerOutput);
            }

            if (language is TargetLanguage.JavaScript or TargetLanguage.Python &&
                !string.IsNullOrWhiteSpace(result.BuildOutput))
            {
                failures.Add(
                    $"{language}: interpreted target unexpectedly reported compile-stage output." +
                    Environment.NewLine + result.BuildOutput);
            }

            if (!result.Success || result.ExitCode != 0)
            {
                failures.Add(
                    $"{language}: {scenario} build/run failed.{Environment.NewLine}" +
                    compilerOutput);
            }
            else if (!string.Equals(
                    expected,
                    NormalizePhysicalNewlines(result.StandardOutput),
                    StringComparison.Ordinal))
            {
                failures.Add($"{language}: {scenario} stdout differed from SmileEvaluator.");
            }

            if (failures.Count != failureCountBeforeTarget)
            {
                continue;
            }

            TestContext.WriteLine(
                language is TargetLanguage.JavaScript or TargetLanguage.Python
                    ? $"{language}: no compile stage; {scenario} runtime matched SmileEvaluator"
                    : $"{language}: zero detected warnings; {scenario} runtime matched SmileEvaluator");
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

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(result.Success, JoinDiagnostics(result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static bool EnvironmentFlagIsEnabled(string name) =>
        string.Equals(
            Environment.GetEnvironmentVariable(name),
            "1",
            StringComparison.Ordinal);

    private static string FormatBuildAndErrorOutput(BuildRunResult result) =>
        string.Join(
            Environment.NewLine,
            new[] { result.BuildOutput, result.StandardError }
                .Where(output => !string.IsNullOrWhiteSpace(output)));

    private static string NormalizePhysicalNewlines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string JoinDiagnostics(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
