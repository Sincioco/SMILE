namespace SMILE.Engine;

internal static class NativeNumberPersistence
{
    public static string Generate(BoundProgram program, bool cpp = false, bool exported = false)
    {
        CoreBasicProgramFeatureSet features = CoreBasicProgramFeatureSet.Create(program);
        string name = SmilePersistentStorage.Sanitize(program.ProgramName);
        string prefix = exported ? "" : "static ";
        if (cpp) return Cpp(name, features);
        string code = $$"""
static int smile_number_path(const char *key, wchar_t *path)
{
    const wchar_t *root = _wgetenv(L"LOCALAPPDATA");
    if (root == NULL || *root == 0 || wcslen(root) + 540 >= 2048) return 0;
    swprintf(path, 2048, L"%ls\\SMILE", root);
    CreateDirectoryW(path, NULL);
    wcscat(path, L"\\Games");
    CreateDirectoryW(path, NULL);
    wcscat(path, L"\\{{name}}");
    CreateDirectoryW(path, NULL);
    size_t length = wcslen(path);
    path[length++] = L'\\';
    while (*key) path[length++] = (unsigned char)*key++;
    path[length] = 0;
    return 1;
}
""";
        if (features.HasNumberLoad) code += "\n" + $$"""
{{prefix}}int64_t smile_load_number(const char *key, int64_t fallback)
{
    wchar_t path[2048];
    if (!smile_number_path(key, path)) return fallback;
    FILE *file = _wfopen(path, L"rb");
    if (file == NULL) return fallback;
    char text[64];
    size_t length = fread(text, 1, 63, file);
    int failed = ferror(file);
    fclose(file);
    if (failed) return fallback;
    text[length] = 0;
    char *start = text;
    while (*start == ' ' || *start == '\t' || *start == '\r' || *start == '\n') start++;
    char *digits = start + (*start == '+' || *start == '-');
    if (*digits < '0' || *digits > '9') return fallback;
    errno = 0;
    char *end;
    int64_t value = strtoll(start, &end, 10);
    if (errno == ERANGE) return fallback;
    while (*end == ' ' || *end == '\t' || *end == '\r' || *end == '\n') end++;
    return end == text + length ? value : fallback;
}
""";
        if (features.HasNumberSave) code += "\n" + $$"""
{{prefix}}void smile_save_number(const char *key, int64_t value)
{
    wchar_t path[2048];
    if (!smile_number_path(key, path)) return;
    FILE *file = _wfopen(path, L"wb");
    if (file == NULL) return;
    fprintf(file, "%" PRId64, value);
    fclose(file);
}
""";
        return code;
    }

    public const string Includes = "#include <inttypes.h>\n#include <stdio.h>\n#include <stdlib.h>\n#include <errno.h>\n#include <wchar.h>\n#include <windows.h>\n";

    public static string Companion(BoundProgram program, bool cobol)
    {
        string code = Includes + Generate(program, exported: !cobol);
        if (!cobol) return code;
        var features = CoreBasicProgramFeatureSet.Create(program);
        if (features.HasNumberLoad) code += "\n" + """
void smile_load_number_cobol(const char *key, int64_t length, const int64_t *fallback, int64_t *result)
{
    char name[260];
    memcpy(name, key, (size_t)length);
    name[length] = 0;
    *result = smile_load_number(name, *fallback);
}
""";
        if (features.HasNumberSave) code += "\n" + """
void smile_save_number_cobol(const char *key, int64_t length, const int64_t *value)
{
    char name[260];
    memcpy(name, key, (size_t)length);
    name[length] = 0;
    smile_save_number(name, *value);
}
""";
        return "#include <string.h>\n" + code;
    }

    private static string Cpp(string name, CoreBasicProgramFeatureSet features)
    {
        string code = $$"""
static std::filesystem::path smile_number_path(const char *key)
{
    const wchar_t *root = _wgetenv(L"LOCALAPPDATA");
    if (root == nullptr || *root == 0) return {};
    auto folder = std::filesystem::path(root) / "SMILE" / "Games" / "{{name}}";
    std::filesystem::create_directories(folder);
    return folder / key;
}
""";
        if (features.HasNumberLoad) code += "\n" + """
static std::int64_t smile_load_number(const char *key, std::int64_t fallback)
{
    try {
        auto path = smile_number_path(key);
        if (path.empty()) return fallback;
        std::ifstream file(path, std::ios::binary);
        char bytes[63];
        file.read(bytes, sizeof(bytes));
        if (file.bad()) return fallback;
        std::string_view text(bytes, static_cast<std::size_t>(file.gcount()));
        auto start = text.find_first_not_of(" \t\r\n");
        if (start == std::string_view::npos) return fallback;
        text = text.substr(start, text.find_last_not_of(" \t\r\n") - start + 1);
        if (text.front() == '+') {
            text.remove_prefix(1);
            if (text.empty() || text.front() < '0' || text.front() > '9') return fallback;
        }
        std::int64_t value;
        auto result = std::from_chars(text.data(), text.data() + text.size(), value);
        return result.ec == std::errc() && result.ptr == text.data() + text.size() ? value : fallback;
    } catch (const std::exception&) { return fallback; }
}
""";
        if (features.HasNumberSave) code += "\n" + """
static void smile_save_number(const char *key, std::int64_t value)
{
    try {
        auto path = smile_number_path(key);
        if (path.empty()) return;
        std::ofstream file(path, std::ios::binary | std::ios::trunc);
        file << value;
    } catch (const std::exception&) { }
}
""";
        return code;
    }
}
