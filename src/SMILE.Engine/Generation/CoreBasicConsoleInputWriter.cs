namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private void WriteConsoleKeySwitch(string value, bool characters = false)
        {
            bool swift = _language is TargetLanguage.Swift;
            Line(swift ? $"switch {value} {{" : $"switch ({value}) {{");
            foreach (var key in ConsoleKeyMap.Keys)
            {
                IEnumerable<int> values = characters ? key.Characters.Select(c => (int)c) : key.Virtual;
                if (!values.Any()) continue;
                string cases = swift || _language is TargetLanguage.Java
                    ? "case " + string.Join(", ", values)
                    : string.Join(" ", values.Select(number => $"case {number}:"));
                Line(swift ? $"    {cases}: return {key.Code}" :
                    _language is TargetLanguage.Java ? $"    {cases} -> {{ return {key.Code}; }}" : $"    {cases} return {key.Code};");
            }
            Line(swift ? "    default: break" : _language is TargetLanguage.Java ? "    default -> { }" : "    default: break;");
            Line("}");
        }

        private void WriteCSharpConsoleInput()
        {
            Lines("[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = 20)]",
                "private struct SmileInputRecord", "{",
                "    [System.Runtime.InteropServices.FieldOffset(0)] public ushort EventType;",
                "    [System.Runtime.InteropServices.FieldOffset(4)] public int KeyDown;",
                "    [System.Runtime.InteropServices.FieldOffset(10)] public ushort VirtualKey;",
                "    [System.Runtime.InteropServices.FieldOffset(14)] public ushort Character;", "}",
                "[System.Runtime.InteropServices.DllImport(\"kernel32.dll\")]",
                "private static extern nint GetStdHandle(int kind);",
                "[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", ExactSpelling = true)]",
                "private static extern bool PeekConsoleInputW(nint input, out SmileInputRecord record, uint length, out uint count);",
                "[System.Runtime.InteropServices.DllImport(\"kernel32.dll\", ExactSpelling = true)]",
                "private static extern bool ReadConsoleInputW(nint input, out SmileInputRecord record, uint length, out uint count);",
                "private static long SmileGetKey()", "{",
                "    nint input = GetStdHandle(-10);",
                "    while (PeekConsoleInputW(input, out SmileInputRecord record, 1, out uint count) && count != 0)", "    {",
                "        if (!ReadConsoleInputW(input, out record, 1, out count) || count == 0) return 0;",
                "        if (record.EventType != 1 || record.KeyDown == 0) continue;");
            _indent += 2;
            WriteConsoleKeySwitch("record.VirtualKey");
            WriteConsoleKeySwitch("record.Character", characters: true);
            Line("if (record.VirtualKey != 0 || record.Character != 0) return 19;");
            _indent -= 2;
            Lines("    }", "    return 0;", "}");
        }

        private void WriteJavaConsoleFields()
        {
            Lines("private static final Arena SMILE_ARENA = Arena.global();",
                "private static final Linker SMILE_LINKER = Linker.nativeLinker();",
                "private static final SymbolLookup SMILE_CONSOLE = SymbolLookup.libraryLookup(\"kernel32\", SMILE_ARENA);",
                "private static final MethodHandle SMILE_INPUT = SMILE_LINKER.downcallHandle(SMILE_CONSOLE.find(\"GetStdHandle\").orElseThrow(), FunctionDescriptor.of(ValueLayout.ADDRESS, ValueLayout.JAVA_INT));",
                "private static final FunctionDescriptor SMILE_READ_SIGNATURE = FunctionDescriptor.of(ValueLayout.JAVA_INT, ValueLayout.ADDRESS, ValueLayout.ADDRESS, ValueLayout.JAVA_INT, ValueLayout.ADDRESS);",
                "private static final MethodHandle SMILE_PEEK = SMILE_LINKER.downcallHandle(SMILE_CONSOLE.find(\"PeekConsoleInputW\").orElseThrow(), SMILE_READ_SIGNATURE);",
                "private static final MethodHandle SMILE_READ = SMILE_LINKER.downcallHandle(SMILE_CONSOLE.find(\"ReadConsoleInputW\").orElseThrow(), SMILE_READ_SIGNATURE);");
        }

        private void WriteJavaConsoleInput()
        {
            Lines("private static long smileGetKey()", "{", "    try (Arena arena = Arena.ofConfined())", "    {",
                "        var input = (java.lang.foreign.MemorySegment)SMILE_INPUT.invokeExact(-10);",
                "        var record = arena.allocate(20, 4);", "        var count = arena.allocate(ValueLayout.JAVA_INT);",
                "        while ((int)SMILE_PEEK.invokeExact(input, record, 1, count) != 0 && count.get(ValueLayout.JAVA_INT, 0) != 0)", "        {",
                "            if ((int)SMILE_READ.invokeExact(input, record, 1, count) == 0 || count.get(ValueLayout.JAVA_INT, 0) == 0) return 0;",
                "            if (record.get(ValueLayout.JAVA_SHORT, 0) != 1 || record.get(ValueLayout.JAVA_INT, 4) == 0) continue;",
                "            int virtualKey = Short.toUnsignedInt(record.get(ValueLayout.JAVA_SHORT, 10));",
                "            int character = Short.toUnsignedInt(record.get(ValueLayout.JAVA_SHORT, 14));");
            _indent += 3;
            WriteConsoleKeySwitch("virtualKey");
            WriteConsoleKeySwitch("character", characters: true);
            Line("if (virtualKey != 0 || character != 0) return 19;");
            _indent -= 3;
            Lines("        }", "    }", "    catch (Throwable error)", "    {", "        throw new IllegalStateException(\"Could not poll console input.\", error);", "    }", "    return 0;", "}");
        }

        private void WriteSwiftConsoleInput()
        {
            Lines("func smileGetKey() -> Int64 {", "    let input = GetStdHandle(STD_INPUT_HANDLE)",
                "    var record = INPUT_RECORD()", "    var count: DWORD = 0",
                "    while PeekConsoleInputW(input, &record, 1, &count) && count != 0 {",
                "        if !ReadConsoleInputW(input, &record, 1, &count) || count == 0 { return 0 }",
                "        if record.EventType != KEY_EVENT || !record.Event.KeyEvent.bKeyDown.boolValue { continue }",
                "        let virtualKey = record.Event.KeyEvent.wVirtualKeyCode",
                "        let character = record.Event.KeyEvent.uChar.UnicodeChar");
            _indent += 2;
            WriteConsoleKeySwitch("virtualKey");
            WriteConsoleKeySwitch("character", characters: true);
            Line("if virtualKey != 0 || character != 0 { return 19 }");
            _indent -= 2;
            Lines("    }", "    return 0", "}");
        }

        private void WritePythonConsoleInput()
        {
            Lines("smile_console = ctypes.WinDLL(\"kernel32\", use_last_error=True)",
                "smile_console.GetStdHandle.argtypes = [ctypes.c_int32]",
                "smile_console.GetStdHandle.restype = ctypes.c_void_p",
                "for smile_read in (smile_console.PeekConsoleInputW, smile_console.ReadConsoleInputW):",
                "    smile_read.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)]",
                "    smile_read.restype = ctypes.c_int32", "def smile_get_key():",
                "    input_handle = smile_console.GetStdHandle(-10)", "    record = ctypes.create_string_buffer(20)",
                "    count = ctypes.c_uint32()",
                "    while smile_console.PeekConsoleInputW(input_handle, record, 1, ctypes.byref(count)) and count.value:",
                "        if not smile_console.ReadConsoleInputW(input_handle, record, 1, ctypes.byref(count)) or not count.value:",
                "            return 0",
                "        if int.from_bytes(record.raw[0:2], 'little') != 1 or not int.from_bytes(record.raw[4:8], 'little'):",
                "            continue", "        virtual_key = int.from_bytes(record.raw[10:12], 'little')",
                "        character = int.from_bytes(record.raw[14:16], 'little')");
            foreach (bool characters in new[] { false, true })
            {
                Line($"        match {(characters ? "character" : "virtual_key")}:");
                foreach (var key in ConsoleKeyMap.Keys)
                {
                    IEnumerable<int> values = characters ? key.Characters.Select(c => (int)c) : key.Virtual;
                    if (values.Any()) Line($"            case {string.Join(" | ", values)}: return {key.Code}");
                }
            }
            Lines("        if virtual_key or character:", "            return 19", "    return 0");
        }
    }
}
