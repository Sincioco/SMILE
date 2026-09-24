namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private void WriteTextFileLoad(BoundTextFileLoadStatement load)
        {
            string path = PreparedExpression(load.Path);
            string destination = Name(load.Destination);
            string call = _language switch
            {
                TargetLanguage.CSharp => $"SmileLoadTextFile({path}, {destination})",
                TargetLanguage.JavaScript => $"await smileLoadTextFile({path}, {destination})",
                TargetLanguage.Java => $"smileLoadTextFile({path}, {destination})",
                TargetLanguage.Swift => $"smileLoadTextFile({path}, &{destination})",
                TargetLanguage.Python => $"smile_load_text_file({path}, {destination})",
                TargetLanguage.Cpp => $"smile_load_text_file({path}, std::span<std::int64_t>({destination}))",
                _ => $"smile_load_text_file({path}, {destination}, {load.Destination.ArrayLength})"
            };
            WriteSimpleAssignment(Name(load.Count), call);
        }

        private void WriteTextFileIncludes()
        {
            if (!_features.HasTextFileLoad) return;
            switch (_language)
            {
                case TargetLanguage.C or TargetLanguage.ObjectiveC: Line("#include <string.h>"); break;
                case TargetLanguage.Cpp:
                    Lines("#include <cstring>", "#include <filesystem>", "#include <fstream>", "#include <span>");
                    break;
                case TargetLanguage.Python: Line("import pathlib"); break;
                case TargetLanguage.JavaScript:
                    Lines("const smileFileSystem = require(\"node:fs/promises\");", "const smilePath = require(\"node:path\");");
                    break;
            }
        }

        private void WriteTextFilePrototypes()
        {
            if (!_features.HasTextFileLoad || _language is not (TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp)) return;
            Line(NativeTextFileSupport.PathDefinition.Split('\n')[0] + ";");
            Line((_language is TargetLanguage.Cpp ? NativeTextFileSupport.CppLoadDefinition : NativeTextFileSupport.LoadDefinition).Split('\n')[0] + ";");
        }

        private void WriteTextFileHelpers()
        {
            if (!_features.HasTextFileLoad) return;
            if (_language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp)
            {
                Lines(NativeTextFileSupport.PathDefinition.Split('\n'));
                Lines((_language is TargetLanguage.Cpp ? NativeTextFileSupport.CppLoadDefinition : NativeTextFileSupport.LoadDefinition).Split('\n'));
            }
            else Lines(ManagedTextFileSupport.Definition(_language).Split('\n'));
        }
    }
}

internal sealed partial class CoreBasicMasmWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void WriteTextFileLoad(BoundTextFileLoadStatement load, int indent)
        {
            EmitExpression(load.Path, indent);
            Emit(indent, "mov rcx, rax");
            EmitArrayBase(load.Destination, "rdx", indent);
            Emit(indent, $"mov r8, {load.Destination.ArrayLength}");
            NoteCall(3);
            Emit(indent, "call smile_load_text_file");
            StoreVariable(load.Count, "rax", indent);
        }
    }
}

internal sealed partial class CobolWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void WriteTextFileLoad(BoundTextFileLoadStatement load, int indent)
        {
            string path = PrepareExpression(load.Path, indent);
            Temporary captured = NewTemporary(SmileType.String);
            Assign(captured.Name, SmileType.String, load.Path, path, indent, LengthName(captured));
            Temporary capacity = NewTemporary(SmileType.Integer);
            Line(indent, $"MOVE {load.Destination.ArrayLength} TO {capacity.Name}");
            Line(indent, $"CALL \"smile_load_text_file_cobol\" USING BY REFERENCE {captured.Name} {LengthName(captured)} {_owner.ArrayElementName(load.Destination)}(1) {capacity.Name} {_owner.Name(load.Count)}");
        }
    }
}
