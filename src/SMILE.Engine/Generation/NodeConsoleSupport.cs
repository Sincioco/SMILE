namespace SMILE.Engine;

// Node has no built-in Win32 FFI. A tiny stable Node-API addon supplies the one
// missing primitive. Resolve Node's exported ABI directly so no npm package,
// downloaded headers, import library or node-gyp build is required.
internal static class NodeConsoleSupport
{
    public static string Generate() => "#include <windows.h>\n#include <stdint.h>\n\n" +
        ConsoleKeyMap.NativeFunction("static int64_t smile_get_key(void)") + "\n" + """
typedef struct napi_env__ *napi_env;
typedef struct napi_value__ *napi_value;
typedef struct napi_callback_info__ *napi_callback_info;
typedef napi_value (__cdecl *napi_callback)(napi_env, napi_callback_info);
typedef int (__cdecl *create_bigint_fn)(napi_env, int64_t, napi_value *);
typedef int (__cdecl *create_function_fn)(napi_env, const char *, size_t, napi_callback, void *, napi_value *);
static create_bigint_fn create_bigint;

static napi_value __cdecl read_key(napi_env env, napi_callback_info info)
{
    napi_value result = NULL;
    (void)info;
    if (create_bigint(env, smile_get_key(), &result) != 0) return NULL;
    return result;
}

__declspec(dllexport) int32_t node_api_module_get_api_version_v1(void) { return 6; }

__declspec(dllexport) napi_value napi_register_module_v1(napi_env env, napi_value exports)
{
    HMODULE node = GetModuleHandleW(NULL);
    create_function_fn create_function = (create_function_fn)GetProcAddress(node, "napi_create_function");
    create_bigint = (create_bigint_fn)GetProcAddress(node, "napi_create_bigint_int64");
    napi_value result = NULL;
    (void)exports;
    if (!create_function || !create_bigint) return NULL;
    if (create_function(env, "smileGetKey", 11, read_key, NULL, &result) != 0) return NULL;
    return result;
}
""";
}
