namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private bool UsesCObjects => _language is TargetLanguage.C or TargetLanguage.ObjectiveC && _program.ClassTypes.Count > 0;
        private readonly List<(string Mark, int LoopDepth)> _cWithObjectScopes = new();

        private void WriteCClassRuntime()
        {
            if (!UsesCObjects) return;
            Lines(NativeClassSupport.Definition.Split('\n'));
            foreach (ClassTypeSymbol type in _program.ClassTypes)
                Line($"typedef struct {_identifiers.Get(type)} {_identifiers.Get(type)};");
            Line();
        }

        private void WriteManagedCCollection()
        {
            if (UsesCObjects) Line("smile_object_collect();");
            if (UsesManagedCText) Line("smile_text_collect();");
        }

        private void WriteCClassFactories()
        {
            if (!UsesCObjects) return;
            foreach (ClassTypeSymbol type in _program.ClassTypes)
            {
                string name = _identifiers.Get(type);
                Line($"static {name}* {name}_new({ParameterList(type.Constructor, includeReceiver: false)});");
            }
            foreach (ClassTypeSymbol type in _program.ClassTypes)
            {
                string name = _identifiers.Get(type);
                if (UsesManagedCText && type.ContainsText)
                {
                    Line($"static void {name}_finalize(void* instance) {{");
                    _indent++;
                    Line($"{name}* value = instance;");
                    foreach (InstanceFieldSymbol field in type.Fields)
                        WriteFieldElements(field, "value->" + _identifiers.Get(field), target => WriteCValueRoots(field.Type, target, register: false));
                    _indent--;
                    Line("}");
                }
                Line($"static {name}* {name}_new({ParameterList(type.Constructor, includeReceiver: false)}) {{");
                _indent++;
                Line($"{name}* _smileInstance = smile_object_allocate(sizeof({name}), {(UsesManagedCText && type.ContainsText ? name + "_finalize" : "NULL")});");
                Line("smile_object_register(&_smileInstance);");
                foreach (InstanceFieldSymbol field in type.Fields)
                    WriteFieldElements(field, "_smileInstance->" + _identifiers.Get(field), target =>
                    {
                        Line($"{target} = {DefaultLiteral(field.Type)};");
                        WriteCValueRoots(field.Type, target, register: true);
                    });
                Line($"{RoutineName(type.Constructor)}(_smileInstance{string.Concat(type.Constructor.Parameters.Select(parameter => ", " + StorageName(parameter)))});");
                Line("smile_object_unregister(&_smileInstance);");
                Line("return _smileInstance;");
                _indent--;
                Line("}");
                Line();
            }
        }

        private void BeginCWithObjectScope()
        {
            if (!UsesCObjects) return;
            string mark = $"_smileWithRoots{++_orderedTempId}";
            Line($"size_t {mark} = smile_object_checkpoint();");
            _cWithObjectScopes.Add((mark, _loops.Count));
        }

        private void CaptureCWithObjectRoots()
        {
            if (UsesCObjects) _managedTextTemporaryRoots.Peek().RemoveAll(root => root.Type is ClassTypeSymbol);
            EndManagedTextStatement();
            BeginManagedTextStatement();
        }

        private void EndCWithObjectScope()
        {
            if (!UsesCObjects) return;
            Line($"smile_object_restore({_cWithObjectScopes[^1].Mark});");
            _cWithObjectScopes.RemoveAt(_cWithObjectScopes.Count - 1);
        }

        private void WriteCWithExitCleanup(int targetDepth)
        {
            if (!UsesCObjects) return;
            foreach (var scope in _cWithObjectScopes.AsEnumerable().Reverse().Where(scope => scope.LoopDepth >= targetDepth))
                Line($"smile_object_restore({scope.Mark});");
        }
    }
}
