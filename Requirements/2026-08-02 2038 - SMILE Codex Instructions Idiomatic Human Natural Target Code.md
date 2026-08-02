# Codex Implementation Instructions — Idiomatic, Human-Natural Target Code

## Repository

- Repository: `Sincioco/SMILE`
- Work from the latest `main`.
- The observed baseline when this brief was prepared was commit:
  - `252eb6e96a3f19e06351e01d00209e1aace13937`
  - `Sin and Codex: Preserve PRINT expression intent across targets`
- Re-read `AGENTS.md` before making changes.
- Preserve the recently implemented distinction between interpolation and explicit concatenation.

---

## User objective

Make this a permanent SMILE code-generation rule:

> **Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write.**

This rule applies to:

- all existing target-language generators;
- all future target-language generators;
- declarations;
- expressions;
- statements;
- standard-library/API choices;
- formatting;
- naming only when target validity requires a mapped name;
- generated project and runtime scaffolding.

The immediate defect is the C `PRINT` generator. Its output is valid but mechanically emits one `fputs` call for every literal or variable segment and then a separate `putchar` call for the newline.

That is compiler-like lowering, not the kind of compact C source a developer would usually write by hand.

---

# 1. Non-negotiable target-code quality standard

Add the following normative policy to SMILE's project documentation and follow it in the implementation.

## 1.1 Priority order

When generating a target language, use this priority order:

1. **Semantic correctness**
2. **Safety**
3. **Valid target-language syntax**
4. **Deterministic output**
5. **Preservation of SMILE expression intent**
6. **Idiomatic target-language constructs**
7. **Human readability**
8. **Minimal necessary scaffolding**
9. **Compactness without cleverness**

Do not sacrifice correctness or safety merely to produce shorter code.

Do not use an obscure micro-optimization when a straightforward, conventional construct is easier for a human developer to understand.

## 1.2 Required general rule

Use wording equivalent to this:

> Generated target code MUST preserve SMILE program behavior. It SHOULD use the conventional constructs, APIs, expression forms, formatting, and program structure that a competent developer in the destination language would naturally choose. When the target language provides a clear idiomatic equivalent for the programmer's SMILE expression intent, the generator SHOULD use it. When no close native equivalent exists, the generator MUST use the clearest semantically equivalent fallback.

Also add:

> A target generator SHOULD normally emit one natural target-language statement for one SMILE statement when practical. It SHOULD NOT mechanically expose internal compiler segments as multiple target statements when the destination language provides a clear, safe, readable single-statement form.

## 1.3 Target-specific, not one-size-fits-all

Do not force every language through the same textual output strategy.

Examples:

- C# interpolation should use C# interpolated strings.
- JavaScript interpolation should use template literals.
- Swift interpolation should use Swift interpolation.
- Java may use string concatenation when no equivalent built-in interpolation exists.
- C should use a conventional `printf` format string and arguments.
- MASM may require lower-level output operations because that is natural for the target and avoids adding an unwanted runtime dependency.

Every backend should choose the closest natural form for its own language.

---

# 2. Do not change the approved C# output

The following C# output is approved.

Do not change its behavior, expression style, formatting, or generated project behavior as part of this task:

```csharp
using System;

internal static class Program
{
    private static void Main()
    {
        string Name = "Sin";
        Console.WriteLine();
        Console.WriteLine("Hello World!");
        Console.WriteLine("Hello World!");
        Console.WriteLine($"Hello {Name}!");
        Console.WriteLine($"Hello {Name}!");
        Console.WriteLine("Hello " + Name + "!");
    }
}
```

The recently completed expression-intent work must remain intact:

- friendly interpolation remains interpolation;
- explicit `$"..."` remains interpolation;
- explicit `+` concatenation remains concatenation;
- blank `PRINT` remains `Console.WriteLine();`.

Do not refactor the shared expression model in a way that regresses this output.

---

# 3. Immediate required change: idiomatic C `PRINT`

## 3.1 SMILE source

Use this exact sample as a primary acceptance test:

```smile
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

## 3.2 Current C output to replace

The current style is too mechanically lowered:

```c
#include <stdio.h>

int main(void)
{
    const char *Name = "Sin";
    putchar('\n');
    fputs("Hello World!", stdout);
    putchar('\n');
    fputs("Hello World!", stdout);
    putchar('\n');
    fputs("Hello ", stdout);
    fputs(Name, stdout);
    fputs("!", stdout);
    putchar('\n');
    fputs("Hello ", stdout);
    fputs(Name, stdout);
    fputs("!", stdout);
    putchar('\n');
    fputs("Hello ", stdout);
    fputs(Name, stdout);
    fputs("!", stdout);
    putchar('\n');
    return 0;
}
```

## 3.3 Required C output style

Generate one clear `printf` call per SMILE `PRINT` statement:

```c
#include <stdio.h>

int main(void)
{
    const char *Name = "Sin";

    printf("\n");
    printf("Hello World!\n");
    printf("Hello World!\n");
    printf("Hello %s!\n", Name);
    printf("Hello %s!\n", Name);
    printf("Hello %s!\n", Name);

    return 0;
}
```

This is the required style for the current string-only SMILE implementation.

### Identifier-spelling note

The user's illustrative preferred C example used `name`, while the SMILE declaration is `Name`.

For this task, preserve the identifier spelling declared in SMILE:

```c
const char *Name = "Sin";
```

The official SMILE specification currently says declaration spelling should be preserved in generated code. Do not introduce target-specific identifier renaming as a side effect of this `PRINT` change.

A future, separately specified target-name mapping policy may convert identifiers to target naming conventions. That requires reserved-word handling, invalid-character handling, deterministic collision resolution, and symbol-based mapping. Do not perform a naive lowercase conversion.

---

# 4. C generation rules

## 4.1 One `printf` per `PRINT`

For every supported `PRINT` form, generate one `printf` statement whenever practical.

### Blank line

SMILE:

```smile
PRINT
```

C:

```c
printf("\n");
```

### Plain literal

SMILE:

```smile
PRINT "Hello World!"
```

C:

```c
printf("Hello World!\n");
```

### Raw literal

SMILE:

```smile
PRINT Hello World!
```

C:

```c
printf("Hello World!\n");
```

### Friendly interpolation

SMILE:

```smile
PRINT Hello {Name}!
```

C:

```c
printf("Hello %s!\n", Name);
```

### Explicit interpolation

SMILE:

```smile
PRINT $"Hello {Name}!"
```

C:

```c
printf("Hello %s!\n", Name);
```

### Explicit concatenation

SMILE:

```smile
PRINT "Hello " + Name + "!"
```

C:

```c
printf("Hello %s!\n", Name);
```

C does not have a close native equivalent for SMILE interpolation or string `+` concatenation. A `printf` format string is the conventional C fallback for all three forms.

This does not violate expression-intent preservation. The target lacks an equivalent high-level syntax, so it uses the clearest idiomatic fallback.

## 4.2 Variable-only output

SMILE:

```smile
PRINT $"{Name}"
```

C:

```c
printf("%s\n", Name);
```

## 4.3 Multiple variables

SMILE:

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
PRINT $"{FirstName} {LastName}"
```

C:

```c
const char *FirstName = "Sin";
const char *LastName = "Cioco";

printf("%s %s\n", FirstName, LastName);
```

## 4.4 Adjacent variables

SMILE:

```smile
LET A = "A"
LET B = "B"
PRINT $"{A}{B}"
```

C:

```c
printf("%s%s\n", A, B);
```

## 4.5 Preserve argument order and repetition

SMILE:

```smile
PRINT $"{A}-{B}-{A}"
```

C:

```c
printf("%s-%s-%s\n", A, B, A);
```

Do not deduplicate arguments. Preserve evaluation and output order.

---

# 5. Critical `printf` format-string safety

The generated `printf` format argument MUST always be a compiler-generated C string literal.

Never generate this:

```c
printf(Name);
```

Always generate:

```c
printf("%s\n", Name);
```

A variable value must never become the format string.

## 5.1 Escape literal percent signs

Every literal `%` originating from SMILE text must become `%%` inside the generated C `printf` format string.

SMILE:

```smile
PRINT Progress: 100%
```

Required generated C:

```c
printf("Progress: 100%%\n");
```

Runtime output:

```text
Progress: 100%
```

SMILE:

```smile
PRINT {Name} is 100% ready.
```

Required generated C:

```c
printf("%s is 100%% ready.\n", Name);
```

This rule is mandatory. Failing to escape `%` can produce malformed format strings, undefined behavior, or a format-string vulnerability.

## 5.2 Keep existing C escaping behavior

The new format-string builder must preserve correct C escaping for:

- `"`
- `\`
- tabs
- newlines represented in a string
- carriage returns
- control characters
- octal escapes already supported by the generator
- Unicode/source encoding behavior already supported by the toolchain

Add format-string escaping on top of C string escaping; do not regress the existing escape tests.

## 5.3 Append exactly one PRINT newline

Each SMILE `PRINT` appends exactly one newline.

The generated format string should normally end in:

```c
\n
```

Do not accidentally append two newlines when the source text already contains an escaped newline as data. Preserve the source text, then append the one newline required by `PRINT`.

---

# 6. Recommended C implementation shape

Keep the solution simple and local to the C backend.

The current target-local flattening helper may still be used:

```csharp
BoundStringExpression.FlattenForOutput(expression)
```

That helper is appropriate inside C because C needs a lowered format string and argument list.

Replace the current per-segment emission with a format-plan step.

A simple internal representation may be:

```csharp
internal sealed record CPrintfPlan(
    string FormatText,
    IReadOnlyList<string> Arguments);
```

A helper can:

1. flatten the bound expression in output order;
2. append escaped literal text to a format builder;
3. append `%s` for every string variable segment;
4. add each variable's target identifier to the ordered argument list;
5. append the required `\n`;
6. emit one `printf` statement.

Illustrative pseudocode:

```csharp
private static void AppendCPrint(
    StringBuilder source,
    BoundPrintStatement print)
{
    if (print.IsBlankLine)
    {
        source.AppendLine("    printf(\"\\n\");");
        return;
    }

    CPrintfPlan plan = BuildCPrintfPlan(print.Value);

    source.Append("    printf(");
    source.Append(TargetEscapes.CPrintfFormatString(plan.FormatText));

    foreach (string argument in plan.Arguments)
    {
        source.Append(", ");
        source.Append(argument);
    }

    source.AppendLine(");");
}
```

Exact class and method names may differ.

Follow KISS. Do not create an unnecessary formatting framework or external dependency.

## 6.1 Avoid double escaping

Be precise about whether `FormatText` contains:

- raw runtime text; or
- already escaped C source text.

Choose one representation and document it.

Do not call general C escaping twice.

## 6.2 No temporary concatenated buffers

Do not generate:

- `sprintf` into a temporary buffer;
- heap allocation;
- repeated `strcat`;
- manual string-length arithmetic;
- unnecessary helper runtime functions.

For the current string-only `PRINT`, a direct `printf` call is simpler and more natural.

---

# 7. Human-natural C formatting

Preserve statement order.

Do not move declarations or statements merely to make the output look grouped.

For the common initial declaration block:

1. emit declarations in source order;
2. add one blank line before the first executable statement;
3. add one blank line before `return 0;` when the body contains statements;
4. do not insert arbitrary blank lines between groups of `PRINT` calls unless SMILE later preserves source trivia/blank lines explicitly.

The desired shape is:

```c
int main(void)
{
    /* declarations */

    /* executable statements */

    return 0;
}
```

Do not expand this task into source-trivia preservation.

---

# 8. Apply the general rule across all target languages

Audit all seven current targets against the new standard.

The audit is required, but do not create output churn where a generator is already idiomatic.

## 8.1 C#

Status: approved.

Keep:

- `Console.WriteLine();`
- ordinary string literals;
- C# interpolation;
- explicit `+` concatenation when SMILE used explicit concatenation.

No C# code-generation change is requested.

## 8.2 JavaScript

The current direction is idiomatic:

- `console.log();`
- string literals;
- template literals for interpolation;
- `+` for explicit concatenation.

Keep the current expression-intent behavior unless tests reveal a defect.

## 8.3 Java

The current direction is idiomatic:

- `System.out.println();`
- string literals;
- `+` concatenation as the fallback for interpolation;
- `+` for explicit concatenation.

Do not add a custom interpolation runtime.

## 8.4 Swift

The current direction is idiomatic:

- `print()`
- string literals;
- native `\(value)` interpolation;
- explicit `+` when the SMILE source used explicit concatenation.

Keep it unless tests reveal a defect.

## 8.5 Objective-C

The current Objective-C backend also uses per-segment `fputs` calls.

Apply the same human-readable single-call principle when safe.

Do not use `NSLog`, because it changes console output by adding timestamps and process metadata.

For the current `NSString *` variables, prefer output such as:

```objective-c
printf("Hello %s!\n", [Name UTF8String]);
```

Plain literal:

```objective-c
printf("Hello World!\n");
```

Blank line:

```objective-c
printf("\n");
```

Escape literal `%` as `%%`.

Preserve the current `@autoreleasepool` and Foundation structure.

Use one `printf` call per SMILE `PRINT` where practical.

## 8.6 MASM x64

Do not force MASM to call the C runtime merely to resemble C.

The existing Win32 `WriteFile` approach may remain because it is:

- dependency-light;
- explicit;
- educational;
- natural for the chosen low-level Windows assembly target.

Multiple output operations are acceptable when required by the target's natural implementation.

Still audit for:

- unnecessary repeated setup;
- confusing labels;
- unstable ordering;
- redundant scaffolding;
- comments that explain mechanics without overwhelming the code.

Do not make unrelated MASM changes in this task unless there is a clear defect.

## 8.7 Future targets

Every new target generator must document:

- its idiomatic declaration form;
- its idiomatic blank-line output;
- its idiomatic literal output;
- its interpolation strategy;
- its explicit-concatenation strategy;
- its fallback when no native equivalent exists;
- its escaping rules;
- its target-identifier mapping rules;
- required runtime/toolchain dependencies.

---

# 9. Identifier policy for this task

Do not conflate idiomatic expression/statement generation with identifier renaming.

For now:

- preserve SMILE declaration spelling when it is valid in the target;
- map an identifier only when target validity requires it;
- never perform identifier conversion through string replacement;
- use symbol-based target naming;
- handle target reserved words deterministically;
- prevent collisions.

Do not change `Name` to `name` in C in this patch unless the official identifier policy is separately revised and a complete target-name mapper is implemented and tested.

This restriction keeps the current C# output unchanged and follows the existing official specification.

---

# 10. Required tests

Update existing generator tests rather than only adding weak substring checks.

## 10.1 Exact C sample test

Replace or rename the current test named similarly to:

```text
C_generator_produces_minimal_puts_program
```

Use a name such as:

```text
C_generator_produces_idiomatic_printf_program
```

Assert the exact generated C for the primary sample:

```c
#include <stdio.h>

int main(void)
{
    const char *Name = "Sin";

    printf("\n");
    printf("Hello World!\n");
    printf("Hello World!\n");
    printf("Hello %s!\n", Name);
    printf("Hello %s!\n", Name);
    printf("Hello %s!\n", Name);

    return 0;
}
```

## 10.2 One call per PRINT

For the six `PRINT` statements in the sample:

- assert exactly six `printf(` calls;
- assert there are no `fputs(` calls;
- assert there are no `putchar(` calls.

## 10.3 Percent escaping

Test:

```smile
PRINT Progress: 100%
```

Expected generated fragment:

```c
printf("Progress: 100%%\n");
```

Compile and run it.

Expected runtime output:

```text
Progress: 100%
```

## 10.4 Percent plus variable

Test:

```smile
LET Name = "Sin"
PRINT {Name} is 100% ready.
```

Expected generated fragment:

```c
printf("%s is 100%% ready.\n", Name);
```

## 10.5 Multiple variables

Test:

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
PRINT $"{FirstName} {LastName}"
```

Expected generated fragment:

```c
printf("%s %s\n", FirstName, LastName);
```

## 10.6 Adjacent and repeated variables

Test:

```smile
LET A = "A"
LET B = "B"
PRINT $"{A}{B}{A}"
```

Expected generated fragment:

```c
printf("%s%s%s\n", A, B, A);
```

Expected runtime output:

```text
ABA
```

## 10.7 Literal braces

Test:

```smile
LET Name = "Sin"
PRINT Literal braces: {{Name}}
PRINT $"Literal braces: {{Name}}"
```

The generated C should use literal braces in the format text and must not add an argument for `{Name}`.

Expected runtime lines:

```text
Literal braces: {Name}
Literal braces: {Name}
```

## 10.8 Ordinary quoted braces

Test:

```smile
PRINT "Hello {Name}!"
```

Expected generated C:

```c
printf("Hello {Name}!\n");
```

No interpolation and no variable argument.

## 10.9 Blank versus empty string

Test:

```smile
PRINT
PRINT ""
```

Both may generate:

```c
printf("\n");
```

because C has no useful source-level distinction for their identical output.

The C# distinction must remain unchanged:

```csharp
Console.WriteLine();
Console.WriteLine("");
```

## 10.10 Existing escaping regression tests

Preserve and update tests for:

- backslashes;
- quotation marks;
- control characters;
- literal percent signs;
- deterministic output;
- exactly one trailing file newline.

## 10.11 Objective-C tests

If Objective-C is changed as required:

- assert one `printf` per `PRINT`;
- assert `%s` arguments use `[Name UTF8String]`;
- assert `%` escaping;
- assert no `NSLog`;
- assert no per-segment `fputs` for the tested forms.

## 10.12 Cross-target runtime equivalence

The same SMILE program must produce identical normalized runtime output for every currently runnable target:

- C#
- C
- MASM x64
- JavaScript
- Java

Keep Objective-C and Swift transpile-only behavior unchanged on Windows unless their existing toolchain support changes independently.

---

# 11. Documentation changes

## 11.1 `AGENTS.md`

Strengthen the existing generated-code principle.

Add the exact project rule:

> Generated target code should be semantically correct, idiomatic for the destination language, and close to code a competent human developer would naturally write.

Also state:

- one natural target statement per SMILE statement is preferred when practical;
- target-local lowering must not leak unnecessary compiler mechanics into readable generated source;
- lower-level targets are not exempt from readability, but their natural idioms and dependency constraints must be respected;
- C `PRINT` should prefer one safe `printf` call with a compiler-generated format string.

## 11.2 New public target-code generation standard

Create a canonical document such as:

```text
docs/SMILE Target Code Generation Standard v1.0.md
```

It should define:

- the priority order in Section 1;
- semantic correctness;
- safety;
- intent preservation;
- target idioms;
- human readability;
- deterministic formatting;
- minimal scaffolding;
- target-specific fallbacks;
- identifier policy;
- escaping and format-string safety;
- examples for all current targets;
- a review checklist for future generators.

This is a compiler/code-generation standard, not a new SMILE keyword specification.

## 11.3 Official PRINT specification

Update the target-generation/internal-representation guidance so it no longer suggests that C segment output is automatically preferred merely because C is lower-level.

Use wording equivalent to:

> C, Objective-C, assembly, and other lower-level targets MAY lower interpolation and concatenation into target-specific output operations. However, the generator SHOULD still choose the clearest idiomatic destination-language form. For C string output, a single safe `printf` call with a compiler-generated format string is generally preferred over exposing every internal literal and variable segment as a separate output statement.

Do not change `PRINT` runtime semantics.

## 11.4 `README.md`

Update generated-code examples and the description of target generation.

The README must reflect the actual C and Objective-C output after the implementation.

Do not claim a target is idiomatic unless its generated code has been audited.

---

# 12. Acceptance criteria

This task is complete only when all of the following are true:

1. The approved C# sample remains unchanged.
2. The sample C output uses one `printf` per SMILE `PRINT`.
3. Blank C `PRINT` generates `printf("\n");`.
4. Literal C `PRINT` generates one `printf` with the required newline.
5. C interpolation generates `%s` placeholders and ordered arguments.
6. C explicit concatenation generates the same idiomatic `printf` fallback.
7. Literal `%` characters are emitted as `%%` in C format strings.
8. No SMILE variable is ever passed as the `printf` format string.
9. Multiple, adjacent, and repeated variables work correctly.
10. Literal braces continue to work correctly.
11. Existing C escaping behavior does not regress.
12. C output preserves SMILE identifier spelling in this task.
13. The C generator does not allocate or construct temporary runtime buffers.
14. Objective-C uses a similarly idiomatic single-call form where safe.
15. MASM remains dependency-light and is not forced through the C runtime.
16. All target generators are audited under the general rule.
17. `AGENTS.md` contains the permanent general rule.
18. A public target-code generation standard is added.
19. The official PRINT specification and README are updated.
20. Debug and Release builds and tests pass.
21. CLI generation succeeds for all seven targets.
22. Runnable generated targets compile and produce identical normalized output.
23. Generated output remains deterministic.
24. The commit contains no build artifacts or unrelated changes.

---

# 13. Validation commands

Run from the repository root.

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug
```

```bat
cmd /c dotnet test SMILE.sln -c Debug --no-build
```

```bat
cmd /c dotnet build SMILE.sln -c Release
```

```bat
cmd /c dotnet test SMILE.sln -c Release --no-build
```

Run CLI generation for all seven targets.

Compile and run all currently runnable generated targets.

Validate at least these SMILE programs:

## Primary sample

```smile
LET Name = "Sin"

PRINT
PRINT "Hello World!"
PRINT Hello World!
PRINT Hello {Name}!
PRINT $"Hello {Name}!"
PRINT "Hello " + Name + "!"
```

## Percent safety

```smile
LET Name = "Sin"

PRINT Progress: 100%
PRINT {Name} is 100% ready.
```

## Multiple arguments

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"

PRINT $"{FirstName} {LastName}"
PRINT $"{FirstName}{LastName}{FirstName}"
```

## Literal braces

```smile
LET Name = "Sin"

PRINT Literal braces: {{Name}}
PRINT $"Literal braces: {{Name}}"
PRINT "Literal braces: {Name}"
```

For each runnable target, compare normalized stdout rather than only checking that compilation succeeds.

---

# 14. Implementation constraints

- Follow KISS and KISS v2.
- Do not add external libraries.
- Do not create a generic cross-language pretty-printer framework.
- Keep idiom selection inside each target backend.
- Do not reparse SMILE source in a generator.
- Continue consuming the bound language-neutral representation.
- Target-local lowering is allowed.
- Do not regress expression-intent preservation.
- Do not change the approved C# output.
- Do not rename identifiers through textual replacement.
- Do not commit generated build/output artifacts.
- Do not change unrelated UI or toolchain behavior.
- Keep desktop live transpilation asynchronous and responsive.

---

# 15. Commit guidance

Use a focused commit subject such as:

```text
Sin and Codex: Generate idiomatic human-readable target code
```

The detailed commit message should describe:

- the permanent human-natural code-generation rule;
- the C `printf` format-plan implementation;
- percent-format safety;
- Objective-C improvements, if made;
- the all-target idiomatic audit;
- unchanged approved C# behavior;
- tests added or updated;
- documentation changes;
- exact Debug and Release test counts;
- CLI generation and compile/run validation results.

Before committing:

```bat
cmd /c git diff --check
```

Review the generated C source manually and confirm that it resembles normal hand-written C rather than exposed compiler segments.
