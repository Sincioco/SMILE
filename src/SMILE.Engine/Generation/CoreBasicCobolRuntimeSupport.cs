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
        var numeric = new DoubleProgramFeatures(program);
        if (program.ClassTypes.Count == 0 && !features.HasConsoleRuntime && !features.HasAbs && !features.HasMin && !features.HasMax && !features.HasTextInspection && !numeric.NeedsCobolRuntime && !features.HasTextFileLoad && !features.HasNumberPersistence && !features.HasDataPersistence)
        {
            return null;
        }

        var text = new StringBuilder();
        if (program.ClassTypes.Count > 0)
        {
            text.AppendLine(NativeClassSupport.Definition);
            text.AppendLine("void *smile_object_allocate_cobol(size_t bytes) { return smile_object_allocate(bytes, NULL); }");
        }

        text.AppendLine("#include <stdint.h>");
        if (features.HasTextInspection) text.AppendLine("#include <string.h>");
        if (features.HasAbs)
        {
            text.AppendLine("#include <stdio.h>");
            text.AppendLine("#include <stdlib.h>");
        }
        if (features.HasGetKey || features.HasClearScreen || features.HasMoveCursor || features.HasTextColor || features.HasWait || features.HasTimer || features.HasRandom)
        {
            text.AppendLine("#include <windows.h>");
        }
        text.AppendLine();

        if (features.HasGetKey)
            text.AppendLine(ConsoleKeyMap.NativeFunction("int64_t smile_get_key_cobol(void)"));

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

        if (features.HasMoveCursor)
        {
            text.AppendLine("int smile_move_cursor_cobol(const int64_t *column, const int64_t *row)");
            text.AppendLine("{");
            text.AppendLine("    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);");
            text.AppendLine("    CONSOLE_SCREEN_BUFFER_INFO info;");
            text.AppendLine("    if (output == INVALID_HANDLE_VALUE || !GetConsoleScreenBufferInfo(output, &info)) return 0;");
            text.AppendLine("    COORD position;");
            text.AppendLine("    position.X = (SHORT)(*column < 1 ? 0 : *column > info.dwSize.X ? info.dwSize.X - 1 : *column - 1);");
            text.AppendLine("    position.Y = (SHORT)(*row < 1 ? 0 : *row > info.dwSize.Y ? info.dwSize.Y - 1 : *row - 1);");
            text.AppendLine("    SetConsoleCursorPosition(output, position);");
            text.AppendLine("    return 0;");
            text.AppendLine("}");
            text.AppendLine();
        }

        if (features.HasTextColor)
        {
            text.AppendLine("static WORD smile_default_attributes = 0;");
            text.AppendLine("static int smile_has_default_attributes = 0;");
            text.AppendLine("static const WORD smile_console_colors[] = {0, 12, 10, 14, 9, 13, 11, 15};");
            text.AppendLine("static HANDLE smile_color_output(void)");
            text.AppendLine("{");
            text.AppendLine("    HANDLE output = GetStdHandle(STD_OUTPUT_HANDLE);");
            text.AppendLine("    CONSOLE_SCREEN_BUFFER_INFO info;");
            text.AppendLine("    if (output != INVALID_HANDLE_VALUE && !smile_has_default_attributes && GetConsoleScreenBufferInfo(output, &info))");
            text.AppendLine("    {");
            text.AppendLine("        smile_default_attributes = info.wAttributes;");
            text.AppendLine("        smile_has_default_attributes = 1;");
            text.AppendLine("    }");
            text.AppendLine("    return output;");
            text.AppendLine("}");
            text.AppendLine("int smile_set_text_color_cobol(const int64_t *foreground, const int64_t *background)");
            text.AppendLine("{");
            text.AppendLine("    HANDLE output = smile_color_output();");
            text.AppendLine("    WORD attributes = smile_console_colors[*foreground] | (WORD)(smile_console_colors[*background] << 4);");
            text.AppendLine("    if (output != INVALID_HANDLE_VALUE) SetConsoleTextAttribute(output, attributes);");
            text.AppendLine("    return 0;");
            text.AppendLine("}");
            text.AppendLine("int smile_reset_text_color_cobol(void)");
            text.AppendLine("{");
            text.AppendLine("    HANDLE output = smile_color_output();");
            text.AppendLine("    if (output != INVALID_HANDLE_VALUE && smile_has_default_attributes) SetConsoleTextAttribute(output, smile_default_attributes);");
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

        if (features.HasTextInspection) text.AppendLine(NativeTextInspection.Generate(features, TargetLanguage.Cobol));
        text.AppendLine(NativeDoubleSupport.GenerateCobol(program));
        if (features.HasTextFileLoad) text.AppendLine(NativeTextFileSupport.GenerateCompanion(cobol: true));
        if (features.HasNumberPersistence) text.AppendLine(NativeNumberPersistence.Companion(program, cobol: true));
        if (features.HasDataPersistence) text.AppendLine(NativeDataPersistence.Companion(program, cobol: true));
        return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
