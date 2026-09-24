namespace SMILE.Engine;

// C, MASM and COBOL have allocation but no owning class reference. Classes cannot
// contain other class references, so collecting unreferenced instances needs only
// the live scalar slots, never a tracing graph or a compiler-generated object ID.
internal static class NativeClassSupport
{
    internal static string MasmAllocator(BoundProgram program, ClassTypeSymbol type) => "smile_class_allocate_" + program.ClassTypes.ToList().IndexOf(type);

    internal static string MasmCompanion(BoundProgram program)
    {
        // MASM enters at main and owns explicit shutdown, without a C startup frame.
        var code = new System.Text.StringBuilder(Definition.Replace("void smile_object_initialize(void) { atexit(smile_object_shutdown); }",
            "void smile_object_initialize(void) { }", StringComparison.Ordinal));
        bool text = CoreBasicMasmTextRuntime.NeedsManagedText(program);
        if (text) code.AppendLine("\nvoid smile_text_register_range(const char **, size_t);\nvoid smile_text_unregister_range(const char **, size_t);");
        foreach (ClassTypeSymbol type in program.ClassTypes)
        {
            string name = MasmAllocator(program, type);
            int[] offsets = TextOffsets(type.Fields, 0).ToArray();
            if (text && offsets.Length > 0)
            {
                code.AppendLine($"static void {name}_finalize(void *value) {{");
                foreach (int offset in offsets) code.AppendLine($"    smile_text_unregister_range((const char **)((char *)value + {offset}), 1);");
                code.AppendLine("}");
            }
            code.AppendLine($"void *{name}(void) {{\n    void *value = smile_object_allocate({type.InstanceSize}, {(text && offsets.Length > 0 ? name + "_finalize" : "NULL")});");
            foreach (int offset in offsets)
            {
                code.AppendLine($"    *(const char **)((char *)value + {offset}) = \"\";");
                if (text) code.AppendLine($"    smile_text_register_range((const char **)((char *)value + {offset}), 1);");
            }
            code.AppendLine("    return value;\n}");
        }
        return code.ToString();
    }

    private static IEnumerable<int> TextOffsets(IReadOnlyList<InstanceFieldSymbol> fields, int start)
    {
        foreach (InstanceFieldSymbol field in fields)
            for (int index = 0; index < field.ElementCount; index++)
            {
                int offset = start + field.NativeOffset + index * (field.Type is RecordTypeSymbol record ? record.NativeSize : 8);
                if (field.Type == SmileType.String) yield return offset;
                else if (field.Type is RecordTypeSymbol nested)
                    foreach (int child in TextOffsets(nested.Fields, offset)) yield return child;
            }
    }

    internal const string Definition = """
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct SmileObject {
    struct SmileObject *next;
    void (*finalize)(void *);
} SmileObject;
typedef struct SmileObjectRoot {
    struct SmileObjectRoot *next;
    const void *slot;
} SmileObjectRoot;
static SmileObject *smile_objects;
static SmileObjectRoot *smile_object_roots;
static size_t smile_object_root_count;
static void *smile_object_return_root;
static size_t smile_object_allocations, smile_object_frees, smile_object_peak;
static int smile_object_shutdown_complete;

static void *smile_object_memory(size_t bytes) {
    void *value = calloc(1, bytes);
    if (!value) { fputs("Unable to allocate Class storage.\n", stderr); exit(2); }
    return value;
}
void *smile_object_require(void *value) {
    if (!value) { fputs("Object reference is Nothing.\n", stderr); exit(2); }
    return value;
}
void *smile_object_allocate(size_t bytes, void (*finalize)(void *)) {
    SmileObject *object = smile_object_memory(sizeof(SmileObject) + bytes);
    object->next = smile_objects;
    object->finalize = finalize;
    smile_objects = object;
    smile_object_allocations++;
    size_t live = smile_object_allocations - smile_object_frees;
    if (live > smile_object_peak) smile_object_peak = live;
    return object + 1;
}
void smile_object_register(const void *slot) {
    SmileObjectRoot *root = smile_object_memory(sizeof(SmileObjectRoot));
    root->slot = slot;
    root->next = smile_object_roots;
    smile_object_roots = root;
    smile_object_root_count++;
}
void smile_object_unregister(const void *slot) {
    SmileObjectRoot **link = &smile_object_roots;
    while (*link) {
        SmileObjectRoot *root = *link;
        if (root->slot == slot) {
            *link = root->next;
            free(root);
            smile_object_root_count--;
            return;
        }
        link = &root->next;
    }
}
size_t smile_object_checkpoint(void) { return smile_object_root_count; }
void smile_object_restore(size_t count) {
    while (smile_object_root_count > count) {
        SmileObjectRoot *root = smile_object_roots;
        smile_object_roots = root->next;
        free(root);
        smile_object_root_count--;
    }
}
void smile_object_return(void *value) { smile_object_return_root = value; }
void smile_object_collect(void) {
    SmileObject **link = &smile_objects;
    while (*link) {
        SmileObject *object = *link;
        void *value = object + 1;
        int live = value == smile_object_return_root;
        for (SmileObjectRoot *root = smile_object_roots; !live && root; root = root->next) {
            void *reference;
            memcpy(&reference, root->slot, sizeof(reference));
            live = reference == value;
        }
        if (live) link = &object->next;
        else {
            *link = object->next;
            if (object->finalize) object->finalize(value);
            free(object);
            smile_object_frees++;
        }
    }
}
void smile_object_shutdown(void) {
    if (smile_object_shutdown_complete) return;
    smile_object_shutdown_complete = 1;
    smile_object_restore(0);
    smile_object_return_root = NULL;
    smile_object_collect();
    if (getenv("SMILE_OBJECT_LIFETIME_REPORT"))
        fprintf(stderr, "SMILE Class lifetime: allocations=%zu frees=%zu live=%zu peak=%zu\n",
            smile_object_allocations, smile_object_frees, smile_object_allocations - smile_object_frees, smile_object_peak);
}
void smile_object_initialize(void) { atexit(smile_object_shutdown); }
""";
}
