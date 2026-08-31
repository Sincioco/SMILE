namespace SMILE.Engine;

internal static class CoreBasicMasmTextRuntime
{
    public static bool IsRequired(BoundProgram program) =>
        CoreBasicCodeGenerator.EnumerateExpressionsForSupport(program)
            .Any(expression => expression is BoundBinaryExpression
            {
                Operator.Kind: BoundBinaryOperatorKind.StringConcatenation
            });

    public static string Generate() => GeneratedSourceLayout.Normalize(
        """
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct SmileTextAllocation
{
    struct SmileTextAllocation *next;
    char text[1];
} SmileTextAllocation;

static SmileTextAllocation *smile_text_allocations;
static const char **smile_text_roots[65536];
static const char *smile_text_return_root;
static size_t smile_text_root_count;
static size_t smile_text_allocation_count;
static size_t smile_text_free_count;
static size_t smile_text_live_count;
static size_t smile_text_peak_count;
static bool smile_text_shutdown_complete;

void smile_text_register_range(const char **roots, size_t count)
{
    if (count > 65536 - smile_text_root_count)
    {
        fputs("SMILE Runtime Error: too many live Text roots.\n", stderr);
        exit(1);
    }

    for (size_t index = 0; index < count; index++)
    {
        smile_text_roots[smile_text_root_count++] = &roots[index];
    }
}

void smile_text_unregister_range(const char **roots, size_t count)
{
    for (size_t root_index = 0; root_index < count; root_index++)
    {
        const char **root = &roots[root_index];
        for (size_t index = smile_text_root_count; index > 0; index--)
        {
            if (smile_text_roots[index - 1] == root)
            {
                smile_text_roots[index - 1] = smile_text_roots[--smile_text_root_count];
                break;
            }
        }
    }
}

void smile_text_set_return_root(const char *value)
{
    smile_text_return_root = value;
}

void smile_text_collect(void)
{
    SmileTextAllocation **link = &smile_text_allocations;
    while (*link != NULL)
    {
        SmileTextAllocation *candidate = *link;
        bool rooted = candidate->text == smile_text_return_root;
        for (size_t index = 0; !rooted && index < smile_text_root_count; index++)
        {
            rooted = *smile_text_roots[index] == candidate->text;
        }

        if (rooted)
        {
            link = &candidate->next;
            continue;
        }

        *link = candidate->next;
        free(candidate);
        smile_text_free_count++;
        smile_text_live_count--;
    }
}

void smile_text_shutdown(void)
{
    if (smile_text_shutdown_complete)
    {
        return;
    }

    smile_text_shutdown_complete = true;
    smile_text_root_count = 0;
    smile_text_return_root = NULL;
    smile_text_collect();
    if (getenv("SMILE_TEXT_LIFETIME_REPORT") != NULL)
    {
        fprintf(
            stderr,
            "SMILE Text lifetime: allocations=%zu frees=%zu live=%zu peak=%zu\n",
            smile_text_allocation_count,
            smile_text_free_count,
            smile_text_live_count,
            smile_text_peak_count);
    }
}

void smile_text_initialize(void)
{
    smile_text_shutdown_complete = false;
}

const char *smile_text_concat(const char *left, const char *right)
{
    size_t length = strlen(left) + strlen(right) + 1;
    SmileTextAllocation *allocation = malloc(sizeof(*allocation) + length);
    if (allocation == NULL)
    {
        fputs("SMILE Runtime Error: Text allocation failed.\n", stderr);
        exit(1);
    }

    snprintf(allocation->text, length, "%s%s", left, right);
    allocation->next = smile_text_allocations;
    smile_text_allocations = allocation;
    smile_text_allocation_count++;
    smile_text_live_count++;
    if (smile_text_live_count > smile_text_peak_count)
    {
        smile_text_peak_count = smile_text_live_count;
    }

    return allocation->text;
}
""",
        TargetLanguage.C);
}
