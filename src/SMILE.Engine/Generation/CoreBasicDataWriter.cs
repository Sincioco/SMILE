namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private bool NativeData => _language is TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp;

        private void WriteDataLoad(BoundDataLoadStatement load)
        {
            string key = NewOrderedValue(SmileType.String, PreparedExpression(load.Key));
            string array = Name(load.Destination);
            string recover = DataRecoveryLiteral(load.Status is not null);
            string result = $"_smileData{++_orderedTempId}";
            string count, status;
            if (NativeData)
            {
                count = result + "Count";
                WriteNumberTemporary(count, "0");
                string pointer = _language is TargetLanguage.Cpp ? array + ".data()" : array;
                string keyBytes = _language is TargetLanguage.Cpp ? key + ".data()" : key;
                string keyLength = _language is TargetLanguage.Cpp ? key + ".size()" : $"strlen({key})";
                status = NewOrderedValue(SmileType.Integer, $"smile_load_data({keyBytes}, {keyLength}, {pointer}, {load.Destination.ArrayLength}, &{count}, {recover})");
                Line($"(void){status};");
            }
            else
            {
                string call = _language switch
                {
                    TargetLanguage.CSharp => $"SmileLoadData({key}, {array}, {recover})",
                    TargetLanguage.Python => $"smile_load_data({key}, {array}, {recover})",
                    TargetLanguage.Swift => $"smileLoadData({key}, &{array}, {recover})",
                    _ => $"{(_language is TargetLanguage.JavaScript ? "await " : "")}smileLoadData({key}, {array}, {recover})"
                };
                Line(_language switch
                {
                    TargetLanguage.CSharp => $"var {result} = {call};",
                    TargetLanguage.Java => $"long[] {result} = {call};",
                    TargetLanguage.JavaScript => $"const {result} = {call};",
                    TargetLanguage.Swift => $"let {result} = {call}",
                    _ => $"{result} = {call}"
                });
                count = _language is TargetLanguage.CSharp ? result + ".Count" : _language is TargetLanguage.Swift ? result + ".0" : result + "[0]";
                status = _language is TargetLanguage.CSharp ? result + ".Status" : _language is TargetLanguage.Swift ? result + ".1" : result + "[1]";
            }
            WriteDataTarget(load.Count, count);
            if (load.Status is not null) WriteDataTarget(load.Status, status);
        }

        private void WriteDataSave(BoundDataSaveStatement save)
        {
            string count = NewOrderedValue(SmileType.Integer, PreparedExpression(save.Count));
            string key = NewOrderedValue(SmileType.String, PreparedExpression(save.Key));
            string array = Name(save.Source);
            string recover = DataRecoveryLiteral(save.Status is not null);
            string call;
            if (NativeData)
            {
                string pointer = _language is TargetLanguage.Cpp ? array + ".data()" : array;
                string keyBytes = _language is TargetLanguage.Cpp ? key + ".data()" : key;
                string length = _language is TargetLanguage.Cpp ? key + ".size()" : $"strlen({key})";
                call = $"smile_save_data({pointer}, {save.Source.ArrayLength}, {count}, {keyBytes}, {length}, {recover})";
            }
            else call = _language switch
            {
                TargetLanguage.CSharp => $"SmileSaveData({array}, {count}, {key}, {recover})",
                TargetLanguage.Python => $"smile_save_data({array}, {count}, {key}, {recover})",
                _ => $"{(_language is TargetLanguage.JavaScript ? "await " : "")}smileSaveData({array}, {count}, {key}, {recover})"
            };
            if (save.Status is null) Line((_language is TargetLanguage.Swift ? "_ = " : "") + call + (_language is TargetLanguage.Swift or TargetLanguage.Python ? "" : ";"));
            else WriteDataTarget(save.Status, NewOrderedValue(SmileType.Integer, call));
        }

        private string DataRecoveryLiteral(bool recover) => NativeData ? (recover ? "1" : "0") :
            _language is TargetLanguage.Python ? (recover ? "True" : "False") : (recover ? "true" : "false");

        private void WriteDataTarget(BoundExpression expression, string value)
        {
            if (expression is BoundVariableExpression scalar) { WriteSimpleAssignment(Name(scalar.Variable), value); return; }
            var array = (BoundArrayExpression)expression;
            var indices = new List<string>();
            for (int dimension = 0; dimension < array.Indices.Count; dimension++)
            {
                string indexValue = PreparedExpression(array.Indices[dimension]);
                string index = $"_smileIndex{++_orderedTempId}";
                WriteIndexTemporary(index, CheckedArrayIndex(array.Array, indexValue, dimension));
                indices.Add(index);
            }
            WriteSimpleAssignment(ArrayTarget(array.Array, indices), value);
        }

        private void WriteDataIncludes()
        {
            if (!_features.HasDataPersistence) return;
            if (NativeData) Lines(NativeDataPersistence.Includes.Split('\n'));
            if (_language is TargetLanguage.Python)
            {
                Lines("import hashlib", "import sys");
                if (!_features.HasNumberPersistence) Line("import os");
                if (!_features.HasNumberPersistence && !_features.HasTextFileLoad) Line("import pathlib");
                if (_features.HasDataSave) Lines("import ctypes", "import time");
            }
            if (_language is TargetLanguage.JavaScript)
            {
                Line("const smileCrypto = require(\"node:crypto\");");
                if (!_features.HasNumberPersistence && !_features.HasTextFileLoad)
                    Lines("const smileFileSystem = require(\"node:fs/promises\");", "const smilePath = require(\"node:path\");");
            }
            if (_language is TargetLanguage.Swift && !(_features.HasClearScreen || _features.HasMoveCursor || _features.HasTextColor)) Line("import WinSDK");
        }

        private void WriteDataPrototypes()
        {
            if (!NativeData) return;
            if (_features.HasDataLoad) Line("static " + NativeDataPersistence.LoadPrototype + ";");
            if (_features.HasDataSave) Line("static " + NativeDataPersistence.SavePrototype + ";");
        }

        private void WriteDataHelpers()
        {
            if (!_features.HasDataPersistence) return;
            Lines((NativeData ? NativeDataPersistence.Generate(_program) : ManagedDataPersistence.Generate(_program, _language)).Split('\n'));
            Line();
        }
    }
}
