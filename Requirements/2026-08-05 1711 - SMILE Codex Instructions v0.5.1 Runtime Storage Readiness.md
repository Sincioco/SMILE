# Codex Implementation Instructions — SMILE v0.5.1 Runtime Storage Readiness

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
- Do not add `IF`, `INPUT`, loops, functions, scopes, arrays, floating-point values, comments, or another SMILE keyword.
- Do not add a parser generator, compiler framework, runtime framework, template engine, package manager, or unnecessary dependency.
- Preserve `examples/language.smile` as the cumulative language reference.
- Preserve asynchronous Desktop startup and visible-target-only live transpilation.

The reviewed baseline when this brief was prepared was:

```text
f21e12299d16f38efb447261a6ad8ead477aa06c
Sin and Codex: Load the cumulative language reference after first paint
```

Do not assume that SHA is still current. Always start from the newest `main`.

---

# 1. Milestone

Create:

> **SMILE v0.5.1 — Runtime Storage Readiness**

This is a focused hardening release after v0.5.0.

It must:

1. complete Java runtime validation;
2. make direct C and Objective-C String variable PRINT read current target storage;
3. make C and Objective-C length-aware String equality use current target storage where practical;
4. make direct COBOL variable PRINT read current target storage;
5. prove through adversarial generated-code tests that later output depends on the mutated target variable, not an independently emitted compiler-time literal;
6. preserve all existing v0.5.0 language behavior;
7. add no new syntax.

This release prepares the current runtime-variable model for future `INPUT` and control flow.

---

# 2. Preserve all current behavior

Do not regress:

- `LET`;
- `SET`;
- `PRINT`;
- SET Block String Literal — The SMILE Way;
- exact Block String delimiter rules;
- structural indentation removal;
- logical `\n` normalization;
- exact trailing spaces and tabs;
- embedded NUL;
- case-insensitive variables;
- fixed variable types;
- atomic SET evaluation;
- mutation-aware simplification;
- mutation-aware Integer profiling;
- all ten target languages;
- C++ as the final target;
- Swift `var` only for mutated variables;
- COBOL real `MOVE`;
- MASM pointer-and-length runtime updates;
- deterministic generation;
- exact evaluator conformance;
- cumulative `language.smile`;
- first-paint Desktop startup;
- source-revision race protection;
- visible-target-only live transpilation;
- Ctrl+mouse-wheel zoom;
- Build & Run cancellation and failure containment.

---

# 3. Problem A — Java runtime validation is incomplete

The current validation reports expected Java integration skips because the validation machine did not have a full JDK available.

This is a validation gap, not evidence of a Java defect.

## Required outcome

All Java integration tests must execute rather than skip on Sin's development machine.

The final v0.5.1 report must show:

```text
Java integration tests executed
0 Java SET-related skips
javac succeeded
java succeeded
exit code 0
stdout matched SmileEvaluator
```

If unrelated Java tests remain intentionally skipped for a documented environmental reason, identify them exactly. Do not describe the Java SET milestone as fully validated while SET integration tests are still skipped.

---

# 4. Java toolchain readiness

Inspect the current Java toolchain detection.

Confirm it detects a complete JDK containing at least:

```text
javac
java
```

Do not accept a JRE-only installation as a build-capable toolchain.

Prefer existing supported discovery mechanisms.

Do not hard-code a developer-specific absolute path.

Reasonable discovery sources include:

```text
JAVA_HOME
PATH
known existing Java toolchain discovery already used by SMILE
```

The toolchain status should distinguish:

```text
full JDK available
java runtime only
JDK missing
```

Do not launch an installer or Windows Store alias.

---

# 5. Required explicit Java validation programs

Run Java for:

## Ordinary SET

```smile
LET Counter = 0
SET Counter = Counter + 1
PRINT {Counter}
```

Expected:

```text
1
```

## String reassignment

```smile
LET Name = "Sin"
SET Name = "Louiery"
PRINT {Name}
```

Expected:

```text
Louiery
```

## Block String

```smile
LET Name = ""

SET Name ="
S
 I
  N
"

PRINT {Name}
```

Expected:

```text
S
 I
  N
```

## Embedded NUL

```smile
LET Data = "A\0B"
SET Data = "A\0C"
PRINT {Data}
```

Compare exact UTF-8 bytes.

## Wide Integer introduced by SET

```smile
LET Value = 1
SET Value = 5000000000
PRINT {Value}
```

Expected:

```text
5000000000
```

## Full `examples/language.smile`

Compile and run the cumulative language reference through Java.

Compare output to `SmileEvaluator`.

---

# 6. Problem B — C and Objective-C direct variable PRINT bypasses current storage

The current C-family generators can maintain:

```c
const char *Variable
size_t VariableLength
```

for a String variable that may contain embedded NUL.

However, exact NUL-sensitive PRINT can still lower to a new compiler-owned byte array containing the statically known current value.

That output is semantically correct in v0.5.0, but it does not prove that PRINT reads the variable's runtime storage.

This release must correct direct variable PRINT.

---

# 7. Required C and Objective-C direct variable PRINT behavior

For:

```smile
PRINT {Data}
```

when `Data` is a direct String variable reference, generated C and Objective-C must read:

```text
the current variable pointer
the current logical byte length
```

Preferred shape when exact length is maintained:

```c
fwrite(Data, 1, DataLength, stdout);
fputc('\n', stdout);
```

For an ordinary NUL-free String variable that does not require a logical-length companion, readable output may remain:

```c
printf("%s\n", Data);
```

or another existing conventional equivalent.

## Mandatory rule

A direct String variable PRINT must not generate a second independent static copy of the statement-local String value merely to print it.

---

# 8. When to maintain a C-family logical length

Continue or refine the current analysis.

A String variable needs a logical-length companion when any value assigned to it may require exact byte-length semantics.

At minimum, this includes a variable that can contain embedded NUL at any LET or SET assignment.

A reasonable conservative policy is acceptable:

```text
maintain logical length for every mutable String variable
```

provided generated code stays clear and tests are updated.

The chosen policy must be documented and deterministic.

---

# 9. C-family String storage update

For every SET to a String variable with logical length:

```c
Data = <new string storage>;
DataLength = <exact UTF-8 byte length>;
```

The length update must occur immediately after the pointer update at the SET statement.

Do not derive PRINT length from a stale original LET value.

Do not derive it from final program state.

Use statement order.

---

# 10. Direct variable PRINT detection

Implement a clear shared helper.

Conceptual rule:

```text
if PRINT value is BoundVariableExpression
and variable type is String
then print current target variable storage
```

For C and Objective-C:

```text
if logical-length companion exists:
    fwrite(variable, 1, variableLength, stdout)
    fputc('\n', stdout)
else:
    use ordinary NUL-free String output
```

Keep raw templates, interpolated Strings, concatenations, comparisons, and other expressions on their existing exact lowering paths.

Do not force every C PRINT expression into variable-storage form.

---

# 11. C-family adversarial storage-read proof

Add a structural test that would fail if PRINT uses an independently emitted literal.

SMILE:

```smile
LET Data = "First"
SET Data = "Second"
PRINT {Data}
```

For direct variable PRINT, assert generated C and Objective-C source references:

```text
Data
```

in the actual output call.

For example:

```c
fwrite(Data, 1, DataLength, stdout);
```

or:

```c
printf("%s\n", Data);
```

Also assert that the PRINT statement is not implemented only as:

```c
printf("Second\n");
```

or:

```c
static const unsigned char smilePrintBytes[] = { ...Second... };
```

A literal may still exist as the SET source value. The test must specifically inspect the output operation.

---

# 12. C-family exact NUL storage-read proof

Use:

```smile
LET Data = "ABC"
SET Data = "A\0B"
PRINT {Data}
```

Required generated structure:

```text
Data is assigned A\0B storage
DataLength becomes 3
PRINT uses Data and DataLength
```

Expected conceptual C:

```c
Data = "A\000B";
DataLength = 3;
fwrite(Data, 1, DataLength, stdout);
fputc('\n', stdout);
```

Compare exact output bytes:

```text
41 00 42 0A
```

---

# 13. Problem C — C and Objective-C NUL-sensitive equality can bypass runtime storage

Current exact equality may be lowered from statement-local known values.

That remains semantically correct today, but direct variable equality should use current target storage where practical.

This is important preparation for future runtime-unknown input.

---

# 14. Required C-family direct String equality behavior

For direct String variable comparisons:

```smile
PRINT {Left = Right}
```

or:

```smile
LET Same = Left = Right
```

prefer actual target storage comparison.

## NUL-free variables

Use ordinary ordinal comparison:

```c
strcmp(Left, Right) == 0
```

## Length-aware variables

Use logical length plus byte comparison:

```c
LeftLength == RightLength &&
memcmp(Left, Right, LeftLength) == 0
```

For inequality:

```c
LeftLength != RightLength ||
memcmp(Left, Right, LeftLength) != 0
```

If one side has a logical-length companion and the other is a literal or another expression, use an exact temporary/literal length in the generated expression or a small clearly generated helper plan.

Do not read past the shorter length.

---

# 15. Equality scope for v0.5.1

Required:

- variable versus variable;
- variable versus literal;
- literal versus variable;
- equality and inequality;
- embedded NUL;
- updated value after SET;
- prefix collision such as `A\0B` versus `A\0C`;
- unequal lengths.

It remains acceptable to lower highly complex current compile-time String expressions to a Boolean constant when producing an actual storage comparison would substantially complicate the generator.

Document the boundary.

Do not regress ordinary readable `strcmp` output.

---

# 16. Required equality tests

## Variable versus variable after SET

```smile
LET Left = "A\0B"
LET Right = "A\0B"

SET Right = "A\0C"

PRINT {Left = Right}
PRINT {Left <> Right}
```

Expected:

```text
FALSE
TRUE
```

Generated C-family code should use current storage and current lengths.

## Prefix collision

```smile
LET Left = "A\0B"
LET Right = "A\0C"

PRINT {Left = Right}
```

Expected:

```text
FALSE
```

## Equal exact bytes

```smile
LET Left = "A\0B"
LET Right = "A\0B"

PRINT {Left = Right}
```

Expected:

```text
TRUE
```

## Different lengths

```smile
LET Left = "A\0B"
LET Right = "A\0B\0"

PRINT {Left = Right}
```

Expected:

```text
FALSE
```

---

# 17. Problem D — COBOL direct variable PRINT bypasses current storage

The current COBOL generator emits real `MOVE` statements and logical-length updates, but direct PRINT may still emit a literal computed by the compiler.

This release must make direct variable PRINT read the COBOL variable storage.

---

# 18. Required COBOL direct variable PRINT behavior

For:

```smile
PRINT {Name}
```

when `Name` is a direct variable reference, generated COBOL must display current variable storage.

For mutable String variables, use the current logical length.

A suitable GnuCOBOL approach may use reference modification:

```cobol
DISPLAY Name(1:Name-LENGTH).
```

or a no-advancing form followed by an exact newline if needed to preserve empty or control-containing values.

The exact implementation must be valid for the existing GnuCOBOL free-format toolchain.

Do not output a compiler-generated literal instead of the variable.

---

# 19. COBOL empty String behavior

A zero logical length must still print exactly one SMILE newline and no space.

Do not produce:

```text
one padding space
```

for an empty String variable.

Reuse the existing exact blank-line strategy where appropriate.

---

# 20. COBOL direct variable PRINT plan

Recommended conceptual behavior:

```text
String variable, length > 0:
    DISPLAY variable reference modification using current length

String variable, length = 0:
    emit exact newline only

Integer/Boolean variable:
    display current variable storage or the existing canonical target representation
```

Because COBOL storage may be text-oriented in the current educational profile, preserve canonical Integer and Boolean display.

---

# 21. COBOL runtime branch concern

COBOL cannot choose at compile time whether a mutable variable's runtime length is zero once future INPUT exists.

For v0.5.1, values are still known, but generated code should move toward runtime-authentic storage access.

A small generated runtime condition is acceptable:

```cobol
IF Name-LENGTH = 0
    DISPLAY X"0A" WITH NO ADVANCING
ELSE
    DISPLAY Name(1:Name-LENGTH)
END-IF
```

Use conventional, readable GnuCOBOL.

This introduces target-language control flow only as compiler-owned runtime support. It does not add SMILE IF syntax.

---

# 22. COBOL exact control bytes

Do not regress:

- line feeds;
- carriage returns;
- tabs;
- backspace;
- form feed;
- embedded NUL;
- UTF-8;
- trailing spaces.

If direct variable reference modification cannot preserve one of these values in the current GnuCOBOL representation, use a small exact byte-aware runtime helper plan rather than reverting to compiler-time PRINT literals.

Keep it dependency-free.

---

# 23. COBOL structural proof test

Use:

```smile
LET Name = "First"
SET Name = "Second"
PRINT {Name}
```

Assert:

- generated source contains a `MOVE "Second" TO Name`;
- generated PRINT references `Name`;
- generated PRINT uses the logical length if one exists;
- generated PRINT is not merely `DISPLAY "Second"`.

---

# 24. COBOL exact Block String storage-read test

Use:

```smile
LET Message = ""

SET Message ="
First

Third
"

PRINT {Message}
```

Expected:

```text
First

Third
```

Generated COBOL must:

- store the normalized block value;
- update current logical length;
- PRINT from current storage;
- preserve both logical line-feed bytes.

Compare output to `SmileEvaluator`.

---

# 25. Direct-variable runtime authenticity across targets

Add a shared test program:

```smile
LET Text = "One"
LET Number = 1
LET Flag = FALSE

PRINT {Text}
PRINT {Number}
PRINT {Flag}

SET Text = "Two"
SET Number = 2
SET Flag = TRUE

PRINT {Text}
PRINT {Number}
PRINT {Flag}
```

Expected:

```text
One
1
FALSE
Two
2
TRUE
```

For all ten installed targets:

1. generate;
2. build/run;
3. compare to `SmileEvaluator`;
4. inspect low-level target source to verify variable reads.

Do not rely only on output comparison for C, Objective-C, and COBOL. Structural assertions are required.

---

# 26. MASM and other targets

MASM already updates runtime pointer and length and direct variable PRINT should continue reading runtime storage.

Add or preserve a regression assertion proving:

```text
PRINT {Variable}
```

uses:

```text
variable pointer
variable length
```

after SET.

High-level targets already emit natural variable reads. Preserve them.

Do not refactor targets unnecessarily.

---

# 27. Runtime-storage helper ownership

Any new helper names must be:

- compiler-owned;
- collision-safe;
- deterministic;
- protected by target identifier mapping when appropriate;
- emitted only when needed.

Do not add a shared runtime library.

Keep helpers inside the generated source.

---

# 28. Execution trace scope

Do not redesign `BoundProgramExecutionTrace` in this release.

It remains appropriate for straight-line v0.5.x programs.

Use it to decide:

- which variables need lengths;
- maximum assigned storage size;
- exact assigned data;
- statement-local generator planning.

Do not begin branch merging or `Known/Unknown` lattice work yet.

That belongs with the future IF design.

---

# 29. Generator simplification boundary

The compiler may continue simplifying expressions with statement-local known values.

However:

- do not replace direct variable PRINT with an independent output literal in the targets covered by this release;
- do not replace required direct variable equality with a constant when the target can naturally compare current storage;
- preserve real SET storage updates.

The purpose is to make target runtime state observable.

---

# 30. Documentation updates

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- `docs/SMILE Target Code Generation Standard v1.0.md`;
- requirements/history;
- desktop About/version metadata if v0.5.1 is displayed;
- toolchain documentation if Java discovery changes.

## README

Document:

- Java SET integration is now fully validated;
- direct C/Objective-C String variable PRINT reads current pointer and logical length;
- direct C/Objective-C exact equality uses current target storage when applicable;
- COBOL direct variable PRINT reads current storage and logical length;
- v0.5.1 adds no syntax.

## Architecture

Explain:

```text
Reference evaluator state
    and
generated target runtime storage
```

must agree not only in final output but also in direct variable reads.

## Target generation standard

Add normative wording equivalent to:

> When a direct variable read has a clear target representation, generators must read the target variable's current storage rather than replacing the read with an unrelated compiler-time literal.

Add:

> C and Objective-C exact mutable String variables use pointer-plus-logical-length semantics when embedded NUL is possible.

Add:

> COBOL mutable String output uses current storage and current logical length.

---

# 31. AGENTS.md additions

Preserve all existing rules.

Add wording equivalent to:

> Direct variable PRINT should read the generated target variable's current storage. Do not replace a direct variable read with an unrelated compiler-time literal when the target can represent the read clearly.

Add:

> C and Objective-C mutable Strings that require exact byte semantics must keep pointer and logical length synchronized across LET and SET.

Add:

> COBOL direct mutable String output must use current storage and logical length.

Add:

> v0.5.1 is a syntax-free runtime-readiness release. Do not add IF or INPUT while implementing it.

---

# 32. Roadmap

Add:

## Implemented in v0.5.1

- complete Java SET runtime validation;
- direct C and Objective-C mutable String storage reads;
- length-aware current-storage equality;
- direct COBOL mutable storage reads;
- runtime-authenticity regression tests.

Keep the next major milestone:

```text
v0.6.0 — IF / THEN / ELSE
```

Do not implement IF in this release.

---

# 33. Version

Use:

```text
SMILE v0.5.1 — Runtime Storage Readiness
```

Keep assembly, file, informational, README, About, and roadmap versions aligned.

---

# 34. Required C and Objective-C structural tests

Add tests for:

## NUL-free direct variable PRINT

```smile
LET Name = "Sin"
SET Name = "Louiery"
PRINT {Name}
```

Assert output operation references `Name`.

## NUL-sensitive direct variable PRINT

```smile
LET Data = "ABC"
SET Data = "A\0B"
PRINT {Data}
```

Assert output operation references:

```text
Data
logical length companion
```

## Earlier and later values

```smile
LET Data = "First"
PRINT {Data}
SET Data = "Second"
PRINT {Data}
```

Assert both PRINT operations read `Data`.

Do not assert two different compiler-generated print literals.

## Block String

```smile
LET Message = ""

SET Message ="
A
 B
"

PRINT {Message}
```

Assert output reads `Message`.

---

# 35. Required C and Objective-C equality structural tests

Add tests for:

```smile
LET Left = "A\0B"
LET Right = "A\0B"
SET Right = "A\0C"

PRINT {Left = Right}
```

Assert generated equality references:

```text
Left
Right
LeftLength
RightLength
memcmp
```

or an equally clear exact storage comparison.

Add inequality coverage.

Add variable-versus-literal coverage.

---

# 36. Required COBOL structural tests

Add tests for:

```smile
LET Name = "First"
PRINT {Name}
SET Name = "Second"
PRINT {Name}
```

Assert:

- both PRINTs reference `Name`;
- SET contains `MOVE "Second" TO Name`;
- logical length is maintained where used;
- no direct PRINT is replaced solely with `DISPLAY "First"` or `DISPLAY "Second"`.

Add empty String variable coverage.

Add Block String coverage.

---

# 37. Required Java no-skip tests

The Java SET integration tests must fail when a full JDK is expected but missing in the official local validation environment.

Do not silently skip the release's Java acceptance program.

A test can remain environment-aware for other contributors, but the completion report must prove execution on Sin's machine.

Record:

```text
javac path
java path
detected version
compile exit code
run exit code
```

Do not commit machine-specific paths.

---

# 38. All-ten-target conformance

Run:

```text
examples/language.smile
```

through all ten installed targets.

Expected:

- build/run success;
- exit code 0;
- stdout matches `SmileEvaluator`;
- no target skipped in Sin's final validation.

Also run the runtime-authenticity program from section 25.

---

# 39. Exact-byte validation

Use exact byte comparisons for:

## NUL

```smile
LET Data = "ABC"
SET Data = "A\0B"
PRINT {Data}
```

Expected:

```text
41 00 42 0A
```

## Block String

```smile
LET Text = ""

SET Text ="
A
 B
"

PRINT {Text}
```

Expected bytes:

```text
41 0A 20 42 0A
```

## Empty String

```smile
LET Text = "X"
SET Text = ""
PRINT {Text}
```

Expected:

```text
0A
```

## Trailing space

```smile
LET Text = ""

SET Text ="
A 
B
"

PRINT {Text}
```

Expected bytes:

```text
41 20 0A 42 0A
```

---

# 40. Desktop validation

1. Launch SMILE Desktop.
2. Confirm the window paints before loading `language.smile`.
3. Confirm `language.smile` loads successfully.
4. Confirm visible targets transpile without freezing.
5. Select Java and Build & Run.
6. Confirm Java completes successfully.
7. Select C and inspect direct String variable PRINT.
8. Select Objective-C and inspect the same.
9. Select COBOL and inspect direct variable DISPLAY logic.
10. Run the runtime-authenticity program.
11. Run the embedded-NUL program.
12. Run the Block String program.
13. Confirm exact output.
14. Rapidly switch targets.
15. Confirm responsiveness.
16. Confirm New reloads `language.smile`.
17. Confirm About shows v0.5.1.

---

# 41. Performance

Do not move generation or toolchain work onto the WPF dispatcher.

Preserve:

- first-paint initialization;
- asynchronous language-file loading;
- source-revision guard;
- debounced live transpilation;
- visible-target-only generation;
- cancellation;
- separate timeouts;
- bounded output;
- failure containment.

C-family and COBOL runtime-read changes must not introduce unbounded generated code growth.

---

# 42. Scope exclusions

Do not implement:

- `IF`;
- `THEN`;
- `ELSE`;
- `INPUT`;
- loops;
- functions;
- scopes;
- arrays;
- floating-point;
- comments;
- another destination language;
- a new runtime library;
- branch state merging;
- a `Known/Unknown` value lattice;
- a feature branch.

---

# 43. Acceptance criteria

The task is complete only when all are true:

1. v0.5.1 adds no syntax.
2. Java JDK detection is accurate.
3. Java SET integration tests execute.
4. Java Block String integration tests execute.
5. Java wide Integer SET tests execute.
6. Java embedded-NUL tests execute.
7. Java `language.smile` builds and runs.
8. C direct String variable PRINT reads current variable storage.
9. Objective-C direct String variable PRINT reads current variable storage.
10. C exact String PRINT uses current logical length when needed.
11. Objective-C exact String PRINT uses current logical length when needed.
12. C SET keeps pointer and length synchronized.
13. Objective-C SET keeps pointer and length synchronized.
14. C variable equality uses current storage where required.
15. Objective-C variable equality uses current storage where required.
16. NUL prefix collisions remain exact.
17. unequal lengths remain exact.
18. inequality remains exact.
19. COBOL SET emits real MOVE.
20. COBOL direct variable PRINT reads current storage.
21. COBOL direct variable PRINT uses logical length.
22. COBOL empty String PRINT emits only newline.
23. COBOL Block String PRINT is exact.
24. MASM runtime storage behavior remains correct.
25. high-level target assignments remain natural.
26. direct variable output is not replaced with unrelated literals in the hardened targets.
27. exact NUL bytes match.
28. exact Block String bytes match.
29. exact trailing spaces match.
30. all ten targets remain supported.
31. all ten targets execute on Sin's validation machine.
32. all ten targets match `SmileEvaluator`.
33. Debug build has zero warnings.
34. Release build has zero warnings.
35. Debug tests pass.
36. Release tests pass.
37. no unexpected skips remain.
38. generation remains deterministic.
39. Desktop remains responsive.
40. `language.smile` remains cumulative and deployable.
41. documentation matches implementation.
42. destination-language expansion remains frozen.
43. no unrelated feature is added.
44. no unapproved dependency is added.
45. no build artifacts are committed.
46. all work is performed directly on `main`.

---

# 44. Suggested implementation sequence

1. Confirm newest `main`.
2. Reproduce Java integration skips.
3. Repair or clarify JDK discovery.
4. Execute Java SET acceptance tests.
5. Add failing C direct variable PRINT structural tests.
6. Implement C runtime storage reads.
7. Apply the same design to Objective-C.
8. Add failing C-family equality structural tests.
9. Implement length-aware storage equality.
10. Add failing COBOL direct variable PRINT tests.
11. Implement COBOL storage-based output.
12. Run focused C/Objective-C/COBOL tests.
13. Run exact-byte tests.
14. Run Java integration.
15. Run all ten targets.
16. Run Debug build/tests.
17. Run Release build/tests.
18. Run Desktop smoke tests.
19. Update documentation and version metadata.
20. Commit directly to `main` only when Sin explicitly authorizes it.

---

# 45. Validation commands

Run from the repository root:

```bat
cmd /c git status --short --branch
```

Confirm:

```text
main
```

```bat
cmd /c where java
```

```bat
cmd /c where javac
```

```bat
cmd /c java -version
```

```bat
cmd /c javac -version
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
cmd /c dotnet run --project src\SMILE.Cli -- examples\language.smile --target all
```

Run Java:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\language.smile --target java --run
```

Run C:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <RUNTIME_AUTHENTICITY_EXAMPLE.smile> --target c --run
```

Run Objective-C:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <RUNTIME_AUTHENTICITY_EXAMPLE.smile> --target objective-c --run
```

Run COBOL:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- <RUNTIME_AUTHENTICITY_EXAMPLE.smile> --target cobol --run
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

# 46. Completion report

Report:

- exact baseline commit;
- exact files changed;
- Java detection changes;
- detected Java and javac versions;
- exact Java integration test count;
- exact remaining skipped test count;
- C variable PRINT strategy;
- Objective-C variable PRINT strategy;
- C-family logical-length strategy;
- C-family equality strategy;
- COBOL variable PRINT strategy;
- COBOL empty String strategy;
- MASM regression status;
- exact Debug test count;
- exact Release test count;
- zero-warning results;
- all-ten-target runtime results;
- exact-byte NUL results;
- exact-byte Block String results;
- exact-byte trailing-space results;
- Desktop smoke results;
- documentation changes;
- unresolved concerns.

Do not claim runtime-storage readiness if:

- direct variable PRINT still emits only an independent literal;
- current logical lengths are not read;
- Java SET integration remains skipped;
- COBOL direct variable PRINT still displays only a compiler-time literal.

---

# 47. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

Suggested subject:

```text
Sin and Codex: Harden runtime storage reads
```

Suggested body topics:

- complete Java SET validation;
- C and Objective-C current String storage reads;
- length-aware current-storage equality;
- COBOL current storage output;
- exact-byte conformance;
- all-ten-target validation;
- no new syntax.
