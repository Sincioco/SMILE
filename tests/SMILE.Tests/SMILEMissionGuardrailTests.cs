using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
[TestCategory("MissionGuardrail")]
public sealed class SMILEMissionGuardrailTests
{
    private const string CanonicalInputSource = """
LET age = 0
PRINT How old are you?
INPUT age
PRINT $"You are {age} years old."
""";

    private const string CoreStructureSource = """
LET Count = 0
PRINT Start
SET Count = 1
IF Count = 1 THEN
    PRINT One
END IF
WHILE Count < 2
    SET Count = Count + 1
END WHILE
LET Message ="
First
Second
"
PRINT {Message}
""";

    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    public void Canonical_CSharp_INPUT_uses_beginner_native_code()
    {
        string generated = Generate(CanonicalInputSource, TargetLanguage.CSharp);

        StringAssert.Contains(generated, "int age = 0;");
        StringAssert.Contains(generated, "Console.WriteLine(\"How old are you?\");");
        StringAssert.Contains(generated, "age = int.Parse(Console.ReadLine()!);");
        StringAssert.Contains(generated, "Console.WriteLine($\"You are {age} years old.\");");
        AssertOmits(
            generated,
            "_smile_read_byte",
            "_smile_read_line",
            "_smile_input_",
            "OpenStandardInput",
            "UTF8Encoding",
            "SMILER150");
    }

    [TestMethod]
    public void Canonical_C_INPUT_uses_beginner_native_code()
    {
        string generated = Generate(CanonicalInputSource, TargetLanguage.C);

        StringAssert.Contains(generated, "int age = 0;");
        StringAssert.Contains(generated, "printf(\"How old are you?\\n\");");
        StringAssert.Contains(generated, "scanf(\"%d%*[\\r\\n]\", &age)");
        StringAssert.Contains(generated, "printf(\"You are %d years old.\\n\", age);");
        AssertOmits(
            generated,
            "_smile_read_line",
            "_smile_valid_utf8",
            "_smile_input_",
            "fgetc(stdin)",
            "SMILER150");
    }

    [TestMethod]
    public void Canonical_MASM_INPUT_uses_beginner_native_code()
    {
        string generated = Generate(CanonicalInputSource, TargetLanguage.MasmX64);

        StringAssert.Contains(generated, "includelib ucrt.lib");
        StringAssert.Contains(generated, "extern printf:proc");
        StringAssert.Contains(generated, "extern scanf:proc");
        StringAssert.Contains(generated, "call printf");
        StringAssert.Contains(generated, "call scanf");
        StringAssert.Contains(generated, "call ExitProcess");
        AssertOmits(
            generated,
            "ReadFile",
            "smileReadInputLine",
            "smileValidateInputUtf8",
            "smileInputSkipLf",
            "SMILER150");
    }

    [TestMethod]
    public void Active_targets_keep_native_core_statement_structures()
    {
        string csharp = Generate(CoreStructureSource, TargetLanguage.CSharp);
        StringAssert.Contains(csharp, "int Count = 0;");
        StringAssert.Contains(csharp, "Console.WriteLine(\"Start\");");
        StringAssert.Contains(csharp, "Count = 1;");
        StringAssert.Contains(csharp, "if (Count == 1)");
        StringAssert.Contains(csharp, "while (Count < 2)");
        StringAssert.Contains(csharp, "First");
        StringAssert.Contains(csharp, "Second");

        string c = Generate(CoreStructureSource, TargetLanguage.C);
        StringAssert.Contains(c, "int Count = 0;");
        StringAssert.Contains(c, "printf(\"Start\\n\");");
        StringAssert.Contains(c, "Count = 1;");
        StringAssert.Contains(c, "if (Count == 1)");
        StringAssert.Contains(c, "while (Count < 2)");
        StringAssert.Contains(c, "First");
        StringAssert.Contains(c, "Second");

        string masm = Generate(CoreStructureSource, TargetLanguage.MasmX64);
        StringAssert.Contains(masm, "_smile_Count DWORD 0");
        StringAssert.Contains(masm, "; SET Count");
        StringAssert.Contains(masm, "; IF");
        StringAssert.Contains(masm, "; WHILE");
        StringAssert.Contains(masm, "smilewhileHead");
        StringAssert.Contains(masm, "add eax, r10d");
        StringAssert.Contains(masm, "jo smileArithmeticOverflow");
        StringAssert.Contains(masm, "First");
        StringAssert.Contains(masm, "Second");
        StringAssert.Contains(masm, "call printf");
        AssertOmits(
            masm,
            "ReadFile",
            "smileWriteBuffer",
            "smileFormatInteger",
            "smileRuntimeOverflow",
            "smileCheckedAdd");
    }

    [TestMethod]
    public void CSharp_and_C_use_native_String_and_Boolean_INPUT()
    {
        const string source = """
LET Name = ""
LET Ready = FALSE
INPUT Name
INPUT Ready
PRINT {Name}
PRINT {Ready}
""";

        string csharp = Generate(source, TargetLanguage.CSharp);
        StringAssert.Contains(csharp, "Name = Console.ReadLine() ?? string.Empty;");
        StringAssert.Contains(csharp, "Ready = bool.Parse(Console.ReadLine()!);");
        AssertOmits(csharp, "_smile_input_", "SMILER150");

        string c = Generate(source, TargetLanguage.C);
        StringAssert.Contains(c, "static char smileInput2Buffer[256];");
        StringAssert.Contains(c, "fgets(smileInput2Buffer, sizeof smileInput2Buffer, stdin)");
        StringAssert.Contains(c, "scanf(\"%5s%*[\\r\\n]\", smileInput3Buffer)");
        StringAssert.Contains(c, "strcmp(smileInput3Buffer, \"TRUE\")");
        AssertOmits(c, "_smile_input_", "_smile_read_line", "SMILER150");

        string masm = Generate(source, TargetLanguage.MasmX64);
        StringAssert.Contains(masm, "smileInput0String BYTE 256 DUP (0)");
        StringAssert.Contains(masm, "smileInput1Boolean BYTE 6 DUP (0)");
        StringAssert.Contains(masm, "extern scanf:proc");
        StringAssert.Contains(masm, "extern _stricmp:proc");
        StringAssert.Contains(masm, "call scanf");
        StringAssert.Contains(masm, "call _stricmp");
        AssertOmits(masm, "ReadFile", "smileReadInputLine", "SMILER150");

        const string copiedInput = """
LET Name = ""
LET Copy = ""
INPUT Name
SET Copy = Name
PRINT {Copy}
""";
        string copiedC = Generate(copiedInput, TargetLanguage.C);
        // C needs one small owned buffer here so Copy retains a String value
        // when a later execution overwrites Name's INPUT buffer.
        StringAssert.Contains(copiedC, "memcpy(");
        AssertOmits(copiedC, "_smile_input_", "_smile_read_line");
    }

    private string Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);
        Assert.IsTrue(
            result.Success,
            language + Environment.NewLine +
            string.Join(Environment.NewLine, result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        return result.GeneratedProgram!.PrimaryFile.Content;
    }

    private static void AssertOmits(string generated, params string[] forbiddenMarkers)
    {
        foreach (string marker in forbiddenMarkers)
        {
            Assert.AreEqual(
                -1,
                generated.IndexOf(marker, StringComparison.Ordinal),
                $"Generated code retained forbidden legacy marker '{marker}'.");
        }
    }
}
