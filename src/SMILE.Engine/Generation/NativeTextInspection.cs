using System.Text;

namespace SMILE.Engine;

// C-family Text is UTF-8. Only these targets need a byte-to-scalar traversal;
// higher-level generators use their standard Unicode/string facilities.
internal static class NativeTextInspection
{
    public static string Generate(CoreBasicProgramFeatureSet features, TargetLanguage target)
    {
        bool cpp = target is TargetLanguage.Cpp;
        bool cobol = target is TargetLanguage.Cobol;
        string integer = cpp ? "std::int64_t" : "int64_t";
        string type = cpp ? "const std::string&" : "const char *";
        string prefix = target is TargetLanguage.MasmX64 or TargetLanguage.Cobol ? "" : "static ";
        string bytes = cpp ? "text.data()" : "text";
        string length = cpp ? "text.size()" : cobol ? "(size_t)*byte_length" : "strlen(text)";
        string suffix = cobol ? "_cobol" : "";
        string parameters = cobol ? "const char *text, const int64_t *byte_length" : $"{type} text";
        var code = new StringBuilder("""
static unsigned int smile_utf8_next(const char *text, size_t length, size_t *offset)
{
    unsigned int first = (unsigned char)text[(*offset)++];
    if (first < 128) return first;
    unsigned int remaining = first < 224 ? 1 : first < 240 ? 2 : 3;
    unsigned int scalar = first & (remaining == 1 ? 31u : remaining == 2 ? 15u : 7u);
    while (remaining-- > 0 && *offset < length)
    {
        scalar = (scalar << 6) | ((unsigned char)text[(*offset)++] & 63u);
    }
    return scalar;
}

""");
        if (features.HasTextLength)
        {
            code.AppendLine($"{prefix}{integer} smile_text_length{suffix}({parameters})");
            code.AppendLine($$"""
{
    size_t offset = 0, length = {{length}};
    {{integer}} count = 0;
    while (offset < length)
    {
        smile_utf8_next({{bytes}}, length, &offset);
        count++;
    }
    return count;
}

""");
        }
        if (features.HasTextCodeAt)
        {
            string index = cobol ? "const int64_t *requested_index" : $"{integer} index";
            code.AppendLine($"{prefix}{integer} smile_text_code_at{suffix}({parameters}, {index})");
            code.AppendLine("{");
            if (cobol) code.AppendLine("    int64_t index = *requested_index;");
            code.AppendLine($$"""
    if (index < 0) return -1;
    size_t offset = 0, length = {{length}};
    while (offset < length)
    {
        unsigned int scalar = smile_utf8_next({{bytes}}, length, &offset);
        if (index-- == 0) return scalar;
    }
    return -1;
}

""");
        }
        if (features.HasTextSlice)
        {
            code.AppendLine(cobol
                ? "int smile_text_slice_cobol(const char *text, const int64_t *byte_length, const int64_t *requested_start, const int64_t *requested_count, char *result, int64_t *result_length)"
                : $"{prefix}{(cpp ? "std::string" : "const char *")} smile_text_slice({parameters}, {integer} start, {integer} count)");
            code.AppendLine("{");
            if (cobol)
            {
                code.AppendLine("    int64_t start = *requested_start, count = *requested_count;");
                code.AppendLine("    *result_length = 0;");
            }
            code.AppendLine($"    if (start < 0 || count <= 0) return {(cobol ? "0" : "\"\"")};");
            code.AppendLine($$"""
    size_t offset = 0, length = {{length}};
    while (offset < length && start > 0)
    {
        smile_utf8_next({{bytes}}, length, &offset);
        start--;
    }
    size_t begin = offset;
    while (offset < length && count > 0)
    {
        smile_utf8_next({{bytes}}, length, &offset);
        count--;
    }
""");
            if (cpp) code.AppendLine("    return text.substr(begin, offset - begin);");
            else if (cobol)
            {
                code.AppendLine("    *result_length = (int64_t)(offset - begin);");
                code.AppendLine("    memcpy(result, text + begin, offset - begin);");
                code.AppendLine("    return 0;");
            }
            else
            {
                code.AppendLine("    char *result = smile_text_allocate(offset - begin + 1);");
                code.AppendLine("    memcpy(result, text + begin, offset - begin);");
                code.AppendLine("    result[offset - begin] = 0;");
                code.AppendLine("    return result;");
            }
            code.AppendLine("}");
        }
        return code.ToString();
    }
}
