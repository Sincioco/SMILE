# Codex Implementation Instructions — SMILE v0.4.3 Add C++ as the Final Target Language

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
- Do not add a compiler framework, code-generation framework, template engine, CLI framework, third-party C++ library, package manager, or SMILE runtime library.

---

# 1. Mandatory prerequisite

This milestone must begin only after:

> **SMILE v0.4.2.1 — Exact String and Target-Safe Expression Hardening**

is complete, committed, and green on `main`.

Before adding C++, verify that `main` includes:

- exact embedded-NUL preservation for C and Objective-C;
- exact NUL-sensitive String equality;
- known-value short-circuit simplification in every expression position;
- exact-byte conformance testing;
- Python as the ninth target;
- idiomatic per-program Integer profiles;
- passing Debug and Release tests.

If v0.4.2.1 is incomplete:

1. stop;
2. do not partially add C++;
3. report that the hardening milestone must finish first.

---

# 2. Milestone

Create:

> **SMILE v0.4.3 — C++ Final Target**

Add C++ as SMILE's tenth and final planned destination language.

The supported targets become:

```text
C#
C
Assembly - Windows x64 MASM
JavaScript
Java
COBOL
Objective-C
Swift
Python
C++
```

This milestone adds one destination target only.

Do not change SMILE source-language syntax or semantics.

Do not add:

- another keyword;
- another SMILE type;
- `SET`;
- reassignment;
- `INPUT`;
- `IF`;
- loops;
- functions;
- arrays;
- floating-point values.

---

# 3. Final target-language freeze

C++ is the final target language in the active SMILE roadmap.

After v0.4.3:

> **Destination-language expansion is frozen.**

Add a clearly visible rule to `README.md`, `docs/Roadmap.md`, and `AGENTS.md` equivalent to:

> C++ is SMILE's tenth and final planned destination language. After C++ is complete, target-language expansion is frozen so development can focus on runtime variables, assignment, conditions, input, loops, functions, scopes, debugging, and teaching tools. Do not add another destination language unless Sin explicitly reopens target expansion.

Rust, Zig, and Go remain intentionally deferred and are not active targets.

Do not delete historical files that discussed possible targets.

Mark contradictory active requirements as superseded.

---

# 4. C++ target identity

Append C++ to `TargetLanguage` without reordering existing enum members:

```csharp
Cpp
```

Use:

```text
Stable ID:       cpp
Display name:    C++
Primary file:    Program.cpp
Action label:    Build & Run
```

Add it after Python in `TargetLanguageInfo.All`.

`--target all` must generate ten targets.

Update:

- stable ID parsing;
- display names;
- primary filename;
- target ordering;
- CLI help;
- desktop selectors;
- supported-target tables;
- tests.

---

# 5. C++ design goal

Generated C++ must look like modern, conventional code a competent C++ programmer might naturally write.

Do not implement C++ by merely renaming generated C code from `.c` to `.cpp`.

The C++ target must use:

- `std::string`;
- `std::cout`;
- `std::int64_t` only when wide storage is required;
- ordinary `int` for normal small programs;
- native value-based String equality;
- RAII-owned values;
- `'\n'` instead of `std::endl`;
- no `using namespace std;`;
- no `printf`;
- no `strcmp`;
- no raw `char *` representation for ordinary SMILE String variables.

---

# 6. Preferred generated program shape

SMILE:

```smile
LET Name = "Sin"
LET Age = 49
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
LET Message = $"{Name} is {Age}. Adult={Adult}"

PRINT {Message}
PRINT Age={Age}
```

Preferred C++:

```cpp
#include <iostream>
#include <string>

int main()
{
    std::string Name = "Sin";
    int Age = 49;
    bool Adult = Age >= 18;
    bool WorkingAge = Adult;
    std::string Message =
        Name + " is " + std::to_string(Age) +
        ". Adult=" + (Adult ? "TRUE" : "FALSE");

    std::cout << Message << '\n';
    std::cout << "Age=" << Age << '\n';

    return 0;
}
```

Formatting may differ slightly, but the generated code must remain:

- conventional;
- readable;
- deterministic;
- educational;
- dependency-free.

---

# 7. C++ type mapping

Use the existing per-program Integer profile.

```text
SMILE String  -> std::string
SMILE Integer -> int or std::int64_t
SMILE Boolean -> bool
```

## Small profile

```cpp
int Age = 49;
```

## Wide profile

```cpp
std::int64_t Population = INT64_C(5000000000);
```

Include `<cstdint>` only when needed.

For exact minimum value, use a valid clear representation such as:

```cpp
INT64_MIN
```

or:

```cpp
std::numeric_limits<std::int64_t>::min()
```

If using `std::numeric_limits`, include `<limits>` only when needed.

Do not emit `LL` suffixes in small-profile programs.

---

# 8. C++ String ownership

Represent SMILE String variables as owned `std::string`.

Examples:

## Literal

```smile
LET Name = "Sin"
```

```cpp
std::string Name = "Sin";
```

## Copy

```smile
LET Copy = Name
```

```cpp
std::string Copy = Name;
```

## Concatenation

```smile
LET FullName = FirstName + " " + LastName
```

```cpp
std::string FullName = FirstName + " " + LastName;
```

## Interpolation

```smile
LET Message = $"Hello {Name}. Age={Age}, Adult={Adult}"
```

```cpp
std::string Message =
    std::string{"Hello "} + Name +
    ". Age=" + std::to_string(Age) +
    ", Adult=" + (Adult ? "TRUE" : "FALSE");
```

Do not add a custom String class.

---

# 9. C++ String concatenation validity

C++ does not allow:

```cpp
"A" + "B"
```

because both operands are pointers.

The expression writer must guarantee a `std::string` operand begins any generated String concatenation chain when required.

Examples:

```smile
LET Value = "A" + "B"
```

must become something like:

```cpp
std::string Value = std::string{"A"} + "B";
```

This is invalid and must never be emitted:

```cpp
std::string Value = "A" + "B";
```

When a variable already provides `std::string`, preserve the natural form:

```cpp
std::string Value = Prefix + "B";
```

---

# 10. Embedded NUL and exact String values

C++ `std::string` can contain embedded NUL bytes.

The generator must preserve complete SMILE String values.

For:

```smile
LET Text = "A\0B"
PRINT {Text}
```

generate a length-aware String construction such as:

```cpp
std::string Text{"A\000B", 3};
std::cout << Text << '\n';
```

The exact spelling may differ, but it must preserve:

```text
41 00 42
```

before the newline.

Do not generate:

```cpp
std::string Text = "A\0B";
```

if it would truncate construction at the NUL.

Use the UTF-8 byte length, not UTF-16 character count.

## Equality

Normal `std::string` equality is correct and length-aware:

```cpp
bool Same = Left == Right;
```

It must work for embedded NUL.

Do not lower C++ String equality to `strcmp`.

---

# 11. C++ operators

Map:

```text
+    -> +
-    -> -
*    -> *
/    -> /

=    -> ==
<>   -> !=
<    -> <
<=   -> <=
>    -> >
>=   -> >=

NOT  -> !
AND  -> &&
OR   -> ||
```

Signed C++ integer division truncates toward zero.

The SMILE binder/evaluator continues rejecting:

- division by zero;
- signed 64-bit overflow;
- `-9223372036854775808 / -1`.

Preserve short-circuit evaluation.

---

# 12. C++ precedence

Use a dedicated precedence-aware C++ expression writer or a carefully extended shared target writer.

Preserve:

- unary precedence;
- multiplication/division;
- addition/subtraction;
- comparison;
- equality;
- logical `&&`;
- logical `||`;
- right-child parentheses for subtraction/division;
- nested equality structure.

Required examples:

```smile
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 - (3 - 1)
LET D = 100 / (10 / 2)
LET E = TRUE = (FALSE = FALSE)
```

Generated C++ must preserve the bound tree exactly.

---

# 13. C++ Boolean display

C++ stream insertion would normally display:

```text
0
1
```

or, with `std::boolalpha`:

```text
false
true
```

SMILE requires:

```text
FALSE
TRUE
```

Use:

```cpp
Adult ? "TRUE" : "FALSE"
```

for direct output and String construction.

Do not globally enable `std::boolalpha`.

---

# 14. C++ Integer-to-String conversion

For String interpolation and String concatenation with an Integer, use:

```cpp
std::to_string(value)
```

or a small generated helper if a helper materially improves consistency.

Do not add a formatting library.

Direct `PRINT` should use stream insertion rather than converting first:

```cpp
std::cout << Age << '\n';
```

---

# 15. C++ PRINT generation

Use `std::cout`.

## Blank PRINT

```smile
PRINT
```

```cpp
std::cout << '\n';
```

## String

```smile
PRINT {Name}
```

```cpp
std::cout << Name << '\n';
```

## Integer

```smile
PRINT {Age}
```

```cpp
std::cout << Age << '\n';
```

## Boolean

```smile
PRINT {Adult}
```

```cpp
std::cout << (Adult ? "TRUE" : "FALSE") << '\n';
```

## Raw template

```smile
PRINT Name={Name}, Age={Age}, Adult={Adult}
```

```cpp
std::cout
    << "Name=" << Name
    << ", Age=" << Age
    << ", Adult=" << (Adult ? "TRUE" : "FALSE")
    << '\n';
```

## Literal percent signs

No special escaping is required for `std::cout`.

## Embedded NUL

Streaming a `std::string` must preserve all bytes in its length.

---

# 16. Required headers

Emit only what is needed.

Possible headers:

```cpp
#include <iostream>
#include <string>
#include <cstdint>
#include <limits>
```

Rules:

- `<iostream>` only when `PRINT` is present.
- `<string>` when String values or String output require it.
- `<cstdint>` only for wide Integer profile.
- `<limits>` only if using `std::numeric_limits`.

Do not emit unused headers.

Do not use `using namespace std;`.

---

# 17. C++ generator

Add:

```text
CppCodeGenerator
```

to `CodeGeneratorRegistry`.

The generator must:

- consume `BoundProgram`;
- use `TargetIdentifierMap`;
- use the shared simplified bound program;
- use `TargetIntegerProfile`;
- preserve exact String values;
- preserve natural expressions;
- produce one primary file;
- ensure one trailing newline;
- be deterministic;
- avoid C-style fallback code.

Do not reparse SMILE source text.

---

# 18. C++ identifier mapping

Add C++ target restrictions to `TargetIdentifierMap`.

Include all C++20 keywords and relevant alternative tokens, including at least:

```text
alignas
alignof
and
and_eq
asm
auto
bitand
bitor
bool
break
case
catch
char
char8_t
char16_t
char32_t
class
compl
concept
const
consteval
constexpr
constinit
const_cast
continue
co_await
co_return
co_yield
decltype
default
delete
do
double
dynamic_cast
else
enum
explicit
export
extern
false
float
for
friend
goto
if
inline
int
long
mutable
namespace
new
noexcept
not
not_eq
nullptr
operator
or
or_eq
private
protected
public
register
reinterpret_cast
requires
return
short
signed
sizeof
static
static_assert
static_cast
struct
switch
template
this
thread_local
throw
true
try
typedef
typeid
typename
union
unsigned
using
virtual
void
volatile
wchar_t
while
xor
xor_eq
```

Protect generated/runtime names:

```text
std
main
cout
string
to_string
int64_t
smile_text
```

Apply C/C++ implementation-reserved identifier rules:

- names beginning `__`;
- names beginning `_` followed by uppercase ASCII.

Use deterministic collision-safe mapping.

---

# 19. Syntax highlighting

Use AvalonEdit's built-in C++ highlighting under stable ID:

```text
cpp
```

Do not add a custom C++ XSHD unless the built-in definition is unavailable.

Add tests verifying:

- definition loads;
- C++ target resolves to C++ highlighting;
- switching C → C++ → Python → C++ remains responsive;
- source is tokenized without exceptions.

---

# 20. C++ toolchain

Add:

```text
MsvcCppToolchain
```

or another clearly named dedicated C++ toolchain.

Use the existing `VisualStudioLocator`.

Do not install another compiler.

Detection requires the Visual Studio x64 C++ tools already used by C and MASM.

Preferred build command:

```bat
cl.exe /nologo /EHsc /std:c++20 /utf-8 Program.cpp /Fe:Program.exe
```

Do not compile `Program.cpp` as C.

Run:

```text
Program.exe
```

Use the existing:

- temporary workspaces;
- build timeout;
- program timeout;
- cancellation;
- bounded stdout/stderr;
- pause launcher;
- crash containment;
- old-workspace cleanup.

---

# 21. Desktop integration

C++ must:

- appear in every generated-pane selector;
- use C++ highlighting;
- support Copy;
- support Save Source;
- support Open Generated Folder;
- support Build & Run;
- support Press Any Key launcher;
- show toolchain availability;
- remain asynchronous and cancellable;
- never close SMILE on recoverable failure;
- use cached generation by source revision;
- remain responsive during rapid target switching.

Do not replace the user's default pane selections unless required.

---

# 22. CLI integration

Valid targets become:

```text
csharp
c
masm-x64
javascript
java
cobol
objective-c
swift
python
cpp
all
```

Example:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target cpp --run
```

`--target all` must generate ten targets.

Invalid-target help must list `cpp`.

---

# 23. Required C++ generator tests

Add focused tests for:

- `Program.cpp`;
- no `using namespace std`;
- `std::string`;
- ordinary `int`;
- wide `std::int64_t`;
- `std::cout`;
- `'\n'`;
- Boolean `TRUE`/`FALSE`;
- String copy;
- String concatenation;
- literal-plus-literal concatenation;
- interpolation;
- String equality;
- embedded NUL construction;
- embedded NUL output;
- embedded NUL equality;
- Integer precedence;
- Boolean precedence;
- identifier mapping;
- deterministic generation;
- minimal headers.

---

# 24. Required C++ acceptance examples

## Idiomatic small program

SMILE:

```smile
LET Age = 49
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
```

Required shape:

```cpp
int Age = 49;
bool Adult = Age >= 18;
bool WorkingAge = Adult;
```

## String program

```smile
LET FirstName = "Sin"
LET LastName = "Cioco"
LET FullName = FirstName + " " + LastName
PRINT {FullName}
```

Expected C++ shape:

```cpp
std::string FirstName = "Sin";
std::string LastName = "Cioco";
std::string FullName = FirstName + " " + LastName;
std::cout << FullName << '\n';
```

## Literal concatenation

```smile
LET Text = "A" + "B"
```

Must use a valid `std::string` left operand.

## Embedded NUL

```smile
LET A = "A\0B"
LET B = "A\0C"

PRINT {A}
PRINT {A = B}
```

Output must preserve the NUL and print `FALSE`.

---

# 25. All-ten-target conformance

Add C++ to every applicable target list and test matrix.

Run:

- shipped examples;
- typed-expression corpus;
- wide Integer profile;
- short-circuit hardening cases;
- exact String escape cases;
- embedded NUL cases;
- identifier hardening;
- empty String cases.

For each installed target:

1. evaluate with `SmileEvaluator`;
2. generate;
3. build/run;
4. compare exact output;
5. normalize line endings only;
6. verify exit code zero.

C++ must participate in deterministic generation tests.

---

# 26. Documentation

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- `docs/Toolchains.md`;
- target code generation standard;
- CLI help/examples;
- desktop version/About;
- requirements/history.

## Version

Use:

```text
SMILE v0.4.3 — C++ Final Target
```

## Supported target table

Add:

| Stable ID | Display name | File | Toolchain |
|---|---|---|---|
| `cpp` | C++ | `Program.cpp` | Visual Studio x64 C++ tools |

## Architecture

Document:

- owned `std::string`;
- exact embedded-NUL preservation;
- value-based String equality;
- `std::cout`;
- Integer profile;
- C++20 MSVC toolchain;
- why C++ is not generated as renamed C;
- final target freeze.

---

# 27. AGENTS.md additions

Preserve all existing rules.

Add:

> C++ is the tenth and final planned destination language. After it is implemented, do not add or recommend another target language unless Sin explicitly reopens target expansion.

Add:

> C++ generation must use idiomatic C++ facilities such as `std::string`, `std::cout`, native value equality, and RAII ownership. Do not emit C-style `printf`, `strcmp`, or raw `char *` code merely because the C target already exists.

Add:

> C++ String generation must preserve embedded NUL bytes through length-aware `std::string` construction.

---

# 28. Roadmap after C++

Record C++ as implemented.

Then the roadmap must prioritize language depth:

```text
v0.5.0 — Runtime Variables and SET
v0.6.0 — IF / THEN / ELSE
v0.7.0 — INPUT
v0.8.0 — Loops
v0.9.0 — Functions and scopes
```

No additional target-language milestone should appear.

Rust, Zig, and Go remain deferred unless Sin explicitly changes direction.

---

# 29. Scope exclusions

Do not:

- add another target;
- add Rust;
- add Zig;
- add Go;
- implement `SET`;
- add mutable variables;
- add `INPUT`;
- add `IF`;
- add loops;
- add functions;
- add arrays;
- add floating-point support;
- add CMake;
- add Conan;
- add vcpkg;
- add a package manager;
- add a C++ runtime library;
- add a feature branch.

---

# 30. Acceptance criteria

Complete only when:

1. C++ is appended to `TargetLanguage`.
2. stable ID is `cpp`.
3. display name is `C++`.
4. primary file is `Program.cpp`.
5. `--target all` generates ten targets.
6. C++ consumes `BoundProgram`.
7. C++ does not reparse SMILE.
8. Strings use `std::string`.
9. ordinary Integers use `int`.
10. wide Integers use `std::int64_t`.
11. Booleans use `bool`.
12. output uses `std::cout`.
13. no `using namespace std`.
14. no `printf`.
15. no `strcmp`.
16. no ordinary raw `char *` String variables.
17. String equality is value-based.
18. embedded NUL is preserved.
19. literal-plus-literal concatenation compiles.
20. interpolation preserves canonical Integer/Boolean text.
21. Boolean output is `TRUE`/`FALSE`.
22. short-circuit behavior is preserved.
23. precedence is correct.
24. identifiers are safely mapped.
25. highlighting loads.
26. Build & Run uses MSVC C++ tools.
27. pause launcher works.
28. desktop remains responsive.
29. all ten targets match `SmileEvaluator`.
30. Debug build has zero warnings.
31. Release build has zero warnings.
32. Debug tests pass.
33. Release tests pass.
34. installed C++ builds/runs with exit code zero.
35. documentation matches implementation.
36. C++ is documented as the final target.
37. active roadmap contains no later target-language milestone.
38. Rust, Zig, and Go remain deferred.
39. no unapproved dependency is added.
40. no build artifacts are committed.
41. all work is performed directly on `main`.

---

# 31. Suggested implementation sequence

1. Confirm v0.4.2.1 is complete.
2. Add C++ target metadata and tests.
3. Add C++ identifier mapping.
4. Implement C++ String literal generation.
5. Implement C++ expression writer.
6. Implement interpolation and concatenation.
7. Implement `std::cout` PRINT generation.
8. Implement wide Integer profile.
9. Implement embedded-NUL handling.
10. Add C++ generator to registry.
11. Add MSVC C++ toolchain.
12. Add highlighting.
13. Update desktop.
14. Update CLI.
15. Add evaluator-versus-C++ tests.
16. Run all ten targets.
17. Perform desktop smoke tests.
18. Update documentation and final-target freeze.
19. Commit directly to `main` only when Sin authorizes it.

---

# 32. Validation commands

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
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target cpp --run
```

Run C++ for:

- empty String;
- embedded NUL;
- wide Integer profile;
- short circuit;
- identifier hardening;
- generated expression corpus.

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

# 33. Manual desktop validation

1. Launch SMILE Desktop.
2. Select C++.
3. Confirm C++ highlighting.
4. Rapidly switch C → C++ → Python → Swift → C++.
5. Confirm responsiveness.
6. Build and run a normal typed program.
7. Confirm `int Age = 49;`.
8. Confirm `std::string`.
9. Confirm `std::cout`.
10. Confirm no `printf`, `strcmp`, or `using namespace std`.
11. Run embedded-NUL output.
12. Run embedded-NUL equality.
13. Run wide Integer program.
14. Test missing-toolchain containment if practical.
15. Test cancellation.
16. Test Press Any Key launcher.
17. Confirm the IDE remains open after recoverable failure.

---

# 34. Completion report

Report:

- prerequisite v0.4.2.1 commit;
- exact files changed;
- C++ metadata;
- type mappings;
- String ownership strategy;
- concatenation strategy;
- interpolation strategy;
- embedded-NUL strategy;
- Integer-profile strategy;
- identifier mapping;
- highlighting;
- toolchain detection;
- build command;
- desktop changes;
- CLI changes;
- exact Debug and Release test counts;
- zero-warning results;
- all-ten-target generation;
- C++ build/run results;
- evaluator-versus-C++ results;
- desktop smoke results;
- final target-freeze documentation;
- unresolved concerns.

Do not state C++ local support is complete if it only transpiled and did not actually build/run.

---

# 35. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

Suggested subject:

```text
Sin and Codex: Add C++ as the final target
```

Suggested body topics:

- tenth and final destination target;
- idiomatic `std::string` and `std::cout`;
- exact embedded-NUL preservation;
- natural Integer profiles;
- MSVC C++20 Build & Run;
- all-ten-target evaluator conformance;
- permanent destination-language freeze;
- exact validation totals.
