using System.Text;

namespace SMILE.Engine;

// GnuCOBOL's normal C interoperability supplies the few Windows primitives
// that COBOL itself does not expose portably. The file is emitted only when a
// used text-game primitive needs it and contains no learner/game behavior.
internal static class CoreBasicCobolRuntimeSupport
{
    public static string? Generate(BoundProgram program)
    {
        CoreBasicProgramFeatureSet features = CoreBasicProgramFeatureSet.Create(program);
        if (!features.HasConsoleRuntime && !features.HasAbs && !features.HasMin && !features.HasMax)
        {
            return null;
        }

        var text = new StringBuilder();
        if (features.HasGetKey) text.AppendLine("#include <conio.h>");
        text.AppendLine("#include <stdint.h>");
        if (features.HasAbs)
        {
            text.AppendLine("#include <stdio.h>");
            text.AppendLine("#include <stdlib.h>");
        }
        if (features.HasClearScreen || features.HasWait || features.HasTimer || features.HasRandom)
        {
            text.AppendLine("#include <windows.h>");
        }
        text.AppendLine();

        if (features.HasGetKey)
        {
            text.AppendLine("int64_t smile_get_key_cobol(void)");
            text.AppendLine("{");
            text.AppendLine("    if (!_kbhit()) return 0;");
            text.AppendLine("    int key = _getch();");
            text.AppendLine("    if (key == 0 || key == 224)");
            text.AppendLine("    {");
            text.AppendLine("        if (!_kbhit()) return 19;");
            text.AppendLine("        key = _getch();");
            text.AppendLine("        switch (key) { case 72: return 10; case 80: return 11; case 75: return 12; case 77: return 13; default: return 19; }");
            text.AppendLine("    }");
            text.AppendLine("    switch (key)");
            text.AppendLine("    {");
            text.AppendLine("        case 'w': case 'W': return 1; case 'a': case 'A': return 2; case 's': case 'S': return 3; case 'd': case 'D': return 4;");
            text.AppendLine("        case 13: return 14; case 27: return 15; case ' ': return 16; case '1': return 17; case '2': return 18;");
            text.AppendLine("        case '3': return 20; case 9: return 21; case '4': return 22; default: return 19;");
            text.AppendLine("    }");
            text.AppendLine("}");
            text.AppendLine();
        }

        if (features.HasClearScreen)
        {
            text.AppendLine("int smile_clear_screen_cobol(void)");
            text.AppendLine("{");
            text.AppendLine("    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);");
            text.AppendLine("    CONSOLE_SCREEN_BUFFER_INFO info;");
            text.AppendLine("    if (output == INVALID_HANDLE_VALUE || !GetConsoleScreenBufferInfo(output, &info)) return 0;");
            text.AppendLine("    COORD origin = {0, 0};");
            text.AppendLine("    DWORD cells = (DWORD)info.dwSize.X * (DWORD)(info.srWindow.Bottom - info.srWindow.Top + 1);");
            text.AppendLine("    DWORD written;");
            text.AppendLine("    FillConsoleOutputCharacterA(output, ' ', cells, origin, &written);");
            text.AppendLine("    FillConsoleOutputAttribute(output, info.wAttributes, cells, origin, &written);");
            text.AppendLine("    SetConsoleCursorPosition(output, origin);");
            text.AppendLine("    return 0;");
            text.AppendLine("}");
            text.AppendLine();
        }

        if (features.HasWait)
        {
            text.AppendLine("int smile_wait_cobol(const int64_t *milliseconds)");
            text.AppendLine("{");
            text.AppendLine("    DWORD normalized = *milliseconds <= 0");
            text.AppendLine("        ? 0");
            text.AppendLine("        : *milliseconds > UINT32_MAX ? UINT32_MAX : (DWORD)*milliseconds;");
            text.AppendLine("    Sleep(normalized);");
            text.AppendLine("    return 0;");
            text.AppendLine("}");
            text.AppendLine();
        }

        if (features.HasTimer)
        {
            text.AppendLine("int64_t smile_timer_cobol(void) { return (int64_t)GetTickCount64(); }");
            text.AppendLine();
        }

        if (features.HasRandom)
        {
            text.AppendLine("static uint64_t smile_random_state = 0;");
            text.AppendLine("static uint64_t smile_random_bits(void)");
            text.AppendLine("{");
            text.AppendLine("    if (smile_random_state == 0) smile_random_state = ((uint64_t)GetTickCount64() << 1) ^ (uint64_t)(uintptr_t)&smile_random_state ^ UINT64_C(0x9E3779B97F4A7C15);");
            text.AppendLine("    smile_random_state ^= smile_random_state >> 12; smile_random_state ^= smile_random_state << 25; smile_random_state ^= smile_random_state >> 27;");
            text.AppendLine("    return smile_random_state * UINT64_C(2685821657736338717);");
            text.AppendLine("}");
            text.AppendLine("int64_t smile_random_cobol(const int64_t *lower, const int64_t *upper)");
            text.AppendLine("{");
            text.AppendLine("    if (*lower > *upper) return *lower;");
            text.AppendLine("    uint64_t range = (uint64_t)*upper - (uint64_t)*lower + 1;");
            text.AppendLine("    uint64_t sample, threshold = range == 0 ? 0 : (0 - range) % range;");
            text.AppendLine("    do { sample = smile_random_bits(); } while (sample < threshold);");
            text.AppendLine("    if (range != 0) sample %= range;");
            text.AppendLine("    return (int64_t)((uint64_t)*lower + sample);");
            text.AppendLine("}");
            text.AppendLine();
        }

        if (features.HasAbs)
        {
            text.AppendLine("int64_t smile_abs_cobol(const int64_t *value)");
            text.AppendLine("{");
            text.AppendLine("    if (*value == INT64_MIN) { fputs(\"SMILE Runtime Error SMILER1206: Number arithmetic overflow.\\n\", stderr); exit(1); }");
            text.AppendLine("    return *value < 0 ? -*value : *value;");
            text.AppendLine("}");
            text.AppendLine();
        }
        if (features.HasMin) text.AppendLine("int64_t smile_min_cobol(const int64_t *left, const int64_t *right) { return *left < *right ? *left : *right; }");
        if (features.HasMax) text.AppendLine("int64_t smile_max_cobol(const int64_t *left, const int64_t *right) { return *left > *right ? *left : *right; }");

        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
