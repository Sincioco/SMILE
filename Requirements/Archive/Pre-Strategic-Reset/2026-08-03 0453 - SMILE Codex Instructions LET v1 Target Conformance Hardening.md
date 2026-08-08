# Codex Implementation Instructions — SMILE v0.3.1 `LET` v1.0 Target-Conformance Hardening

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work **directly on `main` only**.
- Sin is the only developer.
- **Do not create, suggest, or use a feature branch.**
- Do not open a pull request for this work.
- Re-read `AGENTS.md` before changing code.
- Inspect the current `main` branch before editing.
- The reviewed baseline when this brief was prepared was:
  - Commit: `9abfbb17b0aa5b9010e3520b4ae58e0875dc9fd5`
  - Subject: `Sin and Codex: Complete LET v1.0 string expressions`
- Do not assume the baseline SHA is still current; use the newest `main`.
- Do not discard, reset, overwrite, or commit unrelated work.
- Do not commit or push unless Sin explicitly authorizes it in the Codex session.
- Follow KISS and KISS v2.
- Do not add third-party libraries or a general compiler framework.

---

# 1. Objective

Create a focused conformance-hardening release:

> **SMILE v0.3.1 — `LET` and `PRINT` v1.0 Target-Conformance Hardening**

The main `LET` v1.0 implementation is now substantially complete. This task closes the remaining target-specific edge cases before SMILE begins its next major language milestone.

The required corrections are:

1. Correct MASM handling of empty string variables.
2. Safely map the valid SMILE identifier `_` for Java and Swift.
3. Audit target identifier rules for other valid SMILE names that are reserved or special in destination languages.
4. Add adversarial identifier conformance tests across all targets.
5. Add empty-string conformance tests across the evaluator and all targets.
6. Give a missing `LET` initializer its own clear diagnostic.
7. Keep all official `LET` and `PRINT` v1.0 behavior unchanged.

Do not add another keyword or another value type.

---

# 2. Preserve the completed `LET` implementation

Do not regress these valid `LET` v1.0 forms:

```smile
LET Name = "Sin"
LET Copy = Name
LET FullName = Name + " Cioco"
LET Greeting = $"Hello {FullName}!"
```

Preserve:

- string literals;
- copies from previously declared variables;
- string concatenation;
- interpolated quoted strings;
- case-insensitive symbol resolution;
- declaration-before-use;
- self-reference rejection;
- forward-reference rejection;
- duplicate declaration rejection;
- failed declarations not leaking symbols;
- compile-time string constant evaluation;
- `SmileEvaluator`;
- symbol-based target identifier mapping;
- expression-intent preservation in high-level targets;
- constant lowering in C, Objective-C, and MASM;
- all current `PRINT` behavior.

---

# 3. Correction 1 — MASM empty string variables

## 3.1 Problem

This is valid SMILE:

```smile
LET Empty = ""
PRINT {Empty}
```

Required output is exactly one newline.

The current MASM data generator uses a fallback byte value of `0` when the string has no bytes, and then computes the variable length from the storage label:

```asm
variable0Value BYTE 0
variable0ValueLength EQU $ - variable0Value
```

That makes the logical length equal to `1`.

The generated `PRINT` then writes one NUL byte before the newline.

Storage may contain one placeholder byte, but the logical SMILE string length must remain zero.

## 3.2 Required MASM output

Use an explicit zero logical length for empty string values:

```asm
variable0Value BYTE 0
variable0ValueLength EQU 0
```

For non-empty strings, retain normal length calculation:

```asm
variable1Value BYTE "Sin"
variable1ValueLength EQU $ - variable1Value
```

## 3.3 Implementation guidance

Keep `MasmByteInitializers` simple if it is useful for non-empty data.

The MASM declaration emitter should know both:

- the storage initializer;
- the logical UTF-8 byte length.

A focused helper is acceptable:

```csharp
private static void AppendMasmVariableData(
    StringBuilder source,
    BoundLetStatement let,
    int index)
```

or:

```csharp
internal sealed record MasmStringData(
    string Initializers,
    int ByteLength);
```

Do not infer logical length from emitted placeholder storage for empty strings.

## 3.4 Required empty-string cases

All of these must work:

```smile
LET Empty = ""
PRINT {Empty}
```

```smile
LET Empty = ""
LET Copy = Empty
PRINT {Copy}
```

```smile
LET Empty = ""
LET Combined = Empty + Empty
PRINT {Combined}
```

```smile
LET Empty = ""
LET Message = $"A{Empty}B"
PRINT {Message}
```

Required outputs:

```text

```

```text

```

```text

```

```text
AB
```

## 3.5 Byte-exact verification

Do not validate only visually.

For MASM, compare the captured output string or raw UTF-8 bytes so an embedded NUL fails the test.

The output for:

```smile
LET Empty = ""
PRINT {Empty}
```

must be exactly:

```text
0A
```

after normalized Unix-style byte representation, or exactly:

```text
0D 0A
```

for native Windows newline bytes.

It must not be:

```text
00 0D 0A
```

---

# 4. Correction 2 — Map `_` safely in Java and Swift

## 4.1 SMILE rule

The official SMILE v1.0 identifier grammar permits `_`:

```text
identifier-start
    ::= ASCII-letter
      | '_'

identifier-part
    ::= ASCII-letter
      | ASCII-digit
      | '_'
```

Therefore this is valid SMILE:

```smile
LET _ = "Sin"
PRINT {_}
```

## 4.2 Java rule

A single underscore is not a legal ordinary variable identifier in current Java.

The Java target must map it, for example:

```java
String _smile_ = "Sin";
System.out.println(_smile_);
```

## 4.3 Swift rule

In Swift, `_` is a wildcard/discard pattern rather than a usable variable binding.

The Swift target must map it, for example:

```swift
let _smile_ = "Sin"
print("\(_smile_)")
```

## 4.4 Required identifier map change

Add `_` to the Java and Swift target restrictions, or implement a target predicate that recognizes it.

The same mapped name must be used for:

- declaration;
- concatenation reference;
- interpolation reference;
- `PRINT` reference.

---

# 5. Correction 3 — Audit target identifier rules

The existing symbol-based `TargetIdentifierMap` is the correct architecture.

Do not replace it with source-text rewriting.

Strengthen its target rules so every valid SMILE identifier produces valid, conventional destination code.

## 5.1 Prefer predicates over only flat word lists

Some target restrictions are patterns, not just exact reserved words.

A suitable internal design is:

```csharp
private static bool RequiresMapping(
    TargetLanguage language,
    string identifier)
```

This may call target-specific helpers.

Exact implementation may differ.

## 5.2 C and Objective-C reserved identifier patterns

SMILE permits identifiers such as:

```smile
LET __internal = "A"
LET _Upper = "B"
```

In C-family implementation namespaces, names beginning with:

- `__`;
- `_` followed by an uppercase ASCII letter;

are reserved to the implementation in ordinary C usage.

Map them for C and Objective-C rather than emitting implementation-reserved names.

Examples:

```c
const char *_smile___internal = "A";
const char *_smile__Upper = "B";
```

A cleaner deterministic spelling is acceptable.

Document the exact mapping convention.

## 5.3 Java

At minimum audit and test:

```text
_
class
record
var
yield
System
String
Program
main
args
```

The mapper must account for current Java keywords, restricted identifiers, and generated-program names.

## 5.4 Swift

At minimum audit and test:

```text
_
class
struct
protocol
extension
let
var
func
print
self
Self
super
Type
Any
```

## 5.5 C#

Audit language keywords, relevant contextual keywords, and generator-owned names.

At minimum test:

```text
class
namespace
record
required
file
global
Console
Program
Main
```

Mapping a contextual keyword even when it might be legal in one local context is acceptable if it keeps output deterministic and future-proof.

## 5.6 JavaScript

Audit:

```text
class
let
const
var
function
await
yield
console
arguments
eval
```

Generated JavaScript may later adopt strict mode, modules, or functions. Mapping `arguments` and `eval` now is acceptable.

## 5.7 C

Audit:

```text
auto
class
printf
main
stdout
__internal
_Upper
```

`class` is legal C and should remain unchanged unless another C-specific rule requires mapping.

## 5.8 Objective-C

Audit C restrictions plus Objective-C/runtime names:

```text
id
Class
self
super
nil
Nil
YES
NO
NSString
printf
main
__internal
_Upper
```

Use exact case-sensitive target rules where appropriate.

## 5.9 MASM

The current MASM generator normally uses compiler-owned labels such as:

```text
variable0Value
variable0Ptr
```

and does not need to emit the original SMILE variable name as a program symbol.

Verify that valid SMILE names cannot collide with:

- directives;
- registers;
- generated labels;
- external procedure names;
- helper labels.

Do not introduce source-derived MASM labels merely to exercise the identifier mapper.

## 5.10 Mapping requirements

Target identifier mapping must remain:

- symbol-based;
- deterministic;
- target-specific;
- collision-safe;
- readable;
- stable across repeated generation;
- consistent for every reference to a declaration.

Preserve the original SMILE spelling when it is safe.

Use a readable mapped form such as:

```text
_smile_<source-name>
```

and deterministic suffixes:

```text
_smile_<source-name>_2
```

when necessary.

---

# 6. Correction 4 — Adversarial identifier test corpus

Add a focused source corpus that includes valid SMILE identifiers likely to conflict in one or more targets.

Use at least:

```smile
LET _ = "_"
LET class = "class"
LET namespace = "namespace"
LET record = "record"
LET Console = "Console"
LET console = "console"
LET System = "System"
LET String = "String"
LET printf = "printf"
LET print = "print"
LET main = "main"
LET args = "args"
LET __internal = "__internal"
LET _Upper = "_Upper"
LET _smile_class = "_smile_class"

PRINT {_}
PRINT {class}
PRINT {namespace}
PRINT {record}
PRINT {Console}
PRINT {console}
PRINT {System}
PRINT {String}
PRINT {printf}
PRINT {print}
PRINT {main}
PRINT {args}
PRINT {__internal}
PRINT {_Upper}
PRINT {_smile_class}
```

Expected runtime output must preserve every value exactly and in order.

## 6.1 Per-target compile/run

For installed runnable targets:

- C#
- C
- MASM x64
- JavaScript
- Java

generate, compile, and run the corpus.

Compare output to `SmileEvaluator`.

## 6.2 Transpile-only targets

For Objective-C and Swift:

- assert exact or structural mapping;
- ensure no invalid raw identifier is emitted;
- keep tests ready to compile on a supported platform later.

## 6.3 Collision test

Test at least:

```smile
LET class = "A"
LET _smile_class = "B"
LET _smile_class_2 = "C"

PRINT {class}
PRINT {_smile_class}
PRINT {_smile_class_2}
```

The target mapping must produce three distinct identifiers and preserve output:

```text
A
B
C
```

## 6.4 Case-insensitivity remains a SMILE rule

This remains invalid:

```smile
LET Name = "A"
LET name = "B"
```

Do not weaken duplicate detection while hardening target mapping.

---

# 7. Correction 5 — Comprehensive empty-string conformance tests

Add a dedicated test matrix covering empty strings in both `LET` and `PRINT`.

## 7.1 Reference evaluator cases

Test:

```smile
LET Empty = ""

PRINT {Empty}
```

```smile
LET Empty = ""
LET Copy = Empty

PRINT {Copy}
```

```smile
LET Empty = ""
LET Combined = Empty + Empty

PRINT {Combined}
```

```smile
LET Empty = ""
LET Prefix = "A" + Empty
LET Suffix = Empty + "B"
LET Middle = $"A{Empty}B"

PRINT {Prefix}
PRINT {Suffix}
PRINT {Middle}
```

Expected normalized output:

```text


A
B
AB
```

Be precise about the leading blank lines.

## 7.2 All generators

For every target, verify:

- valid source is generated;
- empty declarations remain valid;
- empty values are not replaced with a NUL character;
- no target silently drops the required `PRINT` newline;
- generated output is deterministic.

## 7.3 All runnable toolchains

Compare target output to `SmileEvaluator`.

Do not trim embedded NUL characters or other unexpected bytes during normalization.

Normalization may convert CRLF to LF, but must otherwise preserve the data.

---

# 8. Correction 6 — Dedicated missing-initializer diagnostic

## 8.1 Current behavior

This invalid statement:

```smile
LET Name =
```

currently falls through to a general invalid-string-expression diagnostic.

The official specification defines an initializer as required and recommends a distinct diagnostic category.

## 8.2 Required behavior

Add a new stable diagnostic code, preferably:

```text
SMILE1116
```

Meaning:

```text
LET requires an initializer expression.
```

Use it for:

```smile
LET Name =
```

and:

```smile
LET Name =    [spaces only after equals]
```

The diagnostic span should point to the position immediately after `=` or the end of the line.

## 8.3 Preserve other diagnostics

Do not change:

```smile
LET Name
```

which should continue to report missing `=`.

Do not use the missing-initializer diagnostic for a present but malformed initializer:

```smile
LET Name = "Sin" +
```

That should remain an invalid/incomplete string expression.

## 8.4 Documentation

Add `SMILE1116` to:

- README diagnostic table;
- official `LET` specification;
- conformance tests;
- any central diagnostic catalog or documentation.

Do not reuse retired `SMILE1114`.

---

# 9. Preserve `PRINT` v1.0 behavior

Do not change the semantics of `PRINT`.

This distinction must remain:

```smile
LET Name = "Sin"

PRINT Name
PRINT {Name}
```

Output:

```text
Name
Sin
```

Preserve:

- blank `PRINT`;
- quoted literal `PRINT`;
- raw-template `PRINT`;
- interpolation;
- explicit concatenation;
- escaped literal braces;
- one statement per physical line;
- no semicolon statement separators;
- required whitespace after `PRINT`;
- expression-intent preservation;
- idiomatic target output;
- safe C/Objective-C `printf` format construction.

---

# 10. Preserve high-level and low-level `LET` generation

## 10.1 High-level targets

C#, JavaScript, Java, and Swift should continue preserving the closest natural initializer expression form.

Example SMILE:

```smile
LET Name = "Sin"
LET Copy = Name
LET Full = Name + " Cioco"
LET Greeting = $"Hello {Full}!"
```

Expected general shapes:

### C#

```csharp
string Copy = Name;
string Full = Name + " Cioco";
string Greeting = $"Hello {Full}!";
```

### JavaScript

```javascript
let Copy = Name;
let Full = Name + " Cioco";
let Greeting = `Hello ${Full}!`;
```

### Java

```java
String Copy = Name;
String Full = Name + " Cioco";
String Greeting = "Hello " + Full + "!";
```

### Swift

```swift
let Copy = Name
let Full = Name + " Cioco"
let Greeting = "Hello \(Full)!"
```

## 10.2 Low-level targets

C, Objective-C, and MASM may continue lowering immutable `LET` v1.0 values to evaluated constants.

The empty-string correction must preserve this strategy.

Do not introduce:

- heap allocation;
- `strcat`;
- `sprintf` buffers;
- a C runtime dependency in MASM;
- a SMILE runtime library.

---

# 11. Automated tests

Use the existing MSTest project.

## 11.1 New test file

A focused file is recommended:

```text
tests/SMILE.Tests/LetTargetConformanceHardeningTests.cs
```

## 11.2 MASM exact-generation test

Assert an empty `LET` declaration emits a zero logical length.

Example expectation:

```asm
variable0Value BYTE 0
variable0ValueLength EQU 0
```

## 11.3 MASM runtime test

Build and run:

```smile
LET Empty = ""
PRINT {Empty}
```

Assert exact normalized output equals `SmileEvaluator`.

## 11.4 Identifier mapping unit tests

Test `TargetIdentifierMap` indirectly through generated code or expose internal access to the test assembly.

Cover:

- `_`;
- keywords;
- contextual/restricted words;
- generator-owned names;
- C reserved patterns;
- mapping collisions;
- deterministic repeated generation.

## 11.5 Cross-target identifier integration test

Generate and run the adversarial corpus through installed toolchains.

Compare output to `SmileEvaluator`.

## 11.6 Missing initializer diagnostic test

Assert:

```smile
LET Name =
```

returns:

```text
SMILE1116
```

with the expected line and column.

## 11.7 Regression suite

All existing `LET`, `PRINT`, generator, evaluator, desktop, process, and toolchain tests must remain green.

---

# 12. Manual Windows validation

On the Windows development machine, run the desktop application and test:

## 12.1 Empty string

```smile
LET Empty = ""
PRINT {Empty}
PRINT Done
```

Run visible C#, MASM, and C targets.

The visible output must contain:

```text

Done
```

with no unusual square, blank, or NUL character before the first newline.

## 12.2 Java underscore

```smile
LET _ = "Java underscore works"
PRINT {_}
```

Build and run Java.

## 12.3 Identifier stress test

Use the adversarial corpus and run:

- C#;
- C;
- MASM;
- JavaScript;
- Java.

The IDE must remain open and display compiler errors if any target still has a mapping gap.

## 12.4 Rapid language switching

Verify the v0.2.2 responsiveness behavior is unchanged.

## 12.5 Build/run containment

A target compile error must not close the SMILE desktop application.

---

# 13. Documentation updates

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- official `LET` v1.0 specification;
- target code generation standard;
- Day 3 requirements/history notes if the project uses them.

## 13.1 AGENTS.md

Preserve and make explicit:

> All SMILE development is performed directly on `main`. Sin is the only developer. Do not create or recommend feature branches unless Sin explicitly changes this rule.

Also add:

> Every valid SMILE identifier must be mapped to valid, collision-safe destination identifiers in every target. Target restrictions include exact keywords, contextual/restricted identifiers, generator-owned names, and reserved identifier patterns.

## 13.2 Architecture

Document:

- target identifier predicates/pattern rules;
- why C reserved-prefix patterns are mapped;
- why Java and Swift map `_`;
- MASM empty storage versus logical string length.

## 13.3 Roadmap

Record v0.3.1 as conformance hardening, not a new language feature.

Do not mark numeric types or another keyword as implemented.

---

# 14. Scope exclusions

Do not add:

- integer, decimal, or boolean types;
- comments;
- escaped quotes;
- assignment/reassignment;
- `INPUT`;
- `IF`;
- loops;
- functions;
- arrays;
- nested scopes;
- a new parser architecture;
- a runtime library;
- another target language;
- a feature branch.

This is a narrow correctness pass.

---

# 15. Acceptance criteria

This task is complete only when all of the following are true:

1. `LET Empty = ""` is valid.
2. `PRINT {Empty}` emits only the normal `PRINT` newline.
3. MASM does not write a NUL byte for an empty string.
4. MASM empty string storage has logical length zero.
5. copied empty strings remain empty.
6. concatenated empty strings remain empty.
7. interpolation with an empty variable produces correct text.
8. `_` remains a valid SMILE identifier.
9. Java maps `_` to a valid usable identifier.
10. Swift maps `_` to a valid usable identifier.
11. C and Objective-C map implementation-reserved identifier prefixes.
12. target identifier mapping remains symbol-based.
13. mapping is deterministic.
14. mapping is collision-safe.
15. every reference uses the mapped declaration name.
16. the adversarial identifier corpus transpiles for all seven targets.
17. the corpus compiles and runs in installed runnable targets.
18. runtime output matches `SmileEvaluator`.
19. `LET Name =` reports `SMILE1116`.
20. malformed but present initializers do not incorrectly report missing initializer.
21. all existing `LET` v1.0 forms remain valid.
22. all existing `PRINT` v1.0 behavior remains unchanged.
23. C# expression intent remains unchanged.
24. C/Objective-C safe `printf` generation remains unchanged.
25. desktop responsiveness remains unchanged.
26. Build & Run failure containment remains unchanged.
27. Debug build completes with zero warnings.
28. Release build completes with zero warnings.
29. Debug tests pass.
30. Release tests pass.
31. CLI generation succeeds for all seven targets.
32. CLI Build & Run succeeds for all installed runnable targets.
33. documentation matches implementation.
34. no unrelated files or build artifacts are committed.
35. all work is performed directly on `main`.

---

# 16. Validation commands

Run from the repository root on `main`:

```bat
cmd /c git status --short --branch
```

Confirm the active branch is:

```text
main
```

Do not create another branch.

Then run:

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

Run CLI generation for all targets using:

- empty-string acceptance source;
- adversarial identifier source.

Run CLI Build & Run for:

- `csharp`;
- `c`;
- `masm-x64`;
- `javascript`;
- `java`.

Compare every result to `SmileEvaluator`.

Before any authorized commit:

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

# 17. Suggested implementation order

1. Add failing empty-string evaluator and target tests.
2. Correct MASM logical empty-string length.
3. Add failing `_` Java and Swift tests.
4. Refactor target identifier restrictions to support pattern rules.
5. Add C/Objective-C reserved-prefix handling.
6. Build the adversarial identifier corpus.
7. Run compile/run mapping tests.
8. Add `SMILE1116`.
9. Update specifications and documentation.
10. Run full Debug/Release validation.
11. Perform desktop smoke tests.
12. Review generated code manually for readability.
13. Commit directly to `main` only when Sin authorizes it.

---

# 18. Completion report

Report:

- exact files changed;
- MASM empty-string root cause;
- MASM fix;
- identifier rule additions by target;
- mapping collision strategy;
- new diagnostic code and span;
- tests added;
- exact Debug and Release test totals;
- all target generation results;
- installed target compile/run results;
- desktop smoke-test results;
- documentation changes;
- any unresolved target identifier concern.

Do not claim full target conformance if an adversarial identifier or empty-string test remains skipped for a runnable installed toolchain.

---

# 19. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

When authorized, commit directly on `main` with a focused subject such as:

```text
Sin and Codex: Harden LET v1.0 target conformance
```

The commit body should mention:

- MASM zero-length empty strings;
- Java and Swift `_` mapping;
- target reserved-pattern audit;
- adversarial identifier tests;
- `SMILE1116`;
- evaluator-versus-toolchain verification;
- exact validation results.
