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

    private const string PrimaryFriendlyPrintSource = """
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
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
    public void C_generator_produces_idiomatic_printf_program()
    {
        GeneratedProgram program = Generate(PrimaryFriendlyPrintSource, TargetLanguage.C);

        Assert.AreEqual("Program.c", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "#include <stdio.h>",
                "",
                "int main(void)",
                "{",
                "    const char *Name = \"Sin\";",
                "",
                "    printf(\"\\n\");",
                "    printf(\"Hello World!\\n\");",
                "    printf(\"Hello World!\\n\");",
                "    printf(\"Hello %s!\\n\", Name);",
                "    printf(\"Hello %s!\\n\", Name);",
                "    printf(\"Hello %s!\\n\", Name);",
                "",
                "    return 0;",
                "}"),
            program.PrimaryFile.Content);

        Assert.AreEqual(6, CountOccurrences(program.PrimaryFile.Content, "printf("));
        Assert.IsFalse(program.PrimaryFile.Content.Contains("fputs(", StringComparison.Ordinal));
        Assert.IsFalse(program.PrimaryFile.Content.Contains("putchar(", StringComparison.Ordinal));
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
                "print0Segment0 BYTE \"Hello from SMILE!\"         ; PRINT #1 canonical text.",
                "print0Segment0Length EQU $ - print0Segment0     ; Length of this print text.",
                "print1Segment0 BYTE \"Different syntax, same idea.\" ; PRINT #2 canonical text.",
                "print1Segment0Length EQU $ - print1Segment0     ; Length of this print text.",
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
    public void Cobol_generator_produces_free_format_program()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.Cobol);

        Assert.AreEqual("Program.cob", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                ">>SOURCE FORMAT IS FREE",
                "IDENTIFICATION DIVISION.",
                "PROGRAM-ID. Program.",
                "",
                "PROCEDURE DIVISION.",
                "*> Each SMILE PRINT becomes one DISPLAY operation.",
                "    DISPLAY \"Hello from SMILE!\".",
                "    DISPLAY \"Different syntax, same idea.\".",
                "    STOP RUN."),
            program.PrimaryFile.Content);
    }

    [TestMethod]
    public void Objective_c_generator_produces_minimal_console_program()
    {
        GeneratedProgram program = Generate(SampleSource, TargetLanguage.ObjectiveC);

        Assert.AreEqual("Program.m", program.PrimaryFile.RelativePath);
        Assert.AreEqual(
            Lines(
                "#include <stdio.h>",
                "",
                "int main(void)",
                "{",
                "    printf(\"Hello from SMILE!\\n\");",
                "    printf(\"Different syntax, same idea.\\n\");",
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
                "        Console.WriteLine();",
                "        Console.WriteLine(\"Hello World!\");",
                "        Console.WriteLine(\"Hello World!\");",
                "        Console.WriteLine($\"Hello {Name}!\");",
                "        Console.WriteLine($\"Hello {Name}!\");",
                "        Console.WriteLine(\"Hello \" + Name + \"!\");",
                "        Console.WriteLine(\"Literal braces: {Name}\");",
                "        Console.WriteLine(\"A; B; C\");",
                "    }",
                "}"),
            Generate(FriendlyPrintSource, TargetLanguage.CSharp).PrimaryFile.Content);

        Assert.AreEqual(
            Lines(
                "let Name = \"Sin\";",
                "console.log();",
                "console.log(\"Hello World!\");",
                "console.log(\"Hello World!\");",
                "console.log(`Hello ${Name}!`);",
                "console.log(`Hello ${Name}!`);",
                "console.log(\"Hello \" + Name + \"!\");",
                "console.log(\"Literal braces: {Name}\");",
                "console.log(\"A; B; C\");"),
            Generate(FriendlyPrintSource, TargetLanguage.JavaScript).PrimaryFile.Content);

        Assert.AreEqual(
            Lines(
                "let Name: String = \"Sin\"",
                "print()",
                "print(\"Hello World!\")",
                "print(\"Hello World!\")",
                "print(\"Hello \\(Name)!\")",
                "print(\"Hello \\(Name)!\")",
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
                "        System.out.println();",
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
        StringAssert.Contains(c, "printf(\"Hello %s!\\n\", Name);");
        StringAssert.Contains(c, "printf(\"Literal braces: {Name}\\n\");");
        Assert.IsFalse(c.Contains("fputs(", StringComparison.Ordinal));
        Assert.IsFalse(c.Contains("putchar(", StringComparison.Ordinal));

        string objectiveC = Generate(FriendlyPrintSource, TargetLanguage.ObjectiveC).PrimaryFile.Content;
        StringAssert.Contains(objectiveC, "const char *Name = \"Sin\";");
        StringAssert.Contains(objectiveC, "printf(\"Hello %s!\\n\", Name);");
        Assert.IsFalse(objectiveC.Contains("NSLog", StringComparison.Ordinal));
        Assert.IsFalse(objectiveC.Contains("fputs(", StringComparison.Ordinal));

        string cobol = Generate(FriendlyPrintSource, TargetLanguage.Cobol).PrimaryFile.Content;
        StringAssert.Contains(cobol, "01 Name PIC X(3) VALUE \"Sin\".");
        StringAssert.Contains(cobol, "DISPLAY X\"0A\" WITH NO ADVANCING.");
        StringAssert.Contains(cobol, "DISPLAY \"Hello Sin!\".");
        StringAssert.Contains(cobol, "DISPLAY \"Literal braces: {Name}\".");

        string masm = Generate(FriendlyPrintSource, TargetLanguage.MasmX64).PrimaryFile.Content;
        StringAssert.Contains(masm, "variable0Ptr QWORD ?");
        StringAssert.Contains(masm, "print3Segment0 BYTE \"Hello Sin!\"");
        StringAssert.Contains(masm, "print6Segment0 BYTE \"Literal braces: {Name}\"");
    }

    [TestMethod]
    public void Csharp_generator_preserves_explicit_interpolation()
    {
        GeneratedProgram program = Generate("""
LET Name = "Sin"
PRINT $"Hello {Name}!"
""", TargetLanguage.CSharp);

        StringAssert.Contains(program.PrimaryFile.Content, """Console.WriteLine($"Hello {Name}!");""");
    }

    [TestMethod]
    public void Csharp_generator_uses_interpolation_for_friendly_raw_placeholders()
    {
        GeneratedProgram program = Generate("""
LET Name = "Sin"
PRINT Hello {Name}!
""", TargetLanguage.CSharp);

        StringAssert.Contains(program.PrimaryFile.Content, """Console.WriteLine($"Hello {Name}!");""");
    }

    [TestMethod]
    public void Csharp_generator_preserves_explicit_concatenation()
    {
        GeneratedProgram program = Generate("""
LET Name = "Sin"
PRINT "Hello " + Name + "!"
""", TargetLanguage.CSharp);

        StringAssert.Contains(program.PrimaryFile.Content, """Console.WriteLine("Hello " + Name + "!");""");
    }

    [TestMethod]
    public void Javascript_generator_preserves_interpolation_and_concatenation_intent()
    {
        StringAssert.Contains(
            Generate("""
LET Name = "Sin"
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
""", TargetLanguage.JavaScript).PrimaryFile.Content,
            "console.log(`Hello ${Name}!`);");

        StringAssert.Contains(
            Generate("""
LET Name = "Sin"
PRINT "Hello " + Name + "!"
""", TargetLanguage.JavaScript).PrimaryFile.Content,
            "console.log(\"Hello \" + Name + \"!\");");
    }

    [TestMethod]
    public void Swift_generator_preserves_interpolation_and_concatenation_intent()
    {
        string interpolation = Generate("""
LET Name = "Sin"
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
""", TargetLanguage.Swift).PrimaryFile.Content;

        StringAssert.Contains(interpolation, "print(\"Hello \\(Name)!\")");

        StringAssert.Contains(
            Generate("""
LET Name = "Sin"
PRINT "Hello " + Name + "!"
""", TargetLanguage.Swift).PrimaryFile.Content,
            "print(\"Hello \" + Name + \"!\")");
    }

    [TestMethod]
    public void Java_generator_uses_concatenation_as_interpolation_fallback()
    {
        GeneratedProgram program = Generate("""
LET Name = "Sin"
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
""", TargetLanguage.Java);

        Assert.AreEqual(2, CountOccurrences(program.PrimaryFile.Content, "System.out.println(\"Hello \" + Name + \"!\");"));
    }

    [TestMethod]
    public void Generators_escape_literal_braces_in_interpolation_oriented_forms()
    {
        const string source = """
LET Name = "Sin"
PRINT Literal braces: {{Name}}
PRINT $"Literal braces: {{Name}}"
""";

        string csharp = Generate(source, TargetLanguage.CSharp).PrimaryFile.Content;
        StringAssert.Contains(csharp, """Console.WriteLine("Literal braces: {Name}");""");
        StringAssert.Contains(csharp, """Console.WriteLine($"Literal braces: {{Name}}");""");

        string javascript = Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content;
        StringAssert.Contains(javascript, "console.log(\"Literal braces: {Name}\");");
        StringAssert.Contains(javascript, "console.log(`Literal braces: {Name}`);");

        string swift = Generate(source, TargetLanguage.Swift).PrimaryFile.Content;
        StringAssert.Contains(swift, "print(\"Literal braces: {Name}\")");
    }

    [TestMethod]
    public void Interpolation_text_escapes_target_interpolation_markers()
    {
        StringAssert.Contains(
            Generate("""
PRINT $"Literal ${{Name}} and `tick`"
""", TargetLanguage.JavaScript).PrimaryFile.Content,
            """console.log(`Literal \${Name} and \`tick\``);""");

        StringAssert.Contains(
            Generate("""
PRINT $"Literal \\(Name)"
""", TargetLanguage.Swift).PrimaryFile.Content,
            "print(\"Literal \\\\(Name)\")");
    }

    [TestMethod]
    public void Generators_preserve_multiple_and_adjacent_interpolation_parts()
    {
        const string multiple = """
LET FirstName = "Sin"
LET LastName = "Cioco"
PRINT $"{FirstName} {LastName}"
""";

        StringAssert.Contains(
            Generate(multiple, TargetLanguage.CSharp).PrimaryFile.Content,
            """Console.WriteLine($"{FirstName} {LastName}");""");
        StringAssert.Contains(
            Generate(multiple, TargetLanguage.JavaScript).PrimaryFile.Content,
            "console.log(`${FirstName} ${LastName}`);");
        StringAssert.Contains(
            Generate(multiple, TargetLanguage.Swift).PrimaryFile.Content,
            "print(\"\\(FirstName) \\(LastName)\")");

        const string adjacent = """
LET A = "A"
LET B = "B"
PRINT $"{A}{B}"
""";

        StringAssert.Contains(
            Generate(adjacent, TargetLanguage.CSharp).PrimaryFile.Content,
            """Console.WriteLine($"{A}{B}");""");
        StringAssert.Contains(
            Generate(adjacent, TargetLanguage.JavaScript).PrimaryFile.Content,
            "console.log(`${A}${B}`);");
        StringAssert.Contains(
            Generate(adjacent, TargetLanguage.Swift).PrimaryFile.Content,
            "print(\"\\(A)\\(B)\")");
    }

    [TestMethod]
    public void Csharp_generator_preserves_interpolation_even_when_only_one_variable_is_inside()
    {
        GeneratedProgram program = Generate("""
LET Name = "Sin"
PRINT $"{Name}"
""", TargetLanguage.CSharp);

        StringAssert.Contains(program.PrimaryFile.Content, """Console.WriteLine($"{Name}");""");
        Assert.IsFalse(program.PrimaryFile.Content.Contains("Console.WriteLine(Name);", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Csharp_generator_uses_idiomatic_blank_print_without_rewriting_empty_string_literals()
    {
        GeneratedProgram program = Generate("""
PRINT
PRINT ""
""", TargetLanguage.CSharp);

        StringAssert.Contains(program.PrimaryFile.Content, "Console.WriteLine();");
        StringAssert.Contains(program.PrimaryFile.Content, """Console.WriteLine("");""");
    }

    [TestMethod]
    public void C_generator_escapes_printf_percent_literals_in_lowered_text()
    {
        string percentOnly = Generate("PRINT Progress: 100%", TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(percentOnly, "printf(\"Progress: 100%%\\n\");");

        string percentWithVariable = Generate("""
LET Name = "Sin"
PRINT {Name} is 100% ready.
""", TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(percentWithVariable, "printf(\"%s is 100%% ready.\\n\", Name);");
        Assert.IsFalse(percentWithVariable.Contains("printf(Name", StringComparison.Ordinal));
    }

    [TestMethod]
    public void C_generator_preserves_adjacent_and_repeated_interpolation_arguments()
    {
        string multiple = Generate("""
LET FirstName = "Sin"
LET LastName = "Cioco"
PRINT $"{FirstName} {LastName}"
""", TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(multiple, "printf(\"%s %s\\n\", FirstName, LastName);");

        string adjacent = Generate("""
LET A = "A"
LET B = "B"
PRINT $"{A}{B}{A}"
""", TargetLanguage.C).PrimaryFile.Content;
        StringAssert.Contains(adjacent, "printf(\"%s%s%s\\n\", A, B, A);");
    }

    [TestMethod]
    public void C_generator_preserves_literal_and_ordinary_quoted_braces_without_arguments()
    {
        string braces = Generate("""
LET Name = "Sin"
PRINT Literal braces: {{Name}}
PRINT $"Literal braces: {{Name}}"
PRINT "Hello {Name}!"
""", TargetLanguage.C).PrimaryFile.Content;

        Assert.AreEqual(3, CountOccurrences(braces, "printf("));
        Assert.AreEqual(0, CountOccurrences(braces, ", Name);"));
        StringAssert.Contains(braces, "printf(\"Literal braces: {Name}\\n\");");
        StringAssert.Contains(braces, "printf(\"Hello {Name}!\\n\");");
    }

    [TestMethod]
    public void C_generator_uses_printf_for_blank_and_empty_string_prints()
    {
        GeneratedProgram program = Generate("""
PRINT
PRINT ""
""", TargetLanguage.C);

        Assert.AreEqual(2, CountOccurrences(program.PrimaryFile.Content, "printf(\"\\n\");"));
        Assert.IsFalse(program.PrimaryFile.Content.Contains("putchar(", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Objective_c_generator_uses_idiomatic_printf_for_print()
    {
        string source = """
LET Name = "Sin"

PRINT
PRINT Hello {Name}!
PRINT Progress: 100%
""";

        string objectiveC = Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content;

        Assert.AreEqual(3, CountOccurrences(objectiveC, "printf("));
        StringAssert.Contains(objectiveC, "printf(\"\\n\");");
        StringAssert.Contains(objectiveC, "printf(\"Hello %s!\\n\", Name);");
        StringAssert.Contains(objectiveC, "printf(\"Progress: 100%%\\n\");");
        Assert.IsFalse(objectiveC.Contains("NSLog", StringComparison.Ordinal));
        Assert.IsFalse(objectiveC.Contains("fputs(", StringComparison.Ordinal));
        Assert.IsFalse(objectiveC.Contains("putchar(", StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(TargetLanguage.CSharp)]
    [DataRow(TargetLanguage.C)]
    [DataRow(TargetLanguage.MasmX64)]
    [DataRow(TargetLanguage.JavaScript)]
    [DataRow(TargetLanguage.Java)]
    [DataRow(TargetLanguage.Cobol)]
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
    [DataRow(TargetLanguage.Cobol)]
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
        const string source = "PRINT \"C:\\\\Temp\\\\SMILE\"";

        StringAssert.Contains(Generate(source, TargetLanguage.CSharp).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.C).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\\n\"");
        StringAssert.Contains(Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.Java).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.Cobol).PrimaryFile.Content, "\"C:\\Temp\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\\n\"");
        StringAssert.Contains(Generate(source, TargetLanguage.Swift).PrimaryFile.Content, "\"C:\\\\Temp\\\\SMILE\"");
        StringAssert.Contains(Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content, "\"C:\\Temp\\SMILE\"");
    }

    [TestMethod]
    public void Generators_use_target_specific_control_character_escapes()
    {
        const string source = """
PRINT "A\\B\0C\bD\fE\tF"
""";

        StringAssert.Contains(
            Generate(source, TargetLanguage.CSharp).PrimaryFile.Content,
            "\"A\\\\B\\0C\\bD\\fE\\tF\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.C).PrimaryFile.Content,
            "printf(\"A\\\\B%cC\\bD\\fE\\tF\\n\", 0);");
        StringAssert.Contains(
            Generate(source, TargetLanguage.ObjectiveC).PrimaryFile.Content,
            "printf(\"A\\\\B%cC\\bD\\fE\\tF\\n\", 0);");
        StringAssert.Contains(
            Generate(source, TargetLanguage.JavaScript).PrimaryFile.Content,
            "\"A\\\\B\\u0000C\\bD\\fE\\tF\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.Java).PrimaryFile.Content,
            "\"A\\\\B\\000C\\bD\\fE\\tF\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.Cobol).PrimaryFile.Content,
            "X\"415C42004308440C450946\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.Swift).PrimaryFile.Content,
            "\"A\\\\B\\0C\\u{8}D\\u{c}E\\tF\"");
        StringAssert.Contains(
            Generate(source, TargetLanguage.MasmX64).PrimaryFile.Content,
            "\"A\\B\", 0, \"C\", 8, \"D\", 12, \"E\", 9, \"F\"");
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

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int position = 0;
        while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
        {
            count++;
            position += value.Length;
        }

        return count;
    }
}
