using System.IO;
using System.Text;
using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class InputEvaluatorTests
{
    private const string StringProgram = "LET Value = \"Before\"\nINPUT Value\nPRINT [{Value}]";
    private const string IntegerProgram = "LET Value = 0\nINPUT Value\nPRINT [{Value}]";
    private const string BooleanProgram = "LET Value = FALSE\nINPUT Value\nPRINT [{Value}]";
    private readonly SmileEvaluator _evaluator = new();

    [TestMethod]
    public void String_INPUT_preserves_hspace_Unicode_and_NUL_exactly()
    {
        const string input = "  Sin\t🙂\0  \n";

        EvaluationResult result = _evaluator.Evaluate(StringProgram, input);

        AssertSuccess(result);
        Assert.AreEqual("[  Sin\t🙂\0  ]\n", result.StandardOutput);
        Assert.AreEqual(string.Empty, result.StandardError);
        Assert.AreEqual(0, result.ExitCode);
    }

    [TestMethod]
    public void Empty_terminated_line_and_final_unterminated_line_are_distinct_valid_Strings()
    {
        const string source = """
LET First = "Before"
LET Second = "Before"
INPUT First
INPUT Second
PRINT [{First}]
PRINT [{Second}]
""";

        EvaluationResult result = _evaluator.Evaluate(source, "\nFinal");

        AssertSuccess(result);
        Assert.AreEqual("[]\n[Final]\n", result.Output);
    }

    [TestMethod]
    public void Repeated_INPUT_replaces_the_same_variable_once_per_line()
    {
        const string source = """
LET Value = "Before"
INPUT Value
PRINT [{Value}]
INPUT Value
PRINT [{Value}]
""";

        EvaluationResult result = _evaluator.Evaluate(source, "First\nSecond\n");

        AssertSuccess(result);
        Assert.AreEqual("[First]\n[Second]\n", result.Output);
    }

    [TestMethod]
    public void INPUT_recognizes_CR_LF_and_CRLF_as_logical_line_endings()
    {
        const string source = """
LET First = ""
LET Second = ""
LET Third = ""
INPUT First
INPUT Second
INPUT Third
PRINT {First}
PRINT {Second}
PRINT {Third}
""";

        EvaluationResult result = _evaluator.Evaluate(source, "Alpha\rBeta\nGamma\r\n");

        AssertSuccess(result);
        Assert.AreEqual("Alpha\nBeta\nGamma\n", result.Output);
    }

    [TestMethod]
    public void Raw_INPUT_distinguishes_CRLF_from_standalone_CR()
    {
        const string source = """
LET First = ""
LET Second = ""
INPUT First
INPUT Second
PRINT {First}
PRINT {Second}
""";

        using var crlf = new MemoryStream(Encoding.UTF8.GetBytes("First\r\nSecond\n"));
        EvaluationResult crlfResult = _evaluator.Evaluate(source, crlf);
        AssertSuccess(crlfResult);
        Assert.AreEqual("First\nSecond\n", crlfResult.Output);

        using var standalone = new MemoryStream(Encoding.UTF8.GetBytes("First\rSecond\n"));
        EvaluationResult standaloneResult = _evaluator.Evaluate(source, standalone);
        AssertSuccess(standaloneResult);
        Assert.AreEqual("First\nSecond\n", standaloneResult.Output);
    }

    [TestMethod]
    public void String_INPUT_accepts_a_line_beyond_the_superseded_4096_byte_boundary()
    {
        string entered = new('A', SmileLanguage.MaximumInputLineUtf8Bytes + 1);

        EvaluationResult result = _evaluator.Evaluate(StringProgram, entered + "\n");

        AssertSuccess(result);
        Assert.AreEqual($"[{entered}]\n", result.Output);
    }

    [TestMethod]
    public void String_INPUT_has_no_shared_cross_target_byte_limit()
    {
        string entered = string.Concat(Enumerable.Repeat("🙂", 1025));

        EvaluationResult result = _evaluator.Evaluate(
            "PRINT Before\n" + StringProgram + "\nPRINT After",
            entered + "\n");

        AssertSuccess(result);
        Assert.AreEqual($"Before\n[{entered}]\nAfter\n", result.Output);
    }

    [TestMethod]
    public void Raw_redirected_INPUT_accepts_more_than_4096_bytes()
    {
        byte[] bytes = Enumerable.Repeat(
                (byte)'A',
                SmileLanguage.MaximumInputLineUtf8Bytes + 1)
            .Append((byte)'\n')
            .ToArray();
        using var input = new MemoryStream(bytes);

        EvaluationResult result = _evaluator.Evaluate(StringProgram, input);

        AssertSuccess(result);
        Assert.AreEqual(
            $"[{new string('A', SmileLanguage.MaximumInputLineUtf8Bytes + 1)}]\n",
            result.Output);
    }

    [TestMethod]
    [DataRow("0\n", "0")]
    [DataRow("+49\n", "49")]
    [DataRow("-49\n", "-49")]
    [DataRow(" \t49\t \n", "49")]
    [DataRow("-9223372036854775808\n", "-9223372036854775808")]
    [DataRow("9223372036854775807\n", "9223372036854775807")]
    [DataRow("-0\n", "0")]
    public void Integer_INPUT_accepts_only_the_invariant_signed_decimal_grammar(
        string input,
        string expected)
    {
        EvaluationResult result = _evaluator.Evaluate(IntegerProgram, input);

        AssertSuccess(result);
        Assert.AreEqual($"[{expected}]\n", result.Output);
    }

    [TestMethod]
    [DataRow("\n", "SMILER1503")]
    [DataRow("+\n", "SMILER1503")]
    [DataRow("-\n", "SMILER1503")]
    [DataRow("1.5\n", "SMILER1503")]
    [DataRow("1,000\n", "SMILER1503")]
    [DataRow("1_000\n", "SMILER1503")]
    [DataRow("0x31\n", "SMILER1503")]
    [DataRow("49 years\n", "SMILER1503")]
    [DataRow(" 49 \n", "SMILER1503")]
    [DataRow("9223372036854775808\n", "SMILER1504")]
    [DataRow("-9223372036854775809\n", "SMILER1504")]
    public void Integer_INPUT_distinguishes_malformed_text_from_range_failure(
        string input,
        string expectedCode)
    {
        EvaluationResult result = _evaluator.Evaluate(IntegerProgram, input);

        AssertRuntimeError(result, expectedCode);
        Assert.AreEqual(string.Empty, result.Output);
    }

    public static IEnumerable<object[]> BooleanInputCasePermutations()
    {
        foreach (string expected in new[] { "TRUE", "FALSE" })
        {
            int permutationCount = 1 << expected.Length;
            for (int mask = 0; mask < permutationCount; mask++)
            {
                char[] text = expected.ToCharArray();
                for (int index = 0; index < text.Length; index++)
                {
                    if ((mask & (1 << index)) != 0)
                    {
                        text[index] = (char)(text[index] + ('a' - 'A'));
                    }
                }

                yield return new object[] { new string(text) + "\n", expected };
            }
        }

        // Keep the surrounding ASCII space/tab rule in the same conversion test.
        yield return new object[] { " \tTrUe\t \n", "TRUE" };
    }

    [TestMethod]
    [DynamicData(nameof(BooleanInputCasePermutations))]
    public void Boolean_INPUT_accepts_TRUE_and_FALSE_ordinally_case_insensitive(
        string input,
        string expected)
    {
        EvaluationResult result = _evaluator.Evaluate(BooleanProgram, input);

        AssertSuccess(result);
        Assert.AreEqual($"[{expected}]\n", result.Output);
    }

    [TestMethod]
    [DataRow("1\n")]
    [DataRow("0\n")]
    [DataRow("YES\n")]
    [DataRow("NO\n")]
    [DataRow("ON\n")]
    [DataRow("OFF\n")]
    [DataRow("T\n")]
    [DataRow("F\n")]
    [DataRow("falſe\n")]
    [DataRow("\n")]
    public void Boolean_INPUT_rejects_every_noncanonical_value(string input)
    {
        EvaluationResult result = _evaluator.Evaluate(BooleanProgram, input);

        AssertRuntimeError(result, "SMILER1505");
    }

    [TestMethod]
    public void Executed_INPUT_with_immediate_EOF_reports_SMILER1501()
    {
        EvaluationResult result = _evaluator.Evaluate(StringProgram);

        AssertRuntimeError(result, "SMILER1501");
        Assert.AreEqual(
            "SMILE Runtime Error SMILER1501: Input ended before a value was received for 'Value'.\n",
            result.ErrorOutput);
    }

    [TestMethod]
    public void Malformed_redirected_UTF8_reports_SMILER1506()
    {
        using var input = new MemoryStream(new byte[] { 0xC3, 0x28, 0x0A });

        EvaluationResult result = _evaluator.Evaluate(StringProgram, input);

        AssertRuntimeError(result, "SMILER1506");
    }

    [TestMethod]
    public void Malformed_UTF8_on_a_later_line_does_not_fail_an_earlier_INPUT()
    {
        const string source = """
LET First = ""
LET Second = ""
INPUT First
PRINT {First}
INPUT Second
""";
        byte[] validFirstLine = Encoding.UTF8.GetBytes("First\n");
        using var input = new MemoryStream(
            [.. validFirstLine, 0xC3, 0x28, 0x0A]);

        EvaluationResult result = _evaluator.Evaluate(source, input);

        AssertRuntimeError(result, "SMILER1506", "Second");
        Assert.AreEqual("First\n", result.Output);
    }

    [TestMethod]
    public void Standalone_CR_completes_before_a_future_stream_read_failure()
    {
        const string source = "LET Value = \"\"\nINPUT Value\nPRINT {Value}";
        using var input = new ThrowAfterStandaloneCarriageReturnStream();

        EvaluationResult result = _evaluator.Evaluate(source, input);

        AssertSuccess(result);
        Assert.AreEqual("A\n", result.Output);
    }

    [TestMethod]
    public void Read_failure_after_CR_is_reported_for_the_next_INPUT()
    {
        const string source = """
LET First = ""
LET Second = ""
INPUT First
PRINT {First}
INPUT Second
""";
        using var input = new ThrowAfterStandaloneCarriageReturnStream();

        EvaluationResult result = _evaluator.Evaluate(source, input);

        AssertRuntimeError(result, "SMILER1506", "Second");
        Assert.AreEqual("A\n", result.Output);
        StringAssert.Contains(result.ErrorOutput, "'Second'");
    }

    [TestMethod]
    public void Reader_failure_reports_SMILER1506_without_throwing()
    {
        EvaluationResult result = _evaluator.Evaluate(StringProgram, new ThrowingTextReader());

        AssertRuntimeError(result, "SMILER1506");
    }

    [TestMethod]
    [DataRow(
        "LET Value = 0\nINPUT Value\nLET Result = Value + 1\nPRINT {Result}",
        "9223372036854775807\n")]
    [DataRow(
        "LET Value = 0\nINPUT Value\nLET Result = Value - 1\nPRINT {Result}",
        "-9223372036854775808\n")]
    [DataRow(
        "LET Value = 0\nINPUT Value\nLET Result = Value * 2\nPRINT {Result}",
        "9223372036854775807\n")]
    [DataRow(
        "LET Value = 0\nINPUT Value\nLET Result = -Value\nPRINT {Result}",
        "-9223372036854775808\n")]
    public void Input_dependent_arithmetic_overflow_reports_SMILER1206(
        string source,
        string input)
    {
        EvaluationResult result = _evaluator.Evaluate(source, input);

        AssertRuntimeError(result, "SMILER1206");
    }

    [TestMethod]
    public void Input_dependent_division_reports_the_two_runtime_failures()
    {
        const string divideByInput = "LET Divisor = 1\nINPUT Divisor\nLET Result = 1 / Divisor";
        EvaluationResult zero = _evaluator.Evaluate(divideByInput, "0\n");
        AssertRuntimeError(zero, "SMILER1207");

        const string minByNegativeOne = """
LET Left = 0
LET Right = 0
INPUT Left
INPUT Right
LET Result = Left / Right
""";
        EvaluationResult overflow = _evaluator.Evaluate(
            minByNegativeOne,
            "-9223372036854775808\n-1\n");
        AssertRuntimeError(overflow, "SMILER1206");
    }

    [TestMethod]
    public void Input_dependent_division_truncates_toward_zero()
    {
        const string source = "LET Value = 1\nINPUT Value\nLET Result = Value / 2\nPRINT {Result}";

        EvaluationResult result = _evaluator.Evaluate(source, "-7\n");

        AssertSuccess(result);
        Assert.AreEqual("-3\n", result.Output);
    }

    [TestMethod]
    public void Runtime_short_circuit_suppresses_unreached_division_failure()
    {
        const string source = """
LET Divisor = 0
INPUT Divisor
LET Safe = FALSE AND (1 / Divisor = 0)
PRINT {Safe}
""";

        EvaluationResult result = _evaluator.Evaluate(source, "0\n");

        AssertSuccess(result);
        Assert.AreEqual("FALSE\n", result.Output);
    }

    [TestMethod]
    public void Runtime_unknown_left_operand_controls_whether_the_right_failure_is_reached()
    {
        const string source = """
LET Check = FALSE
INPUT Check
LET Result = Check = TRUE AND (1 / 0 = 0)
PRINT {Result}
""";

        EvaluationResult safe = _evaluator.Evaluate(source, "FALSE\n");
        AssertSuccess(safe);
        Assert.AreEqual("FALSE\n", safe.Output);

        EvaluationResult reached = _evaluator.Evaluate(source, "TRUE\n");
        AssertRuntimeError(reached, "SMILER1207");
        Assert.AreEqual(string.Empty, reached.Output);
    }

    [TestMethod]
    public void Only_the_selected_IF_branch_consumes_input()
    {
        const string source = """
LET ChooseFirst = FALSE
LET Value = ""
INPUT ChooseFirst
IF ChooseFirst = TRUE THEN
    INPUT Value
ELSE
    INPUT Value
END IF
PRINT {Value}
""";

        EvaluationResult result = _evaluator.Evaluate(
            source,
            "FALSE\nSelected else\nUnread extra\n");

        AssertSuccess(result);
        Assert.AreEqual("Selected else\n", result.Output);
    }

    [TestMethod]
    public void ELSE_IF_and_nested_IF_consume_only_the_selected_INPUT_lines()
    {
        const string source = """
LET Choice = 0
LET Nested = FALSE
LET Value = ""
INPUT Choice
IF Choice = 1 THEN
    INPUT Value
ELSE IF Choice = 2 THEN
    INPUT Nested
    IF Nested = TRUE THEN
        INPUT Value
    ELSE
        SET Value = "nested fallback"
    END IF
ELSE
    SET Value = "outer fallback"
END IF
PRINT {Value}
""";

        EvaluationResult result = _evaluator.Evaluate(
            source,
            "2\nTRUE\nSelected nested\nUnread extra\n");

        AssertSuccess(result);
        Assert.AreEqual("Selected nested\n", result.Output);
    }

    private static void AssertSuccess(EvaluationResult result)
    {
        Assert.IsTrue(result.Success, Join(result));
        Assert.IsNull(result.RuntimeError);
        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(string.Empty, result.ErrorOutput);
    }

    private static void AssertRuntimeError(
        EvaluationResult result,
        string expectedCode,
        string variableName = "Value")
    {
        Assert.IsFalse(result.Success, Join(result));
        Assert.IsNotNull(result.RuntimeError);
        Assert.AreEqual(expectedCode, result.RuntimeError.Code);
        Assert.AreEqual(1, result.ExitCode);
        string expectedMessage = expectedCode switch
        {
            "SMILER1206" => "Integer arithmetic overflow.",
            "SMILER1207" => "Division by zero.",
            "SMILER1501" => $"Input ended before a value was received for '{variableName}'.",
            "SMILER1503" => $"Input for '{variableName}' is not a valid Integer.",
            "SMILER1504" => $"Input for '{variableName}' is outside the signed 64-bit Integer range.",
            "SMILER1505" => $"Input for '{variableName}' must be TRUE or FALSE.",
            "SMILER1506" => $"Input for '{variableName}' could not be read as valid UTF-8 text.",
            _ => throw new AssertFailedException($"Unexpected runtime code {expectedCode}.")
        };
        Assert.AreEqual(
            $"SMILE Runtime Error {expectedCode}: {expectedMessage}\n",
            result.ErrorOutput);
        Assert.HasCount(0, result.Diagnostics);
    }

    private static string Join(EvaluationResult result) =>
        string.Join(Environment.NewLine, result.Diagnostics) + Environment.NewLine + result.ErrorOutput;

    private sealed class ThrowingTextReader : TextReader
    {
        public override string? ReadLine() => throw new IOException("Injected read failure.");
    }

    private sealed class ThrowAfterStandaloneCarriageReturnStream : Stream
    {
        private int _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int ReadByte() => _position++ switch
        {
            0 => 'A',
            1 => '\r',
            _ => throw new IOException("Future line read failure.")
        };

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
