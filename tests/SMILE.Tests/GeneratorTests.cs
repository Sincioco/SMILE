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
    public void Masm_generator_produces_real_x64_masm_with_unique_labels_and_comments()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.MasmX64);

        Assert.AreEqual("Program.asm", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "option casemap:none                             ; Keep symbol names case-sensitive.",
                "",
                "EXTERN GetStdHandle:PROC                        ; Windows API: get standard console handles.",
                "EXTERN WriteFile:PROC                           ; Windows API: write bytes to the console.",
                "EXTERN ExitProcess:PROC                         ; Windows API: terminate the process.",
                "",
                "STD_OUTPUT_HANDLE EQU -11                       ; Magic value for the console output handle.",
                "",
                ".data                                           ; Static bytes and variables live here.",
                "message0 BYTE \"Hello from SMILE!\", 13, 10       ; PRINT text #1, ending with CR/LF.",
                "message0Length EQU $ - message0                 ; Length equals current address minus the label.",
                "message1 BYTE \"Different syntax, same idea.\", 13, 10 ; PRINT text #2, ending with CR/LF.",
                "message1Length EQU $ - message1                 ; Length equals current address minus the label.",
                "bytesWritten DWORD ?                            ; WriteFile stores how many bytes it wrote.",
                "",
                ".code                                           ; CPU instructions live here.",
                "main PROC                                       ; Program entry point.",
                "    sub rsp, 28h                                ; Reserve Win64 shadow space and align the stack.",
                "",
                "    mov ecx, STD_OUTPUT_HANDLE                  ; First argument: ask for stdout.",
                "    call GetStdHandle                           ; RAX now holds the stdout handle.",
                "",
                "    mov rcx, rax                                ; WriteFile arg 1: stdout handle.",
                "    lea rdx, message0                           ; WriteFile arg 2: address of message bytes.",
                "    mov r8d, message0Length                     ; WriteFile arg 3: byte count.",
                "    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.",
                "    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.",
                "    call WriteFile                              ; Emit the PRINT line.",
                "",
                "    mov ecx, STD_OUTPUT_HANDLE                  ; First argument: ask for stdout.",
                "    call GetStdHandle                           ; RAX now holds the stdout handle.",
                "",
                "    mov rcx, rax                                ; WriteFile arg 1: stdout handle.",
                "    lea rdx, message1                           ; WriteFile arg 2: address of message bytes.",
                "    mov r8d, message1Length                     ; WriteFile arg 3: byte count.",
                "    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.",
                "    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.",
                "    call WriteFile                              ; Emit the PRINT line.",
                "",
                "    xor ecx, ecx                                ; ExitProcess arg 1: process exit code 0.",
                "    call ExitProcess                            ; End the program.",
                "main ENDP                                       ; End of the main procedure.",
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
    public void Objective_c_generator_produces_minimal_foundation_program()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.ObjectiveC);

        Assert.AreEqual("Program.m", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "#import <Foundation/Foundation.h>",
                "",
                "int main(int argc, const char * argv[])",
                "{",
                "    @autoreleasepool",
                "    {",
                "        NSLog(@\"Hello from SMILE!\");",
                "        NSLog(@\"Different syntax, same idea.\");",
                "    }",
                "",
                "    return 0;",
                "}"),
            program.PrimaryFile.Content);
    }

    [TestMethod]
    public void Swift_generator_produces_minimal_top_level_program()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.Swift);

        Assert.AreEqual("Program.swift", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "print(\"Hello from SMILE!\")",
                "print(\"Different syntax, same idea.\")"),
            program.PrimaryFile.Content);
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
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
    [DataRow(TargetLanguage.ObjectiveC)]
    [DataRow(TargetLanguage.Swift)]
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
        StringAssert.Contains(Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content, "@\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.Swift).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
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
