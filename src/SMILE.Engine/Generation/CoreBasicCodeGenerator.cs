using System.Globalization;
using System.Text;

namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    public static GeneratedProgram Generate(BoundProgram program, TargetLanguage language)
    {
        string content = language switch
        {
            TargetLanguage.CSharp => new StructuredWriter(program, language).WriteCSharp(),
            TargetLanguage.C => new StructuredWriter(program, language).WriteC(),
            TargetLanguage.MasmX64 => new CoreBasicMasmWriter(program).Write(),
            TargetLanguage.JavaScript => new StructuredWriter(program, language).WriteJavaScript(),
            TargetLanguage.Java => new StructuredWriter(program, language).WriteJava(),
            TargetLanguage.Cobol => new CobolWriter(program).Write(),
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
        else if (language is TargetLanguage.Cobol)
        {
            string? support = CoreBasicCobolRuntimeSupport.Generate(program);
            if (support is not null)
            {
                files.Add(new GeneratedFile("SmileRuntime.c", support, IsPrimary: false));
            }
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

    private sealed partial class StructuredWriter
    {
        private readonly BoundProgram _program;
        private readonly TargetLanguage _language;
        private readonly TargetIdentifierMap _identifiers;
        private readonly CoreBasicProgramFeatureSet _features;
        private readonly HashSet<RoutineSymbol> _asyncJavaScriptRoutines;
        private readonly StringBuilder _builder = new();
        private readonly List<LoopFrame> _loops = new();
        private int _indent;
        private int _loopId;
        private int _forTempId;
        private int _selectTempId;
        private int _orderedTempId;

        public StructuredWriter(BoundProgram program, TargetLanguage language)
        {
            _program = program;
            _language = language;
            _identifiers = TargetIdentifierMap.Create(program, language);
            _features = CoreBasicProgramFeatureSet.Create(program);
            _asyncJavaScriptRoutines = FindAsyncJavaScriptRoutines(program);
        }

        public string WriteCSharp()
        {
            Line("using System;");
            Line();
            Line("internal static class Program");
            Line("{");
            _indent++;
            WriteGlobalDeclarations(fieldContext: true);
            Line("private static void Main()");
            Line("{");
            _indent++;
            WriteArrayInitializers(_program.Variables);
            WriteItems(_program.SourceItems);
            _indent--;
            Line("}");
            Line();
            WriteRoutines();
            WriteIndexHelper();
            WriteRuntimeHelpers();
            _indent--;
            Line("}");
            return Finish();
        }

        public string WriteJavaScript()
        {
            Line("\"use strict\";");
            WriteRuntimePreamble();
            Line();
            WriteGlobalDeclarations();
            bool wrappedMain = _features.HasGetKey || _features.HasWait;
            if (wrappedMain)
            {
                Line("async function main() {");
                _indent++;
                if (_features.HasGetKey)
                {
                    Line("try {");
                    _indent++;
                }
                WriteItems(_program.SourceItems);
                if (_features.HasGetKey)
                {
                    _indent--;
                    Line("} finally {");
                    _indent++;
                    Line("smileCleanup();");
                    _indent--;
                    Line("}");
                }
                _indent--;
                Line("}");
                Line();
            }
            WriteRoutines();
            WriteIndexHelper();
            WriteRuntimeHelpers();
            if (wrappedMain)
            {
                Line("main().catch(error => { if (!error?.smileEnd) { console.error(error); process.exitCode = 1; } });");
            }
            else
            {
                WriteItems(_program.SourceItems);
            }
            return Finish();
        }

        public string WriteJava()
        {
            WriteRuntimePreamble();
            if (_features.HasGetKey)
            {
                Line();
            }
            Line("public final class Program {");
            _indent++;
            WriteJavaRuntimeFields();
            WriteGlobalDeclarations(fieldContext: true);
            Line("public static void main(String[] args) {");
            _indent++;
            WriteArrayInitializers(_program.Variables);
            WriteItems(_program.SourceItems);
            _indent--;
            Line("}");
            Line();
            WriteRoutines();
            WriteIndexHelper();
            WriteRuntimeHelpers();
            _indent--;
            Line("}");
            return Finish();
        }

        public string WriteSwift()
        {
            if (!_features.HasConsoleRuntime &&
                (ProgramStatements().Any(statement => statement is BoundEndProgramStatement) || ProgramHasArrays()))
            {
                Line("import Foundation");
                Line();
            }

            WriteRuntimePreamble();
            if (_features.HasConsoleRuntime || _features.HasAbs)
            {
                Line();
            }

            WriteIndexHelper();
            WriteRuntimeHelpers();
            WriteGlobalDeclarations();
            WriteRoutines();
            WriteItems(_program.SourceItems);
            return Finish();
        }

        public string WritePython()
        {
            WriteRuntimePreamble();
            if (_features.HasConsoleRuntime)
            {
                Line();
            }
            bool hasModulo = ProgramExpressions().Any(expression => expression is BoundBinaryExpression
            {
                Operator.Kind: BoundBinaryOperatorKind.Modulo
            });
            if (ProgramExpressions().Any(expression => expression is BoundBinaryExpression
                {
                    Operator.Kind: BoundBinaryOperatorKind.Division
                }) || hasModulo)
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

            foreach (int id in GetPythonExitLoopIds(AllExecutableItemSets().SelectMany(items => items).ToArray()))
            {
                Line($"class _SmileExitLoop{id}(Exception):");
                _indent++;
                Line("pass");
                _indent--;
                Line();
            }

            WriteIndexHelper();
            WriteRuntimeHelpers();
            WriteGlobalDeclarations();
            WriteRoutines();
            WriteItems(_program.SourceItems);
            return Finish();
        }

        public string WriteCpp()
        {
            Line("#include <array>");
            Line("#include <cstdint>");
            Line("#include <cstdlib>");
            Line("#include <iostream>");
            Line("#include <string>");
            WriteRuntimePreamble();
            Line();
            WriteGlobalDeclarations();
            WriteRoutinePrototypes();
            WriteHelperPrototypes();
            Line("int main()");
            Line("{");
            _indent++;
            WriteItems(_program.SourceItems);
            Line("return 0;");
            _indent--;
            Line("}");
            Line();
            WriteRoutines();
            WriteIndexHelper();
            WriteRuntimeHelpers();
            return Finish();
        }

        public string WriteC()
        {
            Line("#include <inttypes.h>");
            Line("#include <stdbool.h>");
            Line("#include <stdio.h>");
            Line("#include <stdlib.h>");
            WriteRuntimePreamble();
            if (ProgramHasTextComparison() && !ProgramHasTextConcatenation())
            {
                Line("#include <string.h>");
            }
            if (ProgramHasTextConcatenation())
            {
                Line("#include <string.h>");
            }

            Line();
            WriteGlobalDeclarations();
            WriteRoutinePrototypes();
            WriteHelperPrototypes();
            Line("int main(void)");
            Line("{");
            _indent++;
            WriteArrayInitializers(_program.Variables);
            WriteItems(_program.SourceItems);
            Line("return 0;");
            _indent--;
            Line("}");
            Line();
            WriteRoutines();
            WriteIndexHelper();
            if (ProgramHasTextConcatenation())
            {
                WriteCTextConcatHelper();
                Line();
            }
            WriteRuntimeHelpers();
            return Finish();
        }

        public string WriteObjectiveC()
        {
            Line("#include <inttypes.h>");
            Line("#include <stdbool.h>");
            Line("#include <stdio.h>");
            Line("#include <stdlib.h>");
            WriteRuntimePreamble();
            if (ProgramHasTextComparison() && !ProgramHasTextConcatenation())
            {
                Line("#include <string.h>");
            }
            if (ProgramHasTextConcatenation())
            {
                Line("#include <string.h>");
            }
            Line();
            WriteGlobalDeclarations();
            WriteRoutinePrototypes();
            WriteHelperPrototypes();
            Line("int main(void)");
            Line("{");
            _indent++;
            WriteArrayInitializers(_program.Variables);
            WriteItems(_program.SourceItems);
            Line("return 0;");
            _indent--;
            Line("}");
            Line();
            WriteRoutines();
            WriteIndexHelper();
            if (ProgramHasTextConcatenation())
            {
                WriteCTextConcatHelper();
                Line();
            }
            WriteRuntimeHelpers();
            return Finish();
        }

        private void WriteGlobalDeclarations(bool fieldContext = false)
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

                    string declaration = ConstantDeclaration(variable, constant.Value);
                    if (fieldContext)
                    {
                        declaration = _language switch
                        {
                            TargetLanguage.CSharp => "private " + declaration,
                            TargetLanguage.Java => "private static " + declaration,
                            _ => declaration
                        };
                    }

                    Line(declaration);
                }
                else
                {
                    string declaration = VariableDeclaration(variable);
                    if (fieldContext)
                    {
                        declaration = _language switch
                        {
                            TargetLanguage.CSharp => "private static " + declaration,
                            TargetLanguage.Java => "private static " + declaration,
                            _ => declaration
                        };
                    }

                    Line(declaration);
                }
            }

            if (_program.Variables.Count > 0)
            {
                Line();
            }
        }

        private void WriteRoutinePrototypes()
        {
            if (_language is not (TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp))
            {
                return;
            }

            foreach (BoundRoutineDeclaration routine in _program.Routines)
            {
                Line($"static {RoutineReturnType(routine.Symbol)} {RoutineName(routine.Symbol)}({ParameterList(routine.Symbol)});");
            }

            if (_program.Routines.Count > 0)
            {
                Line();
            }
        }

        private void WriteRoutines()
        {
            foreach (BoundRoutineDeclaration routine in _program.Routines)
            {
                WriteRoutine(routine);
                Line();
            }
        }

        private void WriteRoutine(BoundRoutineDeclaration routine)
        {
            RoutineSymbol symbol = routine.Symbol;
            string name = RoutineName(symbol);
            string parameters = ParameterList(symbol);
            switch (_language)
            {
                case TargetLanguage.CSharp:
                    Line($"private static {RoutineReturnType(symbol)} {name}({parameters})");
                    Line("{");
                    break;
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC:
                case TargetLanguage.Cpp:
                    Line($"static {RoutineReturnType(symbol)} {name}({parameters})");
                    Line("{");
                    break;
                case TargetLanguage.JavaScript:
                    Line($"{(_asyncJavaScriptRoutines.Contains(symbol) ? "async " : string.Empty)}function {name}({parameters}) {{");
                    break;
                case TargetLanguage.Java:
                    Line($"private static {RoutineReturnType(symbol)} {name}({parameters}) {{");
                    break;
                case TargetLanguage.Swift:
                    string arrow = symbol.IsFunction ? $" -> {RoutineReturnType(symbol)}" : string.Empty;
                    Line($"func {name}({parameters}){arrow} {{");
                    break;
                case TargetLanguage.Python:
                    Line($"def {name}({parameters}):");
                    _indent++;
                    string[] globals = AssignedGlobals(routine.SourceItems)
                        .Select(Name)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (globals.Length > 0)
                    {
                        Line("global " + string.Join(", ", globals));
                    }

                    WriteLocalDeclarations(routine);
                    if (!routine.SourceItems.OfType<BoundStatement>().Any() &&
                        !routine.SourceItems.OfType<BoundFullLineComment>().Any())
                    {
                        Line("pass");
                    }
                    else
                    {
                        WriteItems(routine.SourceItems);
                    }

                    _indent--;
                    return;
                default:
                    return;
            }

            _indent++;
            if (_language is TargetLanguage.Swift)
            {
                for (int index = 0; index < symbol.Parameters.Count; index++)
                {
                    VariableSymbol parameter = symbol.Parameters[index];
                    string binding = IsAssigned(parameter, routine.SourceItems) ? "var" : "let";
                    Line($"{binding} {Name(parameter)}: {TypeName(parameter.Type)} = _smileParameter{index + 1}");
                }

                if (symbol.Parameters.Count > 0)
                {
                    Line();
                }
            }

            WriteLocalDeclarations(routine);
            WriteItems(routine.SourceItems);
            _indent--;
            Line("}");
        }

        private void WriteLocalDeclarations(BoundRoutineDeclaration routine)
        {
            VariableSymbol[] locals = routine.Locals.Where(variable => !variable.IsParameter).ToArray();
            foreach (VariableSymbol local in locals)
            {
                Line(VariableDeclaration(local));
            }

            WriteArrayInitializers(locals);
            if (locals.Length > 0)
            {
                Line();
            }
        }

        private void WriteArrayInitializers(IEnumerable<VariableSymbol> variables)
        {
            foreach (VariableSymbol variable in variables.Where(item => item.IsArray && item.Type is SmileType.String))
            {
                string name = Name(variable);
                switch (_language)
                {
                    case TargetLanguage.CSharp:
                        if (variable.ArrayRank == 1)
                        {
                            Line($"Array.Fill({name}, string.Empty);");
                        }
                        else
                        {
                            string x = $"_smile_x_{Math.Abs(variable.DeclarationSpan.Start)}";
                            string y = $"_smile_y_{Math.Abs(variable.DeclarationSpan.Start)}";
                            Line($"for (int {x} = 0; {x} < {variable.ArrayLength}; {x}++)");
                            Line($"for (int {y} = 0; {y} < {variable.ArraySecondLength}; {y}++)");
                            _indent++;
                            Line($"{name}[{x}, {y}] = string.Empty;");
                            _indent--;
                        }
                        break;
                    case TargetLanguage.Java:
                        if (variable.ArrayRank == 1)
                        {
                            Line($"java.util.Arrays.fill({name}, \"\");");
                        }
                        else
                        {
                            string x = $"_smileX{Math.Abs(variable.DeclarationSpan.Start)}";
                            Line($"for (int {x} = 0; {x} < {variable.ArrayLength}; {x}++)");
                            _indent++;
                            Line($"java.util.Arrays.fill({name}[{x}], \"\");");
                            _indent--;
                        }
                        break;
                    case TargetLanguage.C:
                    case TargetLanguage.ObjectiveC:
                        Line($"for (size_t _smile_index_{Math.Abs(variable.DeclarationSpan.Start)} = 0; _smile_index_{Math.Abs(variable.DeclarationSpan.Start)} < {variable.TotalElementCount}; _smile_index_{Math.Abs(variable.DeclarationSpan.Start)}++)");
                        Line("{");
                        _indent++;
                        if (variable.ArrayRank == 1)
                        {
                            Line($"{name}[_smile_index_{Math.Abs(variable.DeclarationSpan.Start)}] = \"\";");
                        }
                        else
                        {
                            Line($"((const char **){name})[_smile_index_{Math.Abs(variable.DeclarationSpan.Start)}] = \"\";");
                        }
                        _indent--;
                        Line("}");
                        break;
                }
            }
        }

        private void WriteIndexHelper()
        {
            if (!ProgramHasArrays())
            {
                return;
            }

            switch (_language)
            {
                case TargetLanguage.CSharp:
                    Line("private static int SmileIndex(long index, int length, string name)");
                    Line("{");
                    _indent++;
                    Line("if (index < 0 || index >= length)");
                    Line("{");
                    _indent++;
                    Line("throw new IndexOutOfRangeException($\"SMILE Runtime Error SMILER1210: Array index {index} is outside the bounds of '{name}'.\");");
                    _indent--;
                    Line("}");
                    Line("return (int)index;");
                    _indent--;
                    Line("}");
                    Line();
                    break;
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC:
                    Line("static size_t smile_index(int64_t index, size_t length, const char *name)");
                    Line("{");
                    _indent++;
                    Line("if (index < 0 || (uint64_t)index >= length)");
                    Line("{");
                    _indent++;
                    Line("fprintf(stderr, \"SMILE Runtime Error SMILER1210: Array index %\" PRId64 \" is outside the bounds of '%s'.\\n\", index, name);");
                    Line("exit(1);");
                    _indent--;
                    Line("}");
                    Line("return (size_t)index;");
                    _indent--;
                    Line("}");
                    Line();
                    break;
                case TargetLanguage.JavaScript:
                    Line("function smileIndex(index, length, name) {");
                    _indent++;
                    Line("if (index < 0n || index >= BigInt(length)) {");
                    _indent++;
                    Line("throw new RangeError(`SMILE Runtime Error SMILER1210: Array index ${index} is outside the bounds of '${name}'.`);");
                    _indent--;
                    Line("}");
                    Line("return Number(index);");
                    _indent--;
                    Line("}");
                    Line();
                    break;
                case TargetLanguage.Java:
                    Line("private static int smileIndex(long index, int length, String name) {");
                    _indent++;
                    Line("if (index < 0 || index >= length) {");
                    _indent++;
                    Line("throw new IndexOutOfBoundsException(\"SMILE Runtime Error SMILER1210: Array index \" + index + \" is outside the bounds of '\" + name + \"'.\");");
                    _indent--;
                    Line("}");
                    Line("return (int)index;");
                    _indent--;
                    Line("}");
                    Line();
                    break;
                case TargetLanguage.Swift:
                    Line("func smileIndex(_ index: Int64, _ length: Int, _ name: String) -> Int {");
                    _indent++;
                    Line("if index < 0 || index >= Int64(length) {");
                    _indent++;
                    Line("fatalError(\"SMILE Runtime Error SMILER1210: Array index \\(index) is outside the bounds of '\\(name)'.\")");
                    _indent--;
                    Line("}");
                    Line("return Int(index)");
                    _indent--;
                    Line("}");
                    Line();
                    break;
                case TargetLanguage.Python:
                    Line("def smile_index(index, length, name):");
                    _indent++;
                    Line("if index < 0 or index >= length:");
                    _indent++;
                    Line("raise IndexError(f\"SMILE Runtime Error SMILER1210: Array index {index} is outside the bounds of '{name}'.\")");
                    _indent--;
                    Line("return index");
                    _indent--;
                    Line();
                    break;
                case TargetLanguage.Cpp:
                    Line("static std::size_t smile_index(std::int64_t index, std::size_t length, const std::string& name)");
                    Line("{");
                    _indent++;
                    Line("if (index < 0 || static_cast<std::size_t>(index) >= length)");
                    Line("{");
                    _indent++;
                    Line("std::cerr << \"SMILE Runtime Error SMILER1210: Array index \" << index << \" is outside the bounds of '\" << name << \"'.\\n\";");
                    Line("std::exit(1);");
                    _indent--;
                    Line("}");
                    Line("return static_cast<std::size_t>(index);");
                    _indent--;
                    Line("}");
                    Line();
                    break;
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
            if (variable.IsArray)
            {
                string dimensions = variable.ArrayRank == 2
                    ? $"{variable.ArrayLength}][{variable.ArraySecondLength}"
                    : variable.ArrayLength.ToString(CultureInfo.InvariantCulture);
                return _language switch
                {
                    TargetLanguage.CSharp when variable.ArrayRank == 2 => $"{TypeName(variable.Type)}[,] {name} = new {TypeName(variable.Type)}[{variable.ArrayLength}, {variable.ArraySecondLength}];",
                    TargetLanguage.CSharp => $"{TypeName(variable.Type)}[] {name} = new {TypeName(variable.Type)}[{variable.ArrayLength}];",
                    TargetLanguage.C => $"{TypeName(variable.Type)} {name}[{dimensions}] = {{0}};",
                    TargetLanguage.JavaScript when variable.ArrayRank == 2 => $"let {name} = Array.from({{ length: {variable.ArrayLength} }}, () => Array({variable.ArraySecondLength}).fill({value}));",
                    TargetLanguage.JavaScript => $"let {name} = Array({variable.ArrayLength}).fill({value});",
                    TargetLanguage.Java when variable.ArrayRank == 2 => $"{TypeName(variable.Type)}[][] {name} = new {TypeName(variable.Type)}[{variable.ArrayLength}][{variable.ArraySecondLength}];",
                    TargetLanguage.Java => $"{TypeName(variable.Type)}[] {name} = new {TypeName(variable.Type)}[{variable.ArrayLength}];",
                    TargetLanguage.ObjectiveC => $"{TypeName(variable.Type)} {name}[{dimensions}] = {{0}};",
                    TargetLanguage.Swift when variable.ArrayRank == 2 => $"var {name}: [[{TypeName(variable.Type)}]] = Array(repeating: Array(repeating: {value}, count: {variable.ArraySecondLength}), count: {variable.ArrayLength})",
                    TargetLanguage.Swift => $"var {name}: [{TypeName(variable.Type)}] = Array(repeating: {value}, count: {variable.ArrayLength})",
                    TargetLanguage.Python when variable.ArrayRank == 2 => $"{name} = [[{value} for _ in range({variable.ArraySecondLength})] for _ in range({variable.ArrayLength})]",
                    TargetLanguage.Python => $"{name} = [{value}] * {variable.ArrayLength}",
                    TargetLanguage.Cpp when variable.ArrayRank == 2 => $"std::array<std::array<{TypeName(variable.Type)}, {variable.ArraySecondLength}>, {variable.ArrayLength}> {name}{{}};",
                    TargetLanguage.Cpp => $"std::array<{TypeName(variable.Type)}, {variable.ArrayLength}> {name}{{}};",
                    _ => string.Empty
                };
            }

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

        private string RoutineReturnType(RoutineSymbol routine) => routine.IsFunction
            ? TypeName(routine.ReturnType ?? SmileType.Error)
            : _language switch
            {
                TargetLanguage.CSharp or TargetLanguage.C or TargetLanguage.Java or
                    TargetLanguage.ObjectiveC or TargetLanguage.Cpp => "void",
                _ => string.Empty
            };

        private string ParameterList(RoutineSymbol routine) => string.Join(", ", routine.Parameters.Select((parameter, index) =>
        {
            string name = Name(parameter);
            return _language switch
            {
                TargetLanguage.CSharp or TargetLanguage.C or TargetLanguage.Java or
                    TargetLanguage.ObjectiveC or TargetLanguage.Cpp => $"{TypeName(parameter.Type)} {name}",
                TargetLanguage.Swift => $"_ _smileParameter{index + 1}: {TypeName(parameter.Type)}",
                _ => name
            };
        }));

        private string RoutineName(RoutineSymbol routine) => _identifiers.Get(routine);

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
            if (TryWriteTextGameStatement(statement))
            {
                return;
            }

            switch (statement)
            {
                case BoundDimStatement or BoundConstStatement:
                    return;
                case BoundSetStatement assignment:
                    WriteAssignment(assignment);
                    return;
                case BoundArraySetStatement assignment:
                    WriteArrayAssignment(assignment);
                    return;
                case BoundCallStatement call:
                    WriteCall(call);
                    return;
                case BoundReturnStatement returnStatement:
                    WriteReturn(returnStatement);
                    return;
                case BoundSelectStatement select:
                    WriteSelect(select);
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
            string expression = PreparedExpression(assignment.Value);
            Line(_language is TargetLanguage.Swift or TargetLanguage.Python
                ? $"{name} = {expression}"
                : $"{name} = {expression};");
        }

        private void WriteArrayAssignment(BoundArraySetStatement assignment)
        {
            var rawIndices = new List<string>(assignment.Indices.Count);
            for (int position = 0; position < assignment.Indices.Count; position++)
            {
                string index = PreparedExpression(assignment.Indices[position]);
                string rawIndex = $"_smileRawIndex{++_orderedTempId}";
                WriteNumberTemporary(rawIndex, index);
                rawIndices.Add(rawIndex);
            }

            var checkedIndices = new List<string>(assignment.Indices.Count);
            for (int position = 0; position < assignment.Indices.Count; position++)
            {
                string checkedIndex = $"_smileIndex{++_orderedTempId}";
                WriteIndexTemporary(checkedIndex, CheckedArrayIndex(assignment.Array, rawIndices[position], position));
                checkedIndices.Add(checkedIndex);
            }

            string value = PreparedExpression(assignment.Value);
            string target = ArrayTarget(assignment.Array, checkedIndices);
            Line(_language is TargetLanguage.Swift or TargetLanguage.Python
                ? $"{target} = {value}"
                : $"{target} = {value};");
        }

        private void WriteCall(BoundCallStatement call)
        {
            string awaitPrefix = _language is TargetLanguage.JavaScript && _asyncJavaScriptRoutines.Contains(call.Routine)
                ? "await "
                : string.Empty;
            string invocation = $"{awaitPrefix}{RoutineName(call.Routine)}({string.Join(", ", call.Arguments.Select(PreparedExpression))})";
            Line(_language is TargetLanguage.Swift or TargetLanguage.Python
                ? invocation
                : invocation + ";");
        }

        private void WriteReturn(BoundReturnStatement returnStatement)
        {
            string suffix = returnStatement.Value is null ? string.Empty : " " + PreparedExpression(returnStatement.Value);
            Line(_language is TargetLanguage.Swift or TargetLanguage.Python
                ? "return" + suffix
                : "return" + suffix + ";");
        }

        private void WriteSelect(BoundSelectStatement select)
        {
            int id = ++_selectTempId;
            string temporary = $"_smileSelect{id}";
            string selector = PreparedExpression(select.Selector);
            string declaration = _language switch
            {
                TargetLanguage.CSharp or TargetLanguage.C or TargetLanguage.Java or
                    TargetLanguage.ObjectiveC or TargetLanguage.Cpp => $"{TypeName(select.Selector.Type)} {temporary} = {selector};",
                TargetLanguage.JavaScript => $"const {temporary} = {selector};",
                TargetLanguage.Swift => $"let {temporary}: {TypeName(select.Selector.Type)} = {selector}",
                TargetLanguage.Python => $"{temporary} = {selector}",
                _ => string.Empty
            };
            Line(declaration);

            bool wroteCondition = false;
            foreach (BoundSelectCaseClause clause in select.Cases)
            {
                if (clause.IsElse)
                {
                    if (_language is TargetLanguage.Python)
                    {
                        Line("else:");
                        WritePythonSuite(clause.SourceItems);
                    }
                    else
                    {
                        Line("else");
                        Line("{");
                        _indent++;
                        WriteItems(clause.SourceItems);
                        _indent--;
                        Line("}");
                    }

                    continue;
                }

                string condition = SelectCondition(temporary, clause.Value!.Value);
                if (_language is TargetLanguage.Python)
                {
                    Line($"{(wroteCondition ? "elif" : "if")} {condition}:");
                    WritePythonSuite(clause.SourceItems);
                }
                else if (_language is TargetLanguage.Swift)
                {
                    Line($"{(wroteCondition ? "else if" : "if")} {condition} {{");
                    _indent++;
                    WriteItems(clause.SourceItems);
                    _indent--;
                    Line("}");
                }
                else
                {
                    Line($"{(wroteCondition ? "else if" : "if")} ({condition})");
                    Line("{");
                    _indent++;
                    WriteItems(clause.SourceItems);
                    _indent--;
                    Line("}");
                }

                wroteCondition = true;
            }
        }

        private string SelectCondition(string selector, SmileValue value)
        {
            string literal = Literal(value);
            if (value.Type is SmileType.String)
            {
                return _language switch
                {
                    TargetLanguage.C or TargetLanguage.ObjectiveC => $"strcmp({selector}, {literal}) == 0",
                    TargetLanguage.Java => $"{selector}.equals({literal})",
                    _ => $"{selector} == {literal}"
                };
            }

            return $"{selector} == {literal}";
        }

        private void WritePrint(BoundCorePrintStatement print)
        {
            foreach (BoundExpression value in print.Values)
            {
                string display = DisplayExpression(value, PreparedExpression(value));
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
            if (UsesOrderedCExpressions ||
                conditional.Clauses.Any(clause => ContainsArrayAccess(clause.Condition)))
            {
                WriteOrderedCIf(conditional, 0);
                return;
            }

            for (int index = 0; index < conditional.Clauses.Count; index++)
            {
                BoundConditionalClause clause = conditional.Clauses[index];
                string keyword = index == 0 ? "if" : _language is TargetLanguage.Python ? "elif" : "else if";
                if (_language is TargetLanguage.Python)
                {
                    Line($"{keyword} {PreparedExpression(clause.Condition)}:");
                    WritePythonSuite(clause.SourceItems);
                }
                else if (_language is TargetLanguage.Swift)
                {
                    Line($"{keyword} {PreparedExpression(clause.Condition)} {{");
                    _indent++;
                    WriteItems(clause.SourceItems);
                    _indent--;
                    Line("}");
                }
                else
                {
                    Line($"{keyword} ({PreparedExpression(clause.Condition)})");
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
            string lower = PreparedExpression(loop.LowerBound);
            string upper = PreparedExpression(loop.UpperBound);
            string comparison = loop.IsDescending ? ">=" : "<=";
            string step = loop.IsDescending ? "--" : "++";
            bool hasCrossKindExit = HasCrossKindExitTargetingLoop(loop.SourceItems, LoopKind.For);
            string? completionFlag = _language is TargetLanguage.Swift && HasExitTargetingLoop(loop.SourceItems, LoopKind.For)
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
                    if (completionFlag is not null)
                    {
                        Line($"var {completionFlag} = true");
                    }
                    Line($"{(hasCrossKindExit ? label + ": " : string.Empty)}for _smileCounter in {range} {{");
                    _indent++;
                    Line($"{counter} = _smileCounter");
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line("}");
                    if (completionFlag is null)
                    {
                        Line($"{counter} = {end} {(loop.IsDescending ? "-" : "+")} 1");
                    }
                    else
                    {
                        Line($"if {completionFlag} {{");
                        _indent++;
                        Line($"{counter} = {end} {(loop.IsDescending ? "-" : "+")} 1");
                        _indent--;
                        Line("}");
                    }
                    Line($"_ = {counter}");
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

            if (UsesOrderedCExpressions)
            {
                Line("do");
                Line("{");
                _indent++;
                WriteItems(loop.SourceItems);
                if (loop.UntilCondition is not null)
                {
                    string condition = PreparedExpression(loop.UntilCondition);
                    Line($"if ({condition})");
                    Line("{");
                    _indent++;
                    Line("break;");
                    _indent--;
                    Line("}");
                }

                _indent--;
                Line("} while (true);");
                if (hasCrossKindExit)
                {
                    Line($"{endLabel}:;");
                }

                _loops.RemoveAt(_loops.Count - 1);
                return;
            }

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
                        : $"}} while (!({PreparedExpression(loop.UntilCondition)}));");
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
                        : $"}} while (!({PreparedExpression(loop.UntilCondition)}));");
                    break;
                case TargetLanguage.Swift:
                    Line($"{(hasCrossKindExit ? label + ": " : string.Empty)}repeat {{");
                    _indent++;
                    WriteItems(loop.SourceItems);
                    _indent--;
                    Line(loop.UntilCondition is null
                        ? "} while true"
                        : $"}} while !({PreparedExpression(loop.UntilCondition)})");
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
                        Line($"if {PreparedExpression(loop.UntilCondition)}:");
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
            else
            {
                Line(targetsInnermostLoop ? "break;" : $"goto {target.Label}_end;");
            }
        }

        private void WriteEndProgram()
        {
            Line(_language switch
            {
                TargetLanguage.CSharp => "Environment.Exit(0);",
                TargetLanguage.Java => "System.exit(0);",
                TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp => "exit(0);",
                TargetLanguage.JavaScript when _features.HasGetKey || _features.HasWait => "throw { smileEnd: true };",
                TargetLanguage.JavaScript => "process.exit(0);",
                TargetLanguage.Swift => "exit(0)",
                TargetLanguage.Python => "raise SystemExit(0)",
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
                BoundArrayExpression array => ArrayElement(array.Array, array.Indices),
                BoundCallExpression call => $"{(_language is TargetLanguage.JavaScript && _asyncJavaScriptRoutines.Contains(call.Routine) ? "await " : string.Empty)}{RoutineName(call.Routine)}({string.Join(", ", call.Arguments.Select(Expression))})",
                BoundIntrinsicExpression intrinsic => Intrinsic(intrinsic),
                BoundUnaryExpression unary => Unary(unary),
                BoundBinaryExpression binary => Binary(binary),
                _ => DefaultLiteral(expression.Type)
            };
        }

        private bool UsesOrderedCExpressions =>
            _language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp;

        private string PreparedExpression(BoundExpression expression) =>
            UsesOrderedCExpressions || ContainsArrayAccess(expression)
                ? LowerOrderedCExpression(expression)
                : Expression(expression);

        private string LowerOrderedCExpression(BoundExpression expression)
        {
            switch (expression)
            {
                case BoundStringLiteralExpression or BoundIntegerLiteralExpression or BoundBooleanLiteralExpression or BoundVariableExpression:
                    return Expression(expression);
                case BoundArrayExpression array:
                {
                    var rawIndices = new List<string>(array.Indices.Count);
                    for (int position = 0; position < array.Indices.Count; position++)
                    {
                        string index = LowerOrderedCExpression(array.Indices[position]);
                        string rawIndex = $"_smileRawIndex{++_orderedTempId}";
                        WriteNumberTemporary(rawIndex, index);
                        rawIndices.Add(rawIndex);
                    }

                    var checkedIndices = new List<string>(array.Indices.Count);
                    for (int position = 0; position < array.Indices.Count; position++)
                    {
                        string checkedIndex = $"_smileIndex{++_orderedTempId}";
                        WriteIndexTemporary(checkedIndex, CheckedArrayIndex(array.Array, rawIndices[position], position));
                        checkedIndices.Add(checkedIndex);
                    }

                    return NewOrderedValue(array.Type, ArrayTarget(array.Array, checkedIndices));
                }
                case BoundIntrinsicExpression intrinsic:
                {
                    string[] arguments = intrinsic.Arguments.Select(LowerOrderedCExpression).ToArray();
                    return NewOrderedValue(intrinsic.Type, Intrinsic(intrinsic.Kind, arguments));
                }
                case BoundCallExpression call:
                {
                    string[] arguments = call.Arguments.Select(LowerOrderedCExpression).ToArray();
                    string awaitPrefix = _language is TargetLanguage.JavaScript && _asyncJavaScriptRoutines.Contains(call.Routine)
                        ? "await "
                        : string.Empty;
                    return NewOrderedValue(call.Type, $"{awaitPrefix}{RoutineName(call.Routine)}({string.Join(", ", arguments)})");
                }
                case BoundUnaryExpression unary:
                {
                    string operand = LowerOrderedCExpression(unary.Operand);
                    string rendered = unary.Operator.Kind switch
                    {
                        BoundUnaryOperatorKind.Identity => operand,
                        BoundUnaryOperatorKind.Negation => $"(-{operand})",
                        BoundUnaryOperatorKind.LogicalNegation => _language is TargetLanguage.Python
                            ? $"(not {operand})"
                            : $"(!{operand})",
                        _ => operand
                    };
                    return NewOrderedValue(unary.Type, rendered);
                }
                case BoundBinaryExpression binary when binary.Operator.Kind is
                    BoundBinaryOperatorKind.LogicalAnd or BoundBinaryOperatorKind.LogicalOr:
                {
                    string left = LowerOrderedCExpression(binary.Left);
                    string result = NewOrderedValue(SmileType.Boolean, left, mutable: true);
                    string condition = binary.Operator.Kind is BoundBinaryOperatorKind.LogicalAnd
                        ? result
                        : _language is TargetLanguage.Python ? $"not {result}" : $"!{result}";
                    if (_language is TargetLanguage.Python)
                    {
                        Line($"if {condition}:");
                    }
                    else if (_language is TargetLanguage.Swift)
                    {
                        Line($"if {condition} {{");
                    }
                    else
                    {
                        Line($"if ({condition})");
                        Line("{");
                    }
                    _indent++;
                    string right = LowerOrderedCExpression(binary.Right);
                    WriteSimpleAssignment(result, right);
                    _indent--;
                    if (_language is not TargetLanguage.Python)
                    {
                        Line("}");
                    }
                    return result;
                }
                case BoundBinaryExpression binary:
                {
                    string left = LowerOrderedCExpression(binary.Left);
                    string right = LowerOrderedCExpression(binary.Right);
                    return NewOrderedValue(binary.Type, RenderBinary(binary, left, right));
                }
                default:
                    return Expression(expression);
            }
        }

        private string NewOrderedValue(SmileType type, string expression, bool mutable = false)
        {
            string name = $"_smileValue{++_orderedTempId}";
            Line(_language switch
            {
                TargetLanguage.JavaScript => $"{(mutable ? "let" : "const")} {name} = {expression};",
                TargetLanguage.Swift => $"{(mutable ? "var" : "let")} {name}: {TypeName(type)} = {expression}",
                TargetLanguage.Python => $"{name} = {expression}",
                _ => $"{TypeName(type)} {name} = {expression};"
            });
            return name;
        }

        private static bool ContainsArrayAccess(BoundExpression expression) => expression switch
        {
            BoundArrayExpression => true,
            BoundCallExpression call => call.Arguments.Any(ContainsArrayAccess),
            BoundIntrinsicExpression intrinsic => intrinsic.Arguments.Any(ContainsArrayAccess),
            BoundUnaryExpression unary => ContainsArrayAccess(unary.Operand),
            BoundBinaryExpression binary => ContainsArrayAccess(binary.Left) || ContainsArrayAccess(binary.Right),
            _ => false
        };

        private string RenderBinary(BoundBinaryExpression binary, string left, string right)
        {
            if (binary.Operator.Kind is BoundBinaryOperatorKind.StringConcatenation)
            {
                return _language switch
                {
                    TargetLanguage.C or TargetLanguage.ObjectiveC => $"smile_text_concat({left}, {right})",
                    _ => $"({left} + {right})"
                };
            }

            if (binary.Left.Type is SmileType.String &&
                binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality)
            {
                bool equal = binary.Operator.Kind is BoundBinaryOperatorKind.Equality;
                return _language switch
                {
                    TargetLanguage.C or TargetLanguage.ObjectiveC => $"(strcmp({left}, {right}) {(equal ? "==" : "!=")} 0)",
                    TargetLanguage.Java => equal ? $"{left}.equals({right})" : $"(!{left}.equals({right}))",
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
            return $"({left} {Operator(binary.Operator.Kind)} {right})";
        }

        private string CheckedArrayIndex(VariableSymbol array, string index, int dimension) => _language switch
        {
            TargetLanguage.C or TargetLanguage.ObjectiveC =>
                $"smile_index({index}, {ArrayDimension(array, dimension)}, {TargetEscapes.CString(array.Name)})",
            TargetLanguage.Cpp =>
                $"smile_index({index}, {ArrayDimension(array, dimension)}, {TargetEscapes.CString(array.Name)})",
            TargetLanguage.CSharp =>
                $"SmileIndex({index}, {ArrayDimension(array, dimension)}, {TargetEscapes.CSharpString(array.Name)})",
            TargetLanguage.JavaScript =>
                $"smileIndex({index}, {ArrayDimension(array, dimension)}, {TargetEscapes.JavaScriptString(array.Name)})",
            TargetLanguage.Java =>
                $"smileIndex({index}, {ArrayDimension(array, dimension)}, {TargetEscapes.JavaString(array.Name)})",
            TargetLanguage.Swift =>
                $"smileIndex({index}, {ArrayDimension(array, dimension)}, {TargetEscapes.SwiftString(array.Name)})",
            TargetLanguage.Python =>
                $"smile_index({index}, {ArrayDimension(array, dimension)}, {TargetEscapes.PythonString(array.Name)})",
            _ => index
        };

        private void WriteOrderedCIf(BoundIfStatement conditional, int clauseIndex)
        {
            BoundConditionalClause clause = conditional.Clauses[clauseIndex];
            string condition = PreparedExpression(clause.Condition);
            if (_language is TargetLanguage.Python)
            {
                Line($"if {condition}:");
                WritePythonSuite(clause.SourceItems);
            }
            else
            {
                Line($"if ({condition})");
                Line("{");
                _indent++;
                WriteItems(clause.SourceItems);
                _indent--;
                Line("}");
            }

            bool hasNext = clauseIndex + 1 < conditional.Clauses.Count;
            if (!hasNext && !conditional.HasElseClause)
            {
                return;
            }

            Line(_language is TargetLanguage.Python ? "else:" : "else");
            if (_language is not TargetLanguage.Python)
            {
                Line("{");
            }
            _indent++;
            if (hasNext)
            {
                WriteOrderedCIf(conditional, clauseIndex + 1);
            }
            else
            {
                WriteItems(conditional.ElseSourceItems);
            }

            _indent--;
            if (_language is not TargetLanguage.Python)
            {
                Line("}");
            }
        }

        private string ArrayElement(VariableSymbol array, IReadOnlyList<BoundExpression> indices)
        {
            string[] checkedIndices = indices
                .Select((index, position) => CheckedArrayIndex(array, Expression(index), position))
                .ToArray();
            return ArrayTarget(array, checkedIndices);
        }

        private static int ArrayDimension(VariableSymbol array, int dimension) =>
            dimension == 0 ? array.ArrayLength : array.ArraySecondLength;

        private string ArrayTarget(VariableSymbol array, IReadOnlyList<string> indices)
        {
            string name = Name(array);
            if (_language is TargetLanguage.CSharp && array.ArrayRank == 2)
            {
                return $"{name}[{string.Join(", ", indices)}]";
            }

            return name + string.Concat(indices.Select(index => $"[{index}]"));
        }

        private string Intrinsic(BoundIntrinsicExpression intrinsic) =>
            Intrinsic(intrinsic.Kind, intrinsic.Arguments.Select(Expression).ToArray());

        private string Intrinsic(BoundIntrinsicKind kind, IReadOnlyList<string> arguments) =>
            (_language, kind) switch
            {
                (TargetLanguage.CSharp, BoundIntrinsicKind.Timer) => "SmileTimer()",
                (TargetLanguage.CSharp, BoundIntrinsicKind.Abs) => $"Math.Abs({arguments[0]})",
                (TargetLanguage.CSharp, BoundIntrinsicKind.Min) => $"Math.Min({arguments[0]}, {arguments[1]})",
                (TargetLanguage.CSharp, BoundIntrinsicKind.Max) => $"Math.Max({arguments[0]}, {arguments[1]})",
                (TargetLanguage.JavaScript, BoundIntrinsicKind.Timer) => "smileTimer()",
                (TargetLanguage.JavaScript, BoundIntrinsicKind.Abs) => $"smileAbs({arguments[0]})",
                (TargetLanguage.JavaScript, BoundIntrinsicKind.Min) => $"smileMin({arguments[0]}, {arguments[1]})",
                (TargetLanguage.JavaScript, BoundIntrinsicKind.Max) => $"smileMax({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Java, BoundIntrinsicKind.Timer) => "smileTimer()",
                (TargetLanguage.Java, BoundIntrinsicKind.Abs) => $"Math.abs({arguments[0]})",
                (TargetLanguage.Java, BoundIntrinsicKind.Min) => $"Math.min({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Java, BoundIntrinsicKind.Max) => $"Math.max({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Swift, BoundIntrinsicKind.Timer) => "smileTimer()",
                (TargetLanguage.Swift, BoundIntrinsicKind.Abs) => $"Swift.abs({arguments[0]})",
                (TargetLanguage.Swift, BoundIntrinsicKind.Min) => $"Swift.min({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Swift, BoundIntrinsicKind.Max) => $"Swift.max({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Python, BoundIntrinsicKind.Timer) => "smile_timer()",
                (TargetLanguage.Python, BoundIntrinsicKind.Abs) => $"abs({arguments[0]})",
                (TargetLanguage.Python, BoundIntrinsicKind.Min) => $"min({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Python, BoundIntrinsicKind.Max) => $"max({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Cpp, BoundIntrinsicKind.Timer) => "smile_timer()",
                (TargetLanguage.Cpp, BoundIntrinsicKind.Abs) => $"smile_abs({arguments[0]})",
                (TargetLanguage.Cpp, BoundIntrinsicKind.Min) => $"std::min({arguments[0]}, {arguments[1]})",
                (TargetLanguage.Cpp, BoundIntrinsicKind.Max) => $"std::max({arguments[0]}, {arguments[1]})",
                (_, BoundIntrinsicKind.Timer) => "smile_timer()",
                (_, BoundIntrinsicKind.Abs) => $"smile_abs({arguments[0]})",
                (_, BoundIntrinsicKind.Min) => $"smile_min({arguments[0]}, {arguments[1]})",
                (_, BoundIntrinsicKind.Max) => $"smile_max({arguments[0]}, {arguments[1]})",
                _ => "0"
            };

        private void WriteIndexTemporary(string name, string expression)
        {
            Line(_language switch
            {
                TargetLanguage.CSharp or TargetLanguage.Java => $"int {name} = {expression};",
                TargetLanguage.C or TargetLanguage.ObjectiveC => $"size_t {name} = {expression};",
                TargetLanguage.Cpp => $"std::size_t {name} = {expression};",
                TargetLanguage.JavaScript => $"const {name} = {expression};",
                TargetLanguage.Swift => $"let {name}: Int = {expression}",
                TargetLanguage.Python => $"{name} = {expression}",
                _ => string.Empty
            });
        }

        private string Unary(BoundUnaryExpression unary)
        {
            string operand = Expression(unary.Operand);
            return unary.Operator.Kind switch
            {
                BoundUnaryOperatorKind.Identity => operand,
                BoundUnaryOperatorKind.Negation => $"(-{operand})",
                BoundUnaryOperatorKind.LogicalNegation => _language is TargetLanguage.Python
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
            BoundBinaryOperatorKind.Modulo => "%",
            BoundBinaryOperatorKind.Equality => "==",
            BoundBinaryOperatorKind.Inequality => "!=",
            BoundBinaryOperatorKind.Less => "<",
            BoundBinaryOperatorKind.LessOrEquals => "<=",
            BoundBinaryOperatorKind.Greater => ">",
            BoundBinaryOperatorKind.GreaterOrEquals => ">=",
            BoundBinaryOperatorKind.LogicalAnd => _language switch
            {
                TargetLanguage.Python => "and",
                _ => "&&"
            },
            BoundBinaryOperatorKind.LogicalOr => _language switch
            {
                TargetLanguage.Python => "or",
                _ => "||"
            },
            _ => string.Empty
        };

        private string DisplayExpression(BoundExpression expression, string value)
        {
            if (expression.Type is not SmileType.Boolean)
            {
                return _language is TargetLanguage.JavaScript ? $"String({value})" : value;
            }

            return _language switch
            {
                TargetLanguage.Python => $"(\"True\" if {value} else \"False\")",
                TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp => $"({value} ? \"True\" : \"False\")",
                _ => $"({value} ? \"True\" : \"False\")"
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
            TargetLanguage.Python => value ? "True" : "False",
            _ => value ? "true" : "false"
        };

        private string StringLiteral(string value) => _language switch
        {
            TargetLanguage.CSharp => TargetEscapes.CSharpString(value),
            TargetLanguage.C => TargetEscapes.CString(value),
            TargetLanguage.JavaScript => TargetEscapes.JavaScriptString(value),
            TargetLanguage.Java => TargetEscapes.JavaString(value),
            TargetLanguage.ObjectiveC => TargetEscapes.CString(value),
            TargetLanguage.Swift => TargetEscapes.SwiftString(value),
            TargetLanguage.Python => TargetEscapes.PythonString(value),
            TargetLanguage.Cpp => TargetEscapes.CString(value),
            _ => TargetEscapes.CString(value)
        };

        private string CommentPrefix() => _language switch
        {
            TargetLanguage.Python => "#",
            _ => "//"
        };

        private string Name(VariableSymbol variable) => _identifiers.Get(variable);

        private BoundConstStatement? FindConstant(VariableSymbol variable) =>
            EnumerateStatements(_program.SourceItems)
                .OfType<BoundConstStatement>()
                .FirstOrDefault(statement => statement.Variable.Equals(variable));

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
            if (text.Length > 0)
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
                    case BoundSelectStatement select:
                        foreach (BoundSelectCaseClause clause in select.Cases)
                        {
                            foreach (BoundStatement nested in EnumerateStatements(clause.SourceItems))
                            {
                                yield return nested;
                            }
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
                    BoundArraySetStatement set => set.Indices.Append(set.Value),
                    BoundConstStatement constant => new[] { constant.Initializer },
                    BoundCallStatement call => call.Arguments,
                    BoundReturnStatement { Value: not null } returnStatement => new[] { returnStatement.Value },
                    BoundCorePrintStatement print => print.Values,
                    BoundIfStatement conditional => conditional.Clauses.Select(clause => clause.Condition),
                    BoundSelectStatement select => new[] { select.Selector },
                    BoundForStatement loop => new[] { loop.LowerBound, loop.UpperBound },
                    BoundDoStatement { UntilCondition: not null } loop => new[] { loop.UntilCondition },
                    BoundWaitStatement wait => new[] { wait.Duration },
                    BoundRandomStatement random => new[] { random.LowerBound, random.UpperBound },
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
                case BoundArrayExpression array:
                    foreach (BoundExpression index in array.Indices)
                    {
                        foreach (BoundExpression child in WalkExpression(index)) yield return child;
                    }
                    break;
                case BoundIntrinsicExpression intrinsic:
                    foreach (BoundExpression argument in intrinsic.Arguments)
                    {
                        foreach (BoundExpression child in WalkExpression(argument)) yield return child;
                    }

                    break;
                case BoundCallExpression call:
                    foreach (BoundExpression argument in call.Arguments)
                    {
                        foreach (BoundExpression child in WalkExpression(argument)) yield return child;
                    }

                    break;
            }
        }

        private IEnumerable<IReadOnlyList<BoundSourceItem>> AllExecutableItemSets()
        {
            foreach (BoundRoutineDeclaration routine in _program.Routines)
            {
                yield return routine.SourceItems;
            }

            yield return _program.SourceItems;
        }

        private IEnumerable<BoundStatement> ProgramStatements() =>
            AllExecutableItemSets().SelectMany(EnumerateStatements);

        private IEnumerable<BoundExpression> ProgramExpressions() =>
            AllExecutableItemSets().SelectMany(EnumerateExpressions);

        private bool ProgramHasArrays() => _program.AllVariables.Any(variable => variable.IsArray);

        private bool ProgramHasTextConcatenation() => ProgramExpressions().Any(expression => expression is BoundBinaryExpression
        {
            Operator.Kind: BoundBinaryOperatorKind.StringConcatenation
        });

        private bool ProgramHasTextComparison() => ProgramExpressions().Any(expression => expression is BoundBinaryExpression binary &&
            binary.Left.Type is SmileType.String &&
            binary.Operator.Kind is BoundBinaryOperatorKind.Equality or BoundBinaryOperatorKind.Inequality) ||
            ProgramStatements().OfType<BoundSelectStatement>().Any(select => select.Selector.Type is SmileType.String);

        private static IEnumerable<VariableSymbol> AssignedGlobals(IReadOnlyList<BoundSourceItem> items)
        {
            foreach (BoundStatement statement in EnumerateStatements(items))
            {
                switch (statement)
                {
                    case BoundSetStatement { Variable.IsGlobal: true } set:
                        yield return set.Variable;
                        break;
                    case BoundForStatement { Counter.IsGlobal: true } loop:
                        yield return loop.Counter;
                        break;
                    case BoundGetKeyStatement { Target.IsGlobal: true } getKey:
                        yield return getKey.Target;
                        break;
                    case BoundRandomStatement { Target.IsGlobal: true } random:
                        yield return random.Target;
                        break;
                }
            }
        }

        private static bool IsAssigned(VariableSymbol variable, IReadOnlyList<BoundSourceItem> items) =>
            EnumerateStatements(items).Any(statement => statement switch
            {
                BoundSetStatement set => ReferenceEquals(set.Variable, variable),
                BoundForStatement loop => ReferenceEquals(loop.Counter, variable),
                BoundGetKeyStatement getKey => ReferenceEquals(getKey.Target, variable),
                BoundRandomStatement random => ReferenceEquals(random.Target, variable),
                _ => false
            });

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

        private static bool HasExitTargetingLoop(
            IReadOnlyList<BoundSourceItem> items,
            LoopKind targetKind,
            int nestedSameKindDepth = 0)
        {
            foreach (BoundSourceItem item in items)
            {
                if (item is not BoundStatement statement)
                {
                    continue;
                }

                if (statement is BoundExitStatement exit &&
                    nestedSameKindDepth == 0 &&
                    ((targetKind is LoopKind.For && exit.Kind is BoundExitKind.For) ||
                     (targetKind is LoopKind.Do && exit.Kind is BoundExitKind.Do)))
                {
                    return true;
                }

                switch (statement)
                {
                    case BoundIfStatement conditional:
                        if (conditional.Clauses.Any(clause => HasExitTargetingLoop(clause.SourceItems, targetKind, nestedSameKindDepth)) ||
                            HasExitTargetingLoop(conditional.ElseSourceItems, targetKind, nestedSameKindDepth))
                        {
                            return true;
                        }

                        break;
                    case BoundSelectStatement select:
                        if (select.Cases.Any(clause => HasExitTargetingLoop(clause.SourceItems, targetKind, nestedSameKindDepth)))
                        {
                            return true;
                        }

                        break;
                    case BoundForStatement loop:
                        if (HasExitTargetingLoop(loop.SourceItems, targetKind, nestedSameKindDepth + (targetKind is LoopKind.For ? 1 : 0)))
                        {
                            return true;
                        }

                        break;
                    case BoundDoStatement loop:
                        if (HasExitTargetingLoop(loop.SourceItems, targetKind, nestedSameKindDepth + (targetKind is LoopKind.Do ? 1 : 0)))
                        {
                            return true;
                        }

                        break;
                }
            }

            return false;
        }
    }

}
