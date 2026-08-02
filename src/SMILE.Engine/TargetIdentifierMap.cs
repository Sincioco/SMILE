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
        ISet<string> reserved = TargetReservedNames.For(language);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var names = new Dictionary<VariableSymbol, string>();

        foreach (VariableSymbol variable in program.Variables)
        {
            string preferred = IsSafeTargetIdentifier(variable.Name, language, reserved)
                ? variable.Name
                : BuildMappedName(variable.Name, language);
            string unique = MakeUnique(preferred, used, language);

            used.Add(unique);
            names.Add(variable, unique);
        }

        return new TargetIdentifierMap(names);
    }

    public string Get(VariableSymbol variable) => _names[variable];

    private static bool IsSafeTargetIdentifier(
        string name,
        TargetLanguage language,
        ISet<string> reserved) =>
        IsTargetIdentifierShape(name, language) && !RequiresMapping(language, name, reserved);

    private static bool IsTargetIdentifierShape(string name, TargetLanguage language) =>
        language is TargetLanguage.Cobol
            ? IsCobolIdentifier(name)
            : IsPortableIdentifier(name);

    private static string BuildMappedName(string name, TargetLanguage language) =>
        language is TargetLanguage.Cobol
            ? BuildCobolMappedName(name)
            // A single underscore is valid SMILE, but Java and Swift cannot use it
            // as a normal variable. Map it to the cleanest readable prefix form.
            : name == "_" ? MappedPrefix : MappedPrefix + name;

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

    private static bool IsCobolIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !SyntaxFacts.IsAsciiLetter(name[0]))
        {
            return false;
        }

        // SMILE identifiers can contain underscores, but ordinary COBOL data
        // names use letters, digits, and hyphens. Since SMILE never permits
        // hyphens, preserving the letter/digit-only subset is the cleanest
        // readable mapping.
        for (int index = 1; index < name.Length; index++)
        {
            if (!SyntaxFacts.IsAsciiLetter(name[index]) && name[index] is not (>= '0' and <= '9'))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildCobolMappedName(string name)
    {
        var characters = new List<char>();
        bool lastWasHyphen = false;

        foreach (char value in name)
        {
            bool isLetterOrDigit =
                SyntaxFacts.IsAsciiLetter(value) ||
                value is >= '0' and <= '9';
            if (isLetterOrDigit)
            {
                characters.Add(value);
                lastWasHyphen = false;
                continue;
            }

            if (characters.Count > 0 && !lastWasHyphen)
            {
                characters.Add('-');
                lastWasHyphen = true;
            }
        }

        while (characters.Count > 0 && characters[^1] == '-')
        {
            characters.RemoveAt(characters.Count - 1);
        }

        string readablePart = characters.Count == 0
            ? "VAR"
            : new string(characters.ToArray());
        return "SMILE-" + readablePart;
    }

    private static bool RequiresMapping(TargetLanguage language, string name, ISet<string> reserved)
    {
        if (reserved.Contains(name))
        {
            return true;
        }

        // C and Objective-C reserve implementation namespace identifiers that
        // begin with "__" or with "_" followed by an uppercase ASCII letter.
        // SMILE lets learners write those names, so targets map them rather
        // than emitting technically reserved implementation identifiers.
        if (language is TargetLanguage.C or TargetLanguage.ObjectiveC &&
            IsCImplementationReservedIdentifier(name))
        {
            return true;
        }

        return false;
    }

    private static bool IsCImplementationReservedIdentifier(string name) =>
        name.StartsWith("__", StringComparison.Ordinal) ||
        (name.Length >= 2 && name[0] == '_' && SyntaxFacts.IsAsciiUppercaseLetter(name[1]));

    private static string MakeUnique(string preferred, ISet<string> used, TargetLanguage language)
    {
        if (!used.Contains(preferred))
        {
            return preferred;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = language is TargetLanguage.Cobol
                ? preferred + "-" + suffix
                : preferred + "_" + suffix;
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
            "ushort", "using", "virtual", "void", "volatile", "while",
            "add", "alias", "and", "ascending", "async", "await", "by", "descending", "dynamic",
            "equals", "file", "from", "get", "global", "group", "init", "into", "join", "let",
            "managed", "nameof", "nint", "not", "notnull", "nuint", "on", "or", "orderby",
            "partial", "record", "remove", "required", "scoped", "select", "set", "unmanaged",
            "value", "var", "when", "where", "with", "yield",
            "Console", "Program", "Main", "String", "System"
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
            "with", "yield", "arguments", "console", "eval"
        };

        private static readonly string[] Java =
        {
            "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class",
            "const", "continue", "default", "do", "double", "else", "enum", "exports", "extends",
            "false", "final", "finally", "float", "for", "goto", "if", "implements", "import",
            "instanceof", "int", "interface", "long", "module", "native", "new", "null", "open",
            "opens", "package", "private", "protected", "provides", "public", "requires", "return",
            "record", "sealed", "permits", "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw",
            "throws", "to", "transient", "transitive", "true", "try", "uses", "var", "void",
            "volatile", "while", "with", "yield", "_", "System", "String", "Program", "main", "args"
        };

        private static readonly string[] Cobol =
        {
            "accept", "add", "all", "and", "any", "by", "call", "cancel", "class", "close", "compute", "configuration",
            "copy",
            "data", "display", "divide", "division", "else", "end", "entry", "environment", "evaluate",
            "exit", "fd", "file", "from", "function", "global", "goback", "identification", "if", "in", "initialize",
            "input-output", "inspect", "into", "is", "linkage", "merge", "move", "multiply", "not", "object",
            "of", "open", "or", "perform", "pic", "picture", "procedure", "program", "program-id", "read",
            "record", "return", "rewrite", "run", "section", "select", "self", "set", "sort", "stop",
            "string", "subtract", "super", "then", "to", "type", "until", "using", "value", "when",
            "working-storage", "write",
            "Program", "SMILE-NEWLINE", "SPACE", "SPACES", "ZERO", "ZEROS", "ZEROES"
        };

        private static readonly string[] ObjectiveC = C
            .Concat(new[]
            {
                "Class", "Nil", "NSString", "printf", "main", "id", "self", "super", "YES", "NO", "nil", "NULL"
            })
            .ToArray();

        private static readonly string[] Swift =
        {
            "Any", "Self", "Type", "as", "associatedtype", "break", "case", "catch", "class",
            "continue", "default", "defer", "deinit", "do", "else", "enum", "extension", "fallthrough",
            "false", "fileprivate", "for", "func", "guard", "if", "import", "in", "init", "inout",
            "internal", "is", "let", "nil", "open", "operator", "private", "protocol", "public",
            "repeat", "return", "self", "static", "struct", "subscript", "super", "switch", "throw",
            "throws", "true", "try", "typealias", "var", "where", "while",
            "_", "async", "await", "print", "yield", "String"
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
                    TargetLanguage.Cobol => Cobol,
                    TargetLanguage.ObjectiveC => ObjectiveC,
                    TargetLanguage.Swift => Swift,
                    _ => Array.Empty<string>()
                },
                language is TargetLanguage.Cobol
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
    }
}
