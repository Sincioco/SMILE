using SMILE.Engine;

namespace SMILE.Tests;

[TestClass]
public sealed class GeneratorTests
{
    private const string SampleSource = """
PRINT "Hello from SMILE!"
PRINT "Different syntax, same idea."
""";

    private const string FriendlyPrintSource = """
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
PRINT Literal braces: {{Name}}
PRINT A; B; C
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
                "    fputs(\"Hello from SMILE!\", stdout);",
                "    putchar('\\n');",
                "    fputs(\"Different syntax, same idea.\", stdout);",
                "    putchar('\\n');",
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
                ".data                                           ; Static bytes and variables live here.",
                "STD_OUTPUT_HANDLE EQU -11                       ; Magic value for the console output handle.",
                "print0Segment0 BYTE \"Hello from SMILE!\"         ; PRINT #1 literal segment.",
                "print0Segment0Length EQU $ - print0Segment0     ; Length of this literal segment.",
                "print1Segment0 BYTE \"Different syntax, same idea.\" ; PRINT #2 literal segment.",
                "print1Segment0Length EQU $ - print1Segment0     ; Length of this literal segment.",
                "newline BYTE 13, 10                             ; SMILE PRINT appends CR/LF on Windows.",
                "newlineLength EQU $ - newline                   ; Length of the newline bytes.",
                "stdoutHandle QWORD ?                            ; Cached standard output handle.",
                "bytesWritten DWORD ?                            ; WriteFile stores how many bytes it wrote.",
                "",
                ".code                                           ; CPU instructions live here.",
                "main PROC                                       ; Program entry point.",
                "    sub rsp, 28h                                ; Reserve Win64 shadow space and align the stack.",
                "",
                "    mov ecx, STD_OUTPUT_HANDLE                  ; Ask Windows for stdout.",
                "    call GetStdHandle                           ; RAX receives the stdout handle.",
                "    mov QWORD PTR [stdoutHandle], rax           ; Cache stdout for every PRINT segment.",
                "",
                "; PRINT #1                                      ; Write each expression segment, then newline.",
                "    mov rcx, QWORD PTR [stdoutHandle]           ; WriteFile arg 1: stdout handle.",
                "    lea rdx, print0Segment0                     ; WriteFile arg 2: address of literal bytes.",
                "    mov r8d, print0Segment0Length               ; WriteFile arg 3: byte count.",
                "    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.",
                "    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.",
                "    call WriteFile                              ; Emit this literal segment.",
                "    mov rcx, QWORD PTR [stdoutHandle]           ; WriteFile arg 1: stdout handle.",
                "    lea rdx, newline                            ; WriteFile arg 2: address of literal bytes.",
                "    mov r8d, newlineLength                      ; WriteFile arg 3: byte count.",
                "    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.",
                "    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.",
                "    call WriteFile                              ; Emit this literal segment.",
                "",
                "; PRINT #2                                      ; Write each expression segment, then newline.",
                "    mov rcx, QWORD PTR [stdoutHandle]           ; WriteFile arg 1: stdout handle.",
                "    lea rdx, print1Segment0                     ; WriteFile arg 2: address of literal bytes.",
                "    mov r8d, print1Segment0Length               ; WriteFile arg 3: byte count.",
                "    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.",
                "    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.",
                "    call WriteFile                              ; Emit this literal segment.",
                "    mov rcx, QWORD PTR [stdoutHandle]           ; WriteFile arg 1: stdout handle.",
                "    lea rdx, newline                            ; WriteFile arg 2: address of literal bytes.",
                "    mov r8d, newlineLength                      ; WriteFile arg 3: byte count.",
                "    lea r9, bytesWritten                        ; WriteFile arg 4: address for bytes-written result.",
                "    mov QWORD PTR [rsp + 20h], 0                ; WriteFile arg 5 on stack: no overlapped I/O.",
                "    call WriteFile                              ; Emit this literal segment.",
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
                "#include <stdio.h>",
                "",
                "int main(void)",
                "{",
                "    @autoreleasepool",
                "    {",
                "        fputs(\"Hello from SMILE!\", stdout);",
                "        putchar('\\n');",
                "        fputs(\"Different syntax, same idea.\", stdout);",
                "        putchar('\\n');",
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
    public void Friendly_print_generation_preserves_variables_templates_and_concatenation()
    {
        Assert.AreEqual(
            Lines(
                "using System;",
                "",
                "internal static class Program",
                "{",
                "    private static void Main()",
                "    {",
                "        string Name = \"Sin\";",
                "        Console.WriteLine(\"\");",
                "        Console.WriteLine(\"Hello World!\");",
                "        Console.WriteLine(\"Hello World!\");",
                "        Console.WriteLine(\"Hello \" + Name + \"!\");",
                "        Console.WriteLine(\"Hello \" + Name + \"!\");",
                "        Console.WriteLine(\"Hello \" + Name + \"!\");",
                "        Console.WriteLine(\"Literal braces: {Name}\");",
                "        Console.WriteLine(\"A; B; C\");",
                "    }",
                "}"),
            Generate(FriendlyPrintSource, TargetLanguage.CSharp).PrimaryFile.Content);

        Assert.AreEqual(
            Lines(
                "let Name = \"Sin\";",
                "console.log(\"\");",
                "console.log(\"Hello World!\");",
                "console.log(\"Hello World!\");",
                "console.log(\"Hello \" + Name + \"!\");",
                "console.log(\"Hello \" + Name + \"!\");",
                "console.log(\"Hello \" + Name + \"!\");",
                "console.log(\"Literal braces: {Name}\");",
                "console.log(\"A; B; C\");"),
            Generate(FriendlyPrintSource, TargetLanguage.JavaScript).PrimaryFile.Content);

        Assert.AreEqual(
            Lines(
                "let Name = \"Sin\"",
                "print(\"\")",
                "print(\"Hello World!\")",
                "print(\"Hello World!\")",
                "print(\"Hello \" + Name + \"!\")",
                "print(\"Hello \" + Name + \"!\")",
                "print(\"Hello \" + Name + \"!\")",
                "print(\"Literal braces: {Name}\")",
                "print(\"A; B; C\")"),
            Generate(FriendlyPrintSource, TargetLanguage.Swift).PrimaryFile.Content);

        Assert.AreEqual(
            Lines(
                "public final class Program",
                "{",
                "    public static void main(String[] args)",
                "    {",
                "        String Name = \"Sin\";",
                "        System.out.println(\"\");",
                "        System.out.println(\"Hello World!\");",
                "        System.out.println(\"Hello World!\");",
                "        System.out.println(\"Hello \" + Name + \"!\");",
                "        System.out.println(\"Hello \" + Name + \"!\");",
                "        System.out.println(\"Hello \" + Name + \"!\");",
                "        System.out.println(\"Literal braces: {Name}\");",
                "        System.out.println(\"A; B; C\");",
                "    }",
                "}"),
            Generate(FriendlyPrintSource, TargetLanguage.Java).PrimaryFile.Content);

        string c = Generate(FriendlyPrintSource, TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(c, "const char *Name = \"Sin\";");
        StringAssert.Contains(c, "fputs(Name, stdout);");
        StringAssert.Contains(c, "fputs(\"Literal braces: {Name}\", stdout);");

        string objectiveC = Generate(FriendlyPrintSource, TargetLanguage.ObjectiveC).PrimaryFile.Content;
        StringAssert.Contains(objectiveC, "NSString *Name = @\"Sin\";");
        StringAssert.Contains(objectiveC, "fputs([Name UTF8String], stdout);");

        string masm = Generate(FriendlyPrintSource, TargetLanguage.MasmX64).PrimaryFile.Content;
        StringAssert.Contains(masm, "variable0Ptr QWORD ?");
        StringAssert.Contains(masm, "mov rdx, QWORD PTR [variable0Ptr]");
        StringAssert.Contains(masm, "print6Segment0 BYTE \"Literal braces: {Name}\"");
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
        StringAssert.Contains(Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.Swift).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content, "\"C:\\Temp\\SMILE\"");
    }

    [TestMethod]
    public void Generators_use_target_specific_control_character_escapes()
    {
        string source = "PRINT \"A\\B" + '\0' + "C" + '\a' + "D" + '\v' + "E" + '\t' + "F" + '\u007f' + "G\"";

        StringAssert.Contains(
            Generate(source, TargetLanguage.CSharp).PrimaryFile.Content,
            "\"A\\\\B\\0C\\aD\\vE\\tF\\u007fG\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.C).PrimaryFile.Content,
            "\"A\\\\B\\000C\\007D\\013E\\tF\\177G\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content,
            "\"A\\\\B\\000C\\007D\\013E\\tF\\177G\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content,
            "\"A\\\\B\\u0000C\\u0007D\\u000bE\\tF\\u007fG\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.Java).PrimaryFile.Content,
            "\"A\\\\B\\000C\\007D\\013E\\tF\\u007fG\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.Swift).PrimaryFile.Content,
            "\"A\\\\B\\0C\\u{7}D\\u{b}E\\tF\\u{7f}G\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content,
            "\"A\\B\", 0, \"C\", 7, \"D\", 11, \"E\", 9, \"F\", 127, \"G\"");
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
