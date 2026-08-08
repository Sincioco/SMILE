# Codex Implementation Instructions — SMILE v0.4.2 Add Python as a Target

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
- Do not add a compiler framework, code-generation framework, template engine, CLI framework, Python package dependency, or SMILE runtime library.

---

# 1. Mandatory prerequisite

This target-expansion milestone must begin **after** SMILE v0.4.1 Typed Expression Conformance Hardening is complete and committed on `main`.

Before adding Python, verify that `main` includes:

- official left-to-right short-circuit semantics for `AND` and `OR`;
- short-circuit behavior in `BoundConstantEvaluator`;
- removal of `ConcatenationExpressionSyntax`;
- removal of `BoundConcatenationExpression`;
- signed 64-bit edge-case hardening;
- string equality and string-escape hardening;
- typed expression runtime comparisons;
- passing Debug and Release tests.

If v0.4.1 is not complete:

1. stop this task;
2. do not partially add Python;
3. report that v0.4.1 must be completed first.

Do not assume any previously reviewed commit SHA is still current. Always use the newest `main`.

---

# 2. Milestone

Create:

> **SMILE v0.4.2 — Python Target**

Add Python as a first-class destination language.

SMILE will then generate nine target languages:

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
```

This is a destination-target expansion only.

Do **not** change SMILE source-language semantics.

Do **not** add:

- a new SMILE keyword;
- another SMILE type;
- reassignment;
- `SET`;
- `INPUT`;
- `IF`;
- loops;
- functions;
- arrays;
- floating-point values.

---

# 3. Explicitly deferred destination languages

The current project roadmap must explicitly state that these destination languages are being intentionally omitted for now:

```text
Rust
Zig
Go
```

They are not rejected forever. They are deliberately deferred so SMILE can focus on becoming a fuller programming language instead of continuously expanding target count.

## 3.1 Required documentation wording

Add a clearly visible section to `README.md` and `docs/Roadmap.md` with wording equivalent to:

> **Deferred destination languages:** Rust, Zig, and Go are intentionally not part of the active SMILE roadmap at this stage. After Python is added, target-language expansion is paused while SMILE focuses on runtime variables, assignment, input, conditions, loops, functions, and scopes. These targets may be reconsidered later when the runtime language model is mature.

## 3.2 AGENTS.md rule

Add a permanent project rule equivalent to:

> Rust, Zig, and Go are intentionally deferred destination languages. Do not add them to target metadata, generators, toolchains, active roadmap milestones, or desktop selectors unless Sin explicitly reactivates one of them.

## 3.3 Historical files

Do not delete old historical requirement or audit files merely because they mention Rust, Zig, or Go.

If a historical file is clearly labeled as an old proposal, preserve it.

The active sources of truth must be:

- `README.md`;
- `docs/Roadmap.md`;
- `AGENTS.md`.

If a current active requirements file still instructs Codex to add Rust, Zig, or Go, mark that instruction as:

```text
Superseded — intentionally deferred by Sin.
```

Do not leave contradictory active instructions.

---

# 4. Why Python is feasible now

The existing compiler already provides:

```text
Lexer
Parser
Typed syntax tree
Binder
Typed bound tree
SmileValue
BoundConstantEvaluator
SmileEvaluator
TargetIdentifierMap
CodeGeneratorRegistry
ToolchainRegistry
CLI
Desktop target selection
Evaluator-versus-toolchain tests
```

The Python generator must consume the existing `BoundProgram`.

It must not:

- reparse SMILE source;
- independently decide SMILE types;
- independently redefine operator semantics;
- implement source-text replacement;
- bypass `SmileEvaluator` as the semantic oracle.

---

# 5. Python target metadata

Append Python to `TargetLanguage` so existing enum ordering is not disturbed:

```csharp
Python
```

Add Python to `TargetLanguageInfo.All` after the existing targets.

Use:

```text
Stable ID:       python
Display name:    Python
Primary file:    Program.py
Action label:    Run
```

Update:

- stable-ID parsing;
- display names;
- primary filenames;
- target ordering;
- CLI help;
- supported-target lists;
- desktop selectors;
- tests.

`--target all` must include Python and generate all nine targets.

---

# 6. Supported Python version

Generate ordinary Python 3 source compatible with:

```text
Python 3.10 or newer
```

Do not require third-party packages.

Do not require or create:

```text
requirements.txt
pyproject.toml
setup.py
virtual environment
```

The generated program is one file:

```text
Program.py
```

Do not automatically install Python.

---

# 7. Shared SMILE semantics to preserve

The Python target must preserve:

- String;
- signed 64-bit Integer;
- Boolean;
- canonical Integer display;
- canonical `TRUE` / `FALSE` display;
- checked SMILE compile-time arithmetic;
- division truncated toward zero;
- case-sensitive string equality;
- left-to-right short-circuit `AND` and `OR`;
- unary `+`;
- unary `-`;
- `NOT`;
- arithmetic precedence;
- comparison precedence;
- equality precedence;
- parentheses;
- official string escapes;
- raw `PRINT` templates;
- interpolated quoted strings;
- literal braces;
- blank `PRINT`;
- `PRINT Name` as literal text;
- `PRINT {Name}` as evaluated output;
- target-safe identifiers;
- deterministic generated source.

Installed Python output must match `SmileEvaluator`.

---

# 8. Preferred generated Python structure

Use a conventional Python program shape:

```python
def _smile_text(value: object) -> str:
    if isinstance(value, bool):
        return "TRUE" if value else "FALSE"

    return str(value)


def _smile_div(left: int, right: int) -> int:
    quotient = abs(left) // abs(right)
    return -quotient if (left < 0) != (right < 0) else quotient


def main() -> None:
    Name = "Sin"
    Age = 49
    Adult = Age >= 18
    Message = f"Hello {Name}! Age={_smile_text(Age)}, Adult={_smile_text(Adult)}"

    print(Message)


if __name__ == "__main__":
    main()
```

Only emit a helper when the generated program needs it.

Examples:

- no Integer division means no `_smile_div`;
- no Integer or Boolean display conversion means `_smile_text` may be omitted;
- String-only programs should stay minimal.

Keep generated code:

- readable;
- idiomatic;
- deterministic;
- educational;
- dependency-free.

---

# 9. Python type mapping

```text
SMILE String  -> Python str
SMILE Integer -> Python int
SMILE Boolean -> Python bool
```

Python integers are not limited to signed 64-bit values, but SMILE semantics are.

The SMILE binder and constant evaluator must continue rejecting:

- out-of-range Integer literals;
- signed 64-bit overflow;
- division by zero;
- `-9223372036854775808 / -1`.

Do not weaken SMILE integer rules because Python supports larger integers.

---

# 10. Python Integer division

## 10.1 Semantic mismatch

Do not generate:

```python
left / right
```

because Python `/` produces floating-point output.

Do not directly generate:

```python
left // right
```

because Python floor division rounds negative results toward negative infinity.

SMILE requires truncation toward zero.

## 10.2 Required helper

Generate a helper equivalent to:

```python
def _smile_div(left: int, right: int) -> int:
    quotient = abs(left) // abs(right)
    return -quotient if (left < 0) != (right < 0) else quotient
```

Then:

```smile
LET Result = -7 / 2
```

becomes:

```python
Result = _smile_div(-7, 2)
```

and produces:

```text
-3
```

## 10.3 Required division tests

```smile
LET A = 7 / 2
LET B = -7 / 2
LET C = 7 / -2
LET D = -7 / -2

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
```

Expected:

```text
3
-3
-3
3
```

---

# 11. Python operator mapping

Map:

```text
+    -> +
-    -> -
*    -> *
/    -> _smile_div(left, right)

=    -> ==
<>   -> !=
<    -> <
<=   -> <=
>    -> >
>=   -> >=

NOT  -> not
AND  -> and
OR   -> or
```

Python `and` and `or` short-circuit.

Because SMILE permits only Boolean operands for `AND` and `OR`, Python's operand-return behavior remains observably Boolean.

---

# 12. Python precedence and parentheses

Python `not` has different precedence behavior from a conventional unary operator.

The Python expression writer must render from the bound tree and add parentheses whenever required.

It must not assume Python source precedence always matches SMILE precedence.

Verify at least:

```smile
LET A = 2 + 3 * 4
LET B = (2 + 3) * 4
LET C = 10 - (3 - 1)
LET D = NOT TRUE OR TRUE
LET E = TRUE OR FALSE AND FALSE
LET F = NOT (Age = 49)
```

Expected values:

```text
A = 14
B = 20
C = 8
D = TRUE
E = TRUE
```

Do not generate ambiguous chained comparisons that change the bound-tree meaning.

---

# 13. Python String generation

Use normal double-quoted Python String literals.

Escape at least:

```text
\       -> \\
"       -> \"
LF      -> \n
CR      -> \r
TAB     -> \t
NUL     -> \x00
BS      -> \x08
FF      -> \x0c
```

Escape other control characters safely.

Preserve UTF-8/Unicode source text.

## 13.1 String concatenation

Generate normal Python concatenation:

```smile
LET FullName = FirstName + " " + LastName
```

```python
FullName = FirstName + " " + LastName
```

## 13.2 Interpolation

Use Python f-strings.

Example:

```smile
LET Message = $"{Name} is {Age}. Adult={Adult}"
```

Preferred:

```python
Message = f"{Name} is {_smile_text(Age)}. Adult={_smile_text(Adult)}"
```

Rules:

- String holes may be inserted naturally.
- Integer holes must use SMILE canonical display.
- Boolean holes must use `TRUE` / `FALSE`.
- literal `{` and `}` in f-string text must be doubled;
- backslashes and quotation marks must be escaped correctly.

---

# 14. Python Boolean display

Python normally displays:

```text
True
False
```

SMILE requires:

```text
TRUE
FALSE
```

Use a small generated helper equivalent to:

```python
def _smile_text(value: object) -> str:
    if isinstance(value, bool):
        return "TRUE" if value else "FALSE"

    return str(value)
```

Use it where an evaluated value becomes text:

- direct `PRINT`;
- raw-template holes;
- interpolated quoted strings.

String values may be inserted directly when that keeps code natural.

---

# 15. Python PRINT generation

Preferred shapes:

## Blank PRINT

```smile
PRINT
```

```python
print()
```

## String

```smile
PRINT {Name}
```

```python
print(Name)
```

## Integer

```smile
PRINT {Age}
```

```python
print(_smile_text(Age))
```

## Boolean

```smile
PRINT {Adult}
```

```python
print(_smile_text(Adult))
```

## Raw template

```smile
PRINT Result: {Age + 1}
```

```python
print(f"Result: {_smile_text(Age + 1)}")
```

## Quoted literal

```smile
PRINT "Hello"
```

```python
print("Hello")
```

Preserve:

```smile
PRINT Name
```

as literal text:

```python
print("Name")
```

Do not reinterpret bare `PRINT` text as a variable.

---

# 16. Python identifier mapping

Add Python target restrictions to `TargetIdentifierMap`.

Include Python keywords and relevant soft keywords for the supported version range, including at least:

```text
False
None
True
and
as
assert
async
await
break
case
class
continue
def
del
elif
else
except
finally
for
from
global
if
import
in
is
lambda
match
nonlocal
not
or
pass
raise
return
try
while
with
yield
```

Protect generated or runtime names that could break generated code:

```text
print
str
bool
int
abs
isinstance
main
_smile_text
_smile_div
__name__
```

A single `_` may remain a valid Python variable, but mapping it is acceptable if the target-wide naming policy prefers readable, non-discard-looking variables.

Preserve original spelling when safe.

Mapping must remain:

- symbol-based;
- deterministic;
- collision-safe;
- target-specific;
- consistent across declarations and references.

## 16.1 Identifier tests

Use valid SMILE names such as:

```smile
LET class = "class"
LET match = "match"
LET main = "main"
LET str = "str"
LET isinstance = "isinstance"
LET _smile_text = "_smile_text"
LET _smile_div = "_smile_div"

PRINT {class}
PRINT {match}
PRINT {main}
PRINT {str}
PRINT {isinstance}
PRINT {_smile_text}
PRINT {_smile_div}
```

Remember:

```smile
LET print = "print"
```

is invalid SMILE because `PRINT` is a case-insensitive SMILE keyword.

Do not weaken the SMILE keyword rule to test Python mapping.

---

# 17. Python code generator

Add:

```text
PythonCodeGenerator
```

to `CodeGeneratorRegistry`.

The generator must:

- consume `BoundProgram`;
- use `TargetIdentifierMap`;
- use shared bound operator kinds;
- preserve expression intent;
- use `_smile_div` only for Integer division;
- use canonical text conversion;
- produce `Program.py`;
- ensure one trailing newline;
- generate deterministic source;
- avoid unnecessary helpers.

Do not implement a Python AST framework.

A focused precedence-aware Python expression writer is preferred.

---

# 18. Python toolchain

Add:

```text
PythonToolchain
```

to `ToolchainRegistry.CreateDefault()`.

## 18.1 Detection

Detect an already-installed Python 3 interpreter.

Try practical commands such as:

```text
python --version
py -3 --version
py --version
```

Requirements:

- accept Python 3.10 or newer;
- reject Python 2;
- report detected version;
- report the selected command or executable;
- avoid triggering an on-demand Store installation;
- do not modify PATH;
- do not download or install Python.

## 18.2 Run command

Run:

```text
Program.py
```

with the selected interpreter.

Prefer disabling bytecode output, for example:

```text
python -B Program.py
```

or the equivalent Python launcher command.

The desktop action label is:

```text
Run
```

Use existing:

- temporary workspace;
- program timeout;
- cancellation;
- bounded stdout/stderr;
- exit-code reporting;
- pause launcher;
- failure containment.

Python is interpreted, so no separate compilation phase is needed.

---

# 19. Syntax highlighting

Add Python highlighting under stable ID:

```text
python
```

Use AvalonEdit's built-in Python definition only if:

- it exists;
- it loads reliably;
- tests verify it.

Otherwise add:

```text
src/SMILE.Desktop/Highlighting/Python.xshd
```

Highlight:

- keywords;
- strings;
- comments;
- numbers;
- Boolean and `None` literals;
- function declarations where practical.

Keep regexes simple.

Add tests that:

- the Python definition loads;
- target switching resolves Python highlighting;
- switching remains fast;
- rapid Python → C → Swift → Python changes do not block the UI.

---

# 20. Desktop integration

Python must:

- appear in every generated-pane target selector;
- use correct syntax highlighting;
- use cached generation for the current source revision;
- avoid unnecessary all-target regeneration;
- support Copy;
- support Save Source;
- support Open Generated Folder;
- support Run;
- support Press Any Key launcher;
- display missing-interpreter messages;
- remain cancellable;
- remain timeout-protected;
- never close the IDE on recoverable failure.

The Python action button should say:

```text
Run
```

Do not hardcode JavaScript as the only run-only target.

Generalize the action-label rule minimally so both JavaScript and Python use `Run`.

Do not unexpectedly change the default target selections in the three generated panes unless required.

---

# 21. CLI integration

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
all
```

Example:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target python --run
```

`--target all` must generate all nine targets.

Invalid-target errors and help text must list `python`.

---

# 22. Generator tests

Add exact or strong structural tests verifying:

- `Program.py`;
- `main()` function;
- main guard;
- String, Integer, and Boolean declarations;
- `_smile_text` only when needed;
- `_smile_div` only when needed;
- negative Integer division;
- f-string interpolation;
- literal brace escaping;
- canonical Boolean output;
- String concatenation;
- String equality;
- short-circuit operators;
- target identifier mapping;
- precedence and parentheses;
- deterministic generation;
- no third-party imports.

Generate the same source twice and compare exact output.

---

# 23. Python conformance programs

Run Python against `SmileEvaluator` using at least:

## Existing PRINT

```text
examples/FriendlyPrint.smile
```

## Complete LET

```text
examples/CompleteLetV1.smile
```

## Typed expressions

```text
examples/TypedExpressionCore.smile
```

## Empty strings

```text
examples/LetEmptyStringHardening.smile
```

## Identifier hardening

```text
examples/LetIdentifierHardening.smile
```

## Short-circuit behavior

```smile
LET A = FALSE AND (1 / 0 = 0)
LET B = TRUE OR (1 / 0 = 0)

PRINT {A}
PRINT {B}
```

Expected:

```text
FALSE
TRUE
```

## Negative division

```smile
LET A = 7 / 2
LET B = -7 / 2
LET C = 7 / -2
LET D = -7 / -2

PRINT {A}
PRINT {B}
PRINT {C}
PRINT {D}
```

Expected:

```text
3
-3
-3
3
```

## String equality

```smile
LET A = "Sin" = "Sin"
LET B = "Sin" = "sin"
LET C = "Sin" <> "sin"

PRINT {A}
PRINT {B}
PRINT {C}
```

Expected:

```text
TRUE
FALSE
TRUE
```

---

# 24. Runtime integration tests

When Python is installed:

1. evaluate source with `SmileEvaluator`;
2. generate `Program.py`;
3. run Python;
4. normalize line endings only;
5. compare stdout exactly;
6. verify exit code zero;
7. verify no unexpected data is discarded.

Do not normalize:

- Boolean case;
- spaces;
- tabs;
- NUL characters;
- backspaces;
- form feeds;
- Integer text.

A missing Python interpreter may make an automated test inconclusive, but the completion report must clearly state whether Python was actually executed.

Do not claim local Python support based only on transpilation.

---

# 25. Documentation

Update:

- `README.md`;
- `AGENTS.md`;
- `docs/Architecture.md`;
- `docs/Roadmap.md`;
- target code generation standard;
- local toolchain documentation;
- CLI examples;
- desktop version/About metadata;
- requirements/history file used by the project.

## 25.1 Version

Update to:

```text
SMILE v0.4.2 — Python Target
```

## 25.2 Supported-target table

Add:

| Stable ID | Display name | File | Toolchain |
|---|---|---|---|
| `python` | Python | `Program.py` | Python 3.10+ |

## 25.3 Architecture

Document:

- Python target metadata;
- Python truncation-toward-zero division helper;
- canonical Boolean display;
- f-string interpolation;
- Python identifier mapping;
- Python toolchain detection;
- nine-target evaluator conformance.

## 25.4 Roadmap

Record Python as the final target expansion before the planned target-language freeze.

The active development direction after Python should prioritize:

```text
Runtime variables
Assignment
INPUT
IF / THEN / ELSE
Loops
Functions
Scopes
```

Explicitly document that Rust, Zig, and Go are deferred.

---

# 26. Scope exclusions

Do not:

- add Rust;
- add Zig;
- add Go;
- add another destination language;
- implement assignment;
- add mutable runtime variables;
- add `SET`;
- add `INPUT`;
- add `IF`;
- add loops;
- add functions;
- add arrays;
- add floating-point support;
- install Python without permission;
- add Python packages;
- add a SMILE runtime library;
- add a feature branch.

---

# 27. Acceptance criteria

This task is complete only when all of the following are true:

1. Python is a `TargetLanguage`.
2. existing enum values retain their order.
3. the stable ID is `python`.
4. the display name is `Python`.
5. the primary file is `Program.py`.
6. `--target all` generates nine targets.
7. Python consumes `BoundProgram`.
8. Python does not reparse SMILE.
9. String maps to Python `str`.
10. Integer maps to Python `int`.
11. Boolean maps to Python `bool`.
12. Python division truncates toward zero.
13. Python does not use `/` for SMILE Integer division.
14. Python does not directly use `//` when negative rounding would differ.
15. Python Boolean output is `TRUE` / `FALSE`.
16. Python `AND` / `OR` short-circuit.
17. Python string equality is case-sensitive.
18. Python precedence preserves the bound tree.
19. f-string literal braces are escaped.
20. all official string escapes are valid in generated Python.
21. Python identifiers are safely mapped.
22. mapping is deterministic and collision-safe.
23. helper names cannot collide with user variables.
24. `_smile_text` is emitted only when needed.
25. `_smile_div` is emitted only when needed.
26. Python highlighting loads.
27. Python appears in desktop selectors.
28. Python action label says `Run`.
29. Python supports Copy and Save Source.
30. Python supports Open Generated Folder.
31. Python supports a pause launcher.
32. missing Python does not close SMILE.
33. installed Python runs generated programs successfully.
34. Python output matches `SmileEvaluator`.
35. all existing eight targets remain unchanged.
36. existing `LET` behavior remains unchanged.
37. existing `PRINT` behavior remains unchanged.
38. short-circuit behavior remains unchanged.
39. signed 64-bit behavior remains unchanged.
40. official string escapes remain unchanged.
41. README explicitly defers Rust, Zig, and Go.
42. Roadmap explicitly defers Rust, Zig, and Go.
43. AGENTS prevents accidental Rust, Zig, or Go implementation.
44. active requirements contain no contradictory instruction to add Rust, Zig, or Go.
45. Debug build has zero warnings.
46. Release build has zero warnings.
47. Debug tests pass.
48. Release tests pass.
49. documentation matches implementation.
50. no unapproved software was installed.
51. no unrelated files or build artifacts are committed.
52. all work is performed directly on `main`.

---

# 28. Validation commands

Run from the repository root:

```bat
cmd /c git status --short --branch
```

Confirm:

```text
main
```

Run:

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

Generate all nine targets:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target all
```

Run Python when installed:

```bat
cmd /c dotnet run --project src\SMILE.Cli -- examples\TypedExpressionCore.smile --target python --run
```

Also run the negative-division, short-circuit, string-equality, escape, and identifier programs through Python.

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

# 29. Manual desktop validation

1. Launch SMILE Desktop.
2. Select Python in one generated pane.
3. Verify Python syntax highlighting.
4. Rapidly switch Python → C → Swift → Python.
5. Confirm the UI remains responsive.
6. Run Python.
7. Compare output with the SMILE evaluator expectation.
8. Test a missing-Python path if practical.
9. Confirm the IDE remains open.
10. Test cancellation.
11. Test Press Any Key launcher.
12. Inspect generated Python for readability.
13. Verify negative division.
14. Verify Boolean output is uppercase.
15. Verify literal braces in f-strings.
16. Verify `PRINT Name` remains literal.
17. Verify `PRINT {Name}` evaluates the variable.

---

# 30. Suggested implementation sequence

1. Confirm v0.4.1 is complete.
2. Update active documentation to defer Rust, Zig, and Go.
3. Add Python target metadata and tests.
4. Add Python identifier rules and tests.
5. Implement Python expression generation.
6. Implement Python String and f-string escaping.
7. Implement `_smile_text`.
8. Implement `_smile_div`.
9. Implement `PythonCodeGenerator`.
10. Implement `PythonToolchain`.
11. Add Python syntax highlighting.
12. Update desktop selectors and Run labeling.
13. Update CLI.
14. Add evaluator-versus-Python tests.
15. Run Debug and Release validation.
16. Run Python explicitly when installed.
17. Perform desktop smoke testing.
18. Update all documentation.
19. Commit directly to `main` only when Sin authorizes it.

---

# 31. Completion report

Report:

- prerequisite v0.4.1 commit verified;
- exact files changed;
- Python target metadata;
- Python generator design;
- Python division strategy;
- canonical Boolean display strategy;
- f-string escaping;
- identifier mapping additions;
- Python highlighting;
- desktop changes;
- CLI changes;
- Python toolchain detection result;
- exact Debug test total;
- exact Release test total;
- zero-warning build results;
- generation result for all nine targets;
- Python run result;
- evaluator-versus-Python comparisons;
- manual desktop validation;
- README/Roadmap/AGENTS deferred-target wording;
- any active requirement marked superseded;
- confirmation that Rust, Zig, and Go were not added;
- unresolved concerns.

Do not state that Python is locally supported if it was only transpiled and its interpreter was not actually executed.

---

# 32. Commit guidance

Do not commit or push unless Sin explicitly authorizes it.

When authorized, commit directly to `main`.

Suggested subject:

```text
Sin and Codex: Add Python target
```

The commit body should mention:

- first-class Python generator;
- exact truncation-toward-zero Integer division;
- canonical Boolean display;
- Python identifier mapping;
- local Python Run support;
- syntax highlighting;
- nine-target evaluator conformance;
- deliberate deferral of Rust, Zig, and Go;
- exact validation results.
