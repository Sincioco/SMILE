namespace SMILE.Engine;

// Native file mechanics stay outside learner statements. Windows supplies UTF-8
// path conversion and executable location; no additional runtime is required.
internal static class NativeTextFileSupport
{
    public const string PathDefinition = """
static int smile_text_file_path(const char *path, wchar_t *full_path, int capacity)
{
    char canonical[4096];
    int starts[512], depth = 0, written = 0;
    if (!path || !*path || *path == '/' || *path == '\\') return 0;
    while (*path)
    {
        while (*path == '/' || *path == '\\') path++;
        const char *start = path;
        while (*path && *path != '/' && *path != '\\')
        {
            if (*path == ':') return 0;
            path++;
        }
        int length = (int)(path - start);
        if (length == 0 || (length == 1 && start[0] == '.')) continue;
        if (length == 2 && start[0] == '.' && start[1] == '.')
        {
            if (depth == 0) return 0;
            written = starts[--depth];
            continue;
        }
        if (depth == 512 || written + length + (written != 0) >= 4096) return 0;
        starts[depth++] = written;
        if (written) canonical[written++] = '/';
        memcpy(canonical + written, start, (size_t)length);
        written += length;
    }
    if (!written) return 0;
    canonical[written] = 0;
    DWORD base = GetModuleFileNameW(NULL, full_path, (DWORD)capacity);
    if (base == 0 || base >= (DWORD)capacity) return 0;
    while (base && full_path[base - 1] != L'\\' && full_path[base - 1] != L'/') base--;
    return MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, canonical, -1,
        full_path + base, capacity - (int)base) > 0;
}
""";

    public const string LoadDefinition = """
static int64_t smile_load_text_file(const char *path, int64_t *destination, int64_t capacity)
{
    memset(destination, 0, (size_t)capacity * sizeof(*destination));
    wchar_t full_path[2048];
    if (!smile_text_file_path(path, full_path, 2048)) return 0;
    HANDLE file = CreateFileW(full_path, GENERIC_READ, FILE_SHARE_READ, NULL,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) return 0;
    unsigned char buffer[4096];
    DWORD read = 0, start = 0;
    int64_t copied = 0;
    int success = ReadFile(file, buffer, 3, &read, NULL) != 0;
    if (success && read == 3 && buffer[0] == 239 && buffer[1] == 187 && buffer[2] == 191) start = 3;
    while (success)
    {
        for (DWORD index = start; index < read && copied < capacity; index++) destination[copied++] = buffer[index];
        if (copied == capacity) break;
        success = ReadFile(file, buffer, sizeof(buffer), &read, NULL) != 0;
        if (!read) break;
        start = 0;
    }
    CloseHandle(file);
    if (success) return copied;
    memset(destination, 0, (size_t)capacity * sizeof(*destination));
    return 0;
}
""";

    public const string CppLoadDefinition = """
static std::int64_t smile_load_text_file(const std::string& path, std::span<std::int64_t> destination)
{
    std::fill(destination.begin(), destination.end(), 0);
    wchar_t full_path[2048];
    if (path.find('\0') != std::string::npos || !smile_text_file_path(path.c_str(), full_path, 2048)) return 0;
    std::ifstream file(std::filesystem::path(full_path), std::ios::binary);
    if (!file) return 0;
    unsigned char prefix[3];
    file.read(reinterpret_cast<char*>(prefix), 3);
    std::streamsize read = file.gcount();
    size_t copied = 0;
    int start = read == 3 && prefix[0] == 239 && prefix[1] == 187 && prefix[2] == 191 ? 3 : 0;
    for (int index = start; index < read && copied < destination.size(); index++) destination[copied++] = prefix[index];
    char byte;
    while (copied < destination.size() && file.get(byte)) destination[copied++] = static_cast<unsigned char>(byte);
    if (!file.bad()) return static_cast<std::int64_t>(copied);
    std::fill(destination.begin(), destination.end(), 0);
    return 0;
}
""";

    public static string GenerateCompanion(bool cobol)
    {
        string source = "#include <windows.h>\n#include <stdint.h>\n#include <string.h>\n\n" + PathDefinition + "\n" + LoadDefinition;
        if (!cobol) return GeneratedSourceLayout.Normalize(source.Replace("static int64_t smile_load_text_file", "int64_t smile_load_text_file"), TargetLanguage.C);
        return source + "\n" + """
int smile_load_text_file_cobol(const char *path, const int64_t *length, int64_t *destination, const int64_t *capacity, int64_t *count)
{
    char buffer[4097];
    memset(destination, 0, (size_t)*capacity * sizeof(*destination));
    *count = 0;
    if (*length < 0 || *length > 4096 || memchr(path, 0, (size_t)*length)) return 0;
    memcpy(buffer, path, (size_t)*length);
    buffer[*length] = 0;
    *count = smile_load_text_file(buffer, destination, *capacity);
    return 0;
}
""";
    }
}
