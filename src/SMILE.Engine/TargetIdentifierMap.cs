namespace SMILE.Engine;

internal sealed class TargetIdentifierMap
{
    private const string MappedPrefix = "_smile_";
    private readonly IReadOnlyDictionary<VariableSymbol, string> _names;

    private TargetIdentifierMap(IReadOnlyDictionary<VariableSymbol, string> names)
    {
        _names = names;
    }

    public static TargetIdentifierMap Create(BoundProgram program, TargetLanguage language)
    {
        var reserved = TargetReservedNames.For(language);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<VariableSymbol, string>();

        foreach (VariableSymbol variable in program.Variables)
        {
            string preferred = IsSafeTargetIdentifier(variable.Name, reserved)
                ? variable.Name
                : MappedPrefix + variable.Name;
            string unique = MakeUnique(preferred, used);

            used.Add(unique);
            names.Add(variable, unique);
        }

        return new TargetIdentifierMap(names);
    }

    public string Get(VariableSymbol variable) => _names[variable];

    private static bool IsSafeTargetIdentifier(string name, ISet<string> reserved) =>
        IsPortableIdentifier(name) && !reserved.Contains(name);

    private static bool IsPortableIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !SyntaxFacts.IsIdentifierStart(name[0]))
        {
            return false;
        }

        for (int index = 1; index < name.Length; index++)
        {
            if (!SyntaxFacts.IsIdentifierPart(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string MakeUnique(string preferred, ISet<string> used)
    {
        if (!used.Contains(preferred))
        {
            return preferred;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = preferred + "_" + suffix;
            if (!used.Contains(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static class TargetReservedNames
    {
        private static readonly string[] CSharp =
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
            "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
            "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
            "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
            "ushort", "using", "virtual", "void", "volatile", "while", "Console", "Program", "Main"
        };

        private static readonly string[] C =
        {
            "auto", "break", "case", "char", "const", "continue", "default", "do", "double",
            "else", "enum", "extern", "float", "for", "goto", "if", "inline", "int", "long",
            "register", "restrict", "return", "short", "signed", "sizeof", "static", "struct",
            "switch", "typedef", "union", "unsigned", "void", "volatile", "while", "_Alignas",
            "_Alignof", "_Atomic", "_Bool", "_Complex", "_Generic", "_Imaginary", "_Noreturn",
            "_Static_assert", "_Thread_local", "printf", "main", "stdout"
        };

        private static readonly string[] JavaScript =
        {
            "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
            "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for",
            "function", "if", "import", "in", "instanceof", "let", "new", "null", "return",
            "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while",
            "with", "yield", "console"
        };

        private static readonly string[] Java =
        {
            "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class",
            "const", "continue", "default", "do", "double", "else", "enum", "exports", "extends",
            "false", "final", "finally", "float", "for", "goto", "if", "implements", "import",
            "instanceof", "int", "interface", "long", "module", "native", "new", "null", "open",
            "opens", "package", "private", "protected", "provides", "public", "requires", "return",
            "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw",
            "throws", "to", "transient", "transitive", "true", "try", "uses", "var", "void",
            "volatile", "while", "with", "yield", "System", "String", "Program", "main", "args"
        };

        private static readonly string[] ObjectiveC = C
            .Concat(new[]
            {
                "NSString", "printf", "main", "id", "self", "super", "YES", "NO", "nil", "NULL"
            })
            .ToArray();

        private static readonly string[] Swift =
        {
            "Any", "Self", "Type", "as", "associatedtype", "break", "case", "catch", "class",
            "continue", "default", "defer", "deinit", "do", "else", "enum", "extension", "fallthrough",
            "false", "fileprivate", "for", "func", "guard", "if", "import", "in", "init", "inout",
            "internal", "is", "let", "nil", "open", "operator", "private", "protocol", "public",
            "repeat", "return", "self", "static", "struct", "subscript", "super", "switch", "throw",
            "throws", "true", "try", "typealias", "var", "where", "while", "print"
        };

        private static readonly string[] Masm =
        {
            "add", "addr", "and", "assume", "byte", "call", "code", "data", "dword", "end", "endp",
            "equ", "extern", "lea", "main", "mov", "none", "option", "proc", "ptr", "qword", "rax",
            "rcx", "rdx", "rsp", "r8d", "r9", "xor"
        };

        public static ISet<string> For(TargetLanguage language) =>
            new HashSet<string>(
                language switch
                {
                    TargetLanguage.CSharp => CSharp,
                    TargetLanguage.C => C,
                    TargetLanguage.MasmX64 => Masm,
                    TargetLanguage.JavaScript => JavaScript,
                    TargetLanguage.Java => Java,
                    TargetLanguage.ObjectiveC => ObjectiveC,
                    TargetLanguage.Swift => Swift,
                    _ => Array.Empty<string>()
                },
                StringComparer.Ordinal);
    }
}
