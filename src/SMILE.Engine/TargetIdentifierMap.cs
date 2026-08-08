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
        ISet<string> reserved)
    {
        // MASM has one case-insensitive symbol namespace shared with a very
        // large, evolving instruction/register/directive vocabulary. Prefixing
        // every learner variable is both safer and easier to understand than
        // pretending a hand-maintained keyword list can stay exhaustive.
        if (language is TargetLanguage.MasmX64)
        {
            return false;
        }

        return IsTargetIdentifierShape(name, language) && !RequiresMapping(language, name, reserved);
    }

    private static bool IsTargetIdentifierShape(string name, TargetLanguage language) =>
        language is TargetLanguage.Cobol
            ? IsCobolIdentifier(name)
            : IsPortableIdentifier(name);

    private static string BuildMappedName(string name, TargetLanguage language) =>
        language switch
        {
            TargetLanguage.Cobol => BuildCobolMappedName(name),
            // Prefixing a C++ implementation-reserved spelling is not enough:
            // the original double underscore would remain reserved anywhere in
            // the final identifier. Spell reserved underscores out so the
            // generated name itself is safe as well as deterministic.
            TargetLanguage.Cpp when IsCppImplementationReservedIdentifier(name) =>
                MappedPrefix + BuildCppReservedUnderscoreName(name),
            // A single underscore is valid SMILE, but Java and Swift cannot use it
            // as a normal variable. Map it to the cleanest readable prefix form.
            _ => name == "_" ? MappedPrefix : MappedPrefix + name
        };

    private static string BuildCppReservedUnderscoreName(string name)
    {
        string readable = name.Replace("_", "_underscore_", StringComparison.Ordinal);
        while (readable.Contains("__", StringComparison.Ordinal))
        {
            readable = readable.Replace("__", "_", StringComparison.Ordinal);
        }

        return readable.Trim('_');
    }

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
        string candidate = "SMILE-" + readablePart;

        // COBOL is case-insensitive, and several compiler-owned IF/runtime
        // fields live in predictable SMILE-* namespaces. An underscore in the
        // source becomes a hyphen here, so names such as IF_CONDITION_0 would
        // otherwise become the exact scratch field emitted by the generator.
        // Keep those namespaces exclusively compiler-owned while preserving a
        // readable, deterministic spelling for the student's variable.
        return IsCobolCompilerOwnedIdentifier(candidate)
            ? "SMILE-VAR-" + readablePart
            : candidate;
    }

    private static bool IsCobolCompilerOwnedIdentifier(string name) =>
        name.StartsWith("SMILE-IF-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("SMILE-WHILE-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("SMILE-RUNTIME-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("SMILE-STATEMENT-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("SMILE-EXPRESSION-", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("SMILE-SET-LENGTH-", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresMapping(TargetLanguage language, string name, ISet<string> reserved)
    {
        if (reserved.Contains(name))
        {
            return true;
        }

        // C-family INPUT statements own deterministic per-statement scratch
        // names in main. Mapping the whole prefix keeps a learner spelling
        // from being captured by its nested scratch declaration.
        if (language is TargetLanguage.C or TargetLanguage.ObjectiveC &&
            name.StartsWith("smileInput", StringComparison.Ordinal))
        {
            return true;
        }

        // MASM data labels and procedures share one case-insensitive symbol
        // namespace. Every compiler-owned runtime helper uses the smile prefix.
        if (language is TargetLanguage.MasmX64 &&
            name.StartsWith("smile", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // C and Objective-C retain their implementation-reserved prefix rules.
        if (language is TargetLanguage.C or TargetLanguage.ObjectiveC &&
            IsCImplementationReservedIdentifier(name))
        {
            return true;
        }

        // C++ additionally reserves a double underscore anywhere in a name.
        if (language is TargetLanguage.Cpp && IsCppImplementationReservedIdentifier(name))
        {
            return true;
        }

        return false;
    }

    private static bool IsCImplementationReservedIdentifier(string name) =>
        name.StartsWith("__", StringComparison.Ordinal) ||
        (name.Length >= 2 && name[0] == '_' && SyntaxFacts.IsAsciiUppercaseLetter(name[1]));

    private static bool IsCppImplementationReservedIdentifier(string name) =>
        IsCImplementationReservedIdentifier(name) || name.Contains("__", StringComparison.Ordinal);

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
        // <stdint.h> and <cstdint> can expose these names as macros. Keeping the
        // family in one shared list prevents C, Objective-C, and C++ from
        // drifting when their wide Integer profiles activate those headers.
        private static readonly string[] FixedWidthIntegerMacros =
        {
            "INT8_MIN", "INT8_MAX", "UINT8_MAX",
            "INT16_MIN", "INT16_MAX", "UINT16_MAX",
            "INT32_MIN", "INT32_MAX", "UINT32_MAX",
            "INT64_MIN", "INT64_MAX", "UINT64_MAX",
            "INT_LEAST8_MIN", "INT_LEAST8_MAX", "UINT_LEAST8_MAX",
            "INT_LEAST16_MIN", "INT_LEAST16_MAX", "UINT_LEAST16_MAX",
            "INT_LEAST32_MIN", "INT_LEAST32_MAX", "UINT_LEAST32_MAX",
            "INT_LEAST64_MIN", "INT_LEAST64_MAX", "UINT_LEAST64_MAX",
            "INT_FAST8_MIN", "INT_FAST8_MAX", "UINT_FAST8_MAX",
            "INT_FAST16_MIN", "INT_FAST16_MAX", "UINT_FAST16_MAX",
            "INT_FAST32_MIN", "INT_FAST32_MAX", "UINT_FAST32_MAX",
            "INT_FAST64_MIN", "INT_FAST64_MAX", "UINT_FAST64_MAX",
            "INTPTR_MIN", "INTPTR_MAX", "UINTPTR_MAX",
            "INTMAX_MIN", "INTMAX_MAX", "UINTMAX_MAX",
            "PTRDIFF_MIN", "PTRDIFF_MAX", "SIG_ATOMIC_MIN", "SIG_ATOMIC_MAX",
            "SIZE_MAX", "WCHAR_MIN", "WCHAR_MAX", "WINT_MIN", "WINT_MAX",
            "INT8_C", "UINT8_C", "INT16_C", "UINT16_C", "INT32_C", "UINT32_C",
            "INT64_C", "UINT64_C", "INTMAX_C", "UINTMAX_C"
        };

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
            "Console", "Program", "Main", "String", "System", "_smile_condition",
            "_smile_input", "_smile_read_line", "_smile_read_byte", "_smile_fail", "_smile_add",
            "_smile_subtract", "_smile_multiply", "_smile_negate", "_smile_divide",
            "_smile_input_stream", "_smile_utf8", "_smile_pending_byte", "_smile_skip_lf",
            "_smile_open_input", "_smile_input_string", "_smile_input_integer",
            "_smile_input_boolean"
        };

        private static readonly string[] C = new[]
        {
            "auto", "break", "case", "char", "const", "continue", "default", "do", "double",
            "else", "enum", "extern", "float", "for", "goto", "if", "inline", "int", "long",
            "register", "restrict", "return", "short", "signed", "sizeof", "static", "struct",
            "switch", "typedef", "union", "unsigned", "void", "volatile", "while", "_Alignas",
            "_Alignof", "_Atomic", "_Bool", "_Complex", "_Generic", "_Imaginary", "_Noreturn",
            "_Static_assert", "_Thread_local",
            "bool", "fputc", "fputs", "fwrite", "int64_t", "main", "memcmp", "memcpy",
            "printf", "size_t", "snprintf", "stdout", "stderr", "stdin", "EOF", "strcmp", "strlen",
            "_smile_input", "_smile_read_line", "_smile_fail", "_smile_add",
            "_smile_subtract", "_smile_multiply", "_smile_negate", "_smile_divide",
            "_smile_input_string", "_smile_input_integer", "_smile_input_boolean",
            "_smile_valid_utf8", "_smile_input_error", "_smile_arithmetic_overflow",
            "_smile_arithmetic_left", "_smile_arithmetic_right"
        }
            .Concat(FixedWidthIntegerMacros)
            .ToArray();

        private static readonly string[] JavaScript =
        {
            "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
            "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for",
            "function", "if", "import", "in", "instanceof", "let", "new", "null", "return",
            "super", "switch", "this", "throw", "true", "try", "typeof", "var", "void", "while",
            "with", "yield", "arguments", "console", "eval", "_smile_input", "_smile_read_line",
            "_smile_fail", "_smile_add", "_smile_subtract", "_smile_multiply",
            "_smile_negate", "_smile_divide", "_smile_checked", "_smile_decoder",
            "_smile_input_byte", "_smile_next_byte", "_smile_pending_byte", "_smile_skip_lf",
            "_smile_input_string", "_smile_input_integer", "_smile_input_boolean",
            "require", "process", "Buffer", "TextDecoder", "BigInt", "Uint8Array", "Error", "fs"
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
            "volatile", "while", "with", "yield", "_", "System", "String", "Program", "main", "args",
            "_smile_condition", "_smile_input", "_smile_read_line", "_smile_read_byte", "_smile_fail", "_smile_add",
            "_smile_subtract", "_smile_multiply", "_smile_negate", "_smile_divide",
            "_smile_pending_byte", "_smile_skip_lf", "_smile_input_string", "_smile_input_integer",
            "_smile_input_boolean", "_smile_ascii_equals", "ByteBuffer", "CharacterCodingException",
            "CodingErrorAction", "StandardCharsets", "IOException", "Math", "Long", "Integer"
        };

        private static readonly string[] Cobol =
        {
            "accept", "add", "all", "and", "any", "by", "call", "cancel", "class", "close", "compute", "configuration",
            "copy", "column", "count", "first",
            "data", "display", "divide", "division", "else", "end", "entry", "environment", "error", "evaluate",
            "exit", "fd", "file", "from", "function", "global", "goback", "identification", "if", "in", "initialize",
            "input-output", "inspect", "into", "is", "left", "linkage", "merge", "message", "move", "multiply", "nested", "not", "number", "object",
            "negative", "of", "open", "or", "perform", "pic", "picture", "procedure", "program", "program-id", "quote", "read", "right",
            "record", "return", "rewrite", "run", "second", "section", "select", "self", "set", "sort", "source", "stop", "sum",
            "same", "string", "subtract", "super", "text", "then", "to", "type", "until", "using", "value", "when",
            "working-storage", "write",
            "Program", "SMILE-NEWLINE", "SMILE-RUNTIME-POINTER", "SMILE-RUNTIME-INTEGER",
            "SMILE-RUNTIME-INTEGER-TEXT", "SMILE-INPUT-STATUS", "SMILE-INPUT-BUFFER",
            "SPACE", "SPACES", "ZERO", "ZEROS", "ZEROES"
        };

        private static readonly string[] ObjectiveC = C
            .Concat(new[]
            {
                "Class", "Nil", "NSString", "printf", "main", "id", "self", "super", "YES", "NO", "nil", "NULL"
            })
            .ToArray();

        private static readonly string[] Cpp = new[]
        {
            "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor",
            "bool", "break", "case", "catch", "char", "char8_t", "char16_t", "char32_t",
            "class", "compl", "concept", "const", "consteval", "constexpr", "constinit",
            "const_cast", "continue", "co_await", "co_return", "co_yield", "decltype",
            "default", "delete", "do", "double", "dynamic_cast", "else", "enum", "explicit",
            "export", "extern", "false", "final", "float", "for", "friend", "goto", "if",
            "import", "inline", "int", "long", "module", "mutable", "namespace", "new",
            "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq", "override",
            "private", "protected", "public",
            "register", "reinterpret_cast", "requires", "return", "short", "signed", "sizeof",
            "static", "static_assert", "static_cast", "struct", "switch", "template", "this",
            "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
            "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor",
            "xor_eq", "atomic_cancel", "atomic_commit", "atomic_noexcept", "synchronized",
            "transaction_safe", "transaction_safe_dynamic", "std", "main", "cout", "string", "stdin", "EOF",
            "to_string", "int64_t", "smile_text", "_smile_input", "_smile_read_line",
            "_smile_fail", "_smile_add", "_smile_subtract", "_smile_multiply",
            "_smile_negate", "_smile_divide", "_smile_next_byte", "_smile_pending_byte", "_smile_skip_lf",
            "_smile_input_string", "_smile_input_integer", "_smile_input_boolean",
            "_smile_valid_utf8", "_smile_arithmetic_left_value",
            "_smile_arithmetic_right_value", "_smile_expression_left_value",
            "_smile_expression_right_value", "_smile_text_result"
        }
            .Concat(FixedWidthIntegerMacros)
            .ToArray();

        private static readonly string[] Swift =
        {
            "Any", "Self", "Type", "as", "associatedtype", "break", "case", "catch", "class",
            "continue", "default", "defer", "deinit", "do", "else", "enum", "extension", "fallthrough",
            "false", "fileprivate", "for", "func", "guard", "if", "import", "in", "init", "inout",
            "internal", "is", "let", "nil", "open", "operator", "private", "protocol", "public",
            "repeat", "return", "self", "static", "struct", "subscript", "super", "switch", "throw",
            "throws", "true", "try", "typealias", "var", "where", "while",
            "_", "async", "await", "print", "yield", "String", "_smile_condition",
            "_smile_input", "_smile_read_line", "_smile_fail", "_smile_add",
            "_smile_subtract", "_smile_multiply", "_smile_negate", "_smile_divide",
            "_smile_next_byte", "_smile_pending_byte", "_smile_skip_lf", "_smile_input_string",
            "_smile_input_integer", "_smile_input_boolean", "_smile_ascii_equals",
            "FileHandle", "Data", "Foundation", "Array", "CharacterSet", "Int", "Int64",
            "UInt8", "UTF8", "Bool", "Never", "exit"
        };

        private static readonly string[] Masm =
        {
            "add", "addr", "and", "assume", "byte", "call", "code", "data", "dword", "end", "endp",
            "equ", "extern", "lea", "main", "mov", "none", "option", "proc", "ptr", "qword", "rax",
            "rcx", "rdx", "rsp", "r8d", "r9", "xor",
            "printf", "scanf", "strcmp", "_stricmp", "ExitProcess"
        };

        private static readonly string[] Python =
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break",
            "case", "class", "continue", "def", "del", "elif", "else", "except", "finally",
            "for", "from", "global", "if", "import", "in", "is", "lambda", "match",
            "nonlocal", "not", "or", "pass", "raise", "return", "try", "while", "with",
            "yield", "print", "str", "bool", "int", "abs", "isinstance", "main",
            "_smile_text", "_smile_div", "_smile_input", "_smile_read_line", "_smile_fail",
            "_smile_add", "_smile_subtract", "_smile_multiply", "_smile_negate",
            "_smile_divide", "_smile_checked", "_smile_next_byte", "_smile_pending_byte", "_smile_skip_lf",
            "_smile_input_string", "_smile_input_integer", "_smile_input_boolean",
            "_smile_ascii_equals", "__name__", "sys", "SystemExit", "bytearray", "len",
            "any", "all", "zip", "OSError", "UnicodeError"
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
                    TargetLanguage.Python => Python,
                    TargetLanguage.Cpp => Cpp,
                    _ => Array.Empty<string>()
                },
                language is TargetLanguage.Cobol or TargetLanguage.MasmX64
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
    }
}
