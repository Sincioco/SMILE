# Codex Implementation Instructions — SMILE v0.7.0 INPUT

> [!IMPORTANT]
> **HISTORICAL / PARTIALLY SUPERSEDED**
>
> This document records the original INPUT implementation milestone. Its `INPUT variable` syntax, fixed-type mutation model, runtime-unknown analysis, and responsive interactive-execution goals remain useful history. Requirements for strict UTF-8 byte handling, a universal 4096-byte limit, embedded-NUL console input, identical runtime errors, custom generated input runtimes, all-ten-target maintenance, and routine strict validation are superseded by `docs/SMILE Core Principles.md` and the current official INPUT specification.

## Repository and workflow

- Repository: `Sincioco/SMILE`
- Work directly on `main`.
- Sin is the only developer.
- Do not create or suggest a feature branch.
- Do not open a pull request.
- Re-read `AGENTS.md` before changing code.
- Inspect the newest `main` commit, working tree, current specifications, current roadmap, and current `SMILE CI` workflow before editing.
- Do not discard, reset, overwrite, or commit unrelated work.
- Do not force-push or rewrite published history.
- Follow KISS and KISS v2, “The Sin Way.”
- Preserve the permanent ten-target destination-language freeze.
- Commit all intended changes and push only after required validation is green.
- After pushing, verify that the `SMILE CI` run for the exact final `main` SHA completes successfully before reporting completion.

The reviewed baseline when this brief was prepared was:

```text
8d323c8e3a2c68769962c98908125abd6a740368
Sin and Codex: Refresh the SMILE mission
```

Do not assume that SHA is still current. Always begin from the newest `main`.

---

# 1. Companion official specification

Use the complete companion document:

```text
008 - SMILE - INPUT Statement Official Specification v1.0.md
```

Publish it at:

```text
docs/SMILE Language Specification/008 - SMILE - INPUT Statement Official Specification v1.0.md
```

The numeric specification prefixes are intentional repository organization chosen by Sin.

Do not rename, remove, reorder, or treat the existing `001` through `007` prefixes as a defect.

Update their cross-links only where `INPUT` changes a normative statement.

---

# 2. Milestone

Create:

> **SMILE v0.7.0 — INPUT**

Implement the canonical statement:

```smile
INPUT variable
```

The statement reads one logical line from standard input and assigns a value to an already-declared variable according to its fixed SMILE type.

---

# 3. Scope warning

This milestone is not merely parser syntax plus `Console.ReadLine()`.

`INPUT` introduces the first SMILE values that are:

```text
known to have a type
but not known at compile time
```

A correct implementation must update:

- lexer and parser;
- syntax and bound trees;
- binding;
- static Known/Unknown analysis;
- simplification;
- evaluator;
- runtime-error reporting;
- checked runtime Integer arithmetic;
- String storage planning;
- all ten generators;
- process execution and scripted stdin;
- CLI and Desktop interactive execution;
- tests, documentation, examples, and version identity.

Do not ship a partial implementation that still propagates the LET initializer past INPUT.

---

# 4. Permanent syntax decisions

Implement exactly:

```text
INPUT hspace+ identifier hspace* statement-end
```

Rules:

- `INPUT` is case-insensitive.
- It becomes a globally reserved keyword.
- It accepts exactly one identifier.
- The target must already be declared.
- Variable lookup remains ordinal case-insensitive.
- The target's existing type determines conversion.
- INPUT is a statement, not an expression.
- INPUT is allowed at top level and inside every IF-related body.
- No prompt form exists in v1.0.
- No comma-separated targets exist.
- No automatic retry exists.
- Inline comments remain unsupported.
- Comments and blank lines around INPUT remain preserved.

---

# 5. Non-goals

Do not add:

- `INPUT "Prompt", Variable`;
- `INPUT AS`;
- declaration through INPUT;
- multiple targets;
- inline comments;
- loops;
- functions;
- procedures;
- scopes;
- arrays;
- classes;
- floating-point or decimal values;
- exception syntax;
- automatic retry;
- another destination language;
- external runtime libraries or packages;
- a parser generator;
- a general virtual machine.

Do not weaken existing LET, SET, PRINT, IF, String, comment, or source-layout rules.

---

# 6. Inspect current architecture first

Inspect at minimum:

```text
AGENTS.md
README.md
docs/Roadmap.md
docs/Architecture.md
docs/Toolchains.md
docs/SMILE Target Code Generation Standard v1.0.md

docs/SMILE Language Specification/
    001 through 007

examples/language.smile

src/SMILE.Engine/Language.cs
src/SMILE.Engine/SyntaxKind.cs
src/SMILE.Engine/SyntaxToken.cs
src/SMILE.Engine/Lexer.cs
src/SMILE.Engine/Parser.cs
src/SMILE.Engine/Binder.cs
src/SMILE.Engine/Evaluation.cs
src/SMILE.Engine/ExecutionTrace.cs
src/SMILE.Engine/Analysis.cs
src/SMILE.Engine/Generation.cs
src/SMILE.Engine/Generation/
src/SMILE.Engine/FullLineCommentFacts.cs

src/SMILE.Toolchains/ProcessRunner.cs
src/SMILE.Toolchains/Toolchains.cs

src/SMILE.Cli/
src/SMILE.Desktop/
src/SMILE.Desktop/Highlighting/SMILE.xshd

tests/SMILE.Tests/
.github/workflows/smile-ci.yml
```

Preserve the v0.6.1 ordered source-item architecture for comments and blank lines.

---

# 7. Task 1 — Add INPUT syntax

Add:

```csharp
InputKeyword
```

to the keyword/token model.

Add a canonical syntax node equivalent to:

```csharp
public sealed record InputStatementSyntax(
    string Name,
    TextSpan NameSpan,
    TextSpan Span)
    : StatementSyntax(Span);
```

Do not represent INPUT as SET, a function call, or a special expression.

---

# 8. Task 2 — Parse INPUT

Implement parsing consistent with existing physical-line statement rules.

Required forms:

```smile
INPUT Name
input Name
INPUT	Name
INPUT Name    
```

Reject:

```smile
INPUT
INPUT"Name"
INPUT 49
INPUT {Name}
INPUT Name Extra
INPUT Name, Other
```

Use the official diagnostics:

| Code | Meaning |
|---|---|
| `SMILE1501` | INPUT must be followed by whitespace |
| `SMILE1502` | INPUT requires a target variable |
| `SMILE1503` | INPUT target must be one identifier |
| `SMILE1504` | Unexpected content follows the INPUT target |

`INPUTAge` remains an ordinary identifier/unknown statement rather than being split into INPUT plus Age.

Preserve exact source spans and physical line numbers.

---

# 9. Task 3 — Reserve INPUT

Add `INPUT` to:

- case-insensitive keyword classification;
- reserved variable-name checking;
- SMILE syntax highlighting;
- documentation keyword lists;
- target identifier tests where source keywords are relevant.

`REM` remains contextual and is not affected.

---

# 10. Task 4 — Bind INPUT

Add a canonical bound node equivalent to:

```csharp
public sealed record BoundInputStatement(
    VariableSymbol Variable)
    : BoundStatement;
```

Binding rules:

- target must resolve to an existing variable;
- lookup is ordinal case-insensitive;
- no new symbol is declared;
- target type remains unchanged;
- undefined target reports `SMILE1505`;
- INPUT is valid inside IF bodies;
- INPUT has no expression child;
- layout items remain in exact order.

Do not append a fake value to the old concrete execution trace.

---

# 11. Task 5 — Remove binding's concrete-value requirement

The current binder incrementally executes source-known statements through `BoundProgramExecutionTraceBuilder`.

That assumption cannot remain mandatory after INPUT.

Refactor so:

- successful binding does not require every statement value to be concrete;
- Unknown is not treated as invalid;
- a failed LET still does not leak a declaration;
- a failed SET still does not replace the prior static fact;
- source-known reachable arithmetic errors still report compile diagnostics;
- runtime-dependent expressions do not produce false compile errors;
- unreachable short-circuit failures remain suppressed;
- every branch is still parsed, bound, and type-checked.

Do not invent a dummy input value such as `0`, `""`, or `FALSE`.

Do not use the LET initializer as the post-INPUT value.

---

# 12. Task 6 — Separate Unknown from Invalid

Create or extend a static evaluation result that distinguishes at least:

```text
Known(value)
Unknown
Invalid(diagnostic)
```

The exact API may differ, but `TryEvaluate == false` must no longer ambiguously mean both runtime-unknown and semantically invalid in compiler phases that need the distinction.

Requirements:

- missing runtime value after INPUT -> Unknown;
- known division by zero on a definitely evaluated path -> Invalid SMILE1207;
- known overflow on a definitely evaluated path -> Invalid SMILE1206;
- runtime-dependent division -> Unknown with runtime check required;
- unreachable short-circuit right side -> not evaluated;
- conditionally reachable branch failure -> runtime behavior, not an unconditional compile error.

Keep KISS: one shared static-evaluation model, not multiple target-specific versions.

---

# 13. Task 7 — Update BoundProgramAnalysis

Teach branch-aware analysis about `BoundInputStatement`.

After INPUT:

## String

```text
Known/Unknown: Unknown
Maximum UTF-8 bytes: 4096
May contain NUL: true
Possible values: inexact
```

## Integer

```text
Known/Unknown: Unknown
Range: long.MinValue through long.MaxValue
Possible values: inexact
```

## Boolean

```text
Known/Unknown: Unknown
Possible values: TRUE and FALSE
```

Also:

- mark the variable mutated;
- remove any selected concrete value for it;
- never select a concrete IF branch when the condition is runtime-unknown;
- merge all possible outgoing paths conservatively;
- do not let a source-selected branch stand in for runtime behavior;
- keep statement ordinals deterministic;
- include INPUT in source-order statement enumeration.

Generators must rely on abstract facts, not on an invented concrete run.

---

# 14. Task 8 — Update simplification

In `BoundProgramSimplifier`:

- preserve INPUT in source order;
- remove the target variable from the known-value environment after INPUT;
- preserve comments and blank lines around it;
- do not fold later reads back to the LET initializer;
- do not remove INPUT;
- do not reorder INPUT;
- do not duplicate INPUT;
- do not simplify an IF away because a pre-input value was formerly known.

Add direct regressions.

---

# 15. Task 9 — Extend runtime expression semantics

Update specification `004` so SMILE's checked arithmetic rules cover runtime-unknown values.

Preserve:

```text
compile-time SMILE1206 for definitely evaluated source-known overflow
compile-time SMILE1207 for definitely evaluated source-known division by zero
```

Add:

```text
runtime SMILER1206 for reached runtime overflow
runtime SMILER1207 for reached runtime division by zero
```

Every target must check:

- unary negation;
- addition;
- subtraction;
- multiplication;
- division by zero;
- long.MinValue / -1.

Do not rely on target-default overflow behavior.

Do not let JavaScript Number approximate signed 64-bit values.

---

# 16. Task 10 — Add runtime error model

Add a small shared runtime-error representation, such as:

```csharp
public sealed record SmileRuntimeError(
    string Code,
    string Message);
```

Extend `EvaluationResult` without losing existing compile-diagnostic behavior.

The result must expose:

- success;
- stdout produced so far;
- stderr;
- exit code;
- compile diagnostics;
- optional runtime error.

Preserve a convenient existing `Evaluate(source)` path for programs with no executed INPUT.

Runtime errors use exit code 1 and exactly one canonical stderr line.

---

# 17. Task 11 — Add injectable evaluator input

The reference evaluator must not read the real process console during tests.

Provide a simple abstraction based on `TextReader`, an interface, or equivalent.

Recommended overloads:

```csharp
Evaluate(string source)
Evaluate(string source, TextReader input)
Evaluate(string source, string scriptedStandardInput)
```

`Evaluate(source)` should behave as EOF when an executed INPUT requests data.

Implement the official line rules:

- CRLF;
- LF;
- standalone CR;
- final non-empty line at EOF;
- empty terminated line;
- 4096 UTF-8 byte maximum;
- strict type conversion.

Keep exact stdout and stderr.

---

# 18. Task 12 — Centralize INPUT conversion

Add one shared evaluator-side conversion helper that implements:

- UTF-8 byte counting;
- String preservation;
- ASCII space/tab trim for Integer and Boolean only;
- signed decimal grammar;
- signed 64-bit range;
- ordinal case-insensitive TRUE/FALSE;
- canonical runtime errors.

Target generators may emit equivalent native helpers, but all conformance tests compare them to this reference implementation.

---

# 19. Task 13 — ProcessRunner input modes

The current `ProcessRunner` redirects and immediately closes stdin.

Extend it deliberately.

Support three clear modes:

```text
Closed
ScriptedText
InteractiveInherited
```

Equivalent API designs are acceptable.

## Closed

Preserve current behavior for noninteractive captured tasks.

## ScriptedText

- redirect stdin;
- write the complete provided test input;
- flush;
- close stdin;
- capture stdout and stderr;
- preserve timeout and cancellation behavior.

## InteractiveInherited

- do not close stdin;
- make prompts visible before input is requested;
- use the invoking console or a visible console window;
- do not buffer all stdout until process exit;
- preserve process exit code.

Do not make interactive execution the default for compiler invocations.

Do not allow a hidden Desktop process to wait forever for invisible input.

---

# 20. Task 14 — Toolchain scripted stdin

Extend the generated-program run path so strict tests can provide scripted stdin to every target.

Keep compiler processes closed-input and captured.

Only the generated program receives scripted input.

Add exact comparisons for:

- stdout;
- stderr;
- exit code;
- timeout;
- cancellation.

No shell-specific `echo` pipelines should be the primary conformance mechanism because they can corrupt Unicode, NUL, quoting, or final-newline behavior.

---

# 21. Task 15 — CLI interactive execution

When the CLI runs a generated program containing INPUT:

- inherit the user's terminal input;
- stream prompts and output live;
- preserve stderr;
- return or report the generated program's exit code;
- support redirected stdin naturally.

Do not capture prompts invisibly and display them only after the program exits.

Non-INPUT programs may retain the current captured path where appropriate.

---

# 22. Task 16 — Desktop interactive execution

The Desktop currently uses a captured process path that closes stdin.

For a bound program containing `BoundInputStatement`:

1. generate and build normally;
2. run using a visible interactive console path;
3. show PRINT prompts live;
4. allow normal keyboard entry;
5. show runtime errors in that console;
6. keep the WPF UI responsive;
7. report that an interactive console was launched;
8. avoid running a second hidden copy;
9. preserve cancellation/failure containment where practical.

The preferred KISS implementation is to reuse or extend the existing generated launcher mechanism and open a visible console.

Do not pre-collect every input line before the learner can see runtime prompts.

Do not block the WPF UI thread.

---

# 23. Task 17 — Common input limit

Define one shared compiler constant:

```text
MaximumInputLineUtf8Bytes = 4096
```

Use it consistently in:

- evaluator;
- static String-size analysis;
- C-family buffers;
- MASM buffers;
- COBOL storage/helper;
- generated runtime checks;
- documentation;
- tests.

Do not scatter unexplained `4096` literals across generators.

---

# 24. Task 18 — C# generation

Generate idiomatic C# with:

- UTF-8 console input configuration where required;
- `Console.ReadLine()` or a shared generated helper;
- `long` for Integer INPUT;
- strict invariant parsing;
- exact Boolean parsing;
- UTF-8 byte-limit check;
- canonical stderr and exit code;
- checked runtime arithmetic.

Emit helpers only when needed.

Preserve source comments and blank lines.

---

# 25. Task 19 — JavaScript generation

Use Node.js standard input without external packages.

Requirements:

- read UTF-8 stdin deterministically;
- consume logical lines in execution order;
- distinguish empty line from EOF;
- support CRLF, LF, and CR;
- use `BigInt` for runtime Integer INPUT;
- enforce signed 64-bit bounds after parsing and arithmetic;
- write runtime errors to `process.stderr`;
- set `process.exitCode = 1` or exit consistently;
- preserve source layout.

Do not use Number for an INPUT-dependent SMILE Integer.

---

# 26. Task 20 — Java generation

Use standard Java facilities only.

Requirements:

- UTF-8 reader;
- one-line consumption;
- `long`;
- strict decimal parsing with optional plus/minus;
- exact TRUE/FALSE parsing;
- `Math.*Exact` or equivalent checked helpers;
- explicit division checks;
- canonical stderr and exit code;
- Java Unicode-escape-safe generated comments remain preserved.

---

# 27. Task 21 — Python generation

Use standard-library Python only.

Prefer reading from `sys.stdin.buffer` so:

- UTF-8 decoding can be strict;
- NUL is preserved;
- EOF and empty line are distinct;
- CRLF/LF/CR can be normalized explicitly.

Requirements:

- signed 64-bit range validation despite Python's arbitrary precision;
- checked result range after every runtime Integer operation;
- canonical stderr;
- `sys.exit(1)`;
- existing `_smile_text` and `_smile_div` helpers remain correct;
- layout and comment preservation remains intact.

---

# 28. Task 22 — Swift generation

Use Swift standard facilities only.

Requirements:

- Unicode line input;
- exact UTF-8 byte count;
- Int64 parsing;
- TRUE/FALSE parsing;
- reporting-overflow operations or explicit checks;
- division checks;
- stderr via standard error;
- exit code 1;
- layout preservation.

---

# 29. Task 23 — C++ generation

Use C++20 standard facilities.

Requirements:

- `std::getline`;
- preserve embedded NUL;
- remove only the consumed logical line terminator;
- `std::int64_t`;
- strict parsing such as `std::from_chars` plus explicit optional-plus handling;
- checked arithmetic;
- `std::cerr`;
- return 1 on runtime error;
- no external dependencies.

---

# 30. Task 24 — C and Objective-C generation

Use dependency-free generated helpers.

Do not use `strlen` to determine an input line's logical size because redirected input may contain NUL.

Implement:

- explicit byte-counted line reading;
- CRLF/LF/CR handling;
- strict UTF-8 validation or equivalent platform conversion;
- 4096-byte enforcement;
- stable String storage plus logical length;
- signed 64-bit parsing;
- Boolean parsing;
- checked arithmetic;
- stderr;
- exit code 1.

Objective-C may share the proven C helper logic while retaining its own generator.

Ordinary no-INPUT programs should not receive unnecessary input helpers.

---

# 31. Task 25 — MASM x64 generation

Implement native Windows input support without external libraries.

Use appropriate Windows APIs for:

- stdin handle;
- stdout/stderr handles;
- interactive and redirected input;
- UTF-8 or Unicode conversion;
- exact byte length;
- String storage;
- Integer/Boolean parsing;
- checked arithmetic;
- runtime error output;
- exit code 1.

Requirements:

- stable per-variable String storage when later INPUT overwrites the shared read buffer;
- explicit logical length so NUL remains observable;
- signed overflow flag checks;
- safe handling before `idiv`;
- deterministic labels;
- no input code in programs that do not use INPUT.

---

# 32. Task 26 — COBOL generation

Preserve exact v1 semantics under the existing GnuCOBOL toolchain.

Native `ACCEPT` may be used only if tests prove it preserves:

- leading spaces;
- trailing spaces;
- empty lines;
- exact logical length;
- Unicode;
- NUL in redirected input;
- EOF distinction;
- 4096-byte limit.

If native facilities cannot meet the contract, generate a small dependency-free C companion helper and link it with the generated COBOL program.

Requirements:

- primary file remains `Program.cob`;
- any helper is an ancillary generated file;
- toolchain builds both deterministically;
- no installed third-party library is added;
- use full signed 64-bit storage;
- use `ON SIZE ERROR` or explicit checked logic where appropriate;
- stderr and exit code match the specification;
- source comments and blank lines still appear once in the correct COBOL region.

---

# 33. Task 27 — String storage planning

Any String variable targeted by INPUT must be planned for:

```text
0 through 4096 UTF-8 bytes
may contain NUL
```

Later concatenation and interpolation sizes must compose from that maximum.

Update:

- C and Objective-C runtime buffers;
- MASM buffers;
- COBOL storage;
- expression display facts;
- exact NUL flags;
- direct variable PRINT;
- String equality;
- SET after INPUT;
- INPUT after SET;
- repeated INPUT into the same variable.

Do not truncate silently.

---

# 34. Task 28 — Integer profile planning

Any Integer variable targeted by INPUT forces full signed 64-bit semantics for:

- its own storage;
- later expressions that depend on it;
- IF comparisons;
- interpolation and PRINT;
- SET destinations receiving its derived values.

Add tests proving that a small initializer:

```smile
LET Age = 0
INPUT Age
```

does not cause `int`, Number, or another narrow representation to be selected incorrectly.

---

# 35. Task 29 — IF integration

Allow INPUT in all IF-related bodies.

Update IF specification `006` to list INPUT among permitted branch statements.

Test:

- INPUT in initial IF body;
- INPUT in ELSE IF;
- INPUT in ELSE;
- nested INPUT;
- unselected branch consumes no input;
- first successful clause consumes only its lines;
- post-IF merge is Unknown when paths differ;
- comments and blanks remain correctly placed;
- comment-only and INPUT-only bodies generate valid target code.

---

# 36. Task 30 — Full-line comments and blank lines

Preserve specification `007` without regression.

Required examples:

```smile
LET Age = 0

// Read age.
INPUT Age

PRINT {Age}
```

Every target must retain:

- the blank line;
- native comment syntax;
- INPUT operation;
- second blank line.

Marker-looking text inside Block Strings remains data.

---

# 37. Task 31 — Syntax highlighting

Update AvalonEdit SMILE highlighting:

- add `INPUT` as a case-insensitive keyword;
- keep full-line comment recognition unchanged;
- do not highlight INPUT inside comments or Strings;
- keep Block String ownership;
- keep target-language highlighting intact.

Add tests using the existing highlighting engine.

---

# 38. Task 32 — Examples

Extend the cumulative:

```text
examples/language.smile
```

with an active INPUT section so it remains the cumulative language reference.

Use a small deterministic sequence for scripted tests.

Update all cumulative evaluator and target tests to supply the canonical input.

Also add a focused example:

```text
examples/input.smile
```

that demonstrates:

- String;
- Integer;
- Boolean;
- IF after INPUT;
- comments;
- blank lines;
- runtime prompts.

Package it with Desktop output if repository conventions package examples.

---

# 39. Task 33 — Parser and binder tests

Add tests for:

- all INPUT casing variants;
- spaces and tabs;
- missing whitespace;
- missing target;
- nonidentifier target;
- trailing text;
- comma-separated target;
- undefined target;
- case-insensitive variable lookup;
- reserved INPUT variable name;
- valid REM variable target;
- top-level INPUT;
- each IF branch;
- comments and blank lines;
- Block String text containing INPUT.

Verify exact diagnostics and spans.

---

# 40. Task 34 — Evaluator tests

Add scripted tests for:

## String

- normal text;
- leading/trailing spaces;
- tabs;
- Unicode;
- empty line;
- final line without terminator;
- NUL through scripted input;
- 4096-byte boundary;
- 4097-byte failure;
- repeated INPUT.

## Integer

- zero;
- plus sign;
- minus sign;
- surrounding ASCII spaces/tabs;
- min Int64;
- max Int64;
- malformed text;
- empty line;
- positive overflow;
- negative overflow.

## Boolean

- all casing forms;
- surrounding ASCII spaces/tabs;
- invalid alternatives.

## EOF and decode errors

- EOF before line;
- malformed UTF-8 where the test infrastructure can supply raw bytes.

Assert stdout, stderr, exit code, and assignment behavior.

---

# 41. Task 35 — Static-analysis regressions

Add direct tests proving:

```smile
LET Age = 0
INPUT Age
PRINT {Age}
```

does not print a folded zero.

Also test:

- INPUT after SET;
- SET after INPUT;
- INPUT in one branch only;
- INPUT in all branches;
- same known assignment after all branches;
- String max size;
- NUL possibility;
- Boolean possible values;
- full Integer range;
- mutation tracking;
- deterministic ordinals.

---

# 42. Task 36 — Runtime arithmetic tests

Use input-dependent operands to test every checked operation.

Success cases and failure cases must run through:

- evaluator;
- all ten target programs.

Include:

- add overflow;
- subtract overflow;
- multiply overflow;
- unary negation of long.MinValue;
- divide by zero;
- long.MinValue divided by -1;
- truncation toward zero;
- safe short-circuit suppression;
- branch-not-selected suppression;
- reached conditional error.

Require canonical stderr and exit code 1.

---

# 43. Task 37 — All-ten-target scripted conformance

Create one normative acceptance test from the official specification.

For each target:

1. transpile;
2. inspect structural input lowering;
3. build;
4. provide identical scripted stdin without shell `echo`;
5. capture exact stdout;
6. capture exact stderr;
7. capture exit code;
8. compare against `SmileEvaluator`;
9. require zero generated compiler warnings.

Run separate invalid-input and runtime-arithmetic cases.

Do not skip a target silently under strict gates.

---

# 44. Task 38 — Determinism

Verify repeated generation with INPUT is byte-identical.

Determinism covers:

- input helper names;
- runtime error text;
- buffers;
- labels;
- ancillary files;
- comment placement;
- blank lines;
- target project files.

No hash-order-dependent helper emission is allowed.

---

# 45. Task 39 — Performance and responsiveness

Keep:

- Desktop first paint;
- debounced visible-target transpilation;
- target-switch caching;
- cancellation;
- bounded output;
- process timeouts;
- WPF responsiveness.

Interactive generated programs may wait for learner input, but the Desktop UI must remain responsive.

Compiler and test processes must not wait for input.

---

# 46. Task 40 — Version and documentation

Update identity to:

```text
0.7.0 INPUT
```

Update applicable files:

- Desktop project metadata;
- About dialog;
- README;
- roadmap;
- architecture;
- toolchain documentation;
- target-generation standard;
- high-level language specification;
- AGENTS permanent rules;
- requirements/progress history;
- official specification index and cross-links;
- `004` runtime arithmetic language;
- `005` LET relationship;
- `006` allowed IF body statements;
- `007` layout interaction.

Do not renumber existing specification files.

Add `008` after `007`.

---

# 47. Required normal validation

Run from the actual repository root. Examples use `D:\SMILE`.

```bat
cmd /c "cd /d D:\SMILE && dotnet restore SMILE.sln"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Debug --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet build SMILE.sln -c Release --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Required:

- zero build warnings;
- zero build errors;
- zero test failures;
- zero unexpected skips.

---

# 48. Required strict validation

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Debug --no-build --no-restore -nologo"
```

```bat
cmd /c "cd /d D:\SMILE && set SMILE_REQUIRE_JAVA=1 && set SMILE_REQUIRE_ALL_TARGETS=1 && set SMILE_REQUIRE_ZERO_TARGET_WARNINGS=1 && dotnet test SMILE.sln -c Release --no-build --no-restore -nologo"
```

Both must require:

- Java;
- all ten target toolchains;
- scripted stdin;
- exact stdout;
- exact stderr;
- exact exit codes;
- zero generated compiler warnings;
- no target skips.

---

# 49. Desktop smoke test

Manually verify:

1. first paint remains responsive;
2. cumulative reference loads;
3. INPUT highlights as a keyword;
4. comments and blank lines remain preserved;
5. generated panes show real target input code;
6. a String/Integer/Boolean example launches interactively;
7. prompts are visible before typing;
8. the Desktop UI remains responsive while the console waits;
9. invalid Integer input shows canonical stderr and exit 1;
10. representative C#, MASM, COBOL, Python, and JavaScript programs work;
11. rapid target switching remains responsive;
12. About displays `0.7.0 INPUT`.

---

# 50. Acceptance criteria

This work is complete only when:

## Language

- INPUT syntax matches specification 008.
- INPUT targets an existing variable.
- fixed String/Integer/Boolean types are honored.
- INPUT works at top level and inside IF.
- prompt syntax and retry are absent.
- exact diagnostics are implemented.

## Unknown-state architecture

- binding no longer requires a concrete value for every statement;
- Unknown is distinct from Invalid;
- pre-input values are never propagated past INPUT;
- branch analysis is conservative;
- simplification preserves INPUT;
- String and Integer profiles are safe.

## Runtime

- evaluator accepts scripted input;
- all input conversions match;
- 4096-byte limit is enforced;
- stdout/stderr/exit are canonical;
- runtime arithmetic is checked;
- short-circuit and branch reachability are correct.

## Toolchains

- scripted stdin works for all ten target tests;
- CLI supports live terminal interaction;
- Desktop uses a visible interactive path;
- compiler processes never wait for stdin.

## Targets

- all ten targets build and run;
- exact evaluator conformance passes;
- zero generated warnings;
- deterministic source and helper files;
- comments and blank lines remain preserved.

## Release

- documentation and About say `0.7.0 INPUT`;
- `008` exists with the exact numbered filename;
- cumulative and focused examples are updated;
- all normal and strict validation passes;
- changes are committed and pushed;
- exact-final-SHA `SMILE CI` concludes success.

---

# 51. Commit message

Use a detailed public commit message similar to:

```text
Sin and Codex: Add runtime INPUT

Release SMILE v0.7.0 INPUT.

Add case-insensitive INPUT variable statements for existing fixed-type String, Integer, and Boolean variables at top level and inside IF bodies. Introduce canonical syntax and bound nodes, injectable evaluator input, exact runtime errors, UTF-8 line handling, the 4096-byte cross-target limit, and scripted stdin support.

Make runtime input values genuinely Unknown to binding, simplification, branch analysis, Integer profiles, String sizing, and NUL planning. Preserve source-known compile diagnostics while adding checked runtime signed-64 arithmetic and short-circuit-correct overflow and division failures.

Generate native input handling for all ten targets, preserve comments and blank lines, support live CLI input and visible responsive Desktop interactive execution, and add exact stdout/stderr/exit-code conformance with zero generated warnings.

Validation: <insert exact normal and strict results>. Post-push SMILE CI: <insert exact final run ID and successful conclusion>.
```

Replace placeholders with actual results.

Commit all intended changes and push to `main`.

Do not create a Git tag or GitHub Release unless Sin explicitly asks or the repository later establishes that convention.

---

# 52. Mandatory post-push completion gate

After pushing:

1. read the exact current `main` SHA;
2. find the `SMILE CI` run for that exact SHA;
3. wait until it is completed;
4. require conclusion `success`;
5. verify Restore, Debug Build/Test, and Release Build/Test succeeded.

Do not use an older run.

If CI fails:

- inspect the failed step and logs;
- fix the root cause;
- rerun applicable validation;
- create a normal follow-up commit;
- push;
- verify the replacement exact-SHA run.

Do not force-push or hide failed history.

---

# 53. Completion report to Sin

Report:

- final commit SHA;
- push status;
- version identity;
- files added and changed;
- specification path;
- syntax and bound nodes;
- Unknown/Invalid architecture;
- evaluator input API;
- ProcessRunner input modes;
- Desktop interactive behavior;
- target-by-target input strategy;
- runtime error model;
- checked arithmetic behavior;
- exact normal Debug/Release results;
- exact strict Debug/Release results;
- test counts and skips;
- all-ten-target conformance;
- generated-warning result;
- normative valid-run stdout/stderr/exit;
- invalid-input results;
- cumulative example results;
- Desktop smoke results;
- GitHub Actions run ID and conclusion;
- whether a corrective follow-up commit was required;
- remaining known limitations.

Highlight these as ready for testing:

- **String INPUT**
- **Integer INPUT**
- **Boolean INPUT**
- **INPUT inside IF**
- **Runtime-unknown analysis**
- **Checked runtime Integer arithmetic**
- **All-ten-target scripted input**
- **Interactive CLI and Desktop input**
- **SMILE v0.7.0 INPUT**
