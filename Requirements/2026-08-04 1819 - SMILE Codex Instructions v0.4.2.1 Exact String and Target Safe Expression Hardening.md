# Codex Implementation Instructions — SMILE v0.4.2.1 Exact String and Target-Safe Expression Hardening

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
- Do not add a runtime framework, parser generator, property-testing framework, compiler framework, third-party dependency, or another target language.

The reviewed baseline when this brief was prepared was:

```text
bcbaa09345f195c2cfbab0fcef9dc9eebe65b0cd
Sin and Codex: Add Python target and idiomatic integer profiles
```

Do not assume that SHA is still current. Always start from the newest `main`.

---

# 1. Milestone

Create the focused correction release:

> **SMILE v0.4.2.1 — Exact String and Target-Safe Expression Hardening**

This release must not add another SMILE keyword or destination language.

Its purpose is to correct two narrow conformance gaps discovered after v0.4.2:

1. C and Objective-C String values containing embedded NUL characters are not always preserved by `%s` and `strcmp`.
2. Short-circuit expressions using known Boolean variable values may still expose unreachable invalid expressions to strict destination compilers in some expression positions.

Also perform a small documentation consistency cleanup.

---

# 2. Preserve everything already working

Do not regress:

- Python as the ninth target;
- idiomatic per-program Integer profiles;
- C/Objective-C `int` for ordinary programs;
- `int64_t`, `long`, `BigInt`, and `Int64` promotion when needed;
- shared Boolean simplification;
- exact signed 64-bit SMILE semantics;
- Python truncation-toward-zero division;
- C/Objective-C complex String equality lowering;
- all official String escapes;
- canonical `TRUE` and `FALSE`;
- `LET`;
- `PRINT`;
- short-circuit semantics;
- case-insensitive SMILE identifiers;
- case-sensitive String data;
- target identifier mapping;
- all nine destination targets;
- desktop responsiveness;
- build/run failure containment;
- syntax highlighting;
- Ctrl+mouse-wheel zoom;
- current mission statement;
- deliberate deferral of Rust, Zig, and Go.

---

# 3. Problem A — Embedded NUL in C and Objective-C String values

## 3.1 Official SMILE behavior

SMILE supports the official escape:

```smile
\0
```

This creates an embedded NUL character inside the String value.

Example:

```smile
LET Text = "A\0B"
PRINT {Text}
```

The required output bytes are:

```text
'A' 0x00 'B' newline
```

The NUL is part of the String value. It does not terminate the SMILE String.

## 3.2 Current C-family danger

C and Objective-C normally use NUL-terminated `char *` Strings.

These forms are unsafe for an embedded NUL:

```c
printf("%s\n", Text);
strcmp(Left, Right);
```

`printf("%s")` stops at the first NUL.

`strcmp` also stops at the first NUL.

Therefore:

```smile
LET Left = "A\0B"
LET Right = "A\0C"
LET Same = Left = Right
```

must not accidentally behave as if both values were only `"A"`.

The correct result is:

```text
FALSE
```

---

# 4. Required C and Objective-C String representation policy

## 4.1 Keep ordinary String code simple

For NUL-free compile-time String values, preserve the current readable C-family output.

Examples:

```c
const char *Name = "Sin";
printf("%s\n", Name);
bool Same = strcmp(Left, Right) == 0;
```

Do not replace every String with a complicated structure.

## 4.2 Length-aware handling only when required

When a bound String value contains an embedded NUL, generated C and Objective-C must preserve:

- all UTF-8 bytes;
- exact byte length;
- bytes after the NUL;
- exact equality semantics;
- exact `PRINT` output.

Use the simplest complete solution.

Preferred current strategy:

- Keep the ordinary readable declaration where practical.
- Generate a compiler-owned exact byte length for a NUL-containing String variable.
- Use byte-aware output for that value.
- Use byte-aware equality or evaluated Boolean lowering whenever either operand contains NUL.

A different small, readable implementation is acceptable if all acceptance tests pass.

Do not add a general SMILE String runtime library.

---

# 5. C-family exact output for embedded NUL

## 5.1 Required behavior

For:

```smile
LET Text = "A\0B"
PRINT {Text}
```

generated C and Objective-C must emit:

```text
A
```

followed by a NUL byte, followed by:

```text
B
```

and the normal newline.

## 5.2 Preferred implementation

Use a compiler-owned exact-length operation such as:

```c
fwrite(Text, 1, TextLength, stdout);
fputc('\n', stdout);
```

or a semantically equivalent form.

For one SMILE `PRINT`, several small C statements are acceptable when exact bytes require them.

Do not force one `printf` call when it would be incorrect.

## 5.3 Compiler-owned byte data

For a literal containing NUL, a deterministic generated byte array is acceptable:

```c
static const unsigned char smileText[] = { 65, 0, 66 };
```

A valid escaped String literal plus explicit byte length is also acceptable.

Generated support names must be deterministic and collision-safe.

## 5.4 Ordinary output remains idiomatic

For NUL-free variables:

```c
printf("%s\n", Name);
```

should remain readable and unchanged.

Do not unnecessarily convert all String output to `fwrite`.

---

# 6. C-family exact equality with embedded NUL

## 6.1 Required semantics

String equality compares:

- complete value;
- complete length;
- every byte;
- case-sensitively;
- ordinally.

For:

```smile
LET A = "A\0B"
LET B = "A\0B"
LET C = "A\0C"
LET Same = A = B
LET Different = A <> C
```

required:

```text
Same = TRUE
Different = TRUE
```

## 6.2 Acceptable current strategies

### Strategy A — Lower NUL-sensitive comparisons to the known Boolean constant

```c
bool Same = true;
bool Different = true;
```

This is acceptable in v0.4.2.1 because all current expressions are pure and compile-time evaluable.

### Strategy B — Compare length plus bytes

```c
bool Same =
    LeftLength == RightLength &&
    memcmp(Left, Right, LeftLength) == 0;
```

This is also acceptable if kept small and readable.

## 6.3 Recommendation

Prefer:

- `strcmp` for simple NUL-free operands;
- evaluated Boolean lowering for current NUL-sensitive comparisons;
- no general String runtime system.

Document this as intentional target-local lowering.

---

# 7. Detecting NUL-sensitive C-family expressions

Add a small helper that can answer:

```text
Does this bound String expression evaluate to a value containing '\0'?
```

Use:

- `BoundConstantEvaluator`;
- `SmileValue`;
- the variable constant-value environment.

Do not inspect SMILE source text.

Required coverage:

- literal containing NUL;
- variable whose value contains NUL;
- copied variable;
- concatenation result containing NUL;
- interpolation result containing NUL;
- raw `PRINT` template with a NUL-containing hole;
- equality where left contains NUL;
- equality where right contains NUL;
- equality where both contain NUL.

---

# 8. Problem B — Short-circuit folding through known Boolean values

## 8.1 Official behavior

These are valid:

```smile
LET Flag = FALSE
LET Result = Flag AND (1 / 0 = 0)
```

```smile
LET Flag = TRUE
LET Result = Flag OR (1 / 0 = 0)
```

The right operand is unreachable at evaluation time.

The same must work in every expression position:

- `LET`;
- direct evaluated `PRINT`;
- raw-template hole;
- interpolated String hole;
- nested Boolean expression.

## 8.2 Current gap

The shared simplifier recognizes Boolean literal nodes such as:

```text
FALSE AND right
TRUE OR right
```

A known Boolean variable may remain a variable reference in the simplified tree.

A strict destination compiler may diagnose the unreachable constant division before runtime.

---

# 9. Required shared short-circuit simplification

## 9.1 Use the constant environment

Enhance the shared bound simplification pass so it receives or constructs the current constant-value environment.

For:

```text
left AND right
left OR right
```

evaluate the simplified left expression with `BoundConstantEvaluator`.

Rules:

```text
If left evaluates to FALSE and operator is AND:
    replace the complete expression with FALSE
    do not simplify or evaluate the unreachable right side

If left evaluates to TRUE and operator is OR:
    replace the complete expression with TRUE
    do not simplify or evaluate the unreachable right side

If left evaluates to TRUE and operator is AND:
    simplify and return right

If left evaluates to FALSE and operator is OR:
    simplify and return right
```

## 9.2 Important ordering rule

Do not eagerly simplify the right operand before deciding whether it is reachable.

Bad:

```text
simplify left
simplify right
inspect left
```

Required:

```text
simplify left
determine reachability
simplify right only when required
```

## 9.3 Binding still checks both sides

Do not change binding.

These remain errors:

```smile
LET Flag = FALSE
LET Result = Flag AND MissingName
```

```smile
LET Flag = TRUE
LET Result = Flag OR 42
```

Simplification occurs only after successful binding.

---

# 10. Apply simplification to every expression position

Cover all of these.

## LET

```smile
LET Flag = FALSE
LET Result = Flag AND (1 / 0 = 0)
```

## Direct PRINT

```smile
LET Flag = FALSE
PRINT {Flag AND (1 / 0 = 0)}
```

## Raw-template hole

```smile
LET Flag = FALSE
PRINT Safe={Flag AND (1 / 0 = 0)}
```

## Interpolated String hole

```smile
LET Flag = TRUE
LET Message = $"Safe={Flag OR (1 / 0 = 0)}"
PRINT {Message}
```

## Nested expression

```smile
LET Flag = FALSE
LET Result = TRUE OR (Flag AND (1 / 0 = 0))
```

Every target must match `SmileEvaluator`.

---

# 11. Reduce target-specific workarounds

After the shared simplifier handles known values:

- review `GeneratorValueFacts.ContainsShortCircuitedBranch`;
- remove duplicated or unnecessary target-specific workarounds where safe;
- retain only defensive target-specific lowering if a strict compiler still requires it.

The primary semantic solution must live in the shared simplifier.

Do not scatter short-circuit logic across nine generators.

---

# 12. Future correctness

Document:

> The v0.4.2.1 simplifier may use known constant values because current SMILE has no input, reassignment, functions, or side effects. When runtime values or side effects are added, optimization must preserve left-to-right evaluation and may fold only expressions proven safe.

Do not create a general optimizer framework.

---

# 13. Required tests — C and Objective-C embedded NUL

Run focused tests for:

```text
TargetLanguage.C
TargetLanguage.ObjectiveC
```

## NUL variable output

```smile
LET Text = "A\0B"
PRINT {Text}
```

Expected bytes:

```text
41 00 42 0A
```

after platform newline handling.

## NUL copy

```smile
LET Original = "A\0B"
LET Copy = Original
PRINT {Copy}
```

## NUL concatenation

```smile
LET Left = "A\0"
LET Text = Left + "B"
PRINT {Text}
```

## NUL interpolation

```smile
LET Middle = "\0"
LET Text = $"A{Middle}B"
PRINT {Text}
```

## NUL equality

```smile
LET A = "A\0B"
LET B = "A\0B"
LET C = "A\0C"

LET Same = A = B
LET Different = A <> C
LET NotSame = A = C

PRINT {Same}
PRINT {Different}
PRINT {NotSame}
```

Expected:

```text
TRUE
TRUE
FALSE
```

## Prefix collision

```smile
LET A = "A\0B"
LET B = "A\0C"
PRINT {A = B}
```

Expected:

```text
FALSE
```

---

# 14. Required tests — known-value short circuit

Run through all nine targets when installed.

## LET initializers

```smile
LET FalseFlag = FALSE
LET TrueFlag = TRUE

LET A = FalseFlag AND (1 / 0 = 0)
LET B = TrueFlag OR (1 / 0 = 0)

PRINT {A}
PRINT {B}
```

Expected:

```text
FALSE
TRUE
```

## Direct PRINT

```smile
LET FalseFlag = FALSE
LET TrueFlag = TRUE

PRINT {FalseFlag AND (1 / 0 = 0)}
PRINT {TrueFlag OR (1 / 0 = 0)}
```

## Raw-template holes

```smile
LET FalseFlag = FALSE
LET TrueFlag = TRUE

PRINT A={FalseFlag AND (1 / 0 = 0)}
PRINT B={TrueFlag OR (1 / 0 = 0)}
```

Expected:

```text
A=FALSE
B=TRUE
```

## Interpolated String holes

```smile
LET FalseFlag = FALSE
LET TrueFlag = TRUE

LET Message = $"A={FalseFlag AND (1 / 0 = 0)}, B={TrueFlag OR (1 / 0 = 0)}"
PRINT {Message}
```

Expected:

```text
A=FALSE, B=TRUE
```

---

# 15. Exact-byte integration testing

For every installed target:

1. Evaluate with `SmileEvaluator`.
2. Generate target source.
3. Build/run or run.
4. Capture stdout without trimming.
5. Normalize CRLF/LF only when the test is specifically comparing line endings.
6. Compare all other bytes exactly.

For embedded NUL:

- compare byte arrays when practical;
- verify bytes after NUL are present;
- verify equality output;
- verify exit code zero.

Do not rely on visual console inspection.

---

# 16. Generated-code style acceptance

Preserve the v0.4.2 idiomatic target policy.

SMILE:

```smile
LET Age = 49
LET Adult = Age >= 18
LET WorkingAge = Adult AND NOT FALSE
```

Required C:

```c
int Age = 49;
bool Adult = Age >= 18;
bool WorkingAge = Adult;
```

Required Python:

```python
Age = 49
Adult = Age >= 18
WorkingAge = Adult
```

Required C#:

```csharp
int Age = 49;
bool Adult = Age >= 18;
bool WorkingAge = Adult;
```

Required Java:

```java
int Age = 49;
boolean Adult = Age >= 18;
boolean WorkingAge = Adult;
```

Required JavaScript:

```javascript
let Age = 49;
let Adult = Age >= 18;
let WorkingAge = Adult;
```

Required Swift:

```swift
let Age: Int = 49
let Adult: Bool = Age >= 18
let WorkingAge: Bool = Adult
```

---

# 17. Documentation consistency cleanup

Update active documentation so version wording is clear.

Preferred wording:

> This specification was introduced in SMILE v0.4.1 and remains normative for v0.4.2.1 and later unless superseded by a newer official specification.

Update as appropriate:

- `SMILE - String Literals Official Specification v1.0.md`
- `SMILE - Core Types and Expressions Official Specification v1.0.md`
- `README.md`
- `docs/Architecture.md`
- `docs/Roadmap.md`
- `docs/SMILE Target Code Generation Standard v1.0.md`
- `AGENTS.md`
- requirements/history;
- desktop version/About metadata.

Do not rename official v1.0 specification files merely to change implementation-version wording.

---

# 18. AGENTS.md additions

Preserve all existing rules.

Add wording equivalent to:

> Destination-language String representations must preserve the complete SMILE String value, including embedded NUL characters. C-family `%s` and `strcmp` may be used only when they are semantically valid for the complete value.

Add:

> Shared short-circuit simplification must use known bound constant values and apply to every expression position. Binding still validates both operands before simplification.

Add:

> Exact-byte conformance tests must not trim or discard NUL, backspace, form-feed, carriage-return, or tab characters.

---

# 19. Architecture documentation

Document:

- why SMILE Strings are length-aware values even when a target uses NUL-terminated Strings;
- why C `%s` and `strcmp` are insufficient for embedded NUL;
- the current KISS strategy for NUL-sensitive output/equality;
- constant-aware shared short-circuit simplification;
- why unreachable right operands are not simplified/evaluated;
- future impact of runtime values and side effects;
- all-nine-target exact-byte conformance.

---

# 20. Roadmap

Add:

## Implemented in v0.4.2.1

- exact embedded-NUL preservation in C and Objective-C output;
- exact NUL-sensitive String equality;
- known-Boolean short-circuit simplification in all expression positions;
- exact-byte all-target conformance hardening;
- documentation version clarification.

Keep the next major milestone:

```text
v0.5.0 — Runtime Variables and SET
```

Do not implement `SET` in this task.

Keep Rust, Zig, and Go deferred.

---

# 21. Scope exclusions

Do not implement:

- C++;
- `SET`;
- reassignment;
- mutable variables;
- `INPUT`;
- `IF`;
- loops;
- functions;
- scopes;
- arrays;
- floating-point types;
- a general optimizer framework;
- a general String runtime library;
- another destination language;
- Rust;
- Zig;
- Go;
- a feature branch.

C++ is a separate later milestone and must not be mixed into this correction release.

---

# 22. Acceptance criteria

This task is complete only when all are true:

1. C preserves bytes after embedded NUL.
2. Objective-C preserves bytes after embedded NUL.
3. C NUL-sensitive equality compares complete values.
4. Objective-C NUL-sensitive equality compares complete values.
5. `"A\0B" = "A\0C"` is `FALSE`.
6. copied NUL-containing Strings preserve exact bytes.
7. concatenated NUL-containing Strings preserve exact bytes.
8. interpolated NUL-containing Strings preserve exact bytes.
9. NUL-free C output remains readable.
10. NUL-free C equality still uses `strcmp` where appropriate.
11. NUL-free Objective-C output remains readable.
12. NUL-free Objective-C equality still uses `strcmp` where appropriate.
13. known `FALSE` variables short-circuit `AND`.
14. known `TRUE` variables short-circuit `OR`.
15. known-value short-circuit works in `LET`.
16. known-value short-circuit works in direct `PRINT`.
17. known-value short-circuit works in raw-template holes.
18. known-value short-circuit works in interpolated String holes.
19. unreachable right expressions are not eagerly simplified.
20. binder diagnostics remain intact.
21. the shared simplifier is the primary solution.
22. all nine targets match `SmileEvaluator`.
23. exact-byte tests do not trim control characters.
24. idiomatic Integer profiles remain intact.
25. Python remains fully supported.
26. all prior v0.4.2 tests remain green.
27. Debug build has zero warnings.
28. Release build has zero warnings.
29. Debug tests pass.
30. Release tests pass.
31. every installed target builds/runs with exit code zero.
32. documentation matches implementation.
33. Rust, Zig, and Go remain deferred.
34. no unapproved dependency is added.
35. no build artifacts are committed.
36. all work is performed directly on `main`.

---

# 23. Suggested implementation sequence

1. Confirm newest `main`.
2. Add failing C/Objective-C embedded-NUL tests.
3. Implement bound-value NUL detection.
4. Implement exact C/Objective-C NUL output.
5. Implement NUL-sensitive equality.
6. Add failing known-variable short-circuit tests.
7. Refactor the shared simplifier to use constant values.
8. Prevent eager right-side simplification.
9. Apply simplification to every expression position.
10. Remove unnecessary target-specific duplication.
11. Run focused tests.
12. Run all Debug tests.
13. Run all Release tests.
14. Build/run all nine installed targets.
15. Perform desktop smoke testing.
16. Update documentation.
17. Commit directly to `main` only when Sin authorizes it.

---

# 24. Validation commands

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

Generate all targets:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target all
```

Run the new NUL and short-circuit examples explicitly through every installed target.

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

# 25. Manual desktop validation

1. Launch SMILE Desktop.
2. Test:

```smile
LET Text = "A\0B"
PRINT {Text}
```

3. Build/run C and Objective-C.
4. Confirm exact-byte tests prove `B` exists after NUL.
5. Test:

```smile
LET A = "A\0B"
LET B = "A\0C"
PRINT {A = B}
```

6. Confirm `FALSE`.
7. Test:

```smile
LET Flag = FALSE
PRINT {Flag AND (1 / 0 = 0)}
```

8. Build/run all visible installed targets.
9. Confirm `FALSE`.
10. Confirm Python still runs.
11. Confirm the Age example still generates `int Age = 49;` in C.
12. Confirm SMILE stays responsive.

---

# 26. Completion report

Report:

- exact baseline commit;
- exact files changed;
- C embedded-NUL strategy;
- Objective-C embedded-NUL strategy;
- exact output strategy;
- exact equality strategy;
- NUL-free code preserved;
- shared simplifier changes;
- proof unreachable right sides are not eagerly processed;
- all expression positions tested;
- binding diagnostics preserved;
- exact Debug and Release test counts;
- zero-warning results;
- all-nine-target runtime results;
- byte-aware validation;
- desktop smoke results;
- documentation changes;
- unresolved concerns.

Do not claim embedded-NUL conformance based only on visible console output.

---

# 27. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

Suggested subject:

```text
Sin and Codex: Harden exact strings and target-safe expressions
```

Suggested body topics:

- exact embedded-NUL C/Objective-C output;
- NUL-sensitive equality;
- known-value shared short-circuit simplification;
- all-expression-position coverage;
- all-nine-target exact-byte conformance;
- documentation version clarification;
- exact validation totals.
