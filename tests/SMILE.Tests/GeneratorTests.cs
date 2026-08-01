using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class GeneratorTests
{
    private const string SampleSource = """
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
""";

    private readonly SmileTranspiler _transpiler = new();

    [TestMethod]
    public void Csharp_generator_produces_minimal_complete_program_and_project()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.CSharp);

        Assert.AreEqual("Program.cs", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "using System;",
                "",
                "internal static class Program",
                "{",
                "    private static void Main()",
                "    {",
                "        Console.WriteLine(\"Hello from SMILE!\");",
                "        Console.WriteLine(\"Different syntax, same idea.\");",
                "    }",
                "}"),
            program.PrimaryFile.Content);

        GeneratedFile project = program.Files.Single(file => file.RelativePath == "GeneratedProgram.csproj");
        Assert.AreEqual(
            Lines(
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <PropertyGroup>",
                "    <OutputType>Exe</OutputType>",
                "    <TargetFramework>net10.0</TargetFramework>",
                "    <ImplicitUsings>disable</ImplicitUsings>",
                "    <Nullable>enable</Nullable>",
                "  </PropertyGroup>",
                "</Project>"),
            project.Content);
    }

    [TestMethod]
    public void C_generator_produces_minimal_puts_program()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.C);

        Assert.AreEqual("Program.c", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "#include <stdio.h>",
                "",
                "int main(void)",
                "{",
                "    puts(\"Hello from SMILE!\");",
                "    puts(\"Different syntax, same idea.\");",
                "    return 0;",
                "}"),
            program.PrimaryFile.Content);
    }

    [TestMethod]
    public void Masm_generator_produces_real_x64_masm_with_unique_labels()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.MasmX64);

        Assert.AreEqual("Program.asm", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "option casemap:none",
                "",
                "EXTERN GetStdHandle:PROC",
                "EXTERN WriteFile:PROC",
                "EXTERN ExitProcess:PROC",
                "",
                "STD_OUTPUT_HANDLE EQU -11",
                "",
                ".data",
                "message0 BYTE \"Hello from SMILE!\", 13, 10",
                "message0Length EQU $ - message0",
                "message1 BYTE \"Different syntax, same idea.\", 13, 10",
                "message1Length EQU $ - message1",
                "bytesWritten DWORD ?",
                "",
                ".code",
                "main PROC",
                "    sub rsp, 28h",
                "",
                "    mov ecx, STD_OUTPUT_HANDLE",
                "    call GetStdHandle",
                "",
                "    mov rcx, rax",
                "    lea rdx, message0",
                "    mov r8d, message0Length",
                "    lea r9, bytesWritten",
                "    mov QWORD PTR [rsp + 20h], 0",
                "    call WriteFile",
                "",
                "    mov ecx, STD_OUTPUT_HANDLE",
                "    call GetStdHandle",
                "",
                "    mov rcx, rax",
                "    lea rdx, message1",
                "    mov r8d, message1Length",
                "    lea r9, bytesWritten",
                "    mov QWORD PTR [rsp + 20h], 0",
                "    call WriteFile",
                "",
                "    xor ecx, ecx",
                "    call ExitProcess",
                "main ENDP",
                "",
                "END"),
            program.PrimaryFile.Content);
    }

    [TestMethod]
    public void Javascript_generator_produces_minimal_console_log_program()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.JavaScript);

        Assert.AreEqual("Program.js", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "console.log(\"Hello from SMILE!\");",
                "console.log(\"Different syntax, same idea.\");"),
            program.PrimaryFile.Content);
    }

    [TestMethod]
    public void Java_generator_produces_minimal_required_class_and_main()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.Java);

        Assert.AreEqual("Program.java", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "public final class Program",
                "{",
                "    public static void main(String[] args)",
                "    {",
                "        System.out.println(\"Hello from SMILE!\");",
                "        System.out.println(\"Different syntax, same idea.\");",
                "    }",
                "}"),
            program.PrimaryFile.Content);
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    public void Empty_programs_are_complete_and_end_with_one_newline(TargetLanguage language)
    {
        GeneratedProgram program = Generate(string.Empty, language);

        AssertEndsWithExactlyOneNewline(program.PrimaryFile.Content);
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    public void Generated_output_is_deterministic(TargetLanguage language)
    {
        GeneratedProgram first = Generate(SampleSource, language);
        GeneratedProgram second = Generate(SampleSource, language);

        CollectionAssert.AreEqual(
            first.Files.Select(file => file.Content).ToArray(),
            second.Files.Select(file => file.Content).ToArray());
    }

    [TestMethod]
    public void Generators_escape_backslashes_for_target_languages()
    {
        const string source = "PRINT \"C:\\Temp\\SMILE\"";

        StringAssert.Contains(Generate(source, TargetLanguage.CSharp).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.C).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.Java).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content, "\"C:\\Temp\\SMILE\"");
    }

    private GeneratedProgram Generate(string source, TargetLanguage language)
    {
        TranspileResult result = _transpiler.Transpile(source, language);

        Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return result.GeneratedProgram!;
    }

    private static string Lines(params string[] lines) =>
        string.Join(Environment.NewLine, lines) + Environment.NewLine;

    private static void AssertEndsWithExactlyOneNewline(string text)
    {
        Assert.IsTrue(text.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        Assert.IsFalse(text.EndsWith(Environment.NewLine + Environment.NewLine, StringComparison.Ordinal));
    }
}
