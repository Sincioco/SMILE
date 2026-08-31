using System.Globalization;

namespace SMILE.Engine;

internal static partial class CoreBasicCodeGenerator
{
    private sealed partial class StructuredWriter
    {
        private bool TryWriteTextGameStatement(BoundStatement statement)
        {
            switch (statement)
            {
                case BoundGetKeyStatement getKey:
                    WriteSimpleAssignment(Name(getKey.Target), RuntimeCall("get_key"));
                    return true;
                case BoundClearScreenStatement:
                    Line(RuntimeStatement("clear_screen"));
                    return true;
                case BoundWaitStatement wait:
                    Line(RuntimeStatement("wait", PreparedExpression(wait.Duration)));
                    return true;
                case BoundRandomStatement random:
                    WriteRandom(random);
                    return true;
                default:
                    return false;
            }
        }

        private void WriteRandom(BoundRandomStatement random)
        {
            string lower = PreparedExpression(random.LowerBound);
            string upper = PreparedExpression(random.UpperBound);
            int id = ++_orderedTempId;
            string lowerName = $"_smileRandomLower{id}";
            string upperName = $"_smileRandomUpper{id}";
            WriteNumberTemporary(lowerName, lower);
            WriteNumberTemporary(upperName, upper);
            WriteSimpleAssignment(Name(random.Target), RuntimeCall("random", lowerName, upperName));
        }

        private void WriteNumberTemporary(string name, string expression)
        {
            Line(_language switch
            {
                TargetLanguage.CSharp or TargetLanguage.Java => $"long {name} = {expression};",
                TargetLanguage.C or TargetLanguage.ObjectiveC => $"int64_t {name} = {expression};",
                TargetLanguage.Cpp => $"std::int64_t {name} = {expression};",
                TargetLanguage.JavaScript => $"const {name} = {expression};",
                TargetLanguage.Swift => $"let {name}: Int64 = {expression}",
                TargetLanguage.Python => $"{name} = {expression}",
                _ => string.Empty
            });
        }

        private void WriteSimpleAssignment(string target, string expression) =>
            Line(_language is TargetLanguage.Swift or TargetLanguage.Python
                ? $"{target} = {expression}"
                : $"{target} = {expression};");

        private string RuntimeCall(string operation, params string[] arguments)
        {
            string name = (_language, operation) switch
            {
                (TargetLanguage.CSharp, "get_key") => "SmileGetKey",
                (TargetLanguage.CSharp, "clear_screen") => "SmileClearScreen",
                (TargetLanguage.CSharp, "wait") => "SmileWait",
                (TargetLanguage.CSharp, "random") => "SmileRandom",
                (TargetLanguage.JavaScript, "get_key") => "smileGetKey",
                (TargetLanguage.JavaScript, "clear_screen") => "smileClearScreen",
                (TargetLanguage.JavaScript, "wait") => "smileWait",
                (TargetLanguage.JavaScript, "random") => "smileRandom",
                (TargetLanguage.Java, "get_key") => "smileGetKey",
                (TargetLanguage.Java, "clear_screen") => "smileClearScreen",
                (TargetLanguage.Java, "wait") => "smileWait",
                (TargetLanguage.Java, "random") => "smileRandom",
                (TargetLanguage.Swift, "get_key") => "smileGetKey",
                (TargetLanguage.Swift, "clear_screen") => "smileClearScreen",
                (TargetLanguage.Swift, "wait") => "smileWait",
                (TargetLanguage.Swift, "random") => "smileRandom",
                (TargetLanguage.Python, "get_key") => "smile_get_key",
                (TargetLanguage.Python, "clear_screen") => "smile_clear_screen",
                (TargetLanguage.Python, "wait") => "smile_wait",
                (TargetLanguage.Python, "random") => "smile_random",
                (_, "get_key") => "smile_get_key",
                (_, "clear_screen") => "smile_clear_screen",
                (_, "wait") => "smile_wait",
                (_, "random") => "smile_random",
                _ => throw new InvalidOperationException(operation)
            };
            string invocation = $"{name}({string.Join(", ", arguments)})";
            return _language is TargetLanguage.JavaScript && operation == "wait"
                ? "await " + invocation
                : invocation;
        }

        private string RuntimeStatement(string operation, params string[] arguments)
        {
            string invocation = RuntimeCall(operation, arguments);
            return _language is TargetLanguage.Swift or TargetLanguage.Python
                ? invocation
                : invocation + ";";
        }

        private static HashSet<RoutineSymbol> FindAsyncJavaScriptRoutines(BoundProgram program)
        {
            var asyncRoutines = program.Routines
                .Where(routine => EnumerateStatements(routine.SourceItems).Any(statement => statement is BoundWaitStatement))
                .Select(routine => routine.Symbol)
                .ToHashSet();
            bool changed;
            do
            {
                changed = false;
                foreach (BoundRoutineDeclaration routine in program.Routines)
                {
                    if (asyncRoutines.Contains(routine.Symbol))
                    {
                        continue;
                    }

                    bool callsAsync = EnumerateExpressions(routine.SourceItems)
                        .OfType<BoundCallExpression>()
                        .Any(call => asyncRoutines.Contains(call.Routine)) ||
                        EnumerateStatements(routine.SourceItems)
                            .OfType<BoundCallStatement>()
                            .Any(call => asyncRoutines.Contains(call.Routine));
                    if (callsAsync)
                    {
                        asyncRoutines.Add(routine.Symbol);
                        changed = true;
                    }
                }
            }
            while (changed);

            return asyncRoutines;
        }

        private void WriteRuntimePreamble()
        {
            switch (_language)
            {
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC:
                case TargetLanguage.Cpp:
                    if (_features.HasGetKey)
                    {
                        if (_language is TargetLanguage.Cpp)
                        {
                            Line("#define NOMINMAX");
                        }
                        Line("#include <conio.h>");
                    }
                    if (_language is not TargetLanguage.Cpp && _features.HasConsoleRuntime ||
                        _language is TargetLanguage.Cpp && _features.HasClearScreen)
                    {
                        Line("#include <windows.h>");
                    }
                    if ((_features.HasAbs || _features.HasRandom) && _language is TargetLanguage.Cpp)
                    {
                        if (_features.HasAbs) Line("#include <limits>");
                        Line("#include <stdexcept>");
                    }
                    if ((_features.HasMin || _features.HasMax || _features.HasWait) &&
                        _language is TargetLanguage.Cpp)
                    {
                        Line("#include <algorithm>");
                    }
                    if (_language is TargetLanguage.Cpp && (_features.HasWait || _features.HasTimer)) Line("#include <chrono>");
                    if (_language is TargetLanguage.Cpp && _features.HasRandom) Line("#include <random>");
                    if (_language is TargetLanguage.Cpp && _features.HasWait) Line("#include <thread>");
                    break;
                case TargetLanguage.JavaScript:
                    if (_features.HasRandom)
                    {
                        Line("const { randomBytes } = require(\"node:crypto\");");
                    }
                    break;
                case TargetLanguage.Java:
                    if (_features.HasGetKey)
                    {
                        Line("import java.lang.foreign.Arena;");
                        Line("import java.lang.foreign.FunctionDescriptor;");
                        Line("import java.lang.foreign.Linker;");
                        Line("import java.lang.foreign.SymbolLookup;");
                        Line("import java.lang.foreign.ValueLayout;");
                        Line("import java.lang.invoke.MethodHandle;");
                    }
                    break;
                case TargetLanguage.Swift:
                    if (_features.HasWait || _features.HasTimer)
                    {
                        Line("import Foundation");
                    }
                    if (_features.HasClearScreen)
                    {
                        Line("import WinSDK");
                    }
                    break;
                case TargetLanguage.Python:
                    if (_features.HasGetKey) Line("import msvcrt");
                    if (_features.HasClearScreen) Line("import sys");
                    if (_features.HasRandom) Line("import random");
                    if (_features.HasWait || _features.HasTimer) Line("import time");
                    break;
            }
        }

        private void WriteRuntimeHelpers()
        {
            if (!_features.HasConsoleRuntime && !_features.HasAbs && !_features.HasMin && !_features.HasMax)
            {
                return;
            }

            if (_layout.HasContent)
            {
                Line();
            }
            switch (_language)
            {
                case TargetLanguage.CSharp: WriteCSharpRuntimeHelpers(); break;
                case TargetLanguage.C:
                case TargetLanguage.ObjectiveC: WriteCRuntimeHelpers(); break;
                case TargetLanguage.Cpp: WriteCppRuntimeHelpers(); break;
                case TargetLanguage.JavaScript: WriteJavaScriptRuntimeHelpers(); break;
                case TargetLanguage.Java: WriteJavaRuntimeHelpers(); break;
                case TargetLanguage.Swift: WriteSwiftRuntimeHelpers(); break;
                case TargetLanguage.Python: WritePythonRuntimeHelpers(); break;
            }
        }

        private void WriteHelperPrototypes()
        {
            if (_language is not (TargetLanguage.C or TargetLanguage.ObjectiveC or TargetLanguage.Cpp))
            {
                return;
            }

            if (ProgramHasArrays())
            {
                Line(_language is TargetLanguage.Cpp
                    ? "static std::size_t smile_index(std::int64_t index, std::size_t length, const std::string& name);"
                    : "static size_t smile_index(int64_t index, size_t length, const char *name);");
            }
            if (ProgramHasTextConcatenation())
            {
                Line("static const char *smile_text_return_root = NULL;");
                Line("static void smile_text_initialize(void);");
                Line("static void smile_text_register(const char **root);");
                Line("static void smile_text_unregister(const char **root);");
                Line("static void smile_text_collect(void);");
                Line("static void smile_text_shutdown(void);");
                Line("static const char *smile_text_concat(const char *left, const char *right);");
            }
            string integer = _language is TargetLanguage.Cpp ? "std::int64_t" : "int64_t";
            if (_features.HasGetKey) Line($"static {integer} smile_get_key(void);");
            if (_features.HasClearScreen) Line("static void smile_clear_screen(void);");
            if (_features.HasWait) Line($"static void smile_wait({integer} milliseconds);");
            if (_features.HasTimer) Line($"static {integer} smile_timer(void);");
            if (_features.HasRandom) Line($"static {integer} smile_random({integer} lower, {integer} upper);");
            if (_features.HasAbs) Line($"static {integer} smile_abs({integer} value);");
            if (_features.HasMin && _language is not TargetLanguage.Cpp) Line("static int64_t smile_min(int64_t left, int64_t right);");
            if (_features.HasMax && _language is not TargetLanguage.Cpp) Line("static int64_t smile_max(int64_t left, int64_t right);");
            if (ProgramHasArrays() || ProgramHasTextConcatenation() || _features.HasConsoleRuntime || _features.HasAbs || _features.HasMin || _features.HasMax)
            {
                Line();
            }
        }

        private void WriteCSharpRuntimeHelpers()
        {
            if (_features.HasGetKey)
            {
                Lines("private static long SmileGetKey()", "{", "    try", "    {", "        if (!Console.KeyAvailable)", "        {", "            return 0;", "        }", "        ConsoleKeyInfo key = Console.ReadKey(intercept: true);", "        return key.Key switch", "        {", "            ConsoleKey.W => 1,", "            ConsoleKey.A => 2,", "            ConsoleKey.S => 3,", "            ConsoleKey.D => 4,", "            ConsoleKey.UpArrow => 10,", "            ConsoleKey.DownArrow => 11,", "            ConsoleKey.LeftArrow => 12,", "            ConsoleKey.RightArrow => 13,", "            ConsoleKey.Enter => 14,", "            ConsoleKey.Escape => 15,", "            ConsoleKey.Spacebar => 16,", "            ConsoleKey.D1 or ConsoleKey.NumPad1 => 17,", "            ConsoleKey.D2 or ConsoleKey.NumPad2 => 18,", "            ConsoleKey.D3 or ConsoleKey.NumPad3 => 20,", "            ConsoleKey.Tab => 21,", "            ConsoleKey.D4 or ConsoleKey.NumPad4 => 22,", "            _ => 19", "        };", "    }", "    catch (InvalidOperationException)", "    {", "        return 0;", "    }", "}");
            }
            if (_features.HasClearScreen)
            {
                Lines("private static void SmileClearScreen()", "{", "    if (!Console.IsOutputRedirected)", "    {", "        Console.Clear();", "    }", "}");
            }
            if (_features.HasWait)
            {
                Lines("private static void SmileWait(long milliseconds)", "{", "    long remaining = Math.Clamp(milliseconds, 0, uint.MaxValue);", "    while (remaining > int.MaxValue)", "    {", "        System.Threading.Thread.Sleep(int.MaxValue);", "        remaining -= int.MaxValue;", "    }", "    System.Threading.Thread.Sleep((int)remaining);", "}");
            }
            if (_features.HasTimer)
            {
                Lines("private static long SmileTimer() => Environment.TickCount64;");
            }
            if (_features.HasRandom)
            {
                Lines("private static long SmileRandom(long lower, long upper)", "{", "    if (lower > upper)", "    {", "        return lower;", "    }", "    ulong range = unchecked((ulong)upper - (ulong)lower + 1UL);", "    ulong sample;", "    Span<byte> bytes = stackalloc byte[8];", "    do", "    {", "        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);", "        sample = BitConverter.ToUInt64(bytes);", "    } while (range != 0 && sample < unchecked(0UL - range) % range);", "    if (range != 0)", "    {", "        sample %= range;", "    }", "    return unchecked((long)(unchecked((ulong)lower) + sample));", "}");
            }
        }

        private void WriteCRuntimeHelpers()
        {
            if (_features.HasGetKey) WriteCGetKeyHelper();
            if (_features.HasClearScreen) WriteCClearHelper();
            if (_features.HasWait) Lines("static void smile_wait(int64_t milliseconds)", "{", "    DWORD normalized = milliseconds <= 0", "        ? 0", "        : milliseconds > UINT32_MAX ? UINT32_MAX : (DWORD)milliseconds;", "    Sleep(normalized);", "}");
            if (_features.HasTimer) Lines("static int64_t smile_timer(void)", "{", "    return (int64_t)GetTickCount64();", "}");
            if (_features.HasRandom) WriteCRandomHelper();
            if (_features.HasAbs) Lines("static int64_t smile_abs(int64_t value)", "{", "    if (value == INT64_MIN)", "    {", "        fputs(\"SMILE Runtime Error SMILER1206: Number arithmetic overflow.\\n\", stderr);", "        exit(1);", "    }", "    return value < 0 ? -value : value;", "}");
            if (_features.HasMin) Lines("static int64_t smile_min(int64_t left, int64_t right) { return left < right ? left : right; }");
            if (_features.HasMax) Lines("static int64_t smile_max(int64_t left, int64_t right) { return left > right ? left : right; }");
        }

        private void WriteCppRuntimeHelpers()
        {
            if (_features.HasGetKey) WriteCGetKeyHelper();
            if (_features.HasClearScreen) WriteCClearHelper();
            if (_features.HasWait) Lines("static void smile_wait(std::int64_t milliseconds)", "{", "    const std::int64_t normalized = std::clamp<std::int64_t>(milliseconds, 0, UINT32_MAX);", "    std::this_thread::sleep_for(std::chrono::milliseconds(normalized));", "}");
            if (_features.HasTimer) Lines("static std::int64_t smile_timer()", "{", "    return std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();", "}");
            if (_features.HasRandom) Lines("static std::int64_t smile_random(std::int64_t lower, std::int64_t upper)", "{", "    if (lower > upper)", "    {", "        return lower;", "    }", "    static std::mt19937_64 engine(std::random_device{}());", "    return std::uniform_int_distribution<std::int64_t>(lower, upper)(engine);", "}");
            if (_features.HasAbs) Lines("static std::int64_t smile_abs(std::int64_t value)", "{", "    if (value == std::numeric_limits<std::int64_t>::min())", "    {", "        throw std::overflow_error(\"SMILE Runtime Error SMILER1206: Number arithmetic overflow.\");", "    }", "    return value < 0 ? -value : value;", "}");
        }

        private void WriteCGetKeyHelper()
        {
            Lines("static int64_t smile_get_key(void)", "{", "    if (!_kbhit())", "    {", "        return 0;", "    }", "    int key = _getch();", "    if (key == 0 || key == 224)", "    {", "        if (!_kbhit())", "        {", "            return 19;", "        }", "        key = _getch();", "        switch (key)", "        {", "            case 72: return 10;", "            case 80: return 11;", "            case 75: return 12;", "            case 77: return 13;", "            default: return 19;", "        }", "    }", "    switch (key)", "    {", "        case 'w': case 'W': return 1;", "        case 'a': case 'A': return 2;", "        case 's': case 'S': return 3;", "        case 'd': case 'D': return 4;", "        case 13: return 14;", "        case 27: return 15;", "        case ' ': return 16;", "        case '1': return 17;", "        case '2': return 18;", "        case '3': return 20;", "        case 9: return 21;", "        case '4': return 22;", "        default: return 19;", "    }", "}");
        }

        private void WriteCClearHelper()
        {
            Lines("static void smile_clear_screen(void)", "{", "    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);", "    CONSOLE_SCREEN_BUFFER_INFO info;", "    if (output == INVALID_HANDLE_VALUE || !GetConsoleScreenBufferInfo(output, &info))", "    {", "        return;", "    }", "    COORD origin = {0, 0};", "    DWORD cells = (DWORD)info.dwSize.X * (DWORD)(info.srWindow.Bottom - info.srWindow.Top + 1);", "    DWORD written;", "    FillConsoleOutputCharacterA(output, ' ', cells, origin, &written);", "    FillConsoleOutputAttribute(output, info.wAttributes, cells, origin, &written);", "    SetConsoleCursorPosition(output, origin);", "}");
        }

        private void WriteCRandomHelper(bool cpp = false)
        {
            string u64 = cpp ? "std::uint64_t" : "uint64_t";
            string i64 = cpp ? "std::int64_t" : "int64_t";
            Lines($"static {u64} smile_random_state = 0;", $"static {u64} smile_random_bits(void)", "{", "    if (smile_random_state == 0) smile_random_state = ((uint64_t)GetTickCount64() << 1) ^ (uint64_t)(uintptr_t)&smile_random_state ^ UINT64_C(0x9E3779B97F4A7C15);", "    smile_random_state ^= smile_random_state >> 12;", "    smile_random_state ^= smile_random_state << 25;", "    smile_random_state ^= smile_random_state >> 27;", "    return smile_random_state * UINT64_C(2685821657736338717);", "}", $"static {i64} smile_random({i64} lower, {i64} upper)", "{", "    if (lower > upper)", "    {", "        return lower;", "    }", $"    {u64} range = ({u64})upper - ({u64})lower + 1;", $"    {u64} sample, threshold = range == 0 ? 0 : (0 - range) % range;", "    do", "    {", "        sample = smile_random_bits();", "    } while (sample < threshold);", "    if (range != 0) sample %= range;", $"    return ({i64})(({u64})lower + sample);", "}");
        }

        private void WriteJavaScriptRuntimeHelpers()
        {
            if (_features.HasGetKey)
            {
                Lines(
                    "const smileKeyQueue = [];",
                    "const smileKeyMap = new Map([",
                    "    [\"w\", 1n], [\"W\", 1n], [\"a\", 2n], [\"A\", 2n],",
                    "    [\"s\", 3n], [\"S\", 3n], [\"d\", 4n], [\"D\", 4n],",
                    "    [\"\\r\", 14n], [\"\\n\", 14n], [\"\\u001b\", 15n], [\" \", 16n],",
                    "    [\"1\", 17n], [\"2\", 18n], [\"3\", 20n], [\"\\t\", 21n], [\"4\", 22n]",
                    "]);",
                    "let smileInputStarted = false;");
                Lines(
                    "function smileStartInput() {",
                    "    if (smileInputStarted || !process.stdin.isTTY) {",
                    "        return;",
                    "    }",
                    "    smileInputStarted = true;",
                    "    process.stdin.setRawMode(true);",
                    "    process.stdin.resume();",
                    "    process.stdin.on(\"data\", data => {",
                    "        for (let index = 0; index < data.length; index++) {",
                    "            if (data[index] === 27 && data[index + 1] === 91 && index + 2 < data.length) {",
                    "                const arrows = { 65: 10n, 66: 11n, 68: 12n, 67: 13n };",
                    "                smileKeyQueue.push(arrows[data[index + 2]] ?? 19n);",
                    "                index += 2;",
                    "            } else {",
                    "                const key = String.fromCharCode(data[index]);",
                    "                smileKeyQueue.push(smileKeyMap.get(key) ?? 19n);",
                    "            }",
                    "        }",
                    "    });",
                    "}");
                Lines(
                    "function smileGetKey() {",
                    "    smileStartInput();",
                    "    return smileKeyQueue.shift() ?? 0n;",
                    "}");
                Lines(
                    "function smileCleanup() {",
                    "    if (!smileInputStarted || !process.stdin.isTTY) {",
                    "        return;",
                    "    }",
                    "    process.stdin.setRawMode(false);",
                    "    process.stdin.pause();",
                    "    smileInputStarted = false;",
                    "}");
            }
            if (_features.HasClearScreen) Lines("function smileClearScreen() {", "    if (process.stdout.isTTY) {", "        process.stdout.write(\"\\u001b[2J\\u001b[H\");", "    }", "}");
            if (_features.HasWait) Lines("async function smileWait(milliseconds) {", "    let remaining = milliseconds < 0n ? 0n : milliseconds > 4294967295n ? 4294967295n : milliseconds;", "    while (remaining > 0n) {", "        const part = remaining > 2147483647n ? 2147483647n : remaining;", "        await new Promise(resolve => setTimeout(resolve, Number(part)));", "        remaining -= part;", "    }", "}");
            if (_features.HasTimer) Lines("function smileTimer() {", "    return process.hrtime.bigint() / 1000000n;", "}");
            if (_features.HasRandom) Lines("function smileRandom(lower, upper) {", "    if (lower > upper) {", "        return lower;", "    }", "    const modulus = 1n << 64n, range = BigInt.asUintN(64, upper - lower + 1n);", "    let sample, threshold = range === 0n ? 0n : (modulus - range) % range;", "    do { sample = randomBytes(8).readBigUInt64LE(); } while (sample < threshold);", "    if (range !== 0n) sample %= range;", "    return BigInt.asIntN(64, BigInt.asUintN(64, lower) + sample);", "}");
            if (_features.HasAbs) Lines("function smileAbs(value) {", "    if (value === -(1n << 63n)) {", "        throw new RangeError(\"SMILE Runtime Error SMILER1206: Number arithmetic overflow.\");", "    }", "    return value < 0n ? -value : value;", "}");
            if (_features.HasMin) Lines("function smileMin(left, right) {", "    return left < right ? left : right;", "}");
            if (_features.HasMax) Lines("function smileMax(left, right) {", "    return left > right ? left : right;", "}");
        }

        private void WriteJavaRuntimeHelpers()
        {
            if (_features.HasGetKey) Lines("private static long smileGetKey()", "{", "    try", "    {", "        if ((int)SMILE_KBHIT.invokeExact() == 0)", "        {", "            return 0;", "        }", "        int key = (int)SMILE_GETWCH.invokeExact();", "        if (key == 0 || key == 224)", "        {", "            key = (int)SMILE_GETWCH.invokeExact();", "            return switch (key)", "            {", "                case 72 -> 10;", "                case 80 -> 11;", "                case 75 -> 12;", "                case 77 -> 13;", "                default -> 19;", "            };", "        }", "        return switch (key)", "        {", "            case 'w', 'W' -> 1;", "            case 'a', 'A' -> 2;", "            case 's', 'S' -> 3;", "            case 'd', 'D' -> 4;", "            case 13 -> 14;", "            case 27 -> 15;", "            case 32 -> 16;", "            case '1' -> 17;", "            case '2' -> 18;", "            case '3' -> 20;", "            case 9 -> 21;", "            case '4' -> 22;", "            default -> 19;", "        };", "    }", "    catch (Throwable error)", "    {", "        return 0;", "    }", "}");
            if (_features.HasClearScreen) Lines("private static void smileClearScreen()", "{", "    if (System.console() != null)", "    {", "        System.out.print(\"\\033[2J\\033[H\");", "        System.out.flush();", "    }", "}");
            if (_features.HasWait) Lines("private static void smileWait(long milliseconds)", "{", "    long normalized = Math.clamp(milliseconds, 0, 4_294_967_295L);", "    try", "    {", "        Thread.sleep(normalized);", "    }", "    catch (InterruptedException interrupted)", "    {", "        Thread.currentThread().interrupt();", "    }", "}");
            if (_features.HasTimer) Lines("private static long smileTimer()", "{", "    return System.nanoTime() / 1_000_000L;", "}");
            if (_features.HasRandom) Lines("private static long smileRandom(long lower, long upper)", "{", "    if (lower > upper)", "    {", "        return lower;", "    }", "    long range = upper - lower + 1, sample, threshold = range == 0 ? 0 : Long.remainderUnsigned(-range, range);", "    do { sample = java.util.concurrent.ThreadLocalRandom.current().nextLong(); } while (Long.compareUnsigned(sample, threshold) < 0);", "    if (range != 0) sample = Long.remainderUnsigned(sample, range);", "    return lower + sample;", "}");
        }

        private void WriteJavaRuntimeFields()
        {
            if (!_features.HasGetKey)
            {
                return;
            }

            Line("private static final Arena SMILE_ARENA = Arena.global();");
            Line("private static final Linker SMILE_LINKER = Linker.nativeLinker();");
            Line("private static final SymbolLookup SMILE_CRT = SymbolLookup.libraryLookup(\"ucrtbase\", SMILE_ARENA);");
            Line("private static final MethodHandle SMILE_KBHIT = SMILE_LINKER.downcallHandle(SMILE_CRT.find(\"_kbhit\").orElseThrow(), FunctionDescriptor.of(ValueLayout.JAVA_INT));");
            Line("private static final MethodHandle SMILE_GETWCH = SMILE_LINKER.downcallHandle(SMILE_CRT.find(\"_getwch\").orElseThrow(), FunctionDescriptor.of(ValueLayout.JAVA_INT));");
            _layout.EnsureBlankLines(_language is TargetLanguage.Python ? 2 : 1);
        }

        private void WriteSwiftRuntimeHelpers()
        {
            if (_features.HasGetKey) Lines("@_silgen_name(\"_kbhit\") func _kbhit() -> Int32", "@_silgen_name(\"_getch\") func _getch() -> Int32", "func smileGetKey() -> Int64 {", "    if _kbhit() == 0 {", "        return 0", "    }", "    var key = _getch()", "    if key == 0 || key == 224 {", "        if _kbhit() == 0 {", "            return 19", "        }", "        key = _getch()", "        switch key {", "        case 72: return 10", "        case 80: return 11", "        case 75: return 12", "        case 77: return 13", "        default: return 19", "        }", "    }", "    switch key {", "    case 119, 87: return 1", "    case 97, 65: return 2", "    case 115, 83: return 3", "    case 100, 68: return 4", "    case 13: return 14", "    case 27: return 15", "    case 32: return 16", "    case 49: return 17", "    case 50: return 18", "    case 51: return 20", "    case 9: return 21", "    case 52: return 22", "    default: return 19", "    }", "}");
            if (_features.HasClearScreen) Lines("func smileClearScreen() {", "    let output = GetStdHandle(STD_OUTPUT_HANDLE)", "    var mode: DWORD = 0", "    if output == INVALID_HANDLE_VALUE || !GetConsoleMode(output, &mode) {", "        return", "    }", "    print(\"\\u{001B}[2J\\u{001B}[H\", terminator: \"\")", "}");
            if (_features.HasWait) Lines("func smileWait(_ milliseconds: Int64) {", "    let normalized = min(max(milliseconds, 0), 4_294_967_295)", "    Thread.sleep(forTimeInterval: Double(normalized) / 1000.0)", "}");
            if (_features.HasTimer) Lines("func smileTimer() -> Int64 {", "    Int64(ProcessInfo.processInfo.systemUptime * 1000.0)", "}");
            if (_features.HasRandom) Lines("func smileRandom(_ lower: Int64, _ upper: Int64) -> Int64 {", "    if lower > upper {", "        return lower", "    }", "    return Int64.random(in: lower...upper)", "}");
        }

        private void WritePythonRuntimeHelpers()
        {
            if (_features.HasGetKey) Lines("smile_extended_key_pending = False", "smile_key_map = {", "    'w': 1, 'W': 1, 'a': 2, 'A': 2,", "    's': 3, 'S': 3, 'd': 4, 'D': 4,", "    '\\r': 14, '\\n': 14, '\\x1b': 15, ' ': 16,", "    '1': 17, '2': 18, '3': 20, '\\t': 21, '4': 22,", "}", "smile_arrow_map = {'H': 10, 'P': 11, 'K': 12, 'M': 13}", "def smile_get_key():", "    global smile_extended_key_pending", "    if smile_extended_key_pending:", "        if not msvcrt.kbhit():", "            return 0", "        smile_extended_key_pending = False", "        return smile_arrow_map.get(msvcrt.getwch(), 19)", "    if not msvcrt.kbhit():", "        return 0", "    key = msvcrt.getwch()", "    if key in ('\\x00', '\\xe0'):", "        if not msvcrt.kbhit():", "            smile_extended_key_pending = True", "            return 0", "        return smile_arrow_map.get(msvcrt.getwch(), 19)", "    return smile_key_map.get(key, 19)");
            if (_features.HasClearScreen) Lines("def smile_clear_screen():", "    if sys.stdout.isatty():", "        print('\\x1b[2J\\x1b[H', end='', flush=True)");
            if (_features.HasWait) Lines("def smile_wait(milliseconds):", "    normalized = min(max(milliseconds, 0), 4_294_967_295)", "    time.sleep(normalized / 1000)");
            if (_features.HasTimer) Lines("def smile_timer():", "    return time.monotonic_ns() // 1_000_000");
            if (_features.HasRandom) Lines("def smile_random(lower, upper):", "    if lower > upper:", "        return lower", "    return random.randint(lower, upper)");
        }

        private void Lines(params string[] lines)
        {
            foreach (string line in lines)
            {
                int spaces = line.TakeWhile(character => character == ' ').Count();
                int prior = _indent;
                _indent += spaces / 4;
                Line(line[spaces..]);
                _indent = prior;
            }
            _layout.EnsureBlankLines(_language is TargetLanguage.Python ? 2 : 1);
        }
    }
}
