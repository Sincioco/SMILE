using System.Security.Cryptography;
using System.Text;

namespace SMILE.Engine;

internal static class NativeDataPersistence
{
    internal static string IdentityHash(BoundProgram program) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(program.ProgramName)));

    public static string Generate(BoundProgram program, bool exported = false)
    {
        var features = CoreBasicProgramFeatureSet.Create(program);
        string code = Common.Replace("APPLICATION_HASH", IdentityHash(program), StringComparison.Ordinal);
        if (features.HasDataLoad) code += "\n" + Load;
        if (features.HasDataSave) code += "\n" + Save;
        return code.Replace("EXPORT ", exported ? "" : "static ", StringComparison.Ordinal);
    }

    public const string Includes = "#ifndef NOMINMAX\n#define NOMINMAX\n#endif\n#include <windows.h>\n#include <bcrypt.h>\n#include <stdint.h>\n#include <stdio.h>\n#include <stdlib.h>\n#include <string.h>\n#include <wchar.h>\n#pragma comment(lib, \"bcrypt.lib\")";
    public const string LoadPrototype = "int64_t smile_load_data(const char *key, int64_t key_length, int64_t *destination, int64_t capacity, int64_t *count, int recover)";
    public const string SavePrototype = "int64_t smile_save_data(const int64_t *source, int64_t capacity, int64_t count, const char *key, int64_t key_length, int recover)";

    public static string Companion(BoundProgram program, bool cobol)
    {
        var features = CoreBasicProgramFeatureSet.Create(program);
        string code = Includes + "\n" + Generate(program, exported: true);
        if (cobol && features.HasDataLoad) code += "\n" + """
void smile_load_data_cobol(const char *key, const int64_t *key_length, int64_t *destination,
    const int64_t *capacity, int64_t *count, const int64_t *recover, int64_t *status)
{
    *status = smile_load_data(key, *key_length, destination, *capacity, count, (int)*recover);
}
""";
        if (cobol && features.HasDataSave) code += "\n" + """
void smile_save_data_cobol(const int64_t *source, const int64_t *capacity, const int64_t *count,
    const char *key, const int64_t *key_length, const int64_t *recover, int64_t *status)
{
    *status = smile_save_data(source, *capacity, *count, key, *key_length, (int)*recover);
}
""";
        return code;
    }

    private const string Common = """
static int smile_data_hash(const unsigned char *bytes, ULONG length, unsigned char *digest)
{
    BCRYPT_ALG_HANDLE algorithm = NULL;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, NULL, 0) < 0) return 0;
    BCRYPT_HASH_HANDLE hash = NULL;
    NTSTATUS status = BCryptCreateHash(algorithm, &hash, NULL, 0, NULL, 0, 0);
    if (status >= 0) status = BCryptHashData(hash, (PUCHAR)bytes, length, 0);
    if (status >= 0) status = BCryptFinishHash(hash, digest, 32, 0);
    if (hash) BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    return status >= 0;
}

static int smile_data_path(const char *key, int64_t length, wchar_t *path)
{
    const wchar_t *root = _wgetenv(L"LOCALAPPDATA");
    unsigned char digest[32];
    if (!root || !*root || wcslen(root) + 160 >= 2048 || length < 0 || length > 1048576 ||
        !smile_data_hash((const unsigned char *)key, (ULONG)length, digest)) return 0;
    swprintf(path, 2048, L"%ls\\SMILE", root);
    CreateDirectoryW(path, NULL);
    wcscat(path, L"\\Games"); CreateDirectoryW(path, NULL);
    wcscat(path, L"\\APPLICATION_HASH"); CreateDirectoryW(path, NULL);
    wcscat(path, L"\\Data"); CreateDirectoryW(path, NULL);
    size_t offset = wcslen(path);
    path[offset++] = L'\\';
    const wchar_t *hex = L"0123456789abcdef";
    for (int index = 0; index < 32; index++) {
        path[offset++] = hex[digest[index] >> 4]; path[offset++] = hex[digest[index] & 15];
    }
    path[offset] = 0; wcscat(path, L".bin");
    return 1;
}

static uint32_t smile_data_u32(const unsigned char *bytes)
{
    return (uint32_t)bytes[0] | (uint32_t)bytes[1] << 8 | (uint32_t)bytes[2] << 16 | (uint32_t)bytes[3] << 24;
}

static int smile_data_read(const wchar_t *path, int64_t *destination, int64_t capacity, int64_t *count)
{
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        DWORD error = GetLastError();
        return error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND ? 1 : 4;
    }
    LARGE_INTEGER size;
    DWORD read = 0;
    int status = 4;
    unsigned char *bytes = NULL;
    unsigned char digest[32];
    if (!GetFileSizeEx(file, &size)) goto done;
    status = 5;
    if (size.QuadPart < 44 || size.QuadPart > 1048620) goto done;
    status = 4;
    bytes = (unsigned char *)malloc((size_t)size.QuadPart);
    if (!bytes || !ReadFile(file, bytes, (DWORD)size.QuadPart, &read, NULL) || read != size.QuadPart) goto done;
    status = 5;
    if (memcmp(bytes, "SMD4", 4) || smile_data_u32(bytes + 4) != 1 || smile_data_u32(bytes + 8) != size.QuadPart - 44) goto done;
    status = 4;
    if (!smile_data_hash(bytes + 44, (ULONG)(size.QuadPart - 44), digest)) goto done;
    status = 5;
    if (memcmp(digest, bytes + 12, 32)) goto done;
    status = 6;
    if (size.QuadPart - 44 > capacity) goto done;
    if (destination) for (int64_t index = 0; index < size.QuadPart - 44; index++) destination[index] = bytes[44 + index];
    *count = size.QuadPart - 44;
    status = 0;
done:
    free(bytes); CloseHandle(file); return status;
}

static void smile_data_fail(const char *message)
{
    fflush(stdout); fputs(message, stderr); fputc('\n', stderr); fflush(stderr); ExitProcess(2);
}
""";

    private const string Load = """
EXPORT int64_t smile_load_data(const char *key, int64_t key_length, int64_t *destination, int64_t capacity, int64_t *count, int recover)
{
    int status = 3;
    wchar_t path[2048], backup[2100];
    *count = 0;
    if (!destination || capacity < 0 || capacity > 1048576) goto done;
    if (!recover) memset(destination, 0, (size_t)capacity * sizeof(int64_t));
    status = 4;
    if (!smile_data_path(key, key_length, path)) goto done;
    status = smile_data_read(path, destination, capacity, count);
    if (recover && (status == 1 || status == 5)) {
        swprintf(backup, 2100, L"%ls.bak", path);
        int backup_status = smile_data_read(backup, destination, capacity, count);
        if (backup_status == 0) status = 2;
        else if (backup_status != 1) status = backup_status;
    }
done:
    if (!recover && status != 0 && status != 1)
        smile_data_fail("Load Data encountered an invalid destination, corrupt block, oversized block, or unavailable storage.");
    return status;
}
""";

    private const string Save = """
EXPORT int64_t smile_save_data(const int64_t *source, int64_t capacity, int64_t count, const char *key, int64_t key_length, int recover)
{
    int status = 3, owns_temporary = 0;
    wchar_t path[2048], temporary[2100], backup[2100];
    HANDLE file = INVALID_HANDLE_VALUE;
    unsigned char *bytes = NULL;
    DWORD written;
    int64_t ignored = 0;
    if (!source || capacity < 0 || capacity > 1048576 || count < 0 || count > capacity) goto done;
    status = 4;
    bytes = (unsigned char *)calloc(1, (size_t)count + 44);
    if (!bytes) goto done;
    status = 3;
    for (int64_t index = 0; index < count; index++) {
        if (source[index] < 0 || source[index] > 255) goto done;
        bytes[index + 44] = (unsigned char)source[index];
    }
    memcpy(bytes, "SMD4", 4); bytes[4] = 1;
    for (int index = 0; index < 4; index++) bytes[8 + index] = (unsigned char)((uint32_t)count >> (index * 8));
    status = 4;
    if (!smile_data_hash(bytes + 44, (ULONG)count, bytes + 12) || !smile_data_path(key, key_length, path)) goto done;
    swprintf(temporary, 2100, L"%ls.tmp.%lu.%llu", path, GetCurrentProcessId(), (unsigned long long)GetTickCount64());
    file = CreateFileW(temporary, GENERIC_WRITE, 0, NULL, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) goto done;
    owns_temporary = 1;
    if (!WriteFile(file, bytes, (DWORD)count + 44, &written, NULL) || written != count + 44 || !FlushFileBuffers(file)) goto done;
    CloseHandle(file); file = INVALID_HANDLE_VALUE;
    if (GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES) {
        swprintf(backup, 2100, L"%ls.bak", path);
        status = smile_data_read(path, NULL, 1048576, &ignored);
        if (status != 0) {
            if (!recover || status != 5) goto done;
            status = smile_data_read(backup, NULL, 1048576, &ignored);
            if (status == 1) status = 4;
            if (status != 0) goto done;
            status = 4;
            if (!ReplaceFileW(path, temporary, NULL, 0, NULL, NULL)) goto done;
        } else {
            status = 4;
            if (!ReplaceFileW(path, temporary, backup, 0, NULL, NULL)) goto done;
        }
    } else if (!MoveFileExW(temporary, path, MOVEFILE_WRITE_THROUGH)) goto done;
    owns_temporary = 0; status = 0;
done:
    if (file != INVALID_HANDLE_VALUE) CloseHandle(file);
    if (owns_temporary) DeleteFileW(temporary);
    free(bytes);
    if (!recover && status != 0) smile_data_fail("Save Data received invalid bytes/count or could not atomically store the block.");
    return status;
}
""";
}
