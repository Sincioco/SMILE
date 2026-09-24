namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private string NumberStorageCall(string operation, string key, string value)
        {
            string method = _language switch
            {
                TargetLanguage.CSharp => $"Smile{operation}Number",
                TargetLanguage.JavaScript or TargetLanguage.Java or TargetLanguage.Swift => $"smile{operation}Number",
                _ => $"smile_{operation.ToLowerInvariant()}_number"
            };
            string fileName = StringLiteral(SmilePersistentStorage.Sanitize(key) + ".txt");
            return $"{(_language is TargetLanguage.JavaScript ? "await " : "")}{method}({fileName}, {value})";
        }

        private void WriteNumberLoad(BoundNumberLoadStatement load) =>
            WriteSimpleAssignment(Name(load.Target), NumberStorageCall("Load", load.Key, PreparedExpression(load.DefaultValue)));

        private void WriteNumberSave(BoundNumberSaveStatement save) =>
            Line(NumberStorageCall("Save", save.Key, Name(save.Source)) + (_language is TargetLanguage.Python or TargetLanguage.Swift ? "" : ";"));

        private void WriteNumberStorageIncludes()
        {
            if (!_features.HasNumberPersistence) return;
            switch (_language)
            {
                case TargetLanguage.C or TargetLanguage.ObjectiveC:
                    Lines("#include <errno.h>", "#include <wchar.h>");
                    break;
                case TargetLanguage.Cpp:
                    Lines("#include <filesystem>", "#include <fstream>", "#include <string_view>", "#include <charconv>");
                    break;
                case TargetLanguage.Python:
                    Line("import os");
                    if (!_features.HasTextFileLoad) Line("import pathlib");
                    break;
                case TargetLanguage.JavaScript when !_features.HasTextFileLoad:
                    Lines("const smileFileSystem = require(\"node:fs/promises\");", "const smilePath = require(\"node:path\");");
                    break;
            }
        }

        private void WriteNumberStoragePrototypes()
        {
            if (!_features.HasNumberPersistence || _language is not (TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp)) return;
            string type = _language is TargetLanguage.Cpp ? "std::int64_t" : "int64_t";
            if (_features.HasNumberLoad) Line($"static {type} smile_load_number(const char *key, {type} fallback);");
            if (_features.HasNumberSave) Line($"static void smile_save_number(const char *key, {type} value);");
        }

        private void WriteNumberStorageHelpers()
        {
            if (!_features.HasNumberPersistence) return;
            Lines((_language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp
                ? NativeNumberPersistence.Generate(_program, cpp: _language is TargetLanguage.Cpp)
                : ManagedNumberPersistence.Generate(_program, _language)).Split('\n'));
            Line();
        }
    }
}

internal sealed partial class CoreBasicMasmWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void WriteNumberLoad(BoundNumberLoadStatement load, int indent)
        {
            EmitExpression(load.DefaultValue, indent);
            Emit(indent, "mov rdx, rax");
            Emit(indent, $"lea rcx, {_owner.InternString(SmilePersistentStorage.Sanitize(load.Key) + ".txt")}");
            NoteCall(2);
            Emit(indent, "call smile_load_number");
            StoreVariable(load.Target, "rax", indent);
        }

        private void WriteNumberSave(BoundNumberSaveStatement save, int indent)
        {
            LoadVariable(save.Source, indent);
            Emit(indent, "mov rdx, rax");
            Emit(indent, $"lea rcx, {_owner.InternString(SmilePersistentStorage.Sanitize(save.Key) + ".txt")}");
            NoteCall(2);
            Emit(indent, "call smile_save_number");
        }
    }
}

internal sealed partial class CobolWriter
{
    private sealed partial class ProcedureEmitter
    {
        private void WriteNumberLoad(BoundNumberLoadStatement load, int indent)
        {
            string value = PrepareExpression(load.DefaultValue, indent);
            Temporary fallback = NewTemporary(SmileType.Integer);
            Line(indent, $"MOVE {value} TO {fallback.Name}");
            string key = SmilePersistentStorage.Sanitize(load.Key) + ".txt";
            Line(indent, $"CALL \"smile_load_number_cobol\" USING BY CONTENT {TargetEscapes.CobolString(key)} BY VALUE {key.Length} BY REFERENCE {fallback.Name} {_owner.Name(load.Target)}");
        }

        private void WriteNumberSave(BoundNumberSaveStatement save, int indent)
        {
            Temporary value = NewTemporary(SmileType.Integer);
            Line(indent, $"MOVE {_owner.ExpressionName(save.Source)} TO {value.Name}");
            string key = SmilePersistentStorage.Sanitize(save.Key) + ".txt";
            Line(indent, $"CALL \"smile_save_number_cobol\" USING BY CONTENT {TargetEscapes.CobolString(key)} BY VALUE {key.Length} BY REFERENCE {value.Name}");
        }
    }
}
