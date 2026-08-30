using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal static class CoreBasicCodeGenerator
{
    public static GeneratedProgram Generate(BoundProgram program, TargetLanguage language)
    {
        string content = language switch
        {
            TargetLanguage.CSharp => new StructuredWriter(program, language).WriteCSharp(),
            TargetLanguage.C => new StructuredWriter(program, language).WriteC(),
            TargetLanguage.MasmX64 => new MasmWriter(program).Write(),
            TargetLanguage.JavaScript => new StructuredWriter(program, language).WriteJavaScript(),
            TargetLanguage.Java => new StructuredWriter(program, language).WriteJava(),
            TargetLanguage.Cobol => new StructuredWriter(program, language).WriteCobol(),
            TargetLanguage.ObjectiveC => new StructuredWriter(program, language).WriteObjectiveC(),
            TargetLanguage.Swift => new StructuredWriter(program, language).WriteSwift(),
            TargetLanguage.Python => new StructuredWriter(program, language).WritePython(),
            TargetLanguage.Cpp => new StructuredWriter(program, language).WriteCpp(),
            _ => throw new ArgumentOutOfRangeException(nameof(language), language, null)
        };

        var files = new List<GeneratedFile>
        {
            new(TargetLanguageInfo.GetPrimaryFileName(language), content, IsPrimary: true)
        };
        if (language is TargetLanguage.CSharp)
        {
            const string project = """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
""";
            files.Add(new GeneratedFile("GeneratedProgram.csproj", project, IsPrimary: false));
        }

        return new GeneratedProgram(language, files);
    }

    private enum LoopKind
    {
        For,
        Do
    }

    private sealed record LoopFrame(
        LoopKind Kind,
        int Id,
        string Label,
        string? CompletionFlag = null);

    private sealed class StructuredWriter
    {
        private readonly BoundProgram _program;
        private readonly TargetLanguage _language;
        private readonly TargetIdentifierMap _identifiers;
        private readonly StringBuilder _builder = new();
        private readonly List<LoopFrame> _loops = new();
        private int _indent;
        private int _loopId;
        private int _forTempId;

        public StructuredWriter(BoundProgram program, TargetLanguage language)
        {
            _program = program;
            _language = language;
            _identifiers = TargetIdentifierMap.Create(program, language);
        }

        public string WriteCSharp()
        {
            Line("using System;");
            Line();
            Line("internal static class Program");
            Line("{");
            _indent++;
            Line("private static void Main()");
            Line("{");
            _indent++;
            WriteDeclarations();
            WriteItems(_program.SourceItems);
            _indent--;
            Line("}");
            _indent--;
            Line("}");
            return Finish();
        }

        public string WriteJavaScript()
        {
            Line("\"use strict\";");
            Line();
            WriteDeclarations();
            WriteItems(_program.SourceItems);
            return Finish();
        }

        public string WriteJava()
        {
            Line("public final class Program {");
            _indent++;
            Line("public static void main(String[] args) {");
            _indent++;
            WriteDeclarations();
            WriteItems(_program.SourceItems);
            _indent--;
            Line("}");
            _indent--;
            Line("}");
            return Finish();
        }

        public string WriteSwift()
        {
            if (HasEndProgram(_program.SourceItems))
            {
                Line("import Foundation");
                Line();
            }

            WriteDeclarations();
            WriteItems(_program.SourceItems);
            return Finish();
        }

        public string WritePython()
        {
            bool hasModulo = HasModulo(_program.SourceItems);
            if (HasDivision(_program.SourceItems) || hasModulo)
            {
                Line("def _smile_div(left, right):");
                _indent++;
                Line("if right == 0:");
                _indent++;
                Line("raise ZeroDivisionError(\"SMILE division by zero\")");
                _indent--;
                Line("quotient = abs(left) // abs(right)");
                Line("return -quotient if (left < 0) != (right < 0) else quotient");
                _indent--;
                Line();
            }

            if (hasModulo)
            {
                Line("def _smile_mod(left, right):");
                _indent++;
                Line("return left - _smile_div(left, right) * right");
                _indent--;
                Line();
            }

            foreach (int id in GetPythonExitLoopIds(_program.SourceItems))
            {
                Line($"class _SmileExitLoop{id}(Exception):");
                _indent++;
                Line("pass");
                _indent--;
                Line();
            }

            WriteDeclarations();
            WriteItems(_program.SourceItems);
            return Finish();
        }

        public string WriteCpp()
        {
            Line("#include <cstdint>");
            Line("#include <iostream>");
            Line("#include <string>");
            Line();
            Line("int main()");
            Line("{");
            _indent++;
            WriteDeclarations();
            WriteItems(_program.SourceItems);
            Line("return 0;");
            _indent--;
            Line("}");
            return Finish();
        }

        public string WriteC()
        {
            Line("#include <inttypes.h>");
            Line("#include <stdbool.h>");
            Line("#include <stdio.h>");
            if (HasTextComparison(_program.SourceItems) && !HasTextConcatenation(_program.SourceItems))
            {
                Line("#include <string.h>");
            }
            if (HasTextConcatenation(_program.SourceItems))
            {
                Line("#include <stdlib.h>");
                Line("#include <string.h>");
                Line();
                WriteCTextConcatHelper();
            }

            Line();
            Line("int main(void)");
            Line("{");
            _indent++;
            WriteDeclarations();
            WriteItems(_program.SourceItems);
            Line("return 0;");
            _indent--;
            Line("}");
            return Finish();
        }

        public string WriteObjectiveC()
        {
            Line("#include <inttypes.h>");
            Line("#include <stdbool.h>");
            Line("#include <stdio.h>");
            if (HasTextComparison(_program.SourceItems) && !HasTextConcatenation(_program.SourceItems))
            {
                Line("#include <string.h>");
            }
            if (HasTextConcatenation(_program.SourceItems))
            {
                Line("#include <stdlib.h>");
                Line("#include <string.h>");
                Line();
                WriteCTextConcatHelper();
            }
            Line();
            Line("int main(void)");
            Line("{");
            _indent++;
            WriteDeclarations();
            WriteItems(_program.SourceItems);
            Line("return 0;");
            _indent--;
            Line("}");
            return Finish();
        }

        public string WriteCobol()
        {
            Line("       IDENTIFICATION DIVISION.");
            Line("       PROGRAM-ID. Program.");
            Line("       DATA DIVISION.");
            Line("       WORKING-STORAGE SECTION.");
            foreach (VariableSymbol variable in _program.Variables)
            {
                string name = Name(variable);
                if (variable.IsConstant)
                {
                    BoundConstStatement? constant = FindConstant(variable);
                    if (constant is not null)
                    {
                        Line($"       78 {name} VALUE {CobolLiteral(constant.Value)}.");
                    }
                }
                else
                {
                    string declaration = variable.Type switch
                    {
                        SmileType.Integer => $"       01 {name} PIC S9(18) VALUE 0.",
                        SmileType.Boolean => $"       01 {name} PIC 9 VALUE 0.",
                        _ => $"       01 {name} PIC X(4096) VALUE SPACES."
                    };
                    Line(declaration);
                }
            }

            if (EnumerateStatements(_program.SourceItems)
                .OfType<BoundCorePrintStatement>()
                .SelectMany(print => print.Values)
                .Any(value => value.Type is SmileType.Integer))
            {
                Line("       01 SMILE-DISPLAY-NUMBER PIC -(17)9.");
            }

            Line("       PROCEDURE DIVISION.");
            WriteItems(_program.SourceItems);
            Line("           STOP RUN.");
            return Finish();
        }

        private void WriteDeclarations()
        {
            foreach (VariableSymbol variable in _program.Variables)
            {
                if (variable.IsConstant)
                {
                    BoundConstStatement? constant = FindConstant(variable);
                    if (constant is null)
                    {
                        continue;
                    }

                    Line(ConstantDeclaration(variable, constant.Value));
                }
                else
                {
                    Line(VariableDeclaration(variable));
                }
            }

            if (_program.Variables.Count > 0)
            {
                Line();
            }
        }

        private string ConstantDeclaration(VariableSymbol variable, SmileValue value)
        {
            string name = Name(variable);
            string literal = Literal(value);
            return _language switch
            {
                TargetLanguage.CSharp => $"const {TypeName(variable.Type)} {name} = {literal};",
                TargetLanguage.C => variable.Type is SmileType.String
                    ? $"const char * const {name} = {literal};"
                    : $"const {TypeName(variable.Type)} {name} = {literal};",
                TargetLanguage.JavaScript => $"const {name} = {literal};",
                TargetLanguage.Java => $"final {TypeName(variable.Type)} {name} = {literal};",
                TargetLanguage.ObjectiveC => variable.Type is SmileType.String
                    ? $"const char * const {name} = {literal};"
                    : $"const {TypeName(variable.Type)} {name} = {literal};",
                TargetLanguage.Swift => $"let {name}: {TypeName(variable.Type)} = {literal}",
                TargetLanguage.Python => $"{name} = {literal}",
                TargetLanguage.Cpp => $"const {TypeName(variable.Type)} {name} = {literal};",
                _ => string.Empty
            };
        }

        private string VariableDeclaration(VariableSymbol variable)
        {
            string name = Name(variable);
            string value = DefaultLiteral(variable.Type);
            return _language switch
            {
                TargetLanguage.CSharp => $"{TypeName(variable.Type)} {name} = {value};",
                TargetLanguage.C => $"{TypeName(variable.Type)} {name} = {value};",
                TargetLanguage.JavaScript => $"let {name} = {value};",
                TargetLanguage.Java => $"{TypeName(variable.Type)} {name} = {value};",
                TargetLanguage.ObjectiveC => $"{TypeName(variable.Type)} {name} = {value};",
                TargetLanguage.Swift => $"var {name}: {TypeName(variable.Type)} = {value}",
                TargetLanguage.Python => $"{name} = {value}",
                TargetLanguage.Cpp => $"{TypeName(variable.Type)} {name} = {value};",
                _ => string.Empty
            };
        }

        private void WriteItems(IReadOnlyList<BoundSourceItem> items)
        {
            foreach (BoundSourceItem item in items)
            {
                switch (item)
                {
                    case BoundBlankLine:
                        Line();
                        break;
                    case BoundFullLineComment comment:
                        Line(CommentPrefix() + comment.Payload);
                        break;
                    case BoundStatement statement:
                        WriteStatement(statement);
                        if (statement is BoundExitStatement or BoundEndProgramStatement)
                        {
                            return;
                        }

                        break;
                }
            }
        }

        private void WriteStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundDimStatement or BoundConstStatement:
                    return;
                case BoundSetStatement assignment:
                    WriteAssignment(assignment);
                    return;
                case BoundCorePrintStatement print:
                    WritePrint(print);
                    return;
                case BoundIfStatement conditional:
                    WriteIf(conditional);
                    return;
                case BoundForStatement loop:
                    WriteFor(loop);
                    return;
                case BoundDoStatement loop:
                    WriteDo(loop);
                    return;
                case BoundExitStatement exit:
                    WriteExit(exit);
                    return;
                case BoundEndProgramStatement:
                    WriteEndProgram();
                    return;
            }
        }

        private void WriteAssignment(BoundSetStatement assignment)
        {
            string name = Name(assignment.Variable);
            string expression = Expression(assignment.Value);
            if (_language is TargetLanguage.Cobol)
            {
                if (assignment.Variable.Type is SmileType.Boolean)
                {
                    Line($"           IF {expression}");
                    Line($"               MOVE 1 TO {name}");
                    Line("           ELSE");
                    Line($"               MOVE 0 TO {name}");
                    Line("           END-IF");
                }
                else if (assignment.Variable.Type is SmileType.Integer)
                {
                    Line($"           COMPUTE {name} = {expression}");
                }
                else
                {
                    Line($"           MOVE {expression} TO {name}");
                }

                return;
            }

            Line(_language is TargetLanguage.Swift or TargetLanguage.Python
                ? $"{name} = {expression}"
                : $"{name} = {expression};");
        }

        private void WritePrint(BoundCorePrintStatement print)
        {
            foreach (BoundExpression value in print.Values)
            {
                string display = DisplayExpression(value);
                switch (_language)
                {
                    case TargetLanguage.CSharp:
                        Line($"Console.Write({display});");
                        break;
                    case TargetLanguage.JavaScript:
                        Line($"process.stdout.write({display});");
                        break;
                    case TargetLanguage.Java:
                        Line($"System.out.print({display});");
                        break;
                    case TargetLanguage.Swift:
                        Line($"print({display}, terminator: \"\")");
                        break;
                    case TargetLanguage.Python:
                        Line($"print({display}, end=\"\")");
                        break;
                    case TargetLanguage.Cpp:
                        Line($"std::cout << {display};");
                        break;
                    case TargetLanguage.C:
                        WriteCPrint(value, display, objectiveC: false);
                        break;
                    case TargetLanguage.ObjectiveC:
                        WriteCPrint(value, display, objectiveC: false);
                        break;
                    case TargetLanguage.Cobol:
                        if (value.Type is SmileType.Integer)
                        {
                            Line($"           MOVE {Expression(value)} TO SMILE-DISPLAY-NUMBER");
                            Line("           DISPLAY FUNCTION TRIM(SMILE-DISPLAY-NUMBER) WITH NO ADVANCING");
                        }
                        else if (value.Type is SmileType.Boolean)
                        {
                            Line($"           IF {Expression(value)}");
                            Line("               DISPLAY \"TRUE\" WITH NO ADVANCING");
                            Line("           ELSE");
                            Line("               DISPLAY \"FALSE\" WITH NO ADVANCING");
                            Line("           END-IF");
                        }
                        else
                        {
                            bool hasExactDisplay = value is BoundStringLiteralExpression ||
                                value is BoundVariableExpression { Variable.IsConstant: true };
                            Line(hasExactDisplay
                                ? $"           DISPLAY {Expression(value)} WITH NO ADVANCING"
                                : $"           DISPLAY FUNCTION TRIM({Expression(value)}, TRAILING) WITH NO ADVANCING");
                        }
                        break;
                }
            }

            if (print.SuppressNewLine)
            {
                return;
            }

            Line(_language switch
            {
                TargetLanguage.CSharp => "Console.WriteLine();",
                TargetLanguage.JavaScript => "process.stdout.write(\"\\n\");",
                TargetLanguage.Java => "System.out.println();",
                TargetLanguage.Swift => "print()",
                TargetLanguage.Python => "print()",
                TargetLanguage.Cpp => "std::cout << '\\n';",
                TargetLanguage.C => "fputc('\\n', stdout);",
                TargetLanguage.ObjectiveC => "fputc('\\n', stdout);",
                TargetLanguage.Cobol => "           DISPLAY X\"0A\" WITH NO ADVANCING",
                _ => string.Empty
            });
        }

        private void WriteCPrint(BoundExpression value, string display, bool objectiveC)
        {
            if (value.Type is SmileType.String)
            {
                Line(objectiveC
                    ? $"fputs([{display} UTF8String], stdout);"
                    : $"fputs({display}, stdout);");
            }
            else if (value.Type is SmileType.Boolean)
            {
                Line($"fputs({display}, stdout);");
            }
            else
            {
                Line($"printf(\"%\" PRId64, {display});");
            }
        }

        private void WriteIf(BoundIfStatement conditional)
        {
            if (_language is TargetLanguage.Cobol)
            {
                for (int index = 0; index < conditional.Clauses.Count; index++)
                {
                    BoundConditionalClause clause = conditional.Clauses[index];
                    Line(index == 0
                        ? $"           IF {Expression(clause.Condition)}"
                        : $"           ELSE IF {Expression(clause.Condition)}");
                    WriteItems(clause.SourceItems);
                }

                if (conditional.HasElseClause)
                {
                    Line("           ELSE");
                    WriteItems(conditional.ElseSourceItems);
                }

                for (int index = 0; index < conditional.Clauses.Count; index++)
                {
                    Line("           END-IF");
                }

                return;
            }

            for (int index = 0; index < conditional.Clauses.Count; index++)
            {
                BoundConditionalClause clause = conditional.Clauses[index];
                string keyword = index == 0 ? "if" : _language is TargetLanguage.Python ? "elif" : "else if";
                if (_language is TargetLanguage.Python)
                {
                    Line($"{keyword} {Expression(clause.Condition)}:");
                    WritePythonSuite(clause.SourceItems);
                }
                else if (_language is TargetLanguage.Swift)
                {
                    Line($"{keyword} {Expression(clause.Condition)} {{");
                    _indent++;
                    WriteItems(clause.SourceItems);
                    _indent--;
                    Line("}");
                }
                else
                {
                    Line($"{keyword} ({Expression(clause.Condition)})");
                    Line("{");
                    _indent++;
                    WriteItems(clause.SourceItems);
                    _indent--;
                    Line("}");
                }
            }

            if (!conditional.HasElseClause)
            {
                return;
            }

            if (_language is TargetLanguage.Python)
            {
                Line("else:");
                WritePythonSuite(conditional.ElseSourceItems);
            }
            else
            {
                Line("else");
                Line("{");
                _indent++;
                WriteItems(conditional.ElseSourceItems);
                _indent--;
                Line("}");
            }
        }

        private void WriteFor(BoundForStatement loop)
        {
            int id = ++_loopId;
            int temp = ++_forTempId;
            string label = $"smile_for_{id}";
            string endLabel = $"{label}_end";
            string counter = Name(loop.Counter);
            string lower = Expression(loop.LowerBound);
            string upper = Expression(loop.UpperBound);
            string comparison = loop.IsDescending ? ">=" : "<=";
            string step = loop.IsDescending ? "--" : "++";
            bool hasCrossKindExit = HasCrossKindExitTargetingLoop(loop.SourceItems, LoopKind.For);
            string? completionFlag = _language is TargetLanguage.Swift
                ? $"_smileForCompleted{temp}"
                : null;
            var frame = new LoopFrame(LoopKind.For, id, label, completionFlag);
            _loops.Add(frame);

            switch (_language)
            {
                case TargetLanguage.CSharp:
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC:
                case TargetLanguage.Cpp:
                {
                    string type = TypeName(SmileType.Integer);
                    string end = $"_smile_for_end_{temp}";
                    Line($"{type} {end} = {upper};");
                    Line($"for ({counter} = {lower}; {counter} {comparison} {end}; {counter}{step})");
                    Line("{");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line("}");
                    if (hasCrossKindExit)
                    {
                        Line($"{endLabel}:;");
                    }
                    break;
                }
                case TargetLanguage.JavaScript:
                {
                    string end = $"_smileForEnd{temp}";
                    Line($"const {end} = {upper};");
                    string delta = loop.IsDescending ? "-= 1n" : "+= 1n";
                    Line($"{(hasCrossKindExit ? label + ": " : string.Empty)}for ({counter} = {lower}; {counter} {comparison} {end}; {counter} {delta}) {{");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line("}");
                    break;
                }
                case TargetLanguage.Java:
                {
                    string end = $"_smileForEnd{temp}";
                    Line($"long {end} = {upper};");
                    Line($"{(hasCrossKindExit ? label + ": " : string.Empty)}for ({counter} = {lower}; {counter} {comparison} {end}; {counter}{step}) {{");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line("}");
                    break;
                }
                case TargetLanguage.Swift:
                {
                    string end = $"_smileForEnd{temp}";
                    Line($"let {end}: Int64 = {upper}");
                    string range = loop.IsDescending
                        ? $"stride(from: {lower}, through: {end}, by: -1)"
                        : $"stride(from: {lower}, through: {end}, by: 1)";
                    Line($"var {completionFlag} = true");
                    Line($"{(hasCrossKindExit ? label + ": " : string.Empty)}for _smileCounter in {range} {{");
                    _indent++;
                    Line($"{counter} = _smileCounter");
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line("}");
                    Line($"if {completionFlag} {{");
                    _indent++;
                    Line($"{counter} = {end} {(loop.IsDescending ? "-" : "+")} 1");
                    _indent--;
                    Line("}");
                    break;
                }
                case TargetLanguage.Python:
                {
                    if (hasCrossKindExit)
                    {
                        Line("try:");
                        _indent++;
                    }

                    string end = $"_smile_for_end_{temp}";
                    Line($"{end} = {upper}");
                    string stop = loop.IsDescending ? $"{end} - 1" : $"{end} + 1";
                    string range = loop.IsDescending ? $"range({lower}, {stop}, -1)" : $"range({lower}, {stop})";
                    Line($"for {counter} in {range}:");
                    _indent++;
                    if (loop.SourceItems.OfType<BoundStatement>().Any())
                    {
                        WriteItems(loop.SourceItems);
                    }
                    else
                    {
                        Line("pass");
                    }

                    _indent--;
                    Line("else:");
                    _indent++;
                    Line($"{counter} = {end} {(loop.IsDescending ? "-" : "+")} 1");
                    _indent--;
                    if (hasCrossKindExit)
                    {
                        _indent--;
                        Line($"except _SmileExitLoop{id}:");
                        _indent++;
                        Line("pass");
                        _indent--;
                    }

                    break;
                }
                case TargetLanguage.Cobol:
                    Line($"           PERFORM VARYING {counter} FROM {lower} BY {(loop.IsDescending ? "-1" : "1")} UNTIL {counter} {(loop.IsDescending ? "<" : ">")} {upper}");
                    WriteItems(loop.SourceItems);
                    Line(hasCrossKindExit ? "           END-PERFORM." : "           END-PERFORM");
                    if (hasCrossKindExit)
                    {
                        Line($"       {endLabel.Replace('_', '-').ToUpperInvariant()}.");
                        Line("           CONTINUE.");
                    }
                    break;
            }

            _loops.RemoveAt(_loops.Count - 1);
        }

        private void WriteDo(BoundDoStatement loop)
        {
            int id = ++_loopId;
            string label = $"smile_do_{id}";
            string endLabel = $"{label}_end";
            bool hasCrossKindExit = HasCrossKindExitTargetingLoop(loop.SourceItems, LoopKind.Do);
            _loops.Add(new LoopFrame(LoopKind.Do, id, label));

            switch (_language)
            {
                case TargetLanguage.CSharp:
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC:
                case TargetLanguage.Cpp:
                    Line("do");
                    Line("{");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line(loop.UntilCondition is null
                        ? "} while (true);"
                        : $"}} while (!({Expression(loop.UntilCondition)}));");
                    if (hasCrossKindExit)
                    {
                        Line($"{endLabel}:;");
                    }
                    break;
                case TargetLanguage.JavaScript:
                case TargetLanguage.Java:
                    Line($"{(hasCrossKindExit ? label + ": " : string.Empty)}do {{");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line(loop.UntilCondition is null
                        ? "} while (true);"
                        : $"}} while (!({Expression(loop.UntilCondition)}));");
                    break;
                case TargetLanguage.Swift:
                    Line($"{(hasCrossKindExit ? label + ": " : string.Empty)}repeat {{");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line(loop.UntilCondition is null
                        ? "} while true"
                        : $"}} while !({Expression(loop.UntilCondition)})");
                    break;
                case TargetLanguage.Python:
                    if (hasCrossKindExit)
                    {
                        Line("try:");
                        _indent++;
                    }

                    Line("while True:");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    if (loop.UntilCondition is not null)
                    {
                        Line($"if {Expression(loop.UntilCondition)}:");
                        _indent++;
                        Line("break");
                        _indent--;
                    }
                    else if (!loop.SourceItems.OfType<BoundStatement>().Any())
                    {
                        Line("pass");
                    }

                    _indent--;
                    if (hasCrossKindExit)
                    {
                        _indent--;
                        Line($"except _SmileExitLoop{id}:");
                        _indent++;
                        Line("pass");
                        _indent--;
                    }

                    break;
                case TargetLanguage.Cobol:
                    Line("           PERFORM WITH TEST AFTER UNTIL " +
                        (loop.UntilCondition is null ? "1 = 0" : Expression(loop.UntilCondition)));
                    WriteItems(loop.SourceItems);
                    Line(hasCrossKindExit ? "           END-PERFORM." : "           END-PERFORM");
                    if (hasCrossKindExit)
                    {
                        Line($"       {endLabel.Replace('_', '-').ToUpperInvariant()}.");
                        Line("           CONTINUE.");
                    }
                    break;
            }

            _loops.RemoveAt(_loops.Count - 1);
        }

        private void WriteExit(BoundExitStatement exit)
        {
            LoopKind kind = exit.Kind is BoundExitKind.For ? LoopKind.For : LoopKind.Do;
            LoopFrame? target = _loops.LastOrDefault(loop => loop.Kind == kind);
            if (target is null)
            {
                return;
            }

            bool targetsInnermostLoop = ReferenceEquals(_loops[^1], target);
            if (_language is TargetLanguage.JavaScript or TargetLanguage.Java or TargetLanguage.Swift)
            {
                if (_language is TargetLanguage.Swift && target.CompletionFlag is not null)
                {
                    Line($"{target.CompletionFlag} = false");
                }

                Line(targetsInnermostLoop
                    ? $"break{(_language is TargetLanguage.Swift ? string.Empty : ";")}"
                    : $"break {target.Label}{(_language is TargetLanguage.Swift ? string.Empty : ";")}");
            }
            else if (_language is TargetLanguage.Python)
            {
                Line(targetsInnermostLoop ? "break" : $"raise _SmileExitLoop{target.Id}()");
            }
            else if (_language is TargetLanguage.Cobol)
            {
                if (ReferenceEquals(_loops[^1], target))
                {
                    Line("           EXIT PERFORM");
                }
                else
                {
                    Line($"           GO TO {target.Label.Replace('_', '-').ToUpperInvariant()}-END");
                }
            }
            else
            {
                Line(targetsInnermostLoop ? "break;" : $"goto {target.Label}_end;");
            }
        }

        private void WriteEndProgram()
        {
            Line(_language switch
            {
                TargetLanguage.CSharp or TargetLanguage.Java => "return;",
                TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp => "return 0;",
                TargetLanguage.JavaScript => "process.exit(0);",
                TargetLanguage.Swift => "exit(0)",
                TargetLanguage.Python => "raise SystemExit(0)",
                TargetLanguage.Cobol => "           STOP RUN",
                _ => string.Empty
            });
        }

        private string Expression(BoundExpression expression)
        {
            return expression switch
            {
                BoundStringLiteralExpression text => StringLiteral(text.Value),
                BoundIntegerLiteralExpression number => IntegerLiteral(number.Value),
                BoundBooleanLiteralExpression boolean => BooleanLiteral(boolean.Value),
                BoundVariableExpression variable => Name(variable.Variable),
                BoundUnaryExpression unary => Unary(unary),
                BoundBinaryExpression binary => Binary(binary),
                _ => DefaultLiteral(expression.Type)
            };
        }

        private string Unary(BoundUnaryExpression unary)
        {
            string operand = Expression(unary.Operand);
            return unary.Operator.Kind switch
            {
                BoundUnaryOperatorKind.Identity => operand,
                BoundUnaryOperatorKind.Negation => $"(-{operand})",
                BoundUnaryOperatorKind.LogicalNegation => _language is TargetLanguage.Cobol
                    ? $"(NOT {operand})"
                    : _language is TargetLanguage.Python
                        ? $"(not {operand})"
                        : $"(!{operand})",
                _ => operand
            };
        }

        private string Binary(BoundBinaryExpression binary)
        {
            string left = Expression(binary.Left);
            string right = Expression(binary.Right);
            if (binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
            {
                return _language switch
                {
                    TargetLanguage.C => $"smile_text_concat({left}, {right})",
                    TargetLanguage.ObjectiveC => $"smile_text_concat({left}, {right})",
                    TargetLanguage.Cobol => $"FUNCTION CONCATENATE({left}, {right})",
                    _ => $"({left} + {right})"
                };
            }

            if (binary.Left.Type is SmileType.String &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                bool equal = binary.Operator.Kind is BoundBinaryOperatorKind.Equality;
                return _language switch
                {
                    TargetLanguage.C => $"(strcmp({left}, {right}) {(equal ? "==" : "!=")} 0)",
                    TargetLanguage.ObjectiveC => $"(strcmp({left}, {right}) {(equal ? "==" : "!=")} 0)",
                    TargetLanguage.Java => equal
                        ? $"{left}.equals({right})"
                        : $"(!{left}.equals({right}))",
                    TargetLanguage.Cobol => $"({left} {(equal ? "=" : "NOT =")} {right})",
                    _ => $"({left} {(equal ? "==" : "!=")} {right})"
                };
            }

            if (_language is TargetLanguage.Python && binary.Operator.Kind is BoundBinaryOperatorKind.Division)
            {
                return $"_smile_div({left}, {right})";
            }

            if (_language is TargetLanguage.Python && binary.Operator.Kind is BoundBinaryOperatorKind.Modulo)
            {
                return $"_smile_mod({left}, {right})";
            }

            string op = Operator(binary.Operator.Kind);
            return $"({left} {op} {right})";
        }

        private string Operator(BoundBinaryOperatorKind kind) => kind switch
        {
            BoundBinaryOperatorKind.Addition => "+",
            BoundBinaryOperatorKind.Subtraction => "-",
            BoundBinaryOperatorKind.Multiplication => "*",
            BoundBinaryOperatorKind.Division => "/",
            BoundBinaryOperatorKind.Modulo => _language is TargetLanguage.Cobol ? "REM" : "%",
            BoundBinaryOperatorKind.Equality => _language is TargetLanguage.Cobol ? "=" : "==",
            BoundBinaryOperatorKind.Inequality => _language is TargetLanguage.Cobol ? "NOT =" : "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => _language switch
            {
                TargetLanguage.Cobol => "AND",
                TargetLanguage.Python => "and",
                _ => "&&"
            },
            BoundBinaryOperatorKind.LogicalOr => _language switch
            {
                TargetLanguage.Cobol => "OR",
                TargetLanguage.Python => "or",
                _ => "||"
            },
            _ => string.Empty
        };

        private string DisplayExpression(BoundExpression expression)
        {
            string value = Expression(expression);
            if (expression.Type is not SmileType.Boolean)
            {
                return _language is TargetLanguage.JavaScript ? $"String({value})" : value;
            }

            return _language switch
            {
                TargetLanguage.Cobol => value,
                TargetLanguage.Python => $"(\"TRUE\" if {value} else \"FALSE\")",
                TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp => $"({value} ? \"TRUE\" : \"FALSE\")",
                _ => $"({value} ? \"TRUE\" : \"FALSE\")"
            };
        }

        private string TypeName(SmileType type) => _language switch
        {
            TargetLanguage.CSharp => type switch { SmileType.Integer => "long", SmileType.Boolean => "bool", _ => "string" },
            TargetLanguage.C => type switch { SmileType.Integer => "int64_t", SmileType.Boolean => "bool", _ => "const char *" },
            TargetLanguage.Java => type switch { SmileType.Integer => "long", SmileType.Boolean => "boolean", _ => "String" },
            TargetLanguage.ObjectiveC => type switch { SmileType.Integer => "int64_t", SmileType.Boolean => "bool", _ => "const char *" },
            TargetLanguage.Swift => type switch { SmileType.Integer => "Int64", SmileType.Boolean => "Bool", _ => "String" },
            TargetLanguage.Cpp => type switch { SmileType.Integer => "std::int64_t", SmileType.Boolean => "bool", _ => "std::string" },
            _ => string.Empty
        };

        private string DefaultLiteral(SmileType type) => type switch
        {
            SmileType.Integer => IntegerLiteral(0),
            SmileType.Boolean => BooleanLiteral(false),
            _ => StringLiteral(string.Empty)
        };

        private string Literal(SmileValue value) => value.Type switch
        {
            SmileType.Integer => IntegerLiteral(value.IntegerValue),
            SmileType.Boolean => BooleanLiteral(value.BooleanValue),
            SmileType.String => StringLiteral(value.StringValue),
            _ => StringLiteral(string.Empty)
        };

        private string IntegerLiteral(long value)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (_language is TargetLanguage.JavaScript)
            {
                return text + "n";
            }

            if (_language is TargetLanguage.Java && value is not (>= int.MinValue and <= int.MaxValue))
            {
                return value == long.MinValue ? "Long.MIN_VALUE" : text + "L";
            }

            if (_language is TargetLanguage.C && value == long.MinValue)
            {
                return "INT64_MIN";
            }

            if (_language is TargetLanguage.C && value is not (>= int.MinValue and <= int.MaxValue))
            {
                return value < 0 ? $"-INT64_C({-value})" : $"INT64_C({value})";
            }

            return text;
        }

        private string BooleanLiteral(bool value) => _language switch
        {
            TargetLanguage.ObjectiveC => value ? "true" : "false",
            TargetLanguage.Cobol => value ? "1 = 1" : "1 = 0",
            TargetLanguage.Python => value ? "True" : "False",
            _ => value ? "true" : "false"
        };

        private string StringLiteral(string value) => _language switch
        {
            TargetLanguage.CSharp => TargetEscapes.CSharpString(value),
            TargetLanguage.C => TargetEscapes.CString(value),
            TargetLanguage.JavaScript => TargetEscapes.JavaScriptString(value),
            TargetLanguage.Java => TargetEscapes.JavaString(value),
            TargetLanguage.Cobol => TargetEscapes.CobolString(value),
            TargetLanguage.ObjectiveC => TargetEscapes.CString(value),
            TargetLanguage.Swift => TargetEscapes.SwiftString(value),
            TargetLanguage.Python => TargetEscapes.PythonString(value),
            TargetLanguage.Cpp => TargetEscapes.CString(value),
            _ => TargetEscapes.CString(value)
        };

        private string CommentPrefix() => _language switch
        {
            TargetLanguage.Python => "#",
            TargetLanguage.Cobol => "       *>",
            _ => "//"
        };

        private string Name(VariableSymbol variable) => _identifiers.Get(variable);

        private BoundConstStatement? FindConstant(VariableSymbol variable) =>
            EnumerateStatements(_program.SourceItems)
                .OfType<BoundConstStatement>()
                .FirstOrDefault(statement => statement.Variable.Equals(variable));

        private string CobolLiteral(SmileValue value) => value.Type switch
        {
            SmileType.Integer => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            SmileType.Boolean => value.BooleanValue ? "1" : "0",
            _ => TargetEscapes.CobolString(value.StringValue)
        };

        private void WritePythonSuite(IReadOnlyList<BoundSourceItem> items)
        {
            _indent++;
            if (items.OfType<BoundStatement>().Any() || items.OfType<BoundFullLineComment>().Any())
            {
                WriteItems(items);
            }
            else
            {
                Line("pass");
            }

            _indent--;
        }

        private void WriteCTextConcatHelper()
        {
            Line("static char *smile_text_concat(const char *left, const char *right)");
            Line("{");
            _indent++;
            Line("size_t length = strlen(left) + strlen(right) + 1;");
            Line("char *result = malloc(length);");
            Line("if (result == NULL) { exit(1); }");
            Line("snprintf(result, length, \"%s%s\", left, right);");
            Line("return result;");
            _indent--;
            Line("}");
        }

        private void Line(string text = "")
        {
            if (text.Length > 0 && _language is not TargetLanguage.Cobol)
            {
                _builder.Append(' ', _indent * 4);
            }

            _builder.AppendLine(text);
        }

        private string Finish() => _builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);

        public static IEnumerable<BoundStatement> EnumerateStatements(IReadOnlyList<BoundSourceItem> items)
        {
            foreach (BoundSourceItem item in items)
            {
                if (item is not BoundStatement statement)
                {
                    continue;
                }

                yield return statement;
                switch (statement)
                {
                    case BoundIfStatement conditional:
                        foreach (BoundConditionalClause clause in conditional.Clauses)
                        {
                            foreach (BoundStatement nested in EnumerateStatements(clause.SourceItems))
                            {
                                yield return nested;
                            }
                        }

                        foreach (BoundStatement nested in EnumerateStatements(conditional.ElseSourceItems))
                        {
                            yield return nested;
                        }

                        break;
                    case BoundForStatement loop:
                        foreach (BoundStatement nested in EnumerateStatements(loop.SourceItems))
                        {
                            yield return nested;
                        }

                        break;
                    case BoundDoStatement loop:
                        foreach (BoundStatement nested in EnumerateStatements(loop.SourceItems))
                        {
                            yield return nested;
                        }

                        break;
                }

                if (statement is BoundExitStatement or BoundEndProgramStatement)
                {
                    yield break;
                }
            }
        }

        private static bool HasEndProgram(IReadOnlyList<BoundSourceItem> items) =>
            EnumerateStatements(items).Any(statement => statement is BoundEndProgramStatement);

        private static bool HasTextConcatenation(IReadOnlyList<BoundSourceItem> items) =>
            EnumerateExpressions(items).Any(expression => expression is BoundBinaryExpression
            {
                Operator.Kind: BoundBinaryOperatorKind.StringConcatenation
            });

        private static bool HasTextComparison(IReadOnlyList<BoundSourceItem> items) =>
            EnumerateExpressions(items).Any(expression => expression is BoundBinaryExpression binary &&
                binary.Left.Type is SmileType.String &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality);

        private static bool HasDivision(IReadOnlyList<BoundSourceItem> items) =>
            EnumerateExpressions(items).Any(expression => expression is BoundBinaryExpression binary &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Division);

        private static bool HasModulo(IReadOnlyList<BoundSourceItem> items) =>
            EnumerateExpressions(items).Any(expression => expression is BoundBinaryExpression binary &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Modulo);

        public static IEnumerable<BoundExpression> EnumerateExpressions(IReadOnlyList<BoundSourceItem> items)
        {
            foreach (BoundStatement statement in EnumerateStatements(items))
            {
                IEnumerable<BoundExpression> roots = statement switch
                {
                    BoundSetStatement set => new[] { set.Value },
                    BoundConstStatement constant => new[] { constant.Initializer },
                    BoundCorePrintStatement print => print.Values,
                    BoundIfStatement conditional => conditional.Clauses.Select(clause => clause.Condition),
                    BoundForStatement loop => new[] { loop.LowerBound, loop.UpperBound },
                    BoundDoStatement { UntilCondition: not null } loop => new[] { loop.UntilCondition },
                    _ => Array.Empty<BoundExpression>()
                };
                foreach (BoundExpression root in roots)
                {
                    foreach (BoundExpression expression in WalkExpression(root))
                    {
                        yield return expression;
                    }
                }
            }
        }

        private static IEnumerable<BoundExpression> WalkExpression(BoundExpression expression)
        {
            yield return expression;
            switch (expression)
            {
                case BoundUnaryExpression unary:
                    foreach (BoundExpression child in WalkExpression(unary.Operand)) yield return child;
                    break;
                case BoundBinaryExpression binary:
                    foreach (BoundExpression child in WalkExpression(binary.Left)) yield return child;
                    foreach (BoundExpression child in WalkExpression(binary.Right)) yield return child;
                    break;
            }
        }

        private static IReadOnlyList<int> GetPythonExitLoopIds(IReadOnlyList<BoundSourceItem> items)
        {
            var ids = new List<int>();
            int next = 0;
            void Visit(IReadOnlyList<BoundSourceItem> sourceItems)
            {
                foreach (BoundSourceItem item in sourceItems)
                {
                    if (item is not BoundStatement statement)
                    {
                        continue;
                    }

                    switch (statement)
                    {
                        case BoundForStatement loop:
                            next++;
                            if (HasCrossKindExitTargetingLoop(loop.SourceItems, LoopKind.For))
                            {
                                ids.Add(next);
                            }
                            Visit(loop.SourceItems);
                            break;
                        case BoundDoStatement loop:
                            next++;
                            if (HasCrossKindExitTargetingLoop(loop.SourceItems, LoopKind.Do))
                            {
                                ids.Add(next);
                            }
                            Visit(loop.SourceItems);
                            break;
                        case BoundIfStatement conditional:
                            foreach (BoundConditionalClause clause in conditional.Clauses) Visit(clause.SourceItems);
                            Visit(conditional.ElseSourceItems);
                            break;
                    }

                    if (statement is BoundExitStatement or BoundEndProgramStatement)
                    {
                        return;
                    }
                }
            }

            Visit(items);
            return ids;
        }

        private static bool HasCrossKindExitTargetingLoop(
            IReadOnlyList<BoundSourceItem> items,
            LoopKind targetKind,
            int nestedSameKindDepth = 0,
            int nestedLoopDepth = 0)
        {
            foreach (BoundSourceItem item in items)
            {
                if (item is not BoundStatement statement)
                {
                    continue;
                }

                if (statement is BoundExitStatement exit &&
                    nestedSameKindDepth == 0 &&
                    nestedLoopDepth > 0 &&
                    ((targetKind is LoopKind.For && exit.Kind is BoundExitKind.For) ||
                     (targetKind is LoopKind.Do && exit.Kind is BoundExitKind.Do)))
                {
                    return true;
                }

                if (statement is BoundExitStatement or BoundEndProgramStatement)
                {
                    return false;
                }

                switch (statement)
                {
                    case BoundIfStatement conditional:
                        if (conditional.Clauses.Any(clause =>
                                HasCrossKindExitTargetingLoop(
                                    clause.SourceItems,
                                    targetKind,
                                    nestedSameKindDepth,
                                    nestedLoopDepth)) ||
                            HasCrossKindExitTargetingLoop(
                                conditional.ElseSourceItems,
                                targetKind,
                                nestedSameKindDepth,
                                nestedLoopDepth))
                        {
                            return true;
                        }

                        break;
                    case BoundForStatement loop:
                        if (HasCrossKindExitTargetingLoop(
                                loop.SourceItems,
                                targetKind,
                                nestedSameKindDepth + (targetKind is LoopKind.For ? 1 : 0),
                                nestedLoopDepth + 1))
                        {
                            return true;
                        }

                        break;
                    case BoundDoStatement loop:
                        if (HasCrossKindExitTargetingLoop(
                                loop.SourceItems,
                                targetKind,
                                nestedSameKindDepth + (targetKind is LoopKind.Do ? 1 : 0),
                                nestedLoopDepth + 1))
                        {
                            return true;
                        }

                        break;
                }
            }

            return false;
        }
    }

    private sealed class MasmWriter
    {
        private readonly BoundProgram _program;
        private readonly TargetIdentifierMap _identifiers;
        private readonly StringBuilder _builder = new();
        private readonly Dictionary<string, string> _strings = new(StringComparer.Ordinal);
        private readonly List<LoopFrame> _loops = new();
        private readonly bool _hasTextConcatenation;
        private int _labelId;

        public MasmWriter(BoundProgram program)
        {
            _program = program;
            _identifiers = TargetIdentifierMap.Create(program, TargetLanguage.MasmX64);
            _hasTextConcatenation = EnumerateExpressions(program.SourceItems).Any(expression =>
                expression is BoundBinaryExpression
                {
                    Operator.Kind: BoundBinaryOperatorKind.StringConcatenation
                });
            InternString(string.Empty);
            InternString("%lld");
            InternString("%s");
            if (_hasTextConcatenation) InternString("%s%s");
            InternString("TRUE");
            InternString("FALSE");
            InternString("\n");
            foreach (BoundExpression expression in EnumerateExpressions(program.SourceItems))
            {
                if (expression is BoundStringLiteralExpression text) InternString(text.Value);
            }
        }

        public string Write()
        {
            Line("option casemap:none");
            Line("ExitProcess PROTO :DWORD");
            Line("printf PROTO :PTR BYTE, :VARARG");
            Line("strcmp PROTO :PTR BYTE, :PTR BYTE");
            if (_hasTextConcatenation) Line("sprintf PROTO :PTR BYTE, :PTR BYTE, :VARARG");
            Line("includelib kernel32.lib");
            Line("includelib msvcrt.lib");
            Line();
            Line(".const");
            foreach ((string value, string label) in _strings)
            {
                Line($"{label} BYTE {TargetEscapes.MasmByteInitializers(value)}, 0");
            }

            Line();
            Line(".data");
            foreach (VariableSymbol variable in _program.Variables)
            {
                string name = _identifiers.Get(variable);
                if (variable.IsConstant && FindConstant(variable) is BoundConstStatement constant)
                {
                    string value = constant.Value.Type switch
                    {
                        SmileType.Integer => constant.Value.IntegerValue.ToString(CultureInfo.InvariantCulture),
                        SmileType.Boolean => constant.Value.BooleanValue ? "1" : "0",
                        _ => $"OFFSET {InternString(constant.Value.StringValue)}"
                    };
                    Line($"{name} QWORD {value}");
                }
                else
                {
                    string value = variable.Type is SmileType.String ? $"OFFSET {InternString(string.Empty)}" : "0";
                    Line($"{name} QWORD {value}");
                }
            }

            if (_hasTextConcatenation) Line("smileTextBuffer BYTE 65536 DUP(0)");
            Line();
            Line(".code");
            Line("main PROC");
            Line("    sub rsp, 40");
            WriteItems(_program.SourceItems, 1);
            Line("smile_program_end:");
            Line("    xor ecx, ecx");
            Line("    call ExitProcess");
            Line("main ENDP");
            Line("END");
            return _builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        }

        private void WriteItems(IReadOnlyList<BoundSourceItem> items, int indent)
        {
            foreach (BoundSourceItem item in items)
            {
                switch (item)
                {
                    case BoundBlankLine:
                        Line();
                        break;
                    case BoundFullLineComment comment:
                        Line(new string(' ', indent * 4) + ";" + comment.Payload);
                        break;
                    case BoundSetStatement set:
                        EmitExpression(set.Value, indent);
                        Line($"{Pad(indent)}mov QWORD PTR [{_identifiers.Get(set.Variable)}], rax");
                        break;
                    case BoundCorePrintStatement print:
                        foreach (BoundExpression value in print.Values) EmitPrint(value, indent);
                        if (!print.SuppressNewLine) EmitPrint(new BoundStringLiteralExpression("\n"), indent);
                        break;
                    case BoundIfStatement conditional:
                        WriteIf(conditional, indent);
                        break;
                    case BoundForStatement loop:
                        WriteFor(loop, indent);
                        break;
                    case BoundDoStatement loop:
                        WriteDo(loop, indent);
                        break;
                    case BoundExitStatement exit:
                        LoopKind kind = exit.Kind is BoundExitKind.For ? LoopKind.For : LoopKind.Do;
                        LoopFrame? target = _loops.LastOrDefault(frame => frame.Kind == kind);
                        if (target is not null) Line($"{Pad(indent)}jmp {target.Label}_end");
                        break;
                    case BoundEndProgramStatement:
                        Line($"{Pad(indent)}jmp smile_program_end");
                        return;
                }

                if (item is BoundExitStatement)
                {
                    return;
                }
            }
        }

        private void WriteIf(BoundIfStatement conditional, int indent)
        {
            int id = ++_labelId;
            string end = $"smile_if_{id}_end";
            for (int index = 0; index < conditional.Clauses.Count; index++)
            {
                string next = $"smile_if_{id}_next_{index}";
                EmitExpression(conditional.Clauses[index].Condition, indent);
                Line($"{Pad(indent)}test rax, rax");
                Line($"{Pad(indent)}jz {next}");
                WriteItems(conditional.Clauses[index].SourceItems, indent);
                Line($"{Pad(indent)}jmp {end}");
                Line($"{next}:");
            }

            if (conditional.HasElseClause) WriteItems(conditional.ElseSourceItems, indent);
            Line($"{end}:");
        }

        private void WriteFor(BoundForStatement loop, int indent)
        {
            int id = ++_labelId;
            string label = $"smile_for_{id}";
            string endValue = $"smile_for_{id}_bound";
            Line(".data");
            Line($"{endValue} QWORD 0");
            Line(".code");
            EmitExpression(loop.LowerBound, indent);
            Line($"{Pad(indent)}mov QWORD PTR [{_identifiers.Get(loop.Counter)}], rax");
            EmitExpression(loop.UpperBound, indent);
            Line($"{Pad(indent)}mov QWORD PTR [{endValue}], rax");
            _loops.Add(new LoopFrame(LoopKind.For, id, label));
            Line($"{label}:");
            Line($"{Pad(indent)}mov rax, QWORD PTR [{_identifiers.Get(loop.Counter)}]");
            Line($"{Pad(indent)}cmp rax, QWORD PTR [{endValue}]");
            Line($"{Pad(indent)}{(loop.IsDescending ? "jl" : "jg")} {label}_end");
            WriteItems(loop.SourceItems, indent);
            Line($"{Pad(indent)}{(loop.IsDescending ? "dec" : "inc")} QWORD PTR [{_identifiers.Get(loop.Counter)}]");
            Line($"{Pad(indent)}jmp {label}");
            Line($"{label}_end:");
            _loops.RemoveAt(_loops.Count - 1);
        }

        private void WriteDo(BoundDoStatement loop, int indent)
        {
            int id = ++_labelId;
            string label = $"smile_do_{id}";
            _loops.Add(new LoopFrame(LoopKind.Do, id, label));
            Line($"{label}:");
            WriteItems(loop.SourceItems, indent);
            if (loop.UntilCondition is not null)
            {
                EmitExpression(loop.UntilCondition, indent);
                Line($"{Pad(indent)}test rax, rax");
                Line($"{Pad(indent)}jz {label}");
            }
            else
            {
                Line($"{Pad(indent)}jmp {label}");
            }

            Line($"{label}_end:");
            _loops.RemoveAt(_loops.Count - 1);
        }

        private void EmitPrint(BoundExpression value, int indent)
        {
            EmitExpression(value, indent);
            if (value.Type is SmileType.Boolean)
            {
                string falseLabel = $"smile_bool_{++_labelId}_false";
                string ready = $"smile_bool_{_labelId}_ready";
                Line($"{Pad(indent)}test rax, rax");
                Line($"{Pad(indent)}jz {falseLabel}");
                Line($"{Pad(indent)}lea rax, {InternString("TRUE")}");
                Line($"{Pad(indent)}jmp {ready}");
                Line($"{falseLabel}:");
                Line($"{Pad(indent)}lea rax, {InternString("FALSE")}");
                Line($"{ready}:");
            }

            if (value.Type is SmileType.Integer)
            {
                Line($"{Pad(indent)}mov rdx, rax");
                Line($"{Pad(indent)}lea rcx, {InternString("%lld")}");
            }
            else
            {
                Line($"{Pad(indent)}mov rdx, rax");
                Line($"{Pad(indent)}lea rcx, {InternString("%s")}");
            }

            Line($"{Pad(indent)}call printf");
        }

        private void EmitExpression(BoundExpression expression, int indent)
        {
            string pad = Pad(indent);
            switch (expression)
            {
                case BoundIntegerLiteralExpression number:
                    Line($"{pad}mov rax, {number.Value.ToString(CultureInfo.InvariantCulture)}");
                    return;
                case BoundBooleanLiteralExpression boolean:
                    Line($"{pad}mov rax, {(boolean.Value ? 1 : 0)}");
                    return;
                case BoundStringLiteralExpression text:
                    Line($"{pad}lea rax, {InternString(text.Value)}");
                    return;
                case BoundVariableExpression variable:
                    Line($"{pad}mov rax, QWORD PTR [{_identifiers.Get(variable.Variable)}]");
                    return;
                case BoundUnaryExpression unary:
                    EmitExpression(unary.Operand, indent);
                    if (unary.Operator.Kind is BoundUnaryOperatorKind.Negation) Line($"{pad}neg rax");
                    if (unary.Operator.Kind is BoundUnaryOperatorKind.LogicalNegation) Line($"{pad}xor rax, 1");
                    return;
                case BoundBinaryExpression binary:
                    if (binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr)
                    {
                        EmitShortCircuit(binary, indent);
                        return;
                    }

                    EmitExpression(binary.Left, indent);
                    Line($"{pad}push rax");
                    EmitExpression(binary.Right, indent);
                    Line($"{pad}mov r10, rax");
                    Line($"{pad}pop rax");
                    EmitBinaryOperation(binary, indent);
                    return;
                default:
                    Line($"{pad}xor eax, eax");
                    return;
            }
        }

        private void EmitShortCircuit(BoundBinaryExpression binary, int indent)
        {
            string pad = Pad(indent);
            string done = $"smile_logic_{++_labelId}_done";
            EmitExpression(binary.Left, indent);
            Line($"{pad}test rax, rax");
            Line($"{pad}{(binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd ? "jz" : "jnz")} {done}");
            EmitExpression(binary.Right, indent);
            Line($"{done}:");
        }

        private void EmitBinaryOperation(BoundBinaryExpression binary, int indent)
        {
            string pad = Pad(indent);
            if (binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
            {
                Line($"{pad}lea rcx, smileTextBuffer");
                Line($"{pad}lea rdx, {InternString("%s%s")}");
                Line($"{pad}mov r8, rax");
                Line($"{pad}mov r9, r10");
                Line($"{pad}call sprintf");
                Line($"{pad}lea rax, smileTextBuffer");
                return;
            }

            if (binary.Left.Type is SmileType.String &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                Line($"{pad}mov rcx, rax");
                Line($"{pad}mov rdx, r10");
                Line($"{pad}call strcmp");
                Line($"{pad}test eax, eax");
                Line($"{pad}{(binary.Operator.Kind is BoundBinaryOperatorKind.Equality ? "sete" : "setne")} al");
                Line($"{pad}movzx rax, al");
                return;
            }

            switch (binary.Operator.Kind)
            {
                case BoundBinaryOperatorKind.Addition: Line($"{pad}add rax, r10"); break;
                case BoundBinaryOperatorKind.Subtraction: Line($"{pad}sub rax, r10"); break;
                case BoundBinaryOperatorKind.Multiplication: Line($"{pad}imul rax, r10"); break;
                case BoundBinaryOperatorKind.Division:
                case BoundBinaryOperatorKind.Modulo:
                    Line($"{pad}cqo");
                    Line($"{pad}idiv r10");
                    if (binary.Operator.Kind is BoundBinaryOperatorKind.Modulo) Line($"{pad}mov rax, rdx");
                    break;
                default:
                    string set = binary.Operator.Kind switch
                    {
                        BoundBinaryOperatorKind.Equality => "sete", BoundBinaryOperatorKind.Inequality => "setne",
                        BoundBinaryOperatorKind.Less => "setl", BoundBinaryOperatorKind.LessOrEquals => "setle",
                        BoundBinaryOperatorKind.Greater => "setg", BoundBinaryOperatorKind.GreaterOrEquals => "setge",
                        _ => "sete"
                    };
                    Line($"{pad}cmp rax, r10");
                    Line($"{pad}{set} al");
                    Line($"{pad}movzx rax, al");
                    break;
            }
        }

        private string InternString(string value)
        {
            if (_strings.TryGetValue(value, out string? label)) return label;
            label = $"smileText{_strings.Count}";
            _strings.Add(value, label);
            return label;
        }

        private BoundConstStatement? FindConstant(VariableSymbol variable) =>
            EnumerateStatements(_program.SourceItems).OfType<BoundConstStatement>()
                .FirstOrDefault(statement => statement.Variable.Equals(variable));

        private static string Pad(int indent) => new(' ', indent * 4);

        private void Line(string text = "") => _builder.AppendLine(text);

        private static IEnumerable<BoundStatement> EnumerateStatements(IReadOnlyList<BoundSourceItem> items) =>
            StructuredWriter.EnumerateStatements(items);

        private static IEnumerable<BoundExpression> EnumerateExpressions(IReadOnlyList<BoundSourceItem> items) =>
            StructuredWriter.EnumerateExpressions(items);
    }
}
