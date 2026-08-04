# Codex Implementation Instructions — SMILE v0.4.3.1 Final Target Identifier and Header Hygiene

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work **directly on `main` only**.
- Sin is the only developer.
- **Do not create, suggest, or use a feature branch.**
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the current `main` branch and working tree before editing.
- Do not discard, reset, overwrite, or commit unrelated user work.
- Do not commit or push unless Sin explicitly authorizes it in the Codex session.
- Follow KISS and KISS v2, “The Sin Way.”
- Do not add another destination language.
- Do not add a new SMILE keyword.
- Do not begin `SET`, `INPUT`, `IF`, loops, functions, or runtime-variable work in this task.
- Do not add a third-party dependency, code-generation framework, parser generator, optimizer framework, package manager, CMake, or a SMILE runtime library.

The reviewed baseline when this brief was prepared was:

```text
442afc57092ef22945d567a6f78f19d067d9de7b
Sin and Codex: Add C++ as the final target
```

Do not assume that SHA is still current. Always start from the newest `main`.

---

# 1. Milestone

Create the focused cleanup release:

> **SMILE v0.4.3.1 — Final Target Identifier and Header Hygiene**

This release fixes the remaining issues found after the C++ target was added.

The required scope is:

1. Protect C, Objective-C, and C++ generated identifiers from fixed-width integer macros and related standard-library macros.
2. Correct C++ implementation-reserved identifier detection for double underscores appearing anywhere in a name.
3. Refine C++ header analysis so `<string>` is emitted only when generated code actually uses `std::string` or another facility requiring that header.
4. Add adversarial tests for these cases.
5. Re-run all ten targets.
6. Keep the destination-language freeze intact.

This is a small hardening release, not an architectural expansion.

---

# 2. Preserve all current behavior

Do not regress:

- C++ as the tenth and final target;
- Python as the ninth target;
- all ten stable target IDs;
- idiomatic per-program Integer profiles;
- `int Age = 49;` for ordinary C and C++ programs;
- `std::int64_t` only when the complete C++ program requires signed 64-bit storage;
- owned `std::string`;
- `std::cout`;
- embedded-NUL preservation;
- exact String equality;
- C/Objective-C exact embedded-NUL handling;
- shared known-value short-circuit simplification;
- all official escapes;
- canonical `TRUE` / `FALSE`;
- target identifier mapping;
- syntax highlighting;
- desktop responsiveness;
- build/run containment;
- Ctrl+mouse-wheel zoom;
- target-language freeze;
- Rust, Zig, and Go remaining deferred.

---

# 3. Problem A — Fixed-width integer macro collisions

## 3.1 Why this matters

The C, Objective-C, and C++ targets can include:

```c
#include <stdint.h>
```

or:

```cpp
#include <cstdint>
```

when wide Integer storage is required.

Those headers expose macro names such as:

```text
INT64_MIN
INT64_MAX
INT64_C
UINT64_C
SIZE_MAX
```

A valid SMILE identifier may use one of those spellings.

Example:

```smile
LET INT64_MAX = 49
LET Wide = 5000000000

PRINT {INT64_MAX}
PRINT {Wide}
```

The wide value causes the generated target to include the fixed-width integer header.

If `INT64_MAX` is emitted as a variable name, the preprocessor may replace it with the macro value and break compilation.

This must be prevented through target identifier mapping.

---

# 4. Required macro-name protection

## 4.1 C++ reserved names

Add the complete relevant fixed-width integer and limit-macro family to the C++ reserved-name set.

Include at least:

```text
INT8_MIN
INT8_MAX
UINT8_MAX

INT16_MIN
INT16_MAX
UINT16_MAX

INT32_MIN
INT32_MAX
UINT32_MAX

INT64_MIN
INT64_MAX
UINT64_MAX

INT_LEAST8_MIN
INT_LEAST8_MAX
UINT_LEAST8_MAX

INT_LEAST16_MIN
INT_LEAST16_MAX
UINT_LEAST16_MAX

INT_LEAST32_MIN
INT_LEAST32_MAX
UINT_LEAST32_MAX

INT_LEAST64_MIN
INT_LEAST64_MAX
UINT_LEAST64_MAX

INT_FAST8_MIN
INT_FAST8_MAX
UINT_FAST8_MAX

INT_FAST16_MIN
INT_FAST16_MAX
UINT_FAST16_MAX

INT_FAST32_MIN
INT_FAST32_MAX
UINT_FAST32_MAX

INT_FAST64_MIN
INT_FAST64_MAX
UINT_FAST64_MAX

INTPTR_MIN
INTPTR_MAX
UINTPTR_MAX

INTMAX_MIN
INTMAX_MAX
UINTMAX_MAX

PTRDIFF_MIN
PTRDIFF_MAX
SIG_ATOMIC_MIN
SIG_ATOMIC_MAX
SIZE_MAX
WCHAR_MIN
WCHAR_MAX
WINT_MIN
WINT_MAX

INT8_C
UINT8_C
INT16_C
UINT16_C
INT32_C
UINT32_C
INT64_C
UINT64_C
INTMAX_C
UINTMAX_C
```

Protect any other standard macro names already used by the generator.

Do not limit protection only to macros directly emitted today. Protect the relevant standard family consistently.

## 4.2 C and Objective-C reserved names

Apply the appropriate macro protection to:

```text
TargetLanguage.C
TargetLanguage.ObjectiveC
```

because those targets also include `<stdint.h>` when the wide Integer profile is active.

At minimum, the complete macro family above must not be emitted as raw variable identifiers in C-family targets.

## 4.3 Mapping style

Continue using the current readable deterministic mapping convention.

Example:

```smile
LET INT64_MAX = 49
```

C++:

```cpp
int _smile_INT64_MAX = 49;
```

C:

```c
int _smile_INT64_MAX = 49;
```

The exact prefix remains the existing target identifier mapping convention.

Do not special-case these names directly inside generators.

The fix belongs in `TargetIdentifierMap`.

---

# 5. Required macro-collision tests

Add tests for C, Objective-C, and C++.

## 5.1 Wide profile triggers header

Use:

```smile
LET INT64_MAX = 49
LET INT64_C = 50
LET UINT64_MAX = 51
LET SIZE_MAX = 52
LET Wide = 5000000000

PRINT {INT64_MAX}
PRINT {INT64_C}
PRINT {UINT64_MAX}
PRINT {SIZE_MAX}
PRINT {Wide}
```

Verify:

- the wide header is included;
- all conflicting SMILE identifiers are mapped;
- every reference uses the mapped name;
- generated code compiles;
- runtime output matches `SmileEvaluator`.

## 5.2 No accidental source corruption

Assert that generated code does not contain declarations shaped like:

```cpp
std::int64_t INT64_MAX =
```

or:

```c
int64_t INT64_MAX =
```

when the macro is active.

## 5.3 Determinism

Generate the same program twice and compare exact generated output.

---

# 6. Problem B — C++ identifiers containing double underscores

## 6.1 C++ reserved rule

For C++, any identifier containing:

```text
__
```

anywhere in the name is reserved to the implementation.

The current shared C-family rule maps names that:

- begin with `__`;
- begin with `_` followed by uppercase ASCII.

That is correct for C-family prefixes, but C++ has the additional rule:

> A double underscore anywhere in an identifier is reserved.

Example valid SMILE:

```smile
LET user__value = 49
PRINT {user__value}
```

C++ must not emit:

```cpp
int user__value = 49;
```

It must map the identifier.

Preferred result:

```cpp
int _smile_user__value = 49;
```

---

# 7. Required implementation-reserved identifier logic

## 7.1 Keep the C and Objective-C behavior

For C and Objective-C, preserve the current rules:

- begins with `__`;
- begins with `_` followed by uppercase ASCII.

Do not unnecessarily broaden the C rule unless justified by the C standard and the project’s existing policy.

## 7.2 Add the C++-specific rule

For `TargetLanguage.Cpp`, map an identifier when any of these is true:

```text
name begins with "__"
name begins with "_" followed by uppercase ASCII
name contains "__" anywhere
```

Examples requiring C++ mapping:

```text
__internal
_Upper
user__value
A__B
value__
```

Examples that remain safe unless otherwise reserved:

```text
_user
user_value
value_
```

## 7.3 Shared helper structure

Keep the code clear.

Preferred shape:

```text
IsCImplementationReservedIdentifier(...)
IsCppImplementationReservedIdentifier(...)
```

or an equally simple structure.

Do not overload one helper with confusing language-condition logic.

---

# 8. Required C++ reserved-identifier tests

Add tests using:

```smile
LET __internal = 1
LET _Upper = 2
LET user__value = 3
LET A__B = 4
LET value__ = 5
LET _user = 6
LET user_value = 7

PRINT {__internal}
PRINT {_Upper}
PRINT {user__value}
PRINT {A__B}
PRINT {value__}
PRINT {_user}
PRINT {user_value}
```

Verify:

- the first five are mapped;
- `_user` remains unchanged unless another reserved-name rule applies;
- `user_value` remains unchanged;
- all references match declarations;
- generated C++ builds and runs;
- output matches `SmileEvaluator`.

Also verify C and Objective-C preserve their intended existing prefix behavior.

---

# 9. Problem C — Unnecessary C++ `<string>` header

## 9.1 Current issue

A directly streamed raw or interpolated `PRINT` expression may not need `std::string`.

Example:

```smile
PRINT Age={49}
```

Natural C++:

```cpp
#include <iostream>

int main()
{
    std::cout << "Age=" << 49 << '\n';

    return 0;
}
```

`<string>` is unnecessary because generated code does not create:

- a `std::string`;
- `std::to_string`;
- length-aware String construction;
- String concatenation;
- String variables.

The C++ generator should honor its minimal-header policy.

---

# 10. Required C++ header analysis

Refine `CppGenerationFacts.NeedsStringHeader`.

The decision must be based on generated C++ facilities, not simply on the presence of a SMILE interpolated or raw template.

## 10.1 `<string>` is required when generated code uses:

```text
std::string variable declarations
std::string literal construction
std::to_string
String concatenation
String equality involving std::string
an interpolated String assigned to LET
embedded-NUL length-aware std::string
String copies
```

## 10.2 `<string>` is not required merely for:

```text
a directly streamed ordinary String literal
a directly streamed raw PRINT template
a directly streamed Integer hole
a directly streamed Boolean hole
a directly streamed NUL-free literal text segment
```

Example that should need only `<iostream>`:

```smile
PRINT Age={49}, Adult={TRUE}
```

Preferred generated includes:

```cpp
#include <iostream>
```

and not:

```cpp
#include <string>
```

## 10.3 Cases that still require `<string>`

These must include `<string>`:

```smile
LET Name = "Sin"
```

```smile
LET Message = $"Age={49}"
```

```smile
LET Text = "A" + "B"
```

```smile
LET Text = "A\0B"
```

```smile
LET Same = "A" = "A"
```

when the C++ expression writer creates an owning `std::string` to preserve value semantics.

---

# 11. Required C++ header tests

Add exact or structural tests.

## 11.1 Direct streamed template

SMILE:

```smile
PRINT Age={49}, Adult={TRUE}
```

Required:

```text
includes <iostream>
does not include <string>
does not include <cstdint>
```

## 11.2 Direct String literal

SMILE:

```smile
PRINT "Hello"
```

Required:

```text
includes <iostream>
does not include <string>
```

provided generated code streams a normal literal directly.

## 11.3 String variable

SMILE:

```smile
LET Name = "Sin"
PRINT {Name}
```

Required:

```text
includes <iostream>
includes <string>
```

## 11.4 String interpolation assigned to LET

SMILE:

```smile
LET Message = $"Age={49}"
PRINT {Message}
```

Required:

```text
includes <iostream>
includes <string>
```

## 11.5 Wide Integer only

SMILE:

```smile
LET Wide = 5000000000
```

Required:

```text
includes <cstdint>
does not include <iostream>
does not include <string>
```

## 11.6 Determinism

Generate twice and compare byte-for-byte.

---

# 12. Review all ten target lists

C++ was added to the target matrices.

Verify it remains included in every applicable list:

```text
TargetLanguageInfo.All
CodeGeneratorRegistry
ToolchainRegistry
CLI --target all
desktop selectors
syntax-highlighting catalog
deterministic generation tests
typed-expression corpus
wide Integer tests
exact String tests
short-circuit tests
toolchain integration tests
```

Do not add another target.

---

# 13. Keep the target-language freeze

Preserve or strengthen this active project rule:

> C++ is SMILE’s tenth and final planned destination language. Target-language expansion is frozen unless Sin explicitly reopens it.

Rust, Zig, and Go remain deferred.

Do not introduce an “ideas” list suggesting more target languages.

Future milestones must focus on language depth.

---

# 14. Documentation updates

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- `docs/SMILE Target Code Generation Standard v1.0.md`;
- requirements/history;
- desktop version/About metadata if the version is bumped.

## 14.1 Version

Use:

```text
SMILE v0.4.3.1 — Final Target Identifier and Header Hygiene
```

## 14.2 Architecture

Document:

- why preprocessor macros can collide with generated identifiers;
- why target identifier mapping protects standard macro names;
- the C++ double-underscore rule;
- facility-based minimal-header analysis;
- why directly streamed templates do not necessarily require `<string>`.

## 14.3 Target generation standard

Add normative wording equivalent to:

> A target identifier map must protect destination preprocessor macros and implementation-reserved identifiers, not only language keywords.

Add:

> C++ header emission is facility-driven. Emit `<string>` only when generated code actually uses `std::string`, `std::to_string`, or another String-library facility.

---

# 15. AGENTS.md additions

Preserve all existing rules.

Add wording equivalent to:

> C, Objective-C, and C++ target identifier maps must protect standard fixed-width Integer macro names whenever those names could be active in generated translation units.

Add:

> C++ identifiers containing a double underscore anywhere are implementation-reserved and must be mapped.

Add:

> C++ headers must be emitted according to generated facilities, not merely according to broad SMILE expression categories.

---

# 16. Roadmap

Add:

## Implemented in v0.4.3.1

- fixed-width macro collision protection;
- C++ double-underscore identifier hardening;
- facility-based C++ minimal headers;
- all-ten-target regression validation.

Keep:

```text
Next Major Milestone:
v0.5.0 — Runtime Variables and SET
```

Do not implement `SET` in this task.

---

# 17. Scope exclusions

Do not implement:

- `SET`;
- mutable variables;
- runtime storage changes;
- `INPUT`;
- `IF`;
- loops;
- functions;
- scopes;
- arrays;
- floating-point types;
- another destination language;
- Rust;
- Zig;
- Go;
- CMake;
- package managers;
- a general optimizer;
- a feature branch.

---

# 18. Acceptance criteria

This task is complete only when all are true:

1. C++ maps `INT64_MAX`.
2. C++ maps `INT64_C`.
3. C++ maps `UINT64_MAX`.
4. C++ maps `SIZE_MAX`.
5. C maps the relevant macro family.
6. Objective-C maps the relevant macro family.
7. wide-profile macro-collision examples compile.
8. declaration and reference mappings are consistent.
9. mapping remains deterministic.
10. C++ maps identifiers containing `__` anywhere.
11. C++ still maps `__prefix`.
12. C++ still maps `_Upper`.
13. C++ does not unnecessarily map `_user`.
14. C++ does not unnecessarily map `user_value`.
15. direct streamed templates omit `<string>` when unused.
16. direct String literals omit `<string>` when streamed directly and no String facility is used.
17. String variables still include `<string>`.
18. String concatenation still includes `<string>`.
19. String interpolation assigned to LET still includes `<string>`.
20. embedded-NUL C++ Strings still include `<string>`.
21. wide Integer-only programs include `<cstdint>` and no unrelated headers.
22. all ten targets remain available.
23. C++ remains the final target.
24. all existing C++ tests remain green.
25. all exact-byte tests remain green.
26. all Integer-profile tests remain green.
27. all identifier tests remain green.
28. all ten installed targets match `SmileEvaluator`.
29. Debug build has zero warnings.
30. Release build has zero warnings.
31. Debug tests pass.
32. Release tests pass.
33. every installed target builds/runs with exit code zero.
34. documentation matches implementation.
35. no new target or syntax is added.
36. no unapproved dependency is added.
37. no build artifacts are committed.
38. all work is performed directly on `main`.

---

# 19. Suggested implementation sequence

1. Confirm newest `main`.
2. Add failing macro-collision tests.
3. Expand C/C++/Objective-C reserved macro names.
4. Add failing C++ double-underscore tests.
5. implement C++-specific reserved-identifier detection.
6. Add failing minimal-header tests.
7. refine `CppGenerationFacts`.
8. run focused generator tests.
9. run Debug build/tests.
10. run Release build/tests.
11. generate all ten targets.
12. explicitly build/run C, Objective-C, and C++ macro-collision examples.
13. explicitly build/run all ten installed targets.
14. perform desktop smoke testing.
15. update documentation.
16. commit directly to `main` only when Sin explicitly authorizes it.

---

# 20. Validation commands

Run from the repository root:

```bat
cmd /c git status --short --branch
```

Confirm:

```text
main
```

```bat
cmd /c dotnet restore SMILE.sln
```

```bat
cmd /c dotnet build SMILE.sln -c Debug -nologo
```

```bat
cmd /c dotnet test SMILE.sln -c Debug --no-build -nologo
```

```bat
cmd /c dotnet build SMILE.sln -c Release -nologo
```

```bat
cmd /c dotnet test SMILE.sln -c Release --no-build -nologo
```

Generate all ten targets:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target all
```

Run C++:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <MACRO_COLLISION_EXAMPLE.smile> --target cpp --run
```

Run C:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <MACRO_COLLISION_EXAMPLE.smile> --target c --run
```

Run Objective-C:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <MACRO_COLLISION_EXAMPLE.smile> --target objective-c --run
```

Before an authorized commit:

```bat
cmd /c git diff --check
```

```bat
cmd /c git diff --stat
```

```bat
cmd /c git status --short --branch
```

---

# 21. Manual desktop validation

1. Launch SMILE Desktop.
2. Select C++.
3. Paste:

```smile
LET INT64_MAX = 49
LET Wide = 5000000000

PRINT {INT64_MAX}
PRINT {Wide}
```

4. Confirm the variable is mapped.
5. Build and run C++.
6. Confirm output is:

```text
49
5000000000
```

7. Paste:

```smile
LET user__value = 49
PRINT {user__value}
```

8. Confirm C++ maps the identifier.
9. Build and run.
10. Paste:

```smile
PRINT Age={49}, Adult={TRUE}
```

11. Confirm generated C++ includes `<iostream>` but not `<string>`.
12. Rapidly switch C++ → C → Python → C++.
13. Confirm responsiveness.
14. Confirm Build & Run failures remain contained.
15. Confirm About shows v0.4.3.1 if version metadata is updated.

---

# 22. Completion report

Report:

- exact baseline commit;
- exact files changed;
- full macro family protected;
- C mapping behavior;
- Objective-C mapping behavior;
- C++ mapping behavior;
- C++ double-underscore logic;
- header-analysis changes;
- exact header tests;
- exact Debug test count;
- exact Release test count;
- zero-warning results;
- C runtime result;
- Objective-C runtime result;
- C++ runtime result;
- all-ten-target run summary;
- desktop smoke results;
- documentation changes;
- confirmation that no new target or keyword was added;
- unresolved concerns.

Do not claim macro-collision conformance based only on source inspection. Build and run the adversarial examples.

---

# 23. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

Suggested subject:

```text
Sin and Codex: Harden final target identifiers and headers
```

Suggested body topics:

- fixed-width macro collision protection;
- C++ double-underscore mapping;
- facility-based minimal C++ headers;
- all-ten-target regression validation;
- destination-language freeze preserved;
- exact Debug/Release validation totals.
